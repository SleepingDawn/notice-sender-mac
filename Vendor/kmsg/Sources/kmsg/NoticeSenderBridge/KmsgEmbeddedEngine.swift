import ApplicationServices.HIServices
import AppKit
import Foundation

public struct KmsgEmbeddedResult: Sendable {
    public let roomTitle: String
    public let didSend: Bool

    public init(roomTitle: String, didSend: Bool) {
        self.roomTitle = roomTitle
        self.didSend = didSend
    }
}

public struct KmsgEmbeddedChat: Sendable {
    public let title: String
    public let chatID: String?
    public let lastMessage: String?
    public let listIndex: Int

    public init(title: String, chatID: String?, lastMessage: String?, listIndex: Int) {
        self.title = title
        self.chatID = chatID
        self.lastMessage = lastMessage
        self.listIndex = listIndex
    }
}

public final class KmsgCancellationToken: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false

    public init() {}

    public var isCancelled: Bool {
        lock.lock()
        defer { lock.unlock() }
        return cancelled
    }

    public func cancel() {
        lock.lock()
        cancelled = true
        lock.unlock()
    }
}

public enum KmsgEmbeddedError: LocalizedError {
    case accessibilityDenied
    case chatListUnavailable
    case roomTitleUnavailable
    case roomTitleMismatch
    case composerNotUnique(Int)
    case composerFocusFailed
    case messageNotReflected
    case enterNotEffective
    case attachmentMissing(String)
    case attachmentPreviewNotFound(String)
    case attachmentSendControlNotFound(String)
    case attachmentUploadTimedOut(String)
    case cancelled

    public var errorDescription: String? {
        switch self {
        case .accessibilityDenied:
            "공지발송 앱의 손쉬운 사용 권한이 없습니다."
        case .chatListUnavailable:
            "카카오톡 채팅 목록을 읽지 못했습니다. 카카오톡을 실행하고 ‘채팅’ 탭을 열어주세요."
        case .roomTitleUnavailable:
            "열린 카카오톡 방 제목을 읽을 수 없습니다."
        case .roomTitleMismatch:
            "열린 카카오톡 방 제목이 요청한 정확한 이름과 다릅니다."
        case .composerNotUnique(let count):
            "카카오톡 메시지 입력창을 정확히 하나로 특정하지 못했습니다(\(count)개)."
        case .composerFocusFailed:
            "검증된 메시지 입력창에 포커스를 둘 수 없습니다."
        case .messageNotReflected:
            "메시지가 입력창에 문자 단위로 반영되었는지 확인하지 못했습니다."
        case .enterNotEffective:
            "Enter 입력 후 메시지 입력창의 변화를 확인하지 못했습니다."
        case .attachmentMissing(let path):
            "첨부파일이 사라졌거나 읽을 수 없습니다: \(path)"
        case .attachmentPreviewNotFound(let name):
            "KakaoTalk에서 첨부파일 미리보기를 확인하지 못했습니다: \(name)"
        case .attachmentSendControlNotFound(let name):
            "KakaoTalk에서 첨부파일 전송 동작을 확인하지 못했습니다: \(name)"
        case .attachmentUploadTimedOut(let name):
            "첨부파일 업로드 완료를 확인하지 못했습니다. 채팅창을 닫지 않았습니다: \(name)"
        case .cancelled:
            "사용자가 작업을 중지했습니다."
        }
    }
}

/// A narrow, fail-closed bridge around kmsg's AX resolver.
///
/// NoticeSender deliberately exposes none of kmsg's fuzzy matching or forced
/// typing behavior. The bridge requires an exact normalized title, a unique
/// search result and one identifiable composer.
public enum KmsgEmbeddedEngine {
    public static let upstreamCommit = "fb70208286a1da3a404861dc944db470176155f6"
    public static let upstreamVersion = "1.260705.0"

    public static func verify(
        roomName: String,
        chatID: String? = nil,
        cancellationToken: KmsgCancellationToken? = nil
    ) throws -> KmsgEmbeddedResult {
        try execute(roomName: roomName, chatID: chatID, message: nil, cancellationToken: cancellationToken)
    }

