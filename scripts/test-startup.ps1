param([string]$RuntimeDirectory, [switch]$SingleFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $projectRoot 'dist' }
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$testId = [Guid]::NewGuid().ToString('N')
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('startup-' + $testId)))
if (-not $fixtureRoot.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture path.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$ownedProcesses = New-Object 'Collections.Generic.List[Diagnostics.Process]'
function Visible-Window([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) { if ($window.Current.Name -eq 'What the Port' -and -not $window.Current.IsOffscreen) { return $window } }
    return $null
}
function Start-Fixture([string]$folder, [switch]$Background, [string]$InstanceId = $testId) {
    $arguments = @('--demo', '--language', 'en', '--test-instance', $InstanceId)
    if ($Background) { $arguments += '--background' }
    $process = Start-Process -FilePath (Join-Path $fixtureRoot ($folder + '\WhatThePort.exe')) -ArgumentList $arguments -WorkingDirectory $fixtureRoot -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($process)
    return $process
}
function Check-Secondary([Diagnostics.Process]$process) {
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0) { throw 'Secondary instance did not hand off successfully.' }
}
try {
    foreach ($folder in @('first', 'downloaded-copy')) {
        $target = Join-Path $fixtureRoot $folder
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $runtimeFiles = if ($SingleFile) { @('WhatThePort.exe') } else { @('WhatThePort.exe','WhatThePort.exe.config','WhatThePort.Core.dll','fonts') }
        foreach ($file in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $file) -Destination $target -Recurse -Force }
        if ($SingleFile -and @(Get-ChildItem -LiteralPath $target).Count -ne 1) { throw 'Standalone startup must use only the downloaded EXE.' }
    }
    $direct = Start-Fixture 'downloaded-copy' -InstanceId ([Guid]::NewGuid().ToString('N'))
    if (-not $direct.WaitForInputIdle(5000)) { throw 'Direct GUI executable did not initialize.' }
    $window = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $window; $attempt++) { Start-Sleep -Milliseconds 100; $window = Visible-Window $direct.Id }
    if ($null -eq $window) { throw 'Direct GUI executable did not show its panel.' }
    Write-Output 'PASS startup: WhatThePort.exe opens directly without the CLI'
    $direct.Kill()
    $direct.WaitForExit(5000) | Out-Null

    $primary = Start-Fixture 'first' -Background
    if (-not $primary.WaitForInputIdle(5000)) { throw 'Primary instance did not initialize.' }
    Start-Sleep -Milliseconds 200
    if ($null -ne (Visible-Window $primary.Id)) { throw 'Background startup opened a panel.' }
    Check-Secondary (Start-Fixture 'downloaded-copy' -Background)
    if ($null -ne (Visible-Window $primary.Id)) { throw 'Background relaunch unexpectedly opened a panel.' }
    Write-Output 'PASS startup: repeated background launch stays in tray'

    Check-Secondary (Start-Fixture 'downloaded-copy')
    $window = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $window; $attempt++) { Start-Sleep -Milliseconds 100; $window = Visible-Window $primary.Id }
    if ($null -eq $window -or $primary.HasExited) { throw 'Explicit relaunch did not reveal the existing instance.' }
    Write-Output 'PASS startup: a second executable copy opens the original instance'

    $hideCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Hide to tray')
    $hide = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $hideCondition)
    $hide.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    if ($null -ne (Visible-Window $primary.Id)) { throw 'Explicit hide did not hide the panel.' }
    Check-Secondary (Start-Fixture 'downloaded-copy')
    $window = $null
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $window; $attempt++) { Start-Sleep -Milliseconds 100; $window = Visible-Window $primary.Id }
    if ($null -eq $window) { throw 'Hidden primary instance did not reopen.' }
    Write-Output 'PASS startup: relaunch reopens an explicitly hidden panel'

    foreach ($mode in @('visible','background')) {
        $stdout = Join-Path $fixtureRoot ('real-' + $mode + '.out')
        $stderr = Join-Path $fixtureRoot ('real-' + $mode + '.err')
        $preferences = Join-Path $fixtureRoot ('preferences-' + $mode)
        $probe = Start-Process -FilePath (Join-Path $RuntimeDirectory 'wtp-startup-tests.exe') -ArgumentList $mode, ('"' + $preferences + '"') -WorkingDirectory $fixtureRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $ownedProcesses.Add($probe)
        # Retain the process handle so Windows PowerShell can read its exit code
        # after a redirected child exits; then drain redirected output completely.
        $probeHandle = $probe.Handle
        if (-not $probe.WaitForExit(20000)) { throw ('Real startup timed out: ' + $mode) }
        $probe.WaitForExit()
        Get-Content -LiteralPath $stdout
        if ($probe.ExitCode -ne 0) { Get-Content -LiteralPath $stderr; throw ('Real startup failed: ' + $mode) }
    }
} finally {
    foreach ($process in $ownedProcesses) { try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } finally { $process.Dispose() } }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
