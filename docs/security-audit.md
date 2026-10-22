# 보안 검수 · 2026-10-05

대상: Windows 앱·CLI·Win32 연동·JSON 저장소·빌드/설치 스크립트·정적 다운로드 사이트. **아래 5건은 현재 코드에 남아 있는 발견 사항. 보안 수정 완료를 뜻하지 않음.** 정적 분석과 제한된 재현 결과이며, 모든 취약점의 부재를 보장하지 않음.

eMudhra 사고·배포 HTTPS·개발 PC Secure Boot의 2026-10-22 점검 결과와 미확인 항목은 [다운로드·인증서 보안 점검](download-security.md#2026-10-22-인증서-보안-점검)에 기록한다.

## 1. Vulnerability Summary

| 심각도 | 건수 | 항목 |
|---|---:|---|
| Critical | 0 | 확인된 항목 없음 |
| High | 2 | S1 외부 메타데이터 경로 접근, S2 터미널 실행 파일 탐색 가로채기 |
| Medium | 2 | S3 부모 PID 재사용에 따른 트리 오귀속, S4 불완전한 관측의 자동 정리 |
| Low | 1 | S5 손상된 링크 레코드의 화면 오류 |

High 등급은 공격 전제가 충족될 때의 영향 기준. S1 실제 SMB 인증 전송, S3 OS PID 재사용 경주, S4 장시간 실제 자동 종료는 미실시. 각 항목에서 재현과 추론을 구분.

### 위협 모델

| 공격자 | 진입점·능력 | 신뢰 경계·자산 |
|---|---|---|
| 익명 사이트 방문자 | URL·경로·HTTP 메서드 조작 | 공개 정적 파일; 인증·변경 API 없음 |
| 비신뢰 프로젝트 공급자 | ZIP의 메타데이터·프로젝트 파일 배치 | 프로젝트 데이터 → 파일 읽기·실행 권한 |
| 동일 사용자 로컬 프로세스 | 리스너·프로세스 트리·현재 폴더·사용자 JSON 변경 | 관측 데이터 → 종료 대상·프로세스 실행 |
| 다른 사용자 프로세스 | OS가 허용한 프로세스·공용 경로 접근 | Windows SID·세션·핸들 권한 |
| 내부자·배포 채널 침해자 | 소스·ZIP·사이트 변경 | 배포물 신뢰·사용자 권한 실행 |

민감 자산: 개발 서버의 미저장 상태, 사용자 권한 실행, 프로젝트 경로·세션 ID, Windows 통합 인증 정보, 배포물 무결성. 앱 계정·비밀번호·발급 토큰·서버 DB는 없음. 시크릿 파일·환경 변수·에이전트 대화 기록은 검수 중 열람하지 않음.

## 2. Detailed Findings

### S1. Git 메타데이터의 외부 경로 접근

- **심각도:** High. 인증 노출은 환경 의존적 잠재 영향.
- **구성 요소:** `src/Core/Scanner.cs:67`의 `ResolveProject`, `.git`의 `gitdir:` 해석 및 `HEAD` 읽기.
- **설명:** 프로젝트의 포인터를 따라가면서 로컬 볼륨·신뢰 저장소·reparse point 여부를 제한하지 않음. 절대 경로·UNC도 `Path.GetFullPath` 이후 `File.Exists`·`StreamReader`로 전달. 4096자 읽기 제한은 있지만 경로·원격 I/O 제한은 없음.
- **공격 순서:** ① ZIP 등으로 제공한 프로젝트의 `.git` 포인터 조작 → ② 피해자가 정상 개발 서버 실행 → ③ 자동 스캔이 외부 파일 또는 UNC 공유 조회 → ④ 허용된 SMB 환경에서 인증 교환·지연 가능.
- **영향:** 비의도적 파일 읽기, Branch/CLI를 통한 일부 내용 노출, 원격 파일시스템 I/O·스캔 정체. SMB 인증 응답 노출은 Windows 정책·네트워크 조건에 의존하며 비밀번호 평문 노출과 다름.
- **재현:** 자체 임시 프로젝트 밖의 공개 marker HEAD가 Branch에 노출되는 것을 확인. UNC 문자열의 경로 조합 통과만 확인했으며 실제 원격 접근·인증 전송은 미실시. 일반 Git clone이 `.git` 파일을 그대로 배포한다고 가정하지 않음.
- **권장 수정:** 파일 I/O 전에 로컬 볼륨 확인, UNC·장치 경로 및 원격 reparse 경로 차단. worktree 외부 gitdir는 명시적으로 신뢰한 로컬 저장소만 허용. 핸들 기반 최종 경로 검증과 취소 가능한 조회; 실패 시 메타데이터 생략.

인증 위험의 근거: [Microsoft SMB NTLM blocking](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-ntlm-blocking).

### S2. 이름만으로 실행하는 `wt.exe` 가로채기

- **심각도:** High.
- **구성 요소:** `src/Core/Launchers.cs:19`. Windows Terminal 내부의 `powershell.exe`도 절대 경로 미지정.
- **설명:** `ProcessStartInfo("wt.exe", …)`가 Windows의 실행 파일 검색에 의존. 현재 디렉터리 등에 배치된 동명 파일을 의도한 설치본으로 오인할 수 있음. `WorkingDirectory=folder`는 실행 파일을 신뢰된 경로로 고정하지 않음.
- **공격 순서:** ① 공격자가 검색 가능한 비신뢰 현재 디렉터리에 `wt.exe` 배치 → ② 피해자가 Windows Terminal 선호 설정 → ③ 터미널 열기·세션 재개 버튼 클릭 → ④ 동명 바이너리 실행.
- **영향:** 현재 사용자 권한의 임의 코드 실행. 관리자 권한 상승을 재현한 것은 아님.
- **재현:** 자체 임시 폴더의 무해한 fixture가 marker 파일만 작성하는 것을 확인. 실제 사용자 터미널·에이전트 실행 없음.
- **권장 수정:** 검증된 앱 실행 별칭/설치 경로를 절대 경로로 고정하고 출처 검증. 중첩 PowerShell도 시스템 디렉터리의 절대 경로 사용. 에이전트 CLI 검색 경로와 PowerShell 프로필 신뢰 분리.

실행 파일 검색의 근거: [Microsoft CreateProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa).

### S3. 부모 PID 재사용에 따른 하위 트리 오귀속

- **심각도:** Medium.
- **구성 요소:** `src/Core/Policy.cs:24`, `src/Core/Scanner.cs:38` 및 `ProcessControl.Stop`.
- **설명:** 트리 edge는 숫자 부모 PID로 구성. 자식 시작 시각은 루트와만 비교하므로 즉시 부모보다 오래된 자식을 차단하지 않음. 개별 PID+시작 시각의 종료 직전 검증은 잘못 연결된 관계 자체를 교정하지 못함.
- **공격 순서:** ① 루트 R의 과거 자식 P가 C를 만든 뒤 종료 → ② P의 PID가 R의 새 자식 Q에 재사용 → ③ 살아 있는 C가 Q의 자식으로 편입 → ④ C가 R보다 늦게 시작했다면 루트 기준 비교 통과 → ⑤ R 정리 시 C까지 종료 대상 가능.
- **영향:** 독립적으로 남아 있던 프로세스의 비의도적 종료·미저장 상태 손실. 원하는 PID 경주를 만드는 능력은 OS·부하에 의존.
- **재현:** 합성 그래프에서 오래된 orphan 편입과 루트 시각 비교 통과 확인. 실제 OS PID 재사용·비선택 사용자 프로세스 종료 미실시.
- **권장 수정:** 모든 부모·자식 edge의 생성 시각 검증. 세대 불일치·확인 불가능 edge 제외. 종료 계획에 부모 identity를 포함하고 종료 전 트리·리스너 경계 재검증.

### S4. 불완전한 관측 상태의 자동 정리 허용

- **심각도:** Medium.
- **구성 요소:** `src/Core/Scanner.cs:36`, 루트 `Connections` 집계, `src/Core/Policy.cs:35`.
- **설명:** 하위 프로세스 측정 실패를 `catch { continue; }`로 생략하며 관측 완전성 상태가 없음. 연결 수는 루트 PID의 리스닝 포트만 계산. worker 측정 실패·I/O 연결을 놓치면 루트의 낮은 CPU·연결 0으로 idle 시간이 유지될 수 있음.
- **공격 순서:** ① 루트는 대기, worker는 I/O 작업 → ② worker 조회 실패 또는 연결 집계 제외 → ③ Automatic 설정 상태에서 idle 기준 시간 경과 → ④ 일부만 관측한 트리를 종료 후보로 판정.
- **영향:** 작업 중인 트리의 자동 종료·작업 상태 손실. 기본 Off에서는 자동 경로 비활성.
- **재현:** 후보 predicate에 완전성·하위 연결 조건이 없음을 코드와 합성 상태로 확인. 실제 권한 경주·수시간 작업 종료 미실시. 합성 테스트만으로 실환경 악용 성공을 주장하지 않음.
- **권장 수정:** complete/incomplete/failed 관측 상태. 일부 실패 시 idle 시계 재시작·자동 종료 제외. 소유 트리의 활동 연결 고려 및 보수적인 후보 제한.

### S5. 링크 로드 이후 개별 레코드 검증 누락

- **심각도:** Low.
- **구성 요소:** `src/Core/Storage.cs:57`, `src/Core/Storage.cs:69`.
- **설명:** `LoadLinks`가 null 객체만 제거. `Folder:null`인 동일 포트 항목은 `LinkFor`의 `TrimEnd`에서 예외 발생.
- **공격 순서:** ① 저장소 변경 권한이 있는 동일 사용자 프로세스 또는 JSON 손상 → ② null 폴더 레코드 생성 → ③ 같은 포트 서버의 상세·링크 작업 → ④ 예외 및 화면 동작 실패.
- **영향:** 앱 가용성 저하·반복 오류. 원격 공격·권한 상승을 입증한 것은 아님.
- **재현:** 자체 격리 JSON의 null 폴더와 `LinkFor`의 NullReferenceException 확인.
- **권장 수정:** 로드 시 I/O 없는 구조 검증, 잘못된 항목 격리, null-safe 조회·복구 안내. 검증 자체가 `Directory.Exists`로 원격 I/O를 유발하지 않도록 S1과 함께 설계.

## 3. Attack Chains

1. **프로젝트 데이터 → 외부 경로 → 인증 노출 가능:** S1 포인터 조작 → 정상 서버 실행 → 자동 스캔 → UNC 조회 → 허용된 SMB 인증 교환. 마지막 단계는 미실시·환경 의존적 추론.
2. **비신뢰 경로 → 작업 복귀 버튼 → 코드 실행:** S2 동명 실행 파일 배치 → Windows Terminal 선택 → 터미널/세션 버튼 → 현재 사용자 권한 실행. 무해한 fixture로 마지막 단계 확인.
3. **잘못된 소유권 + 불완전한 idle → 비의도적 종료:** S3 오래된 edge 편입 + S4 관측 누락 → 자동 후보 → 개별 프로세스 identity가 일치하여 잘못 소유한 노드까지 통과 가능. 결합된 실환경 종료는 미검증.

## 4. Secure Design Recommendations

1. **배포 전:** S1 경로 신뢰 경계 및 S2 실행 파일 경로 고정. 프로젝트 데이터에서 네트워크 I/O·실행 권한으로 넘어가는 경로 차단.
2. **자동 정리 활성화 전:** S3 edge identity, S4 관측 완전성, 종료 직전 서비스 경계 재검증. 확신할 수 없는 노드는 종료 제외.
3. **저장소:** 로드·저장 양쪽 구조 검증, 동시 CLI/UI 저장의 lost update 방지. 사용자 ACL을 경계로 사용하되 같은 사용자 악성 코드까지 격리된 것으로 가정하지 않음.
4. **배포물:** Authenticode·신뢰된 배포 채널·독립된 릴리스 증명. 같은 서버의 ZIP·SHA-256 동시 변조는 체크섬만으로 방어 불가.
5. **사이트:** `docs`만 공개. 호스팅에서 HTTPS·CSP·nosniff·프레임 차단·실제 404 적용. 로컬 미리보기 서버는 생산용으로 사용하지 않음.

### 계층별 검토 범위

| 계층 | 결과 |
|---|---|
| 앱 UI·CLI | 단축키·문자열 출력·프로세스 작업 검토. 세션 문자 제한·PowerShell single-quote escaping 확인. 명령 주입 방어와 S2 검색 경로는 별개 |
| 웹 UI | 정적 HTML/CSS/JS. innerHTML·사용자 입력 삽입·폼 없음. URL을 오류 화면에 반사하지 않음. 모바일·키보드·링크·경로 순회 HTTP 점검 |
| Backend·API | 제품 API·수신 서비스 없음. 미리보기는 127.0.0.1의 GET/HEAD 전용; POST 405·경로 순회 403 |
| 인증·권한 | 계정·JWT·비밀번호 재설정 없음. SID·세션·프로세스 핸들 권한 의존. asInvoker 및 종료 직전 시작 시각 확인 |
| DB·저장소 | SQL/NoSQL 없음. 사용자 JSON, 1 MB 읽기 제한, 임시 파일 후 원자 교체. S5와 동시 갱신 손실 가능성 |
| 브라우저 저장소 | cookie/localStorage/sessionStorage·토큰 저장 없음 |
| 통합 | 등록된 HTTPS Vercel 링크를 브라우저로 전달. DNS·redirect 이후 사이트는 보장 범위 밖. 터미널·CLI 경계는 S2 |
| 의존성 | 신규 NuGet/npm·lock 변경 없음. OS/.NET 패치 수준의 전체 CVE 대조 미실시 |
| 인프라 | 클라우드·DB·버킷 배포 없음. 실제 호스팅 TLS·헤더·접근 제어·공급망 미검증 |

### 추가 잠재 위험 — 건수에서 제외

- 자식이 스캔 후 별도 listener로 전환되는 경주: 원자적 서비스 소유권 보장 없음. 종료 직전 경계 테스트 필요.
- 이름이 고정된 single-instance 이벤트/뮤텍스 선점: 동일 권한 공격자의 시작 방해 가능성. SID 포함 이름·ACL 검토; 실제 선점 미실시.
- 실행 파일 이름 기반 보호는 이름 변경·중간 실행기를 신뢰성 있게 식별하지 못할 수 있음.
- Windows UIA·메시지는 OS 세션·무결성 경계를 따르며 동일 사용자 악성 프로세스에 대한 sandbox가 아님.
- 세션 ID·프로젝트 경로 평문 저장은 인증 비밀 저장과 다르지만 업무 정보의 민감도에 따른 접근 정책 필요.

### 재현

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/security-audit.ps1
```

결과: evidence 6개(S1 2개, S2–S5 각 1개). `FINDING`은 취약점 발견 표시이며 보안 인증 통과가 아님. 기록: `artifacts/security-audit.txt`. 네트워크 인증·사용자 프로세스 종료·시크릿 접근 없음.

## Google 다운로드 가이드라인 재점검 · 2026-10-13

현재 단일 EXE의 SHA-256 일치 후 `scripts/security-audit.ps1 -SingleFile`로 기존 프로브를 실행했다. S1~S5가 다시 확인됐으며, S1의 경로 조합 관측을 포함해 6개 증거 관측이다. 프로브의 종료 코드 0은 문제 재현 성공을 뜻하며 보안 통과 판정이 아니다. 정책 항목·서명·백신·제거 안내 점검은 [다운로드 점검 보고서](google-download-audit.md)에 기록했다. 앱 소스나 공개 EXE는 이 점검에서 변경하지 않았다.
