import AppKit
import Foundation

struct SheetTemplate {
    var tsv: String
    var html: String
    var sessionID: UUID
}

enum ClipboardError: LocalizedError {
    case missingClass
    case missingPreset

    var errorDescription: String? {
        switch self {
        case .missingClass: "반을 선택해주세요."
        case .missingPreset: "Preset을 선택해주세요."
        }
    }
}

enum SheetTemplateBuilder {
    static func build(group: ClassGroup, students: [Student], preset: MessagePreset) -> SheetTemplate {
        switch preset.kind.category {
        case .direct: buildDirect(group: group, students: students, preset: preset)
        case .regular: buildLesson(group: group, students: students, preset: preset)
        case .mock: buildMock(group: group, students: students, preset: preset)
        }
    }

    private static func buildDirect(group: ClassGroup, students: [Student], preset: MessagePreset) -> SheetTemplate {
        let sessionID = UUID()
        let memberStudents = group.members.compactMap { member in students.first { $0.id == member.studentID } }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        // A:D is the only visible table. E:H carries narrow identity and safety data.
        let columnCount = 8
        var rows = [Array(repeating: "", count: columnCount)]
        rows[0] = ["번호", "발송", "성명", "메시지", "student_id", "nickname", "", ""]
        for (offset, student) in memberStudents.enumerated() {
            let nickname = group.members.first(where: { $0.studentID == student.id })?.nicknameOverride?.nilIfEmpty ?? student.nickname
            rows.append([String(offset + 1), "TRUE", student.name, "", student.id.uuidString, nickname, "", ""])
        }
        let metadata = [
            ("NOTICE_SENDER_SAFE", "1"), ("class_id", group.id.uuidString),
            ("session_id", sessionID.uuidString), ("date", "직접입력"),
            ("preset_id", preset.id.uuidString), ("preset_version", String(preset.version)),
            ("preset_type", preset.kind.category.rawValue)
        ]
        for (index, pair) in metadata.enumerated() {
            if index == rows.count { rows.append(Array(repeating: "", count: columnCount)) }
            rows[index][6] = pair.0; rows[index][7] = pair.1
        }
        let tsv = rows.map { $0.map(tsvEscape).joined(separator: "\t") }.joined(separator: "\n")
        let safety = "font-size:1px;color:#fff;width:2px;max-width:2px;padding:0;border:0"
        let visible = "border:1px solid #111;padding:6px"
        let htmlRows = rows.enumerated().map { rowIndex, row in
            "<tr>" + row.enumerated().map { column, value in
                td(value, style: column >= 4 ? safety : visible + (rowIndex == 0 ? ";font-weight:700;background:#eee" : ""))
            }.joined() + "</tr>"
        }.joined()
        return SheetTemplate(tsv: tsv, html: htmlDocument(htmlRows), sessionID: sessionID)
    }

    private static func buildLesson(group: ClassGroup, students: [Student], preset: MessagePreset) -> SheetTemplate {
        let sessionID = UUID()
        let memberStudents = group.members.compactMap { member in students.first { $0.id == member.studentID } }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        // A:J is the visible working table. K:M contains narrow safety data used by the app.
        var rows = Array(repeating: Array(repeating: "", count: 13), count: 3)
        rows[0][1] = "성명 (홍길동)"; rows[0][2] = "성명 (길동이)"; rows[0][3] = "1회차"
        rows[0][8] = "날짜"; rows[0][9] = "날짜 입력"
        rows[1][0] = "번호"; rows[1][1] = "이름"; rows[1][2] = "호칭"
        ["출석", "태도", "숙제(개수 입력)", "테스트(개수 입력)", "숙제 코멘트", "테스트 코멘트", "공지 멘트", "student_id"].enumerated().forEach {
            rows[1][$0.offset + 3] = $0.element
        }
        rows[2][5] = ""; rows[2][6] = ""
        let metadata = [
            ("NOTICE_SENDER_SAFE", "1"), ("class_id", group.id.uuidString),
            ("session_id", sessionID.uuidString), ("date", "=J1"),
            ("preset_id", preset.id.uuidString), ("preset_version", String(preset.version)),
            ("preset_type", preset.kind.category.rawValue),
            ("안내", "A:M 전체를 복사해 앱에 붙여넣으세요")
        ]
        let firstStudentRow = 4
        let teacherRow = firstStudentRow + memberStudents.count + 1

        for (offset, student) in memberStudents.enumerated() {
            let rowNumber = firstStudentRow + offset
            let nickname = group.members.first(where: { $0.studentID == student.id })?.nicknameOverride?.nilIfEmpty ?? student.nickname
            var row = Array(repeating: "", count: 13)
            row[0] = String(offset + 1); row[1] = student.name; row[2] = nickname
            row[3] = "출석"; row[4] = "3"; row[9] = lessonFormula(row: rowNumber, teacherRow: teacherRow, preset: preset)
            row[10] = student.id.uuidString
            rows.append(row)
        }
        rows.append(Array(repeating: "", count: 13))
        var teacherHeader = Array(repeating: "", count: 13); teacherHeader[3] = "선생님 입력 (E열에 값만 입력)"; rows.append(teacherHeader)
        for title in ["진도", "숙제", "시험단원", "공지"] {
            var row = Array(repeating: "", count: 13); row[3] = title; rows.append(row)
        }
        for (index, pair) in metadata.enumerated() where rows.indices.contains(index) {
            rows[index][11] = pair.0; rows[index][12] = pair.1
        }

        let tsv = rows.map { $0.map(tsvEscape).joined(separator: "\t") }.joined(separator: "\n")
        let html = lessonHTML(rows: rows, firstStudentRow: firstStudentRow, studentCount: memberStudents.count)
        return SheetTemplate(tsv: tsv, html: html, sessionID: sessionID)
    }

    private static func buildMock(group: ClassGroup, students: [Student], preset: MessagePreset) -> SheetTemplate {
        let sessionID = UUID()
        let memberStudents = group.members.compactMap { member in students.first { $0.id == member.studentID } }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        let mockCount = max(1, min(3, preset.mockExamCount ?? 3))
        let headers = ["번호", "이름", "호칭", "출석", "태도"]
            + (1...mockCount).map { "모의고사\($0) 점수" }
            + (1...mockCount).map { "모의고사\($0) 코멘트" }
            + ["공지 멘트", "student_id"]
        let metadataColumn = headers.count
        let columnCount = metadataColumn + 2
        var rows = Array(repeating: Array(repeating: "", count: columnCount), count: 2)
        rows[0][3] = "모의고사"; rows[0][headers.count - 2] = "날짜 입력"
        rows[1] = headers + ["", ""]
        let firstStudentRow = 3
        let teacherRow = firstStudentRow + memberStudents.count + 1
        for (offset, student) in memberStudents.enumerated() {
            let rowNumber = firstStudentRow + offset
            let nickname = group.members.first(where: { $0.studentID == student.id })?.nicknameOverride?.nilIfEmpty ?? student.nickname
            var row = Array(repeating: "", count: columnCount)
            row[0] = String(offset + 1); row[1] = student.name; row[2] = nickname; row[3] = "출석"; row[4] = "3"
            row[headers.count - 2] = mockFormula(row: rowNumber, firstStudentRow: firstStudentRow, lastStudentRow: firstStudentRow + memberStudents.count - 1, teacherRow: teacherRow, preset: preset, examCount: mockCount, dateCell: "$\(columnName(headers.count - 2))$1")
            row[headers.count - 1] = student.id.uuidString
            rows.append(row)
        }
        rows.append(Array(repeating: "", count: columnCount))
        var teacherHeader = Array(repeating: "", count: columnCount); teacherHeader[3] = "선생님 입력"; rows.append(teacherHeader)
        for title in ["진도", "숙제", "시험단원", "공지"] { var row = Array(repeating: "", count: columnCount); row[3] = title; rows.append(row) }
        let metadata = [("NOTICE_SENDER_SAFE", "1"), ("class_id", group.id.uuidString), ("session_id", sessionID.uuidString), ("date", "=\(columnName(headers.count - 2))1"), ("preset_id", preset.id.uuidString), ("preset_version", String(preset.version)), ("preset_type", preset.kind.category.rawValue)]
        for (index, pair) in metadata.enumerated() where rows.indices.contains(index) { rows[index][metadataColumn] = pair.0; rows[index][metadataColumn + 1] = pair.1 }
        let tsv = rows.map { $0.map(tsvEscape).joined(separator: "\t") }.joined(separator: "\n")
        let safety = "font-size:1px;color:#fff;width:2px;max-width:2px;padding:0;border:0"
        let htmlRows = rows.map { row in
            "<tr>" + row.enumerated().map { column, value in
                td(value, style: column >= headers.count - 1 ? safety : "border:1px solid #111;padding:6px")
            }.joined() + "</tr>"
        }.joined()
        return SheetTemplate(tsv: tsv, html: htmlDocument(htmlRows), sessionID: sessionID)
    }

    static func copyToPasteboard(_ template: SheetTemplate) {
        let board = NSPasteboard.general
        board.clearContents()
        board.setString(template.tsv, forType: .string)
        board.setString(template.html, forType: .html)
    }

    private static func lessonFormula(row: Int, teacherRow: Int, preset: MessagePreset) -> String {
        if preset.kind == .direct { return "" }
        let academy = formulaText(preset.academyName)
        let teacher = formulaText(preset.teacherName)
        let progress = "$E$\(teacherRow + 1)"
        let assignment = "$E$\(teacherRow + 2)"
        let unit = "$E$\(teacherRow + 3)"
        let notice = "$E$\(teacherRow + 4)"
        let homeworkMax = "$F$3"
        let testMax = "$G$3"
        let homeworkStars = "IF(D\(row)<>\"출석\",\"동영상 시청으로 점수 없습니다.\",IF(OR(F\(row)=\"\",\(homeworkMax)<=0),\"과제를 제출하지 않았습니다.\",IF(F\(row)/\(homeworkMax)>=\(preset.highThreshold),\"★★★\",IF(F\(row)/\(homeworkMax)>=\(preset.middleThreshold),\"★★☆\",\"★☆☆\"))))"
        let testStars = "IF(OR(G\(row)=\"\",\(testMax)<=0),\"시험 미응시로 점수 없습니다\",IF(G\(row)/\(testMax)>=\(preset.highThreshold),\"★★★\",IF(G\(row)/\(testMax)>=\(preset.middleThreshold),\"★★☆\",\"★☆☆\")))"
        let homeworkTop: String
        let homeworkSection: String
        switch preset.kind {
        case .direct:
            homeworkTop = formulaText("")
            homeworkSection = formulaText("")
        case .first:
            homeworkTop = formulaText("첫 수업이므로 과제 점수 없습니다.")
            homeworkSection = formulaText("첫 수업으로 배부된 과제가 없어 숙제 현황 없습니다. 다음 수업부터 숙제 현황 안내드릴 수 있도록 하겠습니다.")
        case .testOnly:
            homeworkTop = formulaText("오늘은 진도상 숙제검사를 생략했습니다.")
            homeworkSection = formulaText("오늘은 진도상 숙제검사를 생략했습니다.")
        case .homeworkOnly, .regular:
            homeworkTop = homeworkStars
            homeworkSection = "\"지난 수업 총 \"&\(homeworkMax)&\" 문제의 과제가 있었습니다.\"&CHAR(10)&\"숙제 성취도 : \"&IF(OR(F\(row)=\"\",\(homeworkMax)<=0),\"과제를 제출하지 않았습니다.\",ROUND(F\(row)/\(homeworkMax)*100,2)&\"%\")"
        case .mock:
            homeworkTop = formulaText("")
            homeworkSection = formulaText("")
        }
        let testTop = (preset.kind == .first || preset.kind == .homeworkOnly) ? unit : testStars
        let fourthTitle = preset.kind == .homeworkOnly ? "④ 시험범위" : "④ 클리닉 테스트"
        let testSection = (preset.kind == .first || preset.kind == .homeworkOnly)
            ? unit
            : "\(unit)&\"에 대해 테스트를 진행했습니다.\"&CHAR(10)&\"학생 점수 : \"&IF(OR(G\(row)=\"\",\(testMax)<=0),\"시험 미응시로 점수 없습니다.\",ROUND(G\(row)/\(testMax)*100,2)&\"%\")"
        let individual = "IF(D\(row)=\"출석\",\"- \"&IF(E\(row)=3,\"수업 집중력이 좋습니다.\",IF(E\(row)=\"2-핸드폰\",\"수업시간에 집중하지 않고 핸드폰을 사용했습니다.\",\"수업 시간에 집중이 흐트러진 모습이 있었습니다.\"))&CHAR(10)&IF(H\(row)=\"\",\"\",\"- \"&H\(row)&CHAR(10))&IF(I\(row)=\"\",\"\",\"- \"&I\(row)&CHAR(10)),\"영상 시청 후 수업 내용을 토대로 과제를 풀어주세요\")"
        let body = "\(academy)&\" 화학강사 \"&\(teacher)&\"입니다.\"&CHAR(10)&C\(row)&\"의 [\"&$J$1&\"] 수업 안내드리겠습니다.\"&CHAR(10)&CHAR(10)&\"과제 점수 : \"&\(homeworkTop)&CHAR(10)&\"수업 태도 : \"&IF(D\(row)=\"출석\",IF(E\(row)=3,\"★★★\",\"★★☆\"),\"동영상 시청으로 점수 없습니다.\")&CHAR(10)&\"테스트 : \"&\(testTop)&CHAR(10)&CHAR(10)&\"① 진도 및 수업 내용\"&CHAR(10)&CHAR(10)&\(progress)&CHAR(10)&CHAR(10)&\"② 과제\"&CHAR(10)&CHAR(10)&\(assignment)&CHAR(10)&CHAR(10)&\"③ 숙제 현황\"&CHAR(10)&CHAR(10)&\(homeworkSection)&CHAR(10)&CHAR(10)&\"\(fourthTitle)\"&CHAR(10)&CHAR(10)&\(testSection)&CHAR(10)&CHAR(10)&\"<개별 코멘트>\"&CHAR(10)&\(individual)&CHAR(10)&CHAR(10)&\"<공통 코멘트>\"&CHAR(10)&\"- \"&\(notice)&CHAR(10)&CHAR(10)&\"문의사항 있으시면 해당 카톡으로 언제든 연락주세요. 감사합니다.\""
        return "=IF(D\(row)=\"\",\"\",\(body))"
    }

