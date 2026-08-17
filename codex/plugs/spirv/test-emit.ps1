# End-to-end binary emit: compile a Codex source to IR, run it through the
# spirvbin plug, assert the emitted word stream validates in-Codex, and pack it
# little-endian into a real .spv whose magic number is checked host-side.
[CmdletBinding()]
param([string]$Src)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Src) { $Src = Join-Path $PSScriptRoot 'test\spirv-probe.codex' }

$OutDir  = Join-Path $PSScriptRoot 'build-output'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$PlugCdx = Join-Path $OutDir 'spirvbin-plug.cdx'
if (-not (Test-Path $PlugCdx)) { Write-Host "MISSING plug; run build-bin.ps1"; exit 2 }

$Seed   = Join-Path $Root 'seed\Codex.cdx'
$IrFile = Join-Path $OutDir 'emit.ir'
# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline in
# codex/compiler/IR/Passes.codex, and spirv/run.ps1, which passes the same
# flag: without it this script grades a different program than the plug is
# handed in service.
& pwsh -NoProfile -File (Join-Path $Root 'build\compile.ps1') -Src $Src -Out $IrFile -Log (Join-Path $OutDir 'emit-ir.log') -IrCce -Passes 'text-plug' -Kernel $Seed
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: IR compile; see emit-ir.log"; exit 3 }

# mode header (CCE "IR-CCE" + CCE newline) + IR bytes + null terminator
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
$hdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) { $u=[int]$ch; if ($u -lt 256) { $hdr.Add([byte]$script:UnicodeToCce[$u]) } }
$hdr.Add([byte]1)
$mode = $hdr.ToArray()
$inp = New-Object byte[] ($mode.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($mode,0,$inp,0,$mode.Length)
[Buffer]::BlockCopy($irBytes,0,$inp,$mode.Length,$irBytes.Length)
$inp[$inp.Length-1] = 0
$inputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($inputFile,$inp)

$outFile = Join-Path $OutDir 'emit.out'
if (Test-Path $outFile) { Remove-Item $outFile -Force }
$vm = Join-Path $Root 'tools\codex-vm.exe'
$p = Start-Process -FilePath $vm -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-headless','-mem','3072') -PassThru -WindowStyle Hidden
$p.WaitForExit(120000) | Out-Null
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; Write-Host "FAIL: VM timeout"; exit 4 }
Remove-Item $inputFile -Force -ErrorAction SilentlyContinue

$raw = if (Test-Path $outFile) { Get-Content $outFile -Raw } else { "" }
$lines = $raw -split "`n"
$verdict = ($lines | Select-String 'SPV-BIN-VALIDATE:' | Select-Object -First 1).ToString()
$boundL  = ($lines | Select-String 'SPV-BIN-BOUND:'    | Select-Object -First 1).ToString()
$wordsL  = ($lines | Select-String 'SPV-BIN-WORDS:'    | Select-Object -First 1).ToString()
Write-Host $verdict
Write-Host $boundL
if (-not $verdict -or ($verdict -notmatch 'VALID')) { Write-Host "FAIL: word stream did not validate"; exit 1 }

# Pack words little-endian into a real .spv, then re-check the magic host-side.
$wtext = ($wordsL -replace '.*SPV-BIN-WORDS:\s*','').Trim()
$words = @($wtext -split '\s+' | Where-Object { $_ -ne '' } | ForEach-Object { [uint32]([int64]$_ -band 0xFFFFFFFF) })
$bytes = New-Object byte[] ($words.Count * 4)
for ($i=0; $i -lt $words.Count; $i++) { [BitConverter]::GetBytes($words[$i]).CopyTo($bytes, $i*4) }
$spv = Join-Path $OutDir 'emit.spv'
[System.IO.File]::WriteAllBytes($spv, $bytes)
Write-Host "[emit] wrote $spv ($($bytes.Length) bytes, $($words.Count) words); magic=0x$('{0:X8}' -f $words[0])"
if ($words[0] -ne 0x07230203) { Write-Host "FAIL: packed .spv has wrong magic"; exit 1 }

Write-Host "SPV-EMIT: PASS"
exit 0
