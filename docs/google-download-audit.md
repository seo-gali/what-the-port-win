# Google 다운로드 가이드라인 점검 · 2026-10-13

## 결론

**전체 준수 확인·악성코드 없음·Google 경고 해제로 판정할 수 없다.** 다운로드 설명과 주요 동작은 확인했으나, 실제 배포 EXE에서 기존 보안 문제 5건을 재확인했다. 코드 서명은 없고 백신 검사는 수행하지 못했다. 아래 판정은 자체 점검이며 Google의 공식 심사 결과가 아니다. 발견한 취약점이 Chrome 경고를 발생시킨 원인이라는 증거는 없다.

## 검사 대상과 증거

- 소스 커밋: `274c423` (`feat(distribution): offer standalone Windows EXE download`).
- 공개 파일: `https://seo-gali.github.io/what-the-port-win/downloads/WhatThePort.exe`.
- 파일: 696,320 bytes / x64 / .NET Framework 4.8.
- SHA-256: `71ce423cf90fa58fcd125668efaf40106ee21d5c9b76749746fd240fac19793f`.
- 공개 HTTPS 응답: 200, 리디렉션 없음, `application/octet-stream`. 공개 파일·로컬 EXE·공개 무결성 보고서의 해시/용량 일치.
- Authenticode: `NotSigned`. 게시자·타임스탬프 없음. 파일 버전 정보의 ProductName/CompanyName은 비어 있고 FileVersion은 0.0.0.0.
- Windows Defender: AntivirusEnabled=false, RealTimeProtectionEnabled=false, 서명 갱신 시각 없음. 백신 검사 결과 아님. 다른 백신의 검사 완료도 확인하지 않았다.
- 증거 로그: `artifacts/google-download-release-check.json`, `artifacts/google-audit-exe.json`, `artifacts/security-audit.txt`.

## 가이드라인과 현재 상태

기준: [Google 다운로드 가이드라인](https://developers.google.com/search/docs/monitor-debug/security/malware#guidelines), [원치 않는 소프트웨어 정책](https://www.google.com/about/unwanted-software-policy.html).

| 점검 항목 | 결과 | 근거·제약 |
|---|---|---|
| 명확한 다운로드와 사용자 선택 | 확인 | 이름·Windows용 EXE·파일 형식·크기 표시. 직접 링크이며 자동 다운로드 코드 없음. |
| 기능 설명과 실제 동작 | 확인 | 포트·프로젝트·메모리·CPU·프로세스 정리 설명. 자동 정리 기본 Off. 즉시 종료 방식 공개. |
| 사칭·허위 경고·광고 | 확인 | 비공식 Windows 이식 표시. 분석·광고 스크립트 없음. 성능 측정 한계 공개. |
| 추가 프로그램·설치 | 확인 | Core·Geist 폰트 내장. 추가 소프트웨어 자동 설치·업데이트 기능 없음. |
| 브라우저 설정·보호 경고 | 확인 | 브라우저 설정·확장·루트 인증서·보호 기능 변경 코드 없음. |
| 게시자 확인 | 권고 미충족 | 공인 코드 서명 없음. 파일의 게시자 메타데이터도 보완 필요. |
| 라이선스 | 부분 확인 | 다운로드 페이지에 MIT 링크. EXE의 --licenses로 MIT/OFL 열람. 앱 설정에서 쉽게 접근하도록 개선 권장. |
| 데이터 처리 설명 | 보완 필요 | 포트·프로세스·폴더·Git 정보 조회, 설정/링크 로컬 저장. 의도된 텔레메트리 없음. Git 경로가 외부 파일·UNC를 가리킬 수 있어 통신 부재를 보장할 수 없음. |
| 종료·제거 | 부분 확인 | 트레이 Quit은 타이머·단축키·아이콘을 해제. 홈페이지·가이드에는 포터블 파일 삭제와 로컬 설정 제거 안내가 부족함. |
| 안전한 프로세스 제어 | 보완 필요 | 보호 정책과 종료 전 identity 검증이 있으나 S3/S4의 트리·관측 위험이 남음. |
| 악성코드 검사·Google 평판 | 미검증 | 정적 검토/해시 검사로 대체하지 않음. Google 재검토를 제출하지 않았음. |

다운로드 가이드라인은 공인 서명을 권장하면서도 **미서명만으로 원치 않는 소프트웨어로 표시하지는 않는다**고 명시한다. 코드 서명이나 SHA-256 확인은 악성코드 검사·평판 판정과 별개다.

## 실제 배포 EXE에서 재확인한 문제

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/security-audit.ps1 -SingleFile`은 공개 EXE를 참조해 기존 무해한 증거 프로브를 실행한다. 종료 코드 0은 **예상한 취약점 관측이 재현되었다**는 뜻이며 안전 검사 통과가 아니다. 사용자 프로세스는 종료하지 않았고 실제 원격 공유에 접근하지 않았다.

| 항목 | 결과 | 수정 방향 |
|---|---|---|
| S1 · 외부 Git 메타데이터 | 프로젝트 밖의 공개 marker HEAD를 읽어 Branch로 표시. UNC 문자열의 경로 조합도 허용. 실제 SMB 인증 전송은 미실시. | 파일 I/O 전에 로컬 최종 경로를 검증하고 원격·장치·reparse 경로 차단. worktree 지원에는 명시적 신뢰 경계 필요. |
| S2 · 터미널 실행 경로 | 현재 폴더의 무해한 wt.exe fixture가 실행되어 자체 marker 생성. | 터미널과 중첩 PowerShell의 검증된 절대 경로 사용. |
| S3 · 부모 PID 재사용 | 합성 그래프에서 이전 세대의 오래된 orphan이 새 부모의 자식으로 편입. 실제 OS 경주는 미실시. | 부모·자식 생성 시각을 각 edge에서 검증하고 불확실한 소유권은 제외. |
| S4 · 불완전한 관측 | 자동 정리 후보 조건에 하위 프로세스 관측 완전성·하위 연결 검증이 없음. 실제 사용자 서버 자동 종료는 미실시. | 부분 관측 시 후보 제외, idle 시간 재시작, 트리의 연결·활동 재확인. |
| S5 · 손상된 링크 | 자체 JSON의 null 폴더 레코드로 NullReferenceException 재현. | 로드 시 레코드 검증과 null-safe 조회. |

자세한 위협 모델과 재현 한계는 [기존 보안 검수](security-audit.md)에 있다. 이번 점검은 수정 완료를 의미하지 않는다.

## 제거 안내에 필요한 내용

단일 EXE 사용자는 트레이 메뉴의 **앱 종료 / Quit**을 선택한 뒤 다운로드한 `WhatThePort.exe`만 삭제할 수 있다. **× / Ctrl+Alt+P는 종료가 아니라 숨김**이다. 설정·프로젝트 링크까지 지우려면 앱 종료 후 본인이 확인한 `%LOCALAPPDATA%\WhatThePort` 폴더를 선택적으로 삭제한다. 프로젝트 폴더는 삭제 대상이 아니다. 이 점검 중 사용자 설정이나 파일을 삭제하지 않았다.

소스의 선택 설치 스크립트를 사용했다면 `%LOCALAPPDATA%\Programs\WhatThePort`, 시작 메뉴의 `What the Port.lnk`, StartWithWindows를 선택한 경우 시작프로그램의 같은 바로가기까지 제거하는 안내가 필요하다. 앱 EXE 직접 실행에는 이 설치 동작이 없다.

## 재검토에 사용할 수 있는 범위

현재는 “가이드라인 전부 준수, 백신 검사 통과”라고 제출하면 안 된다. 경로·실행·정리·링크 문제를 수정하고, 라이선스·데이터·제거 정보를 쉽게 찾도록 보완한 뒤 최종 EXE의 실제 백신 검사 결과를 확보한다. 공인 서명도 추가하는 것이 바람직하다.

앞서 사용자가 보여 준 Search Console 보고서는 **감지된 문제 없음**이었다. 현재 계정 보고서를 직접 확인하거나 Google에 검토 요청을 제출한 것은 아니다. 보고서에 **일반적이지 않은 다운로드**가 실제로 표시될 때, 보완된 파일의 가이드라인 준수 여부를 확인한 후 해당 보고서의 검토 요청을 사용한다. 확인되지 않은 상태를 심사 완료로 작성하지 않는다. [Google 절차](https://support.google.com/webmasters/answer/9044101?hl=ko).