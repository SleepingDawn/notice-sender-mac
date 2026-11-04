# Windows attachment completion implementation plan

> **For agentic workers:** Use executing-plans to implement each independently tested task; dispatch independent static evidence experiments and manifest implementation only.

**Goal:** Complete the Windows sender's ordinary reviewed attachment flow and verify all seven real send scenarios through the app; the user's latest instruction selects the exact self-chat 임세용.

**Architecture:** Preserve exact native process/root/composer/modal binding for all actions. Separate immutable reviewed attachment authorization, native confirmation, and positive post-dispatch receipt evidence. Investigate an exact-transcript visual receipt verifier because the observed client exposes no receipt text through UIA2/3, MSAA Value/Description or WM_GETTEXT; unsupported observations must remain uncertain.

**Tech stack:** C#/.NET10 WPF, native HWND/UIA automation, existing local Windows VM, potential offline visual recognition only for completion evidence.

**Spec:** User's existing Windows port request, approved immediate implementation, exact-window control requirement, bot-only real-send authorization and zero-send dry runs; prior verified state in `2026-11-04-local-windows-vm.md`.

## Constraints

- Stay on `codex/windows-port`; do not merge into main or change the macOS app.
- Keep host Smart App Control enabled and signing separate. Execute blocked app assemblies only in the authorized local VM.
- Earlier sends used approved chatbot rooms. After the user changed the target, further actual sends are limited to the exact self-chat 임세용 using generated payloads. No other human/student test sends.
- Exact native identity controls all actions; no coordinate, guessed command, clipboard or OCR routing fallback.
- Receipt verification may inspect the exact transcript's pixels, but must exclude composer/modal/other-window content and stale or upload/error appearances.
- Preserve durable uncertainty and automatic resend protection; no renaming manual observations as automatic success.

## Task 1: Prove a usable completion observation

- [x] Evaluate existing saved pre-send, upload, complete file/image captures with exact matching and negative controls.
- [x] Capture the exact native transcript separately in a read-only probe, binding PID/root/composer/transcript before and after.
- [x] Select a completion provider only if it positively distinguishes complete receipts from previews, stale content and pending/error states.
- [x] Document unknown coverage; do not replace absent receipts with modal disappearance.

## Task 2: Freeze reviewed files before delivery

Files: new `NoticeSender.Automation/ReviewedAttachmentBatch.cs`, `NoticeSender.Automation.Tests/ReviewedAttachmentBatchTests.cs`; later wire `ReviewWindow.cs`, `LessonView.cs`.

- [x] RED: prove changed bytes/path/order/room and disposed leases fail before transport, and valid files remain readable while writes/deletes are denied.
- [x] Capture immutable per-recipient ordered path/size/digest manifests before the final review.
- [x] Acquire a verified read-only batch lease after approval and before the first text; revalidate exact scope at file dispatch.
- [x] Expose simple file names/sizes in review; keep digest machinery outside the normal user flow.

## Task 3: General native confirmation and receipt integration

Files: `WindowsKakaoClient.cs`, native confirmation policy and receipt provider interfaces, app composition and tests.

- [x] Keep default clients unprivileged; explicitly construct ordinary client with the approved batch lease and explicit manual review when automatic completion cannot be proven.
- [x] Share the already-tested native modal profile, exact file manifests, input/focus rechecks, capture deadline and once-only attempt semantics.
- [x] Keep stricter generated-file/bot restrictions in the unattended test entry point.
- [x] RED/GREEN failures for missing receipts, changed roots/files, stale receipts, upload/error indicators, cancellation, partial sends and restart/retry.
- [x] Verify individual cases, then a fresh full app-driven A/B/C/D/PDF/PNG/F suite in the VM.

## Task 4: Delivery and audit

- [x] Review final source changes and run appropriate Core/Automation/WPF and published-artifact checks.
- [x] Save only private test evidence outside Git; update coverage and remaining limitations truthfully.
- [x] Update Windows preview version/build, keep host signing status explicit, commit on the requested branch and provide the tested artifact.

## Review focus

- A changed or missing file after review must stop before any text delivery.
- A previously visible receipt or an incoming similarly named file must not authorize completion.
- A modal preview, pending image thumbnail or upload-error card must not count as a completed transcript attachment.
- Multiple recipients and files must retain original order, exactly-once attempt consumption and persistent uncertainty across restarts.
- A stalled capture/provider or user cancellation must settle without any later worker dispatching input.

## Final execution record

- Final source: `877c869`; version `3.1.2-preview.2`, file version `3.1.2.78`.
- App-only self-chat dry run newly opened the exact requested room without sending.
- The installed ZIP's app passed all seven real self-chat cases in `self-app-send-28-full.json` at 2026-11-04 10:12:24 KST; separate Korean-file case 29 passed at 10:13:34 KST.
- Existing failures 24–27 remain in private evidence. No previous uncertain item was automatically retried.
- General attachment completion uses explicit, durable user confirmation when no reliable automatic receipt is available; unattended synthetic tests cannot count that as automatic completion.
- Host Smart App Control stays enabled. This is an unsigned VM-tested development build; trusted signing remains separate.