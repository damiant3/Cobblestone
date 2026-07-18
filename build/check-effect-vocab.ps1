# check-effect-vocab.ps1 -- guard the foreword `effect` declarations against
# the capability vocabulary. BACKLOG 1.13.
#
# The capability model lives in three hand-maintained lists. Two are already
# guarded: the in-compiler check-cap-vocab-coherent ties every
# capability-vocabulary name to a manifest cap id (runs on every compile), and
# check-cap-tables.ps1 ties the boot and verified-load bit tables together
# (BACKLOG 1.12). The THIRD list -- the foreword `effect <Name> where`
# declarations -- was unguarded: a new foreword effect with no capability, or a
# capability with no effect, drifted in silence. This is that guard.
#
# The two lists are deliberately NOT 1:1. Some effects are pure or local and
# carry no hardware capability (State, Time, Random); some capabilities are not
# surfaced as a foreword effect (Concurrent, Capability, Flash). Those
# exceptions are listed below. Anything NOT accounted for on either side fails
# the build, so adding an effect or a capability forces a deliberate decision
# about its counterpart rather than a silent divergence.
#
# Usage: check-effect-vocab.ps1  (prints MATCH + exit 0, or the drift + exit 1)

param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# --- foreword effect declarations: `  effect <Name> where` under codex/foreword
$fwDir = Join-Path $Repo 'codex\foreword'
$effectSet = @{}
foreach ($f in Get-ChildItem -Recurse -File -Filter *.codex $fwDir) {
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($line -match '^\s+effect\s+(\w+)\s+where\s*$') { $effectSet[$matches[1]] = $true }
    }
}
$effects = @($effectSet.Keys | Sort-Object)

# --- capability-vocabulary: the array literal in TypeChecker.codex, base names
# (drop the ".Read"/".Write"/".Compute"/".Memory" sub-scopes; the effect name
# is the part before the first dot).
$tcFile = Join-Path $Repo 'codex\compiler\Types\TypeChecker.codex'
$vocabSet = @{}
foreach ($line in [System.IO.File]::ReadAllLines($tcFile)) {
    if ($line -match '^\s+capability-vocabulary\s*=\s*\[(.+)\]\s*$') {
        foreach ($m in [regex]::Matches($matches[1], '"([^"]+)"')) {
            $base = ($m.Groups[1].Value -split '\.')[0]
            $vocabSet[$base] = $true
        }
    }
}
$vocabBase = @($vocabSet.Keys | Sort-Object)

if ($effects.Count -eq 0 -or $vocabBase.Count -eq 0) {
    Write-Host "ERROR: parsed no effects ($($effects.Count)) or no vocabulary ($($vocabBase.Count)) -- a file moved or its shape changed"
    exit 1
}

# --- intended asymmetry (documented in BACKLOG 1.13)
$effectsWithoutCap = @('State', 'Time', 'Random')            # pure/local, no hardware capability
$capsWithoutEffect = @('Concurrent', 'Capability', 'Flash')  # capability not surfaced as an effect

$errs = [System.Collections.Generic.List[string]]::new()
foreach ($e in $effects) {
    if (($vocabBase -notcontains $e) -and ($effectsWithoutCap -notcontains $e)) {
        $errs.Add("foreword effect '$e' has no capability-vocabulary entry (TypeChecker) and is not a known cap-less effect -- add a '$e' vocabulary name, or add '$e' to `$effectsWithoutCap in this script if it is deliberately capability-free")
    }
}
foreach ($c in $vocabBase) {
    if (($effects -notcontains $c) -and ($capsWithoutEffect -notcontains $c)) {
        $errs.Add("capability '$c' (capability-vocabulary) has no foreword 'effect $c where' declaration and is not a known effect-less capability -- add the effect, or add '$c' to `$capsWithoutEffect in this script if it is deliberately effect-free")
    }
}

if ($errs.Count -gt 0) {
    Write-Host "WARNING: foreword effects and the capability vocabulary have DRIFTED -- BACKLOG 1.13"
    foreach ($er in $errs) { Write-Host "  $er" }
    exit 1
}

Write-Host "Effect/vocabulary tables MATCH ($($effects.Count) foreword effects, $($vocabBase.Count) capability base names; $($effectsWithoutCap.Count) cap-less effects + $($capsWithoutEffect.Count) effect-less caps allowed)"
exit 0
