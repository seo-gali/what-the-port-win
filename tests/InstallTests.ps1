$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -TypeDefinition @"
namespace WhatThePort {
    public static class StartupRegistration {
        public static int Calls;
        public static bool Enabled;
        public static string Executable,Folder;
        public static void Set(bool enabled,string executable,string folder) {Calls++;Enabled=enabled;Executable=executable;Folder=folder;}
    }
}
"@
$script:loaded = $null
function Add-Type { param([string]$Path) $script:loaded = $Path }
$script:legacy = 0
$shell = New-Object PSObject
$shell | Add-Member ScriptMethod CreateShortcut {
    param($path)
    $script:legacy++
    $item = [pscustomobject]@{TargetPath='';Arguments='';WorkingDirectory=''}
    $item | Add-Member ScriptMethod Save {}
    return $item
}
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'scripts\install.ps1'),[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Installer syntax error.'}
$branch=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text.Trim() -eq '$StartWithWindows'},$true))
if($branch.Count -ne 1){throw 'Installer startup branch missing.'}
$installRoot=Join-Path $root 'artifacts\installer-test-placeholder'
$body=$branch[0].Clauses[0].Item2.Extent.Text
& ([ScriptBlock]::Create($body.Substring(1,$body.Length-2)))
if([WhatThePort.StartupRegistration]::Calls -ne 1 -or -not [WhatThePort.StartupRegistration]::Enabled){throw 'Installer must use the shared Run registration.'}
if([WhatThePort.StartupRegistration]::Executable -ne (Join-Path $installRoot 'WhatThePort.exe')){throw 'Installer startup target is wrong.'}
if([WhatThePort.StartupRegistration]::Folder -ne [Environment]::GetFolderPath('Startup')){throw 'Installer legacy migration folder is wrong.'}
if($script:legacy -ne 0){throw 'Installer must not create a Startup shortcut.'}
if($script:loaded -ne (Join-Path $installRoot 'WhatThePort.Core.dll')){throw 'Installer must use its installed Core.'}
Write-Output 'PASS installer startup delegates to shared registration without touching the real profile'