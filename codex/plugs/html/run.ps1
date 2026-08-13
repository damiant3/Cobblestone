# Run HTML plug: source -> IR-CCE (DCE in compiler) -> plug CDX -> HTML
# Native CCE pipeline. No encoding conversion. The compiler prunes
# unreachable defs (ir-prune-unreachable) during IR-CCE emit, so there
# is no external DCE step. The plug uses read-line-cce + read-file
# (both CCE-native, null-terminated).
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Src, [Parameter(Mandatory=$true)][string]$Out)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\html-plug.cdx'
$LogFile = Join-Path $PSScriptRoot 'build-output\run.log'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Phase 1: source -> IR-CCE
# text-plug: a browser primitive is a Codex stub (set-render (fn) = 0) whose
# real body is this plug's JS runtime function of the same name, so the inline
# passes must not delete the call. See text-plug-ir-pipeline in Passes.codex.
$IrFile = Join-Path $PSScriptRoot 'build-output\last-run.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug'
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
Write-Host "[html-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

# IR is already dead-code-eliminated by the compiler during IR-CCE emit
# (ir-prune-unreachable, reachable from 'opening'). Fully CCE-native, no
# Unicode round-trip, no external DCE step.
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)

# Phase 3: Build input -- CCE mode header + CCE IR + null terminator
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

# Phase 4: Run plug CDX
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
$html = ($lines -join "`n")
# Strip any leading control bytes the plug emits before the document
# (e.g. a stray CCE newline 0x01) -- they render as .notdef glyphs in browsers.
$html = $html -replace '^[\x00-\x1f]+', ''
[System.IO.File]::WriteAllText($Out, $html, [System.Text.UTF8Encoding]::new($false))
Write-Host "[html-plug] OK: $Out ($($html.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
