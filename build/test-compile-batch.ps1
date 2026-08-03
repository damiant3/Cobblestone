# Batch compiler: boots one VM in REPL mode, feeds all test sources
# sequentially. One VM boot for the entire batch.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$ListFile,
    [Parameter(Mandatory=$true)] [string]$OutRoot,
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
. (Join-Path $PSScriptRoot 'vm-config.ps1')

$sources = @(Get-Content -Path $ListFile | Where-Object { $_.Trim() -ne '' })
if ($sources.Count -eq 0) { exit 0 }
$resolveSw = [System.Diagnostics.Stopwatch]::StartNew()

. (Join-Path $PSScriptRoot 'quire-map.ps1')

function Resolve-Source {
    param([string]$SrcPath)
    $lines = [System.IO.File]::ReadAllLines($SrcPath)
    $seedSeen = @{}; $embPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
    foreach ($l in $lines) {
        if ($l -match $embPat) { $seedSeen["$($matches[1])::$($matches[2])"] = $true }
    }
    try {
        $ordered = Resolve-CiteOrder -RootLines $lines -Repo '.' -SeedSeen $seedSeen
    } catch {
        return $null
    }
    $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$sb.Append($l + "`n") }
    foreach ($l in $lines) { [void]$sb.Append($l + "`n") }
    # The regions ride along so build.log can name the file and the file's own
    # line. This assembler builds its own unit and writes its own log, so it
    # has to map back itself -- compile.ps1 doing it is not enough, and the
    # battery is the path that matters for a `.failing` position pin.
    return @{ Text = $sb.ToString(); Regions = (Get-DiagRegions -Ordered $ordered -SrcPath $SrcPath) }
}

$regionsByName = @{}

# Build combined REPL input
$inputSb = [System.Text.StringBuilder]::new(10485760)
$testNames = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $sources.Count; $i++) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($sources[$i])
    $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    $resolved = Resolve-Source $sources[$i]
    if ($null -eq $resolved) {
        "8" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
        "error 3010: foreword resolution failed" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
        continue
    }
    $testNames.Add($name)
    $regionsByName[$name] = $resolved.Regions
    # Census instrumentation: the resolved concat size is the best host-side
    # proxy for a test's compile cost inside a batch (the VM's output file
    # flushes on exit, so per-test wall time is not observable from here).
    "$($resolved.Text.Length)" | Set-Content -Path (Join-Path $testOut '.src-bytes') -Encoding UTF8
    $flagsFile = Join-Path ([System.IO.Path]::GetDirectoryName($sources[$i])) "$name.flags"
    $extraFlags = if (Test-Path -PathType Leaf $flagsFile) { ' ' + (Get-Content -TotalCount 1 $flagsFile).Trim() } else { '' }
    # Plain CDX: the batch session loops because the SEED is repl-built.
    # A per-request 'repl' flag would embed a REPL loop in the TEST binary
    # (hangs stdin-consuming tests); the 'map' flag would stream the full
    # symbol map over serial per test (the battery-wide map tax). Test
    # binaries get Exit mode and halt cleanly on their own.
    $mode = "CDX$extraFlags`n"
    [void]$inputSb.Append($mode)
    [void]$inputSb.Append($resolved.Text)
    [void]$inputSb.Append([char]4)
}
if ($testNames.Count -eq 0) { exit 0 }

$inputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($inputFile, $inputSb.ToString(), [System.Text.UTF8Encoding]::new($false))
$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$Stage0 = Join-Path (Split-Path $PSScriptRoot) 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) { Write-Error "MISSING: $Stage0"; exit 2 }

$resolveSw.Stop()
$batchSw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
    '-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
$proc.WaitForExit(1800000)
if (-not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id }
$batchSw.Stop()
$parseSw = [System.Diagnostics.Stopwatch]::StartNew()

