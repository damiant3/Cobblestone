# check-test-compile.ps1 -- compile chapters under codex/test and fail on one
# that stops compiling.
#
# Hand-written. CurrentPlan 18222, red's ruling 2026-08-20.
#
# WHY THIS EXISTS. No gate phase compiled anything under codex/test, so a test
# chapter that stopped compiling was UNRUNNABLE and every instrument stayed
# quiet: the suite stops asking and reports exactly what a suite that asks and
# agrees reports (L-CAPABILITY-LOST). It cost three times before anything
# measured it. widget-tone was missed by a signature change TWICE, four days red
# across a release the first time and red again on main until 18248. val's 18220
# fixed six gop-composite siblings and missed the one directory over. And
# codex/test/cost/accumulator-corpus has not compiled since 2026-08-16 because
# build/cost-corpus.ps1, its only runner, is invoked by nothing.
#
# TWO MODES, and the split is red's ruling on measured cost:
#
#   (default, what build.ps1 -Internal runs) CITE-SCOPED. Compile only the test
#   chapters that cite a chapter changed in THIS workspace. Measured over the
#   whole tree: 756 chapters are cited by tests, fan-out median 2, mean 5.1,
#   max 382 (Foreword::Console). Works::GopComposite, the chapter both known
#   misses came from, has 11 citers and costs 26.3 s -- and those 11 contain
#   widget-tone AND exactly the six gop-composite chapters of 18220, so this
#   mode catches the whole known class.
#
#   -Full (what the full gate runs) EVERY chapter. 1,413 units, 1,202 s wall at
#   4 ways, 0.85 s/unit. Do not extrapolate that from a sample: the first 400 in
#   sorted order run at 0.42 s/unit and predict half the real figure, because
#   the sorted head is codex/test/apps and its chapters are small (L-DILUTE).
#
# WHAT IS EXCLUDED, and each exclusion is a promise not to cry wolf:
#   codex/test/errors/**   the gate's check-errors already compiles all 200 and
#                          requires them to be REFUSED.
#   *.failing              the chapter declares it is expected to fail.
#   build/test-compile-baseline.txt   chapters whose own runner supplies an
#                          input the compiler needs, so no bare compile can
#                          succeed. Two today, both CDX3020 quotations.
#
# The account and the numbers are docs/ExaminersAssay.md, "The discipline did
# not hold, and here is the census".
[CmdletBinding()]
param(
    [switch]$Full,
    [string]$Kernel = '',
    [int]$Ways = 4,
    [string]$Baseline = '',
    # Compile these instead of deriving them from p4 opened. The sabotage arms
    # use it, and so does anyone re-running one finding by hand.
    [string[]]$Only = @(),
    # Treat these as the changed files instead of asking Perforce. This is what
    # makes the cite-scoping testable without staging a real edit.
    [string[]]$ChangedIs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
$Kernel = (Resolve-Path $Kernel).Path
Write-Host "kernel: $Kernel [$((Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0,16))]"

if (-not $Baseline) { $Baseline = Join-Path $Repo 'build\test-compile-baseline.txt' }
$excused = @{}
if (Test-Path -PathType Leaf $Baseline) {
    foreach ($line in (Get-Content $Baseline)) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $p = ($t -split '\s{2,}')[0].Trim()
        if ($p) { $excused[($p -replace '/', '\').ToLower()] = $true }
    }
}

# The candidate set. Anything excluded here can never turn this check red, so
# every exclusion is deliberate and named in the header.
$all = @(Get-ChildItem (Join-Path $Repo 'codex\test') -Recurse -Filter *.codex -File |
         Where-Object { $_.FullName -notmatch '\\test\\errors\\' } |
         Where-Object { -not (Test-Path -PathType Leaf ([System.IO.Path]::ChangeExtension($_.FullName, '.failing'))) } |
         Sort-Object FullName)
$candidates = @($all | Where-Object {
    -not $excused.ContainsKey($_.FullName.Substring($Repo.Length + 1).ToLower())
})

function Get-ChapterName([string]$Path) {
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ($l -match '^\s*Chapter:\s*(\S+)') { return $matches[1] }
    }
    return ''
}

# --- choose the subject ---
if ($Only.Count -gt 0) {
    $subject = @($Only | ForEach-Object { (Resolve-Path $_).Path })
    Write-Host "subject: $($subject.Count) named unit(s)"
} elseif ($Full) {
    $subject = @($candidates.FullName)
    Write-Host "subject: FULL, $($subject.Count) chapters"
} else {
    $changed = @()
    if ($ChangedIs.Count -gt 0) {
        $changed = @($ChangedIs)
    } else {
        try {
            $changed = @(p4 opened 2>$null |
                ForEach-Object { (($_ -split '#')[0]) -replace '^//[^/]+/[^/]+/', '' } |
                Where-Object { $_ })
        } catch { }
    }
    $changed = @($changed | Where-Object { $_ -like '*.codex' })
    # A changed chapter is named by tests through its Chapter: name, not its
    # path, so the path has to be read rather than pattern-matched. The quire is
    # deliberately NOT matched: citing the name alone is over-inclusive, which
    # is the safe direction for a check.
    $names = @{}
    foreach ($c in $changed) {
        $fp = Join-Path $Repo ($c -replace '/', '\')
        if (Test-Path -PathType Leaf $fp) {
            $n = Get-ChapterName $fp
            if ($n) { $names[$n] = $true }
        }
    }
    $subject = @()
    # A changed file that IS a test chapter is its own subject.
    foreach ($c in $changed) {
        $fp = Join-Path $Repo ($c -replace '/', '\')
        if ((Test-Path -PathType Leaf $fp) -and ($candidates.FullName -contains $fp)) { $subject += $fp }
    }
    if ($names.Count -gt 0) {
        $pat = '^\s*cites\s+\w+\s+chapter\s+(' + (($names.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\s*$'
        foreach ($u in $candidates) {
            if ($subject -contains $u.FullName) { continue }
            if (Select-String -Path $u.FullName -Pattern $pat -Quiet) { $subject += $u.FullName }
        }
    }
    $subject = @($subject | Sort-Object -Unique)
    # "no .codex opened" and "nothing opened" are different states and the
    # message used to say the second for both, which reads as "Perforce told me
    # your workspace is clean" when it did not.
    Write-Host ("changed here: " + $(if ($changed.Count) { ($changed | Sort-Object -Unique) -join ', ' } else { 'no .codex opened' }))
    Write-Host ("chapters changed: " + $(if ($names.Count) { (($names.Keys | Sort-Object) -join ', ') } else { '(none)' }))
    Write-Host "subject: CITE-SCOPED, $($subject.Count) chapter(s) of $($candidates.Count)"
}

if ($subject.Count -eq 0) { Write-Host 'test-compile: OK (nothing implicated)'; exit 0 }

$out = Join-Path $Repo 'build-output\test-compile'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null

# test-compile-batch.ps1 keys its output directory by the filename STEM, so two
# chapters with the same basename in different directories collide and one is
# silently never compiled (engine-culling and engine-texture were in both
# codex/test and codex/test/forewords until the root pair was renamed
# 2026-08-22; none collide today, and this guard stays for the next pair
# somebody adds). Split those into separate batches so
# every chapter gets its own directory, and assert the count afterwards.
$byStem = @{}
$lists = @()
foreach ($s in $subject) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($s)
    if (-not $byStem.ContainsKey($stem)) { $byStem[$stem] = 0 }
    $slot = $byStem[$stem]
    $byStem[$stem] = $slot + 1
    while ($lists.Count -le $slot) { $lists += , @() }
    $lists[$slot] += $s
}
# Then spread the first (large) list over $Ways batches for wall clock.
$batches = @()
if ($lists.Count -gt 0 -and $lists[0].Count -gt $Ways) {
    for ($k = 0; $k -lt $Ways; $k++) {
        $part = @()
        for ($i = $k; $i -lt $lists[0].Count; $i += $Ways) { $part += $lists[0][$i] }
        if ($part.Count) { $batches += , $part }
    }
} elseif ($lists.Count -gt 0) {
    $batches += , $lists[0]
}
for ($j = 1; $j -lt $lists.Count; $j++) { $batches += , $lists[$j] }

for ($b = 0; $b -lt $batches.Count; $b++) {
    $batches[$b] | Set-Content (Join-Path $out "list$b.txt") -Encoding UTF8
}

$sw = [Diagnostics.Stopwatch]::StartNew()
0..($batches.Count - 1) | ForEach-Object -ThrottleLimit $Ways -Parallel {
    $out = $using:out; $Repo = $using:Repo
    # A SEPARATE PROCESS, not `& $script`. ForEach-Object -Parallel runs
    # runspaces inside one process and these scripts set the working directory;
    # calling them in-runspace races on it (Build.md records the measurement).
    & pwsh -NoProfile -File (Join-Path $Repo 'build\test-compile-batch.ps1') `
        -ListFile (Join-Path $out "list$_.txt") -OutRoot (Join-Path $out "o$_") *> $null
} | Out-Null
$sw.Stop()

$seen = 0
$dirty = @()
for ($b = 0; $b -lt $batches.Count; $b++) {
    foreach ($d in (Get-ChildItem (Join-Path $out "o$b") -Directory -ErrorAction SilentlyContinue)) {
        $seen++
        $c = Join-Path $d.FullName '.exitcode'
        if ((Test-Path -PathType Leaf $c) -and ((Get-Content $c -Raw).Trim() -eq '0')) { continue }
        # The exit code is the only thing that separates a chapter that FAILED
        # to compile from one that never got the chance, and it was being
        # discarded here. test-compile-batch.ps1 assigns them: 4 the VM died on
        # this chapter, 99 it died earlier in the batch and this one never ran,
        # 8 foreword resolution, 7 codegen halted.
        $code = if (Test-Path -PathType Leaf $c) { (Get-Content $c -Raw).Trim() } else { '(absent)' }
        $err = ''
        $lg = Join-Path $d.FullName 'build.log'
        if (Test-Path -PathType Leaf $lg) {
            $m = Get-Content $lg | Select-String 'error CDX' | Select-Object -First 1
            if ($m) { $err = ($m.Line -replace [regex]::Escape($Repo + '\'), '').Trim() }
            else {
                # No CDX error is itself the finding: the log's own first line
                # says what happened, and printing nothing is what made a dead
                # guest indistinguishable from a broken chapter.
                $first = Get-Content $lg | Where-Object { $_.Trim() -ne '' } | Select-Object -First 1
                if ($first) { $err = $first.Trim() }
            }
        }
        $dirty += [pscustomobject]@{ Unit = $d.Name; Code = $code; Err = $err }
    }
}

# A chapter that produced no result at all is a SILENT ABSENCE, and reading it
# as a pass is the failure this whole check exists to stop. Refuse instead.
if ($seen -ne $subject.Count) {
    Write-Host "FAIL: $($subject.Count) chapter(s) submitted, $seen result(s) came back."
    Write-Host '      A chapter that produced no result has NOT been shown to compile.'
    exit 1
}

Write-Host ("test-compile: {0} chapter(s) in {1:N1}s" -f $subject.Count, $sw.Elapsed.TotalSeconds)
if ($dirty.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($dirty.Count) test chapter(s) do not compile"
    $harness = @()
    $dirty | Sort-Object Unit | ForEach-Object {
        $why = switch ($_.Code) {
            '4'  { 'the VM died ON this chapter' }
            '99' { 'the VM died EARLIER in the batch; this chapter never compiled' }
            '8'  { 'foreword resolution failed' }
            '7'  { 'codegen halted' }
            default { '' }
        }
        if ($_.Code -eq '4' -or $_.Code -eq '99') { $harness += $_.Unit }
        Write-Host ("  {0}   [exit {1}{2}]" -f $_.Unit, $_.Code, $(if ($why) { ", $why" } else { '' }))
        if ($_.Err) { Write-Host ("      {0}" -f $_.Err) }
    }
    Write-Host ''
    if ($harness.Count -gt 0) {
        Write-Host ("  {0} of these {1} a HARNESS failure, not a source defect: the guest" -f $harness.Count, $(if ($harness.Count -eq 1) { 'is' } else { 'are' }))
        Write-Host '  died and the chapters after it never got a turn. Re-run this phase'
        Write-Host '  before touching any chapter. The box is shared, and another agent'
        Write-Host '  running a gate at the same time is the usual cause.'
        Write-Host ''
    }
    Write-Host '  A test chapter that does not compile is UNRUNNABLE, and every other'
    Write-Host '  instrument stays green over it. If its own runner supplies an input'
    Write-Host '  the compiler needs, it belongs in build/test-compile-baseline.txt with'
    Write-Host '  the runner named; otherwise it is a red and gets fixed.'
    exit 1
}
Write-Host 'test-compile: OK'
exit 0
