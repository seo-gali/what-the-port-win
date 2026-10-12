param([switch]$Rebuild, [switch]$RequireSignature)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'dist'
$artifactRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$cachePath = Join-Path $artifactRoot 'package-cache.json'
$archive = Join-Path $artifactRoot 'WhatThePort-Windows-x64.zip'
$runtimeNames = @('WhatThePort.exe','WhatThePort.exe.config','WhatThePort.Core.dll','wtp.exe')
function Fingerprint($files) {
    $lines = @($files | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($projectRoot.Length).Replace('\','/') + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash })
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "\n")))).Replace('-','').ToLowerInvariant() } finally { $algorithm.Dispose() }
}
$appInputs = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File) + @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'assets\fonts') -Recurse -File) + @(Get-Item -LiteralPath (Join-Path $PSScriptRoot 'build.ps1'),(Join-Path $PSScriptRoot 'compile-markup.proj'))
$appFingerprint = Fingerprint $appInputs
$cache = $null
try { if (Test-Path -LiteralPath $cachePath) { $cache = [IO.File]::ReadAllText($cachePath) | ConvertFrom-Json } } catch { $cache = $null }
$reuseBuild = -not $Rebuild -and $cache -and $cache.appFingerprint -eq $appFingerprint -and @($cache.runtime).Count -eq $runtimeNames.Count
if ($reuseBuild) {
    foreach ($name in $runtimeNames) {
        $file = Join-Path $outputRoot $name
        $saved = $cache.runtime | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if (-not $saved -or -not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $saved.sha256) { $reuseBuild = $false; break }
    }
}
if ($reuseBuild) { Write-Output 'App inputs unchanged; reusing existing binaries.' }
else { & (Join-Path $PSScriptRoot 'build.ps1') }
# Refresh documentation without recompiling or replacing signed app binaries.
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\portable-README.md') -Destination (Join-Path $outputRoot 'README.md') -Force
$packageDocs = [IO.Path]::GetFullPath((Join-Path $outputRoot 'docs'))
if ($packageDocs -ne [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\docs'))) { throw 'Unexpected package documentation path.' }
if (Test-Path -LiteralPath $packageDocs) { Remove-Item -LiteralPath $packageDocs -Recurse -Force }
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs\*.md') -Destination $packageDocs -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\images') -Destination $packageDocs -Recurse -Force
# Always refresh fonts from the trusted source, including on a cached build.
New-Item -ItemType Directory -Path (Join-Path $outputRoot 'fonts') -Force | Out-Null
foreach ($font in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'assets\fonts') -File) {
    Copy-Item -LiteralPath $font.FullName -Destination (Join-Path $outputRoot 'fonts') -Force
}
$files = @('WhatThePort.exe','WhatThePort.exe.config','WhatThePort.Core.dll','wtp.exe','LICENSE','README.md','fonts','docs') | ForEach-Object { Join-Path $outputRoot $_ }
$packageInputs = @()
foreach ($file in $files) {
    if ((Get-Item -LiteralPath $file).PSIsContainer) { $packageInputs += @(Get-ChildItem -LiteralPath $file -Recurse -File) }
    else { $packageInputs += Get-Item -LiteralPath $file }
}
$packageFingerprint = Fingerprint $packageInputs
$reuseArchive = -not $Rebuild -and $cache -and $cache.packageFingerprint -eq $packageFingerprint -and (Test-Path -LiteralPath $archive)
if ($reuseArchive) { $reuseArchive = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $cache.archiveHash }
if ($reuseArchive) { Write-Output 'Package inputs unchanged; preserving the exact ZIP bytes.' }
else { Compress-Archive -LiteralPath $files -DestinationPath $archive -Force }
$runtime = @($runtimeNames | ForEach-Object { [ordered]@{name=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $outputRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant()} })
$state = [ordered]@{appFingerprint=$appFingerprint; packageFingerprint=$packageFingerprint; archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); runtime=$runtime}
[IO.File]::WriteAllText($cachePath, ($state | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $archive -RequireSignature:$RequireSignature
Write-Output ('Portable package: ' + $archive)
