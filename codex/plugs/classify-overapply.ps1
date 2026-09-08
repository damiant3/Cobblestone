# Classify each plug's emission for test-input/overapply.codex.
#
# The verdict vocabulary is REFUSE or EMITS, and EMITS is never a pass:
# `ada` emits 7,166 bytes of confident nonsense for this subject and scores
# PASS under test-plugs.ps1, which grades exit code and non-empty output
# (plugs-backlog 1.59).
#
# The subject's third line is `choose 0 2 3`, where `choose` takes ONE
# parameter and RETURNS a two-parameter function. DevelopersRulebook.md:260:
# application arrives curried on the wire, so a plug must emit the extra
# arguments applied ONE AT A TIME unless it knows the callee's arity. A
# single flat `choose(0, 2, 3)` calls a one-parameter function with three
# arguments, and what that does depends entirely on the target: a compile
# error in Kotlin, an ArityException in Clojure, extra arguments silently
# discarded in Lua and Perl.
#
# So the classifier reads WHICH SHAPE was emitted and never whether the
# harness passed it.
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'test-output'),
    [string]$Subject = 'overapply'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The shape rules live in one file, shared with test-plugs.ps1, which GRADES
# on them. A second copy of these patterns here would drift from the grade.
. (Join-Path $PSScriptRoot 'overapply-shape.ps1')

$notSubject = $script:OverapplyNotSubject

function Get-EmissionText {
    param([string]$Path)
    # wpf emits a PROJECT, not a file, and test-plugs.ps1 folds a directory
    # into its concatenated contents. Do the same or the one plug that emits
    # a project scores as a refusal.
    if ((Get-Item $Path).PSIsContainer) {
        return ((Get-ChildItem -File -Recurse $Path) | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
    }
    return [System.IO.File]::ReadAllText($Path)
}

# A plug that exits non-zero REFUSED, whatever it left on disk: babbage exits
# 6 and still leaves 1,673 bytes of partial output, so reading the file alone
# scores an honest refusal as an emission.
#
# `_results.txt` HOLDS ONLY THE LAST RUN'S SCOPE. test-plugs.ps1 rewrites it
# whole, so after `-Plug kotlin` it names kotlin and nothing else, and every
# other plug here silently loses its exit status and is judged on its residue
# file alone. Run the full sweep before reading this table as a whole-corpus
# measurement, or babbage reads as an emission rather than the refusal it is.
$exit = @{}
$resultsFile = Join-Path $OutputDir '_results.txt'
if (Test-Path $resultsFile) {
    foreach ($line in (Get-Content $resultsFile)) {
        $f = $line -split "`t"
        # Only an EXIT-code failure is a refusal. The harness also fails this
        # subject on the emitted SHAPE, and a shape failure is an emission,
        # not a refusal: reading every FAIL as REFUSE hid thirty plugs behind
        # the wrong word.
        if ($f.Count -ge 3 -and $f[0] -match "^(.+)/$Subject$" -and $f[1] -eq 'FAIL' -and $f[2] -like 'exit *') {
            $exit[$Matches[1]] = 'FAIL'
        }
    }
}

$rows = @()
foreach ($dir in (Get-ChildItem $OutputDir -Directory | Sort-Object Name)) {
    $plug = $dir.Name
    $file = Join-Path $dir.FullName "$Subject.out"
    $isSubject = if ($plug -in $notSubject) { 'no' } else { 'yes' }

    if ($exit.ContainsKey($plug) -and $exit[$plug] -eq 'FAIL') {
        $rows += [pscustomobject]@{ plug = $plug; subject = $isSubject; verdict = 'REFUSE'; shape = 'non-zero exit' }
        continue
    }

    if (-not (Test-Path $file)) {
        $rows += [pscustomobject]@{ plug = $plug; subject = $isSubject; verdict = 'REFUSE'; shape = 'no output' }
        continue
    }
    $text = Get-EmissionText $file
    if ($text.Trim().Length -eq 0) {
        $rows += [pscustomobject]@{ plug = $plug; subject = $isSubject; verdict = 'REFUSE'; shape = 'empty output' }
        continue
    }

    $shape = Get-OverapplyShape -Text $text -Plug $plug
    $rows += [pscustomobject]@{
        plug    = $plug
        subject = $isSubject
        verdict = 'EMITS'
        shape   = (Get-OverapplyShapeNote $shape)
    }
}

$rows | Format-Table -AutoSize
$sub = @($rows | Where-Object { $_.subject -eq 'yes' })
Write-Host ''
Write-Host ("subject plugs: {0}" -f $sub.Count)
Write-Host ("  REFUSE                    {0}" -f @($sub | Where-Object { $_.verdict -eq 'REFUSE' }).Count)
Write-Host ("  EMITS, one at a time      {0}" -f @($sub | Where-Object { $_.shape -match 'one argument|native' }).Count)
Write-Host ("  EMITS, FLAT               {0}" -f @($sub | Where-Object { $_.shape -match 'FLAT' }).Count)
Write-Host ("  EMITS, ARGUMENTS DROPPED  {0}" -f @($sub | Where-Object { $_.shape -match 'DROPPED' }).Count)
Write-Host ''
Write-Host 'EMITS is not a pass. FLAT and DROPPED cannot produce 6.'
