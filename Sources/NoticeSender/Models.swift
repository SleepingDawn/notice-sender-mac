import Foundation

enum PresetKind: String, Codable, CaseIterable, Identifiable, Sendable {
    case direct = "직접입력"
    case first = "첫 수업"
    case testOnly = "시험만"
    case homeworkOnly = "숙제만"
    case regular = "일반 수업"
    case mock = "모의고사"
    var id: String { rawValue }
}

struct Student: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var name: String
    var nickname: String
    var school: String
    var admissionYear: Int
    var chatRoomName: String
    /// Synthetic local ID assigned by kmsg. Used only when same-title rooms
    /// cannot be distinguished safely by their visible title.
    var chatID: String? = nil
    var isActive: Bool = true

    var duplicateKey: String { "\(school)|\(admissionYear)|\(name)" }
}

enum AdmissionYearPolicy {
    static let validRange = 0...99
    static let allYears = Array(validRange)

    static func isValid(_ year: Int) -> Bool {
        validRange.contains(year)
    }

    static func formatted(_ year: Int) -> String {
        isValid(year) ? String(format: "%02d", year) : String(year)
    }

    static func parseTwoDigit(_ value: String) -> Int? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count == 2,
              trimmed.unicodeScalars.allSatisfy({ (48...57).contains(Int($0.value)) }),
              let year = Int(trimmed),
              isValid(year)
        else { return nil }
        return year
    }

    static func parseImported(_ value: String) -> Int? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: "")
        var year: Int
        if let numeric = Double(trimmed), numeric.rounded() == numeric {
            year = Int(numeric)
        } else {
            let digits = trimmed.filter(\.isNumber)
            guard let parsed = Int(digits) else { return nil }
            year = parsed
        }
        if (1_000...9_999).contains(year) { year %= 100 }
        return isValid(year) ? year : nil
    }

    static func normalized(_ years: [Int]) -> [Int] {
        Array(Set(years.filter(isValid))).sorted()
    }
}

struct ClassMember: Codable, Identifiable, Hashable, Sendable {
    var id: UUID { studentID }
    var studentID: UUID
    var nicknameOverride: String?
}

struct ClassGroup: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var name: String
    var school: String
    var admissionYear: Int
    var members: [ClassMember] = []
    var defaultPresetID: UUID?
    var version: Int = 1
}

enum ClassMemberSorter {
    static func sorted(_ members: [ClassMember], students: [Student]) -> [ClassMember] {
        let studentByID = Dictionary(uniqueKeysWithValues: students.map { ($0.id, $0) })
        return members.sorted { lhs, rhs in
            let lhsName = studentByID[lhs.studentID]?.name ?? ""
            let rhsName = studentByID[rhs.studentID]?.name ?? ""
            if lhsName.isEmpty != rhsName.isEmpty { return !lhsName.isEmpty }
            let nameOrder = lhsName.localizedStandardCompare(rhsName)
            if nameOrder != .orderedSame { return nameOrder == .orderedAscending }
            return lhs.studentID.uuidString < rhs.studentID.uuidString
        }
    }
}

enum ClassStudentFilter {
    static func students(in databaseStudents: [Student], school: String, admissionYear: Int?) -> [Student] {
        let normalizedSchool = school.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedSchool.isEmpty, let admissionYear else { return [] }
        return databaseStudents
            .filter {
                $0.isActive &&
                $0.school.trimmingCharacters(in: .whitespacesAndNewlines).localizedCaseInsensitiveCompare(normalizedSchool) == .orderedSame &&
                $0.admissionYear == admissionYear
            }
            .sorted {
                $0.name.localizedStandardCompare($1.name) == .orderedAscending
            }
    }
}

struct MessagePreset: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var kind: PresetKind
    var name: String
    var version: Int = 1
    var teacherName: String = "김요섭"
    var academyName: String = "SNT"
    var highThreshold: Double = 0.8
    var middleThreshold: Double = 0.5
    var presentTemplate: String
    var videoTemplate: String
    var absentTemplate: String
    var updatedAt: Date = .now
    var mockExamCount: Int? = nil
}

struct ExamInput: Codable, Hashable, Sendable {
    var title: String
    var score: Double?
    var maximum: Double?
    var average: Double?
    var highest: Double?
    var rank: Int?
    var attendees: Int?
    var comment: String = ""
}

struct LessonInput: Codable, Hashable, Sendable {
    var studentID: UUID
    var studentName: String
    var nickname: String
    var date: String
    var attendance: String
    var attitude: String
    var homeworkScore: Double?
    var homeworkMaximum: Double?
    var testScore: Double?
    var testMaximum: Double?
    var homeworkComment: String
    var testComment: String
    var progress: String
    var assignment: String
    var examUnit: String
    var notice: String
    var exams: [ExamInput] = []
}