    public static func send(
        roomName: String,
        chatID: String? = nil,
        message: String,
        cancellationToken: KmsgCancellationToken? = nil
    ) throws -> KmsgEmbeddedResult {
        try execute(
            roomName: roomName,
            chatID: chatID,
            messages: [message],
            attachmentPaths: [],
            cancellationToken: cancellationToken
        )
    }

    public static func send(
        roomName: String,
        chatID: String? = nil,
        messages: [String],
        attachmentPaths: [String] = [],
        cancellationToken: KmsgCancellationToken? = nil
    ) throws -> KmsgEmbeddedResult {
        try execute(
            roomName: roomName,
            chatID: chatID,
            messages: messages,
            attachmentPaths: attachmentPaths,
            cancellationToken: cancellationToken
        )
    }

    public static func listChats(limit: Int = 1_000) throws -> [KmsgEmbeddedChat] {
        // Do not re-check trust on this detached worker. The app's diagnostic
        // performs the user-facing permission check on the main actor, while
        // AX itself still enforces access for every operation below. Repeating
        // AXIsProcessTrusted() here can return a stale false immediately after
        // the user grants access and incorrectly block the DB sync.
        let kakao = try KakaoTalkApp()
        _ = kakao.ensureWindowReopened(timeout: 3.0)
        guard let initialWindow = kakao.chatListWindow ?? kakao.ensureMainWindow(timeout: 5.0),
              let window = kakao.openChatListTab(fallbackWindow: initialWindow)
        else {
            throw KmsgEmbeddedError.chatListUnavailable
        }
        let boundedLimit = max(1, min(limit, 1_000))
        let snapshots = ChatListScanner().scan(in: window, limit: boundedLimit)
        guard !snapshots.isEmpty else { throw KmsgEmbeddedError.chatListUnavailable }
        let assignedIDs = ChatIdentityRegistryStore.shared.assignChatIDs(for: snapshots.map(\.discovery))
        return zip(snapshots, assignedIDs).enumerated().map { index, pair in
            let (snapshot, chatID) = pair
            return KmsgEmbeddedChat(
                title: snapshot.discovery.title,
                chatID: chatID.isEmpty ? nil : chatID,
                lastMessage: snapshot.discovery.lastMessage,
                listIndex: index
            )
        }
    }

    public static func normalizeRoomName(_ value: String) -> String {
        // Safety-critical room matching preserves spaces, punctuation, symbols
        // and case. NFC is the only normalization so visually identical Hangul
        // composed with different Unicode scalar sequences still compares equal.
        value.precomposedStringWithCanonicalMapping
    }

    private static func execute(
        roomName: String,
        chatID: String?,
        message: String?,
        cancellationToken: KmsgCancellationToken?
    ) throws -> KmsgEmbeddedResult {
        try execute(
            roomName: roomName,
            chatID: chatID,
            messages: message.map { [$0] },
            attachmentPaths: [],
            cancellationToken: cancellationToken
        )
    }

    private static func execute(
        roomName: String,
        chatID: String?,
        messages: [String]?,
        attachmentPaths: [String],
        cancellationToken: KmsgCancellationToken?
    ) throws -> KmsgEmbeddedResult {
        try throwIfCancelled(cancellationToken)
        let attachmentURLs = try validateAttachmentPaths(attachmentPaths)
        let kakao = try KakaoTalkApp()
        let runner = AXActionRunner(
            traceEnabled: ProcessInfo.processInfo.environment["NOTICE_SENDER_AX_TRACE"] == "1",
            cancellationToken: cancellationToken,
            targetProcessIdentifier: kakao.processIdentifier
        )
        let resolver = ChatWindowResolver(
            kakao: kakao,
            runner: runner,
            useCache: false,
            deepRecoveryEnabled: false,
            layoutMode: .preserve,
            interactionMode: .allowUIAutomation,
            exactMatchOnly: true,
            requireUniqueMatch: true
        )
        let resolution: ChatWindowResolution
        if let chatID, !chatID.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            resolution = try resolver.resolve(chatID: chatID)
        } else {
            resolution = try resolver.resolve(query: roomName)
        }
        var mayCloseTransientWindow = attachmentURLs.isEmpty
        defer {
            // A batch must not accumulate one KakaoTalk window per student.
            // Closing only the exact window opened by this resolution keeps the
            // next search independent from stale focus and old search state.
            //
            // Once a file paste starts, a timeout, cancellation or alert must
            // leave the window open. Closing an uploading window can cancel the
            // transfer or cause KakaoTalk to present a separate warning.
            if resolution.openedTransiently, mayCloseTransientWindow {
                _ = resolver.closeWindow(resolution.window)
            }
        }
        try throwIfCancelled(cancellationToken)
        guard let actualTitle = resolution.window.title else {
            throw KmsgEmbeddedError.roomTitleUnavailable
        }
        guard normalizeRoomName(actualTitle) == normalizeRoomName(roomName) else {
            throw KmsgEmbeddedError.roomTitleMismatch
        }

