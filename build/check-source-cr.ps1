# check-source-cr.ps1 -- refuse a .codex source carrying a carriage return that
# is not a line terminator.
#
# Hand-written; no generator under codex/build/ emits this.
#
# WHY THIS IS A GATE AND NOT A NOTE. A stray CR is invisible in every ordinary
# reader: PowerShell's Get-Content normalises on read and so does every harness
# that reaches the compiler through one, so the tree compiles and the defect
# only appears to something that reads the bytes faithfully. Two ways it bites,
# both measured on codex/foreword 2026-09-07:
#
#   a CR inside a TYPE halts the compile        CDX1000
#   a CR at the end of a `Chapter:` header       CDX3007
#
# The second is the one that hides. The header line ends `Chapter: FFT<CR>`, so
# the chapter's NAME is "FFT\r"; the chapter is present in the unit and
# `cites Foreword chapter FFT` still cannot match it, and the unit then reports
# a missing chapter, which is not what is wrong with it.
#
# Perforce does not prevent this and no per-file setting causes it. Measured:
# the fifteen foreword files and their controls are all filetype `unicode` on a
# client whose LineEnd is `local`. Line-end translation rewrites the TERMINATOR;
# a CR that is not one is content, and content is preserved exactly. So the only
# thing that keeps this out of the tree is a reader, and this is it.
[CmdletBinding()]
param(
    # Report and exit 0 instead of failing. For a census.
    [switch]$WarnOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# EVERY root that holds .codex, not just the two big ones. The first version of
# this scanned codex/ and apps/ only -- 3,885 files -- and reported "clean" for
# a tree that also carries .codex under bench, build, docs, shaders and tools.
# Those 137 were in fact clean when measured 2026-09-07, so nothing was hidden,
# but a guard whose population is narrower than its claim is the same defect it
# exists to catch, one level up (L-DENOM).
#
# old/ is excluded deliberately: it is the retired reference compiler and
# nothing in it may be edited, so a finding there is unactionable. It also holds
# a DIRECTORY named Codex.Emit.Codex, which a -Filter *.codex without -File
# returns and then fails to read.
$roots = @('codex', 'apps', 'bench', 'build', 'docs', 'shaders', 'tools') |
    ForEach-Object { Join-Path $Repo $_ } | Where-Object { Test-Path $_ }

$bad = [System.Collections.Generic.List[object]]::new()
$scanned = 0

foreach ($root in $roots) {
    foreach ($f in (Get-ChildItem $root -Filter *.codex -Recurse -File)) {
        $scanned++
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        $offsets = [System.Collections.Generic.List[int]]::new()
        for ($i = 0; $i -lt $b.Length; $i++) {
            if ($b[$i] -ne 13) { continue }
            # A terminator is exactly CR LF with no CR before it. Anything else
            # -- a doubled CR, a CR before a non-LF byte, a trailing CR -- is
            # content that does not belong in source.
            $isTerminator = ($i + 1 -lt $b.Length) -and ($b[$i + 1] -eq 10) -and (($i -eq 0) -or ($b[$i - 1] -ne 13))
            if (-not $isTerminator) { [void]$offsets.Add($i) }
        }
        if ($offsets.Count -gt 0) {
            $rel = $f.FullName.Substring($Repo.Length + 1)
            # Name the line, because "offset 12" sends a reader to a hex editor
            # and "line 1, the Chapter: header" sends them to the defect.
            $line = 1
            for ($j = 0; $j -lt $offsets[0]; $j++) { if ($b[$j] -eq 10) { $line++ } }
            $bad.Add([pscustomobject]@{ File = $rel; Count = $offsets.Count; FirstLine = $line })
        }
    }
}

Write-Host "check-source-cr: $scanned .codex scanned, $($bad.Count) carrying a stray carriage return"
foreach ($x in ($bad | Sort-Object File)) {
    Write-Host ("  {0,-52} {1} stray CR, first at line {2}" -f $x.File, $x.Count, $x.FirstLine)
}

if ($bad.Count -eq 0) { Write-Host 'check-source-cr: clean'; exit 0 }
if ($WarnOnly)        { Write-Host 'check-source-cr: WARN ONLY, not failing'; exit 0 }

Write-Host ''
Write-Host 'FAIL: a .codex source carries a carriage return that is not a line terminator.'
Write-Host '      A CR at the end of a Chapter: header puts the CR IN THE CHAPTER NAME, so'
Write-Host '      every cites of that chapter fails to match and the unit reports a missing'
Write-Host '      chapter instead. Strip the byte; the line terminator itself is not the'
Write-Host '      problem and must not be changed.'
exit 1
