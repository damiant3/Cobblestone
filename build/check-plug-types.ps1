# check-plug-types.ps1 -- the IR text wire is a closed contract
#
# codex/plugs/common/IRTextParser.codex calls itself "Inverse of
# codex/Emit/IRTextEmitter.codex". Nothing checked that, and for a long time
# it was not true: the emitter could write forms the parser could not read,
# and each one fell through to a PLAUSIBLE DEFAULT rather than a diagnostic
# -- (vector-mask N) read back as ErrorTy, (a-linear T) as ANamedType
# "Unknown", (unit-def ..) as an empty record. Silent, and invisible to
# x86-64, which never crosses this wire.
#
# The baseline is EMPTY as of the CL that closed that gap: every form
# the emitter writes has a parser arm. That is the state to keep. A new
# entry in the baseline is a hole someone chose to leave open, and the
# reason belongs in the CL description that added it.
#
# That is the shape the IrNegate miscompile had: a hand-maintained copy that
# quietly stopped agreeing, latent until a program took the one path where
# the two answers differed. It answered -(-2.5) as 1.7 on ARM64 and RISC-V
# while x86-64 said 2.5. The residue this script baselines is latent TODAY;
# the point is to fail the build the moment a NEW divergence appears.
#
# Usage: check-plug-types.ps1 [-Update]
#   Without -Update: prints OK or the new divergences + exits 0 or 1
#   With -Update: rewrites the baseline (read the delta before taking it)

param([switch]$Update)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EmitFile = Join-Path $Repo 'codex\compiler\Emit\IRTextEmitter.codex'
$ParseFile= Join-Path $Repo 'codex\plugs\common\IRTextParser.codex'
$BaseFile = Join-Path $Repo 'build\plug-wire-baseline.txt'

foreach ($f in @($EmitFile, $ParseFile)) {
    if (-not (Test-Path $f)) { Write-Host "check-plug-types: missing $f"; exit 1 }
}

# ---------------------------------------------------------------------------
# What the emitter can WRITE.
#
# Two forms, and missing the second is what made the first cut of this script
# useless: a compound "(head ..." and a bare atom "real". Collecting only the
# first reported 58 phantom divergences -- every bare atom the parser accepts
# looked unreachable -- which is a gate nobody would read twice.
# ---------------------------------------------------------------------------
$emitted = [System.Collections.Generic.SortedSet[string]]::new()
foreach ($line in [System.IO.File]::ReadAllLines($EmitFile)) {
    if ($line -match '^\s*$') { continue }
    # compound: "(head  or "(head"
    foreach ($m in [regex]::Matches($line, '"\((?<h>[a-z][a-z0-9\-]*)(?=[ "])')) {
        [void]$emitted.Add($m.Groups['h'].Value)
    }
    # bare atom in a match arm: -> "atom"
    foreach ($m in [regex]::Matches($line, '->\s*"(?<h>[a-z][a-z0-9\-]*)"\s*$')) {
        [void]$emitted.Add($m.Groups['h'].Value)
    }
}

# ---------------------------------------------------------------------------
# What the parser can READ.
#
# Also two forms. The parser dispatches on a head atom with `head == "x"`,
# but reads the chapter's named sections positionally through
# `walk-named-* xs "title"` -- so a check that models only the first accuses
# the parser of not handling forms it handles fine.
# ---------------------------------------------------------------------------
$accepted = [System.Collections.Generic.SortedSet[string]]::new()
foreach ($line in [System.IO.File]::ReadAllLines($ParseFile)) {
    foreach ($m in [regex]::Matches($line, '(?:head|a)\s*==\s*"(?<h>[a-z][a-z0-9\-]*)"')) {
        [void]$accepted.Add($m.Groups['h'].Value)
    }
    foreach ($m in [regex]::Matches($line, '(?:walk-named-\w+|find-named-form)\s+\w+\s+"(?<h>[a-z][a-z0-9\-]*)"')) {
        [void]$accepted.Add($m.Groups['h'].Value)
    }
}

if ($emitted.Count -eq 0 -or $accepted.Count -eq 0) {
    # An empty side compares clean against anything. A check that cannot fail
    # is a comment, so refuse rather than report OK.
    Write-Host "check-plug-types: FAILED to extract (emitted=$($emitted.Count) accepted=$($accepted.Count))"
    Write-Host "  the extraction broke, not the wire -- fix this script before trusting a green"
    exit 1
}

# ---------------------------------------------------------------------------
# Structural wrappers the parser consumes BY POSITION, not by dispatch.
#
# These are not divergences and must not be baselined as if they were. The
# parser reaches them through its caller and reads from index 1 onward,
# deliberately skipping slot 0: walk-chapter takes `list-at xs 1` without ever
# looking at the head, parse-def and parse-text-list do the same. Listing them
# as "known holes" would put three benign entries in a file whose whole value
# is that every line in it is a real gap someone should close.
#
# `strs` additionally only ever appears in PROSE in the emitter (a comment
# describing the wrapper), never in an emitted literal.
# ---------------------------------------------------------------------------
$positional = @('chapter', 'def', 'strs', 'args', 'params', 'sum-ctors',
                'record-fields', 'tparams', 'ctors', 'fields', 'effs', 'scopes')

$divergent = @($emitted |
    Where-Object { -not $accepted.Contains($_) } |
    Where-Object { $positional -notcontains $_ } |
    Sort-Object)

if ($Update) {
    $header = @(
        "# plug-wire-baseline.txt -- generated by build/check-plug-types.ps1 -Update",
        "#",
        "# Forms codex/compiler/Emit/IRTextEmitter.codex can WRITE that",
        "# codex/plugs/common/IRTextParser.codex cannot READ. Each reads back as a",
        "# plausible default, not an error, so nothing downstream notices.",
        "#",
        "# This file is the KNOWN residue. The check fails on anything not listed",
        "# here. Shrinking it is the work -- give each form a parser arm. Growing it",
        "# needs a reason in the CL description.",
        ""
    )
    Set-Content -Path $BaseFile -Value ($header + $divergent) -Encoding utf8
    Write-Host "baseline updated: $($divergent.Count) known divergence(s)"
    $divergent | ForEach-Object { Write-Host "  $_" }
    exit 0
}

if (-not (Test-Path $BaseFile)) {
    Write-Host "check-plug-types: no baseline at $BaseFile -- run with -Update"
    exit 1
}

$baseline = @(Get-Content $BaseFile |
    Where-Object { $_ -notmatch '^\s*#' -and $_ -match '\S' } |
    ForEach-Object { $_.Trim() })

$new   = @($divergent | Where-Object { $baseline -notcontains $_ })
$fixed = @($baseline  | Where-Object { $divergent -notcontains $_ })

if ($new.Count -gt 0) {
    Write-Host "check-plug-types: FAIL -- $($new.Count) new wire divergence(s)"
    foreach ($h in $new) {
        Write-Host "  ($h ...) is emitted by IRTextEmitter and read by no parser arm"
    }
    Write-Host ""
    Write-Host "  The emitter writes this; the plug parser falls through to a default."
    Write-Host "  ARM64 and RISC-V will read a plausible wrong value. x86-64 cannot fail"
    Write-Host "  on it -- it never crosses this wire -- so no x86 test will catch it."
    Write-Host "  Add the parser arm, or record it: build/check-plug-types.ps1 -Update"
    exit 1
}

if ($fixed.Count -gt 0) {
    Write-Host "check-plug-types: OK -- and $($fixed.Count) baselined divergence(s) are gone:"
    $fixed | ForEach-Object { Write-Host "  $_ (now parsed -- drop it from the baseline)" }
    exit 0
}

Write-Host "check-plug-types: OK ($($emitted.Count) emitted, $($accepted.Count) accepted, $($divergent.Count) known)"
exit 0
