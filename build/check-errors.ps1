# check-errors.ps1 -- every test under codex/test/errors must be REFUSED, with the codes it declares.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    # The compiler to judge. Empty means compile.ps1's own default, which is
    # build-output/bare-metal/Codex.cdx and therefore whichever kernel ran
    # LAST. Every caller inside a gate passes this explicitly.
    [string]$Kernel = '',
    [int]$Jobs = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

# 176 tests assert a REFUSAL, and until 2026-08-17 the gate ran 13 of them.
#
# codex/test/errors is the diagnostic path: each source is a program the
# compiler must reject, and its .failing names the codes that must fire. The
# set is reachable only from build/test.ps1, which is the battery and refuses
# to run without Damian. bvt.ps1 carries thirteen by name. So a rejection that
# stopped happening, or started happening for a different reason, was caught
# by nothing an agent is allowed to run.
#
# That is not hypothetical. bounded-exceeded declared `bounded linear` over a
# loop appending a text LITERAL; COMPILER-8 made that extend in place, rule 3
# stopped inferring it growing, the declaration held, and the test compiled
# clean -- the exact opposite of what it exists to assert. It reached main
# green in Update 45 and was found by a release battery, not by a gate.
#
# The set is derived from the directory, never listed here. A hand-maintained
# list is how the thirteen came to stand for a hundred and seventy-six.
#
# Exit 1 on any test that compiles, that fails with the wrong codes, or that
# declares nothing at all.

$errDir = Join-Path 'codex' (Join-Path 'test' 'errors')
if (-not (Test-Path -PathType Container $errDir)) {
    Write-Host "FAIL: $errDir is missing"
    exit 1
}

$tests = @(Get-ChildItem $errDir -Filter *.codex -File | ForEach-Object { $_.FullName } | Sort-Object)
if ($tests.Count -eq 0) {
    Write-Host "FAIL: no tests under $errDir -- a check over an empty set passes for free"
    exit 1
}

$outRoot = Join-Path 'test-output' '_errors'
if (Test-Path $outRoot) { Remove-Item -Recurse -Force $outRoot }
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "  check-errors: $($tests.Count) refusals, $Jobs parallel slots"

$compileScript = Join-Path (Resolve-Path .).Path 'build\compile.ps1'
$bad = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$skipped = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$passed = [System.Collections.Concurrent.ConcurrentBag[string]]::new()

$tests | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $t = $_
    $base = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $skipFile = $t -replace '\.codex$', '.skip'
    if (Test-Path -PathType Leaf $skipFile) {
        ($using:skipped).Add("$base ($((Get-Content -TotalCount 1 $skipFile).Trim()))")
        return
    }
    $failFile = $t -replace '\.codex$', '.failing'
    if (-not (Test-Path -PathType Leaf $failFile)) {
        ($using:bad).Add("$base -- no .failing sidecar: the test asserts nothing")
        return
    }
    $codes = @(Get-Content $failFile | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($codes.Count -eq 0) {
        ($using:bad).Add("$base -- .failing is empty: the test asserts nothing")
        return
    }
    $outDir = Join-Path $using:outRoot $base
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $cdxOut = Join-Path $outDir "$base.cdx"
    $logOut = Join-Path $outDir "$base.log"
    # NOT $args: that is an automatic variable, and assigning it inside a
    # scriptblock shadows the one splatting reads.
    $cargs = @('-Src', $t, '-Out', $cdxOut, '-Log', $logOut)
    if ($using:Kernel) { $cargs += @('-Kernel', $using:Kernel) }
    & pwsh -NoProfile -File $using:compileScript @cargs *> $null
    if ($LASTEXITCODE -eq 0) {
        ($using:bad).Add("$base -- COMPILED, and it must be refused ($($codes -join ' '))")
        return
    }
    $logText = if (Test-Path $logOut) { Get-Content $logOut -Raw } else { '' }
    # test.ps1's adjudication, and it must stay identical to it: `error
    # (CDX)?0*<n>` rather than a bare CDX<n> search, because the lexer's codes
    # print unprefixed and single-digit (`error 6:`) and a CDX-prefixed search
    # reports those as wrong-code failures. A bare `CDX2031` checks only that
    # the code fired; `CDX2031@33:5` also pins WHERE, which a bare code cannot
    # -- a diagnostic reported at a synthetic 0,0 span prints no line:column
    # prefix at all, so a code-only check passes against a compiler that lost
    # the position entirely.
    $missing = ''
    foreach ($code in $codes) {
        if ($code -match '^(?<c>(CDX)?\d+)@(?<l>\d+):(?<col>\d+)$') {
            $c = $matches['c'] -replace '^CDX', ''
            $pat = "\b$($matches['l']):$($matches['col']):\s*error (CDX)?0*$c\b"
            if ($logText -notmatch $pat) { $missing = $code; break }
        } elseif ($logText -notmatch "error (CDX)?0*$code\b") { $missing = $code; break }
    }
    if ($missing) {
        ($using:bad).Add("$base -- refused, but not with $missing (declared: $($codes -join ' '))")
    } else {
        ($using:passed).Add($base)
    }
}

$sw.Stop()
$elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 1)

if ($bad.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($bad.Count) of $($tests.Count) refusal test(s) did not refuse as declared:"
    foreach ($b in ($bad | Sort-Object)) { Write-Host "  $b" }
    Write-Host ''
    Write-Host '  A test here asserts the compiler REFUSES a program, and what it refuses'
    Write-Host '  with is the assertion. A rejection that moved to a different code is a'
    Write-Host '  diagnostic regression; one that stopped happening is a soundness hole.'
    Write-Host "  Per-test logs: $outRoot\<name>\<name>.log"
    exit 1
}

$skipNote = if ($skipped.Count -gt 0) { ", $($skipped.Count) skipped" } else { '' }
Write-Host "  refusals ok ($($passed.Count) refused as declared$skipNote, ${elapsed}s)"
foreach ($s in ($skipped | Sort-Object)) { Write-Host "    skipped: $s" }
exit 0
