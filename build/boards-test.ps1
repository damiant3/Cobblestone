# boards-test.ps1 -- the board battery.
#
# The nine IoT board drivers (codex/boards/) have smoke tests in
# codex/test/*-drivers.codex, but they cannot live in the default battery:
# three of them need `codex-vm -board-mmio` to back the register windows
# that sit above the RAM ceiling, and the generic harness passes no such
# flag. So they run from here, all nine together, with the flag on.
#
# Each test's `opening` returns a count of the sub-tests that passed, and the
# .expected sidecar holds that number. We report the count per board and sum
# them, because the sub-test total is a number the docs have quoted for months
# and nothing has ever measured. Never carry a count forward -- re-measure it.
#
# What this proves and what it does not: a register access lands on memory and
# reads back what it wrote, so the address arithmetic, the access width, and
# the read-modify-write logic are all exercised. It is NOT peripheral
# behaviour, and no silicon has been in the loop. Renode is where that lives;
# -Renode adds the cross-architecture boot smoke on the modelled boards.
#
# Usage:
#   build/boards-test.ps1                  # the nine board driver batteries
#   build/boards-test.ps1 -Only rp2040     # one board
#   build/boards-test.ps1 -Renode          # also the arm64/riscv64 boot smoke
[CmdletBinding()]
param(
    [string[]]$Only,
    [switch]$Renode,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'build-output\boards'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$Vm = Join-Path $Repo 'tools\codex-vm.exe'
if (-not (Test-Path $Vm)) { throw "codex-vm not built: $Vm (run tools/build-vm.ps1)" }

# Every board with a driver chapter and a smoke test. `Aperture` records why a
# board needs -board-mmio, so a failure here reads as a fact and not a mystery.
#
# `Test` is the test's base name. It is almost always <board>-drivers, but QEMU
# virt's is codex/test/qemu-virt-board.codex -- and the first revision of this
# script assumed the convention, failed to find it, and reported the board as
# having no smoke test at all. It has one, it is in the default battery, and it
# returns 6. That false gap went as far as a BACKLOG entry before it was caught.
# Do not infer a coverage gap from a filename.
$boards = @(
    @{ Name = 'stm32f4';  Test = 'stm32f4-drivers';  Aperture = $null                       }
    @{ Name = 'esp32c6';  Test = 'esp32c6-drivers';  Aperture = $null                       }
    @{ Name = 'pi4';      Test = 'pi4-drivers';      Aperture = 'BCM2711 @ 0xFE000000'      }
    @{ Name = 'nrf52840'; Test = 'nrf52840-drivers'; Aperture = $null                       }
    @{ Name = 'rp2040';   Test = 'rp2040-drivers';   Aperture = 'SIO @ 0xD0000000'          }
    @{ Name = 'nrf9160';  Test = 'nrf9160-drivers';  Aperture = $null                       }
    @{ Name = 'stm32l4';  Test = 'stm32l4-drivers';  Aperture = 'Cortex-M SCB @ 0xE000ED00' }
    @{ Name = 'fe310';    Test = 'fe310-drivers';    Aperture = $null                       }
    @{ Name = 'qemuvirt'; Test = 'qemu-virt-board';  Aperture = $null                       }
)

if ($Only) { $boards = $boards | Where-Object { $Only -contains $_.Name } }
if (-not $boards) { throw "no boards matched -Only: $($Only -join ', ')" }

Write-Host ""
Write-Host "Board battery -- $($boards.Count) boards, codex-vm with -board-mmio" -ForegroundColor Cyan
Write-Host ""

$pass = 0; $fail = 0; $subtotal = 0
$failed = @()
$untested = @()

foreach ($b in $boards) {
    $name = $b.Name
    $src  = Join-Path $Repo "codex\test\$($b.Test).codex"
    $exp  = Join-Path $Repo "codex\test\$($b.Test).expected"

    if (-not (Test-Path $src)) {
        # Not a skip to shrug at: a board chapter exists and nothing exercises it.
        # Check the filename before you believe it -- see the note on $boards.
        Write-Host ("  {0,-9} UNTESTED  driver chapter exists, no smoke test ($($b.Test))" -f $name) -ForegroundColor Yellow
        $untested += $name
        continue
    }

    $cdx = Join-Path $OutDir "$name-drivers.cdx"
    $log = Join-Path $OutDir "$name-drivers.log"
    $out = Join-Path $OutDir "$name-drivers.out"

    Write-Host ("  {0,-9} compiling..." -f $name) -NoNewline

    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
        -Src $src -Out $cdx -Log $log 2>&1 | Out-Null
    $ErrorActionPreference = $prev

    if (-not (Test-Path $cdx)) {
        Write-Host "`r  $("{0,-9}" -f $name) FAIL  compile (see $log)          " -ForegroundColor Red
        $fail++; $failed += $name; continue
    }

    # -board-mmio is passed unconditionally. Six boards do not need it and are
    # unaffected by it; the three that do would page-fault without it.
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $Vm -kernel $cdx -headless -board-mmio -output $out 2>&1 | Out-Null
    $ErrorActionPreference = $prev

    $got  = if (Test-Path $out) { ((Get-Content $out -Raw) -replace "`r", '').Trim() } else { '' }
    $want = ((Get-Content $exp -Raw) -replace "`r", '').Trim()

    if ($got -eq $want) {
        # The serial stream opens with a 0x01 (SOH) marker, which Trim() does not
        # strip and which both the actual and the expected carry -- so equality
        # holds, but a ^\d+$ anchor does not. Pull the count out by search.
        $n = if ($got -match '(\d+)') { [int]$matches[1] } else { 0 }
        $subtotal += $n
        $note = if ($b.Aperture) { "  [aperture: $($b.Aperture)]" } else { '' }
        Write-Host "`r  $("{0,-9}" -f $name) PASS  $n sub-tests$note                    " -ForegroundColor Green
        $pass++
    } else {
        $shown = if ($got) { $got } else { '(no output)' }
        Write-Host "`r  $("{0,-9}" -f $name) FAIL  got '$shown' want '$want'            " -ForegroundColor Red
        $fail++; $failed += $name
    }
}

Write-Host ""
Write-Host "  Boards: $pass pass, $fail fail" -ForegroundColor $(if ($fail) { 'Red' } else { 'Green' })
Write-Host "  Sub-tests passing: $subtotal (measured, not carried forward)" -ForegroundColor $(if ($fail) { 'Yellow' } else { 'Green' })
if ($failed)   { Write-Host "  Failed: $($failed -join ', ')" -ForegroundColor Red }
if ($untested) { Write-Host "  Untested boards (no smoke test at all): $($untested -join ', ')" -ForegroundColor Yellow }

if ($Renode) {
    Write-Host ""
    Write-Host "Renode -- cross-architecture boot smoke" -ForegroundColor Cyan
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File (Join-Path $Repo 'build\test-boards.ps1')
    $renodeFail = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($renodeFail) { $fail += $renodeFail }
}

if (-not $KeepArtifacts -and -not $fail) {
    Remove-Item (Join-Path $OutDir '*.cdx') -Force -ErrorAction SilentlyContinue
}

Write-Host ""
exit $fail
