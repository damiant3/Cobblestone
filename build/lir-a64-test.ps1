# Something reads the second-target probe.
#
# The LIR retarget step 2 runs the allocator and both verifiers over the
# lir-check corpus under two deliberately non-x86 register files
# (IR/LirTargets.codex) and emits nothing. That is the evidence for the claim
# that "a new target is a table entry", and until this script existed NOTHING
# READ IT -- reachable only by hand, which is exactly how lir-check.lir-expected
# came to sit unread for months (ExaminersAssay.md, "The LIR Dump Pin").
#
# What this pins that build/lir-dump-test.ps1 does not: that pin is the x86
# assignment, and it moves on any allocator change. This one is the same
# allocator over TWO other register files, so a change that is x86-specific by
# accident shows up here as a divergence between the targets rather than as a
# uniform re-record.
#
#   pwsh build/lir-a64-test.ps1                                # against seed/Codex.cdx
#   pwsh build/lir-a64-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT
#   pwsh build/lir-a64-test.ps1 -Accept                        # re-record the pin
#
# The leading `+` is load-bearing: `-Passes lir-dump-a64` REPLACES the pipeline
# and would pin un-inlined IR, a program nobody compiles; `-Passes +lir-dump-a64`
# adds to the default and tracks default-ir-pipeline (Passes.codex) without an
# edit here. The compile log's `PIPELINE ...` line names what actually ran.
#
# -Accept records whatever the compiler just did, including whatever it did
# wrong. READ THE DIFF THIS PRINTS BEFORE YOU ACCEPT IT.

param(
  [string]$Kernel = "seed/Codex.cdx",
  [switch]$Accept
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/lir-a64"
New-Item -ItemType Directory -Force $out | Out-Null

$src      = Join-Path $root "codex/test/lir-check.codex"
$pin      = Join-Path $root "codex/test/lir-check.a64-expected"
$cdx      = Join-Path $out "lir-check.cdx"
$log      = Join-Path $out "lir-check.log"
$compile  = Join-Path $root "build/compile.ps1"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

Write-Host "[lir-a64] compiling lir-check with -Passes lir-dump-a64 against $Kernel ..."
pwsh $compile -Src $src -Out $cdx -Log $log -Kernel $kernelPath -Passes "+lir-dump-a64" | Out-Null

if (-not (Test-Path $log)) {
  Write-Host "[lir-a64] FAIL: no compile log at $log" -ForegroundColor Red
  exit 1
}

$act = @(Get-Content $log |
  Where-Object { $_ -match '^info CDX4030: (A64|NARROW) ALLOC ' } |
  ForEach-Object { ($_ -replace '^info CDX4030: ', '') -replace "`r", "" })

# A test that cannot fail proves nothing. Both descriptors must report, so the
# floor is per-target: a run that silently lost one of them would otherwise
# halve the output and still look like a pin.
$a64 = @($act | Where-Object { $_ -match '^A64 ' })
$narrow = @($act | Where-Object { $_ -match '^NARROW ' })
$floor = 20
if ($a64.Count -lt $floor -or $narrow.Count -lt $floor) {
  Write-Host "[lir-a64] FAIL: A64 $($a64.Count) lines, NARROW $($narrow.Count) lines (floor $floor each)." -ForegroundColor Red
  Write-Host "          The lir-dump-a64 pass did not run, or one descriptor stopped reporting."
  Get-Content $log | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
  exit 1
}

# The whole point of a second target is that its verifiers stay clean. A
# violation here is the retarget claim failing, and it
# deserves to be named rather than buried in a snapshot delta.
#
# Three verdicts ride each line now, not two: the structural check, the
# allocation check, and the entry-move simulation, which sits between them as
# `[entry: ...]` and says VIOLATION when the planned moves do not leave every
# parameter where the assignment promised. That one caught a real defect on its
# first run -- the planner assumed parameter i arrives in argument register i,
# which is false once a function has more parameters than the target has
# argument registers -- so it is asserted here rather than left to the snapshot.
$bad = @($act | Where-Object { ($_ -notmatch '\[alloc-check: ok\]') -or ($_ -notmatch '\[check: ok\]$') -or ($_ -match 'VIOLATION') })
if ($bad.Count -gt 0) {
  Write-Host "[lir-a64] FAIL: a verifier rejected a non-x86 register file" -ForegroundColor Red
  $bad | ForEach-Object { Write-Host "  $_" }
  exit 1
}

# The narrow descriptor exists to reach the crossing arm, Belady eviction and
# the spill path, which the wide one never does -- nineteen registers and
# nothing in the corpus spills. A narrow run with no spill slot is not
# exercising what it was built for, whatever else it agrees with.
$spills = @($narrow | Where-Object { $_ -match '->s\d' })
if ($spills.Count -lt 1) {
  Write-Host "[lir-a64] FAIL: the NARROW descriptor produced no spill slots." -ForegroundColor Red
  Write-Host "          It is sized to force them; if it stopped, it is testing the wide case twice."
  exit 1
}

if ($Accept) {
  $exp = @()
  if (Test-Path $pin) { $exp = @((Get-Content $pin) -replace "`r", "" | Where-Object { $_.Trim() -ne '' }) }
  $delta = Compare-Object -ReferenceObject $exp -DifferenceObject $act -SyncWindow 0
  if ($delta) {
    Write-Host "[lir-a64] re-recording $($act.Count) lines. Changes being accepted:" -ForegroundColor Yellow
    $delta | ForEach-Object {
      $mark = if ($_.SideIndicator -eq '=>') { 'new' } else { 'was' }
      Write-Host "  $mark $($_.InputObject)"
    }
  } else {
    Write-Host "[lir-a64] re-recording $($act.Count) lines (no change)." -ForegroundColor Yellow
  }
  Set-Content -Path $pin -Value $act
  Write-Host "[lir-a64] recorded $pin -- p4 add/edit it before submitting."
  exit 0
}

if (-not (Test-Path $pin)) {
  Write-Host "[lir-a64] FAIL: no pin at $pin (run with -Accept to record one)" -ForegroundColor Red
  exit 1
}

$exp = @((Get-Content $pin) -replace "`r", "" | Where-Object { $_.Trim() -ne '' })
$delta = Compare-Object $exp $act -SyncWindow 0

if ($delta) {
  Write-Host "[lir-a64] FAIL: the second-target allocation does not match codex/test/lir-check.a64-expected" -ForegroundColor Red
  Write-Host "          $($exp.Count) pinned, $($act.Count) emitted"
  Write-Host "--- expected (<=) / emitted (=>) ---"
  $delta | ForEach-Object { Write-Host "  $($_.SideIndicator) $($_.InputObject)" }
  Write-Host ""
  Write-Host "If BOTH targets moved together this is an ordinary allocator change. If only"
  Write-Host "ONE moved, the allocator has become target-sensitive and that is the bug this"
  Write-Host "pin exists to catch. Read every line, then re-record with:"
  Write-Host "  pwsh build/lir-a64-test.ps1 -Accept"
  exit 1
}

Write-Host "[lir-a64] PASS: $($act.Count) lines match the pin ($($a64.Count) A64, $($narrow.Count) NARROW), all verifiers ok" -ForegroundColor Green
exit 0
