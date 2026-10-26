param([switch]$AllowUnsigned)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$siteRoot = Join-Path $projectRoot 'docs'
$package = Join-Path $projectRoot 'artifacts\WhatThePort-Windows-x64.zip'
if (-not (Test-Path -LiteralPath $package)) { throw 'Run scripts/package.ps1 before preparing the download site.' }
# Validate before changing any public site files.
$report = Join-Path $projectRoot 'artifacts\package-verification.json'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $package -ReportPath $report -RequireSignature:(-not $AllowUnsigned)
& (Join-Path $PSScriptRoot 'build-single-file.ps1')
$standalone = Join-Path $projectRoot 'artifacts\single-file\WhatThePort.exe'
$standaloneReport = Join-Path $projectRoot 'artifacts\single-file-verification.json'
& (Join-Path $PSScriptRoot 'verify-single-file.ps1') -ExecutablePath $standalone -ReportPath $standaloneReport -RequireSignature:(-not $AllowUnsigned)
$exeInfo = [IO.File]::ReadAllText($standaloneReport) | ConvertFrom-Json
$packageInfo = [IO.File]::ReadAllText($report) | ConvertFrom-Json
$version = [string]$exeInfo.executable.version
if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or @($packageInfo.binaries | Where-Object { $_.version -ne $version }).Count) { throw 'Standalone EXE and ZIP must have the same release version.' }
& (Join-Path $PSScriptRoot 'update-sitemap.ps1')
$exeDownloadName = "WhatThePort-v${version}.exe"
$zipDownloadName = "WhatThePort-v${version}-Windows-x64.zip"
foreach ($folder in @('assets\images','assets\fonts','downloads')) { New-Item -ItemType Directory -Force -Path (Join-Path $siteRoot $folder) | Out-Null }
Copy-Item -Path (Join-Path $projectRoot 'docs\images\*.png') -Destination (Join-Path $siteRoot 'assets\images') -Force
foreach ($font in @('Geist-Regular.ttf','Geist-Medium.ttf','OFL.txt')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('assets\fonts\' + $font)) -Destination (Join-Path $siteRoot 'assets\fonts') -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $siteRoot 'LICENSE.txt') -Force
Copy-Item -LiteralPath $package -Destination (Join-Path $siteRoot 'downloads') -Force
Copy-Item -LiteralPath $report -Destination (Join-Path $siteRoot 'downloads\PACKAGE-INFO.json') -Force
Copy-Item -LiteralPath $standalone -Destination (Join-Path $siteRoot 'downloads\WhatThePort.exe') -Force
Copy-Item -LiteralPath $standaloneReport -Destination (Join-Path $siteRoot 'downloads\EXE-INFO.json') -Force
$exeHash = (Get-FileHash -LiteralPath $standalone -Algorithm SHA256).Hash.ToLowerInvariant()
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $siteRoot 'downloads\SHA256SUMS.txt'), ($hash + '  ' + $zipDownloadName + [Environment]::NewLine + $exeHash + '  ' + $exeDownloadName + [Environment]::NewLine), [Text.Encoding]::ASCII)
$index = Join-Path $siteRoot 'index.html'
$html = [IO.File]::ReadAllText($index)
$size = ((Get-Item -LiteralPath $package).Length / 1MB).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) + ' MB'
$html = [regex]::Replace($html, '<span data-package-size>.*?</span>', ('<span data-package-size>' + $size + '</span>'))
$exeSize = ((Get-Item -LiteralPath $standalone).Length / 1KB).ToString('0', [Globalization.CultureInfo]::InvariantCulture) + ' KB'
$html = [regex]::Replace($html, '<span data-exe-size>.*?</span>', ('<span data-exe-size>' + $exeSize + '</span>'))
foreach ($pageName in @('index.html','guide.html')) {
    $pagePath = Join-Path $siteRoot $pageName
    $pageHtml = if ($pageName -eq 'index.html') { $html } else { [IO.File]::ReadAllText($pagePath) }
    $pageHtml = [regex]::Replace($pageHtml, '(href="downloads/WhatThePort\.exe"\s+download)(?:="[^"]*")?', ('$1="' + $exeDownloadName + '"'))
    $pageHtml = [regex]::Replace($pageHtml, '(href="downloads/WhatThePort-Windows-x64\.zip"\s+download)(?:="[^"]*")?', ('$1="' + $zipDownloadName + '"'))
    [IO.File]::WriteAllText($pagePath, $pageHtml, (New-Object Text.UTF8Encoding($false)))
}
if ($AllowUnsigned) { Write-Warning 'Unsigned test distribution: Chrome reputation warnings may remain. This does not disable browser protection.' }
Write-Output "Prepared static site: $siteRoot"
Write-Output "Optional ZIP: $size; SHA-256 $hash"
Write-Output "Standalone EXE: $exeSize; SHA-256 $exeHash"
