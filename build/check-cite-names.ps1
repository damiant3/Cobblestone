# check-cite-names.ps1 -- refuse a `cites` line whose parenthesised NAME does not
# exist in the chapter it names.
#
# Hand-maintained, like check-backlog-ids.ps1 beside it. No generator emits it.
#
# THE CHAPTER HALF OF A CITE IS CHECKED AND THE NAME HALF IS NOT. The host
# resolver raises 3010 for a chapter it cannot find (quire-map.ps1, and the
# compiler never sees a cite because the host splices chapters in before it
# reads a byte), while the names in parentheses reach the AST as
# ACitesDecl.selected-names and nothing compares them against anything. So a
# typo, a renamed definition, or a name left behind by a move reads as a
# declaration and is decoration. Found reviewing Steve Howell's PR 116 (reek,
# 2026-09-02), whose cite line on Opening named `compile-flags-of`, a definition
# that exists nowhere; it survived a self-compile and a fixed-point comparison
# because nothing looks.
#
# MEASURED BEFORE THIS WAS WRITTEN (fester, 2026-09-07): of 11,902 cite lines in
# the tree, 67 carry a name list at all, holding 121 names, and 121 of 121
# resolve. There is no live instance, so this is a guard against drift rather
# than a repair, which is why it is host-side: the compiler-side refusal would
# have cost a seed, a token and a fleet-wide seed move to protect a surface that
# is already clean.
#
# WHY THE DEFINITION TEST IS DELIBERATELY GENEROUS. A false refusal here reds
# the gate on correct source, which is worse than the silence it replaces. So a
# name counts as defined if the chapter carries it as a signature, as a
# definition or type, or as a variant constructor; a name is reported ONLY when
# none of those matched. The 121-of-121 pass over the tree is what says the
# generosity is not hiding everything: the arms below say it can still fail.
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # A single file, for one register or for a sabotage arm.
    [string]$File = '',
    # Prove the checker can still return the other answer (L-FALSIF).
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'quire-map.ps1')

# A cite naming a quire that is not in $QuireDirs is not an error: the compiler's
# own chapters come in through concat rather than through a directory, so fall
# back to an index of every Chapter: header. Resolving by directory alone
# reported 39 of 121 names unresolvable, and every one was that case -- a number
# about the resolver rather than about the tree.
# A chapter NAME is not unique: ten files declare `Chapter: Opening`, one per
# app plus the compiler's own. A first-wins index answered
# apps\browser\opening.codex for the compiler's cite and called `codex-opening`
# undefined, which is a false refusal and would have redded the gate on correct
# source. So the index keeps EVERY claimant and a name counts as defined if ANY
# of them defines it. That is weaker than resolving the one true chapter, and
# deliberately: it cannot mis-attribute, and the case this check exists for is a
# name defined by NOBODY.
function Get-ChapterIndex {
    param([string]$Repo)
    $idx = @{}
    foreach ($f in (Get-ChildItem -Path $Repo -Recurse -Filter '*.codex' -File -ErrorAction SilentlyContinue)) {
        if (Test-Excluded -Path $f.FullName) { continue }
        foreach ($l in [System.IO.File]::ReadLines($f.FullName)) {
            if ($l -match '^Chapter:\s*(\S+)') {
                if (-not $idx.ContainsKey($matches[1])) { $idx[$matches[1]] = [System.Collections.Generic.List[string]]::new() }
                $idx[$matches[1]].Add($f.FullName)
                break
            }
        }
    }
    return $idx
}

# build-output holds concatenated units the build wrote, and old/ is the retired
# reference compiler. Neither is source, and both carry copies of cite lines
# that would be counted twice.
function Test-Excluded {
    param([string]$Path)
    return ($Path -match '[\\/](build-output|old)[\\/]')
}

function Get-DefinedNames {
    param([string]$Path)
    $set = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($l in [System.IO.File]::ReadLines($Path)) {
        # Column-2 prose is exactly one leading space and defines nothing.
        if ($l -match '^ [^ ]') { continue }
        if ($l -match '^\s{0,5}([A-Za-z_][A-Za-z0-9_-]*)\s*:') { [void]$set.Add($matches[1]) }
        if ($l -match '^\s{0,5}([A-Za-z_][A-Za-z0-9_-]*)\s*=') { [void]$set.Add($matches[1]) }
        foreach ($m in [regex]::Matches($l, '\|\s*([A-Za-z_][A-Za-z0-9_-]*)')) { [void]$set.Add($m.Groups[1].Value) }
    }
    return $set
}

