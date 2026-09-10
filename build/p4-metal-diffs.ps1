# p4-metal-diffs.ps1 -- what a file said about metal, revision by revision.
#
# Hand-written. There is no generator for this file.
#
#   pwsh build/p4-metal-diffs.ps1
#   pwsh build/p4-metal-diffs.ps1 -File //Codex/main/CLAUDE.md -Out claude.md
#   pwsh build/p4-metal-diffs.ps1 -Pattern 'sitting|stick' -MaxRev 50
#
# WHY THIS EXISTS. A register written under R-HISTORY replaces each row in
# place, therefore the current revision holds no history of itself and
# `p4 annotate` over it attributes every surviving line to the most recent
# rewrite. The history is real and is in Perforce, one revision at a time.
# This script walks the revisions and keeps only the lines that were ADDED or
# DELETED and that match the subject pattern, so that a reader sees when a
# claim entered the register and when the claim left it.
#
# WHAT IT CANNOT DO, stated here because a checker's limits rot faster than
# its code:
#
# 1. A line that was reworded rather than added reports as one deletion and
#    one addition, and nothing here pairs the two. A reader counting
#    "additions" is counting edits, not new claims.
# 2. The pattern decides everything. A row about the board that never uses a
#    matched word is invisible, and no count here is a count of "rows about
#    metal" (L-CENSUS: a census finds the spelling you searched for).
# 3. The diff is line-based, so a reflowed paragraph reports every line of
#    the paragraph as changed even where the sentence did not change.
# 4. Attribution is the CLIENT that submitted the revision, which names the
#    lane, not the author of the words. Damian's words reach the register
#    through a lane's changelist.

[CmdletBinding()]
param(
    [string]$File = '//Codex/main/docs/PM/CurrentPlan.md',
    [string]$Out = '',
    [string]$Pattern = 'sitting|stick|metal|ASUS|flight|board|bed|flash|rehears',
    [int]$MaxRev = 0,
    [int]$FirstRev = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

Write-Host "p4-metal-diffs: $File"
Write-Host "p4-metal-diffs: pattern /$Pattern/"

# Revision map: revision number -> changelist, date, submitting client.
$log = & p4 filelog $File 2>&1
$revs = @{}
foreach ($line in $log) {
    if ($line -match '^\.\.\. #(\d+) change (\d+) \S+ on (\d{4}/\d\d/\d\d) by (\S+)') {
        $revs[[int]$Matches[1]] = [pscustomobject]@{
            Rev    = [int]$Matches[1]
            CL     = [int]$Matches[2]
            Date   = $Matches[3] -replace '/', '-'
            Client = ($Matches[4] -replace '^[^@]*@', '')
        }
    }
}
if ($revs.Count -eq 0) { throw "p4-metal-diffs: no revisions parsed for $File" }

$top = ($revs.Keys | Measure-Object -Maximum).Maximum
if ($MaxRev -gt 0 -and $MaxRev -lt $top) { $top = $MaxRev }
Write-Host "p4-metal-diffs: $($revs.Count) revisions, walking #$FirstRev to #$top"

$rows = New-Object System.Collections.Generic.List[object]
$scanned = 0

# Revision 1 has no predecessor: every matching line in it is an addition.
if ($FirstRev -le 1 -and $revs.ContainsKey(1)) {
    $r = $revs[1]
    foreach ($t in (& p4 print -q "$File#1" 2>&1)) {
        if ($t -match $Pattern) {
            $rows.Add([pscustomobject]@{ Rev = 1; CL = $r.CL; Date = $r.Date; Client = $r.Client; Sign = '+'; Text = $t })
        }
    }
    $scanned++
}

$start = [Math]::Max(2, $FirstRev)
for ($n = $start; $n -le $top; $n++) {
    if (-not $revs.ContainsKey($n) -or -not $revs.ContainsKey($n - 1)) { continue }
    $r = $revs[$n]
    $diff = & p4 diff2 -u "$File#$($n-1)" "$File#$n" 2>&1
    foreach ($line in $diff) {
        $s = [string]$line
        if ($s.Length -eq 0) { continue }
        $c = $s.Substring(0, 1)
        if ($c -ne '+' -and $c -ne '-') { continue }
        if ($s -match '^(\+\+\+|---)') { continue }
        $text = $s.Substring(1)
        if ($text -notmatch $Pattern) { continue }
        $rows.Add([pscustomobject]@{ Rev = $n; CL = $r.CL; Date = $r.Date; Client = $r.Client; Sign = $c; Text = $text })
    }
    $scanned++
    if ($scanned % 100 -eq 0) { Write-Host "p4-metal-diffs: $scanned revisions, $($rows.Count) matching lines" }
}

$added = ($rows | Where-Object { $_.Sign -eq '+' }).Count
$deleted = ($rows | Where-Object { $_.Sign -eq '-' }).Count
$touching = ($rows | Select-Object -ExpandProperty CL -Unique).Count
Write-Host "p4-metal-diffs: $scanned revisions scanned"
Write-Host "p4-metal-diffs: $($rows.Count) matching lines, $added added, $deleted deleted, over $touching changelists"

if ($Out) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("| rev | CL | date | lane | +/- | line |")
    [void]$sb.AppendLine("|---|---|---|---|---|---|")
    foreach ($x in $rows) {
        $t = ($x.Text -replace '\|', '/') -replace '[—–]', '--'
        if ($t.Length -gt 150) { $t = $t.Substring(0, 150) }
        [void]$sb.AppendLine("| $($x.Rev) | $($x.CL) | $($x.Date) | $($x.Client) | $($x.Sign) | $t |")
    }
    [System.IO.File]::WriteAllText((Join-Path $Repo $Out), $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "p4-metal-diffs: wrote $Out"
}
