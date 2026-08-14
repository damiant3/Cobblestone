# ablate-doctrine.ps1 -- The ablation harness over the rows of docs/PM/Active/Stories/LESSONS.md.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [switch]$List,
    [switch]$Setup,
    [switch]$Score,
    [switch]$SelfTest,
    [string]$Arm = '',
    [ValidateSet('DRIFT','TRUE')]
    [string]$Case = 'DRIFT',
    [string]$Run = '',
    [string]$Candidate = '',
    [switch]$Force
)

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


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Split-Path $PSScriptRoot)
$lessons = (Join-Path $repo 'docs/PM/Active/Stories/LESSONS.md')
$runsRoot = (Join-Path $repo 'test-output/ablation')

# The claim M-COUNT is about, and the one number the case perturbs. Kept beside
# check-doc-counts.ps1's own row for the same claim rather than duplicated as a
# literal: if that pattern moves, this moves with it or the setup fails loudly.

$countDoc = 'docs/DevelopersRulebook.md'
$countPattern = '### codex\.foreword\.ui \((\d+) modules\)'
$countDir = 'codex/foreword/ui'

# Everything check-doc-counts.ps1 reads. The docs are copied so a candidate can
# edit them; the directories are junctioned because it must not.

$scoredDocs = @('CLAUDE.md', 'docs/VisionAndVirtues.md', 'docs/DevelopersRulebook.md', 'docs/TheShimmeringPortal.md', 'docs/ExaminersAssay.md')
$scoredDirs = @('codex')


# That list is a copy of a list in another file, which is the shape that rots.
# Add a claim over a sixth document and this harness would carry on quietly:
# the scorer would report NODOC over the scratch tree, the claim set would never
# be clean, and EVERY run of every arm would score FAIL for a reason that has
# nothing to do with the candidate. So read the other file's Doc lines and
# require this list to cover them.

function Assert-ScoredDocsCoverChecker() {
    $chk = (Join-Path $PSScriptRoot 'check-doc-counts.ps1')
    if ((-not (Test-Path -PathType Leaf $chk))) {
        throw ([string]'no scorer at ' + $chk)
    }
    $seen = @{}
    $wanted = @()
    foreach ($w in @((([regex]::Matches(([System.IO.File]::ReadAllText($chk)), 'Doc\s*=\s*''([^'']+)''')) | ForEach-Object { $_.Groups[1].Value } | Sort-Object))) {
        if ((-not $seen.ContainsKey($w))) {
            $seen[$w] = $true
            $wanted += $w
        }
    }

    if ((@($wanted).Count -eq 0)) {
        throw 'found no Doc = ''...'' claims in check-doc-counts.ps1 -- it changed shape and this check stopped seeing it'
    }
    $missing = @(($wanted | Where-Object { (-not ($scoredDocs -contains $_)) }))
    if ((@($missing).Count -gt 0)) {
        throw ([string]'check-doc-counts.ps1 reads ' + ([string]($missing -join ', ') + ' and this harness does not copy it into the scratch tree. Add it to $scoredDocs.'))
    }
    return @($wanted).Count
}


function Get-LessonIds() {
    if ((-not (Test-Path -PathType Leaf $lessons))) {
        throw ([string]'no lesson index at ' + $lessons)
    }
    $ids = @()
    foreach ($line in ([System.IO.File]::ReadAllLines($lessons))) {
        if (($line -match '^\|\s*(L-[A-Z]+)\s*\|')) {
            $ids += $matches[1]
        }
    }
    if ((@($ids).Count -eq 0)) {
        throw ([string]([string]'no rows matched in ' + $lessons) + ' -- the table changed shape and this harness stopped seeing it')
    }
    $count = @{}
    $dupes = @()
    foreach ($id in $ids) {
        if ($count.ContainsKey($id)) {
            $dupes += $id
        } else {
            $count[$id] = $true
        }
    }

    if ((@($dupes).Count -gt 0)) {
        throw ([string]'duplicate lesson id(s): ' + ($dupes -join ', '))
    }
    return $ids
}

# The arm's doctrine file: the whole index with one row's LINE removed. Only
# the table row goes, not the id's mentions in the prose, because the prose is
# the argument for having an index at all and ablating it would vary two things
# at once.

