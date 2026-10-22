# What the Port for Windows

로컬 개발 서버의 포트·메모리·CPU를 확인하고 선택한 서버를 정리하는 Windows 트레이 앱입니다.

[EXE 다운로드](https://seo-gali.github.io/what-the-port-win/downloads/WhatThePort.exe) · [사용 가이드](https://seo-gali.github.io/what-the-port-win/guide.html) · [1.1.3 변경 기록](CHANGELOG.md)

![What the Port 소개 일러스트](docs/images/cover.jpg)

## 시작

1. Windows 10/11 x64에서 `WhatThePort.exe`를 실행합니다. .NET Framework 4.8이 필요합니다.
2. 개발 서버를 실행하고 **Ctrl+Alt+P** 또는 트레이 아이콘으로 목록을 엽니다.
3. 서버를 선택하면 상세 그래프·폴더·프로세스와 실행 버튼이 나옵니다.

업데이트할 때는 트레이 메뉴의 **앱 종료**로 기존 앱을 닫고 새 EXE를 실행하세요. 터미널 명령이 필요하면 [CLI 포함 ZIP](https://seo-gali.github.io/what-the-port-win/downloads/WhatThePort-Windows-x64.zip)을 폴더째 풀어 사용합니다.

현재 다운로드는 **코드 서명이 없는 시험 빌드**입니다. 브라우저·백신 경고와 검사 기록은 [다운로드 보안 안내](docs/download-security.md)를 확인하세요.

## 주요 동작

- **서버 조회:** 포트·프로젝트·연결 앱·자원 사용량과 최근 10분 그래프. 목록 상단 요약은 스크롤해도 유지됩니다.
- **창 조절:** 제목을 드래그하고 −·+로 창·글자·그래프를 함께 조절합니다. 위쪽 창은 상단, 아래쪽 창은 하단을 유지합니다. 전환은 Windows 애니메이션 설정을 따릅니다.
- **프로젝트 연결:** 상세의 **연결**에서 Codex·Claude 세션과 Vercel 미리보기 URL을 등록합니다. 터미널·세션·미리보기는 해당 버튼으로 실행합니다.
- **서버 정리:** 선택한 프로세스 트리를 즉시 종료합니다. 셸·에이전트·시스템·데이터베이스 등 보호 대상은 제외하며 자동 정리는 기본 꺼짐입니다. 저장되지 않은 서버 작업은 유실될 수 있습니다.

설정에서 한국어·영어, 조회 간격, 자원 알림, 배경 불투명도와 로그인 자동 실행을 변경합니다. 알림의 **1시간 쉬기**는 다시 누르면 해제됩니다.

## 개발

C# · WPF · .NET Framework 4.8. Windows 내장 빌드 도구를 사용하며 외부 패키지를 내려받지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
.\dist\WhatThePort.exe --demo
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-single-file.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

ZIP 생성 위치는 `artifacts/WhatThePort-Windows-x64.zip`입니다. 문서·폰트·라이선스가 함께 포함됩니다. 변경 시 [코딩 기준](CODING_STANDARDS.md) · [디자인](DESIGN.md) · [게시 절차](docs/publishing.md)를 따릅니다.

## 지원 범위와 보안

Windows 네이티브 TCP와 동일 사용자·로그인 세션을 조회합니다. UDP, WSL·Docker 내부 자원, ARM64는 지원·검증 범위에 포함하지 않습니다. 측정값은 공유 메모리·스캔 간격에 따른 오차가 있습니다.

텔레메트리·자동 업데이트·앱 자체의 외부 네트워크 요청은 없습니다. 설정과 연결 정보는 `%LOCALAPPDATA%\WhatThePort`에 저장하며, 브라우저·터미널 실행은 사용자 조작 시에만 수행합니다. 외부 프로젝트 메타데이터와 실행 파일 탐색 등 미해결 사항 5건은 [보안 검수 보고서](docs/security-audit.md)에 기록되어 있습니다.

## 라이선스

[Tomjohn Design의 WhatThePort](https://github.com/tomjohndesign/what-the-port)를 기반으로 한 비공식 Windows 이식입니다. [MIT](LICENSE) · 원본 © 2026 Tomjohn Design LLC. Geist·Geist Mono는 SIL Open Font License이며 `assets/fonts/OFL.txt`와 배포 파일에 포함합니다. 위 표지는 소개용 생성 일러스트입니다.