    private static func mockFormula(row: Int, firstStudentRow: Int, lastStudentRow: Int, teacherRow: Int, preset: MessagePreset, examCount: Int, dateCell: String) -> String {
        let progress = "$E$\(teacherRow + 1)"
        let assignment = "$E$\(teacherRow + 2)"
        let notice = "$E$\(teacherRow + 4)"
        func exam(index: Int) -> String {
            let scoreColumnIndex = 5 + (index - 1)
            let commentColumnIndex = 5 + examCount + (index - 1)
            let scoreColumn = columnName(scoreColumnIndex)
            let commentColumn = columnName(commentColumnIndex)
            let score = "\(scoreColumn)\(row)"
            let comment = "\(commentColumn)\(row)"
            let range = "\(scoreColumn)$\(firstStudentRow):\(scoreColumn)$\(lastStudentRow)"
            return "\"\(index + 2) 모의고사 \(index)\"&CHAR(10)&\"응시인원 : \"&COUNTIF(\(range),\">0\")&\"명\"&CHAR(10)&\"평균 : \"&IFERROR(ROUND(AVERAGEIF(\(range),\">0\"),2),\"-\")&CHAR(10)&\"최고점 : \"&IFERROR(MAX(\(range)),\"-\")&CHAR(10)&\"학생 점수 : \"&IF(\(score)=\"\",\"미제출로 점수 없습니다.\",\(score)&\"점\")&CHAR(10)&\"학생 등수 : \"&IF(\(score)=\"\",\"미제출로 등수 없습니다.\",RANK(\(score),\(range))&\"등\")&CHAR(10)&IF(\(comment)=\"\",\"\",\"- \"&\(comment))&CHAR(10)&CHAR(10)"
        }
        let exams = (1...examCount).map(exam).joined(separator: "&")
        let body = "\(formulaText(preset.academyName))&\" 화학강사 \"&\(formulaText(preset.teacherName))&\"입니다.\"&CHAR(10)&C\(row)&\"의 [\"&\(dateCell)&\"] 모의고사 결과 안내드리겠습니다.\"&CHAR(10)&CHAR(10)&\"① 수업 내용\"&CHAR(10)&\(progress)&CHAR(10)&CHAR(10)&\"② 과제\"&CHAR(10)&\(assignment)&CHAR(10)&CHAR(10)&\(exams)&\"<공통 코멘트>\"&CHAR(10)&\"- \"&\(notice)&CHAR(10)&CHAR(10)&\"문의사항 있으시면 해당 카톡으로 언제든 연락주세요. 감사합니다.\""
        return "=IF(D\(row)=\"\",\"\",\(body))"
    }

    private static func columnName(_ zeroBased: Int) -> String {
        var number = zeroBased + 1
        var result = ""
        while number > 0 { number -= 1; result = String(UnicodeScalar(65 + number % 26)!) + result; number /= 26 }
        return result
    }

