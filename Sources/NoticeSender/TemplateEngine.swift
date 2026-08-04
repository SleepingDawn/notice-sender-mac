import Foundation

enum TemplateValidationError: LocalizedError {
    case invalidThresholds
    case emptyTemplate(String)
    case unknownToken(String)
    case malformedPlaceholder(String)
    case unsupportedAttendance(String)

    var errorDescription: String? {
        switch self {
        case .invalidThresholds: "별점 기준은 0~1 사이이며 중간 기준이 높은 기준보다 작아야 합니다."
        case .emptyTemplate(let name): "\(name) 문구가 비어 있습니다."
        case .unknownToken(let token): "지원하지 않는 변수입니다: {{\(token)}}"
        case .malformedPlaceholder(let name): "\(name) 문구에 닫히지 않았거나 잘못된 변수 표시가 있습니다."
        case .unsupportedAttendance(let value): "출결은 출석 또는 동영상만 사용할 수 있습니다: \(value)"
        }
    }
}

enum TemplateEngine {
    static let supportedTokens: Set<String> = [
        "academy", "teacher", "student_name", "nickname", "date", "attendance",
        "attitude_stars", "attitude_comment", "homework_stars", "test_stars",
        "homework_score", "homework_max", "homework_percent", "test_score", "test_max", "test_percent",
        "homework_comment", "test_comment", "homework_comment_line", "test_comment_line",
        "progress", "assignment", "exam_unit", "notice", "exam_sections", "exam_summary_sections"
    ]

    static func validate(_ preset: MessagePreset) throws {
        guard preset.middleThreshold >= 0, preset.highThreshold <= 1, preset.middleThreshold < preset.highThreshold else {
            throw TemplateValidationError.invalidThresholds
        }
        for (name, template) in [("출석", preset.presentTemplate), ("동영상", preset.videoTemplate)] {
            guard !template.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw TemplateValidationError.emptyTemplate(name) }
            guard hasWellFormedPlaceholders(in: template) else {
                throw TemplateValidationError.malformedPlaceholder(name)
            }
            for token in tokens(in: template) where !supportedTokens.contains(token) {
                throw TemplateValidationError.unknownToken(token)
            }
        }
    }

    static func render(preset: MessagePreset, input: LessonInput) throws -> String {
        try validate(preset)
        guard let attendance = LessonAttendanceMode(normalized: input.attendance) else {
            throw TemplateValidationError.unsupportedAttendance(input.attendance)
        }
        let template = switch attendance {
        case .present: preset.presentTemplate
        case .video: preset.videoTemplate
        }

        let attitudeComment: String
        switch input.attitude {
        case "3", "3.0": attitudeComment = "수업 집중력이 좋습니다."
        case "2-핸드폰": attitudeComment = "수업시간에 집중하지 않고 핸드폰을 사용했습니다."
        default: attitudeComment = "수업 시간에 집중이 흐트러진 모습이 있었습니다."
        }

        let values: [String: String] = [
            "academy": preset.academyName,
            "teacher": preset.teacherName,
            "student_name": input.studentName,
            "nickname": input.nickname,
            "date": input.date,
            "attendance": attendance.rawValue,
            "attitude_stars": input.attitude.hasPrefix("3") ? "★★★" : "★★☆",
            "attitude_comment": attitudeComment,
            "homework_stars": stars(score: input.homeworkScore, maximum: input.homeworkMaximum, preset: preset, missing: "과제를 제출하지 않았습니다."),
            "test_stars": stars(score: input.testScore, maximum: input.testMaximum, preset: preset, missing: "시험 미응시로 점수 없습니다."),
            "homework_score": number(input.homeworkScore),
            "homework_max": number(input.homeworkMaximum),
            "homework_percent": percent(score: input.homeworkScore, maximum: input.homeworkMaximum, missing: "과제를 제출하지 않았습니다."),
            "test_score": number(input.testScore),
            "test_max": number(input.testMaximum),
            "test_percent": percent(score: input.testScore, maximum: input.testMaximum, missing: "시험 미응시로 점수 없습니다."),
            "homework_comment": input.homeworkComment,
            "test_comment": input.testComment,
            "homework_comment_line": commentLine(input.homeworkComment),
            "test_comment_line": commentLine(input.testComment),
            "progress": input.progress,
            "assignment": input.assignment,
            "exam_unit": input.examUnit,
            "notice": input.notice,
            "exam_sections": examSections(Array(input.exams.prefix(max(1, min(3, preset.mockExamCount ?? 3)))), includeStudent: true),
            "exam_summary_sections": examSections(Array(input.exams.prefix(max(1, min(3, preset.mockExamCount ?? 3)))), includeStudent: false)
        ]
        var rendered = template
        for (key, value) in values { rendered = rendered.replacingOccurrences(of: "{{\(key)}}", with: value) }
        let closing = "문의사항 있으시면 해당 카톡으로 언제든 연락주세요. 감사합니다."
        return rendered.trimmingCharacters(in: .whitespacesAndNewlines) + "\n\n" + closing
    }

    private static func tokens(in template: String) -> [String] {
        guard let regex = try? NSRegularExpression(pattern: #"\{\{([^{}]+)\}\}"#) else { return [] }
        let range = NSRange(template.startIndex..., in: template)
        return regex.matches(in: template, range: range).compactMap {
            guard let tokenRange = Range($0.range(at: 1), in: template) else { return nil }
            return String(template[tokenRange])
        }
    }

    private static func hasWellFormedPlaceholders(in template: String) -> Bool {
        guard let regex = try? NSRegularExpression(pattern: #"\{\{([^{}]+)\}\}"#) else { return false }
        let range = NSRange(template.startIndex..., in: template)
        let textWithoutPlaceholders = regex.stringByReplacingMatches(
            in: template,
            range: range,
            withTemplate: ""
        )
        return !textWithoutPlaceholders.contains("{{")
            && !textWithoutPlaceholders.contains("}}")
    }

    private static func stars(score: Double?, maximum: Double?, preset: MessagePreset, missing: String) -> String {
        guard let score, let maximum, maximum > 0 else { return missing }
        let ratio = score / maximum
        return ratio >= preset.highThreshold ? "★★★" : (ratio >= preset.middleThreshold ? "★★☆" : "★☆☆")
    }

    private static func percent(score: Double?, maximum: Double?, missing: String) -> String {
        guard let score, let maximum, maximum > 0 else { return missing }
        return String(format: "%.2f%%", score / maximum * 100)
    }

    private static func number(_ value: Double?) -> String {
        guard let value else { return "" }
        return value.rounded() == value ? String(Int(value)) : String(format: "%.2f", value)
    }

    private static func commentLine(_ value: String) -> String {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "" : "\n- \(trimmed)\n"
    }

    private static func examSections(_ exams: [ExamInput], includeStudent: Bool) -> String {
        exams.prefix(3).enumerated().map { index, exam in
            var lines = ["\(index + 3) \(exam.title)"]
            if let attendees = exam.attendees { lines.append("응시인원 : \(attendees)명") }
            if let average = exam.average { lines.append("평균 : \(number(average))") }
            if let highest = exam.highest { lines.append("최고점 : \(number(highest))") }
            if includeStudent {
                if let score = exam.score { lines.append("학생 점수 : \(number(score))점") }
                if let rank = exam.rank { lines.append("학생 등수 : \(rank)등") }
                if !exam.comment.isEmpty { lines.append("\n- \(exam.comment)") }
            }
            return lines.joined(separator: "\n")
        }.joined(separator: "\n\n")
    }
}