# Parse output: text lines interleaved with binary CDX blocks. Newlines are
# found with a native string scan, not a PowerShell byte loop: when a batch
# VM dies mid-binary the framing is lost and the remainder of the output is
# walked as "lines", which the per-byte loop turned into 17-23 MINUTES on a
# crashed batch (census 2026-07-27). The Latin-1 shadow string maps chars to
# bytes 1:1, so positions in it are byte positions; line TEXT is still
# decoded from the bytes as UTF-8.
$raw = if (Test-Path $outputFile) { [System.IO.File]::ReadAllBytes($outputFile) } else { [byte[]]::new(0) }
$rawStr = [System.Text.Encoding]::GetEncoding(28591).GetString($raw)
$pos = 0; $testIdx = 0

function NextLine {
    if ($script:pos -ge $raw.Length) { return $null }
    $start = $script:pos
    $nl = $rawStr.IndexOf([char]10, $start)
    $end = if ($nl -lt 0) { $raw.Length } else { $nl }
    $script:pos = if ($nl -lt 0) { $raw.Length } else { $nl + 1 }
    $len = $end - $start
    if ($len -gt 0 -and $raw[$start + $len - 1] -eq 13) { $len-- }
    if ($len -le 0) { return '' }
    return [System.Text.Encoding]::UTF8.GetString($raw, $start, $len)
}

function SkipBytes { param([int]$n); $script:pos = [Math]::Min($script:pos + $n, $raw.Length) }

$vmDead = $false
$LogCap = 2000

# Decode a [Start,End) byte span into kept log lines: UTF-8, CR stripped,
# empty and WD:/HEAP: telemetry dropped, capped. The cap matters on a dead
# batch, where a span is misframed binary rather than a log.
function Add-LogSpan {
    param($Lines, [int]$Start, [int]$End)
    if ($End -le $Start) { return }
    $text = [System.Text.Encoding]::UTF8.GetString($raw, $Start, $End - $Start)
    foreach ($l in $text.Split("`n")) {
        if ($l.EndsWith("`r")) { $l = $l.Substring(0, $l.Length - 1) }
        if (-not $l) { continue }
        if ($l.StartsWith('WD:') -or $l.StartsWith('HEAP:')) { continue }
        if ($Lines.Count -lt $script:LogCap) { $Lines.Add($l) }
        else { $Lines.Add('(build.log truncated by harness at 2000 lines)'); return }
    }
}

# Next newline-preceded occurrence of a marker at or after From, or -1.
# Ordinal: the default IndexOf(string) is culture-sensitive and an order
# of magnitude slower over megabytes. The anchored-at-pos case (a marker
# standing exactly at the current position, e.g. directly after a skipped
# binary block with no separating newline) is handled by the caller, not
# here -- position 0 included.
function Get-NextMarkerAt {
    param([string]$Marker, [int]$From)
    $j = $rawStr.IndexOf("`n$Marker", [Math]::Max(0, $From - 1), [System.StringComparison]::Ordinal)
    if ($j -ge 0) { return $j + 1 } else { return -1 }
}

# One native marker search per test instead of a per-line walk. A test's
# region ends at the next protocol marker standing at a line start: SIZE:
# (binary follows, success), CODEGEN-HALTED/CODEGEN-ERRORS (diagnostics,
# exit 7), !EXC (VM death), STACK: (compiler stack report, failure). The
# lines before the marker are the test's log. On a dead batch the stream
# between markers is megabytes of binary; IndexOf skips it natively, where
# the per-line walk (a PowerShell function call per garbage line) burned
# 865 s of host CPU per crashed batch even with fast line-finding. The
# memo keeps each marker's search monotonic: one pass over the output per
# marker for the whole batch, not per test. -2 = not yet searched, -1 =
# no occurrence remains.
$markerList = @('SIZE:', 'CODEGEN-HALTED', 'CODEGEN-ERRORS', '!EXC', 'STACK:')
$markerMemo = @{}
foreach ($m in $markerList) { $markerMemo[$m] = -2 }

