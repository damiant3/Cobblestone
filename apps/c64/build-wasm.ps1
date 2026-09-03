# Compile the C64 emulator to a wasm module for the landing site.
#
#   C64Wasm.codex -> IR-CCE -> codex/plugs/wasm -> WAT -> wat2wasm -> .wasm
#
# The same pipeline apps/games/build-wasm.ps1 runs, and for the same reasons:
# compile.ps1 resolves every `cites` through build/quire-map.ps1, so there is
# no hand-rolled bundler here and no second copy of the quire table to drift.
# The export wrappers are generated from $Exports below rather than written as
# WAT by hand.
#
# If the module fails to build, that is a PARITY finding for the wasm plug lane
# (reek), not something to work around here: report the failing step and leave
# apps/c64's Codex source alone.
#
# Usage: pwsh apps/c64/build-wasm.ps1 [-WatOnly] [-Kernel <cdx>]
[CmdletBinding()]
param(
    [switch]$WatOnly,
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugCdx = Join-Path $Repo 'codex\plugs\wasm\build-output\wasm-plug.cdx'
$OutDir  = Join-Path $Repo 'apps\landing\web\c64'
$WorkDir = Join-Path $PSScriptRoot 'build-output'
$Chapter = Join-Path $PSScriptRoot 'C64Wasm.codex'

# The whole machine lives at fixed addresses in linear memory, so the page
# holds no state at all: it calls c64_reset once and c64_frame per animation
# frame. c64_screen and c64_pc exist for the grader -- what the machine
# believes it is showing, and where the 6502 actually is, are checkable
# without reading a single pixel.
$Exports = @(
    @{ Name = 'c64_reset';  Fn = 'c64w_reset';        Arity = 1 }
    @{ Name = 'c64_frame';  Fn = 'c64w_frame';        Arity = 1 }
    @{ Name = 'c64_key';    Fn = 'c64w_key';          Arity = 1 }
    @{ Name = 'c64_fb';     Fn = 'c64w_fb_addr';      Arity = 1 }
    @{ Name = 'c64_w';      Fn = 'c64w_frame_width';  Arity = 1 }
    @{ Name = 'c64_h';      Fn = 'c64w_frame_height'; Arity = 1 }
    @{ Name = 'c64_screen'; Fn = 'c64w_screen';       Arity = 1 }
    @{ Name = 'c64_pc';     Fn = 'c64w_pc';           Arity = 1 }
    @{ Name = 'c64_halted'; Fn = 'c64w_halted';       Arity = 1 }
)

if (-not (Test-Path -PathType Leaf $Chapter)) { Write-Host "REFUSE: missing $Chapter"; exit 2 }
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "REFUSE: no wasm plug at $PlugCdx (run codex/plugs/wasm/build.ps1 first)"; exit 2
}
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }

. (Join-Path $Repo 'build\vm-config.ps1')
New-Item -ItemType Directory -Force -Path $WorkDir, $OutDir | Out-Null

