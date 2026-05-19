# Build plugs/csharp/build-output/csharp-plug.cdx
#
# Bundles the type-defining subset of codex/ (Core, Types, Ast, IR)
# plus plugs/common/IRTextParser.codex plus plugs/csharp/*.codex
# plus a synthesized opening that calls into CSharpPlug, and feeds
# the lot through the seed CDX in CDX mode to produce csharp-plug.cdx.
#
# The plug binary, once built, reads IR text on stdin (the wire format
# emitted by `codex IR <source>`) and prints C# source to stdout. The
# downstream `dotnet build` step is owned by run.ps1.
#
# Usage:
#   plugs/csharp/build.ps1
#   plugs/csharp/build.ps1 -Force          # ignore stamp; rebuild
#
# Cam — when porting another emitter (ELF / EFI / IMG / CDX) to the
# plug architecture, copy this script into plugs/<target>/build.ps1
# and adjust:
#   $PlugDir   = 'plugs/<target>'
#   $OutFile   = '<target>-plug.cdx'
#   The list of plugs/<target>/*.codex files to bundle.
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$OutFile   = Join-Path $OutDir 'csharp-plug.cdx'
$BundleSrc = Join-Path $OutDir 'plug-source.codex'
$LogFile   = Join-Path $OutDir 'build.log'
$Stage0    = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'

if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0 — run build/build.ps1 first to build the self-host")
    exit 2
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ── Phase 1: bundle source ──────────────────────────────────────────
# Minimal plug: type definitions + IR parser + C# emitter + entry.
# No compiler logic (parser, type checker, lowerer, x86 emitter).
$lines = [System.Collections.Generic.List[string]]::new()

function Add-Chapter {
    param([string]$Path, [string]$Quire, [string[]]$StripCites = @())
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ((-not $renamed) -and $Quire -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: ${Quire}--$($matches[1])")
            $renamed = $true
        } else {
            $skip = $false
            foreach ($sc in $StripCites) { if ($l -match "cites.*$sc") { $skip = $true } }
            if (-not $skip) { $lines.Add($l) }
        }
    }
    $lines.Add('')
    $lines.Add('')
}

Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\PlugTypes.codex')      -Quire ''
Add-Chapter -Path (Join-Path $Repo 'codex\plugs\common\IRTextParser.codex')   -Quire ''
Add-Chapter -Path (Join-Path $PlugDir 'CSharpEmitter.codex')            -Quire ''
Add-Chapter -Path (Join-Path $PlugDir 'CSharpEmitterExpressions.codex') -Quire ''
Add-Chapter -Path (Join-Path $PlugDir 'CSharpPlug.codex')               -Quire ''

# ── Phase 2: resolve foreword cites transitively ─────────────────────
$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
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
    $fwLines = [System.IO.File]::ReadAllLines($fwPath)
    foreach ($l in $fwLines) {
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
        } else {
            $preLines.Add($l)
        }
    }
    $preLines.Add('')
    $preLines.Add('')
}

$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))

Write-Host "[csharp-plug] bundled $($preLines.Count + $lines.Count) lines, $($body.Length) bytes"

# ── Phase 3: compile via seed CDX in CDX mode ───────────────────────
$run = Start-QemuRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048
if (-not $run) {
    [Console]::Error.WriteLine("FAIL: QEMU did not listen after 4 attempts")
    exit 4
}

try {
    $conn = $run.Conn
    if (-not (Read-QemuReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("READY not received within 30s")
        exit 4
    }
    $stream = $conn.Data.GetStream()
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("CDX`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $srcBytes = [System.IO.File]::ReadAllBytes($BundleSrc)
    $stream.Write($srcBytes, 0, $srcBytes.Length)
    $stream.WriteByte(4)
    $stream.Flush()

    Set-Content -Path $LogFile -Value '' -Encoding UTF8
    $binSize = 0
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) { break }
        if ($line.StartsWith('SIZE:')) {
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            Add-Content -Path $LogFile -Value $line -Encoding UTF8
            while ($true) {
                $l2 = Read-StreamLine -Stream $stream -TimeoutSec 5
                if ($null -eq $l2) { break }
                Add-Content -Path $LogFile -Value $l2 -Encoding UTF8
                if ($l2.StartsWith('HEAP:')) { break }
            }
            [Console]::Error.WriteLine("FAIL: plug compile halted; see $LogFile")
            exit 5
        }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    }
    if ($binSize -le 0) {
        [Console]::Error.WriteLine("FAIL: no SIZE seen; see $LogFile")
        exit 6
    }
    $bytes = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec 300
    if ($null -eq $bytes -or $bytes.Length -ne $binSize) {
        [Console]::Error.WriteLine("FAIL: read $($bytes.Length) bytes, expected $binSize")
        exit 7
    }
    [System.IO.File]::WriteAllBytes($OutFile, $bytes)
    Write-Host "[csharp-plug] OK: $OutFile ($binSize bytes)"
    exit 0
} finally {
    if ($run) {
        Close-Qemu -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
