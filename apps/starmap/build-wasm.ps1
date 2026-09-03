# Build the star map's browser module: StarMapWasm.codex -> IR-CCE -> wasm plug
# -> WAT -> WASM.
#
# Shaped after apps/spark/build-wasm.ps1: codex/plugs/wasm/run.ps1 resolves the
# chapter closure itself, so there is no concat and no chapter list here.
#
#   pwsh apps/starmap/build-wasm.ps1
#   pwsh apps/starmap/build-wasm.ps1 -Kernel build/output/Sut.cdx
[CmdletBinding()]
param([string]$Kernel = '', [switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$OutDir   = Join-Path $PSScriptRoot 'web'
$BuildOut = Join-Path $OutDir 'build-output'
$watFile  = Join-Path $BuildOut 'starmap.wat'
$wasmFile = Join-Path $OutDir 'starmap.wasm'

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }
New-Item -ItemType Directory -Force -Path $BuildOut | Out-Null

Write-Host '[starmap-wasm] source -> WAT via the wasm plug ...'
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\run.ps1') `
    -Src (Join-Path $PSScriptRoot 'StarMapWasm.codex') -Out $watFile -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[starmap-wasm] FAIL: plug run'; exit 3 }

# The plug exports only what its own baked wasm-export-list names, and none of
# these are in it, so the page's entry points are declared here the way spark
# declares its one. Codex functions take and answer i64; the shims narrow.
$api = @(
    @{ n = 'sm_tick';                   a = 1 },
    @{ n = 'sm_orbit';                  a = 2 },
    @{ n = 'sm_zoom';                   a = 1 },
    @{ n = 'sm_move_forward';           a = 1 },
    @{ n = 'sm_move_right';             a = 1 },
    @{ n = 'sm_move_up';                a = 1 },
    @{ n = 'sm_set_speed';              a = 1 },
    @{ n = 'sm_select_obj';             a = 1 },
    @{ n = 'sm_fly_to';                 a = 1 },
    @{ n = 'sm_set_mag_limit';          a = 1 },
    @{ n = 'sm_toggle_labels';          a = 0 },
    @{ n = 'sm_toggle_grid';            a = 0 },
    @{ n = 'sm_toggle_constellations';  a = 0 },
    @{ n = 'sm_get_star_count';         a = 0 },
    @{ n = 'sm_get_visible_count';      a = 0 },
    @{ n = 'sm_get_label_count';        a = 0 }
)

$wat = [System.IO.File]::ReadAllText($watFile, [System.Text.UTF8Encoding]::new($false))

$missing = @($api | Where-Object { $wat -notmatch ('\(func \$' + $_.n + '[ \r\n\)]') })
if ($missing.Count -gt 0) {
    Write-Host ('[starmap-wasm] FAIL: the WAT carries no ' + (($missing | ForEach-Object { '$' + $_.n }) -join ', '))
    exit 4
}

$lines = foreach ($f in $api) {
    $idx     = if ($f.a -gt 0) { 0..($f.a - 1) } else { @() }
    $params  = ($idx | ForEach-Object { "(param `$p$_ i32)" }) -join ' '
    $actuals = ($idx | ForEach-Object { "(i64.extend_i32_s (local.get `$p$_))" }) -join ' '
    "  (func `$api_$($f.n) $params (result i32) (i32.wrap_i64 (call `$$($f.n) $actuals)))"
    "  (export `"$($f.n)`" (func `$api_$($f.n)))"
}
$wat = $wat.TrimEnd() -replace '\)\s*$', (($lines -join "`n") + "`n)")
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[starmap-wasm] WAT: $watFile ($($wat.Length) chars, $($api.Count) exports)"

# A builtin with no wasm arm assembles and traps at RUNTIME rather than failing
# here, so both spellings of the refusal marker are read. Neither alone finds
# both: five sites emit one and two emit the other.
$refusals = @(Select-String -Path $watFile -Pattern 'no wasm form for|has no form on this target|wasm plug:')
if ($refusals.Count -gt 0) {
    Write-Host "[starmap-wasm] FAIL: $($refusals.Count) builtin refusal(s) in the WAT; the page would trap at runtime"
    $refusals | Select-Object -First 8 | ForEach-Object { Write-Host ('    ' + $_.Line.Trim()) }
    exit 5
}

if ($WatOnly) { Write-Host '[starmap-wasm] -WatOnly given; stopping at the WAT.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 6
}
# --enable-tail-call: the emitter writes `return_call` in tail position and
# wat2wasm will not assemble one without permission.
& wat2wasm --enable-tail-call $watFile -o $wasmFile
# Refusing rather than warning: a warning here leaves the previous run's module
# on disk beside a fresh page, which is how fishtank shipped one four days stale.
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 6 }
Write-Host "[starmap-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host '[starmap-wasm] done'
exit 0
