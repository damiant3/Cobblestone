# audit-skips.ps1 -- re-test the SKIPPED tests. Not all the tests the battery
# does not run: this globs `*.skip` and nothing else.
#
# That distinction was wrong in this header until 2026-07-28 and it mattered.
# The battery's skipped count is `.skip` + `.slow` + `.fatal` -- measured that
# day, 26 + 4 + 15 = 45 -- so this script covered 26 of 45 while its first line
# claimed all of them, and the 15 `.fatal` safety tests went unexamined for as
# long as anyone believed the claim.
#
# The `.fatal` set is NOT audited here now either, and deliberately: it has a
# real runner instead, `build/test.ps1 -Tier traps`, which executes each one
# and judges it on the fault it raises (`exc:` in the sidecar pins WHICH
# fault). That is a stronger verdict than this script can produce, so
# duplicating it here would be a second instrument to keep in step. The 4
# `.slow` have `-Tier slow`.
#
# A .skip is a claim, and it is the only kind of claim in this tree that
# nothing re-tests: the battery reads the reason and believes it, forever. On
# 2026-07-27 four were probed by hand and THREE were stale --
# `let-shadow-scope` said "known-failing: a shadowed let emits the wrong
# value" and answered correctly; `linalg-test` said "mat-mul GPFs at runtime"
# and passed; `rp2040-drivers` said the harness could not pass -board-mmio,
# which a per-test .vmargs does. One, `probability-test`, was accurate.
#
# So this compiles and runs every skipped test that has an .expected, with the
# same sidecar semantics build/test.ps1 uses, and reports which skips are no
# longer true.
#
#   STALE       passes now, and the expected output looks like an assertion
#   TRIVIAL     passes now, but the expected output is one line that is just
#               the test's own name and "ok" -- see below
#   REAL        runs and differs -- the reason still holds
#   UNRUNNABLE  no output inside the wall budget, or does not compile
#
# TRIVIAL exists because the first full run got this wrong and would have done
# damage. Nine tests came back STALE and the report said "delete the .skip and
# let the battery run them". Seven of those nine have a body that is one
# print-line-uni of a literal, so they pass headless for the same reason they
# would pass anywhere: they assert nothing. Un-skipping them would have put
# seven tests into the battery whose green means nothing at all, which is
# strictly worse than a skip -- a skip reads as "not covered", a green test
# reads as "covered". PASSES is not the same claim as THE SKIP WAS WRONG, and
# this script said the second while measuring the first.
#
# The split is a HEURISTIC and is labelled one: a single expected line matching
# the test's own name followed by ok. It cannot see a multi-line expected file
# full of constants, and it will mis-file a real one-line test. Read the
# expected output this prints for every pass before deleting anything.
#
# UNRUNNABLE is the honest bucket for a test that genuinely needs hardware, a
# human, or a display: this cannot tell that apart from a hang, and it does not
# pretend to. There is deliberately NO exclusion list -- a hand-maintained list
# of tests-not-to-audit would be one more claim with no runner behind it, which
# is the exact thing being audited.
#
# On-demand: it boots one VM per test and takes tens of minutes.
#
#   pwsh build/audit-skips.ps1                 # everything
#   pwsh build/audit-skips.ps1 -Only rp2040    # substring match on the name

[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$OutRoot = 'test-output/skip-audit'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$runner  = Join-Path $PSScriptRoot 'test-run.ps1'
New-Item -ItemType Directory -Force $OutRoot | Out-Null

# @() around the pipeline: a single match returns a scalar, and under
# StrictMode a scalar has no .Count. This is the trap val's workplan records
# as "wrap every work-wire call that yields a list", and it bites here too.
$skips = @(Get-ChildItem -Recurse 'codex/test' -Filter '*.skip' -File |
    Where-Object { -not $Only -or $_.BaseName -like "*$Only*" } |
    Sort-Object BaseName)

Write-Host "audit-skips: $($skips.Count) skipped test(s) to re-test"
Write-Host ''

$stale = @(); $trivial = @(); $real = @(); $unrunnable = @(); $noexpected = @()

# One expected line that is just the test's own name and "ok" cannot fail. See
# the header: this is a heuristic, and the expected text is printed either way
# so the reader judges rather than the script.
function Test-AssertsNothing {
    param([string[]]$Expected, [string]$Name)
    if ($Expected.Count -ne 1) { return $false }
    return ($Expected[0].Trim() -match ("^" + [regex]::Escape($Name) + "[: ]\s*ok$"))
}

foreach ($s in $skips) {
    $name = $s.BaseName
    $dir  = $s.DirectoryName
    $src  = Join-Path $dir "$name.codex"
    $exp  = Join-Path $dir "$name.expected"
    $reason = (Get-Content -TotalCount 1 $s.FullName)

    if (-not (Test-Path -PathType Leaf $src)) { continue }
    if (-not (Test-Path -PathType Leaf $exp)) {
        # Nothing to compare against, so this one cannot be judged here. Said
        # out loud rather than counted as covered.
        $noexpected += $name
        Write-Host ("  {0,-30} SKIP-NO-EXPECTED" -f $name)
        continue
    }

    $out = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force $out | Out-Null
    $bin = Join-Path $out "$name.cdx"
    Remove-Item $bin -Force -ErrorAction SilentlyContinue

    # .flags is not optional: build/test.ps1's own retry path once dropped it
    # and reported prose-anchor as a compiler failure for it.
    $flagsFile = Join-Path $dir "$name.flags"
    $args = @('-Src', $src, '-Out', $bin, '-Log', (Join-Path $out 'build.log'))
    if (Test-Path -PathType Leaf $flagsFile) {
        $args += @('-RawFlags', (Get-Content -TotalCount 1 $flagsFile).Trim())
    }

    $actual = Join-Path $out 'actual.txt'
    $runArgs = @('-Kernel', $bin, '-OutFile', $actual)
    foreach ($p in @(@{ E = 'stdin'; A = '-StdinFile' }, @{ E = 'keys'; A = '-KeysFile' },
                     @{ E = 'disk'; A = '-DiskFile' }, @{ E = 'vmargs'; A = '-VmArgsFile' })) {
        $f = Join-Path $dir "$name.$($p.E)"
        if (Test-Path -PathType Leaf $f) { $runArgs += @($p.A, $f) }
    }
    $smpFile = Join-Path $dir "$name.smp"
    if (Test-Path -PathType Leaf $smpFile) {
        $runArgs += @('-Smp', [int]((Get-Content -TotalCount 1 $smpFile).Trim()))
    }

    # Compile and run, and RETRY THE WHOLE PAIR ONCE before filing UNRUNNABLE.
    #
    # Every way this loop fails to produce output is also a way a loaded box
    # fails transiently, and one attempt cannot tell them apart. Measured
    # 2026-07-28 across a full 27-test run: uefi-read-key-nofirmware was filed
    # "no usable binary" and editor-notify-test "empty output", and BOTH
    # compiled and ran clean when re-run alone moments later.
    #
    # That is the hazard of this bucket specifically. UNRUNNABLE is the one
    # verdict here that asks nothing further of anybody -- it reads as "needs
    # hardware, a human, or a display" -- so a flake landing in it is a finding
    # DELETED rather than deferred. One of those two was hiding a genuinely
    # failing test: a contract that had been deliberately changed, asserted at
    # its dead value, behind a skip reason that was false.
    #
    # The zero-byte-.cdx trap the header describes is one cause and contention
    # is another; a retry costs one boot and covers both without either having
    # to be diagnosed. Size, not Test-Path, is the binary check -- see header.
    $ok = $false
    $why = ''
    foreach ($attempt in 1, 2) {
        Remove-Item $bin -Force -ErrorAction SilentlyContinue
        Remove-Item $actual -Force -ErrorAction SilentlyContinue
        & pwsh -NoProfile -File $compile @args 2>&1 | Out-Null
        if (-not (Test-Path -PathType Leaf $bin) -or (Get-Item $bin).Length -eq 0) {
            $why = 'did not compile, or compiled to an empty binary'
            continue
        }
        & pwsh -NoProfile -File $runner @runArgs 2>&1 | Out-Null
        if (-not (Test-Path -PathType Leaf $actual)) { $why = 'no output'; continue }
        if (@((Get-Content $actual) -replace "`r", '').Count -eq 0) { $why = 'empty output'; continue }
        $ok = $true
        if ($attempt -eq 2) {
            Write-Host ("  {0,-30} (first attempt flaky: {1})" -f $name, $why) -ForegroundColor DarkGray
        }
        break
    }
    if (-not $ok) {
        $unrunnable += "$name ($why, twice)"
        Write-Host ("  {0,-30} UNRUNNABLE  {1} (twice)" -f $name, $why)
        continue
    }

    $a = @((Get-Content $actual) -replace "`r", '')
    $e = @((Get-Content $exp)    -replace "`r", '')
    # Clear it every iteration. A throw on one comparison would otherwise leave
    # the previous result in scope and the next test would be judged on it.
    $delta = $null
    $delta = @(Compare-Object $a $e)
    if ($delta.Count -eq 0) {
        $shown = ($e -join ' | ')
        if (Test-AssertsNothing -Expected $e -Name $name) {
            $trivial += "$name -- expected is `"$shown`" -- skip says: $reason"
            Write-Host ("  {0,-30} TRIVIAL     passes, but asserts nothing: {1}" -f $name, $shown) -ForegroundColor DarkYellow
        } else {
            $stale += "$name -- expected is `"$shown`" -- skip says: $reason"
            Write-Host ("  {0,-30} STALE       passes now: {1}" -f $name, $shown) -ForegroundColor Yellow
        }
    } else {
        $real += $name
        Write-Host ("  {0,-30} REAL        still differs" -f $name)
    }
}

Write-Host ''
Write-Host "stale=$($stale.Count)  trivial=$($trivial.Count)  real=$($real.Count)  unrunnable=$($unrunnable.Count)  no-expected=$($noexpected.Count)"

if ($stale.Count -gt 0) {
    Write-Host ''
    Write-Host 'Passes, and the expected output looks like an assertion. READ IT, then delete the .skip:' -ForegroundColor Yellow
    foreach ($x in $stale) { Write-Host "  $x" }
}
if ($trivial.Count -gt 0) {
    Write-Host ''
    Write-Host 'Passes because it asserts nothing. Do NOT un-skip: a green test that cannot fail' -ForegroundColor DarkYellow
    Write-Host 'reads as coverage, which is worse than the skip. Write the body, or say "stub" in' -ForegroundColor DarkYellow
    Write-Host 'the reason so the next reader is not told it is an environment limitation:' -ForegroundColor DarkYellow
    foreach ($x in $trivial) { Write-Host "  $x" }
}
if ($unrunnable.Count -gt 0) {
    Write-Host ''
    Write-Host 'Not judged here (hardware, a human, a display -- or a genuine hang):'
    foreach ($x in $unrunnable) { Write-Host "  $x" }
}

# A stale skip is a finding, not a build failure: this is an audit, and it runs
# on demand rather than in the gate. Exit 0 unless the audit itself broke.
exit 0
