# Windows 신뢰 서명 준비

2026-11-04 기준 사용자는 **Smart App Control을 유지하고 신뢰 서명을 준비**하기로 선택했고, 원본 코드 권한 보유를 확인하며 MIT 적용 및 무료 SignPath 심사 신청을 승인했습니다. 현재 배포본은 서명되지 않았으며 실제 카카오톡 텍스트·파일 전송 시험은 재개하지 않았습니다.

## 현재 확인한 대상

`dist/NoticeSender-Windows` 전체의 EXE/DLL을 읽기 전용으로 검사합니다. 현재 로컬 보고서에는 458개 중 Microsoft 배포 파일 454개의 서명이 Valid이고 아래 4개가 NotSigned입니다. 빌드에 따라 파일 개수는 달라질 수 있습니다.

- `NoticeSender.exe`
- `NoticeSender.dll`
- `NoticeSender.Core.dll`
- `NoticeSender.Automation.dll`

EXE만 서명하면 충분하지 않습니다. 이 네 파일을 모두 같은 승인된 발행자로 서명하고 원래 Microsoft DLL은 재서명하지 않습니다. 공개 신뢰 Authenticode 인증서가 필요합니다. .NET 강력한 이름, GitHub 증명, 자체 서명 인증서 설치는 이 절차를 대신하지 않습니다. [Microsoft SAC 서명 안내](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)

## 신청 경로와 필요한 결정

| 경로 | 현재 조건 | 다음 결정 |
|---|---|---|
| SignPath Foundation | 오픈소스 프로젝트 무료. OSI 라이선스, 유지보수·공개 릴리스·프로젝트 평판 심사, 담당자 MFA 및 서명 정책 필요. 발행자는 SignPath Foundation. 승인·소요 기간 보장 없음 | 소유자 승인으로 MIT 적용. 제공자 필수 동의를 승인받아 2026-11-04 신청 제출, 양식의 Form submitted 확인. 제공자 심사 대기 |
| Certum 개인용 Standard cloud | 공식 페이지 표시 가격 €209부터. 개인 신원·주소 확인, SimplySign 모바일/PC 앱 필요. 선택 기간·세금·실제 결제액 및 한국 서류 수용 여부는 결제 전 확인 필요 | 유료 가입·신원 확인을 할지 결정. 개인정보는 제공자 보안 페이지에 사용자가 직접 제출 |
| Microsoft Artifact Signing | 현재 공개 신뢰 개인은 미국·캐나다만 지원. 한국은 조직 경로에 포함되나 개인 SNU 이메일만으로 조직 자격을 대신할 수 없음 | 실제 발행 권한을 가진 조직이 참여할 때 별도 검토 |

근거: [SignPath 신청 조건](https://signpath.org/terms.html), [신청 페이지](https://signpath.org/apply.html), [Certum Standard](https://shop.certum.eu/standard-code-signing-in-the-cloud.html), [Certum 필요 서류](https://support.certum.eu/en/code-signing-required-documents/), [Microsoft 국가·신원 조건](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart).

Certum 오픈소스 상품도 €49부터 표시되지만 현재 페이지에는 품절이라고 나옵니다. 즉시 이용할 경로로 가정하지 않습니다. [상품 원문](https://shop.certum.eu/open-source-code-signing-on-simplysign.html)

## 준비한 검증 도구

저장소 루트에서 실행합니다. 첫 명령은 바이너리를 실행하지 않고 상태·발행자·해시를 JSON으로 기록합니다.

```powershell
./scripts/test-windows-signatures.ps1 -ReportPath outputs/windows-signatures-before-signing.json
```

서명된 결과를 받은 후 제공자 페이지/인증서 정보에서 **별도로 확인한** 발행자 인증서 지문을 지정합니다. 아래 값은 예시 자리이며 실제 지문으로 바꿔야 합니다.

```powershell
./scripts/test-windows-signatures.ps1 `
  -PublishDirectory dist/NoticeSender-Windows-signed `
  -ReportPath outputs/windows-signatures-after-signing.json `
  -RequireSigned -ExpectedApplicationSignerThumbprint '<APPROVED_CERTIFICATE_THUMBPRINT>'
```

검증은 모든 EXE/DLL의 Windows 서명 상태, 네 앱 파일의 누락·추가, 내장 Authenticode 서명과 예상 발행자를 검사합니다. 실패하면 보고서를 남기고 오류로 끝납니다. 보고서 경로는 제품 파일을 덮어쓰지 않도록 배포 폴더 밖이어야 합니다. 지문을 검사 대상 파일에서 자동 추출해 그대로 신뢰하지 않습니다. 인증서가 교체되면 승인된 새 지문을 별도로 확인합니다.

이 검사는 파일을 로드하거나 실행하지 않으며 SAC 허용·실제 발송 성공을 증명하지 않습니다. 해시는 서명으로 바뀌므로 서명 전후 보고서를 각각 보존합니다. 차단된 자동화 DLL은 재실행하지 않습니다. 별도 자동화 모듈을 참조하지 않는 Core 테스트는 원래 SDK 실행 경로로 로컬 23/23 통과했으며, 이 결과와 실제 카카오톡 연동 검증은 구분합니다.

## SignPath 승인 후 연결할 순서

1. 사용자 권한 확인 및 MIT·신청 승인은 받았습니다. [코드 서명 정책](https://github.com/SleepingDawn/notice-sender-mac/blob/codex/windows-port/CODE_SIGNING_POLICY.md)과 [개인정보 정책](https://github.com/SleepingDawn/notice-sender-mac/blob/codex/windows-port/PRIVACY.md)을 공개하며, MFA와 제공자 역할 설정은 활성화 전에 확인합니다. 사실과 다른 무료 서명 제공 문구를 미리 게시하지 않습니다.
2. SignPath 심사를 받고 프로젝트·정책·인증서를 받습니다. 이 시점 전에는 GitHub 앱 권한이나 비밀 토큰을 등록하지 않습니다.
3. GitHub 호스팅 Windows 작업에서 현재 빌드·단위 검사·격리 smoke를 수행합니다. 최종 공개 소스 커밋, 실행 ID와 서명 전 해시를 보존합니다. 학생 DB·대화·개인 진단 보고서는 제출하지 않습니다.
4. `dist/NoticeSender-Windows/` **내용**을 직접 Actions artifact로 업로드합니다. 그 ZIP 루트에 앱 파일 네 개가 있어야 합니다. 기존 배포 ZIP을 다시 감싼 이중 ZIP과 혼동하지 않습니다. `signing/signpath-artifacts.xml.example`은 이 구조용 초안이며 등록 전 실제 artifact 및 제공자 스키마로 검증해야 합니다.
5. 제공받은 프로젝트·정책·조직 ID를 사용하는 수동 승인 서명 작업을 연결합니다. 제출 토큰은 GitHub 보호된 환경의 secret에 보관합니다. 서명 action과 artifact action의 호환 버전·커밋을 확인하고 고정합니다. 임의 PR·fork가 서명 자격을 얻지 않게 제한합니다.
6. 서명 결과를 별도 폴더로 받고 원래 소스·입출력 파일 목록과 대조합니다. 위 엄격 검증을 통과한 뒤 서명된 배포 ZIP을 만듭니다. 재빌드는 서명 뒤에 하지 않습니다.
7. 이 PC에서 **정상 경로의 서명된 앱**을 실행해 SAC와 진단을 확인합니다. 차단되면 해당 서명·정책 이벤트를 확인하고 제공자 경로로 해결합니다.
8. 앱 자체 드라이런 후 사용자가 허용한 봇에서만 텍스트·TXT/CSV·단일/다중 첨부·첨부 단독·PDF·PNG·중단·보호된 재시도를 재개합니다. 불명확한 전송은 자동 반복하지 않습니다.

연결 기준: [SignPath GitHub origin 검증·action](https://docs.signpath.io/trusted-build-systems/github), [artifact 구성 예제](https://docs.signpath.io/artifact-configuration/examples). 이 문서는 서명 준비·신청 접수 기록이며 서비스 승인, 서명 또는 실제 발송 완료 보고가 아닙니다.
