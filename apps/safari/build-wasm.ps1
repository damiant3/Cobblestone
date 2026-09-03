# Take the donated safari chapters through the wasm plug.
#
#   SafariIntake.codex -> IR-CCE -> codex/plugs/wasm -> WAT [-> wat2wasm]
#
# This is the intake's stage 3 instrument rather than a page build: the point
# is whether Steve Howell's twenty-seven `port/` chapters survive OUR wasm
# road, and every failure is classified as ours or his against the twenty fork
# commits named in PROVENANCE.md rather than fixed here.
#
# SafariIntake.codex cites all twenty-seven so the compiler reaches every one.
# It is a WASI program, the same shape apps/mathbook and apps/data use, so the
# `_start` / fd_write / fd_read assertions below apply to it unchanged.
#
# THE PLUG BINARY IS THE SUBJECT, NOT THE PLUG SOURCE. This runs
# codex/plugs/wasm/build-output/wasm-plug.cdx, so a source fix that has not
# been rebuilt is not being measured (L-SAMEVER). The refusal below is the
# guard; run codex/plugs/wasm/build.ps1 first.
#
# Usage: pwsh apps/safari/build-wasm.ps1 [-Wasm] [-Kernel <cdx>]
[CmdletBinding()]
param(
    [switch]$Wasm,
    [switch]$Page,
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugCdx = Join-Path $Repo 'codex\plugs\wasm\build-output\wasm-plug.cdx'
$WorkDir = Join-Path $PSScriptRoot 'build-output'
# -Page builds the BROWSER module (exports plus the command buffer); the
# default builds the intake driver (a WASI program that prints counts).
$Chapter = Join-Path $PSScriptRoot $(if ($Page) { 'SafariWasm.codex' } else { 'SafariIntake.codex' })
$Stem    = $(if ($Page) { 'safari-page' } else { 'safari' })

if (-not (Test-Path -PathType Leaf $Chapter)) { Write-Host "REFUSE: missing $Chapter"; exit 2 }
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "REFUSE: no wasm plug at $PlugCdx (run codex/plugs/wasm/build.ps1 first)"; exit 2
}
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }

. (Join-Path $Repo 'build\vm-config.ps1')
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# -- Phase 1: source -> IR-CCE ----------------------------------------
$irFile  = Join-Path $WorkDir ($Stem + '.ir')
$logFile = Join-Path $WorkDir ($Stem + '-compile.log')
Write-Host "[safari-wasm] compiling $(Split-Path $Chapter -Leaf) to IR ..."
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Chapter -Out $irFile -Log $logFile -IrCce -Kernel $Kernel
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $irFile)) {
    Write-Host "FAIL: IR compile; see $logFile"
    Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" }
    exit 3
}
Write-Host "[safari-wasm] IR: $((Get-Item $irFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT through the wasm plug -------------------------
# The plug reads a mode header before the IR: the mode name in CCE bytes, a 1
# separator, the IR, and a trailing 0. Feeding it the bare IR does not fail,
# it HANGS, which reads as a slow plug rather than as a malformed input.
$irBytes = [System.IO.File]::ReadAllBytes($irFile)
$hdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in 'IR-CCE'.ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdr.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdr.Add([byte]1)
$modeHeader = $hdr.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0

$inputFile = [System.IO.Path]::GetTempFileName()
$outFile   = [System.IO.Path]::GetTempFileName()
$errFile   = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($inputFile, $combined)

