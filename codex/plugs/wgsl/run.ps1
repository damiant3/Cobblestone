# Run WGSL plug: source -> IR-CCE -> plug CDX -> WGSL text
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Src, [Parameter(Mandatory=$true)][string]$Out)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\wgsl-plug.cdx'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Scratch is keyed to $Out so two concurrent runs cannot share it. A fixed
# last-run.ir crossed two runs' outputs and cost ten guests on 2026-09-02
# (L-SHARED); the outputs were plausible, so nothing failed and the grading
# read as a regression in the plug.
$RunTag  = [System.IO.Path]::GetFileNameWithoutExtension($Out)
$LogFile = Join-Path $PSScriptRoot "build-output\run-$RunTag.log"

# Phase 1: source -> IR-CCE
$IrFile = Join-Path $PSScriptRoot "build-output\last-run-$RunTag.ir"
# text-plug: this plug resolves a Codex call by its NAME -- ISA-shaped target,
# by-name resolution -- so the inline passes must not substitute a body and
# delete the call. See text-plug-ir-pipeline in codex/compiler/IR/Passes.codex.
# -Kernel names the compiler. Without it compile.ps1 takes whatever build.ps1
# last left in build-output, which is a compiler nobody chose and which its own
# NOTE line says is not the seed: the IR under every shader in the tree was
# produced by an unrecorded one. The plug build beside this file already names
# the seed (plug-build-lib.ps1), so this only brings the two halves of the same
# pipeline onto the same compiler.
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' -Kernel (Join-Path $Repo 'seed\Codex.cdx')
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
Write-Host "[wgsl-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

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
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wgsl = ($lines -join "`n")
$wgsl = $wgsl -replace '^[\x00-\x1f]+', ''

# The emitter names what it could not lower rather than emitting a plausible
# value for it (L-BAILVALUE), and this is the reader for that channel: without
# one the marker rides into a .wgsl file and is found later by a browser, or
# not at all. Refusing BEFORE the write is what keeps a half-lowered shader off
# disk, so a stale but correct .wgsl survives a failed run.
$markers = @()
$markers += [regex]::Matches($wgsl, 'CODEX_REFUSED_[A-Za-z0-9_]+') | ForEach-Object { $_.Value }
if ($wgsl -match 'WGSL PLUG REFUSAL') { $markers += 'WGSL_PLUG_REFUSAL_undelivered_definitions' }
if ($markers.Count -gt 0) {
    [Console]::Error.WriteLine("FAIL: the emitter refused $($markers.Count) construct(s) in $Src. Nothing was written to $Out.")
    $wl = $wgsl -split "`n"
    foreach ($m in @($markers | Sort-Object -Unique)) {
        $n = @($markers | Where-Object { $_ -eq $m }).Count
        [Console]::Error.WriteLine("  $m  x$n")
        for ($i = 0; $i -lt $wl.Count; $i++) {
            if ($wl[$i] -match [regex]::Escape($m)) {
                [Console]::Error.WriteLine("    line $($i + 1): $($wl[$i].Trim())")
                break
            }
        }
    }
    [Console]::Error.WriteLine("  The rules are docs/Reference/WgslSpec.md.")
    Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
    exit 6
}

[System.IO.File]::WriteAllText($Out, $wgsl, [System.Text.UTF8Encoding]::new($false))
Write-Host "[wgsl-plug] OK: $Out ($($wgsl.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
