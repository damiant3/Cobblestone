# The hosted-compiler subject: the whole rung's chapters, a different driver.
# Identical chapter set to bundle_whole.ps1 for the same reason that one
# wraps bundle_fibx.ps1 -- one list, kept in one place.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bundle_fibx.ps1') `
    -Harness 'CodexIrHarness.codex' `
    -OutName 'codexir-subject.codex' `
    -PlugName 'codexir-subject' `
    -ExtraChapters @(
        'codex/compiler/IR/Occurrence.codex',
        'codex/compiler/IR/IRCheck.codex',
        'codex/compiler/IR/LambdaLifting.codex',
        'codex/compiler/IR/Simplify.codex',
        'codex/compiler/IR/Passes.codex',
        'codex/compiler/IR/LirTargets.codex',
        'codex/compiler/Emit/CodexEmitter.codex')
