import Foundation

struct TemplateVariableDescriptor: Hashable, Sendable {
    var token: String
    var label: String
    var detail: String
}

enum TemplateVariableCatalog {
    private static let identity: [TemplateVariableDescriptor] = [
        .init(token: "academy", label: "학원명", detail: "Preset에 설정된 학원 이름"),
        .init(token: "teacher", label: "선생님 이름", detail: "Preset에 설정된 선생님 이름"),
        .init(token: "student_name", label: "학생 이름", detail: "학생의 전체 이름"),
        .init(token: "nickname", label: "학생 호칭", detail: "학생별로 설정한 자연스러운 호칭"),
        .init(token: "date", label: "수업 날짜", detail: "이번 수업 날짜"),
        .init(token: "attendance", label: "수업 방식", detail: "출석 또는 동영상 상태"),
    ]

    private static let lessonShared: [TemplateVariableDescriptor] = [
        .init(token: "attitude_stars", label: "수업 태도 별점", detail: "수업 태도를 별표로 변환한 값"),
        .init(token: "attitude_comment", label: "수업 태도 코멘트", detail: "태도 점수에 맞춘 설명"),
        .init(token: "progress", label: "수업 진도", detail: "이번 수업에서 진행한 내용"),
        .init(token: "assignment", label: "다음 과제", detail: "학생에게 안내할 과제"),
        .init(token: "notice", label: "공지 내용", detail: "수업별로 입력한 공지"),
    ]

    private static let regular: [TemplateVariableDescriptor] = [
        .init(token: "homework_stars", label: "과제 별점", detail: "과제 성취도를 별표로 변환한 값"),
        .init(token: "test_stars", label: "테스트 별점", detail: "테스트 성취도를 별표로 변환한 값"),
        .init(token: "homework_score", label: "과제 점수", detail: "학생의 과제 점수"),
        .init(token: "homework_max", label: "과제 총점", detail: "과제의 만점"),
        .init(token: "homework_percent", label: "과제 성취도", detail: "과제 점수를 백분율로 변환한 값"),
        .init(token: "test_score", label: "테스트 점수", detail: "학생의 테스트 점수"),
        .init(token: "test_max", label: "테스트 총점", detail: "테스트의 만점"),
        .init(token: "test_percent", label: "테스트 성취도", detail: "테스트 점수를 백분율로 변환한 값"),
        .init(token: "homework_comment", label: "과제 코멘트", detail: "입력한 과제 코멘트 원문"),
        .init(token: "test_comment", label: "테스트 코멘트", detail: "입력한 테스트 코멘트 원문"),
        .init(token: "homework_comment_line", label: "과제 코멘트 문장", detail: "코멘트가 있을 때만 문장 전체를 표시"),
        .init(token: "test_comment_line", label: "테스트 코멘트 문장", detail: "코멘트가 있을 때만 문장 전체를 표시"),
        .init(token: "exam_unit", label: "시험 범위", detail: "테스트 또는 시험 단원"),
    ]

    private static let mockBase: [TemplateVariableDescriptor] = [
        .init(token: "mock_exam_name", label: "모의고사명", detail: "예: 기말 모의고사"),
        .init(token: "exam_sections", label: "모의고사 학생 결과 전체", detail: "1~3회차 점수·등수·코멘트를 한 번에 표시"),
        .init(token: "exam_summary_sections", label: "모의고사 공통 요약 전체", detail: "학생 개인 결과를 제외한 1~3회차 요약"),
    ]

