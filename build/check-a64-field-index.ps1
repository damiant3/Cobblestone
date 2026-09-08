# check-a64-field-index.ps1 -- grade the ARM64 hard-coded field-index table
# against the record declarations it claims to describe. COMPILER-72.
#
# WHAT A PASS DOES NOT MEAN. This is a text check over two descriptions of the
# same records; it runs no guest and compiles nothing. A pass says the table
# AGREES with every record declaration it can be matched to. It does not say
# the table is reachable, that any test exercises it, or that arm64 codegen is
# correct. Nothing observes the table at runtime: on 2026-09-08 a deliberately
# wrong index passed every arm in the tree (COMPILER-72), which is why this
# check exists at all.
#
# WHAT IT GRADES. `a64-hardcoded-field-index` in
# codex/plugs/arm64/Arm64CodeGen3.codex is a FALLBACK: `a64-find-field-index-st`
# uses the record's real field list when the type carries one, and reaches the
# table only when it does not. Both paths must answer the same number, and the
# real path returns the field's position in DECLARATION order, so a table entry
# is correct exactly when it equals that position.
#
# THE SCOPE, and it is narrower than the table. The table is keyed by field
# NAME alone (it takes the type name and ignores it), so an entry can only be
# graded when the name is declared by exactly ONE record in the tree. Names
# declared by several records are AMBIGUOUS by construction: the table cannot
# be right for all of them, and this check reports the count rather than
# guessing which record an entry meant.
#
# Exit 0 clean, 1 on a disagreement.

[CmdletBinding()]
param([switch]$Quiet)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$tableFile = 'codex/plugs/arm64/Arm64CodeGen3.codex'
if (-not (Test-Path $tableFile)) { Write-Host "check-a64-field-index: $tableFile not found"; exit 1 }

$lines = Get-Content $tableFile

# The live table is the a64-hfi-* family. a64-hardcoded-field-index-disabled is
# a second copy that nothing calls; its entries are counted, not graded.
$liveEntries = @()
$deadEntries = 0
$inDead = $false
for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '^\s{2}a64-hardcoded-field-index-disabled\b') { $inDead = $true }
    elseif ($l -match '^\s{2}a64-[a-z0-9-]+\s*:\s') { $inDead = $false }

    if ($l -match 'a64-field-name-match\s+(?:fn|field-name)\s+"([^"]+)"\s+then\s+(-?\d+)\s*$') {
        if ($inDead) { $deadEntries++ }
        else { $liveEntries += [pscustomobject]@{ Field = $Matches[1]; Index = [int]$Matches[2]; Line = $i + 1 } }
    }
}

if ($liveEntries.Count -eq 0) {
    Write-Host "check-a64-field-index: FAIL -- parsed 0 live entries from $tableFile."
    Write-Host "  The table's shape changed, or the pattern no longer matches it. A check"
    Write-Host "  that reads nothing passes everything, so this is a failure, not a pass."
    exit 1
}

# Every record declaration in the tree, field names in declaration order.
$sources = Get-ChildItem -Recurse -Include '*.codex' -Path 'codex', 'apps' -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch '[\\/]build-output[\\/]' }

$declOf = @{}   # field name -> list of @{ Record; Index }
foreach ($f in $sources) {
    $text = [IO.File]::ReadAllText($f.FullName)
    foreach ($m in [regex]::Matches($text, '(?m)^\s*([A-Za-z][\w-]*)\s*=\s*record\s*\{(.*?)\}', 'Singleline')) {
        $rec = $m.Groups[1].Value
        $body = $m.Groups[2].Value
        $idx = 0
        foreach ($fieldChunk in ($body -split ',')) {
            if ($fieldChunk -match '^\s*([A-Za-z][\w-]*)\s*:') {
                $name = $Matches[1]
                if (-not $declOf.ContainsKey($name)) { $declOf[$name] = @() }
                $declOf[$name] += [pscustomobject]@{ Record = $rec; Index = $idx; File = $f.Name }
                $idx++
            }
        }
    }
}

$graded = 0; $ambiguous = 0; $orphan = 0
$bad = @()
$conflicted = @()
foreach ($e in $liveEntries) {
    if (-not $declOf.ContainsKey($e.Field)) { $orphan++; continue }
    $decls = @($declOf[$e.Field] | Sort-Object Record -Unique)
    if ($decls.Count -ne 1) {
        $ambiguous++
        # The table answers one index whatever the record. Where the records
        # that declare this name disagree about the position, that one answer
        # is provably wrong for at least one of them. Reported, not failed:
        # it is the table's design rather than a regression (COMPILER-72).
        $positions = @($decls | ForEach-Object { $_.Index } | Sort-Object -Unique)
        if ($positions.Count -gt 1) {
            $names = @($decls | ForEach-Object { $_.Record })
            $shown = if ($names.Count -gt 3) { ($names[0..2] -join ', ') + ", +$($names.Count - 3) more" } else { $names -join ', ' }
            $conflicted += "  '$($e.Field)' -> $($e.Index): $($names.Count) records declare it, at $($positions -join '/') ($shown)"
        }
        continue
    }
    $graded++
    if ($decls[0].Index -ne $e.Index) {
        $bad += "  $($tableFile):$($e.Line): '$($e.Field)' -> $($e.Index), but $($decls[0].Record) declares it at $($decls[0].Index) ($($decls[0].File))"
    }
}

if (-not $Quiet) {
    Write-Host "check-a64-field-index: $($liveEntries.Count) live entries, $graded graded, $ambiguous ambiguous (name in several records), $orphan naming no record in the tree."
    if ($deadEntries -gt 0) {
        Write-Host "  note: a64-hardcoded-field-index-disabled holds $deadEntries more entries and nothing calls it; it is not graded."
    }
    if ($conflicted.Count -gt 0) {
        Write-Host "  note: $($conflicted.Count) ambiguous entry/entries name records that disagree about the position,"
        Write-Host "        so the table's single answer is wrong for at least one of them (COMPILER-72, not a regression):"
        $conflicted | ForEach-Object { Write-Host $_ }
    }
}

if ($bad.Count -gt 0) {
    Write-Host "check-a64-field-index: FAIL -- $($bad.Count) entry/entries disagree with the record they describe."
    $bad | ForEach-Object { Write-Host $_ }
    Write-Host "  The table is a fallback for a64-find-field-index-st and must answer what the"
    Write-Host "  record's own field list answers, which is the field's position in declaration order."
    exit 1
}

if (-not $Quiet) { Write-Host "check-a64-field-index: OK." }
exit 0
