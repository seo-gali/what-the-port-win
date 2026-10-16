# Chrome 다운로드 경고 대응

현재 기본 다운로드는 단일 GUI EXE이며 터미널용 ZIP도 선택할 수 있다. 두 다운로드 모두 HTTPS로 제공하지만 Authenticode 서명은 없다. 단일 EXE는 압축 해제 없는 실행 편의를 위한 것으로, 확장자 변경만으로 Chrome 경고가 사라진다는 뜻은 아니다. SHA-256·ZIP 구성 검사와 악성코드 검사, Chrome 평판은 서로 다른 결과다. 무결성 검사를 통과했다고 안전 판정 또는 경고 해제를 주장하지 않는다.

## 경고 구분

“일반적으로 다운로드되지 않으며 위험할 수 있습니다”는 Google Safe Browsing이 아직 충분히 알지 못하는 파일에 대한 **Uncommon downloads** 경고다. 악성코드 검출·HTTPS 혼합 콘텐츠 경고와 구분한다. Google의 평가 전까지 경고가 남을 수 있고, 서명만 추가해도 즉시 사라진다고 보장할 수 없다.

[Google의 경고 설명](https://support.google.com/chrome/answer/6261569?hl=ko) · [Search Console의 Uncommon downloads 항목](https://support.google.com/webmasters/answer/9044101?hl=ko)

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

현재 서명과 백신 검사가 완료됐다고 작성하지 않는다. 코드 서명·Google 심사 완료 전에는 Chrome 경고 해제가 미완료 상태다.
