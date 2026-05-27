# Extract x86 output from a compiler ELF build into the binary wire protocol
# format that the ELF plug expects.
#
# Compiles a Codex source file to ELF using the CDX seed directly (sending
# "ELF" mode header), then parses the resulting ELF to extract code bytes,
# data bytes, and function table into the wire protocol format.
#
# Usage:
#   extract-x86-output.ps1 -Src <codex-file> -Out <x86out-file>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
$OutDir = Join-Path $PSScriptRoot 'build-output'
$ElfFile = Join-Path $OutDir 'reference.elf'
$LogFile = Join-Path $OutDir 'extract.log'

if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0 -- run build/build.ps1 first")
    exit 2
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# -- Step 1: Resolve foreword dependencies ----------------------------
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
$citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'
$queue = [System.Collections.Generic.Queue[hashtable]]::new()
$seen = @{}
$embeddedPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
foreach ($line in [System.IO.File]::ReadAllLines($Src)) {
    if ($line -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    if ($line -match $embeddedPat) { $seen["$($matches[1])::$($matches[2])"] = $true }
}
$ordered = @()
while ($queue.Count -gt 0) {
    $cite = $queue.Dequeue()
    $key = "$($cite.Quire)::$($cite.Name)"
    if ($seen[$key]) { continue }
    $seen[$key] = $true
    $fwPath = Join-Path $Repo (Join-Path $QuireDirs[$cite.Quire] "$($cite.Name).codex")
    if (-not (Test-Path -PathType Leaf $fwPath)) {
        [Console]::Error.WriteLine("MISSING: cited $($cite.Quire) chapter '$($cite.Name)' ($fwPath)")
        exit 3
    }
    $lines = [System.IO.File]::ReadAllLines($fwPath)
    foreach ($l in $lines) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Lines = $lines }
}
[array]::Reverse($ordered)
$emitted = @{}
$fwSb = [System.Text.StringBuilder]::new(524288)
foreach ($entry in $ordered) {
    $key = "$($entry.Quire)::$($entry.Name)"
    if ($emitted[$key]) { continue }
    $emitted[$key] = $true
    $renamed = $false
    foreach ($l in $entry.Lines) {
        if (-not $renamed -and $l -match '^Chapter:\s*(.+?)\s*$') {
            [void]$fwSb.Append("Chapter: $($entry.Quire)--$($matches[1])`n")
            $renamed = $true
        } else { [void]$fwSb.Append($l + "`n") }
    }
    [void]$fwSb.Append("`n`n")
}
$fwTmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($fwTmp, $fwSb.ToString(), [System.Text.UTF8Encoding]::new($false))

