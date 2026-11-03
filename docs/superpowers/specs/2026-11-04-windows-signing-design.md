# Windows signing preparation

The user chose to keep Smart App Control enabled and prepare trusted signing. The existing product and its exact-window, bot-only delivery constraints remain binding. Actual KakaoTalk delivery is still unverified.

Prepare a read-only release audit and provider onboarding guide. The audit records every published EXE/DLL's SHA-256 and Authenticode state without loading or launching it. Strict verification requires valid signatures on every PE, the four expected application files, and an explicitly pinned application signer thumbprint. Vendor binaries retain their original signatures. A successful audit is a prerequisite for a normal product launch, not proof that SAC or KakaoTalk accepts the build.

Approaches considered: SignPath Foundation is the preferred free application path, conditional on an owner-approved OSI license and provider review; a commercial individual certificate is an alternative requiring payment and identity validation; Azure public trust is unavailable to a South Korean individual under the current published eligibility rules. Do not claim enrollment or guaranteed acceptance.

No security-setting changes, self-signed trust installation, alternative execution hosts, purchase, enrollment, license change, private-key handling, or external application submission. Provider selection and any license/payment decision stay with the user. Design questions were previously waived; proceed with this preparation.
