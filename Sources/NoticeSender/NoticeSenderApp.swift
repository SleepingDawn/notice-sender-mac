import SwiftUI
import Darwin

@main
struct NoticeSenderApp: App {
    @StateObject private var store: AppStore
    @StateObject private var kakao: KakaoAutomationService

    init() {
        if CommandLine.arguments.contains("--self-test") {
            Darwin.exit(SelfTest.run())
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
