# Run WASM plug: source -> IR-CCE -> plug CDX -> WAT
[CmdletBinding()]
param([string]$Src, [Parameter(Mandatory=$true)][string]$Out, [string]$Ir)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\wasm-plug.cdx'
$LogFile = Join-Path $PSScriptRoot 'build-output\run.log'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Phase 1: source -> IR-CCE, or take IR already compiled by the caller.
# -Ir is what build/plug-oracle-test.ps1 passes: the harness compiles the
# subject once and feeds the same IR to every plug, so re-compiling here would
# grade a different program than the other arms.
$IrFile = Join-Path $PSScriptRoot 'build-output\last-run.ir'
if ($Ir) {
    if (-not (Test-Path -PathType Leaf $Ir)) { [Console]::Error.WriteLine("MISSING: -Ir $Ir"); exit 3 }
    $IrFile = (Resolve-Path $Ir).Path
} elseif ($Src) {
# text-plug: this plug resolves a Codex call by its NAME -- ISA-shaped target,
# by-name resolution -- so the inline passes must not substitute a body and
# delete the call. See text-plug-ir-pipeline in codex/compiler/IR/Passes.codex.
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug'
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
} else {
    [Console]::Error.WriteLine("FAIL: provide -Src <source.codex> or -Ir <prebuilt.ir>")
    exit 1
}
Write-Host "[wasm-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

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
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem', '3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
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
$wat = ($lines -join "`n")
$wat = $wat -replace '^[\x00-\x1f]+', ''
[System.IO.File]::WriteAllText($Out, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[wasm-plug] OK: $Out ($($wat.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
