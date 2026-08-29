# Run WASM plug: source -> IR-CCE -> plug CDX -> WAT
[CmdletBinding()]
# -Decks is a pass-through to compile.ps1 for a bundle whose IR compile needs a
# bigger reservation than the default. The native backends need it: the riscv
# bundle carries the compiler's LIR as well as the whole emitter, and its IR
# compile dies in __alloc at ~542 MB without one, exactly as its NETWORK build
# does without the -Decks 160 that build passes. 0 means "say nothing", so every
# existing caller of this shared service is unchanged.
param([string]$Src, [Parameter(Mandatory=$true)][string]$Out, [string]$Ir, [string]$Kernel, [int]$Decks = 0)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\wasm-plug.cdx'
# Beside the caller's -Out rather than at a fixed path here. Every other plug's
# run.ps1 owns its scratch because only that plug uses it; this one is a shared
# service -- codex/plugs/common/build-plug-wasm.ps1 routes EVERY plug's wasm
# module through it -- so a fixed path here is one file that concurrent builds
# of different plugs overwrite in turn. Deriving from -Out, which is unique per
# caller by construction, makes that collision impossible rather than detectable.
$LogFile = "$Out.log"
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Phase 1: source -> IR-CCE, or take IR already compiled by the caller.
# -Ir is what build/plug-oracle-test.ps1 passes: the harness compiles the
# subject once and feeds the same IR to every plug, so re-compiling here would
# grade a different program than the other arms.
$IrFile = "$Out.ir"
if ($Ir) {
    if (-not (Test-Path -PathType Leaf $Ir)) { [Console]::Error.WriteLine("MISSING: -Ir $Ir"); exit 3 }
    $IrFile = (Resolve-Path $Ir).Path
} elseif ($Src) {
# text-plug: this plug resolves a Codex call by its NAME -- ISA-shaped target,
# by-name resolution -- so the inline passes must not substitute a body and
# delete the call. See text-plug-ir-pipeline in codex/compiler/IR/Passes.codex.
# -Kernel matters here even though this phase only produces IR. Without it
# compile.ps1 takes whatever build.ps1 last left in build-output, which is not
# the seed and not necessarily the compiler a caller graded its truth arm with:
# the two arms would then come from two different compilers and any
# disagreement could belong to either. build/wasm-e2e.ps1 threads its own.
    $kernelArg = if ($Kernel) { @('-Kernel', $Kernel) } else { @() }
    $decksArg  = if ($Decks -ne 0) { @('-Decks', $Decks) } else { @() }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' @kernelArg @decksArg
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
# A guest that died is not a plug that answered. The runtime prints OUT OF
# MEMORY on the same stream as the WAT, so its message IS the output: the
# non-empty check above passes, the message gets written to -Out, and this
# script printed OK with exit 0 on a run that emitted nothing. Measured
# 2026-08-24, that is exactly how 16.3 MB of compiler IR reported success
# while producing 55 bytes. build/plug-run.ps1 already refuses this class;
# this plug has its own runner and did not.
$raw = [System.IO.File]::ReadAllText($outFile)
foreach ($death in @('OUT OF MEMORY', '!EXC')) {
    if ($raw.Contains($death)) {
        [Console]::Error.WriteLine("FAIL: the plug guest died ($death), so $Out would hold a message and not a module.")
        exit 9
    }
}

$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($lines -join "`n")
$wat = $wat -replace '^[\x00-\x1f]+', ''
[System.IO.File]::WriteAllText($Out, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[wasm-plug] OK: $Out ($($wat.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