struct BatchMetadata: Codable, Hashable, Sendable {
    var schemaVersion: Int
    var classID: UUID
    var sessionID: UUID
    var date: String
    var presetID: UUID
    var presetVersion: Int
    var isLegacy: Bool = false
}

enum BatchItemStatus: String, Codable, Sendable {
    case ready = "준비"
    case sending = "전송 중"
    case verified = "드라이런 확인 완료"
    case sent = "전송 요청 완료"
    case failed = "실패"
    case uncertain = "확인 필요"
    case skipped = "제외"
}

struct BatchItem: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var studentID: UUID
    var studentName: String
    var nickname: String
    var chatRoomName: String
    var message: String
    var chatID: String? = nil
    var status: BatchItemStatus = .ready
    var error: String?
    /// Messages after the first one. Total messages are capped at five.
    var additionalMessages: [String]? = nil
    /// Absolute paths selected by the depth-limited attachment scanner.
    var attachmentPaths: [String]? = nil
    /// Direct-input messages preserve every character except the explicitly removed quote pair.
    var preserveMessageWhitespace: Bool? = nil

    var allMessages: [String] {
        let source = [message] + (additionalMessages ?? [])
        if preserveMessageWhitespace == true {
            return source.filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        }
        return source.map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
    }
}

enum CommonMessagePolicy {
    static func effectiveMessage(isEnabled: Bool, text: String) -> String? {
        guard isEnabled,
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else { return nil }
        return text
    }

    /// Keeps every student's own notice first and the shared message second.
    /// Empty/whitespace-only values are not included in the send sequence.
    static func orderedMessages(individualNotice: String, commonMessage: String?) -> [String] {
        [individualNotice, commonMessage].compactMap { message in
            guard let message,
                  !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            else { return nil }
            return message
        }
    }
}

struct SendBatch: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var metadata: BatchMetadata
    var items: [BatchItem]
    var createdAt: Date = .now
}

enum IssueSeverity: String, Codable, Sendable {
    case error = "오류"
    case warning = "경고"
}

struct ValidationIssue: Identifiable, Hashable, Sendable {
    var id = UUID()
    var severity: IssueSeverity
    var row: Int?
    var message: String
}

struct SendLog: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var batchID: UUID
    var studentID: UUID
    var studentName: String
    var chatRoomName: String
    var sentAt: Date
    var result: BatchItemStatus
    var messageSHA256: String
    var detail: String?
}

struct LessonPlan: Codable, Identifiable, Hashable, Sendable {
    var id: UUID = UUID()
    var classID: UUID
    var presetID: UUID
    var date: Date = .now
    var progress: String = ""
    var assignment: String = ""
    var examUnit: String = ""
    var notice: String = ""
    var previewAttendance: String = "출석"
    var updatedAt: Date = .now
}

struct AppDatabase: Codable, Sendable {
    var schemaVersion: Int = 7
    var students: [Student] = []
    var classes: [ClassGroup] = []
    var presets: [MessagePreset] = DefaultPresets.all
    var logs: [SendLog] = []
    /// Optional to preserve compatibility with databases created before school-specific academy settings.
    var schoolAcademies: [String: String]? = nil
    /// Optional to preserve compatibility with databases created before lesson management.
    var lessonPlans: [LessonPlan]? = nil
    var attachmentRootPath: String? = nil
    /// Legacy compatibility field. Schema 7 always stores every two-digit cohort (00...99).
    var operatingAdmissionYears: [Int]? = nil
}

enum DefaultPresets {
    static let direct = MessagePreset(
        kind: .direct,
        name: "직접입력",
        presentTemplate: "{{notice}}",
        videoTemplate: "{{notice}}",
        absentTemplate: "{{notice}}"
    )

