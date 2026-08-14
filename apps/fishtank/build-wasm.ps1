# Build FishTank WASM: source -> concat -> IR-CCE -> WASM plug -> WAT -> WASM
# Then assemble the HTML page via the page emitter.
# Usage: pwsh apps/FishTank/build-wasm.ps1
[CmdletBinding()]
param([switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugDir   = Join-Path $Repo 'codex' 'plugs' 'wasm'
$OutDir    = Join-Path $PSScriptRoot 'web'
$PlugCdx   = Join-Path $PlugDir 'build-output' 'wasm-plug.cdx'
$LogFile   = Join-Path $OutDir 'build-wasm.log'

if (-not (Test-Path $PlugCdx)) {
    Write-Error "MISSING: $PlugCdx (run codex/plugs/wasm/build.ps1 first)"
    exit 2
}

. (Join-Path $Repo 'build' 'vm-config.ps1')

# -- Concatenate FishTank WASM source files --
$lines = [System.Collections.Generic.List[string]]::new()
$srcFiles = @(
    (Join-Path $PSScriptRoot 'FishTankWasm.codex')
)
foreach ($f in $srcFiles) {
    if (-not (Test-Path $f)) { Write-Error "MISSING: $f"; exit 3 }
    foreach ($l in [System.IO.File]::ReadAllLines($f)) {
        if ($l -match '^\s*cites\s+FishTank\s+chapter') { continue }
        $lines.Add($l)
    }
    $lines.Add(''); $lines.Add('')
}

# -- Cite resolution for foreword deps --
$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Math' = 'codex\foreword\math'
}
$citePat = '^\s*cites\s+(Foreword|Math)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen = @{}
foreach ($l in $lines) {
    if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
}
$ordered = @()
while ($queue.Count -gt 0) {
    $cite = $queue.Dequeue()
    $key = "$($cite.Quire)::$($cite.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $fwPath = Join-Path $Repo (Join-Path $QuireDirs[$cite.Quire] "$($cite.Name).codex")
    if (-not (Test-Path -PathType Leaf $fwPath)) {
        Write-Warning "MISSING: cited $($cite.Quire) chapter '$($cite.Name)'"
        continue
    }
    foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Path = $fwPath }
}
[array]::Reverse($ordered)

$preLines = [System.Collections.Generic.List[string]]::new()
$emitted = @{}
foreach ($entry in $ordered) {
    $key = "$($entry.Quire)::$($entry.Name)"
    if ($emitted[$key]) { continue }
    $emitted[$key] = $true
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($entry.Path)) {
        if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $preLines.Add("Chapter: $($entry.Quire)--$($matches[1])")
            $renamed = $true
        } else { $preLines.Add($l) }
    }
    $preLines.Add(''); $preLines.Add('')
}

$bundleSrc = Join-Path $OutDir 'build-output' 'fishtank-wasm-bundle.codex'
New-Item -ItemType Directory -Force -Path (Split-Path $bundleSrc) | Out-Null
$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($bundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[fishtank-wasm] bundled $($preLines.Count + $lines.Count) lines ($($body.Length) bytes)"

# -- Phase 1: source -> IR-CCE --
$IrFile = Join-Path $OutDir 'build-output' 'fishtank.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $bundleSrc -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) {
    Write-Error "FAIL: IR compile; see $LogFile"
    Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    exit 4
}
Write-Host "[fishtank-wasm] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT via WASM plug --
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0
[System.IO.File]::WriteAllBytes($inputFile, $combined)

$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem','3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(600000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Error "FAIL: plug timeout"; exit 5 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    Write-Error "FAIL: plug produced no output"
    if ($err -match 'EXC') { Write-Host $err.Substring(0, [Math]::Min(500, $err.Length)) }
    exit 6
}

$raw = [System.IO.File]::ReadAllText($outFile)
$watLines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($watLines -join "`n") -replace '^[\x00-\x1f]+', ''
$watFile = Join-Path $OutDir 'build-output' 'fishtank.wat'
$apiExports = @(
    '  (func $api_init (result i32) (i32.wrap_i64 (call $init_aquarium)))',
    '  (func $api_tick (result i32) (i32.wrap_i64 (call $tick)))',
    '  (func $api_spawn_fish (param $sp i32) (result i32) (i32.wrap_i64 (call $spawn_one_fish (i64.extend_i32_s (local.get $sp)))))',
    '  (func $api_spawn_food (result i32) (i32.wrap_i64 (call $spawn_food)))',
    '  (func $api_scatter (result i32) (i32.wrap_i64 (call $scatter)))',
    '  (func $api_toggle_light (result i32) (i32.wrap_i64 (call $toggle_light)))',
    '  (export "init_aquarium" (func $api_init))',
    '  (export "tick" (func $api_tick))',
    '  (export "spawn_one_fish" (func $api_spawn_fish))',
    '  (export "spawn_food" (func $api_spawn_food))',
    '  (export "scatter" (func $api_scatter))',
    '  (export "toggle_light" (func $api_toggle_light))'
)
# Fix signed reads: peek-32 compiles to unsigned extend, but we need signed for negative positions
$wat = $wat -replace 'i64\.extend_i32_u \(i32\.load', 'i64.extend_i32_s (i32.load'
$wat = $wat.TrimEnd() -replace '\)\s*$', (($apiExports -join "`n") + "`n)")
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[fishtank-wasm] WAT: $watFile ($($wat.Length) chars)"

Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue

# -- Phase 3: WAT -> WASM --
if (-not $WatOnly) {
    $wat2wasm = Get-Command 'wat2wasm' -ErrorAction SilentlyContinue
    if ($wat2wasm) {
        $wasmFile = Join-Path $OutDir 'fishtank.wasm'
        & wat2wasm $watFile -o $wasmFile
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[fishtank-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
        } else { Write-Warning "wat2wasm failed; WAT is still available at $watFile" }
    } else {
        Write-Host "[fishtank-wasm] wat2wasm not found; WAT ready at $watFile"
    }
}

# -- Phase 4: Assemble HTML page --
Write-Host "[fishtank-wasm] Assembling HTML page via FishTankWasmPage emitter..."
$pageSrcFiles = @(
    (Join-Path $PSScriptRoot 'FishTankCss.codex'),
    (Join-Path $PSScriptRoot 'FishTankWasmBridge.codex'),
    (Join-Path $PSScriptRoot 'FishTankWasmPage.codex')
)
$pageLines = [System.Collections.Generic.List[string]]::new()
foreach ($f in $pageSrcFiles) {
    if (-not (Test-Path $f)) { Write-Warning "MISSING: $f (skipping HTML assembly)"; exit 0 }
    foreach ($l in [System.IO.File]::ReadAllLines($f)) {
        if ($l -match '^\s*cites\s+FishTank\s+chapter') { continue }
        $pageLines.Add($l)
    }
    $pageLines.Add(''); $pageLines.Add('')
}
$pageBundleSrc = Join-Path $OutDir 'build-output' 'fishtank-page-bundle.codex'
$pageBody = ($pageLines -join "`n") + "`n"
[System.IO.File]::WriteAllText($pageBundleSrc, $pageBody, [System.Text.UTF8Encoding]::new($false))

$pageCdx = Join-Path $OutDir 'build-output' 'fishtank-page.cdx'
$pageLog = Join-Path $OutDir 'build-page.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $pageBundleSrc -Out $pageCdx -Log $pageLog
if ($LASTEXITCODE -ne 0) {
    Write-Warning "HTML assembly compile failed; see $pageLog"
} else {
    Write-Host "[fishtank-wasm] Page CDX: $pageCdx ($((Get-Item $pageCdx).Length) bytes)"
    $rawOut = [System.IO.Path]::GetTempFileName()
    # Capture straight from codex-vm, NOT through build/test-run.ps1. That
    # script strips every CR from the stream (test-run.ps1:100) because a
    # serial test wants CRLF normalised -- but CCE 'e' IS code 13, so routing
    # a CCE payload through it deletes every 'e' in the page: <head> shipped
    # as <had> and <title> as <titl>. It also reads the stream with
    # ReadAllText, which mangles the multi-byte sequences ConvertFrom-CceBytes
    # below exists to decode.
    & $script:CodexVmBin -kernel $pageCdx -output $rawOut -mem 3072 -headless | Out-Null
    # codex-vm returns a non-zero process exit even on a clean run, which is
    # why test-run.ps1 gates on the output file rather than the exit code.
    if ((-not (Test-Path -PathType Leaf $rawOut)) -or (Get-Item $rawOut).Length -eq 0) {
        Write-Warning "HTML assembly run failed"
    } else {
        $rawBytes = [System.IO.File]::ReadAllBytes($rawOut)
        # The serial stream opens with a SOH; drop just that one byte.
        if ($rawBytes.Length -gt 0 -and $rawBytes[0] -eq 1) {
            $rawBytes = $rawBytes[1..($rawBytes.Length - 1)]
        }
        $enc = [System.Text.UTF8Encoding]::new($false)
        # The page is a CCE stream. Decoding it a byte at a time turned every
        # character above tier 0 into '?' in the shipped HTML; ConvertFrom-CceBytes
        # reads the multi-byte sequences.
        $outBytes = [System.Collections.Generic.List[byte]]::new($rawBytes.Length * 3)
        $outBytes.AddRange($enc.GetBytes((ConvertFrom-CceBytes $rawBytes)))
        $pageOut = Join-Path $OutDir 'fishtank-wasm.html'
        [System.IO.File]::WriteAllBytes($pageOut, $outBytes.ToArray())
        Write-Host "[fishtank-wasm] HTML: $pageOut ($((Get-Item $pageOut).Length) bytes)"
    }
    Remove-Item $rawOut -Force -ErrorAction SilentlyContinue
}

Write-Host "[fishtank-wasm] done"
# codex-vm leaves a non-zero LASTEXITCODE behind even on a clean run, and it is
# the last thing this script invokes. Failures above are reported with
# Write-Warning, not an exit code, so say so explicitly rather than handing the
# caller the VM's.
exit 0
