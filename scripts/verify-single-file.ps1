param(
    [string]$ExecutablePath,
    [string]$ReportPath,
    [string]$ExpectedHash,
    [switch]$RequireSignature
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $projectRoot 'artifacts\single-file\WhatThePort.exe' }
if (-not $ReportPath) { $ReportPath = Join-Path $projectRoot 'artifacts\single-file-verification.json' }
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) { throw 'Build the standalone executable first.' }
$hash = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedHash -and $hash -ne $ExpectedHash.ToLowerInvariant()) { throw 'Standalone executable does not match the expected SHA-256.' }
# Metadata-only inspection does not run executable code or resolve user dependencies.
$assembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($ExecutablePath))
if ($assembly.GetName().Name -ne 'WhatThePort' -or $assembly.GetName().ProcessorArchitecture -ne 'Amd64') { throw 'Expected the x64 WhatThePort assembly.' }
if (@($assembly.GetReferencedAssemblies() | Where-Object { $_.Name -eq 'WhatThePort.Core' }).Count -gt 0) { throw 'Standalone executable still depends on the Core DLL.' }
foreach ($name in @('WhatThePort.g.resources','WhatThePort.LICENSE.txt','WhatThePort.OFL.txt')) {
    if ($assembly.GetManifestResourceNames() -notcontains $name) { throw ('Missing standalone resource: ' + $name) }
}
$signature = Get-AuthenticodeSignature -LiteralPath $ExecutablePath
$status = $signature.Status.ToString()
if ($status -ne 'Valid' -and $status -ne 'NotSigned') { throw ('Invalid executable signature: ' + $status) }
$publisher = $null
if ($signature.SignerCertificate) { $publisher = $signature.SignerCertificate.Subject }
$trusted = $status -eq 'Valid' -and [bool]$signature.TimeStamperCertificate
$report = [ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    executable = [ordered]@{ name=[IO.Path]::GetFileName($ExecutablePath); version=[Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).FileVersion; bytes=(Get-Item -LiteralPath $ExecutablePath).Length; sha256=$hash; signature=$status; publisher=$publisher; timestamped=[bool]$signature.TimeStamperCertificate }
    trustedTimestampedSignature = $trusted
    runtime = 'Windows 10/11 x64; .NET Framework 4.8'
    distribution = 'Standalone GUI executable with embedded Core, WPF resources, fonts and licenses; CLI remains in the optional ZIP.'
    malwareScan = 'Not performed by this verification; this is an integrity, metadata and signature check.'
    browserReputation = 'Not verified; Chrome Safe Browsing makes an independent decision.'
}
New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))) -Force | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),($report | ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding($false)))
Write-Output ('Standalone integrity checked: ' + $hash + '; signature: ' + $status)
if ($RequireSignature -and -not $trusted) { throw 'Public release requires a trusted, timestamped Authenticode signature. See docs/download-security.md.' }
