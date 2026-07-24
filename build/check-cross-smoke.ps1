# check-cross-smoke.ps1 -- the cross-arch lanes still EXECUTE
#
# build.ps1's plug-binary leg proves the arm64 and riscv plugs COMPILE. It
# runs nothing they emit, so "this lane produces no output at all" is a class
# the gate cannot see. That class is not hypothetical: CL 8221 put an
# unconditional PSCI CPU_ON in every ARM64 program's __start, the committed
# Renode board answers no PSCI, and every ARM64 program stopped reaching
# `opening`. Nothing caught it. It was found by hand weeks later, and 238
# tests had been failing silently the whole time (this leg is what closed that gap).
#
# One program per architecture, booted for real and compared against its
# .expected. That is enough to tell a live lane from a dead one, which is the
# whole job here. It is deliberately NOT the battery -- build/test-cross-batch.ps1
# is the battery and stays out-of-band, because 363 tests do not belong in a
# gate that runs every few minutes.
#
# Usage: check-cross-smoke.ps1 [-Tests a,b] [-TimeoutSec N]

param(
    [string[]]$Tests = @('factorial'),
    [int]$TimeoutSec = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

$crossScript = Join-Path $PSScriptRoot 'test-cross.ps1'
if (-not (Test-Path $crossScript)) {
    Write-Host "check-cross-smoke: missing $crossScript"
    exit 1
}

$fail    = @()
$ran     = 0
$skipped = @()

foreach ($arch in @('arm64','riscv64')) {
    foreach ($t in $Tests) {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $out = & pwsh -NoProfile -File $crossScript -Arch $arch -Test $t -TimeoutSec $TimeoutSec 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev
        $text = ($out | Out-String)

        # test-cross.ps1 exits 0 when Renode or the board file is absent. A
        # missing emulator is not a passing lane, and letting it read as one
        # would rebuild the exact blind spot this leg exists to remove.
        if ($text -match 'SKIP') {
            $skipped += "$arch/$t"
            continue
        }

        $ran++
        if ($code -ne 0) {
            $fail += "$arch/$t"
            Write-Host "check-cross-smoke: FAIL $arch/$t"
            foreach ($line in ($text -split "`r?`n")) {
                if ($line -match 'FAIL|exp=|act=') { Write-Host "    $($line.Trim())" }
            }
        }
    }
}

if ($skipped.Count -gt 0) {
    Write-Host "check-cross-smoke: NOT EXECUTED: $($skipped -join ', ')"
    Write-Host "  Renode or a board file is missing. These lanes were not tested;"
    Write-Host "  a break in them cannot fail this gate on this machine."
}

if ($fail.Count -gt 0) {
    Write-Host "check-cross-smoke: FAIL ($($fail.Count) of $ran executed): $($fail -join ', ')"
    Write-Host "  A cross-arch lane stopped producing the right output. x86-64 cannot"
    Write-Host "  catch this -- it does not run these backends."
    exit 1
}

if ($ran -eq 0) {
    Write-Host "check-cross-smoke: nothing executed (see above). Not a pass."
    exit 0
}

Write-Host "check-cross-smoke: OK ($ran program(s) executed across the cross-arch lanes)"
exit 0
