$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Tests
& (Join-Path $projectRoot 'dist\wtp-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Core or integration tests failed.' }
$json = & (Join-Path $projectRoot 'dist\wtp.exe') list --json
if ($LASTEXITCODE -ne 0) { throw 'CLI snapshot failed.' }
$snapshot = $json | ConvertFrom-Json
if ($snapshot.TotalMemory -le 0) { throw 'CLI JSON has no system memory.' }
Write-Output 'PASS CLI JSON snapshot'
try {
    $ErrorActionPreference = 'Continue'
    & (Join-Path $projectRoot 'dist\wtp.exe') unknown-command 2>$null
    $invalidExit = $LASTEXITCODE
} finally { $ErrorActionPreference = 'Stop' }
if ($invalidExit -ne 1) { throw 'CLI must reject unknown commands.' }
Write-Output 'PASS CLI error exit code'
& (Join-Path $projectRoot 'dist\wtp-ui-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'WPF interaction tests failed.' }
$report = Join-Path $projectRoot 'artifacts\live-smoke.txt'
$smoke = Start-Process -FilePath (Join-Path $projectRoot 'dist\WhatThePort.exe') -ArgumentList '--smoke-test', ('"' + $report + '"') -WindowStyle Hidden -PassThru
if (-not $smoke.WaitForExit(15000)) { throw 'Live app smoke test timed out.' }
if ($smoke.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'Live app smoke test failed.' }
if ((Get-Content -LiteralPath $report -Raw) -notmatch 'result=PASS') { throw 'Live app report failed.' }
Write-Output 'PASS live app: tray, window handle and native scanning'
& (Join-Path $PSScriptRoot 'test-startup.ps1')

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'tests\InstallTests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Installer startup regression failed.' }
& (Join-Path $PSScriptRoot 'security-audit.ps1')