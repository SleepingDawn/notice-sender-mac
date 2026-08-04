import Foundation

struct TemplateVariableDescriptor: Hashable, Sendable {
    var token: String
    var label: String
    var detail: String
}

enum TemplateVariableCatalog {
    static let all: [TemplateVariableDescriptor] = [
        .init(token: "academy", label: "학원명", detail: "Preset에 설정된 학원 이름"),
        .init(token: "teacher", label: "선생님 이름", detail: "Preset에 설정된 선생님 이름"),
        .init(token: "student_name", label: "학생 이름", detail: "학생의 전체 이름"),
        .init(token: "nickname", label: "학생 호칭", detail: "학생별로 설정한 자연스러운 호칭"),
        .init(token: "date", label: "수업 날짜", detail: "이번 수업 날짜"),
        .init(token: "attendance", label: "수업 방식", detail: "출석 또는 동영상 상태"),
        .init(token: "attitude_stars", label: "수업 태도 별점", detail: "수업 태도를 별표로 변환한 값"),
        .init(token: "attitude_comment", label: "수업 태도 코멘트", detail: "태도 점수에 맞춘 설명"),
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
        .init(token: "progress", label: "수업 진도", detail: "이번 수업에서 진행한 내용"),
        .init(token: "assignment", label: "다음 과제", detail: "학생에게 안내할 과제"),
        .init(token: "exam_unit", label: "시험 범위", detail: "테스트 또는 시험 단원"),
        .init(token: "notice", label: "공지 내용", detail: "수업별로 입력한 공지"),
        .init(token: "exam_sections", label: "모의고사 학생 결과", detail: "모의고사별 학생 점수·등수·코멘트 묶음"),
        .init(token: "exam_summary_sections", label: "모의고사 공통 요약", detail: "학생 개인 결과를 제외한 모의고사 요약"),
    ]

    static var promptGuide: String {
        all.map { "{{\($0.token)}} = \($0.label): \($0.detail)" }
            .joined(separator: "\n")
    }

    static var friendlyLabels: String {
        all.map(\.label).joined(separator: " · ")
    }
}