        let composers = messageInputs(
            in: resolution.window,
            application: kakao.applicationElement
        )
        guard composers.count == 1, let composer = composers.first else {
            throw KmsgEmbeddedError.composerNotUnique(composers.count)
        }
        guard messages != nil || !attachmentURLs.isEmpty else {
            return KmsgEmbeddedResult(roomTitle: actualTitle, didSend: false)
        }

        kakao.activate()
        for message in messages ?? [] {
            try throwIfCancelled(cancellationToken)
            guard runner.focusWithVerification(composer, label: "notice sender composer", attempts: 1) else {
                try throwIfCancelled(cancellationToken)
                throw KmsgEmbeddedError.composerFocusFailed
            }
            try throwIfCancelled(cancellationToken)
            guard runner.setTextWithVerification(message, on: composer, label: "notice sender message", attempts: 1) else {
                try throwIfCancelled(cancellationToken)
                throw KmsgEmbeddedError.messageNotReflected
            }
            guard composer.stringValue == message else {
                throw KmsgEmbeddedError.messageNotReflected
            }
            if cancellationToken?.isCancelled == true {
                try? composer.setAttribute(kAXValueAttribute, value: "" as CFString)
                throw KmsgEmbeddedError.cancelled
            }
            guard runner.pressEnterWithVerification(
                on: composer,
                label: "notice sender message",
                attempts: 1,
                reflectionTimeout: 0.4,
                retryDelay: 0.08
            ) else {
                try throwIfCancelled(cancellationToken)
                throw KmsgEmbeddedError.enterNotEffective
            }
        }

        for attachmentURL in attachmentURLs {
            try throwIfCancelled(cancellationToken)
            try sendAttachment(
                attachmentURL,
                in: resolution.window,
                composer: composer,
                application: kakao.applicationElement,
                kakao: kakao,
                runner: runner,
                cancellationToken: cancellationToken
            )
        }

