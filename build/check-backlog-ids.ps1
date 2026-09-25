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
# Three id shapes are in use and all are checked:
#   ## 2.10 -- ...      / **2.10 -- ...      numeric, plugs and product
#   | COMPILER-40 | ... named, compiler, works and games
#   ## WORKS-68: ...                         named, as a heading
#
# REUSE. A closed row is deleted, so its id is free in the register and taken
# again, while every CL that named the old row still names it. COMPILER-88 and
# WORKS-68 were both reused on 2026-09-23/24 this way, and WORKS-68's second
# row sat beside the first, as a table row under a heading this check could not
# read. So a NEW named row (an id the base revision does not carry) is refused
# when any submitted CL description older than the row already names its id.
#   default     registers opened in this workspace, against #have
#   -Change N   registers in submitted CL N, against the depot at N-1. The CL
#               that ADDED the row is found by following N's integration back
#               to its edit, and only descriptions older than that edit count.
# Numeric ids are not graded for reuse: "2.10" is not searchable in CL text.
# Descriptions are cached under build-output\check-backlog-ids and fetched only
# when a new named id exists; a cold fetch of every //Codex CL measured 53 s
# (2026-09-24). With nothing opened, as in the release gate, no row is new and
# the reuse check does nothing.
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # A single file, for the sabotage arm and for checking one register.
    [string]$File = '',
    [int]$Change = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$files = if ($File) { @(Get-Item -LiteralPath $File) }
         else { @(Get-ChildItem -Path $Root -Recurse -Filter '*-backlog.md' -File |
                  Where-Object { $_.FullName -notmatch '\\(build-output|old)\\' }) }

# The `--` separator is required so a mention in prose cannot be read as a row.
$numeric = '^\s{0,3}(?:#{2,3}\s+|\*\*)(\d+\.\d+)\s+--'
$named   = '^\|\s*\*{0,2}([A-Z][A-Z0-9]*-\d+)\*{0,2}\s*\|'
$heading = '^#{2,4}\s+\*{0,2}([A-Z][A-Z0-9]*-\d+)\*{0,2}\s*[:.\s]'

function Get-NamedIds([string[]]$lines) {
    $ids = @{}
    foreach ($l in $lines) {
        if ($l -match $named) { $ids[$Matches[1]] = $true }
        elseif ($l -match $heading) { $ids[$Matches[1]] = $true }
    }
    $ids
}

$dupes = 0
$scanned = 0
foreach ($f in $files) {
    $scanned++
    $seen = @{}
    $lines = @(Get-Content -LiteralPath $f.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $id = $null
        if ($lines[$i] -match $numeric) { $id = $Matches[1] }
        elseif ($lines[$i] -match $named) { $id = $Matches[1] }
        elseif ($lines[$i] -match $heading) { $id = $Matches[1] }
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

# The CL that added a row: walk back from CL N while the revision still carries
# the id, first through the same path's earlier revisions, then through the
# integration it came from. A copy-up's stream revision can be a merge-down, and
# its merge source is main WITHOUT the row, so the id decides each step.
function Get-RowOrigin([string]$path, [string]$id, [int]$change) {
    $at = "$path@=$change"
    $origin = $change
    for ($hop = 0; $hop -lt 64; $hop++) {
        $log = @(p4 filelog -m 1 $at 2>$null)
        $top = $log | Where-Object { $_ -match '^\.\.\. #(\d+) change (\d+) ' } | Select-Object -First 1
        if (-not $top) { break }
        $null = $top -match '^\.\.\. #(\d+) change (\d+) '
        $rev = [int]$Matches[1]
        $origin = [int]$Matches[2]
        $file = ($log | Where-Object { $_ -match '^//' } | Select-Object -First 1)
        $next = $null
        if ($rev -gt 1 -and (Get-NamedIds @(p4 print -q "$file#$($rev - 1)" 2>$null)).ContainsKey($id)) { $next = "$file#$($rev - 1)" }
        else {
            $src = $log | Where-Object { $_ -match '^\.\.\. \.\.\. (copy|branch|merge|moved) from (//[^#\s]+)#(?:\d+,#)?(\d+)' } | Select-Object -First 1
            if ($src) {
                $null = $src -match 'from (//[^#\s]+)#(?:\d+,#)?(\d+)'
                $cand = "$($Matches[1])#$($Matches[2])"
                if ((Get-NamedIds @(p4 print -q $cand 2>$null)).ContainsKey($id)) { $next = $cand }
            }
        }
        if (-not $next) { break }
        $at = $next
    }
    $origin
}

$pairs = @()
if ($Change -gt 0) {
    $paths = @(p4 describe -s $Change 2>$null | Where-Object { $_ -match '^\.\.\. (//\S+-backlog\.md)#\d+ ' } | ForEach-Object { $Matches[1] })
    foreach ($p in $paths) {
        $pairs += [pscustomobject]@{ Name = $p; Now = @(p4 print -q "$p@=$Change" 2>$null); Before = @(p4 print -q "$p@$($Change - 1)" 2>$null); Origin = [int]::MaxValue }
    }
} elseif (-not $File) {
    foreach ($o in @(p4 -ztag -F '%depotFile%|%action%' opened 2>$null | Where-Object { $_ -match '-backlog\.md\|' })) {
        $depot, $action = $o -split '\|'
        $local = (p4 -ztag -F '%path%' where $depot 2>$null | Select-Object -First 1)
        if (-not $local -or -not (Test-Path -LiteralPath $local)) { continue }
        $before = if ($action -match 'add|branch') { @() } else { @(p4 print -q "$depot#have" 2>$null) }
        $pairs += [pscustomobject]@{ Name = $depot; Now = @(Get-Content -LiteralPath $local); Before = $before; Origin = [int]::MaxValue }
    }
}

$fresh = @()
$ungraded = 0
foreach ($pr in $pairs) {
    $was = Get-NamedIds $pr.Before
    foreach ($id in (Get-NamedIds $pr.Now).Keys) {
        if ($was.ContainsKey($id)) { continue }
        $origin = if ($Change -gt 0) { Get-RowOrigin $pr.Name $id $Change } else { $pr.Origin }
        $fresh += [pscustomobject]@{ Name = $pr.Name; Id = $id; Origin = $origin }
    }
    $numBefore = @($pr.Before | Where-Object { $_ -match $numeric } | ForEach-Object { $Matches[1] })
    $ungraded += @($pr.Now | Where-Object { $_ -match $numeric } | ForEach-Object { $Matches[1] } | Where-Object { $numBefore -notcontains $_ }).Count
}

$reused = 0
if ($fresh.Count -gt 0) {
    $cacheDir = Join-Path $Root 'build-output\check-backlog-ids'
    $cache = Join-Path $cacheDir 'cl-descriptions.txt'
    if (-not (Test-Path $cacheDir)) { New-Item -ItemType Directory -Path $cacheDir | Out-Null }
    $old = if (Test-Path $cache) { [IO.File]::ReadAllText($cache) } else { '' }
    $last = if ($old -match '^Change (\d+) on ') { [int]$Matches[1] } else { 0 }
    $range = if ($last -gt 0) { "//Codex/...@$($last + 1),@now" } else { '//Codex/...' }
    $new = (@(p4 changes -l -s submitted $range 2>$null) -join "`n")
    if ($new) { $old = $new + "`n" + $old; [IO.File]::WriteAllText($cache, $old) }
    $blocks = $old -split '(?m)^(?=Change \d+ on )'
    foreach ($f in $fresh) {
        $re = '(?<![A-Za-z0-9-])' + [regex]::Escape($f.Id) + '(?![0-9])'
        $hits = @($blocks | Where-Object { $_ -match '^Change (\d+) on ' -and [int]$Matches[1] -lt $f.Origin -and $_ -match $re } |
                  ForEach-Object { $null = $_ -match '^Change (\d+) on '; [int]$Matches[1] } | Sort-Object)
        if ($hits.Count -eq 0) { continue }
        $reused++
        Write-Host "REUSED ID  $($f.Name)  '$($f.Id)' is a new row, and $($hits.Count) earlier CL(s) already name it: $(($hits | Select-Object -First 8) -join ' ')"
    }
}

Write-Host ''
if ($dupes -gt 0) {
    Write-Host "check-backlog-ids: $dupes duplicate id(s) across $scanned register(s). An id is what a CL cites; two rows cannot share one."
}
if ($reused -gt 0) {
    Write-Host "check-backlog-ids: $reused new row(s) reuse an id an earlier CL already names. Take the next unused id; a citation to the old row would resolve to the new one."
}
if ($dupes -gt 0 -or $reused -gt 0) { exit 1 }
$note = if ($pairs.Count -gt 0) { ", $($fresh.Count) new named id(s) checked for reuse over $($pairs.Count) changed register(s), $ungraded new numeric id(s) not graded" } else { '' }
Write-Host "check-backlog-ids: $scanned register(s), no duplicate ids$note."
exit 0
