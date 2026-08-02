import ApplicationServices.HIServices
import Foundation

enum ChatWindowLayoutMode: String {
    case preserve
    case left
    case right
    case splitLeft = "split-left"
    case splitRight = "split-right"

    var isRightAligned: Bool {
        self == .right || self == .splitRight
    }

    var isSplit: Bool {
        self == .splitLeft || self == .splitRight
    }
}

enum ChatWindowResolutionMethod {
    case existingWindow
    case openedViaChatList
    case openedViaSearch
}

enum ChatWindowInteractionMode {
    case allowUIAutomation
    case backgroundSafe
}

struct ChatWindowResolution {
    let window: UIElement
    let method: ChatWindowResolutionMethod

    var openedViaSearch: Bool {
        method == .openedViaSearch
    }

    var openedTransiently: Bool {
        method != .existingWindow
    }
}

private enum ChatWindowFailureCode: String {
    case backgroundSafeBlocked = "BACKGROUND_SAFE_BLOCKED"
    case focusFail = "FOCUS_FAIL"
    case inputNotReflected = "INPUT_NOT_REFLECTED"
    case windowNotReady = "WINDOW_NOT_READY"
    case searchMiss = "SEARCH_MISS"
}

private struct SearchScanProfile {
    let label: String
    let timeout: TimeInterval
    let pollInterval: TimeInterval
    let rowLimit: Int
    let cellLimit: Int
    let supplementalLimit: Int
    let candidateNodeBudget: Int
    let textLimit: Int
    let textNodeBudget: Int
    let includeSupplementalRoles: Bool
    let includeApplicationRoot: Bool
}

private struct SearchCandidate {
    let element: UIElement
    let textScore: Int
    let matchedText: String
}

struct ChatWindowResolver {
    private let kakao: KakaoTalkApp
    private let runner: AXActionRunner
    private let useCache: Bool
    private let deepRecoveryEnabled: Bool
    private let layoutMode: ChatWindowLayoutMode
    private let interactionMode: ChatWindowInteractionMode
    private let exactMatchOnly: Bool
    private let requireUniqueMatch: Bool

    init(
        kakao: KakaoTalkApp,
        runner: AXActionRunner,
        useCache: Bool = true,
        deepRecoveryEnabled: Bool = false,
        layoutMode: ChatWindowLayoutMode = .preserve,
        interactionMode: ChatWindowInteractionMode = .allowUIAutomation,
        exactMatchOnly: Bool = false,
        requireUniqueMatch: Bool = false
    ) {
        self.kakao = kakao
        self.runner = runner
        self.useCache = useCache
        self.deepRecoveryEnabled = deepRecoveryEnabled
        self.layoutMode = layoutMode
        self.interactionMode = interactionMode
        self.exactMatchOnly = exactMatchOnly
        self.requireUniqueMatch = requireUniqueMatch
    }

    func resolve(query: String) throws -> ChatWindowResolution {
        try ensureNotCancelled()
        if interactionMode == .backgroundSafe {
            return try resolveExistingWindowOnly(query: query)
        }

        let usableWindow = try requireUsableWindow()
        try ensureNotCancelled()

        // A visible window title cannot prove that only one room with the same
        // name exists. In unique-match mode always go through search so the
        // complete exposed result set is counted before opening anything.
        if !requireUniqueMatch {
            let existingWindows = matchingChatWindows(in: kakao.windows, query: query)
            if let existingWindow = existingWindows.first {
                standardizeReadableWindow(existingWindow, label: "existing chat window")
                return ChatWindowResolution(window: existingWindow, method: .existingWindow)
            }
        }

        let searchWindow = selectSearchWindow(fallback: usableWindow)
        standardizeReadableWindow(searchWindow, label: "search root window")
        try ensureNotCancelled()
        let chatWindow = try openChatViaSearch(query: query, in: searchWindow, fallbackWindow: usableWindow)
        standardizeReadableWindow(chatWindow, label: "opened chat window")
        return ChatWindowResolution(window: chatWindow, method: .openedViaSearch)
    }

    func resolve(chatID: String) throws -> ChatWindowResolution {
        try ensureNotCancelled()
        guard let record = ChatIdentityRegistryStore.shared.record(for: chatID) else {
            throw KakaoTalkError.elementNotFound("Unknown chat_id '\(chatID)'. Run 'kmsg chats' first to refresh the local registry.")
        }

        if interactionMode == .backgroundSafe {
            return try resolveExistingWindowOnly(query: record.displayName)
        }

        let usableWindow = try requireUsableWindow()
        try ensureNotCancelled()
        let query = record.displayName

        // A title-only existing-window match is unsafe for a selected chat_id:
        // another room may expose the same visible title. Strict callers always
        // re-open the registry-matched chat-list row.
        if !requireUniqueMatch, let existingWindow = findMatchingChatWindow(in: kakao.windows, query: query) {
            standardizeReadableWindow(existingWindow, label: "existing chat window")
            return ChatWindowResolution(window: existingWindow, method: .existingWindow)
        }

        if !requireUniqueMatch,
           let chatListWindow = kakao.chatListWindow,
           let chatWindow = openChatListRow(chatID: chatID, query: query, in: chatListWindow, fallbackWindow: usableWindow)
        {
            standardizeReadableWindow(chatWindow, label: "opened chat window")
            return ChatWindowResolution(window: chatWindow, method: .openedViaChatList)
        }

        // chat_id registry positions can become stale whenever KakaoTalk reorders
        // or virtualizes the recent-chat list. Fall back to the same exact and
        // unique title search instead of failing at the list-row lookup.
        runner.log("chat_id: exact row unavailable; falling back to exact search for '\(query)'")
        let searchWindow = selectSearchWindow(fallback: usableWindow)
        standardizeReadableWindow(searchWindow, label: "search root window")
        try ensureNotCancelled()
        let chatWindow = try openChatViaSearch(query: query, in: searchWindow, fallbackWindow: usableWindow)
        standardizeReadableWindow(chatWindow, label: "opened chat window")
        return ChatWindowResolution(window: chatWindow, method: .openedViaSearch)
    }

    @discardableResult
    func closeWindow(_ window: UIElement) -> Bool {
        let closeAction = "AXClose"

        kakao.activate()
        _ = tryRaiseWindow(window)

        if supportsAction(closeAction, on: window) {
            do {
                try window.performAction(closeAction)
                if waitForWindowClosed(window, label: "close via AXClose") {
                    return true
                }
            } catch {
                runner.log("close window: AXClose failed (\(error))")
            }
        }

        if let closeButton = findCloseButton(in: window) {
            do {
                try closeButton.press()
                if waitForWindowClosed(window, label: "close via button") {
                    return true
                }
            } catch {
                runner.log("close window: button press failed (\(error))")
            }
        }

        runner.log("close window: fallback via cmd+w")
        runner.pressCommandW()
        return waitForWindowClosed(window, label: "close via cmd+w")
    }

    private func resolveExistingWindowOnly(query: String) throws -> ChatWindowResolution {
        if let existingWindow = findMatchingChatWindow(in: kakao.windows, query: query) {
            runner.log("background-safe: matched already exposed chat window")
            return ChatWindowResolution(window: existingWindow, method: .existingWindow)
        }

        if let focusedWindow = kakao.focusedWindow,
           let title = focusedWindow.title,
           scoreQueryMatch(query: query, candidateText: title) > 0
        {
            runner.log("background-safe: matched already focused chat window")
            return ChatWindowResolution(window: focusedWindow, method: .existingWindow)
        }

        throw KakaoTalkError.elementNotFound(
            "[\(ChatWindowFailureCode.backgroundSafeBlocked.rawValue)] No already exposed chat window matched '\(query)'. " +
            "Background-safe mode does not activate KakaoTalk, open chat rows, search, resize, or close windows."
        )
    }

    private func requireUsableWindow() throws -> UIElement {
        if let immediateWindow = kakao.focusedWindow ?? kakao.mainWindow ?? kakao.windows.first {
            runner.log("Usable window found via immediate probe")
            return immediateWindow
        }

        if let usableWindow = kakao.ensureMainWindow(timeout: 0.9, mode: .fast, trace: { message in
            runner.log(message)
        }) {
            return usableWindow
        }

        runner.log("window fast path failed; attempting one-shot open defense")
        if let usableWindow = attemptQuickOpenDefense(forceOpenEvenIfWindowPresent: !deepRecoveryEnabled) {
            return usableWindow
        }

        guard deepRecoveryEnabled else {
            runner.log("window fast path failed; deep recovery disabled")
            throw KakaoTalkError.actionFailed("[\(ChatWindowFailureCode.windowNotReady.rawValue)] Usable KakaoTalk window unavailable (fast mode)")
        }

        runner.log("window: escalating to full recovery (3.0s)")
        if let usableWindow = kakao.ensureMainWindow(timeout: 3.0, mode: .recovery, trace: { message in
            runner.log(message)
        }) {
            return usableWindow
        }

        throw KakaoTalkError.actionFailed("[\(ChatWindowFailureCode.windowNotReady.rawValue)] Usable KakaoTalk window unavailable")
    }

    private func attemptQuickOpenDefense(forceOpenEvenIfWindowPresent: Bool) -> UIElement? {
        runner.log("window: quick-open defense start")

        let hasVisibleWindow = kakao.focusedWindow != nil || kakao.mainWindow != nil || !kakao.windows.isEmpty
        if forceOpenEvenIfWindowPresent || !hasVisibleWindow {
            if KakaoTalkApp.isRunning {
                if hasVisibleWindow && forceOpenEvenIfWindowPresent {
                    runner.log("window: forcing open /Applications/KakaoTalk.app (fast-mode fallback)")
                } else {
                    runner.log("window: no visible windows; forcing open /Applications/KakaoTalk.app")
                }
                _ = KakaoTalkApp.forceOpen(timeout: 0.8)
            } else {
                runner.log("window: KakaoTalk not running; launching")
                _ = KakaoTalkApp.launch(timeout: 0.8)
            }
        } else {
            runner.log("window: quick-open defense skipped (windows already present)")
        }

        kakao.activate()
        if let usableWindow = kakao.ensureMainWindow(timeout: 0.8, mode: .fast, trace: { message in
            runner.log(message)
        }) {
            runner.log("window: quick-open defense succeeded")
            return usableWindow
        }

        runner.log("window: quick-open defense failed")
        return nil
    }

    private func selectSearchWindow(fallback: UIElement) -> UIElement {
        if let chatListWindow = kakao.chatListWindow {
            runner.log("search root selected: chatListWindow")
            return chatListWindow
        }
        if let mainWindow = kakao.mainWindow {
            runner.log("search root selected: mainWindow")
            return mainWindow
        }
        runner.log("search root selected: fallback usable window")
        return fallback
    }

