# Compiler acceptance test harness — batch mode.
#
# Phase 1: batch-compile all non-skipped tests through persistent VM
#           instances (one per job slot, repl-loop reuse).
# Phase 2: run compiled tests that have .expected files (individual VM
#           per test, parallel).
#
# Usage:
#   build/test.ps1 [-CodexCdx FP] [-Jobs N] [-ErrorsOnly | -NoErrors]
#   build/test.ps1 -FW      # also include codex\test\forewords\*.codex
#   build/test.ps1 -Apps    # also include codex\test\apps\*.codex
#   build/test.ps1 -Fuzz    # also include codex\test\fuzz\*.codex
#   build/test.ps1 -All     # all of the above
#   build/test.ps1 -All -Slow  # include slow tests too
#   build/test.ps1 -Fatal     # include fatal tests (GPF/exception demos)
#
# Sidecars (all optional, presence-driven):
#   codex*.test\foo.expected  — compile must SUCCEED, runtime output must match
#   codex*.test\foo.failing   — compile must FAIL with listed CDX error codes
#   codex*.test\foo.skip      — skipped entirely (first line = reason)
#   codex*.test\foo.slow      — skipped unless -Slow is passed (first line = reason)
#   codex*.test\foo.fatal     — skipped unless -Fatal is passed (kills VM at runtime)
#
# Exit status: 0 iff every sample ends in its expected bucket.
[CmdletBinding()]
param(
    [string]$CodexCdx,
    [int]$Jobs = 4,
    [switch]$ErrorsOnly,
    [switch]$NoErrors,
    [switch]$Apps,
    [switch]$FW,
    [switch]$All,
    [switch]$Fuzz,
    [switch]$Slow,
    [switch]$Fatal
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$Scope = if ($ErrorsOnly) { 'errors' } elseif ($NoErrors) { 'positive' } else { 'both' }
$OutRoot     = 'test-output'
$ResultsDir  = Join-Path $OutRoot '_results'
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
if (Test-Path $ResultsDir) { Remove-Item -Recurse -Force $ResultsDir }
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

$TestLog = Join-Path $OutRoot 'test.log'
Set-Content -Path $TestLog -Value '' -Encoding UTF8
$env:CODEX_SWEEP_LOG = (Resolve-Path $TestLog).Path

$tests = [System.Collections.Generic.List[string]]::new()
if ($Scope -eq 'both' -or $Scope -eq 'positive') {
    Get-ChildItem -Path 'codex\test\*.codex' -File -ErrorAction SilentlyContinue | ForEach-Object { $tests.Add($_.FullName) }
    if ($Apps -or $All) {
        Get-ChildItem -Path 'codex\test\apps\*.codex' -File -ErrorAction SilentlyContinue | ForEach-Object { $tests.Add($_.FullName) }
    }
    if ($FW -or $All) {
        Get-ChildItem -Path 'codex\test\forewords\*.codex' -File -ErrorAction SilentlyContinue | ForEach-Object { $tests.Add($_.FullName) }
    }
    if ($Fuzz -or $All) {
        Get-ChildItem -Path 'codex\test\fuzz\*.codex' -File -ErrorAction SilentlyContinue | ForEach-Object { $tests.Add($_.FullName) }
    }
}
if ($Scope -eq 'both' -or $Scope -eq 'errors') {
    Get-ChildItem -Path 'codex\test\errors\*.codex' -File -ErrorAction SilentlyContinue | ForEach-Object { $tests.Add($_.FullName) }
}

if ($CodexCdx) {
    $stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    Copy-Item -Force $CodexCdx $stage0
}

# ═══════════════════════════════════════════════════════════════════════════
# Pre-filter: handle skips, partition into compile lists
# ═══════════════════════════════════════════════════════════════════════════
$toCompile = [System.Collections.Generic.List[string]]::new()
foreach ($src in $tests) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    $dir  = [System.IO.Path]::GetDirectoryName($src)
    $skipFile = Join-Path $dir "$name.skip"
    $slowFile = Join-Path $dir "$name.slow"
    if (Test-Path -PathType Leaf $skipFile) {
        $reason = (Get-Content -TotalCount 1 $skipFile)
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t$reason" | Set-Content -Path $resultFile -Encoding UTF8
    } elseif (-not $Slow -and (Test-Path -PathType Leaf $slowFile)) {
        $reason = (Get-Content -TotalCount 1 $slowFile)
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t(slow) $reason" | Set-Content -Path $resultFile -Encoding UTF8
    } elseif (-not $Fatal -and (Test-Path -PathType Leaf (Join-Path $dir "$name.fatal"))) {
        $reason = (Get-Content -TotalCount 1 (Join-Path $dir "$name.fatal"))
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t(fatal) $reason" | Set-Content -Path $resultFile -Encoding UTF8
    } else {
        $toCompile.Add($src)
    }
}

Write-Host "Tests: $($tests.Count) total, $($tests.Count - $toCompile.Count) skipped, $($toCompile.Count) to compile ($Jobs batch slots)"

# ═══════════════════════════════════════════════════════════════════════════
# Phase 1: Batch compile — one VM per job slot
# ═══════════════════════════════════════════════════════════════════════════
$batchDir = Join-Path $OutRoot '_batches'
if (Test-Path $batchDir) { Remove-Item -Recurse -Force $batchDir }
New-Item -ItemType Directory -Force -Path $batchDir | Out-Null

$listFiles = @()
$writers = @()
for ($j = 0; $j -lt $Jobs; $j++) {
    $lf = Join-Path $batchDir "batch-$j.txt"
    $listFiles += $lf
    $writers += [System.IO.StreamWriter]::new($lf, $false, [System.Text.UTF8Encoding]::new($false))
}
for ($i = 0; $i -lt $toCompile.Count; $i++) {
    $writers[$i % $Jobs].WriteLine($toCompile[$i])
}
foreach ($w in $writers) { $w.Close() }

$compileScript = Join-Path $PSScriptRoot 'test-compile-batch.ps1'
$compileProcs = @()
for ($j = 0; $j -lt $Jobs; $j++) {
    $lf = $listFiles[$j]
    if ((Get-Item $lf).Length -eq 0) { continue }
    $pcore = ($j + 1) % 8
    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', $compileScript,
        '-ListFile', $lf,
        '-OutRoot', $OutRoot,
        '-PCore', $pcore
    ) -PassThru -WindowStyle Hidden
    $compileProcs += $proc
    Write-Host "  batch $j started (pid $($proc.Id), pcore $pcore)"
}

Write-Host "Waiting for $($compileProcs.Count) compile batches..."
foreach ($proc in $compileProcs) {
    $proc.WaitForExit(1800000) | Out-Null
    if (-not $proc.HasExited) {
        Write-Host "  batch pid $($proc.Id) timed out — killing" -ForegroundColor Yellow
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
}
Write-Host "Phase 1 (compile) complete."

# ═══════════════════════════════════════════════════════════════════════════
# Phase 1b: Classify compile results
# ═══════════════════════════════════════════════════════════════════════════
$needsRun = [System.Collections.Generic.List[hashtable]]::new()
foreach ($src in $toCompile) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    $dir  = [System.IO.Path]::GetDirectoryName($src)
    $out  = Join-Path $OutRoot $name
    $resultFile = Join-Path $ResultsDir $name
    $failingFile  = Join-Path $dir "$name.failing"
    $expectedFile = Join-Path $dir "$name.expected"
    $stdinFile    = Join-Path $dir "$name.stdin"
    $diskFile     = Join-Path $dir "$name.disk"
    $log = Join-Path $out 'build.log'
    $bin = Join-Path $out "$name.cdx"
    $exitFile = Join-Path $out '.exitcode'

    $exitCode = if (Test-Path $exitFile) { (Get-Content -TotalCount 1 $exitFile).Trim() } else { '99' }
    $compileOk = ($exitCode -eq '0')

    if (Test-Path -PathType Leaf $failingFile) {
        if ($compileOk) {
            "FAIL_EXPECTED_BUT_COMPILED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            continue
        }
        $codesOk = $true
        $logText = if (Test-Path $log) { Get-Content -Raw -Path $log } else { '' }
        foreach ($code in (Get-Content $failingFile)) {
            $code = $code.Trim()
            if (-not $code) { continue }
            if ($logText -notmatch "error (CDX)?0*$code\b") { $codesOk = $false; break }
        }
        if ($codesOk) {
            "PASS_FAILING`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        } else {
            "FAIL_WRONG_DIAGNOSTIC`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        }
        continue
    }

    if (-not $compileOk) {
        "FAIL_COMPILE`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        continue
    }

    if (-not (Test-Path -PathType Leaf $expectedFile)) {
        "PASS_UNVERIFIED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        continue
    }

    $needsRun.Add(@{
        Name = $name; Bin = $bin; Expected = $expectedFile;
        Stdin = $stdinFile; Disk = $diskFile
    })
}

# ═══════════════════════════════════════════════════════════════════════════
# Phase 2: Run tests with .expected files (individual VM per test)
# ═══════════════════════════════════════════════════════════════════════════
if ($needsRun.Count -gt 0) {
    Write-Host "Phase 2: running $($needsRun.Count) tests with expected output ($Jobs parallel)..."

    $coreQueue = [System.Collections.Concurrent.ConcurrentQueue[int]]::new()
    for ($i = 0; $i -lt $Jobs; $i++) { $coreQueue.Enqueue(($i + 1) % 8) }

    $runWorker = {
        $t = $_
        $name = $t.Name
        $bin  = $t.Bin
        $expectedFile = $t.Expected
        $stdinFile    = $t.Stdin
        $diskFile     = $t.Disk
        $out  = Join-Path $using:OutRoot $name
        $resultFile = Join-Path $using:ResultsDir $name
        $runScript  = Join-Path $using:PSScriptRoot 'test-run.ps1'
        $actual = Join-Path $out 'runtime.actual'

        $cq = $using:coreQueue
        $pcore = 1
        [void]$cq.TryDequeue([ref]$pcore)

        try {
            $runArgs = @('-NoProfile', '-File', $runScript, '-Kernel', $bin, '-OutFile', $actual, '-PCore', $pcore)
            if (Test-Path -PathType Leaf $stdinFile) { $runArgs += @('-StdinFile', $stdinFile) }
            if (Test-Path -PathType Leaf $diskFile)  { $runArgs += @('-DiskFile', $diskFile) }
            & pwsh @runArgs
            if ($LASTEXITCODE -ne 0) {
                "FAIL_RUNTIME`t$name`trun failed" | Set-Content -Path $resultFile -Encoding UTF8
                return
            }

            $expectedBytes = [System.IO.File]::ReadAllText($expectedFile) -replace "`r",''
            $actualBytes   = if (Test-Path $actual) { [System.IO.File]::ReadAllText($actual) } else { '' }
            if ($expectedBytes -eq $actualBytes) {
                "PASS_EXPECTED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            } else {
                "FAIL_OUTPUT`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            }
        } finally {
            $cq.Enqueue($pcore)
        }
    }

    $needsRun | ForEach-Object -Parallel $runWorker -ThrottleLimit $Jobs
}
Write-Host "Phase 2 (run) complete."

# ═══════════════════════════════════════════════════════════════════════════
# Aggregate results
# ═══════════════════════════════════════════════════════════════════════════
$buckets = @{
    PASS_EXPECTED = @(); PASS_UNVERIFIED = @(); PASS_FAILING = @()
    SKIPPED = @(); FAIL_COMPILE = @(); FAIL_RUNTIME = @()
    FAIL_OUTPUT = @(); FAIL_EXPECTED_BUT_COMPILED = @(); FAIL_WRONG_DIAGNOSTIC = @()
}
foreach ($f in Get-ChildItem -File $ResultsDir) {
    $line = Get-Content -TotalCount 1 $f.FullName
    if (-not $line) { continue }
    $parts = $line -split "`t", 3
    $status = $parts[0]; $name = $parts[1]; $detail = if ($parts.Count -ge 3) { $parts[2] } else { '' }
    if (-not $buckets.ContainsKey($status)) {
        Write-Host "unknown result status '$status' for $name" -ForegroundColor Yellow
        continue
    }
    if ($status -in 'SKIPPED','FAIL_RUNTIME') {
        $buckets[$status] += "$name`: $detail"
    } else {
        $buckets[$status] += $name
    }
}

$total = ($buckets.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
$passed = $buckets.PASS_EXPECTED.Count + $buckets.PASS_FAILING.Count + $buckets.PASS_UNVERIFIED.Count
$unexpected = $buckets.FAIL_COMPILE.Count + $buckets.FAIL_EXPECTED_BUT_COMPILED.Count `
            + $buckets.FAIL_WRONG_DIAGNOSTIC.Count + $buckets.FAIL_OUTPUT.Count + $buckets.FAIL_RUNTIME.Count

Write-Host "total=$total  pass=$passed  fail=$unexpected  skip=$($buckets.SKIPPED.Count)"

if ($unexpected -gt 0) {
    function Show-Failures { param([string]$Label, [object[]]$Items); if ($Items.Count -gt 0) { Write-Host "$Label`:"; foreach ($i in $Items) { Write-Host "  $i" } } }
    Show-Failures 'compile failed'              $buckets.FAIL_COMPILE
    Show-Failures 'expected error but compiled' $buckets.FAIL_EXPECTED_BUT_COMPILED
    Show-Failures 'wrong diagnostic'            $buckets.FAIL_WRONG_DIAGNOSTIC
    Show-Failures 'output mismatch'             $buckets.FAIL_OUTPUT
    Show-Failures 'runtime error'               $buckets.FAIL_RUNTIME
    exit 1
}
exit 0
