# Build Spark WebGPU: source -> concat -> IR-CCE -> WASM plug -> WAT
# Usage: pwsh codex/plugs/wasm/build-spark.ps1
[CmdletBinding()]
param([string]$Src, [switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$PlugCdx   = Join-Path $OutDir 'wasm-plug.cdx'
$LogFile   = Join-Path $OutDir 'spark-build.log'

if (-not $Src) { $Src = Join-Path $OutDir 'spark-webgpu.codex' }
if (-not (Test-Path $Src)) { Write-Error "MISSING: $Src"; exit 1 }
if (-not (Test-Path $PlugCdx)) { Write-Error "MISSING: $PlugCdx (run build.ps1 first)"; exit 2 }

. (Join-Path $Repo 'build' 'vm-config.ps1')

# -- Cite resolution (same pattern as build.ps1) --
$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
    'Spark' = 'apps\spark'
}

$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Spark)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

$srcLines = [System.IO.File]::ReadAllLines((Resolve-Path $Src).Path)

$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen = @{}
foreach ($l in $srcLines) {
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
        Write-Error "MISSING: cited $($cite.Quire) chapter '$($cite.Name)' (expected $fwPath)"
        exit 3
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

$bundleSrc = Join-Path $OutDir 'spark-bundle.codex'
$body = (($preLines + [System.Collections.Generic.List[string]]::new($srcLines)) -join "`n") + "`n"
[System.IO.File]::WriteAllText($bundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[spark] bundled $($preLines.Count + $srcLines.Length) lines ($($body.Length) bytes), $($ordered.Count) cited chapters"

# -- Phase 1: source -> IR-CCE --
$IrFile = Join-Path $OutDir 'spark.ir'
# Survey overrides for Spark: higher type density than compiler source.
# check-mul raised from default 400 to 800 (Spark has ~10x record definitions per KB).
# lower-mul raised from 300 to 500. headroom at 150% (default 120%).
# These scale with source size so no manual tuning as source grows.
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $bundleSrc -Out $IrFile -Log $LogFile -IrCce -Survey "check-mul:800,lower-mul:500,headroom:150" -MemMB 4096
if ($LASTEXITCODE -ne 0) {
    Write-Error "FAIL: IR compile; see $LogFile"
    Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    exit 4
}
Write-Host "[spark] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

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
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem','4096','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Error "FAIL: plug timeout"; exit 5 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    Write-Error "FAIL: plug produced no output"
    if ($err -match 'EXC') { Write-Host $err.Substring(0, [Math]::Min(500, $err.Length)) }
    exit 6
}

$raw = [System.IO.File]::ReadAllText($outFile)
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($lines -join "`n") -replace '^[\x00-\x1f]+', ''
$watFile = Join-Path $OutDir 'spark-webgpu.wat'
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[spark] WAT: $watFile ($($wat.Length) chars)"

Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue

# -- Phase 3: WAT -> WASM (if wat2wasm available) --
if (-not $WatOnly) {
    $wat2wasm = Get-Command 'wat2wasm' -ErrorAction SilentlyContinue
    if ($wat2wasm) {
        $wasmFile = Join-Path $OutDir 'spark-webgpu.wasm'
        & wat2wasm $watFile -o $wasmFile
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[spark] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
        } else { Write-Warning "wat2wasm failed; WAT is still available at $watFile" }
    } else {
        Write-Host "[spark] wat2wasm not found; WAT ready at $watFile"
        Write-Host "  Install: npm install -g wabt  (or download from github.com/WebAssembly/wabt)"
    }
}

Write-Host "[spark] done"
