# check-builtin-alloc.ps1 -- the `fixed` rows in the builtin registry are
# correct by inspection of code no registry row covers. This notices when that
# code changes.
#
# Hand-written. There is no generator for this file.
#
# WHY THIS EXISTS. `bs-alloc` on `BuiltinSpec` is keyed by builtin NAME, and
# the allocation happens a level below the name: `vec-add` names
# `emit-vec-add-builtin`, which allocates nothing itself and delegates to
# `emit-vec-arith-core`, which is where the 16 bytes are. `__list-tail` is
# worse: it names `emit-helper-call-1`, and the bytes are in the `__list_tail`
# runtime helper, which has no registry row at all and never will.
#
# So the fourteen rows reading `fixed` were established (CostModel.md 5.1,
# 2026-09-07) by reading functions that nothing connects back to the rows. A
# `fixed` row that stops being true ACCEPTS a `bounded fixed` promise it
# cannot keep, where `unknown` would have refused, and no test in the tree can
# see it: the arms compile programs, and a per-call allocation that starts
# following an input still compiles.
#
# WHAT THIS CHECKS, and it is deliberately two things, because the first one
# alone is only as wide as the spellings it knows (L-CENSUS):
#
#   1. Every allocation site in a pinned body takes an IMMEDIATE. A literal
#      `heap-bump-imm st 8` is the same bytes every call; `heap-bump-imm st n`
#      is not, and that is the transition from `fixed` to something else.
#
#   2. The pinned bodies are digested. ANY edit to one reds this check, even
#      an allocation spelled a way rule 1 has never heard of. That is the
#      backstop: rule 1 can only refuse what it recognises, rule 2 refuses
#      change itself and makes a human re-read.
#
# A red here is not "you broke something". It is "a row's evidence moved, go
# and re-measure it, then run -Update". The measured byte counts are in
# CostModel.md 5.1 beside each row.
#
# Usage:
#   build/check-builtin-alloc.ps1           # verify
#   build/check-builtin-alloc.ps1 -Update   # re-pin after a deliberate change
[CmdletBinding()]
param(
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Builtins = Join-Path $Repo 'codex\compiler\Types\Builtins.codex'
$EmitDir  = Join-Path $Repo 'codex\compiler\Emit'
$Witness  = Join-Path $PSScriptRoot 'builtin-alloc-witness.txt'

# The allocation spellings this tree uses inside the emitters. Rule 2 covers
# anything not listed here; this list only decides what rule 1 can JUDGE.
$AllocCalls = @('heap-bump-imm', 'emit-bivy-alloc', 'alloc-zeroed')

$fail = @()
$notes = @()

# -- 1. the rows that claim `fixed`, and the emitter each one names

$rows = @()
foreach ($line in [System.IO.File]::ReadLines($Builtins)) {
    if ($line -match 'bs-name = "([^"]+)", bs-alloc = "fixed"') {
        $name = $matches[1]
        $emit = ''
        if ($line -match 'bs-emit = Just \(\\s a -> ([a-z0-9-]+)') { $emit = $matches[1] }
        $rows += [pscustomobject]@{ Name = $name; Emit = $emit }
    }
}
if ($rows.Count -eq 0) { Write-Host 'REFUSED: no rows reading "fixed" were found. The pattern stopped matching, which is not the same as a clean tree.'; exit 1 }

# -- 2. resolve a function body by name across the Emit chapters
#
# A body runs from its signature line to the next definition at the same
# indent. Prose between definitions is column-2 and is skipped by the same
# rule, which is why the scan keys on the two-space signature form.

$emitFiles = Get-ChildItem $EmitDir -Filter *.codex -File
function Get-Body([string]$fn) {
    foreach ($f in $emitFiles) {
        $lines = [System.IO.File]::ReadAllLines($f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "^  $([regex]::Escape($fn)) :") {
                $body = @($lines[$i])
                for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                    if ($lines[$j] -match '^  [A-Za-z_][A-Za-z0-9_-]* :') { break }
                    if ($lines[$j] -match '^Section: ') { break }
                    $body += $lines[$j]
                }
                return [pscustomobject]@{ File = $f.Name; Text = ($body -join "`n") }
            }
        }
    }
    return $null
}

# -- 3. the set to pin: where each row's BYTES actually come from
#
# This table is the missing link the registry does not carry, and it is
# written by hand on purpose. Crawling the emitters instead pins `emit-expr`,
# which every one of them calls to evaluate its ARGUMENTS: argument evaluation
# is charged to the argument expression and not to the builtin, which is how
# every row in CostModel.md 5.1 is read. A check that reds on unrelated
# codegen churn teaches people to run -Update without looking, which is worse
# than no check.
#
# A row reading `fixed` with no entry here is a FAILURE, not a skip: it is a
# claim whose evidence nobody has written down.

$Sites = @{
    'char-to-text'        = @('emit-char-to-text-builtin')
    'char-encode'         = @('emit-char-encode-builtin')
    '__list-tail'         = @('emit-list-tail')
    '__linked-list-push'  = @('emit-linked-list-push-builtin')
    'vec-splat'           = @('emit-vec-splat-builtin')
    'vec4-splat'          = @('emit-vec4-splat-builtin')
    'vec-load-at'         = @('emit-vec-load-at-builtin')
    'vec-add'             = @('emit-vec-arith-core')
    'vec-sub'             = @('emit-vec-arith-core')
    'vec-mul'             = @('emit-vec-arith-core')
    'vec-div'             = @('emit-vec-arith-core')
    'vec-select'          = @('emit-vec-select-builtin')
    'vec-empty'           = @('emit-list', 'emit-list-bivy')
    'vec-singleton'       = @('emit-list', 'emit-list-bivy')
}

$pin = [ordered]@{}
foreach ($r in $rows) {
    if (-not $Sites.ContainsKey($r.Name)) {
        $fail += "$($r.Name) reads 'fixed' and no allocation site is recorded for it. Measure where its bytes come from, add it to `$Sites, and record the count in CostModel.md 5.1."
        continue
    }
    foreach ($fn in $Sites[$r.Name]) {
        if ($pin.Contains($fn)) { continue }
        $b = Get-Body $fn
        if (-not $b) { $fail += "$($r.Name): its recorded site $fn was not found under codex/compiler/Emit. Either it was renamed, or this table is stale."; continue }
        $pin[$fn] = $b
    }
}

# -- 4. rule 1: every allocation site in a pinned body takes an immediate

foreach ($fn in $pin.Keys) {
    foreach ($bl in ($pin[$fn].Text -split "`n")) {
        foreach ($call in $AllocCalls) {
            if ($bl -notmatch "\b$([regex]::Escape($call))\b") { continue }
            # the argument list after the call, to the end of the line
            $after = $bl.Substring($bl.IndexOf($call) + $call.Length)
            # an immediate is a bare number, or an arithmetic form over numbers
            if ($after -match '\(\s*[a-z]') { continue }   # a parenthesised expression is judged below
            if ($after -match '\b\d+\b') { continue }
            $fail += "$fn ($($pin[$fn].File)): '$call' with no immediate on: $($bl.Trim())"
        }
    }
}

# -- 5. rule 2: the pinned bodies are what they were

$have = [ordered]@{}
foreach ($fn in $pin.Keys) {
    $sha = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($pin[$fn].Text))).Replace('-','')
    $have[$fn] = "$fn $($pin[$fn].File) $sha"
}

if ($Update) {
    $out = @(
        '# builtin-alloc-witness -- pinned bodies behind the registry rows reading "fixed".',
        '# Regenerated by build/check-builtin-alloc.ps1 -Update. Re-measure the row in',
        '# CostModel.md 5.1 BEFORE you re-pin: this file records what the code was, not',
        '# that it is right.',
        ''
    ) + @($have.Values)
    Set-Content -LiteralPath $Witness -Encoding ascii -Value $out
    Write-Host "re-pinned $($have.Count) bodies into $Witness"
    exit 0
}

if (-not (Test-Path -PathType Leaf $Witness)) {
    Write-Host "REFUSED: no witness file at $Witness. Run -Update to create it, after reading CostModel.md 5.1."
    exit 1
}

$want = @{}
foreach ($l in [System.IO.File]::ReadLines($Witness)) {
    if ($l -match '^\s*#' -or -not $l.Trim()) { continue }
    $p = $l -split '\s+'
    if ($p.Count -ge 3) { $want[$p[0]] = $l.Trim() }
}

foreach ($fn in $have.Keys) {
    if (-not $want.ContainsKey($fn)) {
        $fail += "$fn is reached by a 'fixed' row and is not pinned. Re-measure it, then -Update."
        continue
    }
    if ($want[$fn] -ne $have[$fn]) {
        $fail += "$fn CHANGED since it was pinned. Re-read it against CostModel.md 5.1 and re-measure the rows that reach it, then -Update."
    }
}
foreach ($fn in $want.Keys) {
    if (-not $have.Contains($fn)) {
        $notes += "$fn is pinned and no longer reached by any 'fixed' row; -Update drops it"
    }
}

foreach ($n in $notes) { Write-Host "  note: $n" }

if ($fail.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($fail.Count) finding(s) behind the registry's 'fixed' rows:"
    foreach ($f in $fail) { Write-Host "  $f" }
    Write-Host ''
    Write-Host "  A 'fixed' row that stops being true ACCEPTS a bounded fixed promise it"
    Write-Host '  cannot keep, and no arm in the tree can see that. The byte counts are in'
    Write-Host '  CostModel.md 5.1 beside each row.'
    exit 1
}

Write-Host "check-builtin-alloc: OK ($($rows.Count) rows reading 'fixed', $($have.Count) bodies pinned)"
