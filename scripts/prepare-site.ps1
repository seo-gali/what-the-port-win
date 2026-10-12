param([switch]$AllowUnsigned)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$siteRoot = Join-Path $projectRoot 'docs'
$package = Join-Path $projectRoot 'artifacts\WhatThePort-Windows-x64.zip'
if (-not (Test-Path -LiteralPath $package)) { throw 'Run scripts/package.ps1 before preparing the download site.' }
# Validate before changing any public site files.
$report = Join-Path $projectRoot 'artifacts\package-verification.json'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $package -ReportPath $report -RequireSignature:(-not $AllowUnsigned)
foreach ($folder in @('assets\images','assets\fonts','downloads')) { New-Item -ItemType Directory -Force -Path (Join-Path $siteRoot $folder) | Out-Null }
Copy-Item -Path (Join-Path $projectRoot 'docs\images\*.png') -Destination (Join-Path $siteRoot 'assets\images') -Force
foreach ($font in @('Geist-Regular.ttf','Geist-Medium.ttf','OFL.txt')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('assets\fonts\' + $font)) -Destination (Join-Path $siteRoot 'assets\fonts') -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $siteRoot 'LICENSE.txt') -Force
Copy-Item -LiteralPath $package -Destination (Join-Path $siteRoot 'downloads') -Force
Copy-Item -LiteralPath $report -Destination (Join-Path $siteRoot 'downloads\PACKAGE-INFO.json') -Force
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $siteRoot 'downloads\SHA256SUMS.txt'), ($hash + '  WhatThePort-Windows-x64.zip' + [Environment]::NewLine), [Text.Encoding]::ASCII)
$index = Join-Path $siteRoot 'index.html'
$html = [IO.File]::ReadAllText($index)
$size = ((Get-Item -LiteralPath $package).Length / 1MB).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) + ' MB'
$html = [regex]::Replace($html, '<span data-package-size>.*?</span>', ('<span data-package-size>' + $size + '</span>'))
[IO.File]::WriteAllText($index, $html, (New-Object Text.UTF8Encoding($false)))
if ($AllowUnsigned) { Write-Warning 'Unsigned test distribution: Chrome reputation warnings may remain. This does not disable browser protection.' }
Write-Output "Prepared static site: $siteRoot"
Write-Output "Download: $size; SHA-256 $hash"
