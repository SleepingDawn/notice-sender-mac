import Foundation
import KmsgSafeCore

enum SelfTest {
    @MainActor
    static func run() -> Int32 {
        var failures: [String] = []
        check("직접입력 기본 Preset", failures: &failures) {
            DefaultPresets.all.first?.kind == .direct
                && DefaultPresets.direct.name == "직접입력"
                && DefaultPresets.direct.presentTemplate == "{{notice}}"
        }
        check("직접입력 바깥 큰따옴표 한 쌍 제거", failures: &failures) {
            DirectNoticeMessage.normalized("\"첫 줄\n둘째 줄\"") == "첫 줄\n둘째 줄"
                && DirectNoticeMessage.normalized("\"앞만") == "\"앞만"
                && DirectNoticeMessage.normalized("뒤만\"") == "뒤만\""
                && DirectNoticeMessage.normalized(" \"공백 보존\" ") == " \"공백 보존\" "
        }
        check("테스트 반 직접입력 기본 마이그레이션", failures: &failures) {
            let root = FileManager.default.temporaryDirectory.appendingPathComponent("notice-sender-direct-migration-\(UUID().uuidString)", isDirectory: true)
            let file = root.appendingPathComponent("database.json")
            try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: root) }
            let student = Student(name: "테스트학생", nickname: "학생이", school: "테스트학교", admissionYear: 26, chatRoomName: "테스트방")
            let group = ClassGroup(name: "테스트", school: student.school, admissionYear: student.admissionYear, members: [ClassMember(studentID: student.id)])
            let oldDatabase = AppDatabase(schemaVersion: 4, students: [student], classes: [group], presets: [DefaultPresets.regular], logs: [])
            let encoder = JSONEncoder(); encoder.dateEncodingStrategy = .iso8601
            try encoder.encode(oldDatabase).write(to: file)
            let migrated = AppStore(databaseURL: file).database
            guard let direct = migrated.presets.first(where: { $0.kind == .direct }), let testClass = migrated.classes.first(where: { $0.name == "테스트" }) else { return false }
            return migrated.schemaVersion == 6
                && testClass.defaultPresetID == direct.id
                && migrated.operatingAdmissionYears == OperatingAdmissionYearPolicy.defaultYears()
        }
        check("운영 학번 2자리 기본값·자유 설정", failures: &failures) {
            var calendar = Calendar(identifier: .gregorian)
            calendar.timeZone = TimeZone(secondsFromGMT: 0)!
            let january = calendar.date(from: DateComponents(year: 2027, month: 1, day: 10))!
            let march = calendar.date(from: DateComponents(year: 2027, month: 3, day: 10))!
            return OperatingAdmissionYearPolicy.defaultYears(for: january, calendar: calendar) == [24, 25, 26]
                && OperatingAdmissionYearPolicy.defaultYears(for: march, calendar: calendar) == [25, 26, 27]
                && OperatingAdmissionYearPolicy.normalized([27, 25, 27, -1, 100]) == [25, 27]
        }
        check("테스트 반 학생별 직접 문구 발송 목록", failures: &failures) {
            let students = (1...5).map { index in
                Student(name: "테스트학생\(index)", nickname: "학생\(index)", school: "테스트학교", admissionYear: 26, chatRoomName: "테스트방\(index)")
            }
            let group = ClassGroup(name: "테스트", school: "테스트학교", admissionYear: 26, members: students.map { ClassMember(studentID: $0.id) }, defaultPresetID: DefaultPresets.direct.id)
            let items = students.enumerated().map { index, student in
                let entered = "\"\(student.name) 전용 공지 \(index + 1)\""
                return BatchItem(studentID: student.id, studentName: student.name, nickname: student.nickname, chatRoomName: student.chatRoomName, message: DirectNoticeMessage.normalized(entered), preserveMessageWhitespace: true)
            }
            let metadata = BatchMetadata(schemaVersion: 1, classID: group.id, sessionID: UUID(), date: "7월 31일", presetID: DefaultPresets.direct.id, presetVersion: 1)
            let issues = BatchParser.validate(batch: SendBatch(metadata: metadata, items: items), database: AppDatabase(students: students, classes: [group]))
            return items.map(\.message) == (1...5).map { "테스트학생\($0) 전용 공지 \($0)" }
                && !issues.contains { $0.severity == .error }
        }
        check("별점·미제출 규칙", failures: &failures) {
            let message = try TemplateEngine.render(preset: DefaultPresets.regular, input: sampleInput(homework: 8, homeworkMax: 10, test: 5, testMax: 10))
            return message.contains("과제 점수 : ★★★") && message.contains("테스트 : ★★☆")
        }
        check("직접입력 공백·줄바꿈 보존", failures: &failures) {
            let student = Student(name: "테스트학생", nickname: "학생이", school: "테스트", admissionYear: 26, chatRoomName: "테스트방")
            let entered = "\"  첫 줄\n둘째 줄  \""
            let item = BatchItem(studentID: student.id, studentName: student.name, nickname: student.nickname, chatRoomName: student.chatRoomName, message: DirectNoticeMessage.normalized(entered), preserveMessageWhitespace: true)
            return item.allMessages == ["  첫 줄\n둘째 줄  "]
        }
        check("드라이런·실제 발송 재확인 상태 규칙", failures: &failures) {
            BatchRunPolicy.shouldProcess(status: .sent, dryRun: true)
                && !BatchRunPolicy.shouldProcess(status: .sent, dryRun: false)
                && BatchRunPolicy.shouldProcess(status: .verified, dryRun: false)
                && BatchRunPolicy.statusAfterSuccess(previousStatus: .sent, dryRun: true) == .sent
                && BatchRunPolicy.statusAfterSuccess(previousStatus: .verified, dryRun: false) == .sent
                && BatchRunPolicy.statusAfterFailure(previousStatus: .sent, dryRun: true) == .sent
                && BatchRunPolicy.statusAfterFailure(previousStatus: .verified, dryRun: true) == .failed
        }
        check("모의고사 3개", failures: &failures) {
            var input = sampleInput(homework: nil, homeworkMax: nil, test: nil, testMax: nil)
            input.exams = (1...3).map { ExamInput(title: "모의고사 \($0)회", score: Double(80 + $0), maximum: 100, average: 75, highest: 95, rank: $0, attendees: 10, comment: "코멘트") }
            let message = try TemplateEngine.render(preset: DefaultPresets.mock, input: input)
            return message.contains("모의고사 3회") && message.contains("학생 등수 : 3등")
        }
        check("안전 클립보드 왕복", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let preset = DefaultPresets.regular
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id, nicknameOverride: "길동이")])
            let template = SheetTemplateBuilder.build(group: group, students: [student], preset: preset)
            var lines = template.tsv.components(separatedBy: "\n")
            var top = lines[0].components(separatedBy: "\t"); top[9] = "7월 12일"; lines[0] = top.joined(separator: "\t")
            if let dateLine = lines.firstIndex(where: { $0.components(separatedBy: "\t").contains("date") }) {
                var dateCells = lines[dateLine].components(separatedBy: "\t")
                if let dateColumn = dateCells.firstIndex(of: "date"), dateCells.indices.contains(dateColumn + 1) { dateCells[dateColumn + 1] = "7월 12일" }
                lines[dateLine] = dateCells.joined(separator: "\t")
            }
            guard let header = lines.firstIndex(where: { $0.contains("student_id") && $0.contains("공지 멘트") }) else { return false }
            var cells = lines[header + 1].components(separatedBy: "\t")
            if cells[10].isEmpty { // maximum-count row sits between the header and student rows
                cells = lines[header + 2].components(separatedBy: "\t")
                cells[9] = "길동이의 안내"
                lines[header + 2] = cells.joined(separator: "\t")
            }
            let db = AppDatabase(students: [student], classes: [group], presets: [preset], logs: [])
            let result = BatchParser.parse(text: lines.joined(separator: "\n"), database: db, selectedClassID: nil)
            return result.batch?.items.count == 1 && !result.issues.contains { $0.severity == .error }
        }
        check("수식 오류 차단", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let metadata = BatchMetadata(schemaVersion: 1, classID: UUID(), sessionID: UUID(), date: "", presetID: DefaultPresets.regular.id, presetVersion: 1)
            let batch = SendBatch(metadata: metadata, items: [BatchItem(studentID: student.id, studentName: student.name, nickname: student.nickname, chatRoomName: student.chatRoomName, message: "#DIV/0!")])
            let db = AppDatabase(students: [student], classes: [], presets: [DefaultPresets.regular], logs: [])
            return BatchParser.validate(batch: batch, database: db).contains { $0.message.contains("수식 오류") }
        }
        check("기존 형식 호칭 불일치 차단", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id, nicknameOverride: "길동이")])
            let text = "회차\t\t\t\t\t날짜\t7월 12일\n\t태도\t숙제\t테스트\t숙제 코멘트\t테스트 코멘트\t공지 멘트\n\t\t10\t10\t\t\t\n출석\t3\t10\t10\t\t\t다른학생의 안내"
            let db = AppDatabase(students: [student], classes: [group], presets: DefaultPresets.all, logs: [])
            return BatchParser.parse(text: text, database: db, selectedClassID: group.id).issues.contains { $0.severity == .error && $0.message.contains("호칭") }
        }
        check("여러 줄 TSV 셀 보존", failures: &failures) {
            let rows = BatchParser.parseTSV("이름\t공지 멘트\n홍길동\t\"첫 줄\n둘째 \"\"인용\"\" 줄\"")
            return rows.count == 2 && rows[1].count == 2 && rows[1][1] == "첫 줄\n둘째 \"인용\" 줄"
        }
        check("일반 CSV 학생 헤더 가져오기", failures: &failures) {
            let csv = "이름,학교,학번,톡방 이름,chat_id,호칭\n홍길동,한성,25,\"25. 한성 홍길동 화학방\",chat-recent-1,길동이\n김학생,세종,2026,세종26 김학생 방,,학생이"
            let rows = StudentFileImporter.parseDelimited(csv, delimiter: ",")
            let students = try StudentFileImporter.students(from: rows)
            return students.count == 2
                && students.first(where: { $0.name == "홍길동" })?.nickname == "길동이"
                && students.first(where: { $0.name == "홍길동" })?.chatID == "chat-recent-1"
                && students.first(where: { $0.name == "김학생" })?.admissionYear == 26
        }
        check("학생 DB CSV 전체 필드 왕복", failures: &failures) {
            let original = Student(
                id: UUID(uuidString: "6A28B859-1E25-4C14-8F15-A9F0D91DE671")!,
                name: "김\"학생",
                nickname: "별칭,학생이",
                school: "한성",
                admissionYear: 7,
                chatRoomName: "07. 한성 김학생\n화학방",
                chatID: "chat-id-1",
                isActive: false
            )
            guard var text = String(data: StudentDatabaseCSV.data(students: [original]), encoding: .utf8) else { return false }
            if text.first == "\u{feff}" { text.removeFirst() }
            let rows = StudentFileImporter.parseDelimited(text, delimiter: ",")
            let records = try StudentFileImporter.records(from: rows)
            guard let record = records.first else { return false }
            return record.student == original
                && record.sourceID == original.id
                && record.nicknameProvided
                && record.chatIDProvided
                && record.statusProvided
        }
        check("학교별 Academy 양식 적용", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id)])
            var preset = DefaultPresets.regular
            preset.academyName = "SNT 한성과고"
            let template = SheetTemplateBuilder.build(group: group, students: [student], preset: preset)
            return template.tsv.contains("SNT 한성과고")
        }
        check("시험만 Preset 숙제검사 생략 문구", failures: &failures) {
            let message = try TemplateEngine.render(preset: DefaultPresets.testOnly, input: sampleInput(homework: nil, homeworkMax: nil, test: 8, testMax: 10))
            return message.components(separatedBy: "오늘은 진도상 숙제검사를 생략했습니다.").count >= 3 && message.contains("학생 점수")
        }
        check("숙제만 Preset 시험범위 대체", failures: &failures) {
            let message = try TemplateEngine.render(preset: DefaultPresets.homeworkOnly, input: sampleInput(homework: 8, homeworkMax: 10, test: nil, testMax: nil))
            return message.contains("④ 시험범위") && message.contains("테스트 : 시험 단원") && !message.contains("시험 미응시")
        }
        check("기존 셀형 값 붙여넣기 양식", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id)])
            let tsv = SheetTemplateBuilder.build(group: group, students: [student], preset: DefaultPresets.regular).tsv
            return tsv.contains("번호\t이름\t호칭\t출석\t태도\t숙제(개수 입력)\t테스트(개수 입력)\t숙제 코멘트\t테스트 코멘트\t공지 멘트\tstudent_id") && tsv.contains("선생님 입력")
        }
        check("Google Sheets 양식 성명 오름차순", failures: &failures) {
            let studentB = Student(name: "박하연", nickname: "하연이", school: "한성", admissionYear: 25, chatRoomName: "B방")
            let studentA = Student(name: "김길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "A방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: studentB.id), ClassMember(studentID: studentA.id)])
            let rows = BatchParser.parseTSV(SheetTemplateBuilder.build(group: group, students: [studentB, studentA], preset: DefaultPresets.regular).tsv)
            guard let header = rows.firstIndex(where: { $0.contains("student_id") }), rows.indices.contains(header + 2), rows.indices.contains(header + 3) else { return false }
            return rows[header + 2][1] == "김길동" && rows[header + 3][1] == "박하연"
        }
        check("새 반 학교·학번 학생 필터", failures: &failures) {
            let targetB = Student(name: "박하연", nickname: "하연이", school: "한성", admissionYear: 25, chatRoomName: "B방")
            let targetA = Student(name: "김길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "A방")
            let wrongSchool = Student(name: "김세종", nickname: "세종이", school: "세종", admissionYear: 25, chatRoomName: "C방")
            let wrongYear = Student(name: "김후배", nickname: "후배", school: "한성", admissionYear: 26, chatRoomName: "D방")
            let inactive = Student(name: "김비활성", nickname: "비활성이", school: "한성", admissionYear: 25, chatRoomName: "E방", isActive: false)
            let students = [targetB, wrongSchool, inactive, wrongYear, targetA]
            let filtered = ClassStudentFilter.students(in: students, school: " 한성 ", admissionYear: 25)
            return filtered.map(\.name) == ["김길동", "박하연"]
                && ClassStudentFilter.students(in: students, school: "", admissionYear: 25).isEmpty
                && ClassStudentFilter.students(in: students, school: "한성", admissionYear: nil).isEmpty
        }
        check("반 명단 성명순 정렬", failures: &failures) {
            let studentC = Student(name: "이민준", nickname: "민준이", school: "한성", admissionYear: 25, chatRoomName: "C방")
            let studentA = Student(name: "김길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "A방")
            let studentB = Student(name: "박하연", nickname: "하연이", school: "한성", admissionYear: 25, chatRoomName: "B방")
            let unsorted = [studentC, studentA, studentB].map { ClassMember(studentID: $0.id) }
            let sorted = ClassMemberSorter.sorted(unsorted, students: [studentC, studentA, studentB])
            let namesByID = Dictionary(uniqueKeysWithValues: [studentC, studentA, studentB].map { ($0.id, $0.name) })
            return sorted.compactMap { namesByID[$0.studentID] } == ["김길동", "박하연", "이민준"]
        }
        check("스프레드시트 드래그 범위 삭제", failures: &failures) {
            let first = PreparedNoticeRow(id: UUID(), number: 1, name: "김길동", nickname: "길동이", homework: "8", test: "9")
            let second = PreparedNoticeRow(id: UUID(), number: 2, name: "박하연", nickname: "하연이", homework: "7", test: "6")
            let changes = SpreadsheetCanvas.deletionChanges(anchor: .init(row: 2, column: 6), cursor: .init(row: 3, column: 7), studentCount: 2) { row, column in
                if row == 2 { return column == 6 ? first.homework : first.test }
                return column == 6 ? second.homework : second.test
            }
            let result = PerformanceSpreadsheet.applying(changes, to: [first, second], homeworkMaximum: "10", testMaximum: "10")
            return changes.count == 4 && result.rows.allSatisfy { $0.homework.isEmpty && $0.test.isEmpty } && result.rows.map(\.name) == ["김길동", "박하연"]
        }
        check("스프레드시트 개별 셀·총개수 수정", failures: &failures) {
            let row = PreparedNoticeRow(id: UUID(), number: 1, name: "김길동", nickname: "길동이")
            let changes = [
                SpreadsheetCanvas.CellChange(row: 2, column: 1, value: "false"),
                SpreadsheetCanvas.CellChange(row: 2, column: 8, value: "숙제 수정"),
                SpreadsheetCanvas.CellChange(row: 2, column: 10, value: "\"학생별 공지\""),
                SpreadsheetCanvas.CellChange(row: 1, column: 6, value: "20")
            ]
            let result = PerformanceSpreadsheet.applying(changes, to: [row], homeworkMaximum: "10", testMaximum: "10")
            return result.rows[0].isIncluded == false
                && result.rows[0].homeworkComment == "숙제 수정"
                && result.rows[0].noticeMessage == "\"학생별 공지\""
                && DirectNoticeMessage.normalized(result.rows[0].noticeMessage) == "학생별 공지"
                && result.homeworkMaximum == "20"
                && result.testMaximum == "10"
        }
        check("학생 발송 선택 기본값·제외 문구 검증", failures: &failures) {
            let included = PreparedNoticeRow(id: UUID(), number: 1, name: "김길동", nickname: "길동이", noticeMessage: "\"선택 공지\"")
            var excluded = PreparedNoticeRow(id: UUID(), number: 2, name: "박하연", nickname: "하연이")
            excluded.isIncluded = false
            let rows = [included, excluded]
            return included.isIncluded
                && PreparedNoticeSelection.includedRows(rows).map(\.id) == [included.id]
                && PreparedNoticeSelection.directMessagesAreReady(in: rows)
                && !PreparedNoticeSelection.directMessagesAreReady(in: [excluded])
        }
        check("스프레드시트 셀 한글 다중 입력·Backspace", failures: &failures) {
            let editor = SpreadsheetTextView(frame: NSRect(x: 0, y: 0, width: 180, height: SpreadsheetCanvas.rowHeight))
            editor.insertText("가나다라", replacementRange: NSRange(location: 0, length: 0))
            editor.deleteBackward(nil)
            editor.deleteBackward(nil)
            return editor.string == "가나"
        }
        check("모의고사 개수별 양식", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id)])
            var preset = DefaultPresets.mock; preset.mockExamCount = 1
            let tsv = SheetTemplateBuilder.build(group: group, students: [student], preset: preset).tsv
            return tsv.contains("모의고사1 점수") && !tsv.contains("모의고사2 점수")
        }
        check("Google Sheets 공식의 선생님 입력행 참조", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let group = ClassGroup(name: "한성25", school: "한성", admissionYear: 25, members: [ClassMember(studentID: student.id)])
            let lines = SheetTemplateBuilder.build(group: group, students: [student], preset: DefaultPresets.regular).tsv.components(separatedBy: "\n")
            guard let header = lines.firstIndex(where: { $0.contains("student_id") && $0.contains("공지 멘트") }) else { return false }
            let formula = lines[header + 2]
            return formula.contains("$E$7") && formula.contains("$E$10") && formula.contains("$F$3") && formula.contains("$G$3")
        }
        check("새 수업 초기 상태", failures: &failures) {
            let first = LessonPlan(classID: UUID(), presetID: UUID(), progress: "기존 진도", assignment: "기존 숙제", examUnit: "기존 시험", notice: "기존 공지")
            let fresh = LessonPlan(classID: first.classID, presetID: first.presetID)
            return fresh.id != first.id && fresh.progress.isEmpty && fresh.assignment.isEmpty && fresh.examUnit.isEmpty && fresh.notice.isEmpty
        }
        check("받침·알파벳 호칭 규칙", failures: &failures) {
            NicknameGenerator.generate(from: "김영준A") == "영준이" &&
            NicknameGenerator.generate(from: "구승모B") == "승모" &&
            NicknameGenerator.generate(from: "박하연") == "하연이"
        }
        check("수업 관리 저장 데이터 왕복", failures: &failures) {
            let plan = LessonPlan(classID: UUID(), presetID: UUID(), progress: "진도", assignment: "숙제", examUnit: "시험", notice: "공지")
            let database = AppDatabase(lessonPlans: [plan])
            let data = try JSONEncoder().encode(database)
            let restored = try JSONDecoder().decode(AppDatabase.self, from: data)
            return restored.lessonPlans?.first?.progress == "진도" && restored.lessonPlans?.first?.notice == "공지"
        }
        check("Preset 선택·편집 상태 전환", failures: &failures) {
            let selected = DefaultPresets.regular
            var editableDraft = selected
            editableDraft.name = "일반 수업 수정본"
            editableDraft.presentTemplate += "\n{{notice}}"
            try TemplateEngine.validate(editableDraft)
            let message = try TemplateEngine.render(preset: editableDraft, input: sampleInput(homework: 8, homeworkMax: 10, test: 8, testMax: 10))
            return editableDraft.id == selected.id && editableDraft.name == "일반 수업 수정본" && message.contains("공지")
        }
        check("첨부파일 하위 폴더·정확한 성명 매칭", failures: &failures) {
            let root = FileManager.default.temporaryDirectory.appendingPathComponent("notice-sender-attachment-\(UUID().uuidString)", isDirectory: true)
            let child = root.appendingPathComponent("하위", isDirectory: true)
            try FileManager.default.createDirectory(at: child, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: root) }
            let student = Student(name: "김영준A", nickname: "영준이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            try Data("a".utf8).write(to: root.appendingPathComponent("한성25김영준A_성적표.pdf"))
            try Data("b".utf8).write(to: child.appendingPathComponent("2025_한성_김영준A_해설.txt"))
            try Data("c".utf8).write(to: child.appendingPathComponent("한성25김영준_다른학생.pdf"))
            let result = try AttachmentScanner.scan(rootPath: root.path, students: [student])
            return result.scannedFileCount == 3 && result.filesByStudentID[student.id]?.count == 2
        }
        check("일부 학생에게만 첨부파일 할당", failures: &failures) {
            let root = FileManager.default.temporaryDirectory.appendingPathComponent("notice-sender-partial-attachment-\(UUID().uuidString)", isDirectory: true)
            try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: root) }
            let students = [
                Student(name: "김길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "길동방"),
                Student(name: "박하연", nickname: "하연이", school: "한성", admissionYear: 25, chatRoomName: "하연방"),
                Student(name: "이민준", nickname: "민준이", school: "한성", admissionYear: 25, chatRoomName: "민준방"),
            ]
            let targetFile = root.appendingPathComponent("한성25_박하연_성적표.pdf")
            try Data("partial attachment".utf8).write(to: targetFile)
            let result = try AttachmentScanner.scan(rootPath: root.path, students: students)
            let items = students.map { student in
                BatchItem(
                    studentID: student.id,
                    studentName: student.name,
                    nickname: student.nickname,
                    chatRoomName: student.chatRoomName,
                    message: "학생별 공지",
                    attachmentPaths: result.filesByStudentID[student.id]?.map(\.path) ?? []
                )
            }
            let metadata = BatchMetadata(schemaVersion: 1, classID: UUID(), sessionID: UUID(), date: "7월 31일", presetID: UUID(), presetVersion: 0, isLegacy: true)
            let issues = BatchParser.validate(batch: SendBatch(metadata: metadata, items: items), database: AppDatabase(students: students))
            return items[0].attachmentPaths?.isEmpty == true
                && items[1].attachmentPaths?.map { URL(fileURLWithPath: $0).lastPathComponent } == [targetFile.lastPathComponent]
                && items[2].attachmentPaths?.isEmpty == true
                && !issues.contains { $0.severity == .error }
        }
        check("공통 메시지 최대 5개 개별 유지", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let item = BatchItem(studentID: student.id, studentName: student.name, nickname: student.nickname, chatRoomName: student.chatRoomName, message: "1", additionalMessages: ["2", "3", "4", "5"])
            let metadata = BatchMetadata(schemaVersion: 1, classID: UUID(), sessionID: UUID(), date: "7월 12일", presetID: UUID(), presetVersion: 0, isLegacy: true)
            let batch = SendBatch(metadata: metadata, items: [item])
            let db = AppDatabase(students: [student])
            return item.allMessages == ["1", "2", "3", "4", "5"] && !BatchParser.validate(batch: batch, database: db).contains { $0.message.contains("최대 5개") }
        }
        check("메시지 6개 발송 차단", failures: &failures) {
            let student = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "테스트방")
            let item = BatchItem(studentID: student.id, studentName: student.name, nickname: student.nickname, chatRoomName: student.chatRoomName, message: "1", additionalMessages: ["2", "3", "4", "5", "6"])
            let metadata = BatchMetadata(schemaVersion: 1, classID: UUID(), sessionID: UUID(), date: "7월 12일", presetID: UUID(), presetVersion: 0, isLegacy: true)
            let batch = SendBatch(metadata: metadata, items: [item])
            return BatchParser.validate(batch: batch, database: AppDatabase(students: [student])).contains { $0.message.contains("최대 5개") }
        }
        check("kmsg 안전 옵션 고정", failures: &failures) {
            let arguments = KmsgSafeAdapter.safeArguments(roomName: "정확한 방", message: "메시지", verifyOnly: true)
            return arguments.contains("--exact")
                && arguments.contains("--require-unique")
                && arguments.contains("--no-forced-typing")
                && arguments.contains("--verify-only")
                && !arguments.contains("--dry-run")
        }
        check("kmsg 즉시 중지 토큰", failures: &failures) {
            let token = KmsgCancellationToken()
            guard !token.isCancelled else { return false }
            token.cancel()
            return token.isCancelled
        }
        check("kmsg 내장 버전·방 제목 공백 포함 정확 비교", failures: &failures) {
            KmsgEmbeddedEngine.upstreamCommit == "fb70208286a1da3a404861dc944db470176155f6"
                && KmsgSafeAdapter.normalizeRoomName("25. 한성 홍길동 방") != KmsgSafeAdapter.normalizeRoomName("25.한성홍길동방")
                && KmsgSafeAdapter.normalizeRoomName("25.  한성 홍길동 방") != KmsgSafeAdapter.normalizeRoomName("25. 한성 홍길동 방")
                && KmsgSafeAdapter.normalizeRoomName("\u{AC00}") == KmsgSafeAdapter.normalizeRoomName("\u{1100}\u{1161}")
        }
        check("같은 제목 방을 서로 다른 chat_id로 구분", failures: &failures) {
            let first = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "같은 제목", chatID: "chat-1")
            let second = Student(name: "김학생", nickname: "학생이", school: "한성", admissionYear: 25, chatRoomName: "같은 제목", chatID: "chat-2")
            let items = [
                BatchItem(studentID: first.id, studentName: first.name, nickname: first.nickname, chatRoomName: first.chatRoomName, message: "첫 메시지", chatID: first.chatID),
                BatchItem(studentID: second.id, studentName: second.name, nickname: second.nickname, chatRoomName: second.chatRoomName, message: "둘째 메시지", chatID: second.chatID),
            ]
            let metadata = BatchMetadata(schemaVersion: 0, classID: UUID(), sessionID: UUID(), date: "7월 26일", presetID: UUID(), presetVersion: 0, isLegacy: true)
            let issues = BatchParser.validate(batch: SendBatch(metadata: metadata, items: items), database: AppDatabase(students: [first, second]))
            return !issues.contains { $0.message.contains("동일한 카카오톡 발송 대상") || $0.message.contains("동일 톡방 이름") }
        }
        check("같은 chat_id 중복 발송 차단", failures: &failures) {
            let first = Student(name: "홍길동", nickname: "길동이", school: "한성", admissionYear: 25, chatRoomName: "방1", chatID: "same-chat")
            let second = Student(name: "김학생", nickname: "학생이", school: "한성", admissionYear: 25, chatRoomName: "방2", chatID: "same-chat")
            let items = [
                BatchItem(studentID: first.id, studentName: first.name, nickname: first.nickname, chatRoomName: first.chatRoomName, message: "첫 메시지", chatID: first.chatID),
                BatchItem(studentID: second.id, studentName: second.name, nickname: second.nickname, chatRoomName: second.chatRoomName, message: "둘째 메시지", chatID: second.chatID),
            ]
            let metadata = BatchMetadata(schemaVersion: 0, classID: UUID(), sessionID: UUID(), date: "7월 26일", presetID: UUID(), presetVersion: 0, isLegacy: true)
            return BatchParser.validate(batch: SendBatch(metadata: metadata, items: items), database: AppDatabase(students: [first, second])).contains {
                $0.message.contains("동일한 카카오톡 발송 대상")
            }
        }
        check("카카오톡 학생방 제목 파싱", failures: &failures) {
            let exactTitle = "26.  인천영 김민준  화학 단톡방"
            let chat = KmsgEmbeddedChat(
                title: exactTitle,
                chatID: "chat-parser",
                lastMessage: "안녕하세요",
                listIndex: 3
            )
            guard let parsed = KakaoStudentRoomParser.parse(chat, knownSchools: []) else { return false }
            return parsed.school == "인천"
                && parsed.admissionYear == 26
                && parsed.name == "김민준"
                && parsed.title == exactTitle
                && parsed.chatID == "chat-parser"
                && parsed.hasStandardSuffix
        }
        check("카카오톡 최신 방 학생 DB 자동 갱신", failures: &failures) {
            let existing = Student(name: "김민준", nickname: "민준이", school: "인천", admissionYear: 26, chatRoomName: "이전 방", chatID: "chat-old-db")
            let chats = [
                KmsgEmbeddedChat(title: "26 인천 김민준 화학 단톡방", chatID: "chat-no-message", lastMessage: nil, listIndex: 0),
                KmsgEmbeddedChat(title: "26  인천 김민준 화학 단톡방", chatID: "chat-new", lastMessage: "최근 대화", listIndex: 2),
                KmsgEmbeddedChat(title: "26 인천 김민준 화학 단톡방", chatID: "chat-older", lastMessage: "이전 대화", listIndex: 5),
                KmsgEmbeddedChat(title: "한성26 박하연 화학 단톡방", chatID: "chat-hayeon", lastMessage: nil, listIndex: 7),
                KmsgEmbeddedChat(title: "세종26 이학생 화학 단톡방", chatID: "chat-ambiguous-1", lastMessage: nil, listIndex: 8),
                KmsgEmbeddedChat(title: "26 세종 이학생 화학 단톡방", chatID: "chat-ambiguous-2", lastMessage: nil, listIndex: 9),
            ]
            let output = KakaoStudentDBSynchronizer.synchronize(chats: chats, currentStudents: [existing], currentYear: 26)
            let updated = output.students.first { $0.name == "김민준" }
            let added = output.students.first { $0.name == "박하연" }
            return updated?.chatID == "chat-new"
                && updated?.chatRoomName == "26  인천 김민준 화학 단톡방"
                && added?.nickname == "하연이"
                && output.students.contains(where: { $0.name == "이학생" }) == false
                && output.report.updatedStudents == 1
                && output.report.addedStudents == 1
                && output.report.skippedIdentities == 1
                && output.report.multipleRoomSelections == 1
        }
        check("카카오톡 DB 운영 학번 제한", failures: &failures) {
            let chats = [
                KmsgEmbeddedChat(title: "21 한성 김길동 화학 단톡방", chatID: "chat-21", lastMessage: "최신", listIndex: 0),
                KmsgEmbeddedChat(title: "26 한성 박하연 화학 단톡방", chatID: "chat-26", lastMessage: "최신", listIndex: 1),
                KmsgEmbeddedChat(title: "31 한성 이민준 화학 단톡방", chatID: "chat-31", lastMessage: "최신", listIndex: 2),
            ]
            let output = KakaoStudentDBSynchronizer.synchronize(
                chats: chats,
                currentStudents: [],
                allowedAdmissionYears: Set([21, 31]),
                currentYear: 26
            )
            return Set(output.students.map(\.admissionYear)) == Set([21, 31])
                && output.students.allSatisfy { $0.admissionYear != 26 }
        }
        check("실제 추출 채팅 최대 1000개 학생방 인식", failures: &failures) {
            struct ChatRow: Decodable {
                var title: String
                var chatID: String?
                var lastMessage: String?
                enum CodingKeys: String, CodingKey {
                    case title
                    case chatID = "chat_id"
                    case lastMessage = "last_message"
                }
            }
            struct Payload: Decodable { var chats: [ChatRow] }
            let url = URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("kakao-chats-limit-2000.json")
            guard FileManager.default.fileExists(atPath: url.path) else { return false }
            let payload = try JSONDecoder().decode(Payload.self, from: Data(contentsOf: url))
            let chats = payload.chats.prefix(1_000).enumerated().map { index, row in
                KmsgEmbeddedChat(title: row.title, chatID: row.chatID, lastMessage: row.lastMessage, listIndex: index)
            }
            let recognized = chats.compactMap { KakaoStudentRoomParser.parse($0, knownSchools: []) }
            return chats.count <= 1_000 && recognized.count >= 500
        }
        let providedWorkbook = URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("2026 1학기.xlsx")
        if FileManager.default.fileExists(atPath: providedWorkbook.path) {
            check("제공 XLSX 340명 가져오기", failures: &failures) {
                let result = try XLSXMigrationImporter.importWorkbook(at: providedWorkbook)
                return result.students.count == 340 && !result.classes.isEmpty && result.students.allSatisfy { !$0.chatRoomName.isEmpty }
            }
            check("일반 XLSX 학생 헤더 가져오기", failures: &failures) {
                try XLSXMigrationImporter.importStudents(at: providedWorkbook).count == 340
            }
        } else {
            print("SKIP: 제공 XLSX 340명 가져오기 — 2026 1학기.xlsx 없음")
            print("SKIP: 일반 XLSX 학생 헤더 가져오기 — 2026 1학기.xlsx 없음")
        }
        if failures.isEmpty {
            print("SELF-TEST PASSED")
            return 0
        }
        print("SELF-TEST FAILED: \(failures.joined(separator: ", "))")
        return 1
    }

    private static func check(_ name: String, failures: inout [String], body: () throws -> Bool) {
        do {
            if try body() { print("PASS: \(name)") } else { failures.append(name); print("FAIL: \(name)") }
        } catch { failures.append(name); print("FAIL: \(name) — \(error.localizedDescription)") }
    }

    private static func sampleInput(homework: Double?, homeworkMax: Double?, test: Double?, testMax: Double?) -> LessonInput {
        LessonInput(studentID: UUID(), studentName: "홍길동", nickname: "길동이", date: "7월 12일", attendance: "출석", attitude: "3", homeworkScore: homework, homeworkMaximum: homeworkMax, testScore: test, testMaximum: testMax, homeworkComment: "", testComment: "", progress: "진도", assignment: "숙제", examUnit: "시험 단원", notice: "공지")
    }
}