    private func openChatViaSearch(query: String, in rootWindow: UIElement, fallbackWindow: UIElement) throws -> UIElement {
        try ensureNotCancelled()
        runner.log("search: locating search field")

        guard let searchField = locateSearchField(in: rootWindow) else {
            throw KakaoTalkError.elementNotFound("[\(ChatWindowFailureCode.searchMiss.rawValue)] Search field not found")
        }

        guard runner.focusWithVerification(searchField, label: "search field", attempts: 1) else {
            try ensureNotCancelled()
            throw KakaoTalkError.actionFailed("[\(ChatWindowFailureCode.focusFail.rawValue)] Could not focus search field")
        }

        try ensureNotCancelled()
        _ = runner.setTextWithVerification("", on: searchField, label: "search field clear", attempts: 1)

        let searchInputReady =
            runner.setTextWithVerification(query, on: searchField, label: "search field input", attempts: 1) ||
            runner.typeTextWithVerification(query, on: searchField, label: "search field input", attempts: 2)

        guard searchInputReady else {
            try ensureNotCancelled()
            runner.pressEscape()
            throw KakaoTalkError.actionFailed("[\(ChatWindowFailureCode.inputNotReflected.rawValue)] Search keyword was not entered")
        }

        try ensureNotCancelled()
        let matchingCandidates = waitForMatchingSearchResults(query: query, rootWindow: rootWindow)
        try ensureNotCancelled()
        if requireUniqueMatch, matchingCandidates.count != 1 {
            runner.pressEscape()
            throw KakaoTalkError.actionFailed(
                "[AMBIGUOUS_MATCH] Expected exactly one search result for '\(query)', found \(matchingCandidates.count)"
            )
        }
        guard let matchingResult = pickBestSearchResult(from: matchingCandidates) else {
            runner.pressEscape()
            throw KakaoTalkError.elementNotFound("[\(ChatWindowFailureCode.searchMiss.rawValue)] No search result found for '\(query)'")
        }

        let openTriggered = triggerSearchResultOpen(
            matchingResult,
            searchField: searchField,
            query: query
        ) {
            resolveOpenedChatWindowFast(query: query) != nil
        }
        try ensureNotCancelled()
        guard openTriggered else {
            runner.pressEscape()
            throw KakaoTalkError.actionFailed("[\(ChatWindowFailureCode.searchMiss.rawValue)] Could not open matched search result")
        }

        if let window = waitForOpenedChatWindow(query: query, fallbackWindow: fallbackWindow) {
            return window
        }

        throw KakaoTalkError.windowNotFound("[\(ChatWindowFailureCode.windowNotReady.rawValue)] Chat window for '\(query)' did not open")
    }

    private func openChatListRow(chatID: String, query: String, in chatListWindow: UIElement, fallbackWindow: UIElement) -> UIElement? {
        runner.log("chat_id: scanning chat list rows")
        standardizeReadableWindow(chatListWindow, label: "chat list window")
        let registry = ChatIdentityRegistryStore.shared
        let lastSeenIndex = registry.record(for: chatID)?.lastSeenIndex ?? 199
        let scanLimit = min(max(lastSeenIndex + 100, 200), 2_000)
        let scanner = ChatListScanner()
        let snapshots = scanner.scan(in: chatListWindow, limit: scanLimit, trace: { message in
            runner.log(message)
        })
        guard !snapshots.isEmpty else {
            runner.log("chat_id: chat list scan returned no rows")
            return nil
        }

        let assignedIDs = registry.assignChatIDs(for: snapshots.map(\.discovery))
        guard let matchIndex = assignedIDs.firstIndex(of: chatID) else {
            runner.log("chat_id: no visible chat row matched \(chatID)")
            return nil
        }

        let row = snapshots[matchIndex].element
        runner.log("chat_id: matched row title='\(snapshots[matchIndex].discovery.title)'")
        kakao.activate()
        _ = tryRaiseWindow(chatListWindow)

        if triggerChatListRowOpen(row) {
            if let window = waitForOpenedChatWindow(query: query, fallbackWindow: fallbackWindow) {
                return window
            }
        }

        runner.log("chat_id: matched row did not open a chat window")
        return nil
    }

    private func triggerChatListRowOpen(_ row: UIElement) -> Bool {
        if tryActivateSearchResult(row, label: "chat list row") {
            return true
        }

        let selected = trySelectSearchResult(row, label: "chat list row")
        if !selected, let parent = row.parent, trySelectSearchResult(parent, label: "chat list row.parent") {
            runner.pressEnterKey()
            return true
        }

        if selected {
            runner.pressEnterKey()
            return true
        }

        return false
    }

    private func standardizeReadableWindow(_ window: UIElement, label: String) {
        guard interactionMode != .backgroundSafe else {
            runner.log("\(label): background-safe mode; preserving window focus, size, and position")
            return
        }

        kakao.activate()
        _ = tryRaiseWindow(window)
        if layoutMode != .preserve {
            runner.log("\(label): geometry-independent mode ignores requested window layout")
        }
    }

    private func resolveCachedElement(
        slot: AXPathSlot,
        root: UIElement,
        validate: (UIElement) -> Bool
    ) -> UIElement? {
        guard useCache else { return nil }
        return AXPathCacheStore.shared.resolve(
            slot: slot,
            root: root,
            validate: validate,
            trace: { message in
                runner.log(message)
            }
        )
    }

    private func rememberCachedElement(slot: AXPathSlot, root: UIElement, element: UIElement) {
        guard useCache else { return }
        AXPathCacheStore.shared.remember(
            slot: slot,
            root: root,
            element: element,
            trace: { message in
                runner.log(message)
            }
        )
    }

