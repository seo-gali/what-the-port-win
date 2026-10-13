param(
    [ValidateRange(15,300)][int]$SecondsPerState = 30,
    [ValidateRange(0,20)][int]$FixtureServers = 0,
    [ValidatePattern('^[a-zA-Z0-9_-]+\.json$')][string]$ReportName = 'resource-usage.json',
    [string]$RuntimeDirectory,
    [switch]$SingleFile
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    if ($SingleFile) {
        & (Join-Path $PSScriptRoot 'build-single-file.ps1')
        $RuntimeDirectory = Join-Path $projectRoot 'artifacts\single-file'
    } else {
        & (Join-Path $PSScriptRoot 'build.ps1')
        $RuntimeDirectory = Join-Path $projectRoot 'dist'
    }
}
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('resources-' + [Guid]::NewGuid().ToString('N'))))
if (-not $fixtureRoot.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected resource fixture path.' }
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$reportPath = Join-Path $artifactRoot $ReportName
$process = $null
$serverProcesses = New-Object 'Collections.Generic.List[Diagnostics.Process]'
try {
    $runtimeFiles = if ($SingleFile) { @('WhatThePort.exe') } else { @('WhatThePort.exe','WhatThePort.Core.dll','fonts') }
    foreach ($file in $runtimeFiles) {
        Copy-Item -LiteralPath (Join-Path $RuntimeDirectory $file) -Destination $fixtureRoot -Recurse -Force
    }
    $probePath = Join-Path $fixtureRoot 'wtp-resource-probe.exe'
    $coreReference = if ($SingleFile) { @() } else { @("/r:$fixtureRoot\WhatThePort.Core.dll") }
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/out:$probePath" $coreReference "/r:$fixtureRoot\WhatThePort.exe" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\WindowsBase.dll" /r:System.Xaml.dll /r:System.Runtime.Serialization.dll (Join-Path $projectRoot 'tests\ResourceProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Resource probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination ($probePath + '.config') -Force
    if ($FixtureServers -gt 0) {
        # The scanner recognizes this runtime name. Each fixture owns a loopback
        # listener and exits on its own if the parent runner is interrupted.
        $serverPath = Join-Path $fixtureRoot 'wtp-tests.exe'
        Copy-Item -LiteralPath $probePath -Destination $serverPath
        Copy-Item -LiteralPath ($probePath + '.config') -Destination ($serverPath + '.config')
        for ($serverIndex = 0; $serverIndex -lt $FixtureServers; $serverIndex++) {
            $serverDirectory = Join-Path $fixtureRoot ('server-' + $serverIndex)
            New-Item -ItemType Directory -Path $serverDirectory | Out-Null
            $serverProcess = Start-Process -FilePath $serverPath -ArgumentList '--fixture-server', (3 * $SecondsPerState + 90) -WorkingDirectory $serverDirectory -WindowStyle Hidden -PassThru
            $serverProcesses.Add($serverProcess)
        }
    }
    $arguments = @(('"' + $reportPath + '"'), ('"' + (Join-Path $fixtureRoot 'settings') + '"'), $SecondsPerState, $FixtureServers)
    if ($FixtureServers -gt 0) { $arguments += (($serverProcesses | ForEach-Object Id) -join ',') }
    $process = Start-Process -FilePath $probePath -ArgumentList $arguments -WorkingDirectory $fixtureRoot -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(3 * $SecondsPerState + 80)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Resource probe timed out.' }
    }
    if ($process.ExitCode -ne 0) { throw 'Resource probe failed.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Write-Output ("Logical processors: {0}; sampling: {1}s; duration per state: {2}s" -f $report.LogicalProcessors, $report.ScanSeconds, $SecondsPerState)
    $report.Phases | Select-Object State,Scans,MinimumServers,Servers,@{Name='CPU (%)';Expression={[Math]::Round($_.CpuPercent,3)}},@{Name='Average RAM (MiB)';Expression={[Math]::Round($_.AverageWorkingSetMB,1)}},@{Name='Peak RAM (MiB)';Expression={[Math]::Round($_.PeakWorkingSetMB,1)}},@{Name='Peak private (MiB)';Expression={[Math]::Round($_.PeakPrivateMB,1)}} | Format-Table -AutoSize
    Write-Output "Resource report: $reportPath"
} finally {
    if ($null -ne $process) { try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } finally { $process.Dispose() } }
    foreach ($serverProcess in $serverProcesses) { try { if (-not $serverProcess.HasExited) { $serverProcess.Kill(); $serverProcess.WaitForExit(5000) | Out-Null } } finally { $serverProcess.Dispose() } }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
