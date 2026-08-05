import SwiftUI

struct MockExamPreviewDraft: Hashable {
    var title: String
    var score: Double
    var maximum: Double
    var average: Double
    var highest: Double
    var rank: Int
    var attendees: Int
    var comment: String

    static func sample(_ index: Int) -> Self {
        .init(
            title: "기말 모의고사 \(index)회차",
            score: Double(92 - (index - 1) * 4),
            maximum: 100,
            average: Double(76 - (index - 1) * 2),
            highest: Double(98 - (index - 1)),
            rank: index + 1,
            attendees: 20,
            comment: "\(index)회차 틀린 문제를 다시 확인해주세요."
        )
    }
}

struct PresetPreviewDraft: Hashable {
    var attendance: LessonAttendanceMode = .present
    var progress = "화학 결합과 분자 구조"
    var assignment = "교재 20~35번"
    var examUnit = "화학 결합"
    var notice = "복습과 숙제를 꼼꼼히 해주세요."
    var homeworkProblemCount = 20
    var homeworkSolvedCount = 18
    var testProblemCount = 10
    var testSolvedCount = 9
    var homeworkComment = "숙제를 성실하게 했습니다."
    var testComment = "틀린 문제를 복습해주세요."
    var mockExams = (1...3).map(MockExamPreviewDraft.sample)

    func lessonInput(for preset: MessagePreset) -> LessonInput {
        let examCount = max(1, min(3, preset.mockExamCount ?? 3))
        let exams: [ExamInput] = preset.kind.category == .mock
            ? (0..<examCount).map { index in
                let exam = mockExams.indices.contains(index) ? mockExams[index] : MockExamPreviewDraft.sample(index + 1)
                return ExamInput(
                    title: exam.title,
                    score: exam.score,
                    maximum: exam.maximum,
                    average: exam.average,
                    highest: exam.highest,
                    rank: exam.rank,
                    attendees: exam.attendees,
                    comment: exam.comment
                )
            }
            : []
        return LessonInput(
            studentID: UUID(),
            studentName: "홍길동",
            nickname: "길동이",
            date: "7월 12일",
            attendance: attendance.rawValue,
            attitude: "3",
            homeworkScore: Double(homeworkSolvedCount),
            homeworkMaximum: Double(homeworkProblemCount),
            testScore: Double(testSolvedCount),
            testMaximum: Double(testProblemCount),
            homeworkComment: homeworkComment,
            testComment: testComment,
            progress: progress,
            assignment: assignment,
            examUnit: examUnit,
            notice: notice,
            exams: exams
        )
    }
}

struct PresetPreviewInputView: View {
    @Binding var draft: PresetPreviewDraft
    var preset: MessagePreset

    var body: some View {
        GroupBox("결과 미리보기 입력") {
            VStack(alignment: .leading, spacing: 12) {
                Picker("수업 방식", selection: $draft.attendance) {
                    ForEach(LessonAttendanceMode.allCases) { mode in
                        Text(mode.rawValue).tag(mode)
                    }
                }
                .pickerStyle(.segmented)

                switch preset.kind.category {
                case .direct:
                    Grid(alignment: .leading) { previewTextRow("메시지", text: $draft.notice) }
                case .regular:
                    regularInputs
                case .mock:
                    mockInputs
                }
            }
            .padding(8)
        }
    }

    private var regularInputs: some View {
        VStack(alignment: .leading, spacing: 12) {
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                previewTextRow("진도", text: $draft.progress)
                previewTextRow("숙제", text: $draft.assignment)
                previewTextRow("시험 단원", text: $draft.examUnit)
                previewTextRow("공지", text: $draft.notice)
                previewTextRow("숙제 코멘트", text: $draft.homeworkComment)
                previewTextRow("테스트 코멘트", text: $draft.testComment)
            }
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                GridRow { Text("숙제 문제 수"); numericField("문제 수", value: $draft.homeworkProblemCount); Text("숙제 푼 개수"); numericField("푼 개수", value: $draft.homeworkSolvedCount) }
                GridRow { Text("테스트 문제 수"); numericField("문제 수", value: $draft.testProblemCount); Text("테스트 푼 개수"); numericField("푼 개수", value: $draft.testSolvedCount) }
            }
        }
    }

    private var mockInputs: some View {
        VStack(alignment: .leading, spacing: 12) {
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                previewTextRow("진도", text: $draft.progress)
                previewTextRow("다음 과제", text: $draft.assignment)
                previewTextRow("모의고사명", text: $draft.examUnit)
                previewTextRow("공지", text: $draft.notice)
            }
            ForEach(0..<max(1, min(3, preset.mockExamCount ?? 3)), id: \.self) { index in
                GroupBox("모의고사 \(index + 1)회차") {
                    Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                        previewTextRow("이름", text: $draft.mockExams[index].title)
                        GridRow {
                            Text("점수"); doubleField("점수", value: $draft.mockExams[index].score)
                            Text("만점"); doubleField("만점", value: $draft.mockExams[index].maximum)
                            Text("평균"); doubleField("평균", value: $draft.mockExams[index].average)
                            Text("최고점"); doubleField("최고점", value: $draft.mockExams[index].highest)
                        }
                        GridRow {
                            Text("등수"); numericField("등수", value: $draft.mockExams[index].rank)
                            Text("응시인원"); numericField("응시인원", value: $draft.mockExams[index].attendees)
                        }
                        previewTextRow("코멘트", text: $draft.mockExams[index].comment)
                    }.padding(6)
                }
            }
        }
    }

    private func previewTextRow(_ title: String, text: Binding<String>) -> some View {
        GridRow {
            Text(title).frame(width: 90, alignment: .leading)
            TextField(title, text: text, axis: .vertical)
                .lineLimit(1...3)
                .textFieldStyle(.roundedBorder)
        }
    }

    private func numericField(_ title: String, value: Binding<Int>) -> some View {
        TextField(title, value: value, format: .number)
            .textFieldStyle(.roundedBorder)
            .frame(width: 90)
    }

    private func doubleField(_ title: String, value: Binding<Double>) -> some View {
        TextField(title, value: value, format: .number)
            .textFieldStyle(.roundedBorder)
            .frame(width: 70)
    }
}
