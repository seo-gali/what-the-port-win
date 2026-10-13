param(
    [Parameter(Mandatory=$true)][string]$CertificateThumbprint,
    [Parameter(Mandatory=$true)][string]$TimestampServer,
    [switch]$SingleFile
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$thumbprint = $CertificateThumbprint.Replace(' ','').ToUpperInvariant()
if ($thumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Enter the public thumbprint of an installed code-signing certificate.' }
# Windows PowerShell's Authenticode timestamp provider requires an HTTP URL.
$timestampUri = $null
if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$timestampUri) -or $timestampUri.Scheme -ne 'http' -or $timestampUri.UserInfo -or $timestampUri.IsLoopback) { throw 'Use the Authenticode timestamp URL supplied by your certificate authority (http://).' }
$certificates = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert; Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert)
$certificate = $certificates | Where-Object { $_.Thumbprint -eq $thumbprint } | Select-Object -First 1
if (-not $certificate -or -not $certificate.HasPrivateKey) { throw 'The requested code-signing certificate is not installed with a usable signing key.' }
if ($certificate.Subject -eq $certificate.Issuer) { throw 'Self-signed certificates are not accepted for public distribution.' }
if ($certificate.NotBefore -gt [DateTime]::Now -or $certificate.NotAfter -le [DateTime]::Now) { throw 'The signing certificate is outside its validity period.' }
$chain = New-Object Security.Cryptography.X509Certificates.X509Chain
try {
    $chain.ChainPolicy.ApplicationPolicy.Add((New-Object Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
    if (-not $chain.Build($certificate)) { throw 'A publicly trusted code-signing certificate is required; self-signed certificates do not establish publisher trust.' }
} finally { $chain.Dispose() }
$runtimeRoot = if ($SingleFile) { Join-Path $projectRoot 'artifacts\single-file' } else { Join-Path $projectRoot 'dist' }
$runtimeNames = if ($SingleFile) { @('WhatThePort.exe') } else { @('WhatThePort.exe','WhatThePort.Core.dll','wtp.exe') }
foreach ($name in $runtimeNames) {
    $file = Join-Path $runtimeRoot $name
    $existing = Get-AuthenticodeSignature -LiteralPath $file
    if ($existing.Status -eq 'Valid' -and $existing.SignerCertificate.Thumbprint -eq $thumbprint -and $existing.TimeStamperCertificate) {
        Write-Output ('Already signed; unchanged: ' + $name)
        continue
    }
    $result = Set-AuthenticodeSignature -LiteralPath $file -Certificate $certificate -HashAlgorithm SHA256 -IncludeChain NotRoot -TimestampServer $TimestampServer
    $verified = Get-AuthenticodeSignature -LiteralPath $file
    if ($result.Status -ne 'Valid' -or $verified.Status -ne 'Valid' -or -not $verified.TimeStamperCertificate) { throw ('Signing or timestamp verification failed: ' + $name) }
    Write-Output ('Signed and timestamped: ' + $name)
}
# Update the local build cache so packaging does not undo a valid signature.
$cachePath = if ($SingleFile) { Join-Path $runtimeRoot 'build-cache.json' } else { Join-Path $projectRoot 'artifacts\package-cache.json' }
if (Test-Path -LiteralPath $cachePath) {
    $cache = [IO.File]::ReadAllText($cachePath) | ConvertFrom-Json
    if ($SingleFile) { $cache.sha256 = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot 'WhatThePort.exe') -Algorithm SHA256).Hash.ToLowerInvariant() }
    else { foreach ($item in $cache.runtime) { $item.sha256 = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot $item.name) -Algorithm SHA256).Hash.ToLowerInvariant() } }
    [IO.File]::WriteAllText($cachePath, ($cache | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
}
