# Build Spark's browser demo: SparkWasm.codex -> IR-CCE -> wasm plug -> WAT -> WASM.
#
# Shorter than apps/fishtank/build-wasm.ps1 on purpose: codex/plugs/wasm/run.ps1
# is the shared service that already does source -> IR -> WAT, and it resolves
# the chapter closure itself, so there is no concat and no chapter list here.
#
#   pwsh apps/spark/build-wasm.ps1
#   pwsh apps/spark/build-wasm.ps1 -Kernel build/output/Sut.cdx
[CmdletBinding()]
param([string]$Kernel = '', [switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$OutDir = Join-Path $PSScriptRoot 'web'
$BuildOut = Join-Path $OutDir 'build-output'
$watFile  = Join-Path $BuildOut 'spark.wat'
$wasmFile = Join-Path $OutDir 'spark.wasm'

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }
New-Item -ItemType Directory -Force -Path $BuildOut | Out-Null

Write-Host '[spark-wasm] source -> WAT via the wasm plug ...'
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\run.ps1') `
    -Src (Join-Path $PSScriptRoot 'SparkWasm.codex') -Out $watFile -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[spark-wasm] FAIL: plug run'; exit 3 }

# The plug emits `opening` as _start and exports nothing else, so the one entry
# the page calls is declared here, the same way apps/fishtank/build-wasm.ps1
# declares its six. The shim narrows the i64 a Codex function returns.
$api = @(
    '  (func $api_render (result i32) (i32.wrap_i64 (call $spark_render)))',
    '  (export "spark_render" (func $api_render))'
) -join "`n"
$wat = [System.IO.File]::ReadAllText($watFile, [System.Text.UTF8Encoding]::new($false))
if ($wat -notmatch '\(func \$spark_render') {
    Write-Host '[spark-wasm] FAIL: the WAT carries no $spark_render to export'
    exit 4
}
$wat = $wat.TrimEnd() -replace '\)\s*$', ($api + "`n)")
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[spark-wasm] WAT: $watFile ($($wat.Length) chars)"

# A builtin with no wasm arm assembles and traps at RUNTIME rather than failing
# here, so the WAT is read for both spellings of the refusal marker. Neither
# alone finds both: five sites emit one and two emit the other.
$refusals = @(Select-String -Path $watFile -Pattern 'no wasm form for|has no form on this target|wasm plug:')
if ($refusals.Count -gt 0) {
    Write-Host "[spark-wasm] FAIL: $($refusals.Count) builtin refusal(s) in the WAT; the demo would trap at runtime"
    exit 5
}

if ($WatOnly) { Write-Host '[spark-wasm] -WatOnly given; stopping at the WAT.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 6
}
# --enable-tail-call: the emitter writes `return_call` in tail position and
# wat2wasm will not assemble one without permission. Tail calls are baseline in
# every major browser.
& wat2wasm --enable-tail-call $watFile -o $wasmFile
# Refusing rather than warning: a warning here leaves the previous run's module
# on disk beside a fresh page, which is how fishtank shipped one four days stale.
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 6 }
Write-Host "[spark-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host '[spark-wasm] done'
exit 0
