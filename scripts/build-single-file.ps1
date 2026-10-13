param([switch]$Tests, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 with .NET Framework 4.8 is required.' }
$outputRoot = Join-Path $projectRoot 'artifacts\single-file'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$executable = Join-Path $outputRoot 'WhatThePort.exe'
$cachePath = Join-Path $outputRoot 'build-cache.json'
$coreFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\Core') -Filter '*.cs' | ForEach-Object FullName)
$uiFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\App') -Filter '*.cs' | ForEach-Object FullName)
$fontFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'assets\fonts') -Filter '*.ttf' | ForEach-Object FullName)
$inputs = @($coreFiles) + @($uiFiles) + @($fontFiles) + @((Join-Path $projectRoot 'src\App\Shell.xaml'),(Join-Path $projectRoot 'src\app.manifest'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'assets\fonts\OFL.txt'),$PSCommandPath,(Join-Path $PSScriptRoot 'compile-markup.proj'))
$records = @($inputs | Sort-Object | ForEach-Object { $_.Substring($projectRoot.Length) + ':' + (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
$algorithm = [Security.Cryptography.SHA256]::Create()
try { $inputHash = ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($records -join "`n"))))).Replace('-','').ToLowerInvariant() } finally { $algorithm.Dispose() }
$cached = $false
if (-not $Rebuild -and (Test-Path -LiteralPath $cachePath) -and (Test-Path -LiteralPath $executable)) {
    try { $cache = [IO.File]::ReadAllText($cachePath) | ConvertFrom-Json; $cached = $cache.inputs -eq $inputHash -and $cache.sha256 -eq (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() } catch { $cached = $false }
}
$references = @("/r:$framework\WPF\PresentationFramework.dll","/r:$framework\WPF\PresentationCore.dll","/r:$framework\WPF\WindowsBase.dll",'/r:System.Xaml.dll','/r:System.Runtime.Serialization.dll')
if ($cached) { Write-Output 'Reusing unchanged standalone executable.' } else {
    $markupRoot = Join-Path $outputRoot 'markup'
    & (Join-Path $framework 'MSBuild.exe') (Join-Path $PSScriptRoot 'compile-markup.proj') /nologo /verbosity:minimal ("/p:OutputPath=" + $markupRoot + '\')
    if ($LASTEXITCODE -ne 0) { throw 'WPF markup compilation failed.' }
    $resources = Join-Path $markupRoot 'WhatThePort.g.resources'
    $writer = New-Object System.Resources.ResourceWriter($resources)
    $streams = New-Object 'Collections.Generic.List[IO.Stream]'
    try {
        $markup = [IO.File]::OpenRead((Join-Path $markupRoot 'Shell.baml')); $streams.Add($markup)
        $writer.AddResource('shell.baml', $markup)
        foreach ($font in $fontFiles) {
            $stream = [IO.File]::OpenRead($font); $streams.Add($stream)
            $writer.AddResource(('fonts/' + [IO.Path]::GetFileName($font).ToLowerInvariant()), $stream)
        }
        $writer.Generate()
    } finally { $writer.Dispose(); foreach ($stream in $streams) { $stream.Dispose() } }
    # Compile the UI and independent Core source into one normal managed assembly.
    # No unpacker, AssemblyResolve loader, or temporary runtime extraction.
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /define:SINGLE_FILE "/out:$executable" "/win32manifest:$projectRoot\src\app.manifest" $references "/resource:$resources,WhatThePort.g.resources" "/resource:$projectRoot\LICENSE,WhatThePort.LICENSE.txt" "/resource:$projectRoot\assets\fonts\OFL.txt,WhatThePort.OFL.txt" $coreFiles $uiFiles
    if ($LASTEXITCODE -ne 0) { throw 'Standalone app compilation failed.' }
    $cache = [ordered]@{ inputs = $inputHash; sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() }
    [IO.File]::WriteAllText($cachePath, ($cache | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}
if ($Tests) {
    foreach ($test in @('CoreTests','UiTests','StartupTests','SingleFileTests')) {
        $name = @{ CoreTests='wtp-tests'; UiTests='wtp-ui-tests'; StartupTests='wtp-startup-tests'; SingleFileTests='wtp-single-file-tests' }[$test]
        & $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ "/out:$outputRoot\$name.exe" "/r:$executable" $references /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $projectRoot ('tests\' + $test + '.cs'))
        if ($LASTEXITCODE -ne 0) { throw ('Test compilation failed: ' + $test) }
        if ($test -ne 'SingleFileTests') { Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $outputRoot ($name + '.exe.config')) -Force }
    }
}
Write-Output "Standalone build: $executable"