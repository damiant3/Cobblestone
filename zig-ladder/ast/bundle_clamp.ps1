# The whole-compiler subject: every chapter under codex/compiler except the
# driver, plus the harness that stands in for it.
#
# opening.codex is the one chapter that cannot come along -- it defines
# `opening`, and so does the harness. Leaving it out is not a subset: IR
# emission prunes to what the opening reaches, so what the unit contains is
# decided by what the harness calls, and the harness calls run-ir-pipeline
# exactly where compile-frontend-passes does. That is what pulls Simplify,
# Occurrence, LambdaLifting, Passes and IRCheck into the IR; bundling them
# without calling them would prune them straight back out.
#
# CodexEmitter and LirTargets ride along for completeness and will be pruned
# if this harness never reaches them.
#
# BootPaint stays the STUB. Swapping in the real 341-line painter looked like
# free completeness and was not: it cites Foreword chapter CCE, which makes
# Resolve-PlugForewords add a Foreword--CCE beside the Parsmi--CCE the list
# already carries, and the seed rejects the unit with eight CDX3001 duplicate
# constructors on CharClass. The painter is unreachable from this harness and
# would be pruned from the IR regardless, so it buys nothing to pay for.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bundle_fibx.ps1') `
    -Harness 'ClampHarness.codex' `
    -OutName 'clamp-subject.codex' `
    -PlugName 'clamp-subject' `
    -ExtraChapters @(
        'codex/compiler/IR/Occurrence.codex',
        'codex/compiler/IR/ResolveTypes.codex',
        'codex/compiler/IR/IRCheck.codex',
        'codex/compiler/IR/LambdaLifting.codex',
        'codex/compiler/IR/Simplify.codex',
        'codex/compiler/IR/Passes.codex',
        'codex/compiler/IR/LirTargets.codex',
        'codex/compiler/Emit/CodexEmitter.codex')