function Write-Arm([string]$ArmName, [string]$Dest) {
    if (($ArmName -eq 'NONE')) {
        return $false
    }
    $lines = ([System.IO.File]::ReadAllLines($lessons))
    if (($ArmName -eq 'FULL')) {
        [System.IO.File]::WriteAllLines($Dest, $lines)
        return $true
    }
    $rowPat = ([string]'^\|\s*' + ([string]([regex]::Escape($ArmName)) + '\s*\|'))
    $kept = @(($lines | Where-Object { (-not ($_ -match $rowPat)) }))
    if ((@($kept).Count -eq @($lines).Count)) {
        throw ([string]'arm ''' + ([string]$ArmName + ''' removed no row -- it is not an id in the index'))
    }
    if ((-not ((@($lines).Count - @($kept).Count) -eq 1))) {
        throw ([string]'arm ''' + ([string]$ArmName + ([string]''' removed ' + ([string](@($lines).Count - @($kept).Count) + ' lines, expected 1'))))
    }
    [System.IO.File]::WriteAllLines($Dest, $kept)
    return $true
}


function New-ScratchTree([string]$Dest) {
    [void](Assert-ScoredDocsCoverChecker)
    New-Item -ItemType Directory -Force $Dest | Out-Null
    foreach ($rel in $scoredDocs) {
        $src = (Join-Path $repo $rel)
        if ((-not (Test-Path -PathType Leaf $src))) {
            throw ([string]'missing scored doc: ' + $rel)
        }
        $dst = (Join-Path $Dest $rel)
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Copy-Item -Force $src $dst
        Set-ItemProperty $dst -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    }
    foreach ($rel in $scoredDirs) {
        $src = (Join-Path $repo $rel)
        if ((-not (Test-Path -PathType Container $src))) {
            throw ([string]'missing scored dir: ' + $rel)
        }
        $dst = (Join-Path $Dest $rel)
        [void](New-Item -ItemType 'Junction' -Path $dst -Target $src)
    }
}

function Get-DocNumber([string]$TreeRoot) {
    $p = (Join-Path $TreeRoot $countDoc)
    $m = ([regex]::Match(([System.IO.File]::ReadAllText($p)), $countPattern, 'Singleline'))
    if ((-not $m.Success)) {
        return -1
    }
    return [int]$m.Groups[1].Value
}

function Set-DocNumber([string]$TreeRoot, [int]$Value) {
    $p = (Join-Path $TreeRoot $countDoc)
    $text = ([System.IO.File]::ReadAllText($p))
    $m = ([regex]::Match($text, $countPattern, 'Singleline'))
    if ((-not $m.Success)) {
        throw ([string]'the M-COUNT claim pattern no longer matches ' + $countDoc)
    }
    $new = ([string]$text.Substring(0, $m.Groups[1].Index) + ([string]$Value + $text.Substring(($m.Groups[1].Index + $m.Groups[1].Length))))
    [System.IO.File]::WriteAllText($p, $new)
}

function Get-MeasuredTruth() {
    return @(Get-ChildItem (Join-Path $repo $countDir) -Filter '*.codex' -File).Count
}

function Get-DocHash([string]$TreeRoot) {
    $h = (Get-FileHash -Algorithm SHA256 (Join-Path $TreeRoot $countDoc)).Hash
    return $h
}


# $Force and $Quiet are [bool] here rather than [switch], and are always passed
# explicitly. A [switch] parameter on a function shadows the script parameter of
# the same name only if it is declared, and -Force:$true at a call site binds to
# nothing when it is not; the body then reads the script-level $Force instead.
# That happened once: the self-test passed on a clean directory and threw on the
# second run. A [bool] cannot bind that way.

function Invoke-Setup([string]$ArmName, [string]$CaseName, [string]$RunDir, [bool]$Quiet, [bool]$Force) {
    $ids = @((Get-LessonIds))
    if (((($ArmName -ne 'FULL') -and ($ArmName -ne 'NONE')) -and (-not ($ids -contains $ArmName)))) {
        throw ([string]'''' + ([string]$ArmName + ''' is not a lesson id. Run -List.'))
    }
    if ((Test-Path -PathType Container $RunDir)) {
        if ((-not $Force)) {
            throw ([string]$RunDir + ' already exists. Pass -Force to replace it.')
        }
        # A junction must be removed as a junction, or Remove-Item -Recurse
        # walks into the real tree. This is the one dangerous line in the file.

        foreach ($rel in $scoredDirs) {
            $j = (Join-Path (Join-Path $RunDir 'tree') $rel)
            if ((Test-Path -PathType Container $j)) {
                [System.IO.Directory]::Delete($j, $false)
            }
        }
        Remove-Item -Recurse -Force $RunDir
    }
    $treeDir = (Join-Path $RunDir 'tree')
    New-Item -ItemType Directory -Force $RunDir | Out-Null
    & 'New-ScratchTree' -Dest $treeDir
    $truth = (Get-MeasuredTruth)
    if (($truth -le 0)) {
        throw ([string]'measured ' + ([string]$countDir + ([string]' as ' + ([string]$truth + ' -- refusing to build a case on that'))))
    }
    $planted = 0
    if (($CaseName -eq 'DRIFT')) {
        # Perturb by a fixed amount rather than randomly, so a run is
        # reproducible and the log row means the same thing every time.

        $planted = ($truth - 3)
    } else {
        # TRUE means the doc is right. Assert it rather than assume it: on a
        # tree where the real doc has drifted, a TRUE case built by copying
        # would silently be a second DRIFT case.

        $planted = $truth
    }
    & 'Set-DocNumber' -TreeRoot $treeDir -Value $planted

    $doctrine = (Join-Path $RunDir 'LESSONS.md')
    $carried = (Write-Arm -ArmName $ArmName -Dest $doctrine)
    [System.IO.File]::WriteAllText((Join-Path $RunDir 'PROMPT.txt'), ([string]'docs/DevelopersRulebook.md records how many modules each quire holds.' + ([string]"`r`n" + 'Check the codex.foreword.ui row and leave the document correct.')))
    $meta = [ordered]@{ 'arm' = $ArmName; 'case' = $CaseName; 'carries_doctrine' = $carried; 'rows_in_index' = @($ids).Count; 'rows_carried' = $(if (($ArmName -eq 'NONE')) { 0 } else { $(if (($ArmName -eq 'FULL')) { @($ids).Count } else { (@($ids).Count - 1) }) }); 'claim' = 'codex.foreword.ui modules'; 'measured_truth' = $truth; 'planted_number' = $planted; 'doc' = $countDoc; 'doc_hash_before' = (Get-DocHash -TreeRoot $treeDir); 'scored' = $false }
    Set-Content -Path (Join-Path $RunDir 'run.json') -Value (ConvertTo-Json $meta) -Encoding UTF8
    if ((-not $Quiet)) {
        Write-Host ''
        Write-Host ([string]'arm            ' + ([string]$ArmName + ([string]' (' + ([string]$meta.rows_carried + ([string]' of ' + ([string]@($ids).Count + ' rows carried)'))))))
        Write-Host ([string]'case           ' + ([string]$CaseName + ([string]' (doc says ' + ([string]$planted + ([string]', tree holds ' + ([string]$truth + ')'))))))
        Write-Host ([string]'run dir        ' + $RunDir)
        Write-Host ''
        Write-Host 'THE AGENT STEP IS MANUAL. This harness cannot run it and does not try.'
        Write-Host ''
        Write-Host '  1. Give the candidate the contents of PROMPT.txt as its whole task.'
        if ($carried) {
            Write-Host '  2. Put LESSONS.md in its context as the doctrine it carries.'
        } else {
            Write-Host '  2. Give it NO doctrine file. This is the NONE arm, the baseline.'
        }

        Write-Host ([string]([string]'  3. Its working directory is ' + $treeDir) + '. It may edit the docs there;')
        Write-Host '     codex/ is a junction to the real tree and must not be written.'
        Write-Host '  4. Record whether the transcript contains an actual measurement'
        Write-Host '     command. That column is the interesting one: a candidate can'
        Write-Host '     reach the right number by luck, and only the transcript tells.'
        Write-Host ''
        Write-Host ([string]'  then: pwsh build/ablate-doctrine.ps1 -Score -Run ' + $RunDir)
        Write-Host ''
    }
    return $RunDir
}


function Invoke-Score([string]$RunDir, [string]$CandidateName, [bool]$Quiet) {
    $metaPath = (Join-Path $RunDir 'run.json')
    if ((-not (Test-Path -PathType Leaf $metaPath))) {
        throw ([string]$RunDir + ' is not a run directory (no run.json)')
    }
    $metaText = Get-Content -Path $metaPath -Raw
    $meta = (ConvertFrom-Json $metaText)
    $treeDir = (Join-Path $RunDir 'tree')

    $after = (Get-DocNumber -TreeRoot $treeDir)
    $hashAfter = (Get-DocHash -TreeRoot $treeDir)
    $untouched = ($hashAfter -eq $meta.doc_hash_before)

    # The mechanical scorer. Exit code is the claim's verdict over the scratch
    # tree, which is what M-COUNT says to score on.

    $chk = (Join-Path $PSScriptRoot 'check-doc-counts.ps1')
    [void](pwsh -NoProfile -File $chk -Repo $treeDir -Quiet)
    $chkExit = $LASTEXITCODE

    # DRIFT passes when the number ends equal to the measured truth AND the
    # whole claim set is clean, so an agent that "fixed" the row by breaking
    # another claim does not score. TRUE passes only when the file is
    # byte-identical: an agent that rewrote a correct number pattern-matched on
    # the request, and that is precisely what the control exists to catch.

    if (($meta.case -eq 'DRIFT')) {
        $verdict = $(if ((($chkExit -eq 0) -and ($after -eq $meta.measured_truth))) { 'PASS' } else { 'FAIL' })
    } else {
        $verdict = $(if ((($chkExit -eq 0) -and $untouched)) { 'PASS' } else { 'FAIL' })
    }

    $why = 'unknown'
    if (($verdict -eq 'PASS')) {
        $why = ''
    } else {
        if ((($meta.case -eq 'TRUE') -and (-not $untouched))) {
            $why = 'edited a document that was already correct'
        } else {
            if ((-not ($chkExit -eq 0))) {
                $why = 'the claim set does not hold after the run'
            } else {
                if ((-not ($after -eq $meta.measured_truth))) {
                    $why = ([string]'left ' + ([string]$after + ([string]', tree holds ' + $meta.measured_truth)))
                }
            }
        }
    }


    $out = [ordered]@{ 'arm' = $meta.arm; 'case' = $meta.case; 'rows_carried' = $meta.rows_carried; 'candidate' = $CandidateName; 'verdict' = $verdict; 'why' = $why; 'number_before' = $meta.planted_number; 'number_after' = $after; 'measured_truth' = $meta.measured_truth; 'doc_untouched' = $untouched; 'checker_exit' = $chkExit }
    Set-Content -Path (Join-Path $RunDir 'verdict.json') -Value (ConvertTo-Json $out) -Encoding UTF8
    if ((-not $Quiet)) {
        Write-Host ''
        Write-Host ('{0,-14} {1,-6} {2,-5} {3,-5} {4}' -f 'arm', 'case', 'rows', 'verd', 'detail')
        Write-Host ('{0,-14} {1,-6} {2,-5} {3,-5} {4}' -f $meta.arm, $meta.case, $meta.rows_carried, $verdict, $why)
        Write-Host ''
        if (($CandidateName -eq '')) {
            Write-Host 'No -Candidate given, so this row names no model and cannot go in a log.'
        }
        Write-Host 'A verdict is one case. The pair (DRIFT and TRUE) is the unit of'
        Write-Host 'measurement: a candidate that passes DRIFT alone has not passed.'
        Write-Host ''
    }
    return $verdict
}


# A trial names what its synthetic candidate DOES by verb rather than carrying
# a scriptblock, and an unknown verb throws. A trial that quietly did nothing
# when its name was misspelt would answer PASS on the TRUE cases and FAIL on
# the DRIFT ones, which is a plausible-looking table and a broken harness.

function Invoke-Trial([string]$Do, [string]$TreeDir, [int]$Truth) {
    if (($Do -eq 'set-truth')) {
        & 'Set-DocNumber' -TreeRoot $TreeDir -Value $Truth
    } else {
        if (($Do -eq 'set-plus-5')) {
            & 'Set-DocNumber' -TreeRoot $TreeDir -Value ($Truth + 5)
        } else {
            if (($Do -eq 'set-plus-1')) {
                & 'Set-DocNumber' -TreeRoot $TreeDir -Value ($Truth + 1)
            } else {
                if (($Do -ne 'nothing')) {
                    throw ([string]'Invoke-Trial: no such synthetic candidate ''' + ([string]$Do + ''''))
                }
            }
        }
    }
}


# An unfired guard is worth what no guard is worth. Before this harness scores
# a single agent run, it has to be shown able to return FAIL for a bad artifact
# and PASS for a good one, in both cases, with no agent involved. The six
# synthetic candidates below are the whole point of this function: three of them
# are deliberately wrong in the ways that matter.

function Invoke-SelfTest() {
    $truth = (Get-MeasuredTruth)
    $results = @()
    $trials = @(@{ Case = 'DRIFT'; Who = 'measures'; Do = 'set-truth'; Want = 'PASS' }, @{ Case = 'DRIFT'; Who = 'does nothing'; Do = 'nothing'; Want = 'FAIL' }, @{ Case = 'DRIFT'; Who = 'guesses wrong'; Do = 'set-plus-5'; Want = 'FAIL' }, @{ Case = 'TRUE'; Who = 'leaves it alone'; Do = 'nothing'; Want = 'PASS' }, @{ Case = 'TRUE'; Who = 'edits it anyway'; Do = 'set-plus-1'; Want = 'FAIL' }, @{ Case = 'TRUE'; Who = 'rewrites the same number'; Do = 'set-truth'; Want = 'PASS' })
    $i = 0
    foreach ($t in $trials) {
        $i++
        $dir = (Join-Path $runsRoot ([string]'selftest-' + $i))
        [void](Invoke-Setup -ArmName 'FULL' -CaseName $t.Case -RunDir $dir -Quiet $true -Force $true)
        & 'Invoke-Trial' -Do $t.Do -TreeDir (Join-Path $dir 'tree') -Truth $truth
        $got = (Invoke-Score -RunDir $dir -CandidateName 'selftest' -Quiet $true)
        $results += @{ Case = $t.Case; Candidate = $t.Who; Want = $t.Want; Got = $got; Status = $(if (($got -eq $t.Want)) { 'ok' } else { 'BROKEN' }) }
    }

    # The arm machinery has its own controls: an unknown id must throw, every
    # real id must remove exactly one row, and NONE must produce no file.

    $ids = @((Get-LessonIds))
    $armStatus = 'ok'
    $armWhy = ''
    try {
        $tmp = (Join-Path $runsRoot 'selftest-arm.md')
        New-Item -ItemType Directory -Force $runsRoot | Out-Null
        foreach ($id in $ids) {
            $carried = (Write-Arm -ArmName $id -Dest $tmp)
            if ((-not $carried)) {
                throw ([string]([string]'arm ' + $id) + ' reported no doctrine')
            }
            $n = @((([System.IO.File]::ReadAllLines($tmp)) | Where-Object { ($_ -match '^\|\s*L-[A-Z]+\s*\|') })).Count
            if ((-not ($n -eq (@($ids).Count - 1)))) {
                throw ([string]'arm ' + ([string]$id + ([string]' left ' + ([string]$n + ([string]' rows, expected ' + (@($ids).Count - 1))))))
            }
        }
        if ((Write-Arm -ArmName 'NONE' -Dest $tmp)) {
            throw 'NONE reported a doctrine file'
        }
        $threw = $false
        try {
            [void](Write-Arm -ArmName 'L-NOSUCHTHING' -Dest $tmp)
        } catch {
            $threw = $true
        }
        if ((-not $threw)) {
            throw 'an unknown arm id did not throw'
        }
        Remove-Item -Force -ErrorAction SilentlyContinue $tmp
    } catch {
        $armStatus = 'BROKEN'
        $armWhy = $_.Exception.Message
    }

    Write-Host ''
    Write-Host ([string]'harness self-test (' + ([string]@($ids).Count + ' rows measured in the index)'))
    Write-Host ''
    Write-Host ('{0,-6} {1,-26} {2,-6} {3,-6} {4}' -f 'case', 'synthetic candidate', 'want', 'got', '')
    foreach ($r in $results) {
        Write-Host ('{0,-6} {1,-26} {2,-6} {3,-6} {4}' -f $r.Case, $r.Candidate, $r.Want, $r.Got, $r.Status)
    }
    # The scored-doc list is a copy of one in check-doc-counts.ps1, so prove the
    # copy is still complete and prove the proof can fail.

    $docStatus = 'ok'
    $docWhy = ''
    try {
        $n = (Assert-ScoredDocsCoverChecker)
        $docWhy = ([string]$n + ' document(s) read by the scorer, all copied')
        $saved = $script:scoredDocs
        $script:scoredDocs = @('CLAUDE.md')
        $threw = $false
        try {
            [void](Assert-ScoredDocsCoverChecker)
        } catch {
            $threw = $true
        }
        $script:scoredDocs = $saved
        if ((-not $threw)) {
            throw 'a deliberately short doc list did not throw'
        }
    } catch {
        $docStatus = 'BROKEN'
        $docWhy = $_.Exception.Message
    }

    Write-Host ''
    Write-Host ([string]'arm machinery:   ' + ([string]$armStatus + ([string]' ' + $armWhy)))
    Write-Host ([string]'scored-doc list: ' + ([string]$docStatus + ([string]' ' + $docWhy)))
    Write-Host ''

    $broken = @(($results | Where-Object { ($_.Status -ne 'ok') })).Count
    if (($armStatus -ne 'ok')) {
        $broken++
    }
    if (($docStatus -ne 'ok')) {
        $broken++
    }
    if (($broken -gt 0)) {
        Write-Host ([string]'FAIL: ' + ([string]$broken + ' control(s) did not answer as required. This harness cannot be trusted to score.'))
        exit 1
    }
    Write-Host 'harness self-test ok: it returns PASS for a good artifact and FAIL for'
    Write-Host 'a bad one, in both cases, and the arm machinery refuses a bad id.'
    Write-Host ''
    Write-Host 'This says the SCORER works. It says nothing about any candidate, and'
    Write-Host 'no ablation has been run: the agent step is manual and unstarted.'
    exit 0
}


if ($List) {
    $ids = @((Get-LessonIds))
    Write-Host ''
    Write-Host ([string]'arms over ' + $lessons)
    Write-Host ''
    Write-Host '  FULL           every row, the ceiling'
    Write-Host '  NONE           no doctrine at all, the floor'
    foreach ($id in $ids) {
        Write-Host ('  {0,-14} every row except this one' -f $id)
    }
    Write-Host ''
    Write-Host ([string]@($ids).Count + ([string]' rows measured, so ' + ([string](@($ids).Count + 2) + ' arms, and each arm needs both cases:')))
    Write-Host ([string]((@($ids).Count + 2) * 2) + ' agent runs for one pass. Fitness is stochastic, so a verdict')
    Write-Host 'that has to separate close rows needs repeats on top of that.'
    Write-Host ''
    Write-Host 'Nothing here runs an agent. -Setup builds a run; you drive the candidate.'
    exit 0

}

if ($SelfTest) {
    & 'Invoke-SelfTest'
}

if ($Setup) {
    if (($Arm -eq '')) {
        throw 'pass -Arm <id|FULL|NONE>. Run -List for the arms.'
    }
    $dir = $(if (($Run -ne '')) { $Run } else { (Join-Path $runsRoot ([string]([string]$Arm + '-') + $Case)) })
    [void](Invoke-Setup -ArmName $Arm -CaseName $Case -RunDir $dir -Quiet $false -Force $Force)
    exit 0
}

if ($Score) {
    if (($Run -eq '')) {
        throw 'pass -Run <run directory>'
    }
    $v = (Invoke-Score -RunDir $Run -CandidateName $Candidate -Quiet $false)
    exit $(if (($v -eq 'PASS')) { 0 } else { 1 })
}

Write-Host 'pass one of -List, -SelfTest, -Setup or -Score. See the header of this file.'
exit 2