    private func locateSearchField(in rootWindow: UIElement) -> UIElement? {
        if let cachedSearchField = resolveCachedElement(
            slot: .searchField,
            root: rootWindow,
            validate: { field in
                field.isEnabled && field.role == kAXTextFieldRole
            }
        ) {
            return cachedSearchField
        }

        let initialFields = discoverSearchFieldCandidates(in: rootWindow)
        if let field = pickSearchField(from: initialFields) {
            rememberCachedElement(slot: .searchField, root: rootWindow, element: field)
            return field
        }

        var buttonRoots = [rootWindow]
        if let focusedWindow = kakao.focusedWindow { buttonRoots.append(focusedWindow) }
        if let mainWindow = kakao.mainWindow { buttonRoots.append(mainWindow) }
        buttonRoots.append(kakao.applicationElement)

        let allButtons = deduplicateElements(
            deduplicateElements(buttonRoots).flatMap { root in
                root.findAll(role: kAXButtonRole, limit: 48, maxNodes: 600)
            }
        )
        runner.log("search: visible buttons=\(allButtons.count)")
        let searchButtons = allButtons.filter { button in
            let title = (button.title ?? "").lowercased()
            let description = (button.axDescription ?? "").lowercased()
            let help = (button.helpText ?? "").lowercased()
            let identifier = (button.identifier ?? "").lowercased()

            if identifier == "friends" || identifier == "chatrooms" || identifier == "more" {
                return false
            }

            return title.contains("search")
                || title.contains("검색")
                || description.contains("search")
                || description.contains("검색")
                || help.contains("search")
                || help.contains("검색")
                || identifier.contains("search")
        }
        runner.log("search: search-like buttons=\(searchButtons.count)")

        for button in searchButtons.prefix(4) {
            do {
                try button.press()
                runner.log("search: pressed search-like button title='\(button.title ?? "")' id='\(button.identifier ?? "")'")
            } catch {
                runner.log("search: search-like button press failed (\(error))")
            }

            Thread.sleep(forTimeInterval: 0.08)
            let fields = discoverSearchFieldCandidates(in: rootWindow)
            if let field = pickSearchField(from: fields) {
                rememberCachedElement(slot: .searchField, root: rootWindow, element: field)
                return field
            }
        }

        // KakaoTalk exposes the search command as ⌘F even when its search
        // button is not present in this process's current AX subtree.
        kakao.activate()
        _ = tryRaiseWindow(rootWindow)
        runner.log("search: opening search field via Command-F fallback")
        runner.pressCommandF()
        Thread.sleep(forTimeInterval: 0.12)
        let commandFields = discoverSearchFieldCandidates(in: rootWindow)
        if let field = pickSearchField(from: commandFields) {
            rememberCachedElement(slot: .searchField, root: rootWindow, element: field)
            return field
        }

        return nil
    }

    private func discoverSearchFieldCandidates(in rootWindow: UIElement) -> [UIElement] {
        var fields: [UIElement] = []
        fields.append(contentsOf: rootWindow.findAll(role: kAXTextFieldRole, limit: 8, maxNodes: 140))
        if let focusedWindow = kakao.focusedWindow {
            fields.append(contentsOf: focusedWindow.findAll(role: kAXTextFieldRole, limit: 8, maxNodes: 140))
        }
        if let mainWindow = kakao.mainWindow {
            fields.append(contentsOf: mainWindow.findAll(role: kAXTextFieldRole, limit: 8, maxNodes: 140))
        }
        fields.append(contentsOf: kakao.applicationElement.findAll(role: kAXTextFieldRole, limit: 12, maxNodes: 600))
        return fields.filter { $0.isEnabled }
    }

    private func waitForMatchingSearchResults(query: String, rootWindow: UIElement) -> [SearchCandidate] {
        let fastProfile = SearchScanProfile(
            label: "fast",
            timeout: 0.22,
            pollInterval: 0.04,
            rowLimit: 24,
            cellLimit: 24,
            supplementalLimit: 0,
            candidateNodeBudget: 320,
            textLimit: 6,
            textNodeBudget: 80,
            includeSupplementalRoles: false,
            includeApplicationRoot: false
        )
        let expandedProfile = SearchScanProfile(
            label: "expanded",
            timeout: 0.75,
            pollInterval: 0.05,
            rowLimit: 120,
            cellLimit: 120,
            supplementalLimit: 80,
            candidateNodeBudget: 1_200,
            textLimit: 16,
            textNodeBudget: 220,
            includeSupplementalRoles: true,
            includeApplicationRoot: true
        )

        var matches: [SearchCandidate] = []
        let foundFast = runner.waitUntil(label: "search results (\(fastProfile.label))", timeout: fastProfile.timeout, pollInterval: fastProfile.pollInterval) {
            matches = findMatchingSearchResults(query: query, rootWindow: rootWindow, profile: fastProfile)
            return !matches.isEmpty
        }
        if !foundFast {
            matches = findMatchingSearchResults(query: query, rootWindow: rootWindow, profile: fastProfile)
        }
        if !matches.isEmpty {
            runner.log("search: matching candidates=\(matches.count) via \(fastProfile.label)")
            return matches
        }

        runner.log("search: no matches in fast scan; expanding search scope")
        let foundExpanded = runner.waitUntil(label: "search results (\(expandedProfile.label))", timeout: expandedProfile.timeout, pollInterval: expandedProfile.pollInterval) {
            matches = findMatchingSearchResults(query: query, rootWindow: rootWindow, profile: expandedProfile)
            return !matches.isEmpty
        }
        if !foundExpanded {
            matches = findMatchingSearchResults(query: query, rootWindow: rootWindow, profile: expandedProfile)
        }
        runner.log("search: matching candidates=\(matches.count)")
        return matches
    }

    private func findMatchingSearchResults(
        query: String,
        rootWindow: UIElement,
        profile: SearchScanProfile
    ) -> [SearchCandidate] {
        var roots: [UIElement] = [rootWindow]
        if let focusedWindow = kakao.focusedWindow {
            roots.append(focusedWindow)
        }
        if let mainWindow = kakao.mainWindow {
            roots.append(mainWindow)
        }
        if profile.includeApplicationRoot {
            roots.append(kakao.applicationElement)
        }
        roots = deduplicateElements(roots)

        var results: [SearchCandidate] = []
        for root in roots {
            var candidates: [UIElement] = []
            candidates.append(contentsOf: root.findAll(role: kAXRowRole, limit: profile.rowLimit, maxNodes: profile.candidateNodeBudget))
            candidates.append(contentsOf: root.findAll(role: kAXCellRole, limit: profile.cellLimit, maxNodes: profile.candidateNodeBudget))

            if profile.includeSupplementalRoles {
                candidates.append(contentsOf: root.findAll(role: kAXGroupRole, limit: profile.supplementalLimit, maxNodes: profile.candidateNodeBudget))
                candidates.append(contentsOf: root.findAll(role: kAXButtonRole, limit: profile.supplementalLimit, maxNodes: profile.candidateNodeBudget))
                candidates.append(contentsOf: root.findAll(role: kAXStaticTextRole, limit: profile.supplementalLimit, maxNodes: profile.candidateNodeBudget))
            }

            candidates = deduplicateElements(candidates)
            for candidate in candidates {
                // The container around KakaoTalk's search field can itself be
                // exposed as an AXRow/AXCell. Its current value equals the query,
                // but it is not a chat result.
                guard !containsSearchFieldValue(query, in: candidate) else {
                    continue
                }
                let (matchScore, matchedText) = bestQueryMatch(
                    query: query,
                    in: candidate,
                    textLimit: profile.textLimit,
                    textNodeBudget: profile.textNodeBudget
                )
                guard matchScore > 0, let matchedText else { continue }
                let activationCandidate = activationTarget(for: candidate)
                results.append(
                    SearchCandidate(
                        element: activationCandidate,
                        textScore: matchScore,
                        matchedText: matchedText
                    )
                )
            }

            if !results.isEmpty && !profile.includeSupplementalRoles {
                break
            }
        }

        return deduplicateSearchCandidates(results)
    }

    private func containsSearchFieldValue(_ query: String, in element: UIElement) -> Bool {
        let fields = element.findAll(
            role: kAXTextFieldRole,
            limit: 4,
            maxNodes: 100
        )
        return fields.contains { field in
            guard let value = field.stringValue else { return false }
            return normalizeSearchToken(value) == normalizeSearchToken(query)
        }
    }

    private func waitForOpenedChatWindow(query: String, fallbackWindow: UIElement) -> UIElement? {
        var resolved: UIElement?
        _ = runner.waitUntil(label: "chat context ready", timeout: 1.5, pollInterval: 0.05, evaluateAfterTimeout: false) {
            resolved = resolveOpenedChatWindowFast(query: query)
            return resolved != nil
        }
        return resolved ?? resolveOpenedChatWindow(query: query, fallbackWindow: fallbackWindow)
    }

    private func resolveOpenedChatWindowFast(query: String) -> UIElement? {
        if let matchedWindow = findMatchingChatWindow(in: kakao.windows, query: query) {
            return matchedWindow
        }

        if let focusedWindow = kakao.focusedWindow,
           let title = focusedWindow.title,
           scoreQueryMatch(query: query, candidateText: title) > 0
        {
            return focusedWindow
        }

        return nil
    }

    private func resolveOpenedChatWindow(query: String, fallbackWindow: UIElement) -> UIElement? {
        if let fastWindow = resolveOpenedChatWindowFast(query: query) {
            return fastWindow
        }

        if let matchedWindow = findMatchingChatWindow(in: kakao.windows, query: query) {
            return matchedWindow
        }

        // Safety mode never accepts a generic focused/fallback window merely because
        // it contains a composer. Its title must have matched the requested room.
        if exactMatchOnly {
            return nil
        }

        if let focusedWindow = kakao.focusedWindow, windowContainsLikelyChatInput(focusedWindow) {
            return focusedWindow
        }

        if windowContainsLikelyChatInput(fallbackWindow) {
            return fallbackWindow
        }

        if let mainWindow = kakao.mainWindow, windowContainsLikelyChatInput(mainWindow) {
            return mainWindow
        }

        return nil
    }

    private func windowContainsLikelyChatInput(_ window: UIElement) -> Bool {
        if window.findFirst(where: { element in
            guard element.isEnabled else { return false }
            return element.role == kAXTextAreaRole
        }) != nil {
            return true
        }

        return window.findFirst(where: { element in
            isLikelyMessageInputElement(element, in: window) && element.role != kAXTextFieldRole
        }) != nil
    }

    private func isLikelyMessageInputElement(_ element: UIElement, in window: UIElement? = nil) -> Bool {
        guard element.isEnabled else { return false }
        let role = element.role ?? ""
        if role == kAXTextAreaRole {
            return true
        }

        let editable: Bool = element.attributeOptional(kAXEditableAttribute) ?? false
        guard editable else { return false }
        guard role != kAXStaticTextRole && role != kAXImageRole else { return false }
        if role == kAXTextFieldRole, isLikelySearchField(element, in: window) {
            return false
        }
        return true
    }

    private func isLikelySearchField(_ element: UIElement, in window: UIElement?) -> Bool {
        let role = element.role ?? ""
        guard role == kAXTextFieldRole else { return false }

        let joinedText = [
            element.identifier ?? "",
            element.title ?? "",
            element.axDescription ?? "",
        ]
        .joined(separator: " ")
        .lowercased()

        return joinedText.contains("search") || joinedText.contains("검색")
    }

    private func pickBestSearchResult(from candidates: [SearchCandidate]) -> SearchCandidate? {
        guard !candidates.isEmpty else { return nil }
        let best = candidates.max { lhs, rhs in
            scoreSearchResult(lhs) < scoreSearchResult(rhs)
        }
        if let best {
            runner.log(
                "search: best result role='\(best.element.role ?? "unknown")' title='\(best.element.title ?? "")' textScore=\(best.textScore) matched='\(best.matchedText)'"
            )
        }
        return best
    }

    private func scoreSearchResult(_ candidate: SearchCandidate) -> Int {
        var score = candidate.textScore * 4
        let element = candidate.element
        if supportsAction("AXPress", on: element) {
            score += 4_000
        }
        if supportsAction("AXConfirm", on: element) {
            score += 3_000
        }
        if element.role == kAXRowRole {
            score += 1_600
        } else if element.role == kAXCellRole {
            score += 1_200
        } else if element.role == kAXButtonRole {
            score += 800
        }
        if let title = element.title, !title.isEmpty {
            score += 300
        }
        if element.role == nil || element.role?.isEmpty == true {
            score -= 2_000
        }
        return score
    }

    private func triggerSearchResultOpen(
        _ candidate: SearchCandidate,
        searchField: UIElement,
        query: String,
        opened: () -> Bool
    ) -> Bool {
        if runner.isCancelled { return false }
        let result = candidate.element
        var didTriggerAction = false

        // Lock the exact result into KakaoTalk's semantic table selection before
        // sending any key event. This prevents a stale selection from a previous
        // batch item from receiving Enter.
        kakao.activate()
        let selected = trySelectSearchResult(result, label: "exact result")
        let focused = focusSearchResult(result)
        if selected || focused {
            runner.pressEnterKey()
            didTriggerAction = true
            if runner.waitUntil(
                label: "search open confirm (selected exact result)",
                timeout: 1.2,
                pollInterval: 0.05,
                evaluateAfterTimeout: false,
                condition: opened
            ) {
                return true
            }
        }

        // Some KakaoTalk versions expose a virtual row that can be selected but
        // not focused. Fall back to the documented search-field keyboard
        // selection contract without using screen geometry.
        if searchField.isFocused || runner.focusWithVerification(
            searchField,
            label: "search field exact-result selection",
            attempts: 1
        ) {
            runner.pressDownArrowKey()
            Thread.sleep(forTimeInterval: 0.08)
            runner.pressEnterKey()
            didTriggerAction = true
            if runner.waitUntil(
                label: "search open confirm (keyboard selection)",
                timeout: 1.2,
                pollInterval: 0.05,
                evaluateAfterTimeout: false,
                condition: opened
            ) {
                return true
            }
        }

        if tryActivateSearchResult(result, label: "result") {
            if runner.isCancelled { return false }
            didTriggerAction = true
            if runner.waitUntil(label: "search open confirm", timeout: 0.24, pollInterval: 0.05, evaluateAfterTimeout: false, condition: opened) {
                return true
            }
        }

        // KakaoTalk virtualizes search rows, so re-read the exact-title AX object
        // instead of retaining a stale handle or clicking a cached screen point.
        let refreshProfile = SearchScanProfile(
            label: "activation",
            timeout: 0,
            pollInterval: 0,
            rowLimit: 120,
            cellLimit: 120,
            supplementalLimit: 80,
            candidateNodeBudget: 1_600,
            textLimit: 16,
            textNodeBudget: 240,
            includeSupplementalRoles: true,
            includeApplicationRoot: true
        )
        let refreshed = findMatchingSearchResults(
            query: query,
            rootWindow: kakao.focusedWindow ?? kakao.mainWindow ?? searchField,
            profile: refreshProfile
        )
        for fresh in refreshed.sorted(by: { scoreSearchResult($0) > scoreSearchResult($1) }) {
            if runner.isCancelled { return false }
            if tryActivateSearchResult(fresh.element, label: "fresh exact result") {
                didTriggerAction = true
                if runner.waitUntil(
                    label: "search open confirm (fresh AX action)",
                    timeout: 0.5,
                    pollInterval: 0.05,
                    evaluateAfterTimeout: false,
                    condition: opened
                ) {
                    return true
                }
            }
        }

        let fallbackSelected = trySelectSearchResult(result, label: "result")
        if runner.isCancelled { return false }
        if !fallbackSelected, let parent = result.parent {
            let parentSelected = trySelectSearchResult(parent, label: "result.parent")
            didTriggerAction = didTriggerAction || parentSelected
        }
        didTriggerAction = didTriggerAction || fallbackSelected
        if fallbackSelected,
           runner.waitUntil(label: "search open confirm", timeout: 0.14, pollInterval: 0.05, evaluateAfterTimeout: false, condition: opened)
        {
            return true
        }

        if fallbackSelected {
            kakao.activate()
            if runner.focusWithVerification(searchField, label: "search field confirm", attempts: 1) {
                runner.log("search: fallback confirm via Enter")
                runner.pressEnterKey()
                didTriggerAction = true
                if runner.waitUntil(label: "search open confirm", timeout: 0.18, pollInterval: 0.05, evaluateAfterTimeout: false, condition: opened) {
                    return true
                }
            } else {
                runner.log("search: fallback confirm skipped (search field focus failed)")
            }
        } else {
            runner.log("search: skipping Enter fallback because result selection was not available")
        }

        kakao.activate()
        if runner.isCancelled { return false }
        if searchField.isFocused || runner.focusWithVerification(searchField, label: "search field confirm", attempts: 1) {
            runner.log("search: fallback confirm via Down+Enter")
            runner.pressDownArrowKey()
            Thread.sleep(forTimeInterval: 0.03)
            runner.pressEnterKey()
            didTriggerAction = true
            if runner.waitUntil(label: "search open confirm", timeout: 0.22, pollInterval: 0.05, evaluateAfterTimeout: false, condition: opened) {
                return true
            }
        } else {
            runner.log("search: Down+Enter skipped (search field focus unavailable)")
        }

        return didTriggerAction
    }

    private func ensureNotCancelled() throws {
        if runner.isCancelled {
            throw KakaoTalkError.actionFailed("[CANCELLED] 사용자가 작업을 중지했습니다.")
        }
    }

    private func tryActivateSearchResult(_ element: UIElement, label: String) -> Bool {
        if let actions = try? element.actionNames(), !actions.isEmpty {
            runner.log("search: \(label) actions=\(actions.joined(separator: ","))")
        }

        do {
            if supportsAction("AXPress", on: element) {
                try element.press()
                runner.log("search: \(label) activated via AXPress")
                return true
            }
        } catch {
            runner.log("search: \(label) AXPress failed (\(error))")
        }

        do {
            if supportsAction("AXConfirm", on: element) {
                try element.performAction("AXConfirm")
                runner.log("search: \(label) activated via AXConfirm")
                return true
            }
        } catch {
            runner.log("search: \(label) AXConfirm failed (\(error))")
        }

        for action in ["AXShowDefaultUI", "AXShowAlternateUI", "AXOpen"] {
            do {
                if supportsAction(action, on: element) {
                    try element.performAction(action)
                    runner.log("search: \(label) activated via \(action)")
                    return true
                }
            } catch {
                runner.log("search: \(label) \(action) failed (\(error))")
            }
        }

        return false
    }

