param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 with .NET Framework 4.8 is required.' }
$outputRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$coreFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\Core') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:library /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\WhatThePort.Core.dll" /r:System.Runtime.Serialization.dll $coreFiles
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
$uiFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\App') -Filter '*.cs' | ForEach-Object FullName)
if ($uiFiles.Count -gt 0) {
    # Use the WPF markup compiler included with Windows; no SDK or packages.
    & (Join-Path $framework 'MSBuild.exe') (Join-Path $PSScriptRoot 'compile-markup.proj') /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'WPF markup compilation failed.' }
    $markupRoot = Join-Path $projectRoot 'artifacts\markup'
    $resources = Join-Path $markupRoot 'WhatThePort.g.resources'
    $writer = New-Object System.Resources.ResourceWriter($resources)
    $markup = [IO.File]::OpenRead((Join-Path $markupRoot 'Shell.baml'))
    try { $writer.AddResource('shell.baml', $markup); $writer.Generate() } finally { $writer.Dispose(); $markup.Dispose() }
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\WhatThePort.exe" "/win32manifest:$projectRoot\src\app.manifest" "/r:$outputRoot\WhatThePort.Core.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\WindowsBase.dll" /r:System.Xaml.dll "/resource:$resources,WhatThePort.g.resources" $uiFiles
    if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $outputRoot 'WhatThePort.exe.config') -Force
}
if (Test-Path -LiteralPath (Join-Path $projectRoot 'src\Cli\Program.cs')) {
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\wtp.exe" "/r:$outputRoot\WhatThePort.Core.dll" (Join-Path $projectRoot 'src\Cli\Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'CLI compilation failed.' }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\fonts') -Destination $outputRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\portable-README.md') -Destination (Join-Path $outputRoot 'README.md') -Force
# Bundle the reading material only. The website's downloads must never be nested in their own ZIP.
$packageDocs = [IO.Path]::GetFullPath((Join-Path $outputRoot 'docs'))
if ($packageDocs -ne [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\docs'))) { throw 'Unexpected package documentation path.' }
if (Test-Path -LiteralPath $packageDocs) { Remove-Item -LiteralPath $packageDocs -Recurse -Force }
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'docs\*.md') -Destination $packageDocs -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $packageDocs -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\images') -Destination $packageDocs -Recurse -Force
if ($Tests) {
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\wtp-tests.exe" "/r:$outputRoot\WhatThePort.Core.dll" (Join-Path $projectRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/win32manifest:$projectRoot\src\app.manifest" "/out:$outputRoot\wtp-ui-tests.exe" "/r:$outputRoot\WhatThePort.Core.dll" "/r:$outputRoot\WhatThePort.exe" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\WindowsBase.dll" /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $projectRoot 'tests\UiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $outputRoot 'wtp-ui-tests.exe.config') -Force
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\wtp-startup-tests.exe" "/r:$outputRoot\WhatThePort.Core.dll" "/r:$outputRoot\WhatThePort.exe" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\WindowsBase.dll" /r:System.Xaml.dll (Join-Path $projectRoot 'tests\StartupTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Startup test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $outputRoot 'wtp-startup-tests.exe.config') -Force
}
Write-Output "Built: $outputRoot"
