# Run the T3ISA plug: Codex source -> IR-CCE -> plug CDX -> T3ISA assembly,
# then assemble to the .t3b word file and .t3d string sidecar with the encoder
# proven in spec/.
#
# The assembler is the one validated byte-for-byte against the external
# compiler's own output; see docs/Designs/Done/Compiler/T3IsaPlug.md.
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$Src,
  [Parameter(Mandatory=$true)][string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\t3isa-plug.cdx'
$LogFile = Join-Path $PSScriptRoot 'build-output\run.log'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx. Run codex/plugs/t3isa/build.ps1"); exit 2 }
. (Join-Path $Repo 'build\vm-config.ps1')

# Phase 1: source -> IR in CCE
$IrFile = Join-Path $PSScriptRoot 'build-output\last-run.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
Write-Host "[t3isa-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

# Phase 2: CCE mode header + CCE IR + null terminator
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
  $u = [int]$ch
  if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0
$inputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($inputFile, $combined)

# Phase 3: run the plug
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$vmOk = Invoke-PlugVmFileSerial -Kernel $PlugCdx -InputFile $inputFile -OutputFile $outFile -StderrFile $errFile -MemMB 3072 -TimeoutSec 300
if (-not $vmOk) { [Console]::Error.WriteLine("FAIL: timeout"); exit 4 }
if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
  $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
  [Console]::Error.WriteLine("FAIL: no output")
  if ($err -match 'EXC') { [Console]::Error.WriteLine($err.Substring(0, [Math]::Min(300, $err.Length))) }
  exit 5
}
$raw = [System.IO.File]::ReadAllText($outFile)
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' }
$asm = ($lines -join "`n") -replace '^[\x00-\x1f]+', ''
$asmFile = [System.IO.Path]::ChangeExtension($Out, '.t3s')
[System.IO.File]::WriteAllText($asmFile, $asm, [System.Text.UTF8Encoding]::new($false))
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue

# A refusal is a hard failure. The plug names every construct it cannot
# express rather than emitting something plausible for it, and an artifact
# built past one would make the whole experiment worthless.
$refusals = @(Select-String -Path $asmFile -Pattern '!UNSUPPORTED:' | ForEach-Object { $_.Line.Trim() })
if ($refusals.Count -gt 0) {
  [Console]::Error.WriteLine("REFUSED: the plug cannot express $($refusals.Count) construct(s) in this program:")
  $refusals | Select-Object -Unique | ForEach-Object { [Console]::Error.WriteLine("  $_") }
  [Console]::Error.WriteLine("  assembly with the markers is at $asmFile")
  exit 6
}

# Phase 4: assemble to words + sidecar. The encoder is the CODEX one, run
# through the plug's T3-ASM mode, not the PowerShell script in spec/. That
# script came first and is the proof the Codex chapter is held to
# (spec/validate-codex-encoder.ps1, fourteen programs byte-identical); it is
# no longer in the path that produces artifacts.
$hdr2 = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "T3-ASM".ToCharArray()) { $hdr2.Add([byte]$script:UnicodeToCce[[int]$ch]) }
$hdr2.Add([byte]1)
$asmBytes = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in ([System.IO.File]::ReadAllText($asmFile)).ToCharArray()) {
    $u = [int]$ch
    if ($u -eq 10) { $asmBytes.Add([byte]1); continue }
    if ($u -eq 13 -or $u -ge 256) { continue }
    $c = $script:UnicodeToCce[$u]
    if ($null -eq $c -or [int]$c -eq 0) { continue }
    $asmBytes.Add([byte]$c)
}
$buf = New-Object byte[] ($hdr2.Count + $asmBytes.Count + 1)
$hdr2.CopyTo($buf, 0)
$asmBytes.CopyTo($buf, $hdr2.Count)
$buf[$buf.Length - 1] = 0
$in2 = [System.IO.Path]::GetTempFileName(); $out2 = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($in2, $buf)
$vmOk2 = Invoke-PlugVmFileSerial -Kernel $PlugCdx -InputFile $in2 -OutputFile $out2 -StderrFile $errFile -MemMB 3072 -TimeoutSec 300
if (-not $vmOk2) { [Console]::Error.WriteLine("FAIL: encoder timeout"); exit 7 }
# The first output line carries the serial framing's control bytes; a reader
# that does not strip them loses it, which reads as a dropped instruction.
$encRaw = ([System.IO.File]::ReadAllText($out2)) -replace '^[\x00-\x1f]+', ''
Remove-Item $in2,$out2 -Force -ErrorAction SilentlyContinue
$encErr = @($encRaw -split "`r?`n" | Where-Object { $_ -match '^!ENCODE-ERROR' })
if ($encErr.Count -gt 0) { [Console]::Error.WriteLine("FAIL: encoder refused:"); $encErr | ForEach-Object { [Console]::Error.WriteLine("  $_") }; exit 7 }
$words = @($encRaw -split "`r?`n" | Where-Object { $_ -match '^W ' } | ForEach-Object { [long]($_.Substring(2).Trim()) })
$sideLines = @($encRaw -split "`r?`n" | Where-Object { $_ -match '^S ' } | ForEach-Object { $_.Substring(2) })
if ($words.Count -eq 0) { [Console]::Error.WriteLine("FAIL: encoder produced no words"); exit 7 }
$bytes = New-Object byte[] ($words.Count * 8)
for ($i = 0; $i -lt $words.Count; $i++) { [BitConverter]::GetBytes([long]$words[$i]).CopyTo($bytes, $i * 8) }
[System.IO.File]::WriteAllBytes($Out, $bytes)
$sidecar = [System.IO.Path]::ChangeExtension($Out, '.t3d')
[System.IO.File]::WriteAllLines($sidecar, $sideLines, [System.Text.UTF8Encoding]::new($false))
Write-Host "[t3isa-plug] OK: $Out ($($words.Count) words), $sidecar ($($sideLines.Count) strings), $asmFile"