while ($testIdx -lt $testNames.Count -and $pos -lt $raw.Length -and -not $vmDead) {
    $name = $testNames[$testIdx]
    $testOut = Join-Path $OutRoot $name
    $logLines = [System.Collections.Generic.List[string]]::new()
    $exitCode = '4'

    # A marker standing exactly at $pos wins outright: $pos is always a
    # logical line start (position 0, after a consumed line, or after a
    # skipped binary block), and no later occurrence can beat it. The
    # memoized newline-preceded search cannot see this case, because the
    # byte before $pos may be the tail of a binary block, not a newline.
    # Substring + -ceq, NOT [string]::CompareOrdinal(a, i, b, j, len):
    # PowerShell's binding of that 5-argument static ran at ~2 s per call
    # (measured 2026-07-27), which put 214 of a 214 s profile in this loop.
    $mkAt = -1; $mkKind = $null
    foreach ($m in $markerList) {
        if ($rawStr.Length - $pos -ge $m.Length -and $rawStr.Substring($pos, $m.Length) -ceq $m) { $mkAt = $pos; $mkKind = $m; break }
    }
    if ($null -eq $mkKind) {
        foreach ($m in $markerList) {
            $at = $markerMemo[$m]
            if ($at -ne -1 -and $at -lt $pos) {
                $at = Get-NextMarkerAt $m $pos
                $markerMemo[$m] = $at
            }
            if ($at -ge 0 -and ($mkAt -lt 0 -or $at -lt $mkAt)) { $mkAt = $at; $mkKind = $m }
        }
    }

    if ($null -eq $mkKind) {
        # Stream ended with no terminator for this test: keep what there is.
        Add-LogSpan $logLines $pos $raw.Length
        $pos = $raw.Length
    } else {
        Add-LogSpan $logLines $pos $mkAt
        $pos = $mkAt
        $line = NextLine
        if ($mkKind -eq 'SIZE:') {
            $binSize = 0
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            if ($binSize -gt 0 -and $pos + $binSize -le $raw.Length) {
                $binBytes = New-Object byte[] $binSize
                [Array]::Copy($raw, $pos, $binBytes, 0, $binSize)
                [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.cdx"), $binBytes)
                SkipBytes $binSize
                $exitCode = '0'
            }
        }
        elseif ($mkKind -eq 'CODEGEN-HALTED' -or $mkKind -eq 'CODEGEN-ERRORS') {
            $logLines.Add($line)
            $close = Get-NextMarkerAt 'CODEGEN-HALTED' $pos
            $markerMemo['CODEGEN-HALTED'] = $close
            if ($close -ge 0) {
                Add-LogSpan $logLines $pos $close
                $pos = $close
                $logLines.Add((NextLine))
            } else {
                Add-LogSpan $logLines $pos $raw.Length
                $pos = $raw.Length
            }
            $exitCode = '7'
        }
        elseif ($mkKind -eq '!EXC') {
            # The VM is gone. Keep the dump on the test that crashed -- the
            # register/stack lines are its diagnostic, not the next test's
            # output -- and stop attributing anything after it. The dump is
            # under a hundred lines; 64 KB bounds it comfortably.
            $logLines.Add($line)
            Add-LogSpan $logLines $pos ([Math]::Min($pos + 65536, $raw.Length))
            $exitCode = '4'; $vmDead = $true
        }
        # STACK: needs nothing more: the marker line is consumed and dropped,
        # the log span before it is kept, and the exit stays 4.
    }

    $mapped = [System.Collections.Generic.List[string]]::new($logLines.Count)
    foreach ($l in $logLines) { $mapped.Add((Convert-DiagLine -Line $l -Regions $regionsByName[$name])) }
    [System.IO.File]::WriteAllLines((Join-Path $testOut 'build.log'), $mapped, [System.Text.UTF8Encoding]::new($false))
    $exitCode | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    $testIdx++
}

while ($testIdx -lt $testNames.Count) {
    $name = $testNames[$testIdx]; $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    "99" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    "FAIL: VM died before this test" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
    $testIdx++
}

$parseSw.Stop()
Remove-Item -Force $inputFile, $outputFile, $stderrFile -ErrorAction SilentlyContinue
Write-SweepLog "batch-done pcore=$PCore compiled=$($sources.Count) resolvems=$($resolveSw.ElapsedMilliseconds) vmms=$($batchSw.ElapsedMilliseconds) parsems=$($parseSw.ElapsedMilliseconds)"
