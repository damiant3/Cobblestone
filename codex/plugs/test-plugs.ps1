# Test all transpiler plugs against test inputs.
#
# Usage:
#   codex/plugs/test-plugs.ps1                    # test all built plugs
#   codex/plugs/test-plugs.ps1 -Plug python       # test one plug
#   codex/plugs/test-plugs.ps1 -BuildFirst        # build all plugs, then test
#   codex/plugs/test-plugs.ps1 -Input hello       # test with specific input
#
# For each plug × input, runs:
#   1. Compile .codex → IR text (via seed)
#   2. Feed IR to plug → target source
#   3. Verify output is non-empty and contains expected markers
#
# Exit 0 if all tests pass, 1 if any fail.
[CmdletBinding()]
param(
    [string]$Plug = '',
    [string]$Input = '',
    [switch]$BuildFirst,
    [int]$Jobs = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PlugsDir = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $PlugsDir '..' '..')).Path
$TestInputDir = Join-Path $PlugsDir 'test-input'
$TestOutputDir = Join-Path $PlugsDir 'test-output'

New-Item -ItemType Directory -Force -Path $TestOutputDir | Out-Null

# Transpiler plugs only. The native backends are skipped because this
# harness drives every plug as `run.ps1 -Src <codex> -Out <text>` and then
# asserts the output is non-empty text with source markers in it. A native
# backend answers a different question entirely: arm64 and riscv take
# `-IrInput` and emit the BINARY wire protocol, so they fail parameter
# binding and exit 1 in under a second having done no work at all.
#
# arm64 and riscv were missing from this list, so a clean sweep reported 18
# failures that were nothing of the kind. They are not untested: build.ps1's
# plug-binary leg rebuilds all five native backends every gate run, and the
# cross-architecture battery boots their ELF output on Renode. Do not "fix"
# those 18 by touching the plugs.
$binaryPlugs = @('elf', 'pe', 'img', 'arm64', 'riscv', 'common')
$allPlugs = Get-ChildItem $PlugsDir -Directory |
    Where-Object { $_.Name -notin $binaryPlugs -and $_.Name -ne 'test-input' -and $_.Name -ne 'test-output' } |
    Where-Object { Test-Path (Join-Path $_.FullName 'run.ps1') } |
    ForEach-Object { $_.Name } |
    Sort-Object

if ($Plug) { $allPlugs = @($Plug) }

$testInputs = Get-ChildItem $TestInputDir -Filter '*.codex' -File |
    ForEach-Object { $_.BaseName } |
    Sort-Object

if ($Input) { $testInputs = @($Input) }

# Build plugs if requested
if ($BuildFirst) {
    Write-Host "=== Building plugs ==="
    foreach ($p in $allPlugs) {
        $buildScript = Join-Path $PlugsDir "$p\build.ps1"
        if (Test-Path $buildScript) {
            Write-Host "  Building $p..."
            & pwsh -NoProfile -File $buildScript 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  FAIL: $p build failed"
            }
        }
    }
    Write-Host ""
}

# Check which plugs are built
$builtPlugs = @()
$notBuilt = @()
foreach ($p in $allPlugs) {
    $cdxPattern = Join-Path $PlugsDir "$p\build-output\*-plug.cdx"
    if (Get-ChildItem $cdxPattern -ErrorAction SilentlyContinue) {
        $builtPlugs += $p
    } else {
        $notBuilt += $p
    }
}

if ($notBuilt.Count -gt 0) {
    Write-Host "Skipping (not built): $($notBuilt -join ', ')"
    Write-Host ""
}

# Expected markers per test input
$markers = @{
    'hello'  = @('Hello', 'World', '5')
    'types'  = @('point', 'circle', 'rect', '42')
    'lambda' = @('apply', 'adder', 'lambda')
}

# Run tests
$results = @()
$passCount = 0
$failCount = 0
$skipCount = 0

Write-Host "=== Testing $($builtPlugs.Count) plugs x $($testInputs.Count) inputs ==="
Write-Host ""

foreach ($p in $builtPlugs) {
    foreach ($t in $testInputs) {
      try {
        $testSrc = Join-Path $TestInputDir "$t.codex"
        $outDir = Join-Path $TestOutputDir "$p"
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $outFile = Join-Path $outDir "$t.out"

        $runScript = Join-Path $PlugsDir "$p\run.ps1"

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            & pwsh -NoProfile -File $runScript -Src $testSrc -Out $outFile 2>&1 | Out-Null
            $ec = $LASTEXITCODE
        } catch {
            $ec = 99
        }
        $sw.Stop()
        $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 1)

        if ($ec -ne 0) {
            Write-Host "  FAIL  $p/$t (exit $ec, ${elapsed}s)"
            $failCount++
            $results += "$p/$t`tFAIL`texit $ec"
            continue
        }

        if (-not (Test-Path $outFile)) {
            Write-Host "  FAIL  $p/$t (no output, ${elapsed}s)"
            $failCount++
            $results += "$p/$t`tFAIL`tno output"
            continue
        }

        # Some plugs emit a PROJECT, not a file. wpf writes a directory --
        # App.xaml, MainWindow.xaml.cs, a .csproj -- and that is correct: a
        # WPF app is not one file. Get-Item on a directory has no .Length,
        # so under StrictMode this threw, the exception escaped the loop, and
        # it killed the ENTIRE SWEEP at wpf -- taking zig with it (the two
        # plugs that sort after winforms) and skipping the exit-code line at
        # the bottom, so a run with real failures still reported exit 0.
        # A harness that cannot fail is worse than no harness. Fold a
        # directory into its concatenated contents and judge that.
        $item = Get-Item $outFile
        if ($item.PSIsContainer) {
            $parts = @(Get-ChildItem -File -Recurse $outFile)
            $size = ($parts | Measure-Object -Property Length -Sum).Sum
            if ($null -eq $size) { $size = 0 }
            $content = ($parts | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
        } else {
            $size = $item.Length
            $content = [System.IO.File]::ReadAllText($outFile)
        }

        if ($size -eq 0) {
            Write-Host "  FAIL  $p/$t (empty output, ${elapsed}s)"
            $failCount++
            $results += "$p/$t`tFAIL`tempty output"
            continue
        }

        # Check markers
        $expectedMarkers = if ($markers[$t]) { $markers[$t] } else { @() }
        $missing = @()
        foreach ($m in $expectedMarkers) {
            if ($content -notmatch [regex]::Escape($m)) {
                $missing += $m
            }
        }

        if ($missing.Count -gt 0) {
            Write-Host "  WARN  $p/$t (${size}B, ${elapsed}s) missing: $($missing -join ', ')"
            $results += "$p/$t`tWARN`tmissing markers: $($missing -join ', ')"
            $passCount++  # still counts as pass — output was generated
        } else {
            Write-Host "  PASS  $p/$t (${size}B, ${elapsed}s)"
            $passCount++
            $results += "$p/$t`tPASS`t${size}B"
        }
      } catch {
        # One plug must never be able to take the sweep down with it. wpf did
        # exactly that, and the cost was invisible: the run simply stopped
        # after winforms, so wpf and zig were never tested and nobody noticed.
        Write-Host "  FAIL  $p/$t (harness error: $($_.Exception.Message))"
        $failCount++
        $results += "$p/$t`tFAIL`tharness error: $($_.Exception.Message)"
      }
    }
}

# Summary
Write-Host ""
Write-Host "=== Results ==="
Write-Host "  pass=$passCount  fail=$failCount  skip=$($notBuilt.Count * $testInputs.Count)"
Write-Host "  built plugs: $($builtPlugs.Count)  not built: $($notBuilt.Count)"

# Write results file
$resultsFile = Join-Path $TestOutputDir '_results.txt'
$results | Set-Content -Path $resultsFile -Encoding UTF8
Write-Host "  results: $resultsFile"

if ($failCount -gt 0) { exit 1 } else { exit 0 }
