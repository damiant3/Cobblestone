# test-compile-batch.ps1 -- Batch compiler -- boots one VM in REPL mode and feeds all test sources sequentially. One VM boot for the entire batch
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ListFile,
    [Parameter(Mandatory=$true)]
    [string]$OutRoot,
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


# Resolution is split from emission so the batch input never exists as one
# resident string. It used to be built in a StringBuilder and then written
# with ToString(), which holds the WHOLE batch twice at the write; measured
# 2026-09-01 on 400 chapters, driver live heap went 7 MB before this loop to
# 101 MB after, and peak working set was 711 MB. Each chapter is now written
# through and only that chapter is in hand. Resolve-CiteOrder can throw, so
# ordering happens BEFORE anything is written and a failed chapter emits
# nothing.
function Resolve-Order {
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
    return @{ Lines = $lines; Ordered = $ordered }
}


$regionsByName = @{}

# Build combined REPL input, streamed straight to the file so the batch is
# never resident. The mode line is written only after ordering succeeds, so a
# chapter that fails resolution emits nothing, exactly as the buffered
# version did by skipping the appends.
$inputFile = [System.IO.Path]::GetTempFileName()
$inputWriter = [System.IO.StreamWriter]::new($inputFile, $false, [System.Text.UTF8Encoding]::new($false))
$testNames = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $sources.Count; $i++) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($sources[$i])
    $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    $resolved = Resolve-Order $sources[$i]
    if ($null -eq $resolved) {
        "8" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
        "error 3010: foreword resolution failed" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
        continue
    }
    $testNames.Add($name)
    # The regions ride along so build.log can name the file and the file's own
    # line. This assembler builds its own unit and writes its own log, so it
    # has to map back itself -- compile.ps1 doing it is not enough, and the
    # battery is the path that matters for a `.failing` position pin.
    $regionsByName[$name] = Get-DiagRegions -Ordered $resolved.Ordered -SrcPath $sources[$i]
    $flagsFile = Join-Path ([System.IO.Path]::GetDirectoryName($sources[$i])) "$name.flags"
    $extraFlags = if (Test-Path -PathType Leaf $flagsFile) { ' ' + (Get-Content -TotalCount 1 $flagsFile).Trim() } else { '' }
    # Plain CDX: the batch session loops because the SEED is repl-built.
    # A per-request 'repl' flag would embed a REPL loop in the TEST binary
    # (hangs stdin-consuming tests); the 'map' flag would stream the full
    # symbol map over serial per test (the battery-wide map tax). Test
    # binaries get Exit mode and halt cleanly on their own.
    $inputWriter.Write("CDX$extraFlags`n")
    $srcChars = 0
    foreach ($l in (Format-CiteChapters -Ordered $resolved.Ordered)) { $inputWriter.Write($l); $inputWriter.Write("`n"); $srcChars += $l.Length + 1 }
    foreach ($l in $resolved.Lines) { $inputWriter.Write($l); $inputWriter.Write("`n"); $srcChars += $l.Length + 1 }
    $inputWriter.Write([char]4)
    # Census instrumentation: the resolved concat size is the best host-side
    # proxy for a test's compile cost inside a batch (the VM's output file
    # flushes on exit, so per-test wall time is not observable from here).
    "$srcChars" | Set-Content -Path (Join-Path $testOut '.src-bytes') -Encoding UTF8
}
$inputWriter.Dispose()
if ($testNames.Count -eq 0) { Remove-Item $inputFile -Force -ErrorAction SilentlyContinue; exit 0 }
$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$Stage0 = Join-Path (Split-Path $PSScriptRoot) 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) { Write-Error "MISSING: $Stage0"; exit 2 }

$resolveSw.Stop()
$batchSw = [System.Diagnostics.Stopwatch]::StartNew()
$startArgs = @{
    FilePath = $script:CodexVmBin
    ArgumentList = @('-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', '3072', '-headless')
    PassThru = $true; RedirectStandardError = $stderrFile
}
# -WindowStyle throws on non-Windows editions of pwsh.
if ($IsWindows) { $startArgs.WindowStyle = 'Hidden' }
$proc = Start-Process @startArgs
$proc.WaitForExit(1800000)
if (-not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id }
$batchSw.Stop()
$parseSw = [System.Diagnostics.Stopwatch]::StartNew()


