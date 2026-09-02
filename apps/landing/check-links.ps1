# Check every link the BUILT site emits, against the way it actually ships.
#
# The landing pages deploy as static HTML (Damian, 2026-08-26; the shape is in
# docs/Designs/Active/Marketing/Cobblestone.md). serve.ps1's /prism/ and /repl/
# proxies do not exist on a static host, so a root-relative link works locally
# and is dead the moment it ships. Two links were dead exactly that way before
# anything checked, and nothing else in the tree looks.
#
# This is NOT wired into build.ps1 or any gate. It is a standalone check: run
# it after a build, and before a public push.
#
# Usage: apps/landing/check-links.ps1 [-Web <dir>] [-Live]
#   -Live  also fetch every external URL and report its status.
[CmdletBinding()]
param(
    [string]$Web,
    [switch]$Live
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Web) { $Web = Join-Path $PSScriptRoot 'web' }
if (-not (Test-Path -PathType Container $Web)) {
    Write-Host "REFUSE: no built site at $Web. Run apps/landing/build.ps1 first."; exit 2
}
$Web = (Resolve-Path $Web).Path

$pages = @(Get-ChildItem $Web -Recurse -Filter *.html | Sort-Object FullName)
if ($pages.Count -eq 0) { Write-Host "REFUSE: no .html under $Web"; exit 2 }

$dead = @(); $rootRel = @(); $ok = 0; $external = @()

foreach ($p in $pages) {
    $html = [IO.File]::ReadAllText($p.FullName)
    $rel  = $p.FullName.Substring($Web.Length).TrimStart('\')
    $hrefs = New-Object System.Collections.Generic.HashSet[string]

    # site_link(...) is what the html plug emits for a Codex site-link.
    foreach ($m in [regex]::Matches($html, 'site_link\("[^"]*",\s*"[^"]*",\s*"([^"]*)"')) {
        [void]$hrefs.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($html, 'href="([^"#][^"]*)"'))  { [void]$hrefs.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($html, "url\('([^']+)'\)"))     { [void]$hrefs.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($html, "fetch\('([^']+)'\)"))   { [void]$hrefs.Add($m.Groups[1].Value) }

    foreach ($h in $hrefs) {
        if ($h -match '^(data:|mailto:|javascript:)') { continue }
        # A fragment-only href is an in-page anchor. The page assigns DOM ids at
        # runtime (el.id from the widget id), so the file check cannot see it;
        # the checkable fact is that the fragment appears as a quoted widget id
        # in this page's own emitted tree.
        if ($h.StartsWith('#')) {
            $frag = $h.Substring(1)
            if ($frag.Length -gt 0 -and $html.Contains('"' + $frag + '"')) { $ok++ }
            else { $dead += ,@($rel, "$h (no widget id in page)") }
            continue
        }
        if ($h -match '^https?://') { $external += ,@($rel, $h); continue }
        if ($h.StartsWith('/')) { $rootRel += ,@($rel, $h); continue }
        # `page.html#anchor` is a path AND a fragment, and it used to be
        # resolved whole: the file "landing.html#today" is on no disk, so a
        # link that works in every browser was reported DEAD. Split the
        # fragment off, then hold it to the same standard the fragment-only
        # branch above uses -- the id has to appear as a quoted widget id in
        # the page being linked TO. That is what caught `#showcase` on the
        # games page, which named a section this site does not have.
        $path = ($h -split '[?#]')[0]
        $frag = if ($h.Contains('#')) { ($h -split '#', 2)[1] } else { '' }
        $target = Join-Path (Split-Path $p.FullName) $path
        if (-not (Test-Path -PathType Leaf $target)) { $dead += ,@($rel, $h); continue }
        if ($frag -and $path -match '\.html?$') {
            $targetHtml = [IO.File]::ReadAllText((Resolve-Path $target).Path)
            if (-not $targetHtml.Contains('"' + $frag + '"')) {
                $dead += ,@($rel, "$h (no widget id '$frag' in $path)"); continue
            }
        }
        $ok++
    }
}

Write-Host "[links] $($pages.Count) page(s), $ok relative link(s) resolve on disk"

if ($rootRel.Count -gt 0) {
    Write-Host ''
    Write-Host "[links] ROOT-RELATIVE ($($rootRel.Count)) -- these resolve only behind a server:"
    foreach ($r in $rootRel) { Write-Host ("  {0,-24} {1}" -f $r[0], $r[1]) }
    Write-Host '  A root-relative link is a WARNING, not a failure, because the html'
    Write-Host '  plug emits shared runtime helpers that no page calls. Confirm the'
    Write-Host '  helper is genuinely uncalled before waving one through: measured'
    Write-Host '  2026-08-26, /api/config sits inside check_sd_status, which appears'
    Write-Host '  once as a definition and is never invoked, and the sd-dot and'
    Write-Host '  sd-label elements it wants are on no page here.'
}

if ($external.Count -gt 0) {
    Write-Host ''
    Write-Host "[links] EXTERNAL ($($external.Count)):"
    foreach ($e in $external) {
        if ($Live) {
            $status = try {
                (Invoke-WebRequest -Uri $e[1] -TimeoutSec 20 -UseBasicParsing -MaximumRedirection 3).StatusCode
            } catch { "FAILED $($_.Exception.Message)" }
            Write-Host ("  {0,-14} {1,-24} {2}" -f $status, $e[0], $e[1])
        } else {
            Write-Host ("  {0,-24} {1}" -f $e[0], $e[1])
        }
    }
    if (-not $Live) { Write-Host '  (pass -Live to fetch each one)' }
}

if ($dead.Count -gt 0) {
    Write-Host ''
    Write-Host "[links] DEAD ($($dead.Count)) -- a relative link with no file behind it:"
    foreach ($d in $dead) { Write-Host ("  {0,-24} {1}" -f $d[0], $d[1]) }
    Write-Host '[links] FAIL'
    exit 1
}

Write-Host ''
Write-Host '[links] OK'
exit 0
