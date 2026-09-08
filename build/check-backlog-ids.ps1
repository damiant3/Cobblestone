# check-backlog-ids.ps1 -- refuse a *-backlog.md that gives two rows the same id.
#
# The registers hand out ids and nothing checked them, which is L-BODY's shape:
# the rule was prose plus whoever noticed. Measured 2026-09-07: plugs-backlog.md
# carried TWO rows numbered 2.10 (the Windows .exe, and the binary tab) and TWO
# numbered 2.11 (the hosted Linux/Windows apps, and the wasm binary encoding).
# It cost twice in one session. A CL deleting "2.10" nearly took the wrong row,
# and two lanes appending on the same morning both claimed 2.30 and 2.31, so one
# lane's row was silently discarded by a resolve.
#
# An id is what a CL, a review or another register CITES, so a duplicate does
# not merely look untidy: it makes every citation ambiguous, and the ambiguity
# is invisible at the citing end.
#
# Two id shapes are in use and both are checked:
#   ## 2.10 -- ...      / **2.10 -- ...      numeric, plugs and product
#   | COMPILER-40 | ... named, compiler, works and games
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # A single file, for the sabotage arm and for checking one register.
    [string]$File = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$files = if ($File) { @(Get-Item -LiteralPath $File) }
         else { @(Get-ChildItem -Path $Root -Recurse -Filter '*-backlog.md' -File) }

# The `--` separator is required so a mention in prose cannot be read as a row.
$numeric = '^\s{0,3}(?:#{2,3}\s+|\*\*)(\d+\.\d+)\s+--'
$named   = '^\|\s*\*{0,2}([A-Z][A-Z0-9]*-\d+)\*{0,2}\s*\|'

$dupes = 0
$scanned = 0
foreach ($f in $files) {
    $scanned++
    $seen = @{}
    $lines = Get-Content -LiteralPath $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $id = $null
        if ($lines[$i] -match $numeric) { $id = $Matches[1] }
        elseif ($lines[$i] -match $named) { $id = $Matches[1] }
        if (-not $id) { continue }
        if (-not $seen.ContainsKey($id)) { $seen[$id] = @() }
        $seen[$id] += ($i + 1)
    }
    foreach ($id in ($seen.Keys | Sort-Object)) {
        if ($seen[$id].Count -lt 2) { continue }
        $dupes++
        $rel = $f.FullName.Replace($Root + [IO.Path]::DirectorySeparatorChar, '')
        Write-Host "DUPLICATE ID  $rel  '$id' on $($seen[$id].Count) rows:"
        foreach ($ln in $seen[$id]) {
            $text = $lines[$ln - 1].Trim()
            if ($text.Length -gt 96) { $text = $text.Substring(0, 96) }
            Write-Host ("    line {0,6}: {1}" -f $ln, $text)
        }
    }
}

Write-Host ''
if ($dupes -gt 0) {
    Write-Host "check-backlog-ids: $dupes duplicate id(s) across $scanned register(s). An id is what a CL cites; two rows cannot share one."
    exit 1
}
Write-Host "check-backlog-ids: $scanned register(s), no duplicate ids."
exit 0
