# Windows live feature validation

**Goal:** Continue correcting the Windows app until its supported workflows and authorized live Kakao tests succeed. Run opening and delivery through the app; manual room opening does not count.
**Authorization:** User waived design questions and explicitly requested continued implementation. Dry runs on local-account rooms must not enter or send messages/files. Actual delivery is restricted to verified chatbot rooms (카카오톡 선물하기, 지모, KREAM) with synthetic content only. No account switching, human recipients or real student data.
**Branch:** `codex/windows-port`; retain existing checkout and feature-branch publication policy.

## Work sequence

- [x] Correct asynchronous search-result confirmation with a bounded search-only retry, preserving exact title checks, cancellation, query ownership and the opening lease. Prove delayed results with owned native test windows, then run the app's five-room request.
- [ ] Add an isolated visible bot-only application test window/runner. No real database; strict bot allowlist and explicit opt-in; stop first unexpected failure. Cover individual/common text, single/multiple files, attachment-only, PDF/image types, cancellation and protected retry. Fake-client smoke tests precede implementation.
- [ ] Diagnose actual composer focus (foreground room, target GUI thread's focus/caret, UIA state, text lengths). Restore and focus only the exact verified room; preserve arbitrary drafts. No string-based cue exemption.
- [ ] Correct and verify text entry/send completion against the verified chatbot.
- [ ] Correct and verify native file selection/confirmation/completion for each supported file mode. Do not interpret an issued request or an unverified dialog disappearance as receipt.
- [ ] Re-run data and UI regressions, independent focused review, full build and packaged smoke. Update version/build, guide and evidence matrix, then commit/push the feature branch.

## Evidence and unresolved items

Private request/result files remain under ignored `outputs/`; source tests use synthetic names only. Initial app test `top-five-app-dry-run-2.json` newly opened the first room, then failed the next. One-room probe `one-room-app-dry-run-3.json` newly opened that second room once its search settled, supporting stale asynchronous result selection. No messages/files have been sent in those probes.

Actual app run `top-five-app-dry-run-5.json` passed all five at 23:27 KST, exit0. Opening-only read-only roots are accepted with explicit CanCompose=false; lesson preflight/actual resolution still require a composer. App new-opening evidence exists across the follow-up runs for all five roots. No CUA/manual room opening was used in this continuation.

The earlier bot attempt stopped before entry because cue-looking RichEdit text remained after UIA focus. Microsoft documents that UIA SetFocus need not bring a window forward. Native target-thread focus diagnostics will distinguish failed focus from an application-owned cue. A standard Edit cue-banner exemption is inappropriate for RichEdit.

## Runtime verification block

The first visible bot-suite run `outputs/bot-app-send-1.json` stopped before focus/text entry because a persistent channel menu was classified as a dialog. The app's exact-window PNGs proved that the owned visible window was a modeless channel menu and its room owner remained enabled. Modal detection and attachment-baseline handling were corrected, with four regressions observed failing before implementation (54/58).

Windows then blocked rebuilt test/automation DLL loading with 0x800711C7. CodeIntegrity event3077 identified policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`, Microsoft's Smart App Control VerifiedAndReputableDesktop policy. Debug reproduced the block. No renaming, alternate-host execution, policy/registry modification or other bypass is authorized or attempted. User confirmed no signing service/certificate or separate Windows environment is available. Local live tests therefore remain blocked, not passed.

The existing GitHub Windows workflow is an authorized separate CI environment and previous commits aff4e1d/9ec7080 passed there. Regression verification will continue through that workflow. This does not authorize downloading/executing blocked unsigned builds locally or claim that CI tests exercise the user's Kakao account.

A native semantic-control ownership guard and regression tests were added. Automatic approval review rejected publishing a temporary no-op guard for a RED CI run, so the real strict PID/HWND/child-scope guard was restored before any remote publication. The unpublished local commit is amended to exclude the unsafe intermediate state. These new ownership cases have no observed RED run because local execution is blocked; CI will verify the actual safe implementation without weakening production checks. The prior modal regressions have an observed RED54/58. Actual delivery remains unverified; a test call that stops before entry is not a sent message.
