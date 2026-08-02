import SwiftUI
import Darwin

@main
struct NoticeSenderApp: App {
    @StateObject private var store: AppStore
    @StateObject private var kakao: KakaoAutomationService

    init() {
        let arguments = CommandLine.arguments
        if arguments.contains("--self-test") {
            Darwin.exit(SelfTest.run())
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
