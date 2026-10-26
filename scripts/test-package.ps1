# Exercise packaging in a fresh copy; never replace a running user's app.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('package-tests-' + [Guid]::NewGuid().ToString('N'))))
if (-not $testRoot.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test path.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
foreach ($folder in @('src','scripts','assets','docs','tests')) { Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $testRoot -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $testRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $testRoot
$passed = 0
function Check($name, $condition) {
    if (-not $condition) { throw ('FAIL ' + $name) }
    $script:passed++
    Write-Output ('PASS ' + $name)
}
function Reject($name, [scriptblock]$action) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    Check $name $rejected
}
function Hash($file) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
function Package { & (Join-Path $testRoot 'scripts\package.ps1') }
$zipPath = Join-Path $testRoot 'artifacts\WhatThePort-Windows-x64.zip'
$binaryPaths = @('WhatThePort.exe','WhatThePort.Core.dll','wtp.exe') | ForEach-Object { Join-Path $testRoot ('dist\' + $_) }
$completed = $false
try {
    Package
    $firstZip = Hash $zipPath
    $firstBinaries = @($binaryPaths | ForEach-Object { Hash $_ })
    Package
    Check 'repeat package preserves ZIP bytes' ((Hash $zipPath) -eq $firstZip)
    Check 'repeat package preserves app binaries' ((@($binaryPaths | ForEach-Object { Hash $_ }) -join ',') -eq ($firstBinaries -join ','))
    [IO.File]::AppendAllText((Join-Path $testRoot 'assets\portable-README.md'), "\r\nPackage documentation fixture.\r\n")
    Package
    Check 'documentation update changes ZIP' ((Hash $zipPath) -ne $firstZip)
    Check 'documentation update preserves app binaries' ((@($binaryPaths | ForEach-Object { Hash $_ }) -join ',') -eq ($firstBinaries -join ','))
    $beforeTamper = Hash $zipPath
    $stream = [IO.File]::Open($zipPath, [IO.FileMode]::Append)
    try { $stream.WriteByte(1) } finally { $stream.Dispose() }
    $tampered = Hash $zipPath
    Package
    Check 'changed cached ZIP is regenerated' ((Hash $zipPath) -ne $tampered)
    $marker = Join-Path $testRoot 'src\Core\PackageMarker.cs'
    [IO.File]::WriteAllText($marker, 'namespace WhatThePort { internal static class PackageMarker { public const string Value = "fixture"; } }')
    Package
    Check 'source update rebuilds Core DLL' ((Hash $binaryPaths[1]) -ne $firstBinaries[1])
    $verify = Join-Path $testRoot 'scripts\verify-package.ps1'
    Reject 'wrong expected checksum is rejected' { & $verify -PackagePath $zipPath -ExpectedHash ('0' * 64) }
    Reject 'unsigned package fails signature requirement' { & $verify -PackagePath $zipPath -RequireSignature }
    $publicZip = Join-Path $testRoot 'docs\downloads\WhatThePort-Windows-x64.zip'
    $beforePublic = Hash $publicZip
    Reject 'default site preparation rejects unsigned package' { & (Join-Path $testRoot 'scripts\prepare-site.ps1') }
    Check 'rejected preparation preserves published ZIP' ((Hash $publicZip) -eq $beforePublic)
    foreach ($pageName in @('index.html','guide.html')) {
        $page = Join-Path $testRoot ('docs\' + $pageName)
        $stale = [regex]::Replace([IO.File]::ReadAllText($page), '\bdownload="[^"]*"', 'download="stale-download.bin"')
        [IO.File]::WriteAllText($page, $stale, (New-Object Text.UTF8Encoding($false)))
    }
    & (Join-Path $testRoot 'scripts\prepare-site.ps1') -AllowUnsigned
    Check 'explicit test distribution publishes verified ZIP' ((Hash $publicZip) -eq (Hash $zipPath))
    Check 'test distribution publishes integrity report' (Test-Path -LiteralPath (Join-Path $testRoot 'docs\downloads\PACKAGE-INFO.json'))
    $singleVerify = Join-Path $testRoot 'scripts\verify-single-file.ps1'
    $singleExecutable = Join-Path $testRoot 'artifacts\single-file\WhatThePort.exe'
    $publicExecutable = Join-Path $testRoot 'docs\downloads\WhatThePort.exe'
    Check 'test distribution publishes standalone EXE' ((Hash $singleExecutable) -eq (Hash $publicExecutable))
    $singleInfo = [IO.File]::ReadAllText((Join-Path $testRoot 'docs\downloads\EXE-INFO.json')) | ConvertFrom-Json
    Check 'standalone report matches published bytes and unsigned status' ($singleInfo.executable.sha256 -eq (Hash $publicExecutable).ToLowerInvariant() -and $singleInfo.executable.bytes -eq (Get-Item -LiteralPath $publicExecutable).Length -and $singleInfo.executable.signature -eq 'NotSigned')
    $releaseVersion = $singleInfo.executable.version
    $exeDownloadName = "WhatThePort-v${releaseVersion}.exe"
    $zipDownloadName = "WhatThePort-v${releaseVersion}-Windows-x64.zip"
    $namesUpdated = $true
    foreach ($pageName in @('index.html','guide.html')) {
        $prepared = [IO.File]::ReadAllText((Join-Path $testRoot ('docs\' + $pageName)))
        $namesUpdated = $namesUpdated -and $prepared.Contains('download="' + $exeDownloadName + '"') -and $prepared.Contains('download="' + $zipDownloadName + '"') -and -not $prepared.Contains('stale-download.bin')
    }
    Check 'preparation refreshes stale saved filenames from actual release metadata' $namesUpdated
    $checksums = [IO.File]::ReadAllText((Join-Path $testRoot 'docs\downloads\SHA256SUMS.txt'))
    Check 'checksums use release filenames without changing their bytes' ($checksums.Contains((Hash $publicExecutable).ToLowerInvariant() + '  ' + $exeDownloadName) -and $checksums.Contains((Hash $publicZip).ToLowerInvariant() + '  ' + $zipDownloadName))
    Reject 'standalone wrong checksum is rejected' { & $singleVerify -ExecutablePath $singleExecutable -ExpectedHash ('0' * 64) }
    Reject 'unsigned standalone fails signature requirement' { & $singleVerify -ExecutablePath $singleExecutable -RequireSignature }
    Reject 'separate Core DLL app cannot be published as standalone' { & $singleVerify -ExecutablePath $binaryPaths[0] }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($entryName in @('../escape.txt','hidden/run.cmd','WHATTHEPORT.EXE')) {
        $invalidZip = Join-Path $testRoot 'artifacts\invalid.zip'
        Copy-Item -LiteralPath $zipPath -Destination $invalidZip -Force
        $archive = [IO.Compression.ZipFile]::Open($invalidZip,[IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.CreateEntry($entryName)
            $writer = New-Object IO.StreamWriter($entry.Open())
            try { $writer.Write('harmless fixture') } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
        Reject ('invalid package entry ' + $entryName) { & $verify -PackagePath $invalidZip }
    }
    $beforeMismatchExe = Hash $publicExecutable
    $beforeMismatchZip = Hash $publicZip
    $beforeMismatchPage = Hash (Join-Path $testRoot 'docs\index.html')
    $versionFile = Join-Path $testRoot 'src\Core\AppVersion.cs'
    $versionSource = [IO.File]::ReadAllText($versionFile)
    $parsedVersion = [Version]$releaseVersion
    $nextVersion = '{0}.{1}.{2}' -f $parsedVersion.Major, $parsedVersion.Minor, ($parsedVersion.Build + 1)
    $versionSource = $versionSource.Replace('Current="' + $releaseVersion + '"', 'Current="' + $nextVersion + '"')
    [IO.File]::WriteAllText($versionFile, $versionSource, (New-Object Text.UTF8Encoding($false)))
    $versionRejected = $false
    try { & (Join-Path $testRoot 'scripts\prepare-site.ps1') -AllowUnsigned | Out-Null }
    catch { if ($_.Exception.Message -ne 'Standalone EXE and ZIP must have the same release version.') { throw }; $versionRejected = $true }
    Check 'mixed EXE and ZIP versions fail before publication' $versionRejected
    Check 'version mismatch preserves existing downloads and page' ((Hash $publicExecutable) -eq $beforeMismatchExe -and (Hash $publicZip) -eq $beforeMismatchZip -and (Hash (Join-Path $testRoot 'docs\index.html')) -eq $beforeMismatchPage)
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\package-test-results.txt'), ($passed.ToString() + ' package checks passed'))
    Write-Output ($passed.ToString() + ' package checks passed')
    $completed = $true
} catch {
    Write-Output ('Fixture retained for diagnosis: ' + $testRoot)
    throw
} finally {
    if ($completed) {
        $resolved = (Resolve-Path -LiteralPath $testRoot).ProviderPath
        if ($resolved -ne $testRoot -or -not $resolved.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
        $items = @(Get-Item -LiteralPath $resolved -Force) + @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)
        if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Fixture cleanup refuses reparse points.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Output 'Temporary package fixture removed.'
    }
}
