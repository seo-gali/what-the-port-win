param([switch]$SingleFile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$auditRoot = Join-Path $projectRoot ('artifacts\security-audit-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $auditRoot | Out-Null
$runtime = if ($SingleFile) { Join-Path $projectRoot 'docs\downloads\WhatThePort.exe' } else { Join-Path $projectRoot 'dist\WhatThePort.Core.dll' }
if ($SingleFile) {
    $information = [IO.File]::ReadAllText((Join-Path $projectRoot 'docs\downloads\EXE-INFO.json')) | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $information.executable.sha256) { throw 'Published executable does not match its integrity report.' }
    Write-Output ('Audit target SHA-256: ' + $hash)
}
Copy-Item -LiteralPath $runtime -Destination $auditRoot
$reference = Join-Path $auditRoot ([IO.Path]::GetFileName($runtime))
& $compiler /nologo /target:exe /platform:x64 /platform:x64 "/out:$auditRoot\wt-fixture.exe" (Join-Path $projectRoot 'tests\TerminalAuditFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 "/out:$auditRoot\audit.exe" "/r:$reference" (Join-Path $projectRoot 'tests\SecurityAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Audit compilation failed.' }
& (Join-Path $auditRoot 'audit.exe') $auditRoot | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\security-audit.txt')
if ($LASTEXITCODE -ne 0) { throw 'One or more observations did not reproduce; inspect the evidence.' }
