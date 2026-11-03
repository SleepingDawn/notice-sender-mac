# Code signing policy

## Current status

The maintainer authorized an application to SignPath Foundation on 2026-11-04. **The application was submitted on 2026-11-04 and the form confirmed receipt. Provider review is pending; SignPath has not accepted the project, and no Windows binary is currently signed by SignPath Foundation.** Windows live text/file delivery tests remain incomplete.

If the project is accepted, the required attribution will be: “Free code signing provided by SignPath.io, certificate by SignPath Foundation”. This describes the intended service after approval, not an existing signature or endorsement.

## People and approval

- Maintainer, source author/committer, reviewer and proposed release-signing approver: [Seyong Im / SleepingDawn](https://github.com/SleepingDawn).
- Contributions from others require maintainer review, including changes to build scripts, dependencies and signing configuration.
- Before signing is enabled, the maintainer must confirm MFA for GitHub and SignPath and configure the provider's required roles. This document does not claim those checks have already occurred.
- Each release-signing request requires explicit approval of its source revision and resulting artifacts by the maintainer.

## Signing scope

The intended scope is the native Windows application's four project-built files: `NoticeSender.exe`, `NoticeSender.dll`, `NoticeSender.Core.dll` and `NoticeSender.Automation.dll`. Microsoft runtime dependencies preserve their vendor signatures and licensing. macOS releases are not covered by this Windows signing application.

Builds must originate from reviewed source in this repository on GitHub-hosted Windows runners. Signing tokens belong in a protected CI environment; untrusted pull requests must not receive them. The provider must verify artifact origin. Signed results require a complete file/signature audit and an independently obtained approved publisher certificate thumbprint before local execution tests. Self-signed trust installation and security-policy changes are not part of this process.

The app handles local data and only transmits notices when requested by its operator through KakaoTalk. See the [privacy policy](PRIVACY.md) for local data, optional features and third-party services. Development reports containing personal information are excluded from source and signing submissions.

Implementation and remaining prerequisites: [Windows signing guide](https://github.com/SleepingDawn/notice-sender-mac/blob/codex/windows-port/Windows/SIGNING.md). Provider terms: [SignPath Foundation](https://signpath.org/terms.html).
