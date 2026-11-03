# App-owned room opening fix

**Goal:** Let the Windows app itself open exact named rooms for the user's current five-room dry run. No manual room-opening substitute and no message/file delivery.
**Basis:** Existing Windows design and user approval to implement without another design review. The current native search Edit has no UIA Name, so ResolveAsync rejects it; the custom result list exposes no named ListItems. Reproduced with an unopened room (private report under outputs/).
**Approach:** Identify the main Kakao window and ChatRoomListView-owned search Edit/result list by PID, native ancestry/class/control IDs; never select an arbitrary Edit. Enter only the room query in this search control and invoke only its result control. Check exact unique opened native title and composer ownership after opening. Keep all delivery operations out of a standalone app dry-run path. No coordinates, global key input, clipboard, or composer text entry.

- [x] Extend read-only diagnostics for main native child roles; verify runtime evidence.
- [x] Add a failing native search ownership/opening regression using synthetic owned Windows controls, then implement the strict search fallback and verify it green.
- [x] Add an isolated app-native named-room dry-run window/entry point (progress, cancellation, private report; no real database) and regression checks for no delivery.
- [x] Run the app's five-room dry run, documenting which rooms were newly opened vs already open. Do not count manual opening as app evidence.
- [x] Full tests and focused independent review, version/build 3.1.1/74 and self-contained ZIP. Preserve the existing feature branch for commit/push; no main merge.

**Review risks:** wrong process/parent, non-chat search edits, query changed concurrently, search opening wrong/duplicate titles, cancellation after opening. Fail before delivery in every case. Existing manually opened rooms do not prove closed-room opening; use other initially closed rooms to demonstrate it and explicitly report existing ones.

## Execution evidence

- Initial app-only reproduction rejected the unnamed search Edit; no room was opened.
- Native child evidence confirmed the exact main/OnlineMainView/ChatRoomListView/Edit/SearchListCtrl path, with inactive Edit after search was closed.
- First standalone WPF dry run verified one existing room, then failed opening the next room; exit 1, remaining three not attempted. Private report: `outputs/top-five-app-dry-run-1.json`.
- Post-failure read-only product inspection confirmed search results active but Edit disabled. Targeting the active results list is the focused correction; not a broad hotkey fallback.
- Physical Escape stopped Computer Use during observation. No additional Kakao input/live opening runs this turn. Native correction is checked with owned synthetic HWNDs; real 5-room rerun remains pending.
- Native regression suite: 38/38, including real HWND command ownership, hidden/locked/inactive/conflicting views, query tampering and cross-process contention. WPF smoke includes dry-run order/failure/cancel/report, exact names, demo route and mutually exclusive CLI modes. Named room-opening mutex covers discovery through exact verification across processes.

## Completed follow-up validation

On the user's subsequent explicit continuation, the actual WPF app newly opened the first room, then the second in a settled-query probe. This exposed asynchronous stale search results; bounded result-list-only confirmations corrected it. A following app run opened the third and fourth rooms, with the fourth correctly identified as a read-only notification channel. Opening-only mode now records CanCompose=false while lesson preflight and actual delivery remain blocked for that channel.

The final actual app run `outputs/top-five-app-dry-run-5.json` completed at 2026-11-03 23:27 KST with exit 0, all five roots verified, zero failures, four existing roots and the fifth newly opened by the app. Earlier app runs establish new-opening evidence for the other four. No manual room opening was used in this follow-up, and no message/file delivery was invoked. The initial incomplete evidence above is retained as history, not the current completion status.
