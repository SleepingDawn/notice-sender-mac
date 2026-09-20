import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SEND_COMMAND = REPO_ROOT / "Sources" / "kmsg" / "Commands" / "SendCommand.swift"
CHAT_WINDOW_RESOLVER = REPO_ROOT / "Sources" / "kmsg" / "KakaoTalk" / "ChatWindowResolver.swift"
EMBEDDED_ENGINE = REPO_ROOT / "Sources" / "kmsg" / "NoticeSenderBridge" / "KmsgEmbeddedEngine.swift"


class SendCommandContractTests(unittest.TestCase):
    def test_send_command_delegates_chat_window_resolution(self) -> None:
        source = SEND_COMMAND.read_text(encoding="utf-8")

        self.assertIn("let chatWindowResolver = ChatWindowResolver(", source)
        self.assertIn("chatWindowResolver.resolve(chatID:", source)
        self.assertIn("chatWindowResolver.resolve(query:", source)

        delegated_helpers = [
            "private func requireUsableWindow(",
            "private func selectSearchWindow(",
            "private func openChatViaSearch(",
            "private func pickBestSearchResult(",
            "private func scoreSearchResult(",
            "private func triggerSearchResultOpen(",
            "private func tryActivateSearchResult(",
            "private func trySelectSearchResult(",
            "private func findMatchingChatWindow(",
            "private func locateSearchField(",
            "private func discoverSearchFieldCandidates(",
            "private func waitForMatchingSearchResults(",
            "private func findMatchingSearchResults(",
            "private func waitForOpenedChatWindow(",
            "private func resolveOpenedChatWindowFast(",
            "private func resolveOpenedChatWindow(",
            "private func windowContainsLikelyChatInput(",
            "private func pickSearchField(",
            "private func containsText(",
        ]
        for helper in delegated_helpers:
            self.assertNotIn(helper, source)

    def test_notice_sender_checks_multiple_matches_from_top_to_bottom(self) -> None:
        resolver = CHAT_WINDOW_RESOLVER.read_text(encoding="utf-8")
        engine = EMBEDDED_ENGINE.read_text(encoding="utf-8")

        self.assertIn("checkMultipleMatchesInOrder: true", engine)
        self.assertIn("for index in 0..<count", resolver)
        self.assertIn("preferKeyboardFirstResult: index == 0", resolver)
        self.assertIn("scoreQueryMatch(query: query, candidateText: title) > 0", resolver)
        self.assertIn("result \\(index + 1) opened a different room; checking next result", resolver)

    def test_notice_sender_pastes_all_attachments_as_one_batch(self) -> None:
        source = EMBEDDED_ENGINE.read_text(encoding="utf-8")

        self.assertNotIn("for attachmentURL in attachmentURLs", source)
        self.assertIn("try sendAttachments(\n                attachmentURLs,", source)
        self.assertIn("pasteboard.writeObjects(fileURLs.map { $0 as NSURL })", source)
        self.assertIn("attachmentPreviewContains(filenames: filenames", source)
        self.assertIn("attachmentTranscriptEntriesAreComplete(", source)


if __name__ == "__main__":
    unittest.main()
