# 다운로드·인증서 보안 점검

현재 기본 다운로드는 단일 GUI EXE이며 터미널용 ZIP도 선택할 수 있다. 두 다운로드 모두 HTTPS로 제공하지만 Authenticode 서명은 없다. 단일 EXE는 압축 해제 없는 실행 편의를 위한 것으로, 확장자 변경만으로 Chrome 경고가 사라진다는 뜻은 아니다. SHA-256·ZIP 구성 검사와 악성코드 검사, Chrome 평판은 서로 다른 결과다. 무결성 검사를 통과했다고 안전 판정 또는 경고 해제를 주장하지 않는다.

## 2026-10-22 인증서 보안 점검

대상은 What the Port Windows **1.1.2**와 공개 배포 경로다. 아래 결과는 해당 날짜의 읽기 전용 점검 기록이며, 이후 인증서 갱신·OS 업데이트 상태는 다시 확인해야 한다. 인증서 검사와 악성코드 검사, 코드 서명, Secure Boot 상태를 구분한다.

### eMudhra / emSign 중간 CA 개인 키 유출

Cloudflare는 2026-10-20 공지에서 eMudhra 중간 인증서 8개의 키가 손상되어 공격자가 다른 도메인의 인증서를 위조할 수 있다고 설명했다. 실제 방어 여부는 OS·브라우저·런타임·자체 신뢰 저장소 중 어떤 검증 경로를 쓰는지에 따라 다르다. [Cloudflare 공식 공지와 개발자 조치](https://blog.cloudflare.com/emudhra-certificate-compromise/).

| 대상 | 확인한 내용 | 판단 |
|---|---|---|
| 다운로드 사이트 `seo-gali.github.io` | `*.github.io` → Let's Encrypt YR1 → Root YR → ISRG Root X1. 당시 서버 인증서 만료일은 2026-12-29 UTC | 확인한 체인에 eMudhra/emSign 없음. HTTPS 기본 검증과 온라인 폐기 확인을 포함한 Windows 체인 검사 통과 |
| 소스 저장소 `github.com` | Sectigo Public Server Authentication CA DV E36 → Root E46. 당시 서버 인증서 만료일은 2026-11-29 UTC | 확인한 체인에 eMudhra/emSign 없음. 같은 방식의 체인 검사 통과 |
| 앱·빌드 의존성 | .NET Framework 4.8 / WPF. 앱 자체의 외부 HTTPS API 클라이언트·자체 CA 묶음 없음. Python `certifi`, Go, Electron, NSS를 앱에 포함하지 않음 | 해당 패키지의 프로젝트 내 교체 대상은 발견하지 못함. 브라우저·외부 CLI는 각 도구의 업데이트 상태를 따로 확인 |
| 배포 EXE | 1.1.2, Authenticode `NotSigned` | 코드 서명은 미완료. 이번 CA 사고 점검을 게시자 신원 확인이나 백신 안전 판정으로 해석하지 않음 |

점검한 EXE의 SHA-256은 `38f000df3c0f6888170b1a3e4d9484cfc8d12f16f40a026d7354f3d275ef6725`다. 현재 배포 파일 정보는 [EXE 확인 정보](downloads/EXE-INFO.json)와 [SHA-256 목록](downloads/SHA256SUMS.txt)을 참고한다.

HTTPS는 인증서 검증을 끄지 않고 조회했다. 체인 검사는 `X509Chain.Build`, `RevocationMode=Online`으로 수행했고 두 호스트 모두 체인 오류가 없었다. 이는 **관측한 서버 인증서가 유효했다는 결과**다. 이 PC가 모든 eMudhra 위조 인증서를 차단하는지, 모든 사용자의 브라우저·백그라운드 도구가 패치됐는지는 검증하지 않았다. Windows와 브라우저 보안 업데이트, 별도로 사용하는 CLI·런타임의 공식 보안 업데이트를 확인해야 한다.

### Windows Secure Boot 인증서 전환

Secure Boot는 OS 부팅 단계의 신뢰 경로다. 일반 사용자 모드에서 실행하는 이 앱의 코드 서명·HTTPS와 별도로 관리한다. Microsoft는 2011년 인증서를 2023년 인증서로 전환하도록 안내한다. 미전환 장치가 즉시 부팅 불능이 된다는 뜻은 아니지만 이후 부팅 보안 업데이트·보호에 제약이 생길 수 있다. [Windows 클라이언트 공식 조치](https://learn.microsoft.com/en-us/troubleshoot/windows-client/windows-security/update-secure-boot-certificates).

Microsoft가 명시한 인증서 만료일은 다음과 같다. 특히 **Microsoft UEFI CA 2011은 2027년이 아니라 2026년 6월 27일**이다. [Microsoft 공식 인증서 일정](https://learn.microsoft.com/en-us/azure-stack/operator/secure-boot-certificates?view=azs-2601).

| 기존 인증서 | 만료일 |
|---|---|
| Microsoft Corporation KEK CA 2011 | 2026-06-24 |
| Microsoft Windows Production PCA 2011 | 2026-10-19 |
| Microsoft UEFI CA 2011 | 2026-06-27 |

점검한 개발 PC의 레지스트리에는 `UEFISecureBootEnabled=0`, `UEFICA2023Status=NotStarted`가 표시됐다. 이는 **해당 개발 PC 한 대의 관측값**이며 사용자 PC 전체에 대한 결과가 아니다. `Confirm-SecureBootUEFI`와 `Get-SecureBootUEFI`는 권한 부족으로 실패했으므로 펌웨어의 실제 인증서 등록 상태나 전환 완료 여부를 확정하지 않는다.

다음은 상태를 읽는 명령이다. 레지스트리 조회는 수행했으며, 펌웨어 명령은 관리자 권한으로 다시 확인할 필요가 있다.

```powershell
Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' |
    Select-Object UEFISecureBootEnabled
Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\Servicing' |
    Select-Object UEFICA2023Status, UEFICA2023Error
Confirm-SecureBootUEFI
([Text.Encoding]::ASCII.GetString((Get-SecureBootUEFI -Name db).Bytes)).Contains('Windows UEFI CA 2023')
([Text.Encoding]::ASCII.GetString((Get-SecureBootUEFI -Name KEK).Bytes)).Contains('Microsoft Corporation KEK 2K CA 2023')
```

필요한 조치는 제조사의 BIOS/UEFI 호환성·업데이트 안내와 Windows Update를 확인하고, 적용 후 공식 상태 지표를 다시 검사하는 것이다. 레지스트리의 상태값을 직접 바꿔 완료로 표시하거나 기존 부팅 인증서를 임의 삭제하지 않는다. 이번 점검에서는 펌웨어·신뢰 저장소·부팅 설정 변경이나 OS 업데이트 설치를 수행하지 않았다.

### 미확인·후속 항목

- 개발 PC와 사용자 환경의 eMudhra 차단 패치 적용 여부: 미확인. 정상 사이트의 HTTPS 접속 성공만으로 차단 완료를 판단하지 않는다.
- 개발 PC의 Secure Boot 2023 인증서 등록·부트 매니저 전환: 관리자 권한으로 추가 확인 필요.
- EXE 코드 서명과 기존 IObit 탐지의 제조사 판정·독립적인 악성코드 검사: 미완료. 이번 인증서 점검으로 해결됐다고 주장하지 않는다.

## 경고 구분

“일반적으로 다운로드되지 않으며 위험할 수 있습니다”는 Google Safe Browsing이 아직 충분히 알지 못하는 파일에 대한 **Uncommon downloads** 경고다. 악성코드 검출·HTTPS 혼합 콘텐츠 경고와 구분한다. Google의 평가 전까지 경고가 남을 수 있고, 서명만 추가해도 즉시 사라진다고 보장할 수 없다.

[Google의 경고 설명](https://support.google.com/chrome/answer/6261569?hl=ko) · [Search Console의 Uncommon downloads 항목](https://support.google.com/webmasters/answer/9044101?hl=ko)

## IObit `Suspicious.Startup` 보고

2026-10-21 사용자가 IObit Malware Fighter에서 1.1.0 EXE의 `Suspicious.Startup` 탐지를 보고했다. 해당 다운로드 파일은 공개 배포본과 SHA-256이 일치했다. 해시는 `07a5afd6601fc6640d2d12f5574baaba91a126d0eb8613cc6c354a4ec03e5078`이며, Authenticode 서명은 없다. 파일 일치는 악성코드 검사 통과나 오탐 확정을 뜻하지 않는다.

앱은 사용자가 설정의 Windows 로그인 시 시작을 켜고 저장할 때만 현재 사용자 시작프로그램 폴더에 `What the Port.lnk`를 생성한다. 대상은 현재 EXE와 `--background`이며, 끄고 저장하면 해당 바로가기만 삭제한다. 등록에는 Windows의 `WScript.Shell` 바로가기 API를 사용한다. 후속 로그 대조에서 탐지가 `STARTUP` 유형이었고 해당 바로가기의 제거 기록이 있음을 확인했다. 정확한 탐지 규칙·오탐 여부는 제조사가 확인하지 않았다. 점검 시 실제 시작프로그램 폴더에는 이 바로가기가 없었다.

IObit 공식 설명서의 [Report False Positive](https://www.iobit.com/product-manuals/imf-help/) 절차로 해당 파일·탐지명을 제조사에 검토 요청할 수 있다. 백신 보호 해제나 예외 추가를 해결책으로 안내하지 않는다. 제조사 검토·독립적인 악성코드 검사 결과는 아직 없으며, 새 UI 버전 배포만으로 이 탐지가 해결됐다고 주장하지 않는다.

### 2026-10-22 로컬 백신 재검사

배포 파일을 실행하지 않고 별도 검사 폴더에 복사해 설치된 IObit의 공식 우클릭 검사로 검사했다. 대상은 1.1.2 단일 EXE·ZIP과 ZIP에서 꺼낸 GUI EXE·CLI EXE·Core DLL, 비교용 1.1.0 EXE다. 각 복사본의 SHA-256을 검사 전후 확인했으며 여섯 파일 모두 유지됐다.

| 항목 | 기존 탐지 | 재검사 |
|---|---|---|
| 완료 시각 · PC 로컬 시간 | 2026-10-21 19:41:45 | 2026-10-22 20:33:40 |
| IObit 버전 / 엔진 | 13.5.0.1684 / 13.0.0.1634 | 동일 |
| IObit DB 버전 | 2637 | 2638 |
| 보고서의 BDVersion | 7.101686 · 2026-10-16 GMT | 7.101696 · 2026-10-18 GMT |
| 검사 방식 | AutoCare-Idle / Smart Scan | Manual / Right Scan |
| 검사 개체 | 108,764개 | 70개 |
| 결과 | `Suspicious.Startup` 1건 · 유형 `STARTUP` | **Completed · Threats Detected: 0** · 14초 |

실제 텍스트 보고서에서 검사 경로가 준비한 복사본 폴더와 일치함을 확인했다. 기존 보고서의 MD5 `d9f49ecd797bd21d27dfc2730cf8ef36`도 SHA-256으로 식별한 1.1.0 비교 파일과 일치했다. IObit의 시작프로그램 백업 기록과 사용자가 제공한 격리 목록 화면에는 `What the Port.lnk`의 2026-10-21 제거 기록이 있었다. 이는 기존 탐지가 자동 실행 항목 분류였다는 근거이며, EXE 내부의 감염이나 오탐 여부를 확정하는 분석은 아니다.

한계와 후속 확인:

- 재검사는 설치된 IObit의 파일 검사 결과다. 기존 시작프로그램 스마트 검사와 방식·DB 버전이 달라 시작프로그램 등록 후 재탐지 여부나 규칙 수정 여부는 확인하지 못했다. 점검을 위해 자동 실행을 다시 등록하지 않았다.
- 보고서의 BDVersion은 기록했지만 선택형 Bitdefender 엔진의 실제 활성 여부와 제조사 최신 DB 여부는 별도로 검증하지 않았다.
- Windows Defender는 `Not running`, `AntivirusEnabled=false`여서 다른 제품을 이용한 독립 검사 결과는 얻지 못했다. 보호 설정이나 서비스 상태는 변경하지 않았다.
- IObit의 **이번 검사에서 탐지되지 않았음**을 확인했다. 악성코드 부재·전체 보안 통과·기존 탐지의 오탐 확정을 뜻하지 않는다.
- 원본 로그·검사 파일 목록·해시와 제조사 요청 초안은 로컬 `artifacts/antivirus-review-20261022/`에 보관했다. 외부 제출과 제조사의 정확한 판정은 아직 완료하지 않았다.

## 단일 EXE

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-single-file.ps1`은 Core와 UI 코드를 하나의 일반 .NET 어셈블리로 컴파일한다. WPF 마크업·Geist 폰트·MIT/OFL 전문을 내장한다. 외부 DLL이나 폰트를 임시 폴더에 풀어내는 로더·압축기는 사용하지 않는다. `--licenses`로 라이선스를 열람할 수 있다. Windows의 .NET Framework 4.8은 필요하며, `wtp.exe` CLI는 선택 ZIP에 포함된다.

`test-single-file.ps1`은 Core·UI 회귀 검사, 실제 내장 폰트 해석, EXE 한 개뿐인 폴더에서 시작·재실행·트레이·조회·라이선스 표시를 검사한다. `verify-single-file.ps1`은 x64 메타데이터·내장 리소스 이름·외부 Core 참조 부재·SHA-256·Authenticode를 확인한다. `docs/downloads/EXE-INFO.json`은 이 확인 결과이며 악성코드 검사 결과가 아니다.

## 구현한 배포 절차

- `scripts/package.ps1`: 앱 소스·빌드 입력이 같고 기존 실행 파일의 해시가 일치하면 재빌드를 생략한다. 문서만 바뀌어도 EXE/DLL을 다시 컴파일하지 않는다. 패키지 입력·ZIP 해시까지 같으면 ZIP도 그대로 유지한다. 강제 재빌드는 `-Rebuild`.
- `scripts/verify-package.ps1`: 압축 파일의 SHA-256, 전체 항목, 필수 파일, 중복·경로 순회·뜻밖의 실행 파일, EXE/DLL의 Authenticode를 확인한다. 검사 보고서는 `artifacts/package-verification.json`. 악성코드 검사나 Chrome 평판 검사가 아니다.
- `scripts/sign.ps1`: Windows 인증서 저장소의 공인 코드 서명 인증서로 EXE 두 개·Core DLL에 SHA-256 서명과 타임스탬프를 적용한다. 이미 같은 인증서로 유효하게 서명된 파일은 다시 서명하지 않는다. `-SingleFile`이면 내장형 GUI EXE만 서명한다. PFX·암호·비밀 키 파일을 읽거나 저장하는 기능은 없다.
- `scripts/prepare-site.ps1`: 기본값은 신뢰된 서명과 타임스탬프가 없는 새 배포물의 준비를 거부한다. 미서명 시험 배포는 `-AllowUnsigned`를 명시해야 한다.
- 웹사이트 문구 수정만 할 때는 기존 ZIP을 재생성하지 않는다. 다운로드 주소·내용을 숨기거나 파일 확장자·보호 설정을 변경해 검사를 피하지 않는다.

## 로컬 시험 빌드

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-site.ps1 -AllowUnsigned
```

`-AllowUnsigned`는 경고를 제거하지 않는다. 시험 배포임을 알고 쓰는 옵션이다. 인증서가 없다면 정식 서명 배포를 완료할 수 없다.

## 공인 인증서가 준비된 이후

1. 실제 게시자 명의의 코드 서명 인증서를 Windows 인증서 저장소에 설치한다. 인증서 구입·계정 생성은 별도 결정이다. 자체 서명 인증서를 사용자의 신뢰 목록에 추가하도록 요구하지 않는다.
2. 아래처럼 최초 패키지 빌드로 캐시를 만든 후 서명하고, 서명된 파일을 재컴파일하지 않고 최종 패키지를 만든다.
3. 유효한 백신 환경에서 해당 **최종 ZIP과 내부 파일**을 검사하고 결과를 기록한다. 현재 작업 PC의 Defender 비활성 상태를 검사 통과로 처리하지 않는다.
4. 사이트 준비가 성공한 동일 ZIP을 배포한다. 서명·검사 후 파일을 다시 수정하면 다시 검증한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
$certificateThumbprint = Read-Host '설치한 공인 코드 서명 인증서의 공개 지문'
$timestampUrl = Read-Host '인증기관이 안내한 Authenticode 타임스탬프 URL'
.\scripts\sign.ps1 -CertificateThumbprint $certificateThumbprint -TimestampServer $timestampUrl
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-single-file.ps1
.\scripts\sign.ps1 -SingleFile -CertificateThumbprint $certificateThumbprint -TimestampServer $timestampUrl
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-single-file.ps1 -RequireSignature
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -RequireSignature
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-site.ps1
```

Windows PowerShell의 타임스탬프 공급자는 `http://` URL을 요구한다. 앱·ZIP 다운로드 주소는 계속 HTTPS다. 타임스탬프 URL은 인증기관의 실제 안내값을 사용한다.

[Microsoft Authenticode 서명 문서](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/set-authenticodesignature?view=powershell-5.1)

## Google 소유권 확인과 검토

[Google Search Console](https://search.google.com/search-console/)에서 **URL 접두어** 속성 `https://seo-gali.github.io/what-the-port-win/`를 추가한다. `github.io` 도메인 전체의 DNS 소유권을 요구하는 속성은 선택하지 않는다.

소유권 확인에 Google이 발급한 HTML 파일 방식 또는 HTML 메타 태그 방식을 사용한다. HTML 파일은 `docs/`에 Google이 지정한 이름·내용 그대로 두고 배포한 후 확인한다. 메타 태그는 Google이 지정한 값을 홈페이지 head에 넣는다. 확인용 값은 게시에 필요한 공개 값이며 Google 계정 암호를 제공할 필요는 없다. 소유권 확인 후에도 확인 파일·태그는 유지한다.

**보안 문제** 보고서에 **Uncommon downloads**가 있다면 배포·서명·백신 검사 결과를 실제로 준비한 뒤 **검토 요청**을 사용한다. 재검토 여부·결과는 Google에서 결정한다. 보고서에 해당 문제가 없거나 검토가 진행 중이면 신청 완료·경고 해제를 주장하지 않는다.

사용자가 확인한 현재 보고서는 **감지된 문제 없음**이다. 사이트 소유권 확인은 완료됐지만 다운로드 파일의 평판 경고가 해제됐다는 의미는 아니다. 이 상태에서는 보안 문제 보고서에 검토 요청 버튼이 나타나지 않는다. Chrome의 **도움말 → 문제 신고**로 파일 URL과 경고 화면을 전달할 수 있지만 파일 재검토·응답·해제를 보장하는 절차는 아니다. [Chrome 문제 신고 안내](https://support.google.com/chrome/answer/95315?hl=ko).

[Google 소유권 확인 방법](https://support.google.com/webmasters/answer/9008080?hl=ko) · [보안 문제와 검토 절차](https://support.google.com/webmasters/answer/9044101?hl=ko)

### 검토 요청에 넣을 확인 정보

- 배포 페이지: `https://seo-gali.github.io/what-the-port-win/`
- 기본 EXE 주소: `https://seo-gali.github.io/what-the-port-win/downloads/WhatThePort.exe`
- 선택 ZIP 주소: `https://seo-gali.github.io/what-the-port-win/downloads/WhatThePort-Windows-x64.zip`
- 공개 소스: `https://github.com/seo-gali/what-the-port-win`
- 실제 SHA-256: `docs/downloads/SHA256SUMS.txt`
- 서명·파일 정보: `docs/downloads/EXE-INFO.json`, `docs/downloads/PACKAGE-INFO.json`
- 실제 백신 검사 결과와 검사 날짜: 검사한 후에만 기재
- 설명: Windows 로컬 TCP 리스너·사용자 프로세스를 표시하는 포터블 WPF 도구이며 자동 정리는 기본 Off. 네트워크 통신·텔레메트리·자동 업데이트·추가 소프트웨어 설치는 앱 기능에 포함하지 않음.

로컬 IObit 재검사 결과는 위 기록대로 안내하되, 코드 서명·제조사 판정·독립적인 백신 검사까지 완료됐다고 작성하지 않는다. 코드 서명·Google 심사 완료 전에는 Chrome 경고 해제가 미완료 상태다.
