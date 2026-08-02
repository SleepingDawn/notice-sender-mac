import AppKit
import ApplicationServices
import Foundation
import KmsgSafeCore

struct KakaoDiagnostic: Sendable {
    var accessibilityTrusted: Bool
    var isRunning: Bool
    var appVersion: String
    var exposedElementCount: Int
    var textInputCount: Int
    var kmsgAvailable: Bool
    var kmsgVersion: String
    var summary: String
}

struct KakaoRunSummary: Identifiable, Sendable {
    let id = UUID()
    var batchID: UUID
    var dryRun: Bool
    var finishedAt: Date = .now
    var totalCount: Int
    var completedThisRun: Int
    var alreadySentCount: Int
    var failedCount: Int
    var wasStopped: Bool
    var detail: String
    var items: [BatchItem]

    var succeeded: Bool { failedCount == 0 && !wasStopped }
}

enum BatchRunPolicy {
    static func shouldProcess(status: BatchItemStatus, dryRun: Bool) -> Bool {
        dryRun || status != .sent
    }

    static func statusAfterSuccess(previousStatus: BatchItemStatus, dryRun: Bool) -> BatchItemStatus {
        if dryRun, previousStatus == .sent { return .sent }
        return dryRun ? .verified : .sent
    }

    static func statusAfterFailure(previousStatus: BatchItemStatus, dryRun: Bool) -> BatchItemStatus {
        dryRun && previousStatus == .sent ? .sent : .failed
    }
}

enum KakaoAutomationError: LocalizedError {
    case accessibilityDenied
    case notRunning
    case searchResultCount(Int)
    case roomVerificationFailed
    case composerNotUnique(Int)
    case cannotSetMessage
    case cannotVerifyMessage
    case keyboardEventFailed

    var errorDescription: String? {
        switch self {
        case .accessibilityDenied: "시스템 설정에서 이 앱의 손쉬운 사용 권한을 허용해주세요."
        case .notRunning: "KakaoTalk Mac이 실행 중이거나 로그인된 상태가 아닙니다."
        case .searchResultCount(let count): "정확히 일치하는 톡방 검색 결과가 1개가 아닙니다(\(count)개)."
        case .roomVerificationFailed: "방을 연 뒤 현재 방 제목을 다시 확인하지 못했습니다."
        case .composerNotUnique(let count): "메시지 입력창을 하나로 특정하지 못했습니다(\(count)개)."
        case .cannotSetMessage: "메시지 입력창에 공지 멘트를 설정하지 못했습니다."
        case .cannotVerifyMessage: "입력된 공지 멘트를 전송 전에 검증하지 못했습니다."
        case .keyboardEventFailed: "키보드 이벤트를 생성하지 못했습니다."
        }
    }
}

@MainActor
final class KakaoAutomationService: ObservableObject {
    @Published var diagnostic: KakaoDiagnostic?
    @Published var statusText = "진단 전"
    @Published var isBusy = false
    @Published var shouldStop = false
    @Published var isRepairingAccessibility = false
    @Published private(set) var lastRunSummary: KakaoRunSummary?

    private let bundleIdentifier = "com.kakao.KakaoTalkMac"
    private let kmsg = KmsgSafeAdapter()
    private var diagnosticTask: Task<Void, Never>?
    private var activeCancellationToken: KmsgCancellationToken?

    func requestAccessibilityPermission() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        Task {
            try? await Task.sleep(for: .seconds(1))
            runDiagnostic()
        }
    }

    func ensureAccessibilityForOperation() -> Bool {
        if AXIsProcessTrusted() || diagnostic?.accessibilityTrusted == true {
            runDiagnostic()
            return true
        }
        statusText = "손쉬운 사용 설정에서 공지발송 스위치를 켠 뒤 다시 눌러주세요."
        requestAccessibilityPermission()
        openAccessibilitySettings()
        return false
    }

    func repairAccessibilityPermission() {
        guard !isRepairingAccessibility else { return }
        isRepairingAccessibility = true
        statusText = "이전 권한 기록을 정리하고 있습니다."
        Task {
            let resetResult = await Task.detached(priority: .userInitiated) {
                let process = Process()
                process.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
                process.arguments = ["reset", "Accessibility", "kr.onesolution.NoticeSender"]
                let errorPipe = Pipe()
                process.standardError = errorPipe
                do {
                    try process.run()
                    process.waitUntilExit()
                    let detail = String(data: errorPipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
                    return (process.terminationStatus, detail)
                } catch {
                    return (Int32(-1), error.localizedDescription)
                }
            }.value

            guard resetResult.0 == 0 else {
                statusText = "권한 기록 초기화 실패: \(resetResult.1)"
                isRepairingAccessibility = false
                return
            }

            let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
            _ = AXIsProcessTrustedWithOptions(options)
            openAccessibilitySettings()
            statusText = "시스템 설정에서 공지발송 스위치만 켜주세요. 앱이 자동으로 확인합니다."

            for _ in 0..<90 {
                try? await Task.sleep(for: .seconds(1))
                let pid = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).first?.processIdentifier
                let available = await Task.detached(priority: .utility) {
                    Self.probeAccessibility(pid: pid)
                }.value
                if available {
                    runDiagnostic()
                    statusText = "손쉬운 사용 권한이 정상적으로 연결되었습니다."
                    isRepairingAccessibility = false
                    return
                }
            }
            runDiagnostic()
            isRepairingAccessibility = false
        }
    }

    func runDiagnostic() {
        let application = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).first
        let pid = application?.processIdentifier
        let version = Bundle(path: "/Applications/KakaoTalk.app")?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "알 수 없음"
        diagnosticTask?.cancel()
        statusText = "손쉬운 사용 권한을 확인하고 있습니다."
        diagnosticTask = Task { [weak self] in
            guard let self else { return }
            let trusted = await Task.detached(priority: .utility) {
                Self.probeAccessibility(pid: pid)
            }.value
            guard !Task.isCancelled else { return }
            let summary: String
            if !trusted { summary = "손쉬운 사용 권한이 필요합니다." }
            else if application == nil { summary = "KakaoTalk을 실행하고 로그인해주세요." }
            else { summary = "손쉬운 사용 권한과 KakaoTalk 창 연결을 확인했습니다." }
            let engineVersion = await kmsg.version() ?? "안전형 내장 엔진"
            guard !Task.isCancelled else { return }
            diagnostic = KakaoDiagnostic(
                accessibilityTrusted: trusted,
                isRunning: application != nil,
                appVersion: version,
                exposedElementCount: trusted && application != nil ? 1 : 0,
                textInputCount: 0,
                kmsgAvailable: kmsg.isAvailable,
                kmsgVersion: engineVersion,
                summary: kmsg.isAvailable ? summary : "\(summary) 안전형 kmsg 실행 파일이 없어 발송은 차단됩니다."
            )
            statusText = summary
        }
    }

    func send(batch: SendBatch, dryRun: Bool, store: AppStore) async {
        guard !isBusy else { return }
        isBusy = true
        shouldStop = false
        lastRunSummary = nil
        let cancellationToken = KmsgCancellationToken()
        activeCancellationToken = cancellationToken
        defer {
            if activeCancellationToken === cancellationToken {
                activeCancellationToken = nil
            }
            isBusy = false
        }
        let runtimeIssues = BatchParser.validate(batch: batch, database: store.database, allowEmptyMessages: dryRun)
        store.validationIssues = runtimeIssues
        guard !runtimeIssues.contains(where: { $0.severity == .error }) else {
            statusText = "검증 오류가 있어 전송하지 않았습니다."
            return
        }
        guard let application = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).first else {
            statusText = KakaoAutomationError.notRunning.localizedDescription
            return
        }
        let accessibilityAvailable = await Task.detached(priority: .userInitiated) {
            Self.probeAccessibility(pid: application.processIdentifier)
        }.value
        guard accessibilityAvailable else {
            statusText = KakaoAutomationError.accessibilityDenied.localizedDescription
            return
        }

        var working = batch
        var completedThisRun = 0
        var alreadySentCount = 0
        var failedCount = 0
        var wasStopped = false
        for index in working.items.indices {
            if shouldStop {
                wasStopped = true
                statusText = "사용자가 작업을 중지했습니다."
                break
            }
            let previousStatus = working.items[index].status
            if !BatchRunPolicy.shouldProcess(status: previousStatus, dryRun: dryRun) {
                alreadySentCount += 1
                continue
            }
            working.items[index].status = .sending
            working.items[index].error = nil
            store.currentBatch = working
            statusText = "\(working.items[index].studentName): \(dryRun ? "드라이런" : "전송") 중"
            do {
                try await process(
                    working.items[index],
                    dryRun: dryRun,
                    cancellationToken: cancellationToken
                )
                if shouldStop || cancellationToken.isCancelled {
                    working.items[index].status = BatchRunPolicy.statusAfterFailure(previousStatus: previousStatus, dryRun: dryRun)
                    working.items[index].error = "사용자 중지"
                    failedCount += 1
                    wasStopped = true
                    store.currentBatch = working
                    statusText = "사용자가 작업을 중지했습니다."
                    break
                }
                let completedStatus = BatchRunPolicy.statusAfterSuccess(previousStatus: previousStatus, dryRun: dryRun)
                working.items[index].status = completedStatus
                completedThisRun += 1
                store.log(item: working.items[index], batchID: working.id, result: dryRun ? .verified : .sent, detail: dryRun ? "드라이런: 정확한 방 제목 확인, 메시지·첨부 전송 없음" : "정확한 방 제목 확인 후 메시지·첨부 전송 완료")
                store.currentBatch = working
            } catch {
                working.items[index].status = BatchRunPolicy.statusAfterFailure(previousStatus: previousStatus, dryRun: dryRun)
                let detail = (shouldStop || cancellationToken.isCancelled) ? "사용자 중지" : error.localizedDescription
                working.items[index].error = detail
                failedCount += 1
                wasStopped = shouldStop || cancellationToken.isCancelled
                store.log(item: working.items[index], batchID: working.id, result: .failed, detail: detail)
                store.currentBatch = working
                statusText = (shouldStop || cancellationToken.isCancelled)
                    ? "사용자가 작업을 중지했습니다."
                    : "즉시 중지: \(error.localizedDescription)"
                break
            }
        }
        let completed = failedCount == 0 && !wasStopped && working.items.allSatisfy { item in
            dryRun ? (item.status == .verified || item.status == .sent) : item.status == .sent
        }
        if completed {
            statusText = dryRun ? "드라이런을 완료했습니다. 실제 메시지와 첨부파일은 전송하지 않았습니다." : "전송 요청을 모두 완료했습니다."
        }
        store.currentBatch = working
        lastRunSummary = KakaoRunSummary(
            batchID: working.id,
            dryRun: dryRun,
            totalCount: working.items.count,
            completedThisRun: completedThisRun,
            alreadySentCount: alreadySentCount,
            failedCount: failedCount,
            wasStopped: wasStopped,
            detail: statusText,
            items: working.items
        )
    }

    func stop() {
        shouldStop = true
        activeCancellationToken?.cancel()
        statusText = "중지 요청을 처리하고 있습니다…"
    }

    func clearLastRunSummary() {
        lastRunSummary = nil
    }

    private func process(
        _ item: BatchItem,
        dryRun: Bool,
        cancellationToken: KmsgCancellationToken
    ) async throws {
        if dryRun {
            // A dry run must visibly exercise the same exact-title search that
            // the user expects to verify. Passing chat_id opens a cached chat-list
            // row directly and can look as if no student room was searched.
            // Deliberately omit chat_id here so every dry-run item performs a
            // fresh, exact, unique room-name search without touching the composer.
            _ = try await kmsg.verifyByExactRoomSearch(
                roomName: item.chatRoomName,
                cancellationToken: cancellationToken
            )
            return
        }

        let messages = Array(item.allMessages.prefix(5))
        let attachmentPaths = item.attachmentPaths ?? []
        if messages.isEmpty, attachmentPaths.isEmpty {
            _ = try await kmsg.verify(
                roomName: item.chatRoomName,
                chatID: item.chatID,
                cancellationToken: cancellationToken
            )
        } else {
            _ = try await kmsg.send(
                roomName: item.chatRoomName,
                chatID: item.chatID,
                messages: messages,
                attachmentPaths: attachmentPaths,
                cancellationToken: cancellationToken
            )
        }
    }

    nonisolated private static func probeAccessibility(pid: pid_t?) -> Bool {
        if AXIsProcessTrusted() { return true }
        guard let pid else { return false }
        let root = AXUIElementCreateApplication(pid)
        var windows: CFTypeRef?
        return AXUIElementCopyAttributeValue(root, kAXWindowsAttribute as CFString, &windows) == .success
    }

    private func openAccessibilitySettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") else { return }
        NSWorkspace.shared.open(url)
    }

}
