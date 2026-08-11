# Run the recheck sweep N-wide and merge the shards into one verdict.
#
# WHY THIS EXISTS. A full serial sweep of codex/test measured 4,790 s on
# 2026-08-06, which is a burden nobody schedules. The work is independent per
# chapter, so the only thing that ever made it serial was the port: the plug
# dials one compiled-in port (build/plug-ports.ps1) and codex-vm's NAT used to
# connect to the host on exactly the port the guest asked for, so two workers
# needed the same listener. codex-vm -natmap <guestdest>:<hostport> removed
# that, and each shard now owns a private host port while running the same
# unmodified plug binary.
#
# THE FLOOR IS THE LONGEST CHAPTER, not the total over N. 42 per cent of that
# 4,790 s sat in 8 of 428 chapters and the worst single one was ~450 s, so
# past about 8 workers this stops getting faster and only gets hotter. -Jobs 8
# is the fleet default for every other harness here and it is the right number
# for this one too.
#
#   pwsh codex/plugs/recheck/sweep-all.ps1                 # codex/test, 8-wide
#   pwsh codex/plugs/recheck/sweep-all.ps1 -Jobs 4
#   pwsh codex/plugs/recheck/sweep-all.ps1 -Dir codex/foreword/core
[CmdletBinding()]
param(
    [string]$Dir = 'codex/test',
    [int]$Limit = 0,
    [string]$Kernel = '',
    [string]$Passes = 'none',
    [int]$Jobs = 8,
    # 9250 upward is clear of the 9101-9145 plug block and of 9100, which
    # Accounts/WebServer own. NATMAP_MAX in codex-vm is 16.
    [int]$PortBase = 9250
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PlugDir = (Resolve-Path $PSScriptRoot).Path
$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$WorkDir = Join-Path $PlugDir 'build-output\sweep'
$PlugCdx = Join-Path $PlugDir 'build-output\recheck-plug.cdx'
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run codex/plugs/recheck/build.ps1 first")
    exit 2
}
if ($Jobs -lt 1 -or $Jobs -gt 16) { [Console]::Error.WriteLine("-Jobs must be 1..16 (NATMAP_MAX)"); exit 2 }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# Refuse every port up front rather than discovering the clash one shard in.
for ($k = 0; $k -lt $Jobs; $k++) {
    $p = $PortBase + $k
    try { $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $p); $l.Start(); $l.Stop() }
    catch { [Console]::Error.WriteLine("REFUSING: TCP $p is already held. Another run owns it."); exit 3 }
}

Get-ChildItem $WorkDir -Filter 'sweep.shard*.log' -ErrorAction SilentlyContinue | Remove-Item -Force

$started = Get-Date
Write-Host ("=== SWEEP-ALL: {0} shards over {1} ===" -f $Jobs, $Dir)

