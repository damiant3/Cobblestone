# Run WinForms plug: source -> IR-CCE -> plug CDX -> C#
# Serial I/O pipeline (same as HTML plug).
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Src, [Parameter(Mandatory=$true)][string]$Out)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\winforms-plug.cdx'
$LogFile = Join-Path $PSScriptRoot 'build-output\run.log'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Phase 1: source -> IR-CCE
$IrFile = Join-Path $PSScriptRoot 'build-output\last-run.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
Write-Host "[winforms-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

$irBytes = [System.IO.File]::ReadAllBytes($IrFile)

# Phase 2: Build input -- CCE mode header + CCE IR + null terminator
$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)  # CCE newline
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0  # null terminator for read-file
[System.IO.File]::WriteAllBytes($inputFile, $combined)

# Phase 3: Run plug CDX
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem','3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; [Console]::Error.WriteLine("FAIL: timeout"); exit 4 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    [Console]::Error.WriteLine("FAIL: no output")
    if ($err -match 'EXC') { [Console]::Error.WriteLine($err.Substring(0, [Math]::Min(300, $err.Length))) }
    exit 5
}
$raw = [System.IO.File]::ReadAllText($outFile)
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$cs = ($lines -join "`n")
$cs = $cs -replace '^[\x00-\x1f]+', ''
[System.IO.File]::WriteAllText($Out, $cs, [System.Text.UTF8Encoding]::new($false))
Write-Host "[winforms-plug] OK: $Out ($($cs.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
