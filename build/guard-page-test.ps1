# The heap guard page must fire, and must not fire on healthy input.
#
# 58 inline `add r10, ...` bumps across eight emitter files advance the heap
# frontier, and none of them bounds-checks. A chokepoint was measured not to
# work (CL 12759 guarded `__alloc`, which `__str_concat` does not call, and the
# guarded and unguarded arms crashed identically). The guard page replaces that:
# one unmapped 2 MB page below the boot stack's reserve, so any bump that lands
# in it faults whichever site did it, at zero cost on the allocation path.
#
# This harness is the only thing that can see it. `build/build.ps1` cannot: the
# compiler's own peak frontier is ~1245 MB against a guard page at ~2974 MB, so
# a green gate looks identical whether the page is there or not.
#
#   FIRE arm     parks the frontier just below the guard page, then walks up
#                into it in ~40-byte steps. Caught by the PAGE.
# A LEAP arm ran the whole-compiler -IrCce emit here until 2026-08-15, on the
# premise that it overruns the guard. It does not any more: that emit completes,
# writing 15.7 MB of IR at a peak frontier of 1,305,881,760 bytes, which is the
# ~1245 MB the paragraph above already reports, 1.7 GB below the page. So the
# arm reported FAILED on a healthy tree. It is NOT coming back by lowering
# -MemMB to bring the guard down to the workload: at 1280 MB the compiler
# legitimately needs more memory than it has, so the trip is correct behaviour
# rather than a runaway being caught, and the arm cannot tell those apart --
# the same defect that got the -Decks 450 arm rejected on 2026-08-04.
#
# The WALK arm (2026-08-20) is the replacement that closes what LEAP's
# retirement opened: build/guard-page-walk.codex allocates for real -- one
# ~256 KB __str_concat per step, far under the 2 MB page so it cannot step
# over the hole -- from the heap base until the page catches it, with a fuel
# cap (~6 GB) whose exhaustion prints WALK COMPLETED and fails the arm. It is
# a runaway by construction, so unlike LEAP its subject cannot drift away as
# the compiler gets cheaper, and the arm checks the HEAP= address the trip
# reports lands INSIDE the page: an OUT OF MEMORY from anywhere else is a
# ceiling firing early, not the guard, and is a failure.
#
# NOTE ON WHAT TO TEST AGAINST. An emitter change (emit-demand-unmap,
# emit-pagefault-handler) does NOT appear in build-output/bare-metal/Codex.cdx:
# that binary's boot code was emitted by the SEED. It appears in stage1 and in
# the next seed. Pointing this script at a freshly built SUT tests the probe
# binaries it compiles, not the kernel's own boot code, and that mistake
# produced a published finding in the wrong direction on 2026-08-04.
#   CONTROL arm  parks far below and burns the same amount. Must print SURVIVED
#                and must NOT print OUT OF MEMORY -- it is what stops a `build`
#                that refuses everything, or a page that faults on everything,
#                from passing.
#
# THE CONTROL PARK IS A CONSTANT AND -MemMB IS NOT, so the two disagree below
# about 2.5 GB. Its park is a hardcoded 2,000,000,000 while the guard address is
# derived from reported RAM, so at -MemMB 1280 the guard sits at 1,272,971,264
# and the control parks 727 MB PAST it: the arm reports
# "the control neither survived nor reported OOM" and the run goes red for a
# reason that has nothing to do with the guard. Run this at the default memory.
# (Useful for exactly one thing: it is the cheapest way to show the harness can
# still report a failure at all.)
#
# The control is not decoration. Without it this script passes just as well
# against a compiler that answers OUT OF MEMORY to everything, which is the
# classic instrument that cannot fail.
#
#   pwsh build/guard-page-test.ps1                                  # vs seed
#   pwsh build/guard-page-test.ps1 -Kernel build-output/bare-metal/Codex.cdx

param(
  [string]$Kernel = "seed/Codex.cdx",
  [int]$MemMB = 3072
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $root 'build/vm-config.ps1')

$out = Join-Path $root 'build-output/guard-page'
New-Item -ItemType Directory -Force $out | Out-Null

# codex-vm caps the RAM size it REPORTS to the guest at 3040 MB so the boot
# stack cannot land in the GPU and GOP windows, which sit at fixed GPAs. The
# guest computes the guard page from the REPORTED size, so this must too --
# deriving it from -mem would put the expected address 32 MB too high.
$reportedMB = [Math]::Min($MemMB, 3040)
$ram = $reportedMB * 1MB
$pages = [Math]::Floor($ram / 2097152)
$guardIdx = $pages - 32 - 1
$guardAddr = $guardIdx * 2097152

Write-Host "guard-page-test: reported RAM ${reportedMB} MB, guard page at $guardAddr ($([Math]::Round($guardAddr/1MB,0)) MB), index $guardIdx"

# The probe lives in build/ rather than under codex/test/ because it is a
# TEMPLATE, not a test: it says `park-bytes` where an integer belongs and does
# not compile until this script substitutes one. Under codex/test/ it would be
# enumerated as a test and fail as a broken one.
$src = Get-Content (Join-Path $root 'build/guard-page-probe.codex') -Raw

