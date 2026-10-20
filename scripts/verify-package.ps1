param(
    [string]$PackagePath,
    [string]$ReportPath,
    [string]$ExpectedHash,
    [switch]$RequireSignature
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $PackagePath) { $PackagePath = Join-Path $projectRoot 'docs\downloads\WhatThePort-Windows-x64.zip' }
if (-not $ReportPath) { $ReportPath = Join-Path $projectRoot 'artifacts\package-verification.json' }
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$archiveHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedHash -and $archiveHash -ne $ExpectedHash.ToLowerInvariant()) { throw 'Package SHA-256 mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inspectionRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts\package-inspection-' + [Guid]::NewGuid().ToString('N'))))
$artifactPrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $inspectionRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected inspection path.' }
New-Item -ItemType Directory -Path $inspectionRoot -Force | Out-Null
$runtimeNames = @('WhatThePort.exe', 'WhatThePort.Core.dll', 'wtp.exe')
$runtime = @()
$entryNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$entries = @()
$totalBytes = 0L
$zip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.EndsWith('/')) { continue }
        if ([IO.Path]::IsPathRooted($name) -or $name.Contains(':') -or $name.Split('/') -contains '..' -or $name.Split('/') -contains '.' -or $name.Split('/') -contains '') { throw ('Unsafe ZIP path: ' + $name) }
        if (-not $entryNames.Add($name)) { throw ('Duplicate ZIP entry: ' + $name) }
        $totalBytes += $entry.Length
        if ($totalBytes -gt 100MB -or $entry.Length -gt 25MB) { throw 'Unexpectedly large package.' }
        if ($name -match '\.(exe|dll|ps1|bat|cmd|vbs|js|msi|scr|com)$' -and $runtimeNames -notcontains $name) { throw ('Unexpected executable in package: ' + $name) }
        $stream = $entry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        $entries += [ordered]@{name=$name; bytes=$entry.Length; sha256=$entryHash}
        if ($runtimeNames -contains $name) {
            $target = Join-Path $inspectionRoot $name
            $inputStream = $entry.Open()
            $outputStream = [IO.File]::Create($target)
            try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
            $signature = Get-AuthenticodeSignature -LiteralPath $target
            $status = $signature.Status.ToString()
            if ($status -ne 'Valid' -and $status -ne 'NotSigned') { throw ('Invalid binary signature: ' + $name + ' (' + $status + ')') }
            $publisher = $null
            if ($signature.SignerCertificate) { $publisher = $signature.SignerCertificate.Subject }
            $runtime += [ordered]@{name=$name; version=[Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion; sha256=$entryHash; signature=$status; publisher=$publisher; timestamped=[bool]$signature.TimeStamperCertificate}
        }
    }
    foreach ($required in @($runtimeNames + @('WhatThePort.exe.config','README.md','LICENSE','fonts/Geist-Regular.ttf','fonts/Geist-Medium.ttf','fonts/OFL.txt'))) {
        if (-not $entryNames.Contains($required)) { throw ('Missing package file: ' + $required) }
    }
    $allSigned = @($runtime | Where-Object { $_.signature -ne 'Valid' -or -not $_.timestamped }).Count -eq 0
    $report = [ordered]@{
        checkedAtUtc=[DateTime]::UtcNow.ToString('o')
        archive=[ordered]@{name=[IO.Path]::GetFileName($PackagePath); bytes=(Get-Item -LiteralPath $PackagePath).Length; sha256=$archiveHash; entryCount=$entries.Count}
        trustedTimestampedSignatures=$allSigned
        malwareScan='Not performed by this verification; this is an integrity and signature check.'
        browserReputation='Not verified; Chrome Safe Browsing makes an independent decision.'
        binaries=$runtime
        files=$entries
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))) -Force | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath), ($report | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    Write-Output ('Package integrity checked: ' + $archiveHash)
    foreach ($binary in $runtime) { Write-Output ($binary.name + ': ' + $binary.signature) }
    if ($RequireSignature -and -not $allSigned) { throw 'Public release requires trusted, timestamped Authenticode signatures on both EXEs and the Core DLL. See docs/download-security.md.' }
} finally {
    $zip.Dispose()
    if ($inspectionRoot.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $inspectionRoot)) { Remove-Item -LiteralPath $inspectionRoot -Recurse -Force }
}
