using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhatThePort.App {
    // English keys stay stable for control tags; only visible text and accessibility names change.
    public sealed class Strings {
        public string Language;
        public Strings(string language){Language=language;}
        public string T(string value){string translated;return value!=null&&Language=="ko"&&Korean.TryGetValue(value,out translated)?translated:value;}
        public string F(string template,params object[] values){return String.Format(CultureInfo.CurrentCulture,T(template),values);}
        public string Duration(TimeSpan time){if(Language!="ko")return Format.Duration(time);if(time.TotalDays>=1)return ((int)time.TotalDays)+"일";if(time.TotalHours>=1)return ((int)time.TotalHours)+"시간";return time.TotalMinutes>=1?((int)time.TotalMinutes)+"분":"1분 미만";}
        public string StopSummary(StopResult result){return result.ListenerStopped ? result.Errors.Count==0 ? F(":{0} · listener stopped; {1} process(es) stopped.",result.Port,result.Stopped) : F(":{0} · partial stop: listener stopped, {1} process(es) stopped, {2} error(s).",result.Port,result.Stopped,result.Errors.Count) : F(":{0} · listener was not stopped; {1} error(s).",result.Port,result.Errors.Count);}
        public string Error(string value){
            if(value==null||Language!="ko")return value;
            if(value.StartsWith("+",StringComparison.Ordinal)&&value.EndsWith(" in 10 min",StringComparison.Ordinal))return F("+{0} in 10 min",value.Substring(1,value.Length-11));
            foreach(var pair in Korean)if(value==pair.Key)return pair.Value;
            foreach(string prefix in new[]{"High memory · ","High CPU · ","Scan unavailable. ","Could not stop servers. ","Settings could not be loaded. ","Project links could not be loaded. "})
                if(value.StartsWith(prefix,StringComparison.Ordinal))return T(prefix)+Error(value.Substring(prefix.Length));
            return value;
        }
        static readonly Dictionary<string,string> Korean=new Dictionary<string,string>{
            {"Close","닫기"},{"Process exit confirmation timed out.","종료 요청 후 완료 확인 시간이 초과되었습니다."},
            {"Smaller panel","창 작게"},{"Larger panel","창 크게"},{"BACKGROUND OPACITY","배경 불투명도"},
            {"Start when I sign in to Windows","Windows 로그인 시 자동 실행"},{"Starts this EXE in the tray. Save again after moving the file.","현재 EXE를 트레이에서 시작합니다. 파일을 옮기면 다시 저장해 주세요."},
            {"Resume alerts","알림 재개"},{"Resume alerts · paused until {0}","알림 재개 · {0}까지 중지"},{"Pause or resume resource notifications","리소스 알림 일시 중지 또는 재개"},{"Alerts resumed.","알림을 재개했습니다."},
            {"Started via","실행 출처"},{"Connected apps","연결 앱"},{"No identified local apps","확인된 로컬 앱 없음"},{"Via {0}","{0}에서 실행"},{"Connected: {0}","연결: {0}"},
            {"Servers","서버"},{"Clean up","서버 정리"},{"Settings","설정"},{"Project links","프로젝트 연결"},{"Links","연결"},
            {"Cancel","취소"},{"Save preferences","설정 저장"},{"Save links","연결 저장"},{"Save project links","프로젝트 연결 저장"},{"Discard changes","변경 취소"},
            {"Servers home","서버 목록"},{"Back to servers","서버 목록으로"},{"Back to servers (Esc)","서버 목록으로 (Esc)"},{"Back to server","서버 상세로"},
            {"Keep panel open","창 열린 상태 유지"},{"Unpin panel","창 고정 해제"},{"Hide to tray","트레이로 숨기기"},{"Hide to tray (Esc)","트레이로 숨기기 (Esc)"},
            {"Move panel (Ctrl+Shift+arrows)","창 이동 (Ctrl+Shift+방향키)"},
            {"Ctrl+Alt+P is already in use. Open the panel from its tray icon.","Ctrl+Alt+P를 다른 앱에서 사용 중입니다. 트레이 아이콘으로 열어 주세요."},
            {"DEMO · SAMPLE DATA · NO SYSTEM ACTIONS","데모 · 예시 데이터 · 실제 실행 없음"},
            {"Scan unavailable. ","조회할 수 없습니다. "},{"Could not stop servers. ","종료 중 문제가 발생했습니다. "},
            {"Protected process","보호됨 · 종료 불가"},{"Protected server","보호된 서버 · 종료 불가"},{"Protected","보호됨"},
            {"This app or a parent process. Stopping it could close your working session.","이 앱 또는 상위 실행 프로세스입니다. 종료 시 작업 세션이 닫힐 수 있어 보호됩니다."},
            {"Terminal, agent, system, database or container process. Stop it in its own app.","터미널·에이전트·시스템·DB·컨테이너는 보호됩니다. 해당 앱에서 직접 종료해 주세요."},
            {"This process is protected by the safety policy.","안전 정책에 따라 종료가 금지된 프로세스입니다."},
            {"Idle {0} · no connections","{0} 동안 유휴 · 연결 없음"},{"up {0}","실행 {0}"},{"High memory · ","메모리 높음 · "},{"High CPU · ","CPU 높음 · "},{"+{0} in 10 min","10분간 +{0}"},
            {"CPU (servers)  {0}","서버 CPU  {0}"},{"freed by stopping {0}","{0}개 종료 시 확보"},{"■ Servers {0}   ▪ Other {1}   ▪ Free {2}","■ 서버 {0}   ▪ 기타 {1}   ▪ 여유 {2}"},
            {"Resident working set; shared pages may appear in more than one process. System RAM: {0}","실제 사용 메모리. 공유 영역의 중복 집계 가능. 시스템 RAM: {0}"},
            {"No development servers","실행 중인 개발 서버 없음"},
            {"Show all user listeners","모든 사용자 리스너 표시"},{"Include custom executables and other local apps","사용자 정의 실행 파일과 다른 로컬 앱도 표시"},
            {"Stopping ends the selected process trees immediately.","선택한 서버와 하위 프로세스를 즉시 종료합니다."},
            {"Stop {0} servers · free {1}","서버 {0}개 종료 · {1} 확보"},{"Stop {0} server · free {1}","서버 {0}개 종료 · {1} 확보"},
            {"Cancel cleanup","서버 정리 취소"},{"Stop selected server process trees","선택한 서버와 하위 프로세스 종료"},{"Select servers to stop (C)","종료할 서버 선택 (C)"},
            {"{0} servers · monitoring","서버 {0}개 · 관찰 중"},{"Settings (Ctrl+,)","설정 (Ctrl+,)"},{"Select server {0}","서버 {0} 선택"},
            {"{0} · port {1} · {2} · {3}","{0} · 포트 {1} · {2} · {3}"},
            {"That server has stopped.","이 서버가 더 이상 포트를 열고 있지 않습니다."},{"Running for {0}","실행 시간 {0}"},{"Stop server","서버 종료"},
            {"Session","세션"},{"Branch","브랜치"},{"Folder","폴더"},{"Ports","포트"},{"Not linked","연결 미등록"},{"No Git branch","Git 브랜치 없음"},{"Unavailable for this process","폴더 확인 불가"},
            {"{0}   ·   {1} connections","{0}   ·   연결 {1}개"},{"Memory","메모리"},{"Processes   ·   {0}   ·   {1}","프로세스   ·   {0}개   ·   {1}"},
            {"Open localhost:{0}","localhost:{0} 열기"},{"Open local server (O)","로컬 서버 열기 (O)"},{"Open project in terminal (T)","터미널에서 프로젝트 열기 (T)"},
            {"Resume linked agent session (A)","연결된 에이전트 세션 재개 (A)"},{"Open Vercel preview (V)","Vercel 미리보기 열기 (V)"},
            {"Manage project links and copy local URL","프로젝트 연결 관리 및 로컬 URL 복사"},
            {"Stop :{0} and its {1} process(es)?\n\nThis ends the process tree immediately.",":{0} 서버와 관련 프로세스 {1}개를 종료할까요?\n\n프로세스를 즉시 종료합니다."},
            {"Demo: selected servers removed. No processes were stopped.","데모: 선택 항목을 제거했습니다. 실제 프로세스는 종료하지 않았습니다."},
            {":{0} · no longer listening (already stopped or changed).",":{0} · 더 이상 리스너가 아닙니다(이미 종료되었거나 상태 변경)."},
            {":{0} · automatic cleanup skipped after recheck.",":{0} · 재검사 후 자동 정리 대상에서 제외되었습니다."},
            {":{0} · listener stopped; {1} process(es) stopped.",":{0} · 서버 본체 종료, 프로세스 {1}개 종료 완료."},
            {":{0} · partial stop: listener stopped, {1} process(es) stopped, {2} error(s).",":{0} · 일부 종료: 서버 본체 종료, 프로세스 {1}개 종료·오류 {2}건."},
            {":{0} · listener was not stopped; {1} error(s).",":{0} · 서버 본체 종료 실패·오류 {1}건."},
            {"A server disappears when it stops listening, even if a child could not be stopped.","포트가 닫히면 목록에서 사라집니다. 종료하지 못한 하위 프로세스가 남을 수 있습니다."},
            {"Protected processes skipped: {0}.","보호 프로세스 {0}개 종료 제외."},{"Additional errors: {0}. See the stop result details.","추가 오류 {0}건. 종료 결과 상세에서 확인 가능."},
            {"Stop result details","종료 결과 상세"},{"Automatic clean up","자동 정리"},{"Demo: {0} action simulated.","데모: {0} 동작을 모의 실행했습니다."},
            {"Register this server's exact session ID to resume it.","재개할 서버의 정확한 세션 ID를 등록해 주세요."},{"Register the Vercel preview URL for this project.","이 프로젝트의 Vercel 미리보기 URL을 등록해 주세요."},
            {"LANGUAGE / 언어","언어 / Language"},
            {"SAMPLE INTERVAL · seconds (1–30)","조회 간격 · 초 (1~30)"},{"MEMORY ALERT · GB","메모리 경고 · GB"},{"GROWTH ALERT · MB / 10 min (0 = off)","메모리 증가 경고 · MB / 10분 (0 = 끄기)"},
            {"CPU ALERT · % of this computer","CPU 경고 · 전체 CPU 대비 %"},{"Desktop notifications","바탕 화면 알림"},{"AUTOMATIC CLEAN UP","자동 정리"},{"Off","끄기"},{"Ask","알림 후 직접 선택"},{"Automatic","자동 종료"},
            {"IDLE THRESHOLD · hours (0.05–720)","유휴 기준 · 시간 (0.05~720)"},{"Only continuously observed idle servers are eligible. Protected and alerting servers are never stopped automatically.","계속 관측된 유휴 서버만 정리 대상입니다. 보호되거나 경고 상태인 서버는 자동 종료하지 않습니다."},
            {"PREFERRED TERMINAL","기본 터미널"},{"Show all user TCP listeners","모든 사용자 TCP 리스너 표시"},{"Snooze alerts for 1 hour","알림 1시간 쉬기"},{"Pause resource notifications","자원 경고 알림 일시 중지"},{"Alerts snoozed for one hour.","알림을 1시간 동안 중지했습니다."},
            {"Reset panel position","창 위치 초기화"},{"Reset panel position to primary monitor","주 모니터로 창 위치 초기화"},{"Panel position reset.","창 위치를 초기화했습니다."},
            {"Demo preferences applied for this session.","이번 데모에 설정을 적용했습니다."},{"Preferences saved.","설정을 저장했습니다."},
            {"PROJECT FOLDER","프로젝트 폴더"},{"AGENT","에이전트"},{"SESSION ID","세션 ID"},{"VERCEL PREVIEW URL","Vercel 미리보기 URL"},
            {"Copy {0}","{0} 복사"},{"Copy local server URL","로컬 서버 URL 복사"},{"Demo: URL copy simulated.","데모: URL 복사를 모의 실행했습니다."},{"Local URL copied.","로컬 URL을 복사했습니다."},
            {"Demo: project links simulated.","데모: 프로젝트 연결을 모의 저장했습니다."},{"Project links saved.","프로젝트 연결을 저장했습니다."},
            {"The folder must match this server's detected project folder.","감지된 서버의 프로젝트 폴더와 일치해야 합니다."},
            {"Open What the Port","What the Port 열기"},{"Clean up…","서버 정리…"},{"Settings…","설정…"},{"Quit","앱 종료"},{"monitoring","관찰 중"},
            {"{0} idle server(s) are ready to clean up. Open Clean up to review.","유휴 서버 {0}개가 있습니다. 서버 정리에서 확인해 주세요."},
            {"This server is protected.","보호된 서버는 종료할 수 없습니다."},{"Server data is stale. Refresh before stopping.","서버 정보가 오래되었습니다. 새로 조회 후 종료해 주세요."},{"Server identity unavailable. Refresh before stopping.","서버 식별 정보를 확인할 수 없습니다. 새로 조회해 주세요."},
            {"Protected process.","보호된 프로세스입니다."},{"Process exited or access denied.","이미 종료되었거나 접근 권한이 없습니다."},{"Process identity changed; stop cancelled.","프로세스 식별 정보가 달라져 종료를 취소했습니다."},{"Process belongs to another user or session.","다른 사용자 또는 세션의 프로세스입니다."},
            {"Check the sampling, alert and idle thresholds.","조회 간격과 경고·유휴 기준의 입력 범위를 확인해 주세요."},{"Invalid cleanup mode.","자동 정리 방식이 올바르지 않습니다."},{"Invalid terminal.","터미널 선택이 올바르지 않습니다."},{"Invalid language.","지원하지 않는 언어입니다."},{"Invalid saved panel position.","저장된 창 위치가 올바르지 않습니다."},
            {"Port must be between 1 and 65535.","포트 범위는 1~65535입니다."},{"Choose an existing absolute project folder.","존재하는 프로젝트 폴더의 절대 경로를 입력해 주세요."},{"Choose Codex or Claude Code.","Codex 또는 Claude Code를 선택해 주세요."},
            {"Session ID must contain only letters, digits, dashes and underscores.","세션 ID는 영문·숫자·하이픈·밑줄만 사용할 수 있습니다."},{"Use an HTTPS preview URL on vercel.app (no credentials or custom ports).","vercel.app의 HTTPS URL을 입력해 주세요. 인증 정보·별도 포트는 지원하지 않습니다."},
            {"Enter valid numbers for the sampling and alert thresholds.","조회 간격과 경고 기준에 올바른 숫자를 입력해 주세요."},
            {"Settings could not be read. Safe defaults are active; save Settings to repair.","설정을 읽을 수 없어 안전한 기본값을 적용했습니다. 설정을 저장하면 복구됩니다."},
            {"Project links could not be read. Register the links again.","프로젝트 연결을 읽을 수 없습니다. 연결을 다시 등록해 주세요."},
            {"Register the exact session ID first.","정확한 세션 ID를 먼저 등록해 주세요."},{"The project folder is unavailable.","프로젝트 폴더에 접근할 수 없습니다."},
            {"Windows Terminal was not found. Choose PowerShell in Settings.","Windows Terminal을 찾을 수 없습니다. 설정에서 PowerShell을 선택해 주세요."},
            {"Register a Vercel preview URL first.","Vercel 미리보기 URL을 먼저 등록해 주세요."},
            {"Left and Right: previous or next sample. Home and End: first or latest sample. Tab: next control.","좌우 방향키: 이전·다음 값. Home·End: 처음·최근 값. Tab: 다음 항목."},
            {"{0} chart. Collecting samples.","{0} 차트. 측정값 수집 중."},{"{0} chart, last 10 minutes. {1} samples. Range {2}. Sample {3} of {1}: {4}.","{0} 차트, 최근 10분. 측정값 {1}개. 범위 {2}. {1}개 중 {3}번째: {4}."},
            {" to "," ~ "},{"−10 min","−10분"},{"now","현재"},{"Collecting samples…","측정값 수집 중…"}
        };
    }
}
