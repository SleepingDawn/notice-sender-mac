# Release completion policy

사용자가 요청한 기능 추가·수정 또는 버그 수정이 완료되면, 별도 요청을 기다리지 않고 다음 마감 절차를 수행한다.

1. `Resources/Info.plist`와 `Resources/DevVersion.txt`의 앱 버전·빌드 번호를 갱신한다.
2. 검증 후 `./scripts/install-app.sh`로 `/Applications/공지발송.app`을 최신 빌드로 교체한다.
3. 변경 사항을 `main`에 커밋하고 `origin/main`으로 푸시한다.

