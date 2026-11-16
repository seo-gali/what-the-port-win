param([switch]$SingleFile,[string]$RuntimePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$auditRoot = Join-Path $projectRoot ('artifacts\security-audit-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $auditRoot | Out-Null
$runtime = if ($RuntimePath) { [IO.Path]::GetFullPath($RuntimePath) } elseif ($SingleFile) { Join-Path $projectRoot 'docs\downloads\WhatThePort.exe' } else { Join-Path $projectRoot 'dist\WhatThePort.Core.dll' }
if ($SingleFile -and -not $RuntimePath) {
    $information = [IO.File]::ReadAllText((Join-Path $projectRoot 'docs\downloads\EXE-INFO.json')) | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $information.executable.sha256) { throw 'Published executable does not match its integrity report.' }
}
Write-Output ('Security regression target: ' + $runtime)
Write-Output ('Target SHA-256: ' + (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant())
Copy-Item -LiteralPath $runtime -Destination $auditRoot
$reference = Join-Path $auditRoot ([IO.Path]::GetFileName($runtime))
# The junction and its target contain only this test's public marker.
$target = Join-Path $auditRoot 'reparse-target'
New-Item -ItemType Directory -Path (Join-Path $target '.git') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $target '.git\HEAD'),'ref: refs/heads/PUBLIC_MARKER_ONLY')
New-Item -ItemType Junction -Path (Join-Path $auditRoot 'reparse-project') -Target $target | Out-Null
& $compiler /nologo /target:exe /platform:x64 "/out:$auditRoot\audit.exe" "/r:$reference" (Join-Path $projectRoot 'tests\SecurityAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Security regression compilation failed.' }
& (Join-Path $auditRoot 'audit.exe') $auditRoot | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\security-audit.txt')
if ($LASTEXITCODE -ne 0) { throw 'Security regression failed; inspect the PASS/FAIL report.' }