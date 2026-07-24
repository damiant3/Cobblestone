# Something checks the LIR dump.
#
# codex/test/lir-check.lir-expected records one line per definition: the block
# structure, the live-in sets, the register assignment, and the verdicts of both
# the structural and the allocation verifier. Until this script existed NO
# HARNESS READ IT. Grepping the tree for the filename found three doc mentions
# and the test's own prose, and no runner -- so the file was a snapshot of one
# past run that went stale in silence, and the LIR's whole structural surface
# had no regression coverage at all.
#
# The correctness half was already covered: lir-check.expected is a real
# .expected the battery checks, and it pins what the functions COMPUTE. What was
# uncovered is what the LIR IS. Those are different questions, and the second is
# the one that hid the col-hue parallel-move miscompile through ten sessions of
# green benches and byte-identical fixed points.
#
# The dump rides the info channel as CDX4030 under `-Passes lir-dump`, so it
# lands in the compile log and the comparison is a string compare.
#
# The leading `+` is load-bearing. `-Passes lir-dump` REPLACES the pipeline;
# `-Passes +lir-dump` adds to the default. This harness used the replacing form
# for its whole existence and therefore pinned IR nobody compiles -- `math-mod`
# still a call, allocation done over a function with calls in it -- and the only
# symptom was a dump that disagreed with the emitted binary. The additive form
# tracks default-ir-pipeline (Passes.codex) automatically, so a pass added there
# is picked up here without an edit; the compile log's `PIPELINE ...` line names
# what actually ran, either way.
#
#   pwsh build/lir-dump-test.ps1                                  # against seed/Codex.cdx
#   pwsh build/lir-dump-test.ps1 -Kernel build/output/Sut.cdx     # against a fresh SUT
#   pwsh build/lir-dump-test.ps1 -Accept                          # re-record the snapshot
#
# -Accept records whatever the compiler just did, including whatever it did
# wrong. READ THE DIFF THIS PRINTS BEFORE YOU ACCEPT IT. A snapshot nobody
# looked at is a bug fixture, not a test.

param(
  [string]$Kernel = "seed/Codex.cdx",
  [switch]$Accept
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/lir-dump"
New-Item -ItemType Directory -Force $out | Out-Null

$src      = Join-Path $root "codex/test/lir-check.codex"
$pin      = Join-Path $root "codex/test/lir-check.lir-expected"
$cdx      = Join-Path $out "lir-check.cdx"
$log      = Join-Path $out "lir-check.log"
$compile  = Join-Path $root "build/compile.ps1"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

# The dump is only produced when the pipeline asks for it. A run that forgets
# the pass compiles perfectly and emits nothing, which would read as an empty
# actual -- caught by the floor assertion below, not by the diff.
Write-Host "[lir-dump] compiling lir-check with -Passes lir-dump against $Kernel ..."
pwsh $compile -Src $src -Out $cdx -Log $log -Kernel $kernelPath -Passes "+lir-dump" | Out-Null

if (-not (Test-Path $log)) {
  Write-Host "[lir-dump] FAIL: no compile log at $log" -ForegroundColor Red
  exit 1
}

$act = @(Get-Content $log |
  Where-Object { $_ -match '^info CDX4030: (LIR|LIVE|ALLOC) ' } |
  ForEach-Object { ($_ -replace '^info CDX4030: ', '') -replace "`r", "" })

# A test that cannot fail proves nothing. If the pass silently stops running,
# the extraction yields nothing -- and an empty file accepted once would make
# every later run agree with it forever. Demand a floor.
$floor = 60
if ($act.Count -lt $floor) {
  Write-Host "[lir-dump] FAIL: only $($act.Count) dump lines (floor $floor). The lir-dump pass did not run, or its diagnostic code changed." -ForegroundColor Red
  Write-Host "--- log head ---"
  Get-Content $log | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
  exit 1
}

# The verdicts are inside the lines the diff already compares, but a violation
# deserves to be named rather than buried in a snapshot delta. The entry-move
# simulation is a third verdict on the same line (`[entry: ...]`), and it says
# VIOLATION when the planned parameter moves do not leave every parameter where
# the assignment promised, so it is named here too.
$bad = @($act | Where-Object { ($_ -match 'violations|alloc-check: ' -and $_ -notmatch '\[(check|alloc-check): ok\]') -or ($_ -match 'VIOLATION') })
if ($bad.Count -gt 0) {
  Write-Host "[lir-dump] FAIL: a verifier reported a violation" -ForegroundColor Red
  $bad | ForEach-Object { Write-Host "  $_" }
  exit 1
}

if ($Accept) {
  $exp = if (Test-Path $pin) { @((Get-Content $pin) -replace "`r", "" | Where-Object { $_.Trim() -ne '' }) } else { @() }
  $delta = Compare-Object $exp $act -SyncWindow 0
  if ($delta) {
    Write-Host "[lir-dump] re-recording $($act.Count) lines. Changes being accepted:" -ForegroundColor Yellow
    $delta | ForEach-Object {
      $mark = if ($_.SideIndicator -eq '=>') { 'new' } else { 'was' }
      Write-Host "  $mark $($_.InputObject)"
    }
  } else {
    Write-Host "[lir-dump] re-recording $($act.Count) lines (no change)." -ForegroundColor Yellow
  }
  Set-Content -Path $pin -Value $act
  Write-Host "[lir-dump] recorded $pin -- p4 edit it before submitting."
  exit 0
}

if (-not (Test-Path $pin)) {
  Write-Host "[lir-dump] FAIL: no pin at $pin (run with -Accept to record one)" -ForegroundColor Red
  exit 1
}

$exp = @((Get-Content $pin) -replace "`r", "" | Where-Object { $_.Trim() -ne '' })
$delta = Compare-Object $exp $act -SyncWindow 0

if ($delta) {
  Write-Host "[lir-dump] FAIL: the LIR dump does not match codex/test/lir-check.lir-expected" -ForegroundColor Red
  Write-Host "           $($exp.Count) pinned, $($act.Count) emitted"
  Write-Host "--- expected (<=) / emitted (=>) ---"
  $delta | ForEach-Object { Write-Host "  $($_.SideIndicator) $($_.InputObject)" }
  Write-Host ""
  Write-Host "A block-structure, liveness or allocation change is a real change to what the"
  Write-Host "compiler emits. If it is intended, read every line above, then re-record with:"
  Write-Host "  pwsh build/lir-dump-test.ps1 -Accept"
  exit 1
}

Write-Host "[lir-dump] PASS: $($act.Count) lines match the pin, all verifiers ok" -ForegroundColor Green
exit 0
