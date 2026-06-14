# Build Verification Test — minimal confidence gate.
#
# The compiler self-compile (fixed point) already proves: pattern matching,
# variants, records, let/if/when, function application, closures, text ops,
# list ops, bounded integers, recursion, TCO, binary ops, field access,
# act blocks, prose, cites, chapters/sections/pages.
#
# This BVT covers ONLY what the fixed point does NOT exercise:
#   - type classes, effects/handlers, linear types, mutable state
#   - try/retry, for-loops, concurrency (fork/await)
#   - a handful of error-rejection tests (diagnostic path)
#   - one library smoke (hamt — the backing store for Set/KvStore)
#
# ~15 tests, runs in under 30 seconds after a fresh build.
#
# Usage:
#   build/bvt.ps1                 # uses seed from build-output
#   build/bvt.ps1 -CodexCdx X    # uses specified CDX
[CmdletBinding()]
param(
    [string]$CodexCdx,
    [int]$Jobs = 4
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$BvtTests = @(
    # --- Language features the compiler does NOT use ---
    'codex\test\typeclass-smoke.codex'         # type classes (Show, Eq, Ord)
    'codex\test\handler-smoke.codex'           # effect handlers
    'codex\test\linear-smoke.codex'            # linear types (consume, freeze)
    'codex\test\mutable-smoke.codex'           # mutable records, field mutation
    'codex\test\try-smoke.codex'               # try/retry/fallback
    'codex\test\with-timeout-test.codex'       # timeout construct
    'codex\test\fork-nested.codex'             # concurrency (fork/await)
    'codex\test\unit-smoke.codex'              # unit type

    # --- Library correctness (not exercised by self-compile) ---
    'codex\test\hamt-test.codex'               # HAMT — backs Set and KvStore
    'codex\test\sort-test.codex'               # sort with custom comparators
    'codex\test\crypto-test.codex'             # AES, ChaCha20

    # --- Error rejection (diagnostic path) ---
    'codex\test\errors\type-mismatch.codex'    # type error detection
    'codex\test\errors\unknown-name.codex'     # undefined name detection
    'codex\test\errors\non-exhaustive-match.codex'  # exhaustiveness checker
    'codex\test\errors\keyword-as-pattern-var.codex' # keyword rejection
    'codex\test\errors\linear-errors.codex'    # linear type violations
)

$OutRoot    = 'test-output'
$ResultsDir = Join-Path $OutRoot '_results'
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
if (Test-Path $ResultsDir) { Remove-Item -Recurse -Force $ResultsDir }
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

$TestLog = Join-Path $OutRoot 'bvt.log'
Set-Content -Path $TestLog -Value '' -Encoding UTF8
$env:CODEX_SWEEP_LOG = (Resolve-Path $TestLog).Path

if ($CodexCdx) {
    $stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    Copy-Item -Force $CodexCdx $stage0
}

$stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path $stage0)) {
    Write-Host "ERROR: No kernel at $stage0 — run build/build.ps1 first." -ForegroundColor Red
    exit 1
}

$vmExe = Join-Path (Resolve-Path .).Path 'tools\codex-vm.exe'
if (-not (Test-Path $vmExe)) {
    Write-Host "ERROR: codex-vm.exe not found at $vmExe" -ForegroundColor Red
    exit 1
}