# -- Step 2: Compile to ELF using CDX seed ----------------------------
Write-Host "[extract] Compiling $Src to ELF via CDX seed..."
$run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048
if (-not $run) {
    [Console]::Error.WriteLine("FAIL: VM did not start")
    Remove-Item -Force $fwTmp -ErrorAction SilentlyContinue
    exit 4
}
try {
    $conn = $run.Conn
    if (-not (Read-VmReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("FAIL: no READY")
        exit 4
    }
    $stream = $conn.Data.GetStream()

    # Send "ELF" mode header + foreword + source + EOT
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("ELF`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $fwBytes = [System.IO.File]::ReadAllBytes($fwTmp)
    $srcBytes = [System.IO.File]::ReadAllBytes($Src)
    if ($fwBytes.Length -gt 0) { $stream.Write($fwBytes, 0, $fwBytes.Length) }
    $stream.Write($srcBytes, 0, $srcBytes.Length)
    $stream.WriteByte(4); $stream.Flush()

    # Read diagnostic lines until SIZE
    Set-Content -Path $LogFile -Value '' -Encoding UTF8
    $binSize = 0
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) { break }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
        if ($line.StartsWith('SIZE:')) {
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            while ($true) {
                $l2 = Read-StreamLine -Stream $stream -TimeoutSec 5
                if ($null -eq $l2) { break }
                Add-Content -Path $LogFile -Value $l2 -Encoding UTF8
                if ($l2.StartsWith('HEAP:')) { break }
            }
            [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
            exit 5
        }
    }
    if ($binSize -le 0) { [Console]::Error.WriteLine("FAIL: no SIZE; see $LogFile"); exit 6 }
    $elfData = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec 120
    if ($null -eq $elfData -or $elfData.Length -ne $binSize) {
        [Console]::Error.WriteLine("FAIL: incomplete read ($($elfData.Length)/$binSize)")
        exit 7
    }
    [System.IO.File]::WriteAllBytes($ElfFile, $elfData)
    Write-Host "[extract] ELF: $ElfFile ($binSize bytes)"

    # Read MAP data from remaining serial output
    $funcNames = [System.Collections.Generic.List[string]]::new()
    $funcOffsets = [System.Collections.Generic.List[int]]::new()
    $inMap = $false
    while ($true) {
        $ml = Read-StreamLine -Stream $stream -TimeoutSec 5
        if ($null -eq $ml) { break }
        Add-Content -Path $LogFile -Value $ml -Encoding UTF8
        if ($ml.StartsWith('MAP:')) { $inMap = $true; continue }
        if ($ml.StartsWith('MAP-END')) { break }
        if ($inMap -and $ml -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
            $addr = [Convert]::ToInt64($matches[1], 16)
            $offset = $addr - 0x100000
            $funcNames.Add($matches[3])
            $funcOffsets.Add($offset)
        }
        if ($ml.StartsWith('HEAP:')) { break }
    }
    Write-Host "[extract] Functions: $($funcNames.Count)"
} finally {
    Close-Vm -Conn $run.Conn -Process $run.Process
    Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    Remove-Item -Force $fwTmp -ErrorAction SilentlyContinue
}

# -- Step 3: Parse ELF to extract code, data --------------------------
$elfBytes = [System.IO.File]::ReadAllBytes($ElfFile)

$shoff = [BitConverter]::ToUInt32($elfBytes, 32)
$shnum = [BitConverter]::ToUInt16($elfBytes, 48)
$shstrndx = [BitConverter]::ToUInt16($elfBytes, 50)
$shentSize = 40

$shstrOff = [BitConverter]::ToUInt32($elfBytes, $shoff + $shstrndx * $shentSize + 16)

function Get-SectionName($nameIdx) {
    $sb = [System.Text.StringBuilder]::new()
    $pos = $shstrOff + $nameIdx
    while ($pos -lt $elfBytes.Length -and $elfBytes[$pos] -ne 0) {
        $sb.Append([char]$elfBytes[$pos]) | Out-Null
        $pos++
    }
    $sb.ToString()
}

$textOff = 0; $textSize = 0
$rodataOff = 0; $rodataSize = 0

for ($si = 0; $si -lt $shnum; $si++) {
    $shBase = $shoff + $si * $shentSize
    $nameIdx = [BitConverter]::ToUInt32($elfBytes, $shBase)
    $secName = Get-SectionName $nameIdx
    $secOff = [BitConverter]::ToUInt32($elfBytes, $shBase + 16)
    $secSize = [BitConverter]::ToUInt32($elfBytes, $shBase + 20)
    if ($secName -eq '.text') { $textOff = $secOff; $textSize = $secSize }
    if ($secName -eq '.rodata') { $rodataOff = $secOff; $rodataSize = $secSize }
}

Write-Host "[extract] .text: offset=$textOff size=$textSize"
Write-Host "[extract] .rodata: offset=$rodataOff size=$rodataSize"

$codeBytes = [byte[]]::new($textSize)
[Array]::Copy($elfBytes, $textOff, $codeBytes, 0, $textSize)
$dataBytes = [byte[]]::new($rodataSize)
if ($rodataSize -gt 0) {
    [Array]::Copy($elfBytes, $rodataOff, $dataBytes, 0, $rodataSize)
}

# -- Step 4: Write binary wire protocol -------------------------------
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

$bw.Write([int]$textSize)
$bw.Write([int]$rodataSize)
$bw.Write([int]$funcNames.Count)
$bw.Write($codeBytes)
$bw.Write($dataBytes)

$enc = [System.Text.Encoding]::UTF8
for ($fi = 0; $fi -lt $funcNames.Count; $fi++) {
    $nameBytes = $enc.GetBytes($funcNames[$fi])
    $bw.Write([short]$nameBytes.Length)
    $bw.Write($nameBytes)
    $bw.Write([int]$funcOffsets[$fi])
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host "[extract] OK: $Out ($((Get-Item $Out).Length) bytes)"
Write-Host "[extract] Reference ELF: $ElfFile"
exit 0
