# Windows signing preparation implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this small continuation. Design review was waived by the user.

**Goal:** Prepare a trusted signing route without reducing Windows protection.
**Architecture:** Read-only PowerShell release inventory plus strict verification and a provider onboarding guide; no live signing integration before provider approval.
**Tech stack:** Windows PowerShell, Authenticode, GitHub Windows CI.
**Spec:** `docs/superpowers/specs/2026-11-04-windows-signing-design.md`

## Global constraints

- Preserve SAC, recipient restrictions and no-coordinate automation.
- Never execute unsigned product/test binaries on this blocked PC.
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
- [ ] Run script tests in normal Windows CI, compile locally without product execution, review diff, commit and push to the existing feature branch.
- [ ] Obtain the user's license/provider decision; provider enrollment and live delivery remain pending.

## Evidence and decisions

- Local script tests: 8/8; positive/publisher-policy tests simulate only the OS signature boundary. No certificate or trusted root created.
- Actual local release audit: 458 PE files, 454 Valid vendor signatures and four NotSigned application files. Signature acceptance remains false. Private report: `outputs/windows-signatures-before-signing.json`.
- Local compile/publish with `-SkipTests`: exit 0, zero warnings/errors. No blocked product/test assembly executed.
- Independent static review found report paths could overwrite JSON product configuration. A regression reproduced the failure before the fix; report paths inside publish are now rejected and original runtimeconfig bytes are preserved. Whole script suite returned 8/8 after the fix. No further blocking findings.
- Private application draft: `outputs/signpath-application-draft.json`; not submitted.
- Ruling: keep provider integration as an XML draft and documented steps until license/provider authorization. No fake service credentials or automatic signing workflow was installed.