# -- Phase 1: source -> IR-CCE ----------------------------------------
$irFile  = Join-Path $WorkDir 'c64.ir'
$logFile = Join-Path $WorkDir 'c64-compile.log'
Write-Host '[c64-wasm] compiling C64Wasm.codex to IR ...'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Chapter -Out $irFile -Log $logFile -IrCce -Kernel $Kernel
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $irFile)) {
    Write-Host "FAIL: IR compile; see $logFile"
    Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" }
    exit 3
}
Write-Host "[c64-wasm] IR: $((Get-Item $irFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT through the wasm plug -------------------------
# The plug reads a CCE mode header, the IR, then a NUL.
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
Write-Host '[c64-wasm] running the wasm plug ...'
$proc = Start-Process -FilePath $vmBin -ArgumentList @(
    '-kernel', $PlugCdx, '-input', $inputFile, '-output', $outFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(900000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host 'FAIL: plug timeout'; exit 4 }

# codex-vm prints 'DROPPED ... is SHORT' on a clean exit when the serial
# capture lost bytes (L-SHORT / L-UNHEARD). A truncated WAT and a wrong WAT
# are the same colour on a verdict line, so read it here rather than let a
# short module reach wat2wasm as a syntax error.
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

if ($wat -match 'CODEGEN-ERRORS|CODEGEN-HALTED|!EXC') {
    Write-Host 'FAIL: the plug refused. First lines:'
    ($wat -split "`n" | Select-Object -First 12) | ForEach-Object { Write-Host "  $_" }
    exit 5
}

# -- Phase 2b: the exported wrappers ----------------------------------
# Codex integers are i64 and JavaScript reads i32 without BigInt, so each
# export wraps: i32 arguments widen signed on the way in, the i64 result wraps
# on the way out. Every value crossing this boundary is an address under 16 MB,
# a scancode, or a screen code, so none of them is near the i32 ceiling.
$missing = @()
foreach ($e in $Exports) {
    if ($wat -notmatch ("(?m)^\s*\(func \\?\$" + [regex]::Escape($e.Fn) + "[\s\)]")) { $missing += $e.Fn }
}
if ($missing.Count -gt 0) {
    Write-Host "FAIL: the module does not define: $($missing -join ', ')"
    Write-Host '  (a name the emitter mangled or dropped, not a bad wrapper -- check the WAT)'
    exit 6
}

$wrappers = [System.Collections.Generic.List[string]]::new()
foreach ($e in $Exports) {
    $params = @(); $callArgs = @()
    for ($i = 0; $i -lt $e.Arity; $i++) {
        $params    += "(param `$a$i i32)"
        $callArgs  += "(i64.extend_i32_s (local.get `$a$i))"
    }
    $ps = if ($params.Count) { ($params -join ' ') + ' ' } else { '' }
    $as = if ($callArgs.Count) { ' ' + ($callArgs -join ' ') } else { '' }
    $wrappers.Add("  (func `$api_$($e.Name) $ps(result i32) (i32.wrap_i64 (call `$$($e.Fn)$as)))")
}
foreach ($e in $Exports) {
    $wrappers.Add("  (export `"$($e.Name)`" (func `$api_$($e.Name)))")
}
$wat = $wat.TrimEnd() -replace '\)\s*$', (($wrappers -join "`n") + "`n)")

$watFile = Join-Path $WorkDir 'c64.wat'
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[c64-wasm] WAT: $watFile ($($wat.Length) chars)"

# The machine band -- RAM, ROMs, colour RAM, the SID/VIC/CIA registers, the
# keyboard ring and the saved 6502 registers -- runs from #A00000 to #A19040.
# The emitter's CCE tables and the bump heap grow up from $heap_start, so if
# that ever reached #A00000 the module would quietly overwrite C64 RAM and the
# emulator would fail in a way no export could report. Read it out of the WAT
# rather than assume it stayed low.
if ($wat -match '\(global \$heap_start i32 \(i32\.const (\d+)\)\)') {
    $heapStart = [int]$matches[1]
    Write-Host ("[c64-wasm] heap_start = 0x{0:X} ({1:N0} bytes); machine band at 0xA00000" -f $heapStart, $heapStart)
    if ($heapStart -ge 0xA00000) {
        Write-Host "FAIL: heap_start 0x$('{0:X}' -f $heapStart) has reached the machine band at 0xA00000."
        exit 8
    }
} else {
    Write-Host 'FAIL: no $heap_start global in the WAT; cannot prove the machine band is clear.'
    exit 8
}

# A frame allocates a CpuState per 6502 instruction, so c64w-frame saves and
# restores the heap pointer. If that restore were ever dropped the bump
# allocator would climb into the machine band after a few seconds of play --
# a corruption that looks like an emulator bug, not a build one. Prove the
# store survived into the emitted function rather than trusting the source.
$frameBody = [regex]::Match($wat, '(?ms)^\s*\(func \\?\$c64w_frame\b.*?(?=^\s*\(func )')
if (-not $frameBody.Success) { Write-Host 'FAIL: no $c64w_frame in the WAT.'; exit 8 }
$restores = ([regex]::Matches($frameBody.Value, 'global\.set \$heap_ptr')).Count
if ($restores -lt 1) {
    Write-Host 'FAIL: c64w_frame carries no `global.set $heap_ptr`; the heap restore was eliminated.'
    exit 8
}
Write-Host "[c64-wasm] heap restore present in c64w_frame ($restores site(s))."

# -- Phase 3: WAT -> WASM ---------------------------------------------
if ($WatOnly) { Write-Host '[c64-wasm] -WatOnly given; stopping.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 7
}
$wasmFile = Join-Path $OutDir 'c64.wasm'
# --enable-tail-call: the emitter writes `return_call` for a tail position and
# wat2wasm will not assemble one without permission.
& wat2wasm --enable-tail-call $watFile -o $wasmFile
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 7 }
Write-Host "[c64-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host '[c64-wasm] done'
exit 0
