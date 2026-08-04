import SwiftUI
import Darwin

#if canImport(FoundationModels)
import FoundationModels
#endif

@main
struct NoticeSenderApp: App {
    @StateObject private var store: AppStore
    @StateObject private var kakao: KakaoAutomationService

    init() {
        let arguments = CommandLine.arguments
        if arguments.contains("--self-test") {
            Darwin.exit(SelfTest.run())
        }
        if arguments.contains("--ai-diagnostic") {
            Task.detached {
                let availability = AppleIntelligencePresetEditor.availability
                print("Apple Intelligence 상태: \(availability.message)")
                #if canImport(FoundationModels)
                if #available(macOS 26.0, *) {
                    let model = SystemLanguageModel.default
                    let supportsKorean = model.supportsLocale(Locale(identifier: "ko_KR"))
                    let languages = model.supportedLanguages
                        .map(\.minimalIdentifier)
                        .sorted()
                        .joined(separator: ", ")
                    print("현재 Locale 지원: \(model.supportsLocale())")
                    print("한국어 Locale 지원: \(supportsKorean)")
                    print("지원 언어: \(languages)")
                }
                #endif
                guard availability.isAvailable else {
                    Darwin.exit(EXIT_FAILURE)
                }
                #if canImport(FoundationModels)
                if #available(macOS 26.0, *) {
                    do {
                        let smokeSession = LanguageModelSession(
                            instructions: "Reply briefly in English."
                        )
                        let smokeResponse = try await smokeSession.respond(to: "Reply with the single word OK.")
                        print("기본 모델 응답 성공: \(smokeResponse.content)")
                        let revision = try await AppleIntelligencePresetEditor.revise(
                            preset: DefaultPresets.regular,
                            instruction: "두 문구에서 ‘수업 안내드리겠습니다.’를 ‘수업 소식을 전해드리겠습니다.’로 바꿔줘."
                        )
                        let applied = try AppleIntelligencePresetEditor.applying(
                            revision,
                            to: DefaultPresets.regular
                        )
                        print("Apple Intelligence 생성·구조화·검증 성공")
                        print("변경 요약: \(revision.summary)")
                        print("출석 문구 글자 수: \(applied.presentTemplate.count)")
                        print("동영상 문구 글자 수: \(applied.videoTemplate.count)")
                        Darwin.exit(EXIT_SUCCESS)
                    } catch {
                        fputs("Apple Intelligence 진단 실패: \(PresetAIDiagnostic.errorDetails(error))\n", stderr)
                        Darwin.exit(EXIT_FAILURE)
                    }
                }
                #endif
                Darwin.exit(EXIT_FAILURE)
            }
            dispatchMain()
        }
        if let optionIndex = arguments.firstIndex(of: "--export-student-csv"), arguments.indices.contains(optionIndex + 1) {
            let destination = URL(fileURLWithPath: arguments[optionIndex + 1])
            let exportStore = AppStore()
            do {
                try exportStore.exportStudentCSV(to: destination)
                print("학생 DB \(exportStore.database.students.count)명을 \(destination.path)에 저장했습니다.")
                Darwin.exit(EXIT_SUCCESS)
            } catch {
                fputs("학생 DB CSV 내보내기 실패: \(error.localizedDescription)\n", stderr)
                Darwin.exit(EXIT_FAILURE)
            }
        }
        _store = StateObject(wrappedValue: AppStore())
        _kakao = StateObject(wrappedValue: KakaoAutomationService())
    }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environmentObject(store)
                .environmentObject(kakao)
                .frame(minWidth: 1120, minHeight: 720)
        }
        .windowStyle(.titleBar)
        .commands {
            CommandGroup(replacing: .newItem) { }
        }
    }
}
