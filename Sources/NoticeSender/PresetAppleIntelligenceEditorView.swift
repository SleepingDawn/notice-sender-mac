import SwiftUI

struct PresetAppleIntelligenceEditorView: View {
    @Binding var draft: MessagePreset
    let onDraftChanged: () -> Void

    @State private var instruction = ""
    @State private var revision: PresetAIRevision?
    @State private var status = ""
    @State private var statusIsError = false
    @State private var isGenerating = false

    private var availability: PresetAIAvailability {
        AppleIntelligencePresetEditor.availability
    }

    var body: some View {
        GroupBox("Apple Intelligence로 문구 편집") {
            VStack(alignment: .leading, spacing: 12) {
                Label(
                    availability.message,
                    systemImage: availability.isAvailable ? "apple.intelligence" : "exclamationmark.triangle"
                )
                .foregroundStyle(availability.isAvailable ? .primary : .secondary)

                Text("변수명을 몰라도 됩니다. 예: ‘출석 문구를 더 따뜻하게 바꾸고, 과제 성취도 뒤에 테스트 점수를 안내해줘.’")
                    .font(.callout)
                    .foregroundStyle(.secondary)

                TextEditor(text: $instruction)
                    .frame(minHeight: 72, maxHeight: 110)
                    .padding(5)
                    .background(.background)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(.separator))

                HStack {
                    DisclosureGroup("AI가 사용할 수 있는 학생·수업 정보") {
                        Text(TemplateVariableCatalog.friendlyLabels)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .padding(.top, 4)
                    }
                    Spacer()
                    Button {
                        Task { await generateRevision() }
                    } label: {
                        Label("AI 초안 만들기", systemImage: "apple.intelligence")
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(
                        !availability.isAvailable
                            || instruction.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                            || isGenerating
                    )
                }

                if isGenerating {
                    HStack {
                        ProgressView().controlSize(.small)
                        Text("이 Mac에서 Preset 초안을 만들고 있습니다…")
                            .foregroundStyle(.secondary)
                    }
                } else if !status.isEmpty {
                    Text(status)
                        .font(.callout)
                        .foregroundStyle(statusIsError ? .red : .secondary)
                }

                if let revision {
                    Divider()
                    Text(revision.summary).font(.headline)
                    revisionPreview("출석 문구", text: revision.presentTemplate)
                    revisionPreview("동영상 문구", text: revision.videoTemplate)
                    HStack {
                        Spacer()
                        Button("초안 버리기", role: .cancel) {
                            self.revision = nil
                            status = ""
                            statusIsError = false
                        }
                        Button("편집기에 적용") { applyRevision(revision) }
                            .buttonStyle(.borderedProminent)
                    }
                    Text("적용 후에도 저장되지는 않습니다. 아래 문구와 예시를 검토한 뒤 ‘새 버전으로 저장’을 눌러야 저장됩니다.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(8)
        }
    }

    private func revisionPreview(_ title: String, text: String) -> some View {
        DisclosureGroup(title) {
            ScrollView {
                Text(text)
                    .font(.system(.caption, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
            }
            .frame(maxHeight: 150)
            .background(.background)
            .clipShape(RoundedRectangle(cornerRadius: 6))
        }
    }

    @MainActor
    private func generateRevision() async {
        isGenerating = true
        revision = nil
        status = ""
        statusIsError = false
        defer { isGenerating = false }
        do {
            let generated = try await AppleIntelligencePresetEditor.revise(
                preset: draft,
                instruction: instruction
            )
            revision = generated
            status = "지원 변수와 빈 문구 검증을 통과한 초안입니다."
        } catch {
            status = PresetAIDiagnostic.userMessage(error)
            statusIsError = true
        }
    }

    private func applyRevision(_ revision: PresetAIRevision) {
        do {
            draft = try AppleIntelligencePresetEditor.applying(revision, to: draft)
            self.revision = nil
            status = "AI 초안을 편집기에 적용했습니다. 아직 저장되지 않았습니다."
            statusIsError = false
            onDraftChanged()
        } catch {
            status = error.localizedDescription
            statusIsError = true
        }
    }
}
