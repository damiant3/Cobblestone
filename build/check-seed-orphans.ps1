# Would an UNTRACKED source file be compiled into the seed?
#
# This is the cheap replacement for the old "rebuild the whole gate on the
# main workspace" step (PerforceProcess P-SIGNED). That full rebuild existed
# for exactly one reason: to catch a source file present on your workstream
# but NOT in Perforce. concat-codex-self globs codex/compiler and the cited
# foreword chapters off disk, so an untracked .codex there is baked into the
# seed you build -- and then a copy-up carries the SEED but not the untracked
# file, so main's tracked source no longer reproduces that seed and main is
# busted. Signing is deterministic (measured), so once no orphan can enter,
# the seed proven on your stream IS the seed on main; no second build proves
# anything the orphan check does not.
#
# The seed's source set is codex/compiler plus the cited forewords. The old
# in-gate guard scoped itself to codex/compiler ONLY, which misses an
# untracked chapter under codex/foreword -- and forewords are in the seed
# (a foreword parser guard moved the seed on 2026-08-16). This covers both.
#
#   build/check-seed-orphans.ps1          # exit 0 clean, exit 1 with a list
#
# Reports ADDS only (an untracked file that would be compiled in). An edit to
# a tracked file is your change and is not an orphan; a delete cannot be
# compiled in. Plug build-output dirs hold legitimate untracked .codex
# artifacts the concat never reads, so they are excluded.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repo
try {
    $paths = @('codex/compiler/...', 'codex/foreword/...')
    $orphans = @()
    foreach ($p in $paths) {
        $r = @(p4 reconcile -n $p 2>$null |
            Where-Object { $_ -match '\.codex' -and $_ -match ' - .*\badd\b' -and $_ -notmatch 'build-output' })
        $orphans += $r
    }
    if ($orphans.Count -gt 0) {
        Write-Host "FAIL: untracked .codex file(s) would be compiled into the seed but not carried to main:"
        $orphans | ForEach-Object { Write-Host "  $_" }
        Write-Host "  p4 add them (so the seed is reproducible) or remove them, then re-prove the seed."
        exit 1
    }
    Write-Host "check-seed-orphans: OK (no untracked source under codex/compiler or codex/foreword)"
    exit 0
} finally { Pop-Location }
