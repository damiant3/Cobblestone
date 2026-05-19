# Compile a single .codex sample by booting the bare-metal self-host
# compiler (Codex.cdx) in a VM and feeding the source over a TCP serial
# port. Default output is CDX; pass -Elf for ELF, or -Ir for IR text
# emission to stdout (captured between IR-BEGIN / IR-END sentinels and
# written to $Out as UTF-8 text).
#
# Usage: sample-compile-selfhost.ps1 -Src <source.codex> -Out <out.cdx> -Log <log.out>
#        sample-compile-selfhost.ps1 -Src <source.codex> -Out <out.elf> -Log <log.out> -Elf
#        sample-compile-selfhost.ps1 -Src <source.codex> -Out <out.ir>  -Log <log.out> -Ir
# Exit 0 = compile succeeded, output binary written.
# Exit non-zero = compile failed; log.out contains the serial output for
# diagnostic-code matching.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [Parameter(Mandatory=$true)] [string]$Log,
    [int]$PCore = 1,
    [switch]$Elf,
    [switch]$Efi,
    [switch]$Uefi,
    [switch]$Img,
    [switch]$Fat16,
    [switch]$IrUni,
    [switch]$IrCce,
    [switch]$Prose,
    [switch]$Repl,
    [int]$Heap = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Stage0 = if ($Elf) { 'build-output\bare-metal\Codex.elf' } else { 'build-output\bare-metal\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0 - run build.ps1 first to build the self-host")
    exit 2
}

# Library preload — bare-metal has no filesystem, so prepend cited library
# chapters (Foreword, Kernel, OS, Works) to the serial feed. Chapter headers
# are renamed "Chapter: X" -> "Chapter: Quire--X" so they coexist.
$fwTmp = [System.IO.Path]::GetTempFileName()
$run = $null

$QuireDirs = @{ 'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'; 'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'; 'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'; 'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'; 'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'; 'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'; 'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'; 'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'; 'Magic' = 'apps\games\magic'; 'Games' = 'apps\games\classic'; 'Spark' = 'apps\spark' }

try {
    $citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|Games|Spark)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

    # Transitive cite resolution: scan source for cites, then scan each
    # loaded foreword for its own cites, until no new dependencies appear.
    # Pre-seed seen set with forewords already embedded in the source
    # (e.g. compiler concat prepends Foreword--X chapters).
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
        $fwPath = Join-Path $QuireDirs[$cite.Quire] "$($cite.Name).codex"
        if (-not (Test-Path -PathType Leaf $fwPath)) {
            "error 3010: Cited $($cite.Quire) chapter '$($cite.Name)' not found (expected $fwPath)" | Set-Content -Path $Log -Encoding UTF8
            exit 8
        }
        $lines = [System.IO.File]::ReadAllLines($fwPath)
        foreach ($l in $lines) {
            if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
        }
        $ordered += @{ Quire = $cite.Quire; Name = $cite.Name; Lines = $lines }
    }

    # Emit forewords in dependency-first order (reverse of discovery).
    [array]::Reverse($ordered)
    # Deduplicate again after reversal (keep first occurrence = deepest dep).
    $emitted = @{}
    $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($entry in $ordered) {
        $key = "$($entry.Quire)::$($entry.Name)"
        if ($emitted[$key]) { continue }
        $emitted[$key] = $true
        $renamed = $false
        foreach ($l in $entry.Lines) {
            if (-not $renamed -and $l -match '^Chapter:\s*(.+?)\s*$') {
                [void]$sb.Append("Chapter: $($entry.Quire)--$($matches[1])`n")
                $renamed = $true
            } else {
                [void]$sb.Append($l + "`n")
            }
        }
        [void]$sb.Append("`n`n")
    }
    [System.IO.File]::WriteAllText($fwTmp, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

    # Launch VM + connect (4 retry attempts on bind/listen failure).
    $run = Start-QemuRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore
    if (-not $run) {
        "FAIL: VM did not listen after 4 attempts" | Set-Content -Path $Log -Encoding UTF8
        exit 3
    }
    $conn = $run.Conn

    if (-not (Read-QemuReady -Conn $conn -TimeoutSec 30)) {
        "READY not received within 30s" | Set-Content -Path $Log -Encoding UTF8
        exit 3
    }

    $stream = $conn.Data.GetStream()

    # Send mode header + foreword bytes + source bytes + EOT (0x04).
    $mode = if ($IrUni) { "IR-UNI" } elseif ($IrCce) { "IR-CCE" } elseif ($Img) { "IMG" } elseif ($Uefi) { "UEFI" } elseif ($Efi) { "EFI" } elseif ($Elf) { "ELF QEMU-11.0.0" } else { "CDX" }
    if ($Fat16 -and $Img) { $mode = "$mode fat16" }
    if ($Uefi -and $Img) { $mode = "$mode uefi" }
    if ($Prose) { $mode = "$mode prose" }
    if ($Repl) { $mode = "$mode repl" }
    if ($Heap -gt 0) { $mode = "$mode heap=$Heap" }
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("$mode`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $fwBytes  = [System.IO.File]::ReadAllBytes($fwTmp)
    $srcBytes = [System.IO.File]::ReadAllBytes($Src)
    $debugAll = New-Object byte[] ($fwBytes.Length + $srcBytes.Length)
    if ($fwBytes.Length -gt 0) { [Array]::Copy($fwBytes, 0, $debugAll, 0, $fwBytes.Length) }
    if ($srcBytes.Length -gt 0) { [Array]::Copy($srcBytes, 0, $debugAll, $fwBytes.Length, $srcBytes.Length) }
    [System.IO.File]::WriteAllBytes("build-output\debug-sent.bin", $debugAll)
    if ($fwBytes.Length -gt 0)  { $stream.Write($fwBytes,  0, $fwBytes.Length) }
    if ($srcBytes.Length -gt 0) { $stream.Write($srcBytes, 0, $srcBytes.Length) }
    $stream.WriteByte(4)
    $stream.Flush()

    # Read text portion until SIZE / CODEGEN-HALTED / CODEGEN-ERRORS / IR-BEGIN.
    Set-Content -Path $Log -Value '' -Encoding UTF8
    $binSize = 0
    $status = ''
    $irLines = $null
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 60
        if ($null -eq $line) { break }
        if ($line.StartsWith('SIZE:')) {
            $tail = $line.Substring(5)
            if ($tail -match '^\d+') { $binSize = [int]$matches[0] }
            if ($IrCce) { $status = 'ir-cce' } else { $status = 'size' }
            break
        }
        if ($line.StartsWith('IR-BEGIN')) {
            $irLines = [System.Text.StringBuilder]::new(65536)
            while ($true) {
                $l2 = Read-StreamLine -Stream $stream -TimeoutSec 60
                if ($null -eq $l2 -or $l2.StartsWith('IR-END')) { break }
                [void]$irLines.AppendLine($l2)
            }
            $status = 'ir'
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            Add-Content -Path $Log -Value $line -Encoding UTF8
            $status = 'halted'
            break
        }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
        Add-Content -Path $Log -Value $line -Encoding UTF8
    }

    if ($status -eq 'ir-cce') {
        # Read exactly $binSize raw CCE bytes + 1 trailing CCE newline.
        $irCceBytes = Read-StreamBytes -Stream $stream -Count ($binSize + 1) -TimeoutSec 60
        if ($null -eq $irCceBytes) {
            "FAIL: could not read $binSize CCE bytes" | Set-Content -Path $Log -Encoding UTF8
            exit 4
        }
        # Write raw CCE bytes (strip trailing newline byte).
        [System.IO.File]::WriteAllBytes($Out, $irCceBytes[0..($binSize - 1)])
        # Drain trailing HEAP line.
        while ($true) {
            $line = Read-StreamLine -Stream $stream -TimeoutSec 5
            if ($null -eq $line) { break }
            Add-Content -Path $Log -Value $line -Encoding UTF8
            if ($line.StartsWith('HEAP:')) { break }
        }
        exit 0
    }

    if ($status -eq 'ir') {
        [System.IO.File]::WriteAllText($Out, $irLines.ToString(), [System.Text.UTF8Encoding]::new($false))
        # Drain trailing WD lines into log.
        while ($true) {
            $line = Read-StreamLine -Stream $stream -TimeoutSec 5
            if ($null -eq $line) { break }
            Add-Content -Path $Log -Value $line -Encoding UTF8
            if ($line.StartsWith('HEAP:')) { break }
        }
        exit 0
    }

    if ($status -ne 'size') {
        # Drain remaining text into log so diagnostics are visible.
        while ($true) {
            $line = Read-StreamLine -Stream $stream -TimeoutSec 5
            if ($null -eq $line) { break }
            Add-Content -Path $Log -Value $line -Encoding UTF8
            if ($line.StartsWith('HEAP:')) { break }
        }
        exit 4
    }

    # Read $binSize raw bytes of binary directly from the socket.
    $readTimeout = if ($binSize -gt 1048576) { 300 } else { 60 }
    $bytes = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec $readTimeout
    if ($null -eq $bytes -or $bytes.Length -ne $binSize) {
        "Binary size mismatch: expected $binSize" | Add-Content -Path $Log -Encoding UTF8
        exit 5
    }
    if ($binSize -lt 100) {
        "Binary too small ($binSize)" | Add-Content -Path $Log -Encoding UTF8
        exit 6
    }
    [System.IO.File]::WriteAllBytes($Out, $bytes)

    # Read symbol map if present.
    $mapLine = Read-StreamLine -Stream $stream -TimeoutSec 5
    if ($null -ne $mapLine -and $mapLine.StartsWith('MAP:')) {
        $mapCount = 0
        if ($mapLine.Substring(4) -match '^\d+') { $mapCount = [int]$matches[0] }
        if ($mapCount -gt 0) {
            $mapFile = [System.IO.Path]::ChangeExtension($Out, '.map')
            $mapLines = [System.Collections.Generic.List[string]]::new($mapCount + 2)
            [void]$mapLines.Add('# Codex Symbol Map')
            [void]$mapLines.Add('# Address         Size  Name')
            while ($true) {
                $ml = Read-StreamLine -Stream $stream -TimeoutSec 5
                if ($null -eq $ml -or $ml.StartsWith('MAP-END')) { break }
                [void]$mapLines.Add($ml)
            }
            [System.IO.File]::WriteAllLines($mapFile, $mapLines, [System.Text.UTF8Encoding]::new($false))
        }
    }
    exit 0

} finally {
    if ($run) {
        Close-Qemu -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
    if (Test-Path $fwTmp) { Remove-Item -Force $fwTmp -ErrorAction SilentlyContinue }
}
