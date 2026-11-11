<#
.SYNOPSIS
  Makes the HTML pages of the test terms and the privacy notice from their text files (installer\docs), so that they open as readable
  pages in the web browser (About icono, Help). The text files stay the one source (the installer shows them too); run this script again
  after every change to them.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\make-doc-html.ps1
#>
$docs = Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\docs'

$css = @'
  :root { --bg:#ffffff; --fg:#1b2332; --muted:#5b6472; --line:#d9dee7; --accent:#1f5fd0; --box:#f3f6fb; }
  @media (prefers-color-scheme: dark) { :root { --bg:#16181d; --fg:#e8eaee; --muted:#a3aab6; --line:#2e323a; --accent:#6aa3ff; --box:#1d2027; } }
  body { margin:0; background:var(--bg); color:var(--fg); font:16px/1.6 "Segoe UI", system-ui, sans-serif; }
  main { max-width:820px; margin:0 auto; padding:24px 20px 80px; }
  h1 { font-size:1.8rem; margin:0 0 .4em; } h2 { margin:1.8em 0 .3em; padding-top:.5em; border-top:1px solid var(--line); font-size:1.2rem; }
  p, li { margin:.4em 0; } .muted { color:var(--muted); } a { color:var(--accent); }
  code { background:var(--box); border:1px solid var(--line); border-radius:4px; padding:1px 6px; font:0.92em Consolas, monospace; }
  @media print { h2 { page-break-after:avoid; } }
'@

function Esc([string]$s) { $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;') }

# state of the block that is being read (script scope, so the helper functions share it)
$script:out = $null; $script:block = $null; $script:text = ''; $script:inList = $false

function Flush {
    if ($script:block -eq 'p') { $script:out.Add("<p>$(Esc $script:text)</p>") }
    elseif ($script:block -eq 'li') { $script:out.Add("<li>$(Esc $script:text)</li>") }
    $script:block = $null; $script:text = ''
}

function CloseList {
    if ($script:inList) { Flush; $script:out.Add('</ul>'); $script:inList = $false }
}

# the small amount of markup the text files use - nothing is guessed: a line that starts with "N. " is a heading, a line with three spaces is
# a paragraph, "   - " starts a list item, deeper indented lines continue the block before
function Convert-Doc([string]$source, [string]$target, [string]$lang) {
    $lines = [IO.File]::ReadAllLines($source, [Text.Encoding]::UTF8)
    $script:out = New-Object System.Collections.Generic.List[string]
    $script:block = $null; $script:text = ''; $script:inList = $false
    $title = ($lines[0] -replace '^\uFEFF', '').Trim()
    foreach ($raw in $lines | Select-Object -Skip 1) {
        $line = $raw.TrimEnd()
        if ($line -match '^=+$') { continue }
        if ($line -eq '') { Flush; continue }
        if ($line -match '^\d+\.\s') { Flush; CloseList; $script:out.Add("<h2>$(Esc $line)</h2>"); continue }
        if ($line -match '^   - (.*)$') {
            Flush
            if (-not $script:inList) { $script:out.Add('<ul>'); $script:inList = $true }
            $script:block = 'li'; $script:text = ($Matches[1] -replace '\s{3,}', ' - ').Trim(); continue
        }
        if ($line -match '^   (\S.*)$') {
            # the text files are wrapped at about 140 characters: a line that follows a sentence that has not ended goes on in the same paragraph
            $t = $Matches[1]
            if ($script:block -eq 'p' -and $script:text -notmatch '[.!?:]$') { $script:text = $script:text + ' ' + $t } else { Flush; CloseList; $script:block = 'p'; $script:text = $t }
            continue
        }
        if ($line -match '^\s{4,}(\S.*)$') { $script:text = ($script:text + ' ' + $Matches[1].Trim()).Trim(); continue }
        # a line at the left edge: the introduction, the contact and the date
        Flush; CloseList; $script:block = 'p'; $script:text = $line
    }
    Flush; CloseList
    $body = $script:out -join "`n"
    $html = @"
<!DOCTYPE html>
<html lang="$lang">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$(Esc $title)</title>
<style>
$css
</style>
</head>
<body>
<main>
<h1>$(Esc $title)</h1>
$body
</main>
</body>
</html>
"@
    [IO.File]::WriteAllText($target, $html, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Written: $target"
}

Convert-Doc (Join-Path $docs 'Testbedingungen-DE.txt') (Join-Path $docs 'Testbedingungen-DE.html') 'de'
Convert-Doc (Join-Path $docs 'Terms-EN.txt') (Join-Path $docs 'Terms-EN.html') 'en'
Convert-Doc (Join-Path $docs 'Datenschutz-DE.txt') (Join-Path $docs 'Datenschutz-DE.html') 'de'
Convert-Doc (Join-Path $docs 'Privacy-EN.txt') (Join-Path $docs 'Privacy-EN.html') 'en'
