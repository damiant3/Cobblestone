# PTX f64 regression check: compile test/f64-probe.codex through the
# plug and assert the emitted PTX uses the true f64 float path.
# Codex Real is f64 - .f32 may appear only in the documented trig
# sandwich (sin/cos/tan approx units) and the unused %fs bank decl.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Probe = Join-Path $PSScriptRoot 'test\f64-probe.codex'
$Out = Join-Path $PSScriptRoot 'build-output\f64-probe.ptx'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $Probe -Out $Out
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: plug run failed"; exit 1 }

$ptx = Get-Content $Out -Raw
$required = @(
    'ld.global.f64',
    'st.global.f64',
    'mul.f64',
    'add.f64',
    'div.rn.f64',
    'setp.gt.f64',
    '.param .f64',
    'ld.param.f64',
    'mov.f64',
    '.visible .entry gpu_axpy'
)
$failed = $false
foreach ($r in $required) {
    if (-not $ptx.Contains($r)) { Write-Host "FAIL: missing '$r'"; $failed = $true }
}

# No f32 outside the trig sandwich: the probe has no trig, so the only
# permitted .f32 is the unused %fs register bank declaration.
$f32Lines = ($ptx -split "`n") | Where-Object { $_ -match '\.f32' -and $_ -notmatch '\.reg \.f32' }
if ($f32Lines) {
    Write-Host "FAIL: stray .f32 in emitted PTX:"
    $f32Lines | ForEach-Object { Write-Host "  $_" }
    $failed = $true
}

if ($failed) { Write-Host "PTX-F64: FAIL"; exit 1 }
Write-Host "PTX-F64: PASS (all f64 forms present, no stray .f32)"
exit 0