$missing = @()
foreach ($t in $BvtTests) {
    if (-not (Test-Path $t)) { $missing += $t }
}
if ($missing.Count -gt 0) {
    Write-Host "ERROR: Missing BVT test files:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "BVT: $($BvtTests.Count) tests, $Jobs parallel slots"
Write-Host ""

# Phase 1: compile all tests
Write-Host "--- Phase 1: compile ---"
$compileScript = Join-Path (Resolve-Path .).Path 'build\compile.ps1'
$compileFails  = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$compilePass   = [System.Collections.Concurrent.ConcurrentBag[string]]::new()

$BvtTests | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $t          = $_
    $base       = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $outDir     = Join-Path $using:OutRoot $base
    $cdxOut     = Join-Path $outDir "$base.cdx"
    $logOut     = Join-Path $outDir "$base.log"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $failFile = $t -replace '\.codex$', '.failing'
    $expectFail = Test-Path $failFile

    $r = & pwsh -NoProfile -File $using:compileScript -Src $t -Out $cdxOut -Log $logOut 2>&1
    $ok = $LASTEXITCODE -eq 0

    if ($expectFail) {
        if ($ok) {
            ($using:compileFails).Add("$base (expected compile FAIL but got PASS)")
            Write-Host "  FAIL  $base (expected compile failure)" -ForegroundColor Red
        } else {
            $codes = (Get-Content $failFile -ErrorAction SilentlyContinue) -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' }
            $logText = if (Test-Path $logOut) { Get-Content $logOut -Raw -ErrorAction SilentlyContinue } else { '' }
            $allFound = $true
            foreach ($code in $codes) {
                if ($logText -notmatch "CDX$code") { $allFound = $false }
            }
            if ($allFound) {
                ($using:compilePass).Add($base)
                Write-Host "  PASS  $base (expected error)" -ForegroundColor Green
            } else {
                ($using:compileFails).Add("$base (missing expected CDX codes: $($codes -join ','))")
                Write-Host "  FAIL  $base (wrong error codes)" -ForegroundColor Red
            }
        }
    } else {
        if ($ok) {
            ($using:compilePass).Add($base)
            Write-Host "  PASS  $base" -ForegroundColor Green
        } else {
            ($using:compileFails).Add("$base (compile failed)")
            Write-Host "  FAIL  $base (compile)" -ForegroundColor Red
        }
    }
}

# Phase 2: run tests that have .expected files
Write-Host ""
Write-Host "--- Phase 2: run ---"
$runFails = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$runPass  = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$runSkip  = 0

$runnableTests = $BvtTests | Where-Object {
    $exp = $_ -replace '\.codex$', '.expected'
    Test-Path $exp
}

$runnableTests | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $t    = $_
    $base = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $outDir  = Join-Path $using:OutRoot $base
    $cdxOut  = Join-Path $outDir "$base.cdx"
    $runOut  = Join-Path $outDir "$base.out"
    $expFile = $t -replace '\.codex$', '.expected'

    if (-not (Test-Path $cdxOut)) {
        ($using:runFails).Add("$base (no CDX to run)")
        Write-Host "  SKIP  $base (no CDX)" -ForegroundColor Yellow
        return
    }

    & pwsh -NoProfile -File (Join-Path $using:PWD 'build\test-run.ps1') -Kernel $cdxOut -OutFile $runOut 2>$null
    if (-not (Test-Path $runOut)) {
        ($using:runFails).Add("$base (no output)")
        Write-Host "  FAIL  $base (no output)" -ForegroundColor Red
        return
    }

    $actual   = (Get-Content $runOut -Raw -ErrorAction SilentlyContinue) -replace "`r", ""
    $expected = (Get-Content $expFile -Raw -ErrorAction SilentlyContinue) -replace "`r", ""
    $actual   = $actual.TrimEnd("`n")
    $expected = $expected.TrimEnd("`n")

    if ($actual -eq $expected) {
        ($using:runPass).Add($base)
        Write-Host "  PASS  $base" -ForegroundColor Green
    } else {
        ($using:runFails).Add("$base (output mismatch)")
        Write-Host "  FAIL  $base (output mismatch)" -ForegroundColor Red
        Write-Host "    expected: $($expected.Substring(0, [Math]::Min(80, $expected.Length)))" -ForegroundColor DarkGray
        Write-Host "    actual:   $($actual.Substring(0, [Math]::Min(80, $actual.Length)))" -ForegroundColor DarkGray
    }
}

$sw.Stop()
$totalPass = $compilePass.Count + $runPass.Count
$totalFail = $compileFails.Count + $runFails.Count
Write-Host ""
Write-Host "--- BVT Results ---"
Write-Host "  Compile: $($compilePass.Count) pass, $($compileFails.Count) fail"
Write-Host "  Runtime: $($runPass.Count) pass, $($runFails.Count) fail"
Write-Host "  Total:   $totalPass pass, $totalFail fail"
Write-Host "  Time:    $([math]::Round($sw.Elapsed.TotalSeconds, 1))s"

if ($totalFail -gt 0) {
    Write-Host ""
    Write-Host "FAILURES:" -ForegroundColor Red
    $compileFails | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    $runFails | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "BVT PASSED" -ForegroundColor Green
exit 0
