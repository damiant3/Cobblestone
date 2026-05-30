# vm-config.ps1 — shared VM config + helpers for the harness.
# Memory-mapped I/O: no serial ports, no TCP sockets.
# The VM loads input from a file into guest memory at 0x400000.
# Output is written by the guest to 0x500000 and dumped to a file on exit.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
if (-not (Test-Path -PathType Leaf $script:CodexVmBin)) {
    throw "codex-vm not found at $($script:CodexVmBin). Build with tools/build-vm.ps1."
}

# CCE encode/decode tables — shared by plug run scripts.
$script:CceToUnicode = @(
    0, 10, 32,
    48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
    101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
    108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
    118, 107, 106, 120, 113, 122,
    69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
    76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
    86, 75, 74, 88, 81, 90,
    46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
    43, 61, 42, 60, 62,
    47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
    94,
    36, 37,
    233, 232, 234, 235, 225, 224, 226, 228,
    243, 244, 246, 250, 252, 241, 231, 237
)
$script:UnicodeToCce = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) { $script:UnicodeToCce[$i] = 68 }
for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
    $u = $script:CceToUnicode[$i] % 256
    $script:UnicodeToCce[$u] = [byte]$i
}

# Resolve a code address to "function+offset" using a .map file.
$script:MapCache = @{}
function Resolve-Rip {
    param([long]$Rip, [string]$MapFile = '')
    if (-not $MapFile) { $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    if (-not (Test-Path $MapFile)) { return $null }
    if (-not $script:MapCache[$MapFile]) {
        $entries = @()
        foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
            if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                $entries += @{ Addr = [Convert]::ToInt64($matches[1], 16); Size = [int]$matches[2]; Name = $matches[3] }
            }
        }
        $script:MapCache[$MapFile] = $entries
    }
    foreach ($e in $script:MapCache[$MapFile]) {
        if ($Rip -ge $e.Addr -and $Rip -lt ($e.Addr + $e.Size)) {
            $off = $Rip - $e.Addr
            return "$($e.Name)+0x$($off.ToString('X'))"
        }
    }
    return $null
}

function Resolve-Name {
    param([string]$Name, [string]$MapFile = '')
    if (-not $MapFile) { $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    if (-not (Test-Path $MapFile)) { return 0 }
    foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
        if ($line -match '^(0x[0-9a-fA-F]+)\s+\d+\s+(.+)$') {
            if ($matches[2].Trim() -eq $Name) { return [Convert]::ToInt64($matches[1], 16) }
        }
    }
    return 0
}

function Format-CrashReport {
    param([string[]]$ExcLines)
    $report = [System.Collections.Generic.List[string]]::new()
    if ($ExcLines.Count -eq 0) { return $report }
    $excLine = $ExcLines[0]
    $vec = ''
    if ($excLine -match '!EXC=([0-9a-fA-F]+)') { $vec = $matches[1] }
    $vecInt = [Convert]::ToInt32($vec, 16)
    $vecName = switch ($vecInt) { 0 { 'divide error' } 6 { 'invalid opcode' } 13 { 'general protection' } 14 { 'page fault' } default { "vector $vecInt" } }
    $rip = 0
    if ($excLine -match 'RIP=([0-9a-fA-F]+)') { $rip = [Convert]::ToInt64($matches[1], 16) }
    $ripSym = Resolve-Rip -Rip $rip
    if (-not $ripSym) { $ripSym = "0x$($rip.ToString('X8'))" }
    $cr2 = ''
    if ($excLine -match 'CR2=([0-9a-fA-F]+)') {
        $cr2val = [Convert]::ToInt64($matches[1], 16)
        if ($cr2val -ne 0) { $cr2 = ", CR2=0x$($cr2val.ToString('X12'))" }
    }
    $header = "CRASH in $ripSym ($vecName$cr2)"
    $report.Add($header)
    $report.Add("  RIP   0x$($rip.ToString('X8').PadLeft(8,'0'))  $ripSym")
    foreach ($regName in @('callR','RBX','R12','R13','R14','R10','RDI','RSI','R15')) {
        if ($excLine -match "$regName=([0-9a-fA-F]+)") {
            $val = [Convert]::ToInt64($matches[1], 16)
            $sym = Resolve-Rip -Rip $val
            $hex = "0x$($val.ToString('X8').PadLeft(8,'0'))"
            $extra = ''
            if ($regName -eq 'R10') {
                $heapMB = [math]::Round(($val - 0x600000) / 1048576.0, 1)
                $extra = "  (heap @ $heapMB MB)"
            }
            if ($sym) { $report.Add("  $($regName.PadRight(5)) $hex  $sym$extra") }
            else { $report.Add("  $($regName.PadRight(5)) $hex$extra") }
        }
    }
    for ($i = 1; $i -lt $ExcLines.Count; $i++) {
        $sl = $ExcLines[$i]
        if ($sl -match 'S\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $soff = $matches[1]; $sval = [Convert]::ToInt64($matches[2], 16)
            $ssym = Resolve-Rip -Rip $sval
            if ($ssym) {
                if ($i -eq 1) { $report.Add("  Stack trace (heuristic):") }
                $report.Add("    S[$soff] 0x$($sval.ToString('X8').PadLeft(8,'0'))  $ssym")
            }
        }
    }
    return $report
}

function Write-SweepLog {
    param([string]$Message)
    $logPath = $env:CODEX_SWEEP_LOG
    if ($logPath) {
        $ts = (Get-Date).ToString('HH:mm:ss.fff')
        Add-Content -Path $logPath -Value "$ts $Message" -Encoding UTF8
    }
}

function Normalize-TripleNewlines {
    param([byte[]]$Bytes)
    $out = [System.Collections.Generic.List[byte]]::new($Bytes.Length)
    $nlRun = 0
    foreach ($b in $Bytes) {
        if ($b -eq 10) {
            $nlRun++
            if ($nlRun -le 2) { $out.Add($b) }
        } else {
            $nlRun = 0
            $out.Add($b)
        }
    }
    return $out.ToArray()
}
