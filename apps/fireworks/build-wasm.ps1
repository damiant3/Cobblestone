# Build the fireworks show module: FireworksShow.codex -> IR-CCE -> wasm plug
# -> WAT -> wat2wasm -> web/fireworks-show.wasm
#
# The page draws a skyline it did not compose: the geometry is this module.
# Same three phases apps/fishtank/build-wasm.ps1 uses, and the same reasons
# behind each of them, minus that script's page-assembly phase.
[CmdletBinding()]
param([switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugCdx = Join-Path $Repo 'codex\plugs\wasm\build-output\wasm-plug.cdx'
$OutDir  = Join-Path $PSScriptRoot 'web'
$BuildDir = Join-Path $OutDir 'build-output'
$LogFile = Join-Path $BuildDir 'build-wasm.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx (run codex/plugs/wasm/build.ps1 first)")
    exit 2
}
New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
. (Join-Path $Repo 'build' 'vm-config.ps1')

# -- Cite resolution, depth first, so a cited chapter is emitted before the
# -- chapter that cites it. The show chapter is self-contained today; this is
# -- here so adding a `cites Foreword chapter ...` line does not silently
# -- produce a bundle missing the definitions it names.
$QuireDirs = @{ 'Foreword' = 'codex\foreword\core'; 'Math' = 'codex\foreword\math' }
$citePat = '^\s*cites\s+(Foreword|Math)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$src = Join-Path $PSScriptRoot 'FireworksShow.codex'
if (-not (Test-Path -PathType Leaf $src)) { [Console]::Error.WriteLine("MISSING: $src"); exit 3 }
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($l in [System.IO.File]::ReadAllLines($src)) { $lines.Add($l) }

$ordered = @(); $seen = @{}
$queue = [System.Collections.Generic.Queue[hashtable]]::new()
foreach ($l in $lines) { if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) } }
while ($queue.Count -gt 0) {
    $c = $queue.Dequeue(); $key = "$($c.Quire)::$($c.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $p = Join-Path $Repo (Join-Path $QuireDirs[$c.Quire] "$($c.Name).codex")
    if (-not (Test-Path -PathType Leaf $p)) { [Console]::Error.WriteLine("MISSING: cited $($c.Quire) chapter '$($c.Name)'"); exit 3 }
    foreach ($l in [System.IO.File]::ReadAllLines($p)) { if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) } }
    $ordered += @{ Quire = $c.Quire; Name = $c.Name; Path = $p }
}
[array]::Reverse($ordered)

$pre = [System.Collections.Generic.List[string]]::new()
foreach ($e in $ordered) {
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($e.Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') { $pre.Add("Chapter: $($e.Quire)--$($matches[1])"); $renamed = $true }
        else { $pre.Add($l) }
    }
    $pre.Add(''); $pre.Add('')
}

$bundle = Join-Path $BuildDir 'fireworks-show-bundle.codex'
$body = (($pre + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($bundle, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[fireworks-wasm] bundled $($pre.Count + $lines.Count) lines ($($body.Length) bytes)"

# -- Phase 1: source -> IR-CCE. -Kernel names the compiler; without it
# -- compile.ps1 takes whatever build.ps1 last staged.
$ir = Join-Path $BuildDir 'fireworks-show.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $bundle -Out $ir -Log $LogFile -IrCce -Kernel (Join-Path $Repo 'seed\Codex.cdx')
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR compile; see $LogFile")
    Get-Content $LogFile -EA SilentlyContinue | Where-Object { $_ -match 'error' } | Select-Object -First 12 | ForEach-Object { [Console]::Error.WriteLine("  $_") }
    exit 4
}
Write-Host "[fireworks-wasm] IR: $((Get-Item $ir).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT through the plug
$irBytes = [System.IO.File]::ReadAllBytes($ir)
$inFile = [System.IO.Path]::GetTempFileName()
$hdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) { $u = [int]$ch; if ($u -lt 256) { $hdr.Add([byte]$script:UnicodeToCce[$u]) } }
$hdr.Add([byte]1)
$h = $hdr.ToArray()
$combined = New-Object byte[] ($h.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($h, 0, $combined, 0, $h.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $h.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0
[System.IO.File]::WriteAllBytes($inFile, $combined)

$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$ok = Invoke-PlugVmFileSerial -Kernel $PlugCdx -InputFile $inFile -OutputFile $outFile -StderrFile $errFile -MemMB 3072 -TimeoutSec 600
if (-not $ok) { [Console]::Error.WriteLine('FAIL: plug timeout'); exit 5 }
if ((-not (Test-Path $outFile)) -or (Get-Item $outFile).Length -eq 0) {
    [Console]::Error.WriteLine('FAIL: plug produced no output')
    if (Test-Path $errFile) { $e = Get-Content $errFile -Raw; if ($e -match 'EXC') { [Console]::Error.WriteLine($e.Substring(0, [Math]::Min(400, $e.Length))) } }
    exit 6
}

$raw = [System.IO.File]::ReadAllText($outFile)
$wat = (($raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }) -join "`n") -replace '^[\x00-\x1f]+', ''

# peek-32 compiles to an UNSIGNED extend, and a coordinate can be negative
# (a building starts left of the screen). Same substitution, same reason, as
# apps/fishtank/build-wasm.ps1.
$wat = $wat -replace 'i64\.extend_i32_u \(i32\.load', 'i64.extend_i32_s (i32.load'

# Codex Integer is 64-bit and the JS side wants i32, so each export is wrapped.
$api = @(
    '  (func $api_build_city (param $c i32) (result i32) (i32.wrap_i64 (call $build_city (i64.extend_i32_s (local.get $c)))))',
    '  (export "build_city" (func $api_build_city))'
)
$wat = $wat.TrimEnd() -replace '\)\s*$', (($api -join "`n") + "`n)")
$watFile = Join-Path $BuildDir 'fireworks-show.wat'
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[fireworks-wasm] WAT: $watFile ($($wat.Length) chars)"
Remove-Item $inFile,$outFile,$errFile -Force -EA SilentlyContinue

# -- Phase 3: WAT -> WASM
if (-not $WatOnly) {
    if (-not (Get-Command wat2wasm -EA SilentlyContinue)) {
        [Console]::Error.WriteLine("FAIL: wat2wasm is not on the Path; WAT is at $watFile"); exit 7
    }
    $wasm = Join-Path $OutDir 'fireworks-show.wasm'
    # --enable-tail-call: the emitter writes return_call for a tail position.
    & wat2wasm --enable-tail-call $watFile -o $wasm
    # A warning here used to leave the previous run's module on disk beside a
    # fresh page and still print done, which is how a stale module ships.
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: wat2wasm; WAT is at $watFile"); exit 7 }
    Write-Host "[fireworks-wasm] WASM: $wasm ($((Get-Item $wasm).Length) bytes)"
}
exit 0
