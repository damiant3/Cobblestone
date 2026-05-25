# Build plugs/html/build-output/html-plug.cdx
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$OutFile   = Join-Path $OutDir 'html-plug.cdx'
$BundleSrc = Join-Path $OutDir 'plug-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'
$Stage0    = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'

if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0"); exit 2
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path, [string[]]$StripCites = @())
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        $skip = $false
        foreach ($sc in $StripCites) { if ($l -match "cites.*$sc") { $skip = $true } }
        if (-not $skip) { $lines.Add($l) }
    }
    $lines.Add(''); $lines.Add('')
}

Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\PlugTypes.codex')
Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\IRTextParser.codex')
Add-Chapter -Path (Join-Path $PlugDir 'HtmlEmitter.codex')
Add-Chapter -Path (Join-Path $PlugDir 'HtmlPlug.codex')

# ── Resolve foreword cites ───────────────────────────────────────────
$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|Games|Spark|Data)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
    'Magic' = 'apps\games\magic'; 'Games' = 'apps\games\classic'
    'Spark' = 'apps\spark'; 'Data' = 'apps\data'
}
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
        [Console]::Error.WriteLine("MISSING: cited $($cite.Quire) chapter '$($cite.Name)' (expected $fwPath)")
        exit 3
    }
    foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Path = $fwPath }
}
[array]::Reverse($ordered)
$emitted = @{}
$preLines = [System.Collections.Generic.List[string]]::new()
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

$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[html-plug] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

# ── Compile via seed CDX ─────────────────────────────────────────────
$run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 60 -MemMB 4096
if (-not $run) { [Console]::Error.WriteLine("FAIL: QEMU did not start"); exit 4 }
try {
    if (-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30)) { [Console]::Error.WriteLine("FAIL: no READY"); exit 4 }
    $stream = $run.Conn.Data.GetStream()
    $stream.Write([System.Text.Encoding]::UTF8.GetBytes("CDX`n"), 0, 4)
    $srcBytes = [System.IO.File]::ReadAllBytes($BundleSrc)
    $stream.Write($srcBytes, 0, $srcBytes.Length)
    $stream.WriteByte(4); $stream.Flush()
    Set-Content -Path $LogFile -Value '' -Encoding UTF8
    $binSize = 0
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 180
        if ($null -eq $line) { break }
        if ($line.StartsWith('SIZE:')) { if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }; break }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            Add-Content -Path $LogFile -Value $line -Encoding UTF8
            while ($true) { $l2 = Read-StreamLine -Stream $stream -TimeoutSec 5; if ($null -eq $l2) { break }; Add-Content -Path $LogFile -Value $l2 -Encoding UTF8; if ($l2.StartsWith('HEAP:')) { break } }
            [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile"); exit 5
        }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    }
    if ($binSize -le 0) { [Console]::Error.WriteLine("FAIL: no SIZE; see $LogFile"); exit 6 }
    $bytes = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec 300
    if ($null -eq $bytes -or $bytes.Length -ne $binSize) { [Console]::Error.WriteLine("FAIL: incomplete read"); exit 7 }
    [System.IO.File]::WriteAllBytes($OutFile, $bytes)
    Write-Host "[html-plug] OK: $OutFile ($binSize bytes)"
    exit 0
} finally {
    Close-Vm -Conn $run.Conn -Process $run.Process
    Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
}
