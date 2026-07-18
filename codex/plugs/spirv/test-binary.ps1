# SPIR-V binary encoder self-check: build SpirvBinary + a probe into one CDX,
# boot it, and assert the hand-built minimal GLCompute module validates while a
# deliberately miscounted module is rejected. There is no spirv-val on this box
# (CLAUDE.md rule 6), so spv-validate IS the validator and this is its test.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root  = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$Lib   = Join-Path $PSScriptRoot 'SpirvBinary.codex'
$Probe = Join-Path $PSScriptRoot 'test\binary-probe.codex'
$OutDir = Join-Path $PSScriptRoot 'build-output'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$Combined = Join-Path $OutDir 'binary-selfcheck.codex'
$Cdx      = Join-Path $OutDir 'binary-selfcheck.cdx'
$Log      = Join-Path $OutDir 'binary-selfcheck.log'
$OutFile  = Join-Path $OutDir 'binary-selfcheck.out'

# One compilation unit: the library chapter followed by the probe chapter.
$src = (Get-Content $Lib -Raw) + "`n" + (Get-Content $Probe -Raw)
[System.IO.File]::WriteAllText($Combined, $src, [System.Text.UTF8Encoding]::new($false))

Write-Host "[spirv-bin] compiling self-check..."
$Seed = Join-Path $Root 'seed\Codex.cdx'
& pwsh -NoProfile -File (Join-Path $Root 'build\compile.ps1') -Src $Combined -Out $Cdx -Log $Log -Kernel $Seed
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: compile failed; see $Log"; exit 1 }

$vm = Join-Path $Root 'tools\codex-vm.exe'
if (Test-Path $OutFile) { Remove-Item $OutFile -Force }
$p = Start-Process -FilePath $vm -ArgumentList @('-kernel',$Cdx,'-output',$OutFile,'-headless','-mem','3072') -PassThru -WindowStyle Hidden
$p.WaitForExit(120000) | Out-Null
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Host "FAIL: VM timeout"; exit 1 }

$out = if (Test-Path $OutFile) { Get-Content $OutFile -Raw } else { "" }
Write-Host "--- guest output ---"
Write-Host $out
Write-Host "--------------------"

$ok = $true
if ($out -notmatch 'SPIRV-BIN valid-module: VALID')  { Write-Host "FAIL: valid module did not validate"; $ok = $false }
if ($out -notmatch 'SPIRV-BIN broken-module: FAIL')  { Write-Host "FAIL: broken module was not rejected"; $ok = $false }
if ($out -notmatch 'SPIRV-BIN dup-id-module: FAIL')  { Write-Host "FAIL: duplicate result id was not caught"; $ok = $false }
if ($out -notmatch 'SPIRV-BIN oob-id-module: FAIL')  { Write-Host "FAIL: out-of-bound result id was not caught"; $ok = $false }
if ($out -notmatch 'SPIRV-BIN dangling-module: FAIL') { Write-Host "FAIL: dangling id reference was not caught"; $ok = $false }

if ($ok) { Write-Host "SPIRV-BIN: PASS"; exit 0 }
Write-Host "SPIRV-BIN: FAIL"; exit 1
