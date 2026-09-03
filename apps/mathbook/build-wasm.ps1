# Compile the notebook's evaluator to a wasm module for the landing site.
#
#   MathbookWasm.codex -> IR-CCE -> codex/plugs/wasm -> WAT -> wat2wasm -> .wasm
#
# Unlike apps/games and apps/c64 this module needs no export wrappers. Those
# export i32 functions and are handed throwing WASI stubs, because their state
# is integers. A CAS answers TEXT, and a Codex `Text` is a pointer to a
# four-byte length followed by CCE bytes, which a page cannot read without a
# CCE table. So this takes the compiler page's shape instead: a WASI program,
# expression in on `fd_read`, answer out on `fd_write`, driven by the `_start`
# the emitter already exports. Both directions are plain UTF-8 because the
# foreword converts at the I/O boundary (R-CCE), so the page needs no codec.
# The working precedent is codex/plugs/wasm/page-workspace-arm.js.
#
# If the module fails to build, that is a PARITY finding for the wasm plug lane
# (reek), not something to work around here.
#
# Usage: pwsh apps/mathbook/build-wasm.ps1 [-WatOnly] [-Kernel <cdx>]
[CmdletBinding()]
param(
    [switch]$WatOnly,
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugCdx = Join-Path $Repo 'codex\plugs\wasm\build-output\wasm-plug.cdx'
$OutDir  = Join-Path $Repo 'apps\landing\web\mathbook'
$WorkDir = Join-Path $PSScriptRoot 'build-output'
$Chapter = Join-Path $PSScriptRoot 'MathbookWasm.codex'

if (-not (Test-Path -PathType Leaf $Chapter)) { Write-Host "REFUSE: missing $Chapter"; exit 2 }
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "REFUSE: no wasm plug at $PlugCdx (run codex/plugs/wasm/build.ps1 first)"; exit 2
}
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }

. (Join-Path $Repo 'build\vm-config.ps1')
New-Item -ItemType Directory -Force -Path $WorkDir, $OutDir | Out-Null

# -- Phase 1: source -> IR-CCE ----------------------------------------
$irFile  = Join-Path $WorkDir 'mathbook.ir'
$logFile = Join-Path $WorkDir 'mathbook-compile.log'
Write-Host '[mathbook-wasm] compiling MathbookWasm.codex to IR ...'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Chapter -Out $irFile -Log $logFile -IrCce -Kernel $Kernel
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $irFile)) {
    Write-Host "FAIL: IR compile; see $logFile"
    Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" }
    exit 3
}
Write-Host "[mathbook-wasm] IR: $((Get-Item $irFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT through the wasm plug -------------------------
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
Write-Host '[mathbook-wasm] running the wasm plug ...'
$proc = Start-Process -FilePath $vmBin -ArgumentList @(
    '-kernel', $PlugCdx, '-input', $inputFile, '-output', $outFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(900000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host 'FAIL: plug timeout'; exit 4 }

# codex-vm prints 'DROPPED ... is SHORT' on a clean exit when the serial
# capture lost bytes (L-SHORT). A truncated WAT and a wrong WAT are the same
# colour on a verdict line, so read it here rather than let a short module
# reach wat2wasm as a syntax error.
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

# The page drives this module through _start and the two WASI descriptors and
# nothing else. If any of the three stopped being emitted the page would fail
# at instantiation with a message about imports rather than about the module,
# so name them here instead.
foreach ($needed in '(export "_start"', 'fd_write', 'fd_read') {
    if (-not $wat.Contains($needed)) {
        Write-Host "FAIL: the module does not carry $needed; the page cannot drive it."
        exit 6
    }
}

$watFile = Join-Path $WorkDir 'mathbook.wat'
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[mathbook-wasm] WAT: $watFile ($($wat.Length) chars)"

# -- Phase 3: WAT -> WASM ---------------------------------------------
if ($WatOnly) { Write-Host '[mathbook-wasm] -WatOnly given; stopping.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 7
}
$wasmFile = Join-Path $OutDir 'mathbook.wasm'
# --enable-tail-call: the emitter writes `return_call` for a tail position.
& wat2wasm --enable-tail-call $watFile -o $wasmFile
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 7 }
Write-Host "[mathbook-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host '[mathbook-wasm] done'
exit 0
