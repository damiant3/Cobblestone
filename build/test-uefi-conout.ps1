# test-uefi-conout.ps1 -- compile codex/test chapters with -Uefi, boot each as
# BOOTX64.EFI under codex-vm -uefi, and compare the firmware ConOut stream
# (codex-vm -conout, UTF-16 re-encoded as UTF-8) to the chapter's .expected.
#
# The sidecar set cannot ask for -uefi, so __uefi_print and __uefi_print_no_nl
# are graded here and nowhere else (COMPILER-22, L-NOGATE). Calibrated
# 2026-09-23: seed 966EF113B021F561 (main 19381, before COMPILER-21) prints
# tier-0 Cyrillic as its low byte and fails tier0-cyrillic-print here.
#
#   build/test-uefi-conout.ps1 -Kernel seed\Codex.cdx
#   build/test-uefi-conout.ps1 -Kernel build\output\Sut.cdx -Tests ops/tier0-cyrillic-print
[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string[]]$Tests = @('ops/tier0-cyrillic-print', 'ops/tier0-cyrillic-print-uni'),
    [int]$TimeoutSec = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo
$Vm = Join-Path $Repo 'tools\codex-vm.exe'
$KernelAbs = if ([IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $Repo $Kernel }
foreach ($f in @($Vm, $KernelAbs)) { if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing"; exit 1 } }
Write-Host ("kernel: $Kernel [" + (Get-FileHash -Algorithm SHA256 $KernelAbs).Hash.Substring(0, 16) + "]")

# Per workspace (L-SHARED).
$Work = Join-Path ([IO.Path]::GetTempPath()) ("uefi-conout-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

function Get-Lf([byte[]]$b) { return [Text.Encoding]::UTF8.GetString($b) -replace "`r`n", "`n" }

$bad = 0
foreach ($t in $Tests) {
    $name = Split-Path $t -Leaf
    $src = Join-Path $Repo "codex\test\$t.codex"
    $exp = Join-Path $Repo "codex\test\$t.expected"
    $missing = @(@($src, $exp) | Where-Object { -not (Test-Path -PathType Leaf $_) })
    if ($missing.Count -gt 0) { Write-Host "  $name  FAIL: $($missing -join ', ') missing"; $bad++; continue }
    $cdx = Join-Path $Work "$name.cdx"; $efi = Join-Path $Work "$name.efi"; $img = Join-Path $Work "$name.img"
    $con = Join-Path $Work "$name.conout"; $err = Join-Path $Work "$name.err"
    Remove-Item $cdx, $efi, $img, $con, $err -ErrorAction SilentlyContinue

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') -Src $src -Out $cdx -Log (Join-Path $Work "$name.compile.log") -Kernel $KernelAbs -Uefi *> (Join-Path $Work "$name.compile.out")
    if (-not (Test-Path $cdx)) { Write-Host "  $name  FAIL: compile (log $Work\$name.compile.log)"; $bad++; continue }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'cdx-to-pe.ps1') -CdxInput $cdx -Out $efi *> (Join-Path $Work "$name.pe.log")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $efi)) { Write-Host "  $name  FAIL: cdx-to-pe"; $bad++; continue }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-img.ps1') -PeInput $efi -Out $img -TotalSectors 32768 *> (Join-Path $Work "$name.img.log")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $img)) { Write-Host "  $name  FAIL: build-img"; $bad++; continue }

    $p = Start-Process -FilePath $Vm -ArgumentList @('-kernel', $img, '-uefi', '-headless', '-conout', $con) `
            -NoNewWindow -PassThru -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")
    $p.WaitForExit($TimeoutSec * 1000) | Out-Null
    $timedOut = -not $p.HasExited
    if ($timedOut) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }

    # Distinct failure states (L-STATES): a VM that cannot write -conout, a
    # truncated capture (L-SHORT), and wrong bytes are three different causes.
    if (-not (Test-Path $con)) { Write-Host "  $name  FAIL: no ConOut capture (codex-vm without -conout?)"; $bad++; continue }
    $want = Get-Lf ([IO.File]::ReadAllBytes($exp))
    $got = Get-Lf ([IO.File]::ReadAllBytes($con))
    $crash = @(Get-Content $err -ErrorAction SilentlyContinue | Where-Object { $_ -match 'HOST CRASH' }).Count -gt 0
    if ($got -eq $want -and -not $crash) { Write-Host "  $name  PASS ($($want.Length) chars)"; continue }
    $bad++
    $why = if ($crash) { 'HOST CRASH' } elseif ($got.Length -lt $want.Length -and $want.StartsWith($got)) { "SHORT: strict prefix, $($got.Length) of $($want.Length) chars" + $(if ($timedOut) { ', timed out' } else { '' }) } else { 'WRONG BYTES' }
    Write-Host "  $name  FAIL: $why"
    Write-Host "    expected: $($want.TrimEnd())"
    Write-Host "    got:      $($got.TrimEnd())"
}
if ($bad -gt 0) { Write-Host "test-uefi-conout: FAIL ($bad of $($Tests.Count); artifacts in $Work)"; exit 1 }
Write-Host "test-uefi-conout: OK ($($Tests.Count) of $($Tests.Count))"
exit 0