    private static let mockPerExam: [TemplateVariableDescriptor] = (1...3).flatMap { index -> [TemplateVariableDescriptor] in
        let prefix = "mock\(index)"
        return [
            .init(token: "\(prefix)_title", label: "모의고사 \(index)회차 이름", detail: "\(index)회차의 표시 이름"),
            .init(token: "\(prefix)_score", label: "모의고사 \(index)회차 점수", detail: "학생의 \(index)회차 점수"),
            .init(token: "\(prefix)_max", label: "모의고사 \(index)회차 만점", detail: "\(index)회차의 만점"),
            .init(token: "\(prefix)_percent", label: "모의고사 \(index)회차 백분율", detail: "학생 점수를 만점 기준으로 계산"),
            .init(token: "\(prefix)_average", label: "모의고사 \(index)회차 평균", detail: "응시자 평균 점수"),
            .init(token: "\(prefix)_highest", label: "모의고사 \(index)회차 최고점", detail: "응시자 최고 점수"),
            .init(token: "\(prefix)_rank", label: "모의고사 \(index)회차 등수", detail: "학생의 응시자 내 등수"),
            .init(token: "\(prefix)_attendees", label: "모의고사 \(index)회차 응시인원", detail: "0점 초과 점수가 있는 응시자 수"),
            .init(token: "\(prefix)_comment", label: "모의고사 \(index)회차 코멘트", detail: "학생별 \(index)회차 코멘트"),
            .init(token: "\(prefix)_comment_line", label: "모의고사 \(index)회차 코멘트 문장", detail: "코멘트가 있을 때만 문장 전체를 표시"),
        ]
    }

    static let all: [TemplateVariableDescriptor] = identity + lessonShared + regular + mockBase + mockPerExam

    static func descriptors(for category: PresetCategory) -> [TemplateVariableDescriptor] {
        switch category {
        case .direct: identity + [lessonShared.last!]
        case .regular: identity + lessonShared + regular
        case .mock: identity + lessonShared + mockBase + mockPerExam
        }
    }

    static func descriptors(for preset: MessagePreset) -> [TemplateVariableDescriptor] {
        let source = descriptors(for: preset.kind.category)
        guard preset.kind.category == .mock else { return source }
        let count = max(1, min(3, preset.mockExamCount ?? 3))
        return source.filter { descriptor in
            guard descriptor.token.hasPrefix("mock"),
                  descriptor.token.count > 4,
                  let index = Int(String(descriptor.token.dropFirst(4).prefix(1)))
            else { return true }
            return index <= count
        }
    }

    static func promptGuide(for category: PresetCategory) -> String {
        descriptors(for: category).map { "{{\($0.token)}} = \($0.label): \($0.detail)" }.joined(separator: "\n")
    }

    static func aiPromptGuide(for category: PresetCategory) -> String {
        descriptors(for: category).map { "\(marker(for: $0.token)) = \($0.label)" }.joined(separator: "\n")
    }

    static func aiPromptGuide(for preset: MessagePreset) -> String {
        descriptors(for: preset).map { "\(marker(for: $0.token)) = \($0.label)" }.joined(separator: "\n")
    }

    static func friendlyLabels(for category: PresetCategory) -> String {
        descriptors(for: category).map(\.label).joined(separator: " · ")
    }

    static func friendlyLabels(for preset: MessagePreset) -> String {
        descriptors(for: preset).map(\.label).joined(separator: " · ")
    }

    static func label(for token: String) -> String { all.first(where: { $0.token == token })?.label ?? token }

    static func marker(for token: String) -> String {
        guard let index = all.firstIndex(where: { $0.token == token }) else { return "[[FIELD_UNKNOWN]]" }
        return String(format: "[[FIELD_%02d]]", index + 1)
    }

    static func encodedForAI(_ template: String) -> String {
        all.reduce(template) { $0.replacingOccurrences(of: "{{\($1.token)}}", with: marker(for: $1.token)) }
    }

    static func decodedFromAI(_ template: String) throws -> String {
        let decoded = all.reduce(template) { $0.replacingOccurrences(of: marker(for: $1.token), with: "{{\($1.token)}}") }
        guard !decoded.contains("[[FIELD_") else { throw PresetAIEditorError.invalidFieldCode }
        return decoded
    }
}