    private static func formulaText(_ value: String) -> String { "\"\(value.replacingOccurrences(of: "\"", with: "\"\""))\"" }
    private static func tsvEscape(_ value: String) -> String { value.replacingOccurrences(of: "\t", with: " ") }
    private static func lessonHTML(rows: [[String]], firstStudentRow: Int, studentCount: Int) -> String {
        let border = "border:1px solid #111;padding:6px;vertical-align:middle"
        let center = "\(border);text-align:center;font-weight:600"
        let safety = "font-size:1px;color:#fff;width:2px;max-width:2px;padding:0;border:0"
        var output = "<tr>"
        output += td("", attributes: "rowspan=\"3\"", style: "\(center);background:#cfcfcf;width:45px")
        output += td("성명<br>(홍길동)", attributes: "rowspan=\"3\"", style: "\(center);background:#cfcfcf;width:120px")
        output += td("성명<br>(길동이)", attributes: "rowspan=\"3\"", style: "\(center);background:#cfcfcf;width:120px")
        output += td("1회차", attributes: "colspan=\"5\"", style: "\(center);background:#00ef19")
        output += td("날짜", style: "\(center);background:#22d9e5;width:110px")
        output += td(rows[0][9], style: "\(center);width:120px") + td("", style: safety)
        output += td(rows[0][11], style: safety) + td(rows[0][12], style: safety) + "</tr>"
        output += "<tr>"
        output += td("출석", attributes: "rowspan=\"2\"", style: "\(center);background:#fff1c9;width:115px")
        output += td("태도", attributes: "rowspan=\"2\"", style: "\(center);background:#e99599;width:115px")
        output += td("숙제(개수 입력)", style: "\(center);background:#c9dced;width:130px")
        output += td("테스트(개수 입력)", style: "\(center);background:#e8cedb;width:140px")
        output += td("숙제 코멘트", attributes: "rowspan=\"2\"", style: "\(center);background:#a99ad0;width:140px")
        output += td("테스트 코멘트", attributes: "rowspan=\"2\"", style: "\(center);background:#a99ad0;width:140px")
        output += td("공지 멘트", attributes: "rowspan=\"2\"", style: "\(center);background:#fff500;width:260px")
        output += td("student_id", attributes: "rowspan=\"2\"", style: safety)
        output += td(rows[1][11], style: safety) + td(rows[1][12], style: safety) + "</tr>"
        output += "<tr>" + td(rows[2][5], style: "\(center);background:#c9dced") + td(rows[2][6], style: "\(center);background:#e8cedb")
        output += td(rows[2][11], style: safety) + td(rows[2][12], style: safety) + "</tr>"
        for index in 0..<studentCount {
            let row = rows[firstStudentRow - 1 + index]
            output += "<tr>"
            for column in 0...10 {
                let style: String
                switch column {
                case 0...2: style = "\(border);text-align:center"
                case 3: style = "\(border);text-align:center;background:#f0f1f2"
                case 4: style = "\(border);text-align:center;background:#d7efbd;color:#087d53"
                case 9: style = "\(border);white-space:pre-wrap"
                case 10: style = safety
                default: style = border
                }
                output += td(row[column], style: style)
            }
            output += td(row[11], style: safety) + td(row[12], style: safety) + "</tr>"
        }
        let blankIndex = firstStudentRow - 1 + studentCount
        output += "<tr>" + td("", attributes: "colspan=\"11\"", style: "height:12px") + td(rows[blankIndex][11], style: safety) + td(rows[blankIndex][12], style: safety) + "</tr>"
        let teacherIndex = blankIndex + 1
        output += "<tr>" + td("", attributes: "colspan=\"3\"") + td(rows[teacherIndex][3], attributes: "colspan=\"7\"", style: "\(center);background:#00ef19") + td("", style: safety) + td(rows[teacherIndex][11], style: safety) + td(rows[teacherIndex][12], style: safety) + "</tr>"
        for index in (teacherIndex + 1)...(teacherIndex + 4) {
            output += "<tr>" + td("", attributes: "colspan=\"3\"") + td(rows[index][3], style: "\(center);background:#fff1c9") + td(rows[index][4], attributes: "colspan=\"6\"", style: "\(border);min-width:650px") + td("", style: safety) + td(rows[index][11], style: safety) + td(rows[index][12], style: safety) + "</tr>"
        }
        return htmlDocument(output)
    }

    private static func td(_ value: String, attributes: String = "", style: String = "") -> String {
        "<td \(attributes) style=\"\(style)\">\(htmlEscape(value))</td>"
    }
    private static func htmlDocument(_ rows: String) -> String {
        "<html><head><meta charset=\"utf-8\"></head><body><table style=\"border-collapse:collapse;font-family:-apple-system,Apple SD Gothic Neo,sans-serif;font-size:14px\">\(rows)</table></body></html>"
    }
    private static func htmlEscape(_ value: String) -> String {
        value.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\n", with: "<br>")
    }
}

extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
