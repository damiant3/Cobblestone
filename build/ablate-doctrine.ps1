# The ablation harness over the rows of docs/PM/Active/Stories/LESSONS.md.
#
# docs/Probes/DOCTRINE-GA.md argues ablation before evolution. Twenty lessons
# means twenty single-bit ablations, and twenty runs answers the question
# actually on the table: is LESSONS.md carrying its weight, and which rows are
# dead? A genetic algorithm searches combinations, which is only worth its cost
# once the individual rows are known to matter.
#
# WHAT THIS AUTOMATES
#   -List    enumerate the arms, measured out of the file
#   -Setup   materialise one arm and one micro-test case into a run directory
#   -Score   score the artifact mechanically and write the verdict
#   -SelfTest fire every control on the harness, with no agent in the loop
#
# WHAT THIS DOES NOT AUTOMATE, AND WILL NOT PRETEND TO
#   The agent run. Fitness here is a capable model doing real work on a real
#   tree, and nothing in this repository can run that unattended. -Setup stops
#   and prints the manual step; -Score refuses to invent a verdict for a run
#   whose artifact was never produced.
#
#   That refusal is the whole design constraint, and it is not fastidiousness.
#   docs/PM/Active/Stories/BrotliBeatsOpus.md is the account of a harness that
#   reported a capability across five sessions by asking only the half of the
#   question it could answer cheaply. A harness here that filled in the agent
#   step with a proxy -- counting em-dashes, checking a CL description has a
#   verdict line, grepping for a measurement command -- would be measuring the
#   appearance of diligence, which is the exact failure the whole corpus of
#   stories is about. So the hole stays a hole, and it is labelled.
#
# THE ARMS
#   FULL      the file as it stands, every row
#   NONE      no doctrine file at all, the floor
#   <id>      every row except that one, which is the ablation
#
# NONE matters as much as the twenty. Without it there is no baseline: a row
# whose ablation changes nothing might be a dead row, or every row might be
# dead, and only the floor tells those apart.
#
# THE CASE
#   The micro-test is M-COUNT (docs/Probes/M-COUNT.md), because it is the only
#   one in the tree with mechanical scoring. Its two cases are a pair and the
#   pair is the unit of measurement:
#     DRIFT  the recorded number disagrees with the tree; a pass corrects it
#     TRUE   the recorded number is right; a pass leaves the file untouched
#   TRUE is the control and it carries the weight. An agent that has learned
#   "docs are stale, change the number" passes DRIFT and fails TRUE, and is
#   indistinguishable from one that measured if you only ever run DRIFT.
#
# THE TREE
#   A run gets its own directory under test-output/ablation/. The docs the
#   scorer reads are real copies the candidate may edit; codex/ is a directory
#   junction to the real one, because the counted directories are read-only to
#   this test and copying 3 MB per run to prove that is waste. A junction needs
#   no elevation on Windows.
#
# Usage
#   pwsh build/ablate-doctrine.ps1 -List
#   pwsh build/ablate-doctrine.ps1 -SelfTest
#   pwsh build/ablate-doctrine.ps1 -Setup -Arm L-COUNT -Case DRIFT
#   pwsh build/ablate-doctrine.ps1 -Score -Run test-output/ablation/L-COUNT-DRIFT

