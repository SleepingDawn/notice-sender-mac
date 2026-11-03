# Windows signing preparation implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this small continuation. Design review was waived by the user.

**Goal:** Prepare a trusted signing route without reducing Windows protection.
**Architecture:** Read-only PowerShell release inventory plus strict verification and a provider onboarding guide; no live signing integration before provider approval.
**Tech stack:** Windows PowerShell, Authenticode, GitHub Windows CI.
**Spec:** `docs/superpowers/specs/2026-11-04-windows-signing-design.md`

## Global constraints

- Preserve SAC, recipient restrictions and no-coordinate automation.
- Never retry the known-blocked automation binaries or change hosts/policies to evade their block. Independent non-automation Core tests may use their normal SDK command when not blocked.
- No provider registration, identity submission, payment or licensing without the user's decision.

## Review focus

- Unsigned application DLLs must not be hidden by a signed EXE.
- A valid but unexpected application publisher must fail the strict gate.
- Missing or additional application binaries must be visible and rejected.
- Vendor binaries must be checked without being re-signed.
- An inventory report must never imply SAC or delivery success.

### Task 1: Release audit and signing handoff

**Files:** `scripts/test-windows-signatures.ps1`, `scripts/tests/windows-signatures.tests.ps1`, `Windows/SIGNING.md`, `Windows/README.md`, `.github/workflows/windows.yml`.

- [x] Add executable script tests: real untrusted fixture inventory, strict rejection, missing inputs, invalid signer pin, unexpected application binary. Run and observe failure before implementation.
- [x] Implement inventory/strict verification without loading product assemblies; run all script tests.
- [x] Add a provider guide with verified official sources, blockers and exact next steps; link from README.
- [x] Run script tests in normal Windows CI, compile locally without product execution, review diff, commit and push to the existing feature branch.
- [x] Obtain the user's license/provider decision. The user confirmed ownership and authorized MIT plus a free SignPath application; supplied account name Seyong Im and the approved SNU email.
- [x] Publish owner-approved license/policies on the feature branch, obtain required provider-specific agreement/data-processing consent, submit application and verify the response. Provider acceptance and live delivery remain pending.

## Evidence and decisions

- Local script tests: 8/8; positive/publisher-policy tests simulate only the OS signature boundary. No certificate or trusted root created.
- Actual local release audit: 458 PE files, 454 Valid vendor signatures and four NotSigned application files. Signature acceptance remains false. Private report: `outputs/windows-signatures-before-signing.json`.
- Local compile/publish with `-SkipTests`: exit 0, zero warnings/errors. No blocked product/test assembly executed.
- Independent static review found report paths could overwrite JSON product configuration. A regression reproduced the failure before the fix; report paths inside publish are now rejected and original runtimeconfig bytes are preserved. Whole script suite returned 8/8 after the fix. No further blocking findings.
- Private application draft: `outputs/signpath-application-draft.json`; not submitted.
- Ruling: keep provider integration as an XML draft and documented steps until license/provider authorization. No fake service credentials or automatic signing workflow was installed.
- GitHub Windows CI for `fb529deb84e5ac16474a91fe337f734cdab5dff0`: [run 41666095451](https://github.com/SleepingDawn/notice-sender-mac/actions/runs/41666095451), completed success. Includes the signature-audit tests and full existing product build/tests/smokes.
- After MIT/application authorization, added a project LICENSE independently and preserved all third-party notices. The automatic approval reviewer rejected the initial proposal to generate it by replacing the copyright line in a third-party LICENSE; that command did not execute and was not retried. The separate project license does not reattribute vendor code.
- GitHub Windows CI for the owner-approved license/policy build `dafbd43fcf17c8193f5dc479a6cd48dcc618289c`: [run 41667318489](https://github.com/SleepingDawn/notice-sender-mac/actions/runs/41667318489), completed success with all steps successful.
- The user explicitly approved both required provider consent checkboxes and final submission, and separately authorized the CAPTCHA verification if needed. The next observed page already displayed **Form submitted / Thank you, we'll be in touch soon**; no image-challenge solution was entered by the agent. Optional marketing consent remained unchecked.
- Receipt screenshot and private metadata are in ignored `outputs/`. SignPath acceptance, signing credentials and signed binaries are not yet available. Actual bot-only delivery tests cannot resume until trusted signing succeeds.
- Scope correction after the user's local-testing question: the prior blanket statement about all unsigned test assemblies was too broad. A read-only check found 122 known automation block events and no Core test block events; the independent Core project references Core only. Its normal existing SDK command ran locally, 23/23 passed. Signature-audit script tests also passed 8/8. No blocked automation assembly, alternative loader, trust setting or live Kakao send was used. Current Windows edition is Home; Windows Sandbox is absent and unsupported in that edition. Local VM testing is a separate possible environment requiring installation and guest licensing/login, not an already performed host integration test.
