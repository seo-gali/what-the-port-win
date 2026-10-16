# 기능별 커밋·푸시

- 원격: `https://github.com/seo-gali/what-the-port-win.git`
- 브랜치: `main`; 기존 구현 커밋 완료 상태 확인 (`c6bc94a`까지)
- 이전 차단: `git add`의 `.git/index.lock` 생성 권한 거부
- 재실행: 사용자 권한 허용에 따라 아래 두 그룹의 커밋·푸시 진행. 로컬 수정·빌드·검증 완료. GitHub Pages는 사용자가 게시 완료; 원격 ZIP은 Pages 배포 후 갱신

## 그룹

1. README 소개·다운로드 링크를 실제 Pages 주소로 변경
2. 재실행 시 기존 창 활성화·첫 조작 전 자동 숨김 방지·시작 통합 테스트·새 ZIP·가이드

일반 사용자 터미널 또는 `.git` 쓰기·네트워크가 허용된 세션에서 실행:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/commit-features.ps1 -Push
```

기존 staged 변경이 있으면 중단. 이미 커밋된 그룹은 생략. 강제 푸시·히스토리 재작성 없음. Git 작성자 정보·원격 인증은 사용자의 기존 Git 설정 사용.
