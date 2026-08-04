import SwiftUI

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

    func lessonInput(for preset: MessagePreset) -> LessonInput {
        let examCount = max(1, min(3, preset.mockExamCount ?? 3))
        let exams = preset.kind == .mock
            ? (1...examCount).map { index in
                ExamInput(
                    title: examUnit.isEmpty ? "모의고사 \(index)" : "\(examUnit) \(index)",
                    score: index == 1 ? Double(testSolvedCount) : nil,
                    maximum: Double(testProblemCount),
                    average: nil,
                    highest: nil,
                    rank: nil,
                    attendees: nil,
                    comment: index == 1 ? testComment : ""
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

    var body: some View {
        GroupBox("결과 미리보기 입력") {
            VStack(alignment: .leading, spacing: 12) {
                Picker("수업 방식", selection: $draft.attendance) {
                    ForEach(LessonAttendanceMode.allCases) { mode in
                        Text(mode.rawValue).tag(mode)
                    }
                }
                .pickerStyle(.segmented)

                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                    previewTextRow("진도", text: $draft.progress)
                    previewTextRow("숙제", text: $draft.assignment)
                    previewTextRow("시험 단원", text: $draft.examUnit)
                    previewTextRow("공지", text: $draft.notice)
                    previewTextRow("숙제 코멘트", text: $draft.homeworkComment)
                    previewTextRow("테스트 코멘트", text: $draft.testComment)
                }

                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                    GridRow {
                        Text("숙제 문제 수")
                        numericField("문제 수", value: $draft.homeworkProblemCount)
                        Text("숙제 푼 개수")
                        numericField("푼 개수", value: $draft.homeworkSolvedCount)
                    }
                    GridRow {
                        Text("테스트 문제 수")
                        numericField("문제 수", value: $draft.testProblemCount)
                        Text("테스트 푼 개수")
                        numericField("푼 개수", value: $draft.testSolvedCount)
                    }
                }
            }
            .padding(8)
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
}
