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
# A SILENT LANE IS RETRIED ONCE, ALONE; A WRONG ANSWER NEVER IS.
# test-cross-batch.ps1 learned this and this leg had not, which is backwards:
# the batch is out-of-band and this runs inside build.ps1 every few minutes,
# across several agents' workspaces on one box. Renode gets a 3 second budget
# here, and a busy box makes a healthy lane miss it -- observed 2026-07-28,
# both lanes reported `no uart output` in a gate run and both passed standalone
# seconds later at the same 3 second budget.
#
# The distinction is the whole design and it is the batch harness's: silence
# under load is the machine's fault, a wrong answer is the program's.
# `FAIL (no uart output)` means the emulator wrote no log at all, which is what
# contention looks like. `FAIL (output mismatch)` is a deterministic wrong
# answer; re-running one would spend time and, worse, let a genuine
# intermittent defect be dismissed as a flake. Compile failures are not
# retried either -- they are not this class.
#
# The retry line is printed even when nothing was retried. A run that silently
# absorbed a flake would otherwise read identically to a run that never had
# one, and that difference is the only thing that makes a returning flake
# visible.
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
$silent  = @()
$retried = 0
$recovered = 0

function Invoke-CrossTest([string]$arch, [string]$t) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $out = & pwsh -NoProfile -File $script:crossScript -Arch $arch -Test $t -TimeoutSec $script:TimeoutSec 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    return @{ Code = $code; Text = ($out | Out-String) }
}

function Write-CrossFailDetail([string]$text) {
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match 'FAIL|exp=|act=') { Write-Host "    $($line.Trim())" }
    }
}

foreach ($arch in @('arm64','riscv64')) {
    foreach ($t in $Tests) {
        $r = Invoke-CrossTest $arch $t

        # test-cross.ps1 exits 0 when Renode or the board file is absent. A
        # missing emulator is not a passing lane, and letting it read as one
        # would rebuild the exact blind spot this leg exists to remove.
        if ($r.Text -match 'SKIP') {
            $skipped += "$arch/$t"
            continue
        }

        $ran++
        if ($r.Code -ne 0) {
            Write-Host "check-cross-smoke: FAIL $arch/$t"
            Write-CrossFailDetail $r.Text
            # Only a lane that produced NO log is contention-shaped. A wrong
            # answer is the program's and is never re-run.
            if ($r.Text -match 'no uart output') {
                $silent += "$arch/$t"
            } else {
                $fail += "$arch/$t"
            }
        }
    }
}

# ---- Retry pass: no output at all is the box being busy, not a dead lane ----
if ($silent.Count -gt 0) {
    Write-Host "check-cross-smoke: $($silent.Count) silent lane(s), retrying one at a time"
    foreach ($name in $silent) {
        $parts = $name -split '/', 2
        $r = Invoke-CrossTest $parts[0] $parts[1]
        $retried++
        if ($r.Code -eq 0) {
            $recovered++
            Write-Host "  $name -- passed on serial retry (no output under load)"
        } else {
            $fail += $name
            Write-Host "  $name -- still failing alone"
            Write-CrossFailDetail $r.Text
        }
    }
}

# Stated even when zero, so a run that absorbed a flake does not read like a
# run that never had one.
Write-Host "check-cross-smoke: serial retries $recovered/$retried recovered (silent lanes only; a wrong answer is never re-run)"

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