        // Every attachment was observed in the transcript after its upload
        // indicators disappeared. The transient room can now be closed safely.
        mayCloseTransientWindow = true
        return KmsgEmbeddedResult(
            roomTitle: actualTitle,
            didSend: !(messages ?? []).isEmpty || !attachmentURLs.isEmpty
        )
    }

    private static func validateAttachmentPaths(_ paths: [String]) throws -> [URL] {
        try paths.map { path in
            var isDirectory: ObjCBool = false
            guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory),
                  !isDirectory.boolValue,
                  FileManager.default.isReadableFile(atPath: path)
            else {
                throw KmsgEmbeddedError.attachmentMissing(path)
            }
            return URL(fileURLWithPath: path)
        }
    }

    private static func sendAttachment(
        _ fileURL: URL,
        in window: UIElement,
        composer: UIElement,
        application: UIElement,
        kakao: KakaoTalkApp,
        runner: AXActionRunner,
        cancellationToken: KmsgCancellationToken?
    ) throws {
        let filename = fileURL.lastPathComponent
        guard let transcriptTable = transcriptTable(in: window) else {
            throw KmsgEmbeddedError.attachmentPreviewNotFound(filename)
        }
        let rowsBeforePaste = transcriptRows(in: transcriptTable).count
        let filenameCountBeforePaste = attachmentFilenameCount(
            filename,
            in: transcriptTable
        )

        let writeFileURLToPasteboard = {
            let pasteboard = NSPasteboard.general
            pasteboard.clearContents()
            return pasteboard.writeObjects([fileURL as NSURL])
        }
        let copied = Thread.isMainThread
            ? writeFileURLToPasteboard()
            : DispatchQueue.main.sync(execute: writeFileURLToPasteboard)
        guard copied else {
            throw KmsgEmbeddedError.attachmentMissing(fileURL.path)
        }

        kakao.activate()
        guard runner.focusWithVerification(composer, label: "attachment composer", attempts: 1) else {
            throw KmsgEmbeddedError.composerFocusFailed
        }
        runner.pressPaste()
        runner.log("attachment: pasted file URL '\(filename)'")

        var previewSurface: UIElement?
        let previewFound = runner.waitUntil(
            label: "attachment preview \(filename)",
            timeout: 5.0,
            pollInterval: 0.1
        ) {
            if cancellationToken?.isCancelled == true { return false }
            previewSurface = attachmentPreviewSurface(
                filename: filename,
                window: window,
                application: application
            )
            return previewSurface != nil
        }
        guard previewFound, let previewSurface else {
            try throwIfCancelled(cancellationToken)
            throw KmsgEmbeddedError.attachmentPreviewNotFound(filename)
        }

        if let sendButton = attachmentSendButton(in: previewSurface) {
            if !runner.clickWithRetry(sendButton, label: "attachment send button", attempts: 2) {
                // KakaoTalk can rebuild the top-level sheet between discovery
                // and AXPress, invalidating the button object. The sheet itself
                // remains the active focused surface, so Enter is the stable
                // semantic fallback used by KakaoTalk's own default action.
                if attachmentPreviewSurface(
                    filename: filename,
                    window: window,
                    application: application
                ) != nil {
                    runner.pressEnterKey()
                    runner.log("attachment: send button invalidated; sent Enter to active preview")
                } else {
                    // AXPress can return an error after the press was accepted
                    // because KakaoTalk immediately destroys the sheet/button.
                    runner.log("attachment: preview vanished during AXPress; checking transcript completion")
                }
            }
        } else {
            // Some KakaoTalk versions expose the preview without an actionable
            // AX button. Enter is sent to the active semantic preview.
            runner.pressEnterKey()
            runner.log("attachment: send button absent; sent Enter to active preview")
        }

        let completed = runner.waitUntil(
            label: "attachment upload completion \(filename)",
            timeout: attachmentUploadTimeout(for: fileURL),
            pollInterval: 0.15,
            evaluateAfterTimeout: false
        ) {
            if cancellationToken?.isCancelled == true { return false }
            let previewGone = !isAttachmentPreviewStillOpen(
                previewSurface,
                window: window,
                application: application
            )
            let countNow = attachmentFilenameCount(filename, in: transcriptTable)
            let rowCountNow = transcriptRows(in: transcriptTable).count
            let appendedToTranscript = rowCountNow > rowsBeforePaste
                || countNow > filenameCountBeforePaste
            return previewGone
                && appendedToTranscript
                && attachmentTranscriptEntryIsComplete(
                    filename: filename,
                    in: transcriptTable
                )
        }
        guard completed else {
            try throwIfCancelled(cancellationToken)
            throw KmsgEmbeddedError.attachmentUploadTimedOut(filename)
        }
        runner.log("attachment: upload completed and transcript entry verified for '\(filename)'")
    }

    private static func attachmentPreviewSurface(
        filename: String,
        window: UIElement,
        application: UIElement
    ) -> UIElement? {
        var surfaces: [UIElement] = []
        if let sheets: [AXUIElement] = window.attributeOptional(kAXSheetsAttribute) {
            surfaces.append(contentsOf: sheets.map(UIElement.init))
        }
        if let matchingSheet = surfaces.first(where: { surface in
            elementTreeContains(filename: filename, root: surface, maxNodes: 1_200)
        }) {
            return matchingSheet
        }

        // KakaoTalk 26.6 exposes the file picker as a top-level AXSheet whose
        // focused element is its table, not as an AXSheet of the chat window.
        if let focused = application.focusedUIElement {
            var cursor: UIElement? = focused
            var hops = 0
            while let current = cursor, hops < 12 {
                if (current.role == kAXSheetRole || current.role == "AXDialog"),
                   elementTreeContains(filename: filename, root: current, maxNodes: 400) {
                    return current
                }
                cursor = current.parent
                hops += 1
            }
        }
        return application.findAll(where: { element in
            element.role == kAXSheetRole || element.role == "AXDialog"
        }, limit: 4, maxNodes: 400).first { candidate in
            elementTreeContains(filename: filename, root: candidate, maxNodes: 400)
        }
    }

    private static func attachmentSendButton(in surface: UIElement) -> UIElement? {
        surface.findAll(role: kAXButtonRole, limit: 30, maxNodes: 600).first { button in
            let values = [
                button.title ?? "",
                button.stringValue ?? "",
                button.axDescription ?? "",
                button.helpText ?? "",
            ].map(canonicalAttachmentText)
            return values.contains { value in
                value == "보내기"
                    || value == "send"
                    || value.hasSuffix("전송")
                    || value.hasPrefix("send ")
            }
        }
    }

    private static func attachmentFilenameCount(_ filename: String, in table: UIElement) -> Int {
        let expected = canonicalAttachmentText(filename)
        guard !expected.isEmpty else { return 0 }
        return recentTranscriptRows(in: table).reduce(into: 0) { count, row in
            count += matchingFilenameNodeCount(expected: expected, in: row)
        }
    }

    private static func elementTreeContains(
        filename: String,
        root: UIElement,
        maxNodes: Int
    ) -> Bool {
        let expected = canonicalAttachmentText(filename)
        if semanticAttachmentTexts(of: root).contains(where: {
            canonicalAttachmentText($0).contains(expected)
        }) {
            return true
        }
        return !root.findAll(where: { element in
            semanticAttachmentTexts(of: element).contains { text in
                canonicalAttachmentText(text).contains(expected)
            }
        }, limit: 1, maxNodes: maxNodes).isEmpty
    }

    private static func semanticAttachmentTexts(of element: UIElement) -> [String] {
        [
            element.title,
            element.stringValue,
            element.axDescription,
            element.helpText,
            element.identifier,
        ].compactMap { $0 }
    }

    private static func canonicalAttachmentText(_ value: String) -> String {
        value
            .precomposedStringWithCanonicalMapping
            .folding(options: [.diacriticInsensitive, .widthInsensitive], locale: .current)
            .lowercased()
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func matchingFilenameNodeCount(expected: String, in root: UIElement) -> Int {
        let rootCount = semanticAttachmentTexts(of: root).contains {
            canonicalAttachmentText($0).contains(expected)
        } ? 1 : 0
        return rootCount + root.findAll(where: { element in
            semanticAttachmentTexts(of: element).contains { text in
                canonicalAttachmentText(text).contains(expected)
            }
        }, limit: 50, maxNodes: 350).count
    }

    private static func transcriptTable(in window: UIElement) -> UIElement? {
        window.findAll(role: kAXTableRole, limit: 3, maxNodes: 120).max {
            transcriptRows(in: $0).count < transcriptRows(in: $1).count
        }
    }

    private static func transcriptRows(in table: UIElement) -> [UIElement] {
        table.children.filter { $0.role == kAXRowRole }
    }

    private static func recentTranscriptRows(in table: UIElement) -> [UIElement] {
        Array(transcriptRows(in: table).suffix(6))
    }

    private static func isAttachmentPreviewStillOpen(
        _ preview: UIElement,
        window: UIElement,
        application: UIElement
    ) -> Bool {
        guard preview.role != nil else { return false }
        if let sheets: [AXUIElement] = window.attributeOptional(kAXSheetsAttribute),
           sheets.contains(where: { CFEqual($0, preview.axElement) }) {
            return true
        }
        if let focused = application.focusedUIElement {
            var cursor: UIElement? = focused
            var hops = 0
            while let current = cursor, hops < 12 {
                if CFEqual(current.axElement, preview.axElement) { return true }
                cursor = current.parent
                hops += 1
            }
        }
        return preview.role == kAXSheetRole || preview.role == "AXDialog"
    }

    private static func attachmentTranscriptEntryIsComplete(
        filename: String,
        in table: UIElement
    ) -> Bool {
        let expected = canonicalAttachmentText(filename)
        let matchingRows = recentTranscriptRows(in: table).filter { row in
            matchingFilenameNodeCount(expected: expected, in: row) > 0
        }
        guard let newestRow = matchingRows.last else { return false }

        let nodes = [newestRow] + newestRow.findAll(where: { _ in true }, limit: 120, maxNodes: 350)
        let metadata = canonicalAttachmentText(
            nodes.flatMap(semanticAttachmentTexts).joined(separator: " ")
        )
        let hasBusyIndicator = nodes.contains { $0.role == kAXProgressIndicatorRole }
            || ["업로드 중", "전송 중", "파일 전송", "uploading", "sending file"].contains {
                metadata.contains($0)
            }
        guard !hasBusyIndicator else { return false }

        let readyActions = ["열기", "finder에서 보기", "open", "show in finder"]
        return readyActions.contains { metadata.contains($0) }
    }

    private static func attachmentUploadTimeout(for fileURL: URL) -> TimeInterval {
        let values = try? fileURL.resourceValues(forKeys: [.fileSizeKey])
        let sizeInMB = Double(values?.fileSize ?? 0) / 1_000_000
        return min(max(120, 30 + sizeInMB * 8), 600)
    }

    private static func throwIfCancelled(_ token: KmsgCancellationToken?) throws {
        if token?.isCancelled == true {
            throw KmsgEmbeddedError.cancelled
        }
    }

    private static func messageInputs(in window: UIElement, application: UIElement) -> [UIElement] {
        // KakaoTalk 26.6 exposes the composer as:
        // AXTextArea, AXDescription="메시지 입력", parent AXWindow=<exact room>.
        // AXEnabled is absent on this element, so absence must not mean disabled.
        func isSemanticComposer(_ element: UIElement) -> Bool {
            let role = element.role ?? ""
            guard role == kAXTextAreaRole || role == kAXTextFieldRole else {
                return false
            }
            let explicitlyEnabled: Bool? = element.attributeOptional(kAXEnabledAttribute)
            guard explicitlyEnabled != false else { return false }
            guard belongsToWindow(element, window: window) else { return false }

            let metadata = [
                element.identifier ?? "",
                element.title ?? "",
                element.axDescription ?? "",
                element.helpText ?? "",
            ].joined(separator: " ").lowercased()
            return metadata.contains("메시지 입력")
                || metadata.contains("message input")
                || metadata.contains("message composer")
        }

        func discoverCandidates(in root: UIElement, maxNodes: Int) -> [UIElement] {
            root.findAll(where: { element in
                isSemanticComposer(element)
            }, limit: 8, maxNodes: maxNodes)
        }

        // KakaoTalk focuses the composer after an exact room opens. Resolve that
        // short semantic lineage first instead of traversing a long transcript.
        var candidates: [UIElement] = []
        if let focused = application.focusedUIElement {
            var cursor: UIElement? = focused
            var hops = 0
            while let current = cursor, hops < 12 {
                if isSemanticComposer(current) {
                    candidates.append(current)
                }
                candidates.append(contentsOf: discoverCandidates(in: current, maxNodes: 80))
                if current.role == kAXWindowRole { break }
                cursor = current.parent
                hops += 1
            }
        }

        if candidates.isEmpty {
            candidates = discoverCandidates(in: window, maxNodes: 1_200)
        }
        if candidates.isEmpty {
            candidates = discoverCandidates(in: application, maxNodes: 2_400)
        }
        var unique: [UIElement] = []
        for candidate in candidates where !unique.contains(where: {
            CFEqual($0.axElement, candidate.axElement)
        }) {
            unique.append(candidate)
        }
        return unique
    }

    private static func belongsToWindow(_ element: UIElement, window: UIElement) -> Bool {
        var cursor: UIElement? = element
        var hops = 0
        while let current = cursor, hops < 16 {
            if CFEqual(current.axElement, window.axElement) {
                return true
            }
            if current.role == kAXWindowRole {
                return false
            }
            cursor = current.parent
            hops += 1
        }
        return false
    }
}
