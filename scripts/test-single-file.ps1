$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build-single-file.ps1') -Tests
$runtime = Join-Path $projectRoot 'artifacts\single-file'
$executable = Join-Path $runtime 'WhatThePort.exe'
$firstHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
& (Join-Path $PSScriptRoot 'build-single-file.ps1')
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $firstHash) { throw 'Repeated standalone builds changed an unchanged executable.' }
Write-Output 'PASS standalone: repeated build preserves executable hash'
foreach ($test in @('wtp-tests.exe','wtp-ui-tests.exe','wtp-single-file-tests.exe')) {
    & (Join-Path $runtime $test) $projectRoot
    if ($LASTEXITCODE -ne 0) { throw ('Standalone regression failed: ' + $test) }
}
& (Join-Path $PSScriptRoot 'test-startup.ps1') -RuntimeDirectory $runtime -SingleFile
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$fixture = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('single-file-test-' + [Guid]::NewGuid().ToString('N'))))
if (-not $fixture.StartsWith($artifactRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected standalone fixture path.' }
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath $executable -Destination $fixture
$process = $null
try {
    $report = Join-Path $fixture 'live-smoke.txt'
    $process = Start-Process -FilePath (Join-Path $fixture 'WhatThePort.exe') -ArgumentList '--smoke-test', ('"' + $report + '"') -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
    $processHandle = $process.Handle
    if (-not $process.WaitForExit(20000)) { throw 'Standalone live smoke timed out.' }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report) -or [IO.File]::ReadAllText($report) -notmatch 'result=PASS') { throw 'Standalone live smoke failed.' }
    if (@(Get-ChildItem -LiteralPath $fixture | Where-Object { $_.Name -notin @('WhatThePort.exe','live-smoke.txt') }).Count -ne 0) { throw 'Standalone executable extracted unexpected runtime files.' }
    Write-Output 'PASS standalone: live tray, native scan and window work with only one EXE, without runtime extraction'
    $process.Dispose(); $process = $null
    $process = Start-Process -FilePath (Join-Path $fixture 'WhatThePort.exe') -ArgumentList '--licenses' -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
    if (-not $process.WaitForInputIdle(5000)) { throw 'Embedded license viewer did not initialize.' }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $window = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $window; $attempt++) {
        Start-Sleep -Milliseconds 100
        $candidates = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
        foreach ($candidate in $candidates) { if ($candidate.Current.Name -eq 'What the Port - Licenses') { $window = $candidate; break } }
    }
    if (-not $window) { throw 'Embedded licenses were not displayed.' }
    $editCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)
    $edit = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$editCondition)
    $text = $edit.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1)
    if (-not $text.Contains('MIT License') -or -not $text.Contains('SIL OPEN FONT LICENSE')) { throw 'Embedded license viewer omitted license text.' }
    Write-Output 'PASS standalone: MIT and font licenses are readable from the downloaded EXE'
} finally {
    if ($process) { try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } finally { $process.Dispose() } }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
Write-Output 'Standalone distribution checks passed.'