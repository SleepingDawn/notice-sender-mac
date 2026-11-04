# 첨부 완료 확인용 OCR 구성

첨부 완료 확인 시험에는 배포 폴더의 `ocr/tesseract.exe`, 의존 DLL과 `ocr/tessdata/eng.traineddata`, `kor.traineddata`가 필요합니다. 일반 화면 실행에 필요한 .NET 런타임과는 별도입니다. OCR 구성 파일이 있어도 모든 첨부·카카오톡 버전의 완료 확인이나 상대방 수신이 검증된 것은 아닙니다. 정확히 확인하지 못한 항목은 `확인 필요`로 남깁니다.

개발·배포 담당자는 이미 확보하고 검토한 로컬 OCR 폴더를 지정합니다. 빌드 도구가 다운로드하거나 설치하지 않습니다.

```powershell
./scripts/build-windows.ps1 -Publish -OcrRuntimeDirectory 'D:\verified-ocr' -RequireBotReceiptRuntime
```

`NOTICE_SENDER_OCR_RUNTIME` 환경 변수로 같은 폴더를 지정할 수도 있습니다. 폴더를 지정하면 자동으로 배포본에 포함합니다. 첨부 완료 확인이 필요한 배포에는 `-RequireBotReceiptRuntime`을 함께 사용하세요. 이 옵션에서 폴더를 생략하면 빌드·게시를 시작하기 전에 실패합니다. 미지정 일반 빌드는 OCR 생략 경고를 표시하며, 기존 출력 폴더의 OCR을 조용히 재사용하지 않습니다.

입력 폴더에는 `runtime-dependency-manifest.json`과 그 `files`에 선언한 실행 파일·DLL·언어 모델, `doc/LICENSE`, `doc/AUTHORS`, `doc/README.md`가 있어야 합니다. 선언한 파일마다 `name`, `length`, `sha256`이 필요합니다. `name`은 경로가 없는 파일명이며, 모델은 입력 폴더 바로 아래 또는 `tessdata`에 둘 수 있습니다. 두 위치에 있으면 양쪽 바이트가 일치해야 합니다. 선언하지 않은 DLL이나 다른 파일, 중복 이름, 경로 이동, 재분석 지점, 누락·변경된 파일은 거부합니다.

패키징은 모든 선언 파일의 크기·SHA256을 검증하고, 실행 파일 및 영문·한국어 모델은 앱의 `AttachmentVisualRecognizer`에 고정된 SHA256과도 비교합니다. 출력 모델은 `ocr/tessdata`에 모으며, 포함한 문서와 파일의 상대 경로·크기·해시를 새 배포 명세에 기록합니다. 입력 명세의 컴퓨터별 원본 경로는 복사하지 않습니다. 문서 해시는 복사한 고지문 바이트의 기록이며, 실행 파일 신뢰·코드 서명·전체 런타임 실행 검증을 뜻하지 않습니다. 보안 설정이나 서명 상태를 바꾸지 않습니다.
