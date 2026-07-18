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
    return $sb.ToString()
}

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
    $flagsFile = Join-Path ([System.IO.Path]::GetDirectoryName($sources[$i])) "$name.flags"
    $extraFlags = if (Test-Path -PathType Leaf $flagsFile) { ' ' + (Get-Content -TotalCount 1 $flagsFile).Trim() } else { '' }
    # Plain CDX: the batch session loops because the SEED is repl-built.
    # A per-request 'repl' flag would embed a REPL loop in the TEST binary
    # (hangs stdin-consuming tests); the 'map' flag would stream the full
    # symbol map over serial per test (the battery-wide map tax). Test
    # binaries get Exit mode and halt cleanly on their own.
    $mode = "CDX$extraFlags`n"
    [void]$inputSb.Append($mode)
    [void]$inputSb.Append($resolved)
    [void]$inputSb.Append([char]4)
}
if ($testNames.Count -eq 0) { exit 0 }

$inputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($inputFile, $inputSb.ToString(), [System.Text.UTF8Encoding]::new($false))
$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$Stage0 = Join-Path (Split-Path $PSScriptRoot) 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) { Write-Error "MISSING: $Stage0"; exit 2 }

$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
    '-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
$proc.WaitForExit(1800000)
if (-not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id }

# Parse output byte-by-byte: text lines interleaved with binary CDX blocks.
$raw = if (Test-Path $outputFile) { [System.IO.File]::ReadAllBytes($outputFile) } else { [byte[]]::new(0) }
$pos = 0; $testIdx = 0

function NextLine {
    if ($script:pos -ge $raw.Length) { return $null }
    $start = $script:pos
    while ($script:pos -lt $raw.Length -and $raw[$script:pos] -ne 10) { $script:pos++ }
    $end = $script:pos
    if ($script:pos -lt $raw.Length) { $script:pos++ }
    $len = $end - $start
    if ($len -gt 0 -and $raw[$start + $len - 1] -eq 13) { $len-- }
    if ($len -le 0) { return '' }
    return [System.Text.Encoding]::UTF8.GetString($raw, $start, $len)
}

function SkipBytes { param([int]$n); $script:pos = [Math]::Min($script:pos + $n, $raw.Length) }

$vmDead = $false

while ($testIdx -lt $testNames.Count -and $pos -lt $raw.Length -and -not $vmDead) {
    $name = $testNames[$testIdx]
    $testOut = Join-Path $OutRoot $name
    $logLines = [System.Collections.Generic.List[string]]::new()
    $exitCode = '4'

    :testloop while ($pos -lt $raw.Length) {
        $line = NextLine
        if ($null -eq $line) { break }

        if ($line.StartsWith('SIZE:')) {
            $binSize = 0
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            if ($binSize -gt 0 -and $pos + $binSize -le $raw.Length) {
                $binBytes = New-Object byte[] $binSize
                [Array]::Copy($raw, $pos, $binBytes, 0, $binSize)
                [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.cdx"), $binBytes)
                SkipBytes $binSize
                $exitCode = '0'
            }
            break testloop
        }
        elseif ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            $logLines.Add($line)
            while ($pos -lt $raw.Length) {
                $el = NextLine; if ($null -eq $el) { break }
                if ($el.StartsWith('CODEGEN-HALTED')) { $logLines.Add($el); break }
                if ($el -ne '') { $logLines.Add($el) }
            }
            $exitCode = '7'
            break testloop
        }
        elseif ($line.StartsWith('!EXC')) {
            # The VM is gone. Keep the whole dump on the test that crashed --
            # the register/stack lines that follow are its diagnostic, not the
            # next test's output -- and stop attributing anything after this.
            $logLines.Add($line)
            while ($pos -lt $raw.Length) {
                $el = NextLine; if ($null -eq $el) { break }
                if ($el -ne '') { $logLines.Add($el) }
            }
            $exitCode = '4'; $vmDead = $true; break testloop
        }
        elseif ($line.StartsWith('WD:') -or $line.StartsWith('HEAP:') -or $line.StartsWith('STACK:')) {
            if ($line.StartsWith('STACK:')) { break testloop }
        }
        else { if ($line) { $logLines.Add($line) } }
    }

    [System.IO.File]::WriteAllLines((Join-Path $testOut 'build.log'), $logLines.ToArray(), [System.Text.UTF8Encoding]::new($false))
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

Remove-Item -Force $inputFile, $outputFile, $stderrFile -ErrorAction SilentlyContinue
Write-SweepLog "batch-done pcore=$PCore compiled=$($sources.Count)"
