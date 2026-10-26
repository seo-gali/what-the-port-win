$ErrorActionPreference = 'Stop'
$siteRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs'
$publicBase = 'https://seo-gali.github.io/what-the-port-win/'
$locations = @(
    foreach ($page in (Get-ChildItem -LiteralPath $siteRoot -Filter '*.html' -File -Recurse | Sort-Object FullName)) {
        $html = [IO.File]::ReadAllText($page.FullName)
        if ($html -notmatch '<main\b') { continue }
        $blocked = @([regex]::Matches($html, '<meta\b[^>]*>') | Where-Object { $_.Value -match 'name="(?:robots|googlebot)"' -and $_.Value -match 'content="[^"]*\b(?:noindex|none)\b' })
        if ($blocked.Count) { continue }
        $relative = $page.FullName.Substring($siteRoot.Length + 1).Replace('\','/')
        $expected = if ($relative -eq 'index.html') { $publicBase } else { ([Uri]::new([Uri]$publicBase, $relative)).AbsoluteUri }
        $canonical = [regex]::Matches($html, '<link\b[^>]*\brel="canonical"[^>]*\bhref="([^"]+)"[^>]*>')
        if ($canonical.Count -ne 1 -or $canonical[0].Groups[1].Value -ne $expected) { throw ('Missing or incorrect canonical URL: ' + $relative) }
        [Security.SecurityElement]::Escape($expected)
    }
)
if (-not $locations.Count) { throw 'No indexable HTML pages found; existing sitemap preserved.' }
$xml = @('<?xml version="1.0" encoding="UTF-8"?>','<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">') + @($locations | Sort-Object | ForEach-Object { '  <url><loc>' + $_ + '</loc></url>' }) + '</urlset>'
[IO.File]::WriteAllText((Join-Path $siteRoot 'sitemap.xml'), (($xml -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
Write-Output ('Updated sitemap: ' + $locations.Count + ' canonical pages.')