$procs = @()
for ($k = 0; $k -lt $Jobs; $k++) {
    $shardOut = Join-Path $WorkDir "sweep.shard$k.out"
    $a = @('-NoProfile', '-File', (Join-Path $PlugDir 'sweep.ps1'),
           '-Dir', $Dir, '-Limit', $Limit, '-Passes', $Passes,
           '-Shard', $k, '-Shards', $Jobs, '-HostPort', ($PortBase + $k))
    if ($Kernel) { $a += @('-Kernel', $Kernel) }
    $procs += Start-Process -FilePath 'pwsh' -ArgumentList $a -PassThru `
        -WindowStyle Hidden -RedirectStandardOutput $shardOut
}
Write-Host ("  launched {0} shards on ports {1}..{2}" -f $Jobs, $PortBase, ($PortBase + $Jobs - 1))
$procs | Wait-Process
$elapsed = ((Get-Date) - $started).TotalSeconds

# A dead shard used to be invisible: the merge below skips a missing log with
# `continue`, so eight shards that all exited 1 produced zero counts and the
# summary printed "no disagreements and nothing unsupported" over a run that
# checked nothing. Measured 2026-08-06 with -Dir codex/foreword, which holds
# its chapters in subdirectories while sweep.ps1 enumerates one level only:
# 8 dead shards, 1 s elapsed, exit 0. A sweep that cannot report failure is
# not evidence, which is the thing this lane exists to say about others.
$deadShards = @($procs | Where-Object { $_.ExitCode -ne 0 })
if ($deadShards.Count -gt 0) {
    [Console]::Error.WriteLine("FAILED: $($deadShards.Count) of $Jobs shards exited non-zero")
    foreach ($p in $deadShards) { [Console]::Error.WriteLine("  exit $($p.ExitCode)") }
    [Console]::Error.WriteLine("  shard stdout is in $WorkDir\sweep.shard*.out")
    exit 4
}

# Merge. Each shard wrote its own log; the counts are additive and the kinds
# are unioned, so the merged verdict is what a serial run would have printed.
$swept = 0; $skipped = 0; $excluded = 0; $plugDied = 0; $filesWithDisagree = 0
$totDefs = 0; $totAgree = 0; $totDis = 0; $totUns = 0
$kinds = @{}
$merged = [System.Collections.Generic.List[string]]::new()
for ($k = 0; $k -lt $Jobs; $k++) {
    $lg = Join-Path $WorkDir "sweep.shard$k.log"
    if (-not (Test-Path $lg)) { continue }
    foreach ($line in (Get-Content $lg)) {
        $merged.Add($line)
        if ($line -match '^=== ') { $swept++ }
        elseif ($line -match '^SKIP ') { $skipped++ }
        elseif ($line -match '^EXCLUDE ') { $excluded++ }
        elseif ($line -match '^PLUG-FAILED ') { $plugDied++ }
        elseif ($line -match '^\s+STAGE (\d) DEFS (\d+) AGREE (\d+) DISAGREE (\d+) UNSUPPORTED (\d+)') {
            if ($matches[1] -eq '1') { $totDefs += [int]$matches[2] }
            $totAgree += [int]$matches[3]; $totDis += [int]$matches[4]; $totUns += [int]$matches[5]
        }
        elseif ($line -match '^\s+(DISAGREE|UNSUPPORTED) (\S+) \[([^\]]+)\] (.*)$') {
            $key = "$($matches[1]) [$($matches[3])]"
            if (-not $kinds.ContainsKey($key)) { $kinds[$key] = [System.Collections.Generic.List[string]]::new() }
            $kinds[$key].Add("$($matches[2]): $($matches[4])")
        }
    }
}
# A chapter disagrees if any DISAGREE line names it.
$disagreeChapters = @{}
foreach ($line in $merged) {
    if ($line -match '^\s+DISAGREE (\S+) \[') { $disagreeChapters[$matches[1]] = $true }
}
$filesWithDisagree = $disagreeChapters.Count

$report = Join-Path $WorkDir 'sweep.log'
[System.IO.File]::WriteAllLines($report, $merged)

Write-Host ''
Write-Host '=== SWEEP (merged) ==='
Write-Host ("  dir                    : {0}  (passes={1}, shards={2})" -f $Dir, $Passes, $Jobs)
Write-Host ("  chapters swept         : {0}" -f $swept)
Write-Host ("  skipped, did not build : {0}" -f $skipped)
Write-Host ("  excluded by sidecar    : {0}" -f $excluded)
Write-Host ("  plug died on payload   : {0}" -f $plugDied)
if ($plugDied -gt 0) {
    Write-Host ("      NOT A FLAKE: the plug guest answers OUT OF MEMORY above ~1.13 MB of IR")
    Write-Host ("      and those {0} chapters are UNCHECKED, not clean. Largest answered" -f $plugDied)
    Write-Host ("      2026-08-06 was 1,128,053 bytes; every payload above it died.")
}
Write-Host ("  definitions (stage 1)  : {0}" -f $totDefs)
Write-Host ("  chapters disagreeing   : {0}" -f $filesWithDisagree)
Write-Host ("  verdicts across stages : AGREE {0}  DISAGREE {1}  UNSUPPORTED {2}" -f $totAgree, $totDis, $totUns)
Write-Host ("  elapsed                : {0:N0}s" -f $elapsed)
Write-Host ''
if ($kinds.Count -eq 0) {
    Write-Host '  no disagreements and nothing unsupported'
} else {
    # These are DETAIL LINES, not verdicts, and the two do not reconcile by
    # design: one definition emits one line per offending sub-pattern, and the
    # same definition is re-reported once per stage. The codex/test sweep of
    # 2026-08-06 read 34 stage-summed UNSUPPORTED against 70 detail lines and
    # was written up as a summary contradicting its own detail. Label held.
    Write-Host '=== BY KIND (detail lines; one definition can raise several) ==='
    foreach ($key in ($kinds.Keys | Sort-Object)) {
        Write-Host ("  {0,-40} {1}" -f $key, $kinds[$key].Count)
        foreach ($ex in ($kinds[$key] | Select-Object -First 3)) { Write-Host "      $ex" }
        if ($kinds[$key].Count -gt 3) { Write-Host ("      ... and {0} more" -f ($kinds[$key].Count - 3)) }
    }
}
Write-Host ''
Write-Host "  full log: $report"
Write-Host '  A disagreement here is a bug report against ONE of the two implementations'
Write-Host '  and is unresolved until a human reads it. Do not tune this quiet.'

if ($swept -eq 0) {
    Write-Host ''
    [Console]::Error.WriteLine("FAILED: 0 chapters swept. A sweep of nothing is not a clean sweep.")
    [Console]::Error.WriteLine("  sweep.ps1 enumerates ONE directory level. Point -Dir at a leaf")
    [Console]::Error.WriteLine("  that holds .codex files (codex/foreword/core, not codex/foreword).")
    exit 5
}
