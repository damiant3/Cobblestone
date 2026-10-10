[CmdletBinding()]
param([switch]$Check)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# The Codex DOM pages (AdminLoginPage, AdminPanelPage) compiled by the HTML plug, without the plug's font pack, become
# Text definitions in AdminPage.codex (AdminPanel.md).
function Add-TextDef([Collections.Generic.List[string]]$Lines, [string]$Name, [string[]]$Source) {
    $Lines.Add('')
    $Lines.Add("  $Name : Text")
    $Lines.Add("  $Name =")
    $first = $true
    foreach ($line in $Source) {
        $escaped = $line.Replace('\','\\').Replace('"','\"')
        $prefix = if ($first) {'    '} else {'    & '}
        $Lines.Add($prefix + '"' + $escaped + '\n"')
        $first = $false
    }
}
function Get-CodexPage([string]$Chapter) {
    $plug = Join-Path $repo 'codex/plugs/html/build-output/html-plug.cdx'
    if (-not (Test-Path $plug)) { & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/build.ps1') | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'HTML plug build failed' } }
    $work = Join-Path ([IO.Path]::GetTempPath()) ('admin-page-' + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $work)
    try {
        & pwsh -NoProfile -File (Join-Path $repo 'build/bundle-app.ps1') -Src (Join-Path $PSScriptRoot "$Chapter.codex") -Out (Join-Path $work 'page.codex') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$Chapter bundle failed" }
        & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/html/run.ps1') -Src (Join-Path $work 'page.codex') -Out (Join-Path $work 'page.html') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$Chapter did not compile through the HTML plug" }
        $html = [IO.File]::ReadAllText((Join-Path $work 'page.html'))
        $html = [regex]::Replace($html, '<style id="codex-font-pack">[\s\S]*?</style><script type="application/json" id="codex-font-licenses">[\s\S]*?</script>', '')
        if ($html -match '[^\x00-\x7F]') { throw "$Chapter page carries a non-ASCII byte" }
        return [string[]]($html.Replace("`r", '') -split "`n")
    } finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('Chapter: AdminPage')
# A compiled page is a thousand lines or more; one definition holds at most 200 of them, so the name resolver's
# expression budget (1024, CDX9001) is never reached.
foreach ($entry in @(@('panel-login-next','AdminLoginPage'),@('panel-page-next','AdminPanelPage'))) {
    $page = Get-CodexPage $entry[1]
    $parts = [Collections.Generic.List[string]]::new()
    for ($at = 0; $at -lt $page.Count; $at += 200) {
        $name = "$($entry[0])-$($parts.Count)"
        Add-TextDef $lines $name ($page[$at..([Math]::Min($at + 199, $page.Count - 1))])
        $parts.Add($name)
    }
    $lines.Add('')
    $lines.Add("  $($entry[0]) : Text")
    $lines.Add("  $($entry[0]) = " + ($parts -join ' & '))
}
$expected = ($lines -join "`r`n") + "`r`n"
$target = Join-Path $PSScriptRoot 'AdminPage.codex'
if ($Check) {
    if (-not (Test-Path $target) -or [IO.File]::ReadAllText($target) -cne $expected) { throw 'AdminPage.codex differs from the Codex page sources' }
    'Admin page source parity PASS'
} else { [IO.File]::WriteAllText($target,$expected,[Text.UTF8Encoding]::new($false)) }