    private func trySelectSearchResult(_ element: UIElement, label: String) -> Bool {
        var selected = false
        do {
            try element.setAttribute("AXSelected", value: true as CFBoolean)
            runner.log("search: \(label) selected via AXSelected=true")
            selected = true
        } catch {
            runner.log("search: \(label) select failed (\(error))")
        }

        var cursor: UIElement? = element
        var row: UIElement?
        var table: UIElement?
        var hops = 0
        while let current = cursor, hops < 10 {
            if row == nil, current.role == kAXRowRole {
                row = current
            }
            if current.role == kAXTableRole {
                table = current
                break
            }
            cursor = current.parent
            hops += 1
        }
        if let row, let table {
            do {
                try table.setAttribute(
                    kAXSelectedRowsAttribute,
                    value: [row.axElement] as CFArray
                )
                runner.log("search: \(label) selected via AXSelectedRows")
                selected = true
            } catch {
                runner.log("search: \(label) AXSelectedRows failed (\(error))")
            }
        }
        return selected
    }

    private func focusSearchResult(_ element: UIElement) -> Bool {
        var candidates: [UIElement] = [element]
        candidates.append(contentsOf: element.findAll(
            role: kAXCellRole,
            limit: 4,
            maxNodes: 40
        ))
        if let parent = element.parent {
            candidates.append(parent)
        }
        for candidate in deduplicateElements(candidates) {
            if runner.focusWithVerification(
                candidate,
                label: "exact search result",
                attempts: 1
            ) {
                return true
            }
        }
        return false
    }

    private func supportsAction(_ action: String, on element: UIElement) -> Bool {
        guard let actions = try? element.actionNames() else { return false }
        return actions.contains(action)
    }

    private func findMatchingChatWindow(in windows: [UIElement], query: String) -> UIElement? {
        matchingChatWindows(in: windows, query: query).first
    }

    private func matchingChatWindows(in windows: [UIElement], query: String) -> [UIElement] {
        windows.compactMap { window -> (window: UIElement, score: Int)? in
            guard let title = window.title else { return nil }
            let score = scoreQueryMatch(query: query, candidateText: title)
            guard score > 0 else { return nil }
            return (window, score)
        }
        .sorted(by: { lhs, rhs in lhs.score > rhs.score })
        .map(\.window)
    }

    private func bestQueryMatch(
        query: String,
        in element: UIElement,
        textLimit: Int,
        textNodeBudget: Int
    ) -> (score: Int, matchedText: String?) {
        let candidateTexts = collectCandidateTexts(
            from: element,
            textLimit: textLimit,
            textNodeBudget: textNodeBudget
        )
        guard !candidateTexts.isEmpty else { return (0, nil) }

        var bestScore = 0
        var bestText: String?
        for candidateText in candidateTexts {
            let score = scoreQueryMatch(query: query, candidateText: candidateText)
            if score > bestScore {
                bestScore = score
                bestText = candidateText
            }
        }

        return (bestScore, bestText)
    }

    private func collectCandidateTexts(
        from element: UIElement,
        textLimit: Int,
        textNodeBudget: Int
    ) -> [String] {
        var texts: [String] = []

        func appendText(_ raw: String?) {
            guard let raw else { return }
            let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty else { return }
            texts.append(trimmed)
        }

        appendText(element.title)
        appendText(element.stringValue)
        appendText(element.axDescription)

        let staticTexts = element.findAll(
            role: kAXStaticTextRole,
            limit: textLimit,
            maxNodes: textNodeBudget
        )
        for staticText in staticTexts {
            appendText(staticText.stringValue)
        }

        let textAreas = element.findAll(
            role: kAXTextAreaRole,
            limit: max(2, textLimit / 2),
            maxNodes: textNodeBudget
        )
        for textArea in textAreas {
            appendText(textArea.stringValue)
        }

        return deduplicateStringsPreservingOrder(texts)
    }

    private func scoreQueryMatch(query: String, candidateText: String) -> Int {
        if exactMatchOnly {
            return exactRoomTitle(query) == exactRoomTitle(candidateText) ? 12_000 : 0
        }
        let queryNormalized = normalizeSearchToken(query)
        let candidateNormalized = normalizeSearchToken(candidateText)
        guard !queryNormalized.isEmpty, !candidateNormalized.isEmpty else { return 0 }

        if queryNormalized == candidateNormalized {
            return 12_000
        }
        if candidateNormalized.hasPrefix(queryNormalized) {
            return 10_500
        }
        if candidateNormalized.contains(queryNormalized) {
            return 9_800
        }
        if queryNormalized.contains(candidateNormalized), candidateNormalized.count >= 2 {
            return 8_800
        }

        let queryVariants = honorificVariants(of: queryNormalized)
        let candidateVariants = honorificVariants(of: candidateNormalized)
        var best = 0

        for queryVariant in queryVariants where !queryVariant.isEmpty {
            for candidateVariant in candidateVariants where !candidateVariant.isEmpty {
                if queryVariant == candidateVariant {
                    best = max(best, 8_700)
                    continue
                }
                if candidateVariant.hasPrefix(queryVariant) {
                    best = max(best, 8_400)
                    continue
                }
                if candidateVariant.contains(queryVariant) {
                    best = max(best, 8_200)
                    continue
                }
                if queryVariant.contains(candidateVariant), candidateVariant.count >= 2 {
                    best = max(best, 7_900)
                }
            }
        }

        if best > 0 {
            return best
        }

        let minLength = min(queryNormalized.count, candidateNormalized.count)
        if minLength >= 2 {
            let shortest = queryNormalized.count <= candidateNormalized.count ? queryNormalized : candidateNormalized
            let longest = queryNormalized.count > candidateNormalized.count ? queryNormalized : candidateNormalized
            if longest.contains(shortest) {
                return 6_600
            }
        }

        return 0
    }

    private func normalizeSearchToken(_ text: String) -> String {
        let lowered = text.folding(options: [.diacriticInsensitive, .widthInsensitive], locale: .current).lowercased()
        var scalars = String.UnicodeScalarView()
        scalars.reserveCapacity(lowered.unicodeScalars.count)

        for scalar in lowered.unicodeScalars {
            if CharacterSet.whitespacesAndNewlines.contains(scalar) {
                continue
            }
            if scalar.value == 0x200B || scalar.value == 0x200C || scalar.value == 0x200D || scalar.value == 0xFEFF {
                continue
            }
            if CharacterSet.punctuationCharacters.contains(scalar) || CharacterSet.symbols.contains(scalar) {
                continue
            }
            scalars.append(scalar)
        }

        return String(scalars)
    }

    private func exactRoomTitle(_ text: String) -> String {
        text.precomposedStringWithCanonicalMapping
    }

    private func honorificVariants(of text: String) -> [String] {
        let suffixes = ["선생님", "님", "씨"]
        var variants = Set<String>([text])
        for suffix in suffixes where text.hasSuffix(suffix) {
            let candidate = String(text.dropLast(suffix.count))
            if !candidate.isEmpty {
                variants.insert(candidate)
            }
        }
        return Array(variants)
    }

    private func deduplicateSearchCandidates(_ candidates: [SearchCandidate]) -> [SearchCandidate] {
        var unique: [SearchCandidate] = []
        unique.reserveCapacity(candidates.count)

        for candidate in candidates {
            if let index = unique.firstIndex(where: { existing in
                if areSameAXElement(existing.element, candidate.element) {
                    return true
                }
                guard normalizeSearchToken(existing.matchedText) == normalizeSearchToken(candidate.matchedText) else {
                    return false
                }
                // One visible result is often exposed as a nested AXRow/AXCell/
                // AXGroup chain. Treat only ancestor-related elements as the same
                // result; equal titles in separate rows remain a safe duplicate.
                return isAncestor(existing.element, of: candidate.element)
                    || isAncestor(candidate.element, of: existing.element)
            }) {
                if candidate.textScore > unique[index].textScore {
                    unique[index] = candidate
                }
                continue
            }
            unique.append(candidate)
        }

        return unique
    }

    private func isAncestor(_ ancestor: UIElement, of descendant: UIElement) -> Bool {
        var cursor = descendant.parent
        var hops = 0
        while let current = cursor, hops < 8 {
            if areSameAXElement(ancestor, current) {
                return true
            }
            cursor = current.parent
            hops += 1
        }
        return false
    }

    private func deduplicateElements(_ elements: [UIElement]) -> [UIElement] {
        var unique: [UIElement] = []
        unique.reserveCapacity(elements.count)
        for element in elements {
            if unique.contains(where: { existing in
                areSameAXElement(existing, element)
            }) {
                continue
            }
            unique.append(element)
        }

        return unique
    }

    private func deduplicateStringsPreservingOrder(_ values: [String]) -> [String] {
        var seen = Set<String>()
        var unique: [String] = []
        unique.reserveCapacity(values.count)

        for value in values {
            if seen.contains(value) {
                continue
            }
            seen.insert(value)
            unique.append(value)
        }

        return unique
    }

    private func activationTarget(for element: UIElement) -> UIElement {
        var lineage: [UIElement] = [element]
        var cursor = element.parent
        var hops = 0
        while let current = cursor, hops < 8 {
            lineage.append(current)
            cursor = current.parent
            hops += 1
        }

        // KakaoTalk can expose one visible result through sibling AXCell and
        // AXGroup objects. Canonicalize all of them to their enclosing AXRow
        // (or the next strongest semantic container) so one room is counted
        // once without comparing screen coordinates.
        for preferredRole in [kAXRowRole, kAXCellRole, kAXButtonRole, kAXGroupRole] {
            if let target = lineage.first(where: { $0.role == preferredRole }) {
                return target
            }
        }
        return element
    }

    private func isSearchActivationRole(_ role: String?) -> Bool {
        switch role {
        case kAXRowRole, kAXCellRole, kAXButtonRole, kAXGroupRole:
            return true
        default:
            return false
        }
    }

    private func pickSearchField(from fields: [UIElement]) -> UIElement? {
        let enabled = deduplicateElements(fields.filter(\.isEnabled))
        let semanticMatches = enabled.filter { field in
            let metadata = [
                field.identifier ?? "",
                field.title ?? "",
                field.axDescription ?? "",
                field.helpText ?? "",
            ].joined(separator: " ").lowercased()
            return metadata.contains("search") || metadata.contains("검색")
        }
        if semanticMatches.count == 1 {
            return semanticMatches[0]
        }
        // KakaoTalk exposes only one enabled text field while global search is
        // expanded. A non-unique field set is rejected instead of guessed.
        return enabled.count == 1 ? enabled[0] : nil
    }

    private func tryRaiseWindow(_ window: UIElement) -> Bool {
        if supportsAction(kAXRaiseAction, on: window) {
            do {
                try window.performAction(kAXRaiseAction)
                runner.log("window: raised via AXRaise")
                return true
            } catch {
                runner.log("window: AXRaise failed (\(error))")
            }
        }
        return false
    }

    private func findCloseButton(in window: UIElement) -> UIElement? {
        let buttons = window.findAll(role: kAXButtonRole, limit: 6, maxNodes: 80)
        if let match = buttons.first(where: { button in
            let joined = [
                button.identifier ?? "",
                button.title ?? "",
                button.axDescription ?? "",
            ].joined(separator: " ").lowercased()
            return joined.contains("close") || joined.contains("닫기")
        }) {
            return match
        }

        return nil
    }

    private func waitForWindowClosed(_ window: UIElement, label: String) -> Bool {
        runner.waitUntil(label: label, timeout: 0.9, pollInterval: 0.06, evaluateAfterTimeout: false) {
            !kakao.windows.contains { candidate in
                areSameAXElement(candidate, window)
            }
        }
    }

    private func areSameAXElement(_ lhs: UIElement, _ rhs: UIElement) -> Bool {
        CFEqual(lhs.axElement, rhs.axElement)
    }

}
