# 접근성·화면 검수 · 2026-10-05

> 과거 검수 기록이다. 아래 결과·건수는 당시 빌드와 환경에만 해당한다. 현재 동작은 [사용 가이드](usage.md)을 확인하며, 이 기록을 현재 버전의 접근성 인증으로 사용하지 않는다.

대상: WPF 목록·상세·정리·설정·링크·빈 상태, 웹 홈·가이드·404. 웹과 네이티브 접근성 API를 구분한 검수. **WCAG 전체 적합성 인증은 아님.**

## 키보드·스크린리더

| 항목 | 조치·검증 |
|---|---|
| 앱 키보드 | Enter 열기, Tab 순회, Space 선택, Esc 복귀의 WPF 키 이벤트 |
| 포커스 | 화면 전환 시 입력·복귀 위치, 재렌더링 후 footer 버튼·차트 포커스 유지 |
| 아이콘 | 홈·뒤로·핀 상태·숨김·터미널·세션의 UI Automation 이름 |
| 차트 | 종류·범위·표본 시각·값 텍스트, ←/→/Home/End 탐색 |
| 입력 | 입력·선택·체크박스 이름, 콤보박스 포커스 테두리 |
| 메시지 | 성공·오류 텍스트의 polite live region 및 변경 이벤트 |
| 언어 | 한국어·영어 설정 저장 즉시 적용, 입력·버튼·차트의 접근성 이름 동시 전환 |
| 보호 상태 | 비활성 체크박스의 보호 사유 UIA help text·tooltip, 모든 항목 보호 시 별도 안내 |
| 정렬 | 목록·정리 화면의 열 좌표 일치, 포커스 테두리로 인한 크기 변화 제거 |
| 웹 키보드 | skip link, 모바일 메뉴 Enter/Escape·Tab, 탭 방향키·End |
| 웹 읽기 | Chromium 접근성 트리에서 메뉴·이미지·탭/tabpanel 확인 |
| 이미지 | 정보성 이미지 alt, 장식 로고 빈 alt, 홈 링크 이름 |
| 대비 | 앱 보조 텍스트 4.06→5.85:1, 입력 경계 1.46→3.16:1; 웹 focus-visible·reduced-motion·forced-colors |

기준: [W3C 키보드](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html), [대체 텍스트](https://www.w3.org/WAI/WCAG22/Understanding/non-text-content.html), [WPF live region](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/text_livesetting), [.NET 접근성 설정](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/whats-new-in-accessibility).

## 요청 항목별 결과

| 항목 | 결과 |
|---|---|
| 가로 스크롤·모바일 넘침 | 앱 본문 가로 스크롤 비활성; 웹 320/390/768/1440px의 모든 페이지 넘침 없음 |
| 깨진 링크·푸터 링크 | 내부 파일·앵커·다운로드 HTTP 확인; 원본 GitHub 접근 확인 |
| 모바일 메뉴 | 홈·가이드 메뉴 토글, expanded 상태·Escape·포커스 복귀 |
| 파비콘 | 모든 페이지의 점 격자 SVG |
| 제목·메타 | 웹 고유 title·description, 404 noindex; 앱 현재 화면 제목 |
| 맞춤 404 | 홈·다운로드 복귀 링크 및 HTTP 404 |
| 저작권 연도 | 원본 LICENSE의 2026 유지, 현재 연도와 일치 |
| 이미지 압축 | PNG 1,444,740→JPEG 135,176 bytes, 90.6% 절감; UI 캡처는 PNG 유지 |
| 버튼·로고 | 앱 홈·설정·정리·링크, 웹 홈 로고·메뉴·탭·다운로드 점검 |
| 성공·오류 | 앱 저장·복사·정리·입력 오류, 웹 다운로드 링크 안내·404; 완료되지 않은 다운로드를 성공으로 표시하지 않음 |
| 플레이스홀더 | 실제 이미지·패키지·내용 사용. 명령 예시의 교체용 세션 ID는 명시적 안내 |
| 미사용 탐색 | 미구현 Mac 전용 TUI·자동 업데이트 메뉴 없음; 한국어·영어 언어 설정 지원 |
| 모든 페이지 모바일 대응 | 홈·가이드·404의 4개 폭 직접 브라우저 확인 |

## 검증·한계

- Core/통합 88개, 앱 UI 82개, CLI 2개, 다중 인스턴스 시작 3개 및 live smoke 통과. 연결된 4개 모니터의 실제 물리 창 배치 확인.
- EXE 실행 직후 포커스 상실로 인한 창 숨김 방지, 첫 조작 이후 자동 숨김·Esc·× 검증. 다른 폴더의 두 EXE로 기존 인스턴스 열기·숨김 후 다시 열기 확인.
- `node scripts/check-site.mjs`: 실제 Chromium 렌더링·키 입력·접근성 트리·HTTP 점검 90개 통과. `/what-the-port-win/` 경로의 중첩 404, 다운로드 체크섬 포함.
- 캡처: `artifacts/site-screenshots`의 PC·모바일 화면.
- 미실시: Narrator/NVDA 실제 음성 청취, 점자 장치, 실제 터치 기기, OS 고대비 전체 앱 검수, 물리 혼합 DPI 이동.
- 접근성 이름·트리 검증만으로 모든 보조공학 조합의 완전한 동작을 보장하지 않음.

## 직접 확인

1. 마우스 없이 Tab/Shift+Tab → Enter/Space → Esc로 이동.
2. 앱 차트의 방향키·Home/End로 표본 값 확인.
3. Narrator/NVDA로 버튼 이름·입력 레이블·메시지·차트 설명 청취.
4. 웹 첫 Tab의 본문 건너뛰기, 모바일 메뉴 Escape, 화면 탭 방향키 확인.
5. 확대·고대비·다른 모니터에서 읽기와 포커스 확인.
