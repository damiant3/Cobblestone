# PTX check for the Real approximate storage intrinsics: compile
# test/approx-storage-probe.codex through the plug and assert that
# device-load-approx and device-store-approx move f32 elements.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Probe = Join-Path $PSScriptRoot 'test\approx-storage-probe.codex'
$Out = Join-Path $PSScriptRoot 'build-output\approx-storage-probe.ptx'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $Probe -Out $Out
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: plug run failed"; exit 1 }

$ptx = Get-Content $Out -Raw
$failed = $false
foreach ($r in @('ld.global.f32', 'st.global.f32', '.visible .entry gpu_approx_sq', '.visible .entry gpu_approx_const', 'mov.b32', 'cvt.rn.f32.s64')) {
    if (-not $ptx.Contains($r)) { Write-Host "FAIL: missing '$r'"; $failed = $true }
}
foreach ($r in @('mul.f32', 'add.f32')) { if (-not $ptx.Contains($r)) { Write-Host "FAIL: missing '$r' (Real approximate arithmetic in f32)"; $failed = $true } }
foreach ($r in @('mul.f64', 'add.f64')) { if ($ptx.Contains($r)) { Write-Host "FAIL: '$r' in a probe that holds only Real approximate"; $failed = $true } }
if ($ptx -match 'unknown gpu intrinsic') { Write-Host "FAIL: an intrinsic was not recognised"; $failed = $true }
if ($failed) { Write-Host "PTX-APPROX-STORAGE: FAIL"; exit 1 }
Write-Host "PTX-APPROX-STORAGE: PASS"
exit 0