    static let first = MessagePreset(
        kind: .first,
        name: "첫 수업 기본",
        presentTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : 첫 수업이므로 과제 점수 없습니다.
        수업 태도 : {{attitude_stars}}
        테스트 : {{exam_unit}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        첫 수업으로 배부된 과제가 없어 숙제 현황 없습니다.
        다음 수업부터 숙제 현황 안내드릴 수 있도록 하겠습니다.

        ④ 클리닉 테스트

        {{exam_unit}}

        <개별 코멘트>

        - {{attitude_comment}}
        {{homework_comment_line}}{{test_comment_line}}
        <공통 코멘트>

        - {{notice}}
        """,
        videoTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : 첫 수업이므로 과제 점수 없습니다.
        수업 태도 : 동영상 시청으로 점수 없습니다.
        테스트 : {{exam_unit}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        첫 수업으로 숙제 현황 안내가 없습니다.

        <개별 코멘트>

        영상 시청 후 수업 내용을 토대로 과제를 풀어주세요.

        <공통 코멘트>

        - {{notice}}
        """,
        absentTemplate: "{{nickname}}의 [{{date}}] 첫 수업 결석 안내입니다.\n\n{{notice}}"
    )

    static let regular = MessagePreset(
        kind: .regular,
        name: "숙제와 시험 모두 있는 수업",
        presentTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : {{homework_stars}}
        수업 태도 : {{attitude_stars}}
        테스트 : {{test_stars}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        지난 수업 총 {{homework_max}} 문제의 과제가 있었습니다.
        숙제 성취도 : {{homework_percent}}

        ④ 클리닉 테스트

        {{exam_unit}}에 대해 테스트를 진행했습니다.
        학생 점수 : {{test_percent}}

        <개별 코멘트>

        - {{attitude_comment}}
        {{homework_comment_line}}{{test_comment_line}}
        <공통 코멘트>

        - {{notice}}
        """,
        videoTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        동영상 시청으로 과제 및 수업 태도 점수는 없습니다.

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        영상 시청 후 수업 내용을 토대로 과제를 풀어주세요.

        <공통 코멘트>

        - {{notice}}
        """,
        absentTemplate: "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n\n{{notice}}"
    )

    static let testOnly = MessagePreset(
        kind: .testOnly,
        name: "시험만 있는 수업",
        presentTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : 오늘은 진도상 숙제검사를 생략했습니다.
        수업 태도 : {{attitude_stars}}
        테스트 : {{test_stars}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        오늘은 진도상 숙제검사를 생략했습니다.

        ④ 클리닉 테스트

        {{exam_unit}}에 대해 테스트를 진행했습니다.
        학생 점수 : {{test_percent}}

        <개별 코멘트>

        - {{attitude_comment}}
        {{test_comment_line}}
        <공통 코멘트>

        - {{notice}}
        """,
        videoTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : 오늘은 진도상 숙제검사를 생략했습니다.
        수업 태도 : 동영상 시청으로 점수 없습니다.
        테스트 : {{test_stars}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        오늘은 진도상 숙제검사를 생략했습니다.

        ④ 클리닉 테스트

        {{exam_unit}}

        <공통 코멘트>

        - {{notice}}
        """,
        absentTemplate: "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n시험범위: {{exam_unit}}\n\n{{notice}}"
    )

    static let homeworkOnly = MessagePreset(
        kind: .homeworkOnly,
        name: "숙제만 있는 수업",
        presentTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : {{homework_stars}}
        수업 태도 : {{attitude_stars}}
        테스트 : {{exam_unit}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        지난 수업 총 {{homework_max}} 문제의 과제가 있었습니다.
        숙제 성취도 : {{homework_percent}}

        ④ 시험범위

        {{exam_unit}}

        <개별 코멘트>

        - {{attitude_comment}}
        {{homework_comment_line}}
        <공통 코멘트>

        - {{notice}}
        """,
        videoTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 수업 안내드리겠습니다.

        과제 점수 : 동영상 시청으로 점수 없습니다.
        수업 태도 : 동영상 시청으로 점수 없습니다.
        테스트 : {{exam_unit}}

        ① 진도 및 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        ③ 숙제 현황

        지난 숙제가 완료되었는지 확인해주시기 바랍니다.

        ④ 시험범위

        {{exam_unit}}

        <공통 코멘트>

        - {{notice}}
        """,
        absentTemplate: "{{nickname}}의 [{{date}}] 결석 안내입니다.\n\n진도: {{progress}}\n과제: {{assignment}}\n시험범위: {{exam_unit}}\n\n{{notice}}"
    )

    static let mock = MessagePreset(
        kind: .mock,
        name: "모의고사 기본",
        presentTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 모의고사 결과 안내드리겠습니다.

        ① 수업 내용

        {{progress}}

        ② 과제

        {{assignment}}

        {{exam_sections}}
        <공통 코멘트>

        - {{notice}}
        """,
        videoTemplate: """
        안녕하세요 {{academy}} 화학강사 {{teacher}}입니다.
        {{nickname}}의 [{{date}}] 모의고사 자료 안내드리겠습니다.

        {{exam_summary_sections}}
        자료를 수령해서 풀고 틀린 것을 공부해주세요.

        <공통 코멘트>

        - {{notice}}
        """,
        absentTemplate: "{{nickname}}의 [{{date}}] 모의고사 결석 안내입니다.\n\n{{exam_summary_sections}}\n{{notice}}",
        mockExamCount: 3
    )

    static let all = [direct, first, testOnly, homeworkOnly, regular, mock]
}

enum DirectNoticeMessage {
    /// Removes exactly one ASCII double-quote pair while preserving all other text.
    static func normalized(_ message: String) -> String {
        guard message.count >= 2, message.first == "\"", message.last == "\"" else { return message }
        return String(message.dropFirst().dropLast())
    }
}