$batchDropped = $false
if ((Test-Path -PathType Leaf $stderrFile)) {
    $vmErr = [System.IO.File]::ReadAllText($stderrFile)
    if (($vmErr -match 'DROPPED')) {
        [Console]::Error.WriteLine('codex-vm dropped guest serial bytes: this batch''s capture is SHORT, and every block after the loss is filed under the WRONG test name')
        [Console]::Error.WriteLine($vmErr)
        $batchDropped = $true
    }
}


# Parse output: text lines interleaved with binary CDX blocks. Newlines and
# markers are found by native byte scans, not a PowerShell byte loop: when a
# batch VM dies mid-binary the framing is lost and the remainder of the output
# is walked as "lines", which the per-byte loop turned into 17-23 MINUTES on a
# crashed batch (census 2026-07-27). [Array]::IndexOf does the skipping
# natively, so only newline candidates reach PowerShell.
# A Latin-1 shadow STRING of the whole capture served this until 2026-09-01.
# It cost 2 bytes per captured byte on top of the byte[], a .NET string being
# UTF-16: 240 MB of managed heap on a 120 MB capture, per driver, per parallel
# slot. Byte scanning costs 0.378 s against the shadow's 0.019 s on a full
# 120 MB pass that finds nothing, so about 1.9 s per driver at worst across
# the five markers. Memory was the binding constraint, not time.
# Assigned DIRECTLY, never as `$raw = if (...) { ReadAllBytes } else { ... }`:
# a statement's result goes through the pipeline, which unrolls a byte[] into
# an Object[] of boxed bytes, and every GetString/Array.Copy below then
# re-converts the whole buffer. Measured 2026-08-22: 183 ms per call on a
# 2.9 MB capture, quadratic in the batch (96 tests 130 s, 193 tests 450 s).
$raw = [byte[]]::new(0)
if (Test-Path $outputFile) { $raw = [System.IO.File]::ReadAllBytes($outputFile) }
$pos = 0; $testIdx = 0


function NextLine {
    if ($script:pos -ge $raw.Length) { return $null }
    $start = $script:pos
    $nl = [Array]::IndexOf($raw, [byte]10, $start)
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
function Test-MarkerAtPos {
    param([int]$At, [string]$Marker)
    $mb = [System.Text.Encoding]::ASCII.GetBytes($Marker)
    if ($At + $mb.Length -gt $raw.Length) { return $false }
    for ($k = 0; $k -lt $mb.Length; $k++) { if ($raw[$At + $k] -ne $mb[$k]) { return $false } }
    return $true
}

function Get-NextMarkerAt {
    param([string]$Marker, [int]$From)
    $mb = [System.Text.Encoding]::ASCII.GetBytes($Marker)
    $ml = $mb.Length
    $i = [Math]::Max(0, $From - 1)
    while ($true) {
        $nl = [Array]::IndexOf($raw, [byte]10, $i)
        if ($nl -lt 0 -or $nl + 1 + $ml -gt $raw.Length) { return -1 }
        $ok = $true
        for ($k = 0; $k -lt $ml; $k++) { if ($raw[$nl + 1 + $k] -ne $mb[$k]) { $ok = $false; break } }
        if ($ok) { return $nl + 1 }
        $i = $nl + 1
    }
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
        if (Test-MarkerAtPos $pos $m) { $mkAt = $pos; $mkKind = $m; break }
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

$shortStream = ($testIdx -lt $testNames.Count) -and -not $vmDead


while ($testIdx -lt $testNames.Count) {
    $name = $testNames[$testIdx]; $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    "99" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    "FAIL: VM died before this test" | Set-Content -Path (Join-Path $testOut 'build.log') -Encoding UTF8
    $testIdx++
}


if ($batchDropped -or $shortStream) {
    $why = if ($batchDropped) { 'codex-vm dropped serial bytes' } else { 'the output stream ended before every test had a block' }
    [Console]::Error.WriteLine("BATCH INVALIDATED: $why -- positional attribution cannot be trusted; all $($testNames.Count) members set to exit 99 for re-batch")
    foreach ($name in $testNames) {
        $testOut = Join-Path $OutRoot $name
        "99" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
        Add-Content -Path (Join-Path $testOut 'build.log') -Value "BATCH INVALIDATED: $why -- re-batched" -Encoding UTF8
    }
}


$parseSw.Stop()
Remove-Item -Force $inputFile, $outputFile, $stderrFile -ErrorAction SilentlyContinue
Write-SweepLog "batch-done pcore=$PCore compiled=$($sources.Count) resolvems=$($resolveSw.ElapsedMilliseconds) vmms=$($batchSw.ElapsedMilliseconds) parsems=$($parseSw.ElapsedMilliseconds)"
