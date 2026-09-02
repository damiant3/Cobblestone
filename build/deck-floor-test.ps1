# A starved deck floor says so instead of trampling the bivy.
#
# A phase reserves its deck with `build`, and its deck-record allocations grow
# UP through that reservation while its ordinary allocations grow up through
# the bivy immediately above it. Nothing bounded the first against the second.
# Past the reservation the deck grew into the bivy, and the phase read its own
# overwrite one call later: `compile.ps1 -Decks 5` on the compiler's own source
# died in a #GP inside __linked_list_to_list with no diagnostic at all. The
# phase's own check-deck-overflow runs at phase-measure, which is after the
# damage and after the crash, so it never arrived.
#
# LEX is the phase starving reaches first, measured rather than assumed: at 5
# per cent the parse scratch fits (12.6 MB used against a 19.2 MB floor) and
# the LEX deck does not (28.7 MB against 4.8 MB). tokenize-collect now reads
# __deck-pos every token and stops a guard band short of the ceiling with
# CDX9002.
#
# This harness walks the whole reporting range:
#
#   -Decks 5    must FAIL with CDX9002 and no crash    (LEX, fires early)
#   -Decks 20   must FAIL with CDX9002 and no crash    (LEX, fires LATE)
#   -Decks 40   must FAIL with CDX9002 and no crash    (DESUGAR)
#   -Decks 100  must SUCCEED                           (the guard is not noise)
#
# 20 is not decoration. The first cut of the LEX guard passed at 5 and crashed
# at 20, because firing is not the whole job: the wind-down converted the
# tokens collected so far onto the deck that had just overflowed, and a late
# fire has far more of them than the flat guard band covers. A guard tested
# only where it fires early is a guard tested only where the bug cannot reach.
#
# 40 is the band that used to corrupt silently. Measured 2026-07-21 with the
# phase trace on, -Decks 30, 40, 50 and 60 all completed LEX and PARSE and
# then died inside DESUGAR, in three different shared runtime helpers -- so
# the crash site named nothing and the phase trace is what identified it.
# DESUGAR now stops per definition like LEX does.
#
# The knob is monotonic, measured 2026-07-21 across 5..100: LEX reports at 5
# and 20, DESUGAR at 30-60, LOWER at 85-93, and 94 upward compile. It was NOT
# monotonic until check-deck-overflow stopped reading its operands out of a
# PhaseMetrics that the overflowing phase had already destroyed. If an
# inversion ever reappears here, suspect the instrument before
# the arithmetic.
#
# The legs below stay at the two ends and one middle. Pinning the LOWER
# threshold itself would pin 1.06x of headroom that legitimately moves as the
# compiler grows, and this harness is about whether a starved floor SPEAKS,
# not about where the floor happens to sit this month.
#
#   pwsh build/deck-floor-test.ps1                              # against seed/Codex.cdx
#   pwsh build/deck-floor-test.ps1 -Kernel build/output/Sut.cdx # against a fresh SUT

param(
  [string]$Kernel = "seed/Codex.cdx"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root 'build-output/deck-floor'
New-Item -ItemType Directory -Force $out | Out-Null

$src = Join-Path $root 'build/output/Codex.codex'
Write-Host "deck-floor-test: concatenating the compiler source"
& (Join-Path $root 'build/concat-codex-self.ps1') -CodexDir codex/compiler -OutFile $src | Out-Null
if (-not (Test-Path $src)) { throw "deck-floor-test: no concatenated source at $src" }

$failures = 0

$script:LegBinary = ''

function Invoke-Leg {
  param([int]$Decks, [string]$Tag)
  $log = Join-Path $out "$Tag.log"
  $bin = Join-Path $out "$Tag.cdx"
  if (Test-Path $bin) { Remove-Item $bin -Force }
  & (Join-Path $root 'build/compile.ps1') -Src $src -Out $bin -Log $log -Kernel $Kernel -Decks $Decks 2>&1 | Out-Null
  if (-not (Test-Path $log)) { throw "deck-floor-test: no log at $log" }
  $script:LegBinary = $bin
  return (Get-Content $log -Raw)
}

# The starved floor must name ITSELF, not merely complain. Each leg asserts
# which phase reported, because "some phase raised CDX9002" would pass just as
# well if the wrong guard fired first -- and at these percentages more than one
# floor is genuinely short.
# The middle leg moved from 40 to 60 on 2026-09-01: the token list is now a
# flat list with capacity on the LEX deck (40 MB on the compiler's own unit
# against 32 before, with 45 MB of bivy gone), so at 40 per cent LEX's 38 MB
# floor is the first to starve and the leg no longer reaches DESUGAR. At 60
# LEX has 57 MB, DESUGAR 46 against its 49, and DESUGAR speaks first.
foreach ($leg in @(@{ Decks = 5; Phase = 'LEX' }, @{ Decks = 20; Phase = 'LEX' }, @{ Decks = 60; Phase = 'DESUGAR' })) {
  $d = $leg.Decks
  $want = $leg.Phase
  Write-Host "deck-floor-test: -Decks $d (expect CDX9002 from $want)"
  $starved = Invoke-Leg -Decks $d -Tag "decks$d"
  if ($starved -match 'Deck overflow in (\w+)') {
    $got = $Matches[1]
    if ($got -eq $want) {
      Write-Host "  ok: CDX9002 reported by $got"
    } else {
      Write-Host "  FAIL: CDX9002 came from $got, expected $want"
      $failures++
    }
  } else {
    Write-Host "  FAIL: no CDX9002 in the starved compile"
    $failures++
  }
  if ($starved -match '!EXC=' -or $starved -match 'CRASH in ') {
    Write-Host "  FAIL: the starved compile still crashed -- the guard did not get there first"
    ($starved -split "`n" | Select-String -Pattern '!EXC=|CRASH in ' | Select-Object -First 3) | ForEach-Object { "    $_" }
    $failures++
  } else {
    Write-Host "  ok: no crash"
  }
}

# The last leg -- and the guard must not fire on a floor that is fine. Without
# it the script passes just as well against a compiler that refuses every
# compile, which is the classic function that always answers the same thing.
Write-Host "deck-floor-test: -Decks 100 (expect a clean compile)"
$normal = Invoke-Leg -Decks 100 -Tag 'decks100'
$normalBin = $script:LegBinary
$normalLen = if (Test-Path $normalBin) { (Get-Item $normalBin).Length } else { 0 }
if ($normal -match 'CDX9002') {
  Write-Host "  FAIL: CDX9002 at the shipping floors -- the guard is firing on healthy input"
  $failures++
} elseif ($normal -match 'error CDX') {
  Write-Host "  FAIL: the normal compile reported errors"
  ($normal -split "`n" | Select-String -Pattern 'error CDX' | Select-Object -First 3) | ForEach-Object { "    $_" }
  $failures++
} elseif ($normalLen -gt 1048576) {
  Write-Host "  ok: compiled ($normalLen bytes)"
} else {
  Write-Host "  FAIL: the normal compile produced no binary (got $normalLen bytes)"
  $failures++
}

if ($failures -gt 0) {
  Write-Host ""
  Write-Host "deck-floor-test: FAILED ($failures)"
  exit 1
}
Write-Host ""
Write-Host "deck-floor-test: PASS (LEX and DESUGAR each name themselves when starved; shipping floors compile)"
exit 0