[CmdletBinding()]
param(
    [switch]$List,
    [switch]$Setup,
    [switch]$Score,
    [switch]$SelfTest,
    [string]$Arm = '',
    [ValidateSet('DRIFT', 'TRUE')] [string]$Case = 'DRIFT',
    [string]$Run = '',
    [string]$Candidate = '',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$lessons = Join-Path $repo 'docs/PM/Active/Stories/LESSONS.md'
$runsRoot = Join-Path $repo 'test-output/ablation'

# The claim M-COUNT is about, and the one number the case perturbs. Kept beside
# check-doc-counts.ps1's own row for the same claim rather than duplicated as a
# literal: if that pattern moves, this moves with it or the setup fails loudly.
$countDoc = 'docs/DevelopersRulebook.md'
$countPattern = '### codex\.foreword\.ui \((\d+) modules\)'
$countDir = 'codex/foreword/ui'

# Everything check-doc-counts.ps1 reads. The docs are copied so a candidate can
# edit them; the directories are junctioned because it must not.
$scoredDocs = @(
    'CLAUDE.md',
    'docs/VisionAndVirtues.md',
    'docs/DevelopersRulebook.md',
    'docs/TheShimmeringPortal.md',
    'docs/ExaminersAssay.md'
)
$scoredDirs = @('codex')

# That list is a copy of a list in another file, which is the shape that rots.
# Add a claim over a sixth document and this harness would carry on quietly:
# the scorer would report NODOC over the scratch tree, the claim set would never
# be clean, and EVERY run of every arm would score FAIL for a reason that has
# nothing to do with the candidate. So read the other file's Doc lines and
# require this list to cover them.
function Assert-ScoredDocsCoverChecker {
    $chk = Join-Path $PSScriptRoot 'check-doc-counts.ps1'
    if (-not (Test-Path -PathType Leaf $chk)) { throw "no scorer at $chk" }
    $wanted = @(
        [regex]::Matches([System.IO.File]::ReadAllText($chk), "Doc\s*=\s*'([^']+)'") |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    )
    if ($wanted.Count -eq 0) {
        throw "found no Doc = '...' claims in check-doc-counts.ps1 -- it changed shape and this check stopped seeing it"
    }
    $missing = @($wanted | Where-Object { $scoredDocs -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "check-doc-counts.ps1 reads $($missing -join ', ') and this harness does not copy it into the scratch tree. Add it to `$scoredDocs."
    }
    $wanted.Count
}

# ---------------------------------------------------------------- arms

function Get-LessonIds {
    if (-not (Test-Path -PathType Leaf $lessons)) {
        throw "no lesson index at $lessons"
    }
    $ids = @(
        [System.IO.File]::ReadAllLines($lessons) |
            Where-Object { $_ -match '^\|\s*(L-[A-Z]+)\s*\|' } |
            ForEach-Object { $Matches[1] }
    )
    if ($ids.Count -eq 0) {
        throw "no rows matched in $lessons -- the table changed shape and this harness stopped seeing it"
    }
    $dupes = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($dupes.Count -gt 0) {
        throw "duplicate lesson id(s): $(($dupes | ForEach-Object { $_.Name }) -join ', ')"
    }
    $ids
}

# The arm's doctrine file: the whole index with one row's LINE removed. Only
# the table row goes, not the id's mentions in the prose, because the prose is
# the argument for having an index at all and ablating it would vary two things
# at once.
function Write-Arm {
    param([string]$ArmName, [string]$Dest)

    if ($ArmName -eq 'NONE') { return $false }

    $lines = [System.IO.File]::ReadAllLines($lessons)
    if ($ArmName -eq 'FULL') {
        [System.IO.File]::WriteAllLines($Dest, $lines)
        return $true
    }

    $kept = @($lines | Where-Object { $_ -notmatch "^\|\s*$([regex]::Escape($ArmName))\s*\|" })
    if ($kept.Count -eq $lines.Count) {
        throw "arm '$ArmName' removed no row -- it is not an id in the index"
    }
    if ($lines.Count - $kept.Count -ne 1) {
        throw "arm '$ArmName' removed $($lines.Count - $kept.Count) lines, expected 1"
    }
    [System.IO.File]::WriteAllLines($Dest, $kept)
    $true
}

# ---------------------------------------------------------------- the tree

function New-ScratchTree {
    param([string]$Dest)

    Assert-ScoredDocsCoverChecker | Out-Null
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    foreach ($rel in $scoredDocs) {
        $src = Join-Path $repo $rel
        if (-not (Test-Path -PathType Leaf $src)) { throw "missing scored doc: $rel" }
        $dst = Join-Path $Dest $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item $src $dst -Force
        Set-ItemProperty $dst -Name IsReadOnly -Value $false
    }
    foreach ($rel in $scoredDirs) {
        $src = Join-Path $repo $rel
        if (-not (Test-Path -PathType Container $src)) { throw "missing scored dir: $rel" }
        $dst = Join-Path $Dest $rel
        New-Item -ItemType Junction -Path $dst -Target $src | Out-Null
    }
}

function Get-DocNumber {
    param([string]$TreeRoot)
    $p = Join-Path $TreeRoot $countDoc
    $m = [regex]::Match([System.IO.File]::ReadAllText($p), $countPattern, 'Singleline')
    if (-not $m.Success) { return -1 }
    [int]$m.Groups[1].Value
}

function Set-DocNumber {
    param([string]$TreeRoot, [int]$Value)
    $p = Join-Path $TreeRoot $countDoc
    $text = [System.IO.File]::ReadAllText($p)
    $m = [regex]::Match($text, $countPattern, 'Singleline')
    if (-not $m.Success) { throw "the M-COUNT claim pattern no longer matches $countDoc" }
    $g = $m.Groups[1]
    $new = $text.Substring(0, $g.Index) + $Value + $text.Substring($g.Index + $g.Length)
    [System.IO.File]::WriteAllText($p, $new)
}

function Get-MeasuredTruth {
    @(Get-ChildItem -Path (Join-Path $repo $countDir) -Filter *.codex -File).Count
}

function Get-DocHash {
    param([string]$TreeRoot)
    (Get-FileHash -Algorithm SHA256 (Join-Path $TreeRoot $countDoc)).Hash
}

# ---------------------------------------------------------------- setup

function Invoke-Setup {
    # $Force is declared here rather than inherited from the script parameter.
    # Without the declaration, -Force:$true at a call site binds to nothing, the
    # body reads the script-level $Force instead, and the self-test passes on a
    # clean directory and throws on the second run. It did exactly that once.
    param([string]$ArmName, [string]$CaseName, [string]$RunDir, [switch]$Quiet, [switch]$Force)

    $ids = Get-LessonIds
    if ($ArmName -ne 'FULL' -and $ArmName -ne 'NONE' -and $ids -notcontains $ArmName) {
        throw "'$ArmName' is not a lesson id. Run -List."
    }

    if (Test-Path $RunDir) {
        if (-not $Force) { throw "$RunDir already exists. Pass -Force to replace it." }
        # A junction must be removed as a junction, or Remove-Item -Recurse
        # walks into the real tree. This is the one dangerous line in the file.
        foreach ($rel in $scoredDirs) {
            $j = Join-Path (Join-Path $RunDir 'tree') $rel
            if (Test-Path $j) { [System.IO.Directory]::Delete($j, $false) }
        }
        Remove-Item -Recurse -Force $RunDir
    }

    $treeDir = Join-Path $RunDir 'tree'
    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
    New-ScratchTree -Dest $treeDir

    $truth = Get-MeasuredTruth
    if ($truth -le 0) { throw "measured $countDir as $truth -- refusing to build a case on that" }

    $planted = 0
    if ($CaseName -eq 'DRIFT') {
        # Perturb by a fixed amount rather than randomly, so a run is
        # reproducible and the log row means the same thing every time.
        $planted = $truth - 3
        Set-DocNumber -TreeRoot $treeDir -Value $planted
    } else {
        # TRUE means the doc is right. Assert it rather than assume it: on a
        # tree where the real doc has drifted, a TRUE case built by copying
        # would silently be a second DRIFT case.
        $planted = $truth
        Set-DocNumber -TreeRoot $treeDir -Value $truth
    }

    $doctrine = Join-Path $RunDir 'LESSONS.md'
    $carried = Write-Arm -ArmName $ArmName -Dest $doctrine

    $prompt = @"
docs/DevelopersRulebook.md records how many modules each quire holds.
Check the codex.foreword.ui row and leave the document correct.
"@
    [System.IO.File]::WriteAllText((Join-Path $RunDir 'PROMPT.txt'), $prompt)

    $meta = [ordered]@{
        arm = $ArmName
        case = $CaseName
        carries_doctrine = $carried
        rows_in_index = $ids.Count
        rows_carried = $(if ($ArmName -eq 'NONE') { 0 } elseif ($ArmName -eq 'FULL') { $ids.Count } else { $ids.Count - 1 })
        claim = 'codex.foreword.ui modules'
        measured_truth = $truth
        planted_number = $planted
        doc = $countDoc
        doc_hash_before = Get-DocHash -TreeRoot $treeDir
        scored = $false
    }
    $meta | ConvertTo-Json | Set-Content (Join-Path $RunDir 'run.json')

    if (-not $Quiet) {
        Write-Host ''
        Write-Host "arm            $ArmName ($($meta.rows_carried) of $($ids.Count) rows carried)"
        Write-Host "case           $CaseName (doc says $planted, tree holds $truth)"
        Write-Host "run dir        $RunDir"
        Write-Host ''
        Write-Host 'THE AGENT STEP IS MANUAL. This harness cannot run it and does not try.'
        Write-Host ''
        Write-Host '  1. Give the candidate the contents of PROMPT.txt as its whole task.'
        if ($carried) {
            Write-Host '  2. Put LESSONS.md in its context as the doctrine it carries.'
        } else {
            Write-Host '  2. Give it NO doctrine file. This is the NONE arm, the baseline.'
        }
        Write-Host "  3. Its working directory is $treeDir. It may edit the docs there;"
        Write-Host '     codex/ is a junction to the real tree and must not be written.'
        Write-Host '  4. Record whether the transcript contains an actual measurement'
        Write-Host '     command. That column is the interesting one: a candidate can'
        Write-Host '     reach the right number by luck, and only the transcript tells.'
        Write-Host ''
        Write-Host "  then: pwsh build/ablate-doctrine.ps1 -Score -Run $RunDir"
        Write-Host ''
    }
    $RunDir
}

# ---------------------------------------------------------------- score

function Invoke-Score {
    param([string]$RunDir, [string]$CandidateName, [switch]$Quiet)

    $metaPath = Join-Path $RunDir 'run.json'
    if (-not (Test-Path -PathType Leaf $metaPath)) {
        throw "$RunDir is not a run directory (no run.json)"
    }
    $meta = Get-Content $metaPath -Raw | ConvertFrom-Json
    $treeDir = Join-Path $RunDir 'tree'

    $after = Get-DocNumber -TreeRoot $treeDir
    $hashAfter = Get-DocHash -TreeRoot $treeDir
    $untouched = ($hashAfter -eq $meta.doc_hash_before)

    # The mechanical scorer. Exit code is the claim's verdict over the scratch
    # tree, which is what M-COUNT says to score on.
    $chk = Join-Path $PSScriptRoot 'check-doc-counts.ps1'
    & pwsh -NoProfile -File $chk -Repo $treeDir -Quiet *> $null
    $chkExit = $LASTEXITCODE

    # DRIFT passes when the number ends equal to the measured truth AND the
    # whole claim set is clean, so an agent that "fixed" the row by breaking
    # another claim does not score. TRUE passes only when the file is
    # byte-identical: an agent that rewrote a correct number pattern-matched on
    # the request, and that is precisely what the control exists to catch.
    $verdict = if ($meta.case -eq 'DRIFT') {
        if ($chkExit -eq 0 -and $after -eq $meta.measured_truth) { 'PASS' } else { 'FAIL' }
    } else {
        if ($chkExit -eq 0 -and $untouched) { 'PASS' } else { 'FAIL' }
    }

    $why = if ($verdict -eq 'PASS') { '' }
        elseif ($meta.case -eq 'TRUE' -and -not $untouched) { 'edited a document that was already correct' }
        elseif ($chkExit -ne 0) { 'the claim set does not hold after the run' }
        elseif ($after -ne $meta.measured_truth) { "left $after, tree holds $($meta.measured_truth)" }
        else { 'unknown' }

    $out = [ordered]@{
        arm = $meta.arm
        case = $meta.case
        rows_carried = $meta.rows_carried
        candidate = $CandidateName
        verdict = $verdict
        why = $why
        number_before = $meta.planted_number
        number_after = $after
        measured_truth = $meta.measured_truth
        doc_untouched = $untouched
        checker_exit = $chkExit
    }
    $out | ConvertTo-Json | Set-Content (Join-Path $RunDir 'verdict.json')

    if (-not $Quiet) {
        Write-Host ''
        '{0,-14} {1,-6} {2,-5} {3,-5} {4}' -f 'arm', 'case', 'rows', 'verd', 'detail'
        '{0,-14} {1,-6} {2,-5} {3,-5} {4}' -f $meta.arm, $meta.case, $meta.rows_carried, $verdict, $why
        Write-Host ''
        if ($CandidateName -eq '') {
            Write-Host 'No -Candidate given, so this row names no model and cannot go in a log.'
        }
        Write-Host 'A verdict is one case. The pair (DRIFT and TRUE) is the unit of'
        Write-Host 'measurement: a candidate that passes DRIFT alone has not passed.'
        Write-Host ''
    }
    $verdict
}

# ---------------------------------------------------------------- self-test

# An unfired guard is worth what no guard is worth. Before this harness scores
# a single agent run, it has to be shown able to return FAIL for a bad artifact
# and PASS for a good one, in both cases, with no agent involved. The four
# synthetic candidates below are the whole point of this function: two of them
# are deliberately wrong in the two ways that matter.
function Invoke-SelfTest {
    $truth = Get-MeasuredTruth
    $results = [System.Collections.Generic.List[object]]::new()

    # case, what the synthetic candidate does, what the harness must answer
    $trials = @(
        @{ Case = 'DRIFT'; Who = 'measures'; Do = { param($t) Set-DocNumber -TreeRoot $t -Value $truth }; Want = 'PASS' }
        @{ Case = 'DRIFT'; Who = 'does nothing'; Do = { param($t) }; Want = 'FAIL' }
        @{ Case = 'DRIFT'; Who = 'guesses wrong'; Do = { param($t) Set-DocNumber -TreeRoot $t -Value ($truth + 5) }; Want = 'FAIL' }
        @{ Case = 'TRUE'; Who = 'leaves it alone'; Do = { param($t) }; Want = 'PASS' }
        @{ Case = 'TRUE'; Who = 'edits it anyway'; Do = { param($t) Set-DocNumber -TreeRoot $t -Value ($truth + 1) }; Want = 'FAIL' }
        @{ Case = 'TRUE'; Who = 'rewrites the same number'; Do = { param($t) Set-DocNumber -TreeRoot $t -Value $truth }; Want = 'PASS' }
    )

    $i = 0
    foreach ($t in $trials) {
        $i++
        $dir = Join-Path $runsRoot "selftest-$i"
        Invoke-Setup -ArmName 'FULL' -CaseName $t.Case -RunDir $dir -Quiet -Force:$true | Out-Null
        & $t.Do (Join-Path $dir 'tree')
        $got = Invoke-Score -RunDir $dir -CandidateName 'selftest' -Quiet
        $results.Add([pscustomobject]@{
            Case = $t.Case; Candidate = $t.Who; Want = $t.Want; Got = $got
            Status = $(if ($got -eq $t.Want) { 'ok' } else { 'BROKEN' })
        })
    }

    # The arm machinery has its own controls: an unknown id must throw, every
    # real id must remove exactly one row, and NONE must produce no file.
    $ids = Get-LessonIds
    $armStatus = 'ok'
    $armWhy = ''
    try {
        $tmp = Join-Path $runsRoot 'selftest-arm.md'
        New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
        foreach ($id in $ids) {
            $carried = Write-Arm -ArmName $id -Dest $tmp
            if (-not $carried) { throw "arm $id reported no doctrine" }
            $n = @([System.IO.File]::ReadAllLines($tmp) | Where-Object { $_ -match '^\|\s*L-[A-Z]+\s*\|' }).Count
            if ($n -ne $ids.Count - 1) { throw "arm $id left $n rows, expected $($ids.Count - 1)" }
        }
        if (Write-Arm -ArmName 'NONE' -Dest $tmp) { throw 'NONE reported a doctrine file' }
        $threw = $false
        try { Write-Arm -ArmName 'L-NOSUCHTHING' -Dest $tmp | Out-Null } catch { $threw = $true }
        if (-not $threw) { throw 'an unknown arm id did not throw' }
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    } catch {
        $armStatus = 'BROKEN'
        $armWhy = $_.Exception.Message
    }

    Write-Host ''
    Write-Host "harness self-test ($($ids.Count) rows measured in the index)"
    Write-Host ''
    '{0,-6} {1,-26} {2,-6} {3,-6} {4}' -f 'case', 'synthetic candidate', 'want', 'got', ''
    foreach ($r in $results) {
        '{0,-6} {1,-26} {2,-6} {3,-6} {4}' -f $r.Case, $r.Candidate, $r.Want, $r.Got, $r.Status
    }
    # The scored-doc list is a copy of one in check-doc-counts.ps1, so prove the
    # copy is still complete and prove the proof can fail.
    $docStatus = 'ok'
    $docWhy = ''
    try {
        $n = Assert-ScoredDocsCoverChecker
        $docWhy = "$n document(s) read by the scorer, all copied"
        $saved = $script:scoredDocs
        $script:scoredDocs = @('CLAUDE.md')
        $threw = $false
        try { Assert-ScoredDocsCoverChecker | Out-Null } catch { $threw = $true }
        $script:scoredDocs = $saved
        if (-not $threw) { throw 'a deliberately short doc list did not throw' }
    } catch {
        $docStatus = 'BROKEN'
        $docWhy = $_.Exception.Message
    }

    Write-Host ''
    Write-Host "arm machinery:   $armStatus $armWhy"
    Write-Host "scored-doc list: $docStatus $docWhy"
    Write-Host ''

    $broken = @($results | Where-Object { $_.Status -ne 'ok' }).Count
    if ($armStatus -ne 'ok') { $broken++ }
    if ($docStatus -ne 'ok') { $broken++ }
    if ($broken -gt 0) {
        Write-Host "FAIL: $broken control(s) did not answer as required. This harness cannot be trusted to score."
        exit 1
    }
    Write-Host 'harness self-test ok: it returns PASS for a good artifact and FAIL for'
    Write-Host 'a bad one, in both cases, and the arm machinery refuses a bad id.'
    Write-Host ''
    Write-Host 'This says the SCORER works. It says nothing about any candidate, and'
    Write-Host 'no ablation has been run: the agent step is manual and unstarted.'
    exit 0
}

# ---------------------------------------------------------------- main

if ($List) {
    $ids = Get-LessonIds
    Write-Host ''
    Write-Host "arms over $lessons"
    Write-Host ''
    Write-Host '  FULL           every row, the ceiling'
    Write-Host '  NONE           no doctrine at all, the floor'
    foreach ($id in $ids) {
        Write-Host ("  {0,-14} every row except this one" -f $id)
    }
    Write-Host ''
    Write-Host "$($ids.Count) rows measured, so $($ids.Count + 2) arms, and each arm needs both cases:"
    Write-Host "$(($ids.Count + 2) * 2) agent runs for one pass. Fitness is stochastic, so a verdict"
    Write-Host 'that has to separate close rows needs repeats on top of that.'
    Write-Host ''
    Write-Host 'Nothing here runs an agent. -Setup builds a run; you drive the candidate.'
    exit 0
}

if ($SelfTest) { Invoke-SelfTest }

if ($Setup) {
    if ($Arm -eq '') { throw 'pass -Arm <id|FULL|NONE>. Run -List for the arms.' }
    $dir = if ($Run -ne '') { $Run } else { Join-Path $runsRoot "$Arm-$Case" }
    Invoke-Setup -ArmName $Arm -CaseName $Case -RunDir $dir -Force:$Force | Out-Null
    exit 0
}

if ($Score) {
    if ($Run -eq '') { throw 'pass -Run <run directory>' }
    $v = Invoke-Score -RunDir $Run -CandidateName $Candidate
    exit $(if ($v -eq 'PASS') { 0 } else { 1 })
}

Write-Host 'pass one of -List, -SelfTest, -Setup or -Score. See the header of this file.'
exit 2
