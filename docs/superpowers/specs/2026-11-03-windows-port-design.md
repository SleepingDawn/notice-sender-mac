# Windows notice sender

The user requested a Windows version of this repository on a new branch and selected C#/WPF with further design confirmation waived. Work is on `codex/windows-port`; do not merge or push main. The existing macOS release completion instructions apply only where meaningful: retain Mac functionality, increment its version/build metadata, and publish only this feature branch. macOS installation cannot run on this Windows host.

## Scope

Build a native Korean-language Windows 10/11 x64 WPF application under `Windows/`. Keep student and class management, CSV/TSV/XLSX import, JSON backup and class/preset interchange, normal/mock/direct templates, editable lesson results, rectangular clipboard paste, previews, common messages, attachment matching, dry run, sequential sending, cancellation and logs. Preserve the original schema 8 camelCase JSON identifiers, Korean preset/status strings and ISO dates. Preserve unknown JSON fields. Never include real student data in the repository or tests. Apple Intelligence is macOS-only; provide ordinary template editing on Windows.

## Architecture and alternatives

Use .NET 10 LTS WPF, BCL-only core and OpenXML ZIP/XML reading for XLSX. Python/PySide would simplify prototyping but adds a larger GUI/dependency distribution surface. SwiftUI cannot run on Windows. WPF gives native accessibility and a self-contained Windows distribution. The user accepted this recommended approach.

Three projects: `NoticeSender.Core` (data, import, templates, validation), `NoticeSender.Automation` (Windows UI Automation/targeted Win32 and send coordinator), and `NoticeSender.Windows` (WPF UI). Console self-test projects cover core rules and a fake automation transport. Build/publish scripts and CI produce a self-contained ZIP.

## Sending contract

No coordinates, OCR guessing, current-focus typing or broad SendKeys fallback. Locate KakaoTalk's process, exact unique room title and unique input control using UI Automation or owned child HWNDs. Revalidate process/window/title before every side effect. Dry run never changes the composer, presses its send/Enter control or pastes files. Room search may set only the exact main-window-owned search Edit and queue an opening command only to its verified result list; this native unlabelled-result fallback is dry-run-only. No or duplicate matches and inaccessible controls block the operation. Send only after an explicit in-app review/confirmation of current recipients, message bodies and attachment paths. A cancelled/failed operation after possible delivery becomes `확인 필요` and is never automatically retried. Persist each transition/log. Attachment completion must be observed; otherwise stop with the room open. Never send messages to real recipients during automated tests or this development session.

## Validation

Exercise CSV quoted newlines/EUC-KR, sparse/shared-string XLSX, original JSON interchange, template token/category validation, exact student identity and admission year, scores, six-message limit, suffix-sensitive file matching/depth/symlink rules, duplicate endpoints, dry-run side effects, cancellation and retry behavior. Build and launch WPF with a separate demo database, inspect its UI, and publish Windows x64.

The user subsequently authorized read-only dry runs against the locally connected Kakao account and actual delivery tests only to verified chatbot rooms such as `카카오톡 선물하기`, `지모`, and `KREAM`. Never send to students or human contacts. Test text, common-message order, single and multiple synthetic files, cancellation and retry behavior. Dry-run must never write into the composer, trigger sending, paste or drop files. Live testing is opt-in, never part of the default tests or CI, and its recipient allowlist is deliberately restricted. No account switching or credential changes are authorized.
