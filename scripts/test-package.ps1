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
    & (Join-Path $testRoot 'scripts\prepare-site.ps1') -AllowUnsigned
    Check 'explicit test distribution publishes verified ZIP' ((Hash $publicZip) -eq (Hash $zipPath))
    Check 'test distribution publishes integrity report' (Test-Path -LiteralPath (Join-Path $testRoot 'docs\downloads\PACKAGE-INFO.json'))
    $singleVerify = Join-Path $testRoot 'scripts\verify-single-file.ps1'
    $singleExecutable = Join-Path $testRoot 'artifacts\single-file\WhatThePort.exe'
    $publicExecutable = Join-Path $testRoot 'docs\downloads\WhatThePort.exe'
    Check 'test distribution publishes standalone EXE' ((Hash $singleExecutable) -eq (Hash $publicExecutable))
    $singleInfo = [IO.File]::ReadAllText((Join-Path $testRoot 'docs\downloads\EXE-INFO.json')) | ConvertFrom-Json
    Check 'standalone report matches published bytes and unsigned status' ($singleInfo.executable.sha256 -eq (Hash $publicExecutable).ToLowerInvariant() -and $singleInfo.executable.bytes -eq (Get-Item -LiteralPath $publicExecutable).Length -and $singleInfo.executable.signature -eq 'NotSigned')
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
