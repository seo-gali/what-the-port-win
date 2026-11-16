param([switch]$StartWithWindows)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'dist'
if (-not (Test-Path -LiteralPath (Join-Path $source 'WhatThePort.exe'))) { & (Join-Path $PSScriptRoot 'build.ps1') }
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\WhatThePort'
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
foreach ($name in @('WhatThePort.exe', 'WhatThePort.exe.config', 'WhatThePort.Core.dll', 'wtp.exe', 'LICENSE', 'README.md', 'fonts', 'docs')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $installRoot -Recurse -Force
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'What the Port.lnk'))
$shortcut.TargetPath = Join-Path $installRoot 'WhatThePort.exe'
$shortcut.WorkingDirectory = $installRoot
$shortcut.Save()
if ($StartWithWindows) {
    Add-Type -Path (Join-Path $installRoot 'WhatThePort.Core.dll')
    [WhatThePort.StartupRegistration]::Set($true,(Join-Path $installRoot 'WhatThePort.exe'),[Environment]::GetFolderPath('Startup'))
}
Write-Output "Installed: $installRoot"
Write-Output 'Open What the Port from the Start menu. The installer does not modify PATH.'
