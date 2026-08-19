# check-effect-vocab.ps1 -- Guard the foreword effect declarations against the capability vocabulary
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

# check-effect-vocab.ps1 -- guard the foreword `effect` declarations against
# the capability vocabulary. That gap closed 2026-07-18 with this script as the
# permanent answer rather than an interim one.
# 
# THIS GUARD IS THE END STATE. DO NOT TRY TO REPLACE IT WITH A DERIVATION.
# 
# The capability model used to live in five hand-maintained expansions across
# three quires. Four of them are gone: capability-vocabulary, manifest-cap-id,
# boot-cap-mask-for and cdx-cap-to-kern-bits are all projections of one table
# now (codex/foreword/core/Capability.codex), so the guards that reconciled
# them -- check-cap-tables.ps1 and the in-compiler check-cap-vocab-coherent --
# were deleted along with the states they watched for.
# 
# The fifth cannot be. An `effect <Name> where ...` declaration is a SYNTAX
# form carrying its own operation signatures, and Codex has no metaprogramming
# that generates declarations from data. No table can emit one. The foreword
# effect declarations therefore cannot derive from the capability table, and a
# build-time guard comparing the two is the only instrument that will ever
# cover this list.
# 
# A new foreword effect with no capability, or a capability with no effect,
# would otherwise drift in silence. This is that guard.
# 
# The two lists are deliberately NOT 1:1. Some effects are pure or local and
# carry no hardware capability (State, Time, Random); some capabilities are not
# surfaced as a foreword effect (Concurrent, Capability, Flash). Those
# exceptions are listed below. Anything NOT accounted for on either side fails
# the build, so adding an effect or a capability forces a deliberate decision
# about its counterpart rather than a silent divergence.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path


# foreword effect declarations: `  effect <Name> where` under codex/foreword
$fwDir = (Join-Path $Repo 'codex\foreword')
$effectSet = @{}
foreach ($f in Get-ChildItem $fwDir -Recurse -Filter '*.codex' -File) {
    foreach ($line in ([System.IO.File]::ReadAllLines($f.FullName))) {
        if (($line -match '^\s+effect\s+(\w+)\s+where\s*$')) {
            $effectSet[$matches[1]] = $true
        }
    }
}
$effects = @(($effectSet.Keys | Sort-Object))


# the capability names: the rows of capability-table in the foreword.
# This used to read the capability-vocabulary array literal in
# TypeChecker.codex. That literal is gone -- capability-vocabulary is a
# projection of this table now -- so reading the vocabulary would be reading
# this table through one indirection. Read the source instead.
# 
# Base names only: a row named "Gpu.Compute" answers to the effect "Gpu", and
# the Read/Write refinements the vocabulary adds are directions rather than
# capabilities. The effect name is the part before the first dot.

$capFile = (Join-Path $Repo 'codex\foreword\core\Capability.codex')
$vocabSet = @{}
foreach ($line in ([System.IO.File]::ReadAllLines($capFile))) {
    if (($line -match 'cs-name\s*=\s*"([^"]+)"')) {
        $base = ($matches[1] -split '\.')[0]
        $vocabSet[$base] = $true
    }
}
$vocabBase = @(($vocabSet.Keys | Sort-Object))


if (((@($effects).Count -eq 0) -or (@($vocabBase).Count -eq 0))) {
    Write-Host ([string]'ERROR: parsed no effects (' + ([string]@($effects).Count + ([string]') or no vocabulary (' + ([string]@($vocabBase).Count + ') -- a file moved or its shape changed'))))
    exit 1
}


# intended asymmetry
$effectsWithoutCap = @('State', 'Time', 'Random')
$capsWithoutEffect = @('Concurrent', 'Capability', 'Flash', 'Process', 'Gpio', 'Uart', 'Spi', 'I2c', 'Adc', 'Power')
$errs = @()


foreach ($e in $effects) {
    if (((-not ($vocabBase -contains $e)) -and (-not ($effectsWithoutCap -contains $e)))) {
        $errs += ([string]'foreword effect ''' + ([string]$e + ([string]''' has no row in the capability table (codex/foreword/core/Capability.codex) and is not a known cap-less effect -- add a ''' + ([string]$e + ([string]''' row, or add ''' + ([string]$e + ''' to $effectsWithoutCap in this script if it is deliberately capability-free'))))))
    }
}
foreach ($c in $vocabBase) {
    if (((-not ($effects -contains $c)) -and (-not ($capsWithoutEffect -contains $c)))) {
        $errs += ([string]'capability ''' + ([string]$c + ([string]''' (capability-table, foreword) has no foreword ''effect ' + ([string]$c + ([string]' where'' declaration and is not a known effect-less capability -- add the effect, or add ''' + ([string]$c + ''' to $capsWithoutEffect in this script if it is deliberately effect-free'))))))
    }
}


if ((@($errs).Count -gt 0)) {
    Write-Host 'WARNING: the foreword effect declarations and the capability table have DRIFTED'
    foreach ($er in $errs) {
        Write-Host ([string]'  ' + $er)
    }
    exit 1
}
Write-Host ([string]'Effect/vocabulary tables MATCH (' + ([string]@($effects).Count + ([string]' foreword effects, ' + ([string]@($vocabBase).Count + ([string]' capability base names; ' + ([string]@($effectsWithoutCap).Count + ([string]' cap-less effects + ' + ([string]@($capsWithoutEffect).Count + ' effect-less caps allowed)'))))))))
exit 0
