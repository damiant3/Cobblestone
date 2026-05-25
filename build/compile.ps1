# Compile a single .codex source file to CDX, TEXT, or IR by booting
# the bare-metal self-host compiler (Codex.cdx) in a VM and feeding
# the source over a TCP serial port.
#
# The compiler produces CDX (binary), TEXT (.codex round-trip), or
# IR (intermediate representation). Container formats (ELF, PE, IMG)
# are handled by post-compile plugs -- see codex/plugs/.
#
# Usage: compile.ps1 -Src <source.codex> -Out <out.cdx> -Log <log.out>
#        compile.ps1 -Src <source.codex> -Out <out.ir>  -Log <log.out> -IrUni
# Exit 0 = compile succeeded, output written.
# Exit non-zero = compile failed; log.out contains diagnostics.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [Parameter(Mandatory=$true)] [string]$Log,
    [int]$PCore = 1,
    [switch]$IrUni,
    [switch]$IrCce,
    [switch]$Prose,
    [switch]$Repl,
    [switch]$Poison,
    [string]$Break = '',
    [string]$Watch = '',
    [int]$WatchSize = 8,
    [switch]$DebugMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Stage0 = 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0 - run build.ps1 first to build the self-host")
    exit 2
}

# Library preload — bare-metal has no filesystem, so prepend cited library
# chapters (Foreword, Kernel, OS, Works) to the serial feed. Chapter headers
# are renamed "Chapter: X" -> "Chapter: Quire--X" so they coexist.
$fwTmp = [System.IO.Path]::GetTempFileName()
$run = $null

$QuireDirs = @{ 'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'; 'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'; 'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'; 'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'; 'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'; 'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'; 'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'; 'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'; 'Magic' = 'apps\games\magic'; 'CodexMagic' = 'apps\games\codexmagic'; 'Games' = 'apps\games\classic'; 'Spark' = 'apps\spark'; 'Data' = 'apps\data' }

try {
    $citePat = '^\s*cites\s+(Foreword|Kernel|OS|Works|Trust|Net|Verify|Replay|Sched|Observe|Game|Signal|Compress|Encode|Math|Sim|AI|UI|Dev|Magic|CodexMagic|Games|Spark|Data)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

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

    # Breakpoint patching: copy kernel, write INT3 at function entry.
    $bootKernel = $Stage0
    if ($Break) {
        $bpAddr = Resolve-Name -Name $Break
        if ($bpAddr -eq 0) {
            [Console]::Error.WriteLine("BREAK: function '$Break' not found in symbol map")
            exit 2
        }
        $bpFileOff = $bpAddr - 0x100000 + 224
        $bootKernel = Join-Path $env:TEMP "codex-bp-$PID.cdx"
        $cdxBytes = [System.IO.File]::ReadAllBytes($Stage0)
        $origByte = $cdxBytes[$bpFileOff]
        $cdxBytes[$bpFileOff] = 0xCC
        [System.IO.File]::WriteAllBytes($bootKernel, $cdxBytes)
        $bpSym = Resolve-Rip -Rip $bpAddr
        [Console]::Error.WriteLine("BREAK: patched INT3 at $bpSym (0x$($bpAddr.ToString('X')), file offset $bpFileOff, orig=0x$($origByte.ToString('X2')))")
    }

    # Memory watchpoint: resolve function name to guest address.
    $vmExtra = @()
    if ($Watch) {
        $watchAddr = Resolve-Name -Name $Watch
        if ($watchAddr -eq 0) {
            [Console]::Error.WriteLine("WATCH: function '$Watch' not found in symbol map")
            exit 2
        }
        $vmExtra = @('-watch', "0x$($watchAddr.ToString('X'))", '-watch-size', "$WatchSize")
        $watchSym = Resolve-Rip -Rip $watchAddr
        [Console]::Error.WriteLine("WATCH: $watchSym (0x$($watchAddr.ToString('X')), $WatchSize bytes)")
    }

    # Launch VM + connect (4 retry attempts on bind/listen failure).
    $run = Start-VmRun -Kernel $bootKernel -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore -ExtraArgs $vmExtra
    if (-not $run) {
        "FAIL: VM did not listen after 4 attempts" | Set-Content -Path $Log -Encoding UTF8
        exit 3
    }
    $conn = $run.Conn

    if (-not (Read-VmReady -Conn $conn -TimeoutSec 30)) {
        "READY not received within 30s" | Set-Content -Path $Log -Encoding UTF8
        exit 3
    }

    $stream = $conn.Data.GetStream()

    # Send mode header + foreword bytes + source bytes + EOT (0x04).
    $mode = if ($IrUni) { "IR-UNI" } elseif ($IrCce) { "IR-CCE" } else { "CDX" }
    if ($Prose) { $mode = "$mode prose" }
    if ($Repl) { $mode = "$mode repl" }
    if ($Poison) { $mode = "$mode poison" }
    if ($DebugMode) { $mode = "$mode debug" }
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
        if ($line.StartsWith('!EXC')) {
            $excLines = [System.Collections.Generic.List[string]]::new()
            $excLines.Add($line)
            for ($si = 0; $si -lt 4; $si++) {
                $sline = Read-StreamLine -Stream $stream -TimeoutSec 2
                if ($null -eq $sline) { break }
                $excLines.Add($sline)
                if (-not $sline.Contains('S[')) { break }
            }
            $report = Format-CrashReport $excLines.ToArray()
            $isBreakpoint = $line -match '!EXC=03'
            $label = if ($isBreakpoint) { 'BREAKPOINT HIT' } else { 'CRASH' }
            foreach ($rl in $report) {
                Add-Content -Path $Log -Value $rl -Encoding UTF8
                [Console]::Error.WriteLine("  $rl")
            }
            if ($isBreakpoint) { $status = 'breakpoint' } else { $status = 'fatal' }
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

    if ($status -eq 'breakpoint') {
        [Console]::Error.WriteLine("  (breakpoint hit — VM halted, see log)")
        exit 5
    }

    if ($status -eq 'fatal') {
        exit 4
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
        if ($Watch -and $run.StderrFile -and (Test-Path $run.StderrFile)) {
            $stderr = [System.IO.File]::ReadAllText($run.StderrFile).Trim()
            if ($stderr.Length -gt 0) {
                [Console]::Error.WriteLine("[watch] VM stderr:")
                foreach ($sl in $stderr.Split("`n")) {
                    $resolved = $sl.Trim()
                    if ($resolved -match 'RIP=0x([0-9a-fA-F]+)') {
                        $sym = Resolve-Rip -Rip ([Convert]::ToInt64($matches[1], 16))
                        if ($sym) { $resolved = "$resolved  --> $sym" }
                    }
                    [Console]::Error.WriteLine("  $resolved")
                }
            }
        }
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
    if (Test-Path $fwTmp) { Remove-Item -Force $fwTmp -ErrorAction SilentlyContinue }
    if ($Break -and $bootKernel -ne $Stage0) { Remove-Item -Force $bootKernel -ErrorAction SilentlyContinue }
}