$citePat = '^\s*cites\s+([A-Za-z0-9]+)\s+chapter\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)'

$sources = if ($File) { @(Get-Item -LiteralPath $File) }
           else { @(Get-ChildItem -Path $Root -Recurse -Filter '*.codex' -File -ErrorAction SilentlyContinue |
                    Where-Object { -not (Test-Excluded -Path $_.FullName) }) }

$chapterIndex = Get-ChapterIndex -Repo $Root
$defCache = @{}
$bad = [System.Collections.Generic.List[string]]::new()
$lines = 0
$names = 0

foreach ($src in $sources) {
    $n = 0
    foreach ($l in [System.IO.File]::ReadLines($src.FullName)) {
        $n++
        $m = [regex]::Match($l, $citePat)
        if (-not $m.Success) { continue }
        $lines++
        $quire = $m.Groups[1].Value
        $chapter = $m.Groups[2].Value
        $cited = @($m.Groups[3].Value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

        $candidates = [System.Collections.Generic.List[string]]::new()
        $dir = $QuireDirs[$quire]
        if ($dir) {
            $p = Join-Path $Root (Join-Path $dir "$chapter.codex")
            if (Test-Path -PathType Leaf $p) { $candidates.Add($p) }
        }
        if ($candidates.Count -eq 0 -and $chapterIndex.ContainsKey($chapter)) {
            foreach ($c in $chapterIndex[$chapter]) { $candidates.Add($c) }
        }
        # An unresolvable CHAPTER is quire-map's refusal, not this one (R-ONE).
        if ($candidates.Count -eq 0) { continue }

        foreach ($name in $cited) {
            $names++
            $found = $false
            foreach ($c in $candidates) {
                if (-not $defCache.ContainsKey($c)) { $defCache[$c] = Get-DefinedNames -Path $c }
                if ($defCache[$c].Contains($name)) { $found = $true; break }
            }
            if (-not $found) {
                $rel = $src.FullName.Replace($Root + [System.IO.Path]::DirectorySeparatorChar, '')
                $bad.Add(("{0}:{1}: cites {2} chapter {3} names '{4}', which {3} does not define" -f $rel, $n, $quire, $chapter, $name))
            }
        }
    }
}

if ($SelfTest) {
    # Three arms against one chapter: two names it does define, and one it
    # cannot. An instrument whose verdicts have never been shown to fail is not
    # evidence, and this one reports a clean tree.
    $units = Join-Path $Root 'codex\foreword\core\Units.codex'
    if (-not (Test-Path -PathType Leaf $units)) {
        Write-Output 'SELFTEST: codex\foreword\core\Units.codex is missing; cannot grade.'
        exit 2
    }
    $d = Get-DefinedNames -Path $units
    $arms = @(
        @{ Name = 'Duration'; Want = $true },
        @{ Name = 'Frequency'; Want = $true },
        @{ Name = 'no-such-definition-xyzzy'; Want = $false }
    )
    $failed = 0
    foreach ($a in $arms) {
        $got = $d.Contains($a.Name)
        $ok = ($got -eq $a.Want)
        if (-not $ok) { $failed++ }
        Write-Output ("  {0,-28} defined={1,-6} expected={2,-6} {3}" -f $a.Name, $got, $a.Want, $(if ($ok) { 'ok' } else { 'FAILED' }))
    }
    if ($failed -gt 0) { Write-Output "SELFTEST FAILED: $failed of $($arms.Count) arm(s)"; exit 1 }
    Write-Output 'SELFTEST OK: the checker separates a defined name from an absent one.'
    exit 0
}

if ($bad.Count -gt 0) {
    foreach ($b in $bad) { Write-Output "CITE NAME NOT DEFINED  $b" }
    Write-Output ''
    Write-Output "check-cite-names: $($bad.Count) cited name(s) resolve to nothing, over $lines cite line(s) carrying a name list."
    Write-Output 'A cited name is a declaration, not decoration: correct the name, or drop it from the list.'
    exit 1
}

Write-Output "check-cite-names: $lines cite line(s) with a name list, $names name(s), all defined."
exit 0
