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
#   LEAP arm     runs the whole-compiler -IrCce emit, the real overrun that
#                blocks C1. Must report OUT OF MEMORY and not an !EXC. ~30 s.
#                This is a GUARD PAGE arm: it fails against a seed without the
#                page and passes with it, ceiling test or not.
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
  param([string]$Tag, [long]$Park)

  $armSrc = $src -replace 'park-bytes', "$Park"
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
  if (-not $proc.WaitForExit(45000)) {
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

# The arm the page CANNOT catch, driven through the real compiler because
# `build` lives in the compiler's own unit and no app can cite it. An inflated
# -Decks scales every phase floor, so one deck reservation is a single advance
# of gigabytes and steps clean over the 2 MB hole -- which is exactly how the
# whole-compiler -IrCce frontier got above the guard unnoticed on 2026-08-04.
# It is caught by the ceiling test inside `build` and reports through the same
# __out_of_memory path, so the expected output matches the FIRE arm.
#
# The subject is the whole-compiler -IrCce emit, because it is the only case
# measured to distinguish the fix. An inflated -Decks was tried as a cheaper
# arm and REJECTED: at -Decks 450 the reservation is so oversized that R10
# lands above RSP immediately and the pre-existing prologue `cmp rsp, r10`
# catches it, so that arm prints OUT OF MEMORY against a compiler WITHOUT the
# ceiling test and measures the wrong guard. Measured 2026-08-04; do not
# reinstate it because it is faster.
#
# What this arm discriminates is the GUARD PAGE, not `build`'s ceiling test.
# Ablated 2026-08-04: seed#586 (no page) dies `!EXC=0d` in __str_concat+0xF9;
# seed#587 (page, no ceiling test) prints OUT OF MEMORY in 29 s. No measurement
# separates the ceiling test from the page, so do not read a pass here as
# evidence for it.
$leapSrc = Join-Path $out 'compiler-unit.codex'
$leapLog = Join-Path $out 'leap.log'
Write-Host "guard-page-test: LEAP arm (whole-compiler -IrCce, expect OUT OF MEMORY not !EXC)"
& (Join-Path $root 'build/concat-codex-self.ps1') -CodexDir codex/compiler -OutFile $leapSrc | Out-Null
& (Join-Path $root 'build/compile.ps1') -Src $leapSrc -Out (Join-Path $out 'leap.ir') `
  -Log $leapLog -Kernel $Kernel -IrCce 2>&1 | Out-Null
$leap = if (Test-Path $leapLog) { Get-Content $leapLog -Raw } else { '' }
if ($leap -match 'OUT OF MEMORY') {
  Write-Host "  ok: the overrun was refused instead of corrupting the stack"
} elseif ($leap -match 'EXC=' -or $leap -match 'CRASH in ') {
  Write-Host "  FAIL: the overrun crossed the guard page and corrupted the stack"
  ($leap -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 4) | ForEach-Object { "    $_" }
  $failures++
} else {
  Write-Host "  FAIL: the emit neither refused nor crashed -- read $leapLog"
  ($leap -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 4) | ForEach-Object { "    $_" }
  $failures++
}

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
Write-Host "guard-page-test: PASS (a probe overrun and the real whole-compiler overrun both refused, healthy input untouched)"
exit 0
