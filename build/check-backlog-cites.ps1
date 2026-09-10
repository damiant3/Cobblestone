# check-backlog-cites.ps1 -- resolve every `file:line` citation in every
# *-backlog.md against the tree.
#
# Hand-written. There is no generator for this file.
#
#   pwsh build/check-backlog-cites.ps1                 # every *-backlog.md
#   pwsh build/check-backlog-cites.ps1 -Path <file>    # one register
#   pwsh build/check-backlog-cites.ps1 -Strict         # exit 1 on a finding
#
# IT GRADES TWO CLAIMS OF DIFFERENT STRENGTH, and says which it used.
#
# THE WEAK GRADE, available on every citation: the cited file exists and the
# cited line is within it. That catches L-ROWROT kind 3, a citation into a file
# that shrank or went away, which is otherwise silent and reads exactly like a
# live pointer. It cannot catch kind 2.
#
# THE STRONG GRADE, available wherever the row names a SYMBOL beside the line,
# which most rows do: the named symbol must occur within `-Window` lines of the
# cited line. That is the grade that catches kind 2, the dangerous kind, where
# the line still exists and now names unrelated code. Calibrated against the
# register revision that preceded the 2026-09-09 audit, which holds five known
# drifted citations and their corrections: the strong grade flags exactly those
# five at #179 and is silent on them at head.
#
# A row that names no symbol beside its line is reported as UNGRADED rather
# than as passing, because a check that counts an unaskable question as an
# answer is the shape this tree keeps paying for. Cite the symbol.
#
# WARN-ONLY BY DEFAULT. A named symbol can legitimately sit away from its
# citation, for instance where a row cites a call site and names the callee, so
# a finding here is a question rather than a verdict. `-Strict` exits 1 for a
# caller that wants the failure.
#
# THE FALSE-POSITIVE RATE IS HIGH, MEASURED, AND IS WHY THIS IS NOT GATED.
# Its nine kind-2 findings against `compiler-backlog.md` at head were worked
# through one at a time on 2026-09-09, immediately after it shipped. ONE was a
# real drift (`precise-escape-bag`, cited at 1306 and living at 1823, now
# corrected). The other eight were the checker, and the cause is structural
# rather than a tuning problem: the strong grade gathers every backticked word
# near the citation and looks for it in THE ONE CITED FILE, so a row that names
# two files reports a finding when both citations are correct.
# `emit-record-set-builtin` is cited beside `X86_64Compound.codex:1891` and is
# defined in `X86_64Builtins.codex`, which the same row cites correctly two
# clauses earlier. Two more findings came from the window being 8 lines when
# the symbol sat 10 and 24 away, and one from the extractor taking `cites` and
# `SyntaxNodes` as symbols out of a sentence whose actual subject,
# `copy-sx-diag`, is on the cited line.
#
# So: one finding in nine was rot. Treat the output as a reading list, not a
# defect list, and do not put it in a gate until the strong grade can tell
# which file a symbol belongs to.
#
# THE LEAF-NAME TRAP, which is why the resolver is written the long way. A
# citation like `Parser.codex:1302` names no directory, and this tree holds
# several files by that name: the compiler's `Syntax/Parser.codex` at 2170
# lines and a plug's at 645. Resolving by leaf name alone picks whichever the
# directory walk reaches first, and on 2026-09-09 that produced FIVE false
# "points past the end of its file" findings in a row against a register whose
# citations were correct. A false finding here is worse than no check: it sends
# a reader to repair a pointer that is fine. So the resolver tries the cited
# path first, then the compiler's own subdirectories, then the compiler tree,
# and only then the rest, and it REPORTS which file it resolved to so a wrong
# match is visible rather than silent.
[CmdletBinding()]
param(
    [string]$Path = '',
    [int]$Window = 8,
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

function Resolve-Cite {
    param([string]$Cited)
    $c = $Cited -replace '/', '\'
    $probes = @(
        $c,
        "codex\compiler\$c",
        "codex\compiler\Syntax\$c",
        "codex\compiler\Types\$c",
        "codex\compiler\Emit\$c",
        "codex\compiler\IR\$c",
        "codex\compiler\Ast\$c",
        "codex\compiler\Core\$c",
        "build\$c",
        "docs\$c"
    )
    foreach ($cand in $probes) {
        $p = Join-Path $Repo $cand
        if (Test-Path $p -PathType Leaf) { return $p }
    }
    $leaf = Split-Path $c -Leaf
    # A citation that carries ANY directory is matched on the path SUFFIX
    # before any leaf search. `csharp/run.ps1` must not become
    # `apps/fireworks/run.ps1`: on the first fleet run of this script it did,
    # and produced eight confident "line past end" verdicts against citations
    # that were correct. The header's own leaf-name warning was written before
    # its resolver obeyed it.
    if ($c.Contains('\')) {
        $suffix = '\' + $c.TrimStart('\')
        $hit = Get-ChildItem $Repo -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -notmatch 'build-output|\\old\\' -and $_.FullName.EndsWith($suffix) } |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    $hit = Get-ChildItem (Join-Path $Repo 'codex\compiler') -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if ($hit) { return $hit.FullName }
    $hit = Get-ChildItem $Repo -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch 'build-output|\\old\\' } |
           Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

if ($Path) {
    $registers = @((Resolve-Path $Path).Path)
} else {
    $registers = @(Get-ChildItem $Repo -Recurse -Filter '*-backlog.md' -File |
                   Where-Object { $_.FullName -notmatch 'build-output|\\old\\' } |
                   ForEach-Object { $_.FullName })
}

Write-Host "check-backlog-cites: $($registers.Count) register(s)"

$total = 0
$ungraded = 0
$strong = 0
$findings = @()
foreach ($reg in $registers) {
    $lines = [System.IO.File]::ReadAllLines($reg)
    $relReg = if ($reg.StartsWith($Repo)) { $reg.Substring($Repo.Length + 1) } else { $reg }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in [regex]::Matches($lines[$i], '`([A-Za-z0-9_/\\.-]+\.(?:codex|ps1|md))[:](\d+)`')) {
            $total++
            $cited = $m.Groups[1].Value
            $ln = [int]$m.Groups[2].Value
            $f = Resolve-Cite $cited
            if (-not $f) {
                $findings += "${relReg}:$($i+1)  NO SUCH FILE      $cited"
                continue
            }
            $body = [System.IO.File]::ReadAllLines($f)
            $rel = if ($f.StartsWith($Repo)) { $f.Substring($Repo.Length + 1) } else { $f }
            if ($ln -gt $body.Count) {
                $findings += "${relReg}:$($i+1)  LINE PAST END     ${cited}:$ln -> $rel has $($body.Count) lines"
                continue
            }
            # The strong grade. Symbols are taken from a window of the row's
            # TEXT around the citation, not the whole row: a register row runs
            # to thousands of characters and names symbols from several files,
            # so the whole row would offer a match for almost anything.
            $from = [Math]::Max(0, $m.Index - 220)
            $to = [Math]::Min($lines[$i].Length, $m.Index + $m.Length + 220)
            $near = $lines[$i].Substring($from, $to - $from)
            $syms = @()
            foreach ($sm in [regex]::Matches($near, '`([a-zA-Z_][A-Za-z0-9_-]*)`')) {
                $s = $sm.Groups[1].Value
                if ($s -match '\.(codex|ps1|md)$') { continue }
                if ($s.Length -lt 4) { continue }
                $syms += $s
            }
            $syms = @($syms | Sort-Object -Unique)
            if ($syms.Count -eq 0) { $ungraded++; continue }
            $strong++
            $lo = [Math]::Max(0, $ln - 1 - $Window)
            $hi = [Math]::Min($body.Count - 1, $ln - 1 + $Window)
            $hay = ($body[$lo..$hi] -join "`n")
            $hit = $false
            foreach ($s in $syms) { if ($hay.Contains($s)) { $hit = $true; break } }
            if (-not $hit) {
                $findings += "${relReg}:$($i+1)  SYMBOL NOT NEAR   ${cited}:$ln -> $rel, looked for: $($syms -join ', ')"
            }
        }
    }
}

Write-Host "check-backlog-cites: $total citation(s) with a line number; $strong graded against a named symbol, $ungraded name no symbol and are UNGRADED"
if ($findings.Count -gt 0) {
    Write-Host ''
    foreach ($f in $findings) { Write-Host "  $f" }
    Write-Host ''
    $k3 = @($findings | Where-Object { $_ -match 'NO SUCH FILE|LINE PAST END' }).Count
    $k2 = $findings.Count - $k3
    Write-Host "check-backlog-cites: $($findings.Count) finding(s), $k3 kind 3 and $k2 kind 2."
    if ($k3 -gt 0) {
        Write-Host '  KIND 3, a citation into a file that shrank or went away: it reads as a live'
        Write-Host '  pointer and leads nowhere. This one is a verdict.'
    }
    if ($k2 -gt 0) {
        Write-Host "  KIND 2, the line exists and the symbol the row names is not within $Window lines"
        Write-Host '  of it. This is a QUESTION, not a verdict: a row may cite a call site and name'
        Write-Host '  the callee. Read the row, then either correct the line or cite the symbol.'
    }
    if ($Strict) { exit 1 }
    exit 0
}

Write-Host 'check-backlog-cites: every citation resolves to a line that exists.'
Write-Host '  This does NOT mean the lines still name what the rows claim; that is kind 2'
Write-Host '  and no check in this tree decides it. See the header.'
exit 0
