# A starved deck floor says so, and the deck grows, instead of trampling memory.
#
# A phase reserves its deck with `build`. When a phase overflows its floor it
# raises CDX9002, the front end and the back end grow that deck (CDX9014
# "<DECK> deck grown to N bytes; phase repeated") and repeat the phase, and the
# compile goes on. An in-phase guard has to stop the phase a guard band short of
# its ceiling, because the phase-end check runs after any damage. Inside one
# deck-record extent the deck cursor cell is not written until the extent
# exits, so such a guard reads the live heap cursor (deck-bound-short-of); a
# guard reading __deck-pos there never fires (COMPILER-148).
#
# Legs, on the compiler's own unit:
#
#   -Decks 5    must compile, LEX grown, no crash
#   -Decks 15   must compile, LEX and FRONTEND-KEEP (DESUGAR) grown, no crash
#   -Decks 100  must compile with no growth and no CDX9002
#
# 15 is the leg with teeth: under seeds 229F2026 to 76F77A0D (main 45004 to
# 50365) it trapped in token-text, DESUGAR having overrun its deck into the
# token arrays above it. 5 trapped in __alloc under BC0D2829, CHECK's stale-read
# scan keeping every definition's scratch on the CHECK deck.
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

# Each starved leg asserts which decks grew, because "some deck grew" would pass
# just as well if the wrong floor spoke first.
foreach ($leg in @(@{ Decks = 5; Grown = @('LEX') }, @{ Decks = 15; Grown = @('LEX', 'FRONTEND-KEEP') })) {
  $d = $leg.Decks
  Write-Host "deck-floor-test: -Decks $d (expect a compile with $($leg.Grown -join ' and ') grown)"
  $starved = Invoke-Leg -Decks $d -Tag "decks$d"
  $grown = @([regex]::Matches($starved, 'CDX9014: (\S+) deck grown') | ForEach-Object { $_.Groups[1].Value })
  foreach ($want in $leg.Grown) {
    if ($grown -contains $want) { Write-Host "  ok: $want deck grown" }
    else { Write-Host "  FAIL: no CDX9014 for $want (grown: $($grown -join ','))"; $failures++ }
  }
  if ($starved -match '!EXC=' -or $starved -match 'CRASH in ') {
    Write-Host "  FAIL: the starved compile crashed"
    ($starved -split "`n" | Select-String -Pattern '!EXC=|CRASH in ' | Select-Object -First 3) | ForEach-Object { "    $_" }
    $failures++
  } elseif ($starved -match 'error CDX') {
    Write-Host "  FAIL: the starved compile reported errors"
    ($starved -split "`n" | Select-String -Pattern 'error CDX' | Select-Object -First 3) | ForEach-Object { "    $_" }
    $failures++
  } elseif (-not (Test-Path $script:LegBinary) -or (Get-Item $script:LegBinary).Length -le 1048576) {
    Write-Host "  FAIL: the starved compile produced no binary"
    $failures++
  } else {
    Write-Host "  ok: compiled, no crash"
  }
}
# The last leg -- and the guard must not fire on a floor that is fine. Without
# it the script passes just as well against a compiler that refuses every
# compile, which is the classic function that always answers the same thing.
Write-Host "deck-floor-test: -Decks 100 (expect a clean compile)"
$normal = Invoke-Leg -Decks 100 -Tag 'decks100'
$normalBin = $script:LegBinary
$normalLen = if (Test-Path $normalBin) { (Get-Item $normalBin).Length } else { 0 }
if ($normal -match 'CDX9002' -or $normal -match 'CDX9014') {
  Write-Host "  FAIL: CDX9002 or a grown deck at the shipping floors -- the guard is firing on healthy input"
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
Write-Host "deck-floor-test: PASS (starved floors grow and compile; shipping floors compile without growth)"
exit 0