function Invoke-Arm {
  param([string]$Tag, [long]$Park, [int]$WaitMs = 45000, [string]$SourcePath = '')

  $armSrc = if ($SourcePath) { Get-Content $SourcePath -Raw } else { $src -replace 'park-bytes', "$Park" }
  $srcFile = Join-Path $out "$Tag.codex"
  [System.IO.File]::WriteAllText($srcFile, $armSrc, [System.Text.UTF8Encoding]::new($false))

  $bin = Join-Path $out "$Tag.cdx"
  $log = Join-Path $out "$Tag.log"
  if (Test-Path $bin) { Remove-Item $bin -Force }
  & (Join-Path $root 'build/compile.ps1') -Src $srcFile -Out $bin -Log $log -Kernel $Kernel 2>&1 | Out-Null
  if (-not (Test-Path $bin)) {
    Write-Host "  FAIL: $Tag did not compile"
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -First 5 | ForEach-Object { "    $_" }
    return $null
  }

  # Run codex-vm directly rather than through test-run.ps1: a run that
  # legitimately trips the guard ends in `cli; hlt; jmp -6` and never exits, and
  # test-run.ps1 discards the output of anything that hits its wall budget --
  # which is exactly the output this test is about. Kill it and read what it
  # printed before it halted.
  $outFile = Join-Path $out "$Tag.raw"
  $errFile = Join-Path $out "$Tag.err"
  if (Test-Path $outFile) { Remove-Item $outFile -Force }
  $vmArgs = @('-kernel', $bin, '-output', $outFile, '-mem', "$MemMB", '-headless')
  $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
  if (-not $proc.WaitForExit($WaitMs)) {
    Stop-VmGraceful -ProcessId $proc.Id
    Start-Sleep -Milliseconds 300
  }
  if (-not (Test-Path $outFile)) { return '' }
  return ([System.IO.File]::ReadAllText($outFile) -replace "`r", '' -replace "^\x01", '')
}

$failures = 0

# Park just under the guard page. The heap base is 6 MB and the probe itself
# allocates a little before this runs, so the landing point is approximate --
# it does not need to be exact, because the arm then walks ~4 MB upward and the
# hole is only 2 MB wide, so it cannot be stepped over from below.
$firePark = $guardAddr - 6291456 - 65536
Write-Host "guard-page-test: FIRE arm (park $firePark, expect OUT OF MEMORY)"
$fire = Invoke-Arm -Tag 'fire' -Park $firePark
if ($null -ne $fire) {
  if ($fire -match 'OUT OF MEMORY') {
    Write-Host "  ok: the guard page fired"
  } else {
    Write-Host "  FAIL: no OUT OF MEMORY -- the frontier crossed the guard page unnoticed"
    ($fire -split "`n" | Select-Object -First 6) | ForEach-Object { "    $_" }
    $failures++
  }
  if ($fire -match 'SURVIVED') {
    Write-Host "  FAIL: the arm ran to completion, so it never reached the guard page"
    $failures++
  }
} else { $failures++ }

# The walk starts at the heap base and buys what FIRE cannot: the trip comes at
# the end of a ~2.9 GB march of real allocations, so the allocation path, the
# demand mapping under it, and the guard have all been exercised together.
Write-Host "guard-page-test: WALK arm (genuine allocation walk from the heap base, expect OUT OF MEMORY inside the page)"
$walk = Invoke-Arm -Tag 'walk' -Park 0 -WaitMs 120000 -SourcePath (Join-Path $root 'build/guard-page-walk.codex')
if ($null -ne $walk) {
  if ($walk -match 'WALK COMPLETED') {
    Write-Host "  FAIL: the walk ran to completion -- the frontier crossed the guard page unnoticed"
    $failures++
  } elseif ($walk -match 'OUT OF MEMORY') {
    if ($walk -match 'HEAP=([0-9a-fA-F]+)') {
      $heapAt = [Convert]::ToInt64($matches[1], 16)
      if ($heapAt -ge $guardAddr -and $heapAt -lt ($guardAddr + 4194304)) {
        Write-Host "  ok: the walk was caught inside the page (HEAP=$heapAt, guard at $guardAddr)"
      } else {
        Write-Host "  FAIL: OUT OF MEMORY fired away from the page (HEAP=$heapAt, guard at $guardAddr) -- a ceiling, not the guard"
        $failures++
      }
    } else {
      Write-Host "  FAIL: OUT OF MEMORY without a HEAP= line, so the trip cannot be located"
      $failures++
    }
  } else {
    Write-Host "  FAIL: the walk neither completed nor reported OOM"
    ($walk -split "`n" | Select-Object -First 6) | ForEach-Object { "    $_" }
    $failures++
  }
} else { $failures++ }

# The 2026-08-04 ablation is kept because it is the evidence the page works at
# all, and it is not reproducible from the arms that remain: seed#586 (no page)
# dies `!EXC=0d` in __str_concat+0xF9; seed#587 (page, no ceiling test) prints
# OUT OF MEMORY in 29 s. No measurement separates the ceiling test from the
# page, so do not read a FIRE pass as evidence for the ceiling test.

# The control is what stops a `build` that refuses everything from passing, and
# the shipping floors are exercised by every other compile in this script.
Write-Host "guard-page-test: CONTROL arm (park 2000000000, expect SURVIVED)"
$ctl = Invoke-Arm -Tag 'control' -Park 2000000000
if ($null -ne $ctl) {
  if ($ctl -match 'OUT OF MEMORY') {
    Write-Host "  FAIL: the guard fired on healthy input -- it is not a guard, it is a ceiling"
    ($ctl -split "`n" | Select-Object -First 6) | ForEach-Object { "    $_" }
    $failures++
  } elseif ($ctl -match 'SURVIVED') {
    Write-Host "  ok: healthy input ran to completion"
  } else {
    Write-Host "  FAIL: the control neither survived nor reported OOM"
    ($ctl -split "`n" | Select-Object -First 6) | ForEach-Object { "    $_" }
    $failures++
  }
} else { $failures++ }

if ($failures -gt 0) {
  Write-Host ""
  Write-Host "guard-page-test: FAILED ($failures)"
  exit 1
}
Write-Host ""
Write-Host "guard-page-test: PASS (a parked overrun refused, a genuine allocation walk caught inside the page, healthy input untouched)"
exit 0
