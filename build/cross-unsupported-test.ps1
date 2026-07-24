# cross-unsupported-test.ps1 -- a call the target cannot serve is refused,
# not answered with a placeholder.
#
# arm64 and riscv have no filesystem: there is no block driver behind a file
# read on either lane. The plugs used to emit the EMPTY STRING for one, so a
# caller that named a path got "" back and could not tell that apart from an
# empty file. This pins the refusal.
#
# The control is the point. A harness that only checks the probe is refused
# passes just as well against a plug that refuses EVERYTHING, which would be a
# far worse regression than the one being fixed. So a second program with no
# file read in it must still build on both lanes.
#
# Usage: build/cross-unsupported-test.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out  = Join-Path $Repo 'build-output\cross-unsupported'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$Probe   = Join-Path $Repo 'codex\test\cross-unsupported-probe.codex'
$Control = Join-Path $Repo 'bench\codex\fib.codex'

function Compile-Ir {
    param([string]$Src, [string]$Name)
    $ir  = Join-Path $Out "$Name.ir"
    $log = Join-Path $Out "$Name.log"
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $ir -Log $log -IrCce | Out-Null
    if (-not (Test-Path $ir)) {
        Write-Host "cross-unsupported: FAILED to produce IR for $Name; see $log"
        exit 1
    }
    return $ir
}

$probeIr   = Compile-Ir -Src $Probe   -Name 'probe'
$controlIr = Compile-Ir -Src $Control -Name 'control'

$fail = 0
foreach ($arch in @('arm64','riscv')) {
    $run = Join-Path $Repo "codex\plugs\$arch\run.ps1"

    # 1. the probe must be REFUSED, and must name the call it cannot serve
    $bin = Join-Path $Out "probe.$arch.bin"
    # NOT $out: PowerShell variables are case-insensitive and $Out is the
    # output directory two lines down. That collision made this harness fail
    # inside itself before it measured anything.
    $runOut = & pwsh -NoProfile -File $run -IrInput $probeIr -Out $bin 2>&1 | Out-String
    $code = $LASTEXITCODE
    $named = $runOut -match '\[UNSUPPORTED\]\s*read-file-raw'
    if ($code -eq 0) {
        Write-Host "cross-unsupported: FAIL ($arch) -- the probe built; a file read was served silently"
        $fail++
    } elseif (-not $named) {
        Write-Host "cross-unsupported: FAIL ($arch) -- refused (exit $code) but did not name read-file-raw"
        $fail++
    } else {
        Write-Host "cross-unsupported: OK ($arch) -- refused, named read-file-raw"
    }

    # 2. the control must still BUILD
    $cbin = Join-Path $Out "control.$arch.bin"
    & pwsh -NoProfile -File $run -IrInput $controlIr -Out $cbin 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "cross-unsupported: FAIL ($arch) -- the control was refused too (exit $LASTEXITCODE); the plug refuses everything"
        $fail++
    } else {
        Write-Host "cross-unsupported: OK ($arch) -- control still builds"
    }
}

if ($fail -gt 0) { Write-Host "cross-unsupported: $fail failure(s)"; exit 1 }
Write-Host "cross-unsupported: OK (both lanes refuse the read and build the control)"
exit 0
