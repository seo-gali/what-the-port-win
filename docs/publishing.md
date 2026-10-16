# Windows 다운로드·소개 페이지

- 구성: 의존성 없는 정적 HTML / CSS / JavaScript
- 공개 루트: `docs/`; `docs/.nojekyll`로 Jekyll 생략
- 준비: 정식 배포는 `scripts/prepare-site.ps1`에서 신뢰된 서명·타임스탬프 확인. 미서명 시험 배포만 `-AllowUnsigned` 명시
- 로컬 미리보기: `node scripts/serve-site.mjs` → `http://127.0.0.1:4173`
- 다운로드: 기본 `docs/downloads/WhatThePort.exe`, 선택 CLI `docs/downloads/WhatThePort-Windows-x64.zip` — 실제 다운로드를 위해 Git 추적 대상 포함
- 내용: 홈, 기능·화면 소개, 이미지 사용 가이드, 라이선스, 404, 고정 헤더·모바일 메뉴, 단일 EXE·선택 ZIP·체크섬
- 배포: GitHub Pages의 main / docs. 프로젝트 AGENTS.md에 따라 외부 쓰기·퍼블리싱 전 사전 승인 필요

실제 호스팅 적용 시 HTTPS, CSP, nosniff, Referrer-Policy 및 실제 HTTP 404 매핑 확인 필요. 로컬 서버의 헤더 설정은 `scripts/serve-site.mjs` 기준. 생산용 웹 서버로 사용하지 않음.

## GitHub Pages 설정

저장소 **Settings → Pages → Build and deployment → Deploy from a branch → main → /docs → Save**.

공개 주소: `https://seo-gali.github.io/what-the-port-win/`.
GitHub 저장소의 `blob/main/docs/index.html` 링크는 소스 보기 주소이며 웹사이트 주소와 별개.

현재 404의 복귀·스타일 경로는 `/what-the-port-win/` 기준. 저장소 이름 변경이나 사용자 지정 도메인 적용 시 `404.html`과 미리보기·검증 스크립트의 prefix 변경 필요.

기준: [GitHub 공식 게시 소스 설정 안내](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).

## 준비·검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-single-file.ps1
# Run package.ps1 only when the optional CLI ZIP needs an update.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-site.ps1 -AllowUnsigned
node scripts/serve-site.mjs
# 다른 터미널
node scripts/check-site.mjs
```

프로젝트 경로 미리보기: `http://127.0.0.1:4173/what-the-port-win/`.
검증 범위: 320·360·390·430·768·1024·1440px의 홈·가이드·라이선스·404, 가로 넘침, 스크롤 시 헤더 고정, 키보드 메뉴·탭, 목차 이동 위치, 이미지 확대·Esc 복귀, 대체 텍스트, 라이선스 원문 일치, 로컬 링크, 중첩 주소 404, EXE·ZIP·SHA-256. 휴대폰 구간은 Chrome의 모바일·터치 에뮬레이션으로 확인한다. 실제 iOS Safari·Android 기기 검증은 별도다.

사이트 이미지는 최신 앱의 데모 캡처를 `docs/images/`에 두고 준비 스크립트로 `docs/assets/images/`에 복사한다. 한국어 6개 화면, 영어 설정 화면, 보호 사유 도움말을 사용한다. README 소개 이미지 `docs/images/cover.jpg`는 실제 앱 캡처와 구분한 생성 일러스트다.

홈페이지는 원래 README 일러스트의 어두운 네이비 배경과 청록·보라·분홍 네온 서버를 디자인 기준으로 삼는다. 홈페이지 전용 레이아웃은 `assets/home.css`, 공통 페이지 색상과 탐색은 `assets/site.css`에서 관리한다. 홈페이지의 `assets/images/hero-servers.png`는 원래 README 그림을 참조해 내장 imagegen으로 제작한 투명 배경 일러스트이며 앱 패키지에는 포함하지 않는다. 제목·버튼·설명은 이미지에 넣지 않고 반응형 HTML로 표시한다. README의 원본 표지는 별도로 유지한다.

가이드의 이미지 링크는 JavaScript를 사용하면 원본 크기의 확대 창을 열고, 스크립트가 꺼져 있으면 이미지 파일로 이동한다. 라이선스 전문은 `license.html`에 표시하며 `LICENSE.txt` 다운로드도 제공한다.

앱 패키지에는 Markdown 사용 문서·이미지만 포함. 사이트 다운로드 ZIP의 자기 포함·반복 빌드 시 용량 증가 방지.
로컬 서버의 보안 응답 헤더는 GitHub Pages에 자동 이전되지 않음.

Chrome 다운로드 경고, 공인 코드 서명, ZIP 내용 유지와 Google 검토 절차: [다운로드 보안 절차](download-security.md). 웹사이트 수정만으로 공개 ZIP을 재생성하지 않는다. 기본 사이트 준비는 미서명 배포물을 거부하며, 시험 배포에서만 위의 AllowUnsigned 옵션을 명시한다.