$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
Write-Host '[safari-wasm] running the wasm plug ...'
$proc = Start-Process -FilePath $vmBin -ArgumentList @(
    '-kernel', $PlugCdx, '-input', $inputFile, '-output', $outFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(900000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host 'FAIL: plug timeout'; exit 4 }

# A truncated WAT and a wrong WAT are the same colour on a verdict line
# (L-SHORT), so read the drop line before reading the content.
$vmErr = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { '' }
if ($vmErr -and $vmErr.Contains('DROPPED')) {
    Write-Host 'FAIL: codex-vm dropped serial output; the WAT is truncated, not wrong.'
    Write-Host ($vmErr -split "`n" | Where-Object { $_.Contains('DROPPED') } | Select-Object -First 3)
    exit 5
}
if (-not (Test-Path -PathType Leaf $outFile) -or (Get-Item $outFile).Length -eq 0) {
    Write-Host 'FAIL: plug produced no output'
    if ($vmErr) { Write-Host $vmErr.Substring(0, [Math]::Min(800, $vmErr.Length)) }
    exit 5
}

$raw = [System.IO.File]::ReadAllText($outFile)
$watLines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($watLines -join "`n") -replace '^[\x00-\x1f]+', ''
Remove-Item $inputFile, $outFile, $errFile -Force -ErrorAction SilentlyContinue

# -- Phase 2b: the exported wrappers, for the page module only --------
# Our wasm plug exports a fixed set (_start, memory, __heap_reset,
# disk_reserve) and has no way for a chapter to declare its own, so the arcade
# convention generates the wrappers into the WAT instead. That is what
# apps/c64/build-wasm.ps1 does and this follows it. Codex integers are i64 and
# JavaScript reads i32 without BigInt, so each export widens its argument
# signed on the way in and wraps the result on the way out; every value
# crossing here is a byte offset under 16 MB or a step index.
if ($Page) {
    $Exports = @(
        @{ Name = 'sfw_render';    Fn = 'sfw_render';    Arity = 1 }
        @{ Name = 'sfw_forward';   Fn = 'sfw_forward';   Arity = 1 }
        @{ Name = 'sfw_reset';     Fn = 'sfw_reset';     Arity = 1 }
        @{ Name = 'sfw_buffer_at'; Fn = 'sfw_buffer_at'; Arity = 1 }
        @{ Name = 'sfw_segments';  Fn = 'sfw_segments';  Arity = 1 }
        @{ Name = 'sfw_heap_probe'; Fn = 'sfw_heap_probe'; Arity = 1 }
    )
    $missing = @()
    foreach ($e in $Exports) {
        if ($wat -notmatch ("(?m)^\s*\(func \\?\$" + [regex]::Escape($e.Fn) + "[\s\)]")) { $missing += $e.Fn }
    }
    if ($missing.Count -gt 0) {
        Write-Host "FAIL: the module does not define: $($missing -join ', ')"
        Write-Host '  (a name the emitter mangled or shook out, not a bad wrapper -- check the WAT)'
        exit 6
    }
    $wrappers = [System.Collections.Generic.List[string]]::new()
    foreach ($e in $Exports) {
        $params = @(); $callArgs = @()
        for ($i = 0; $i -lt $e.Arity; $i++) {
            $params   += "(param `$a$i i32)"
            $callArgs += "(i64.extend_i32_s (local.get `$a$i))"
        }
        $ps = if ($params.Count) { ($params -join ' ') + ' ' } else { '' }
        $as = if ($callArgs.Count) { ' ' + ($callArgs -join ' ') } else { '' }
        $wrappers.Add("  (func `$api_$($e.Name) $ps(result i32) (i32.wrap_i64 (call `$$($e.Fn)$as)))")
    }
    foreach ($e in $Exports) {
        $wrappers.Add("  (export `"$($e.Name)`" (func `$api_$($e.Name)))")
    }
    $wat = $wat.TrimEnd() -replace '\)\s*$', (($wrappers -join "`n") + "`n)")
}

$watFile = Join-Path $WorkDir ($Stem + '.wat')
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))

# A refusal is the EXPECTED outcome of stage 3 as often as not, so it is
# reported with its first lines and the WAT is kept for classification rather
# than deleted.
if ($wat -match 'CODEGEN-ERRORS|CODEGEN-HALTED|!EXC') {
    Write-Host "REFUSED by the plug. WAT kept at $watFile. First lines:"
    ($wat -split "`n" | Select-Object -First 20) | ForEach-Object { Write-Host "  $_" }
    exit 5
}

Write-Host "[safari-wasm] WAT: $watFile ($($wat.Length) chars)"

$required = if ($Page) { @('(export "sfw_render"', '(export "sfw_buffer_at"') } else { @('(export "_start"', 'fd_write', 'fd_read') }
foreach ($needed in $required) {
    if (-not $wat.Contains($needed)) {
        Write-Host "FAIL: the module does not carry $needed."
        exit 6
    }
}

if (-not $Wasm) { Write-Host '[safari-wasm] WAT only; pass -Wasm to assemble.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 7
}
$wasmFile = Join-Path $WorkDir ($Stem + '.wasm')
& wat2wasm --enable-tail-call $watFile -o $wasmFile
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 7 }
Write-Host "[safari-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host '[safari-wasm] done'
exit 0
