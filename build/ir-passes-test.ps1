# The ir-check and occ-report passes report their findings in IR mode.
#
# They did not, and worse, they could not: adding either pass's diagnostic
# bag to compile-frontend-passes' merge killed the compiler in bag-add with a
# general protection fault, on a source as small as codex/test/arithmetic --
# the giant let-chain's register allocation crossed a spill cliff (see
# 3.24). So the merge was dropped to ship, and `-Passes occ-report` in IR mode
# ran the pass and printed nothing. The CDX path merged the same bags and
# reported fine; only the IR path was mute.
#
# The fix moved the merge into a helper (frontend-bag-with-passes) so the
# caller's binding count is unchanged, and taught emit-ir-uni to print
# notices the way the CDX emit path already does. This harness pins both
# halves against a fresh kernel:
#
#   POSITIVE  compile arithmetic in IR-UNI with -Passes occ-report; the
#             compiler must NOT crash (IR-END present) and must print at
#             least one 'OCC ' occurrence line.
#   NEGATIVE  the SAME compile with NO -Passes prints ZERO 'OCC ' lines --
#             so the positive is the pass reporting, not a constant.
#
# On-demand (boots VMs); not part of build.ps1. Exit 1 on any failure.
[CmdletBinding()]
param(
    [string]$Kernel = (Join-Path (Split-Path -Parent $PSScriptRoot) 'seed/Codex.cdx'),
    [string]$Src    = (Join-Path (Split-Path -Parent $PSScriptRoot) 'codex/test/arithmetic.codex')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$outDir  = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Invoke-IrUni([string]$passes, [string]$tag) {
    $log = Join-Path $outDir "ir-passes-$tag.log"
    $out = Join-Path $outDir "ir-passes-$tag.txt"
    $args = @('-NoProfile', '-File', $compile, '-Src', $Src, '-Out', $out, '-Log', $log, '-Kernel', $Kernel, '-IrUni')
    if ($passes) { $args += @('-Passes', $passes) }
    & pwsh @args *> $null
    if (-not (Test-Path $log)) { return @{ ended = $false; occ = 0 } }
    $text = Get-Content $log
    return @{
        ended = [bool]($text | Select-String -Pattern 'IR-END' -Quiet)
        occ   = @($text | Select-String -Pattern 'OCC ').Count
    }
}

$fail = 0

# POSITIVE: occ-report reports, and the compiler survives the merge.
$pos = Invoke-IrUni 'occ-report' 'occ'
if (-not $pos.ended) {
    Write-Host 'FAIL: IR-UNI -Passes occ-report did not reach IR-END (compiler crashed or halted).'
    $fail = 1
} elseif ($pos.occ -lt 1) {
    Write-Host 'FAIL: IR-UNI -Passes occ-report emitted no OCC lines (the pass reported nothing).'
    $fail = 1
} else {
    Write-Host "  occ-report: IR-END reached, $($pos.occ) OCC lines."
}

# NEGATIVE: without the pass, no OCC lines -- proves the positive is the pass.
$neg = Invoke-IrUni '' 'noocc'
if (-not $neg.ended) {
    Write-Host 'FAIL: IR-UNI with no passes did not reach IR-END.'
    $fail = 1
} elseif ($neg.occ -ne 0) {
    Write-Host "FAIL: IR-UNI with no passes emitted $($neg.occ) OCC lines; the positive is not the pass."
    $fail = 1
} else {
    Write-Host '  no-passes control: IR-END reached, 0 OCC lines.'
}

if ($fail -ne 0) { Write-Host 'ir-passes-test: FAIL'; exit 1 }
Write-Host 'ir-passes-test: OK (occ-report reports in IR mode; the compiler survives the merge)'
exit 0
