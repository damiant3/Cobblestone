# hosted-elf-test.ps1 -- compile Codex subjects for the HOSTED Linux target, wrap
# each in an ELF, run it, and grade the output against the SAME .expected sidecar
# the bare-metal battery grades against.
#
# The oracle is independent of this target, so a match is agreement with bare
# metal rather than agreement with itself.
#
# WSL is the verification bed here by Damian's ruling of 2026-08-28 (all options
# for the stage-5a Linux bed). Verification only: nothing on the build path.
[CmdletBinding()]
param(
    [string]$Kernel = '',
    # Exact subject names, or WILDCARD patterns matched against the eligible set:
    # -Subject 'ops/*' is the whole operator corpus, -Subject 'real-*','ops/real-*'
    # is every real subject on both levels. Selecting a slice is the normal way to
    # run this harness (Damian, 2026-09-01: focused passes, not sweeps), and it was
    # awkward enough before that callers piped -ListSubjects through a filter.
    # A pattern is expanded against the eligible set rather than the directory, so
    # it can never select a subject the exclusion rule refuses.
    [string[]]$Subject = @(),
    # 0 means the whole eligible corpus. The DEFAULT stays a cap, because a bare
    # invocation must not launch a sweep (Damian, 2026-09-01: BVT plus focused
    # passes, full batteries for releases only).
    #
    # The cap was never the lie -- the missing denominator was. "60 of 60" reads
    # as a finished corpus at any cap because both halves are the same number
    # (L-DENOM), and every arm deriving its corpus from here inherited that. The
    # repair is that the score line now always names what it was drawn FROM, so
    # a capped run reports "60 selected of 996 eligible" and cannot be misread.
    [int]$Max = 60,
    # The linux arm runs each subject through WSL. Eight instances of this
    # harness sharded by -Subject at once gave 151 linux-only reds (exit 1 and
    # -1) that all passed when rerun serially (fester, 2026-09-28): rerun a
    # sharded run's linux reds serially before believing one.
    [ValidateSet('linux','windows','both')][string]$Target = 'both',
    [string]$WorkDir = '',
    # Mangle each subject's entry so a subject that cannot fail is visible.
    [switch]$Calibrate,
    # Print the selected corpus and exit. The wasm parity census grades the SAME
    # subjects as this harness, and a second copy of the selection rule is a set
    # kept equal by hand in two places, which is silent when it drifts. This is
    # the one definition; codex/plugs/wasm/hosted-wasm-test.ps1 asks for it.
    [switch]$ListSubjects,
    # Print every subject the rule DROPS as `<reason><TAB><name>` and exit, so
    # the wasm arm can print the same census as this one.
    [switch]$ListDropped
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$TestDir = Join-Path $Repo 'codex\test'
# The comparison both hosted arms grade with lives in ONE place; that file
# explains why it mirrors build/test-run.ps1 instead of doing its own thing.
. (Join-Path $PSScriptRoot '..\common\hosted-compare-lib.ps1')
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('hosted-elf-' + [Guid]::NewGuid().ToString('n').Substring(0,8)) }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not $Kernel) { $Kernel = Join-Path $Repo 'build\output\Sut.cdx' }
if (-not $ListSubjects -and -not $ListDropped -and -not (Test-Path $Kernel)) { throw "kernel not found: $Kernel. Pass -Kernel explicitly." }

# The container and the compiler that fills it move TOGETHER (main 20822 changed
# cdx-to-pe-console.ps1, PeWriter.codex and the x86-64 emit chapters in one CL,
# with a new seed). Pairing a new container with an old kernel does not announce
# itself: measured 2026-08-31, build\output\Sut.cdx [B47056219FFEDC23] against
# the head container produced a 62,976-byte .exe that RAN, printed nothing, and
# exited 0x50000000, which reads as a codegen regression and is a version skew.
# build\output\Sut.cdx is whatever this workspace built last, so it is exactly
# the artifact most likely to be stale. Refuse rather than report a red.
# The depot seed is exempt: a sync stamps every file with the sync time, so a
# container change that landed without a seed (main 29787) makes the seed read
# as older than the container while the pair is exactly head.
$isSeed = (Resolve-Path $Kernel -ErrorAction SilentlyContinue).Path -eq (Join-Path $Repo 'seed\Codex.cdx')
if (-not $ListSubjects -and -not $ListDropped -and -not $isSeed) {
    $kernelAge = (Get-Item $Kernel).LastWriteTime
    foreach ($c in @((Join-Path $PSScriptRoot 'cdx-to-elf.ps1'),
                     (Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1'),
                     (Join-Path $Repo 'codex\plugs\pe\PeWriter.codex'))) {
        if ((Test-Path $c) -and (Get-Item $c).LastWriteTime -gt $kernelAge) {
            throw "REFUSE: $Kernel is older than $c. The container and the compiler move together; pass -Kernel seed\Codex.cdx or rebuild."
        }
    }
}

# A subject that reaches a kernel service cannot run as a user process, and that
# is a property of the subject rather than a defect in the target. Console-only
# is what hosted v1 covers, so the corpus is selected by what the source asks
# for, not by what happens when it runs.
# The five terms after `Works chapter Gop` were added 2026-09-02 (reek) because
# the rule missed a whole shape: a subject that reaches ring 0 through a CHAPTER
# rather than through an effect name. Measured, not guessed -- eight subjects
# were being selected and failing on BOTH hosted targets with matching faults
# (linux 139/SIGSEGV and 132/SIGILL against windows 0xC0000005, 0xC000001D and
# 0xC0000096 PRIVILEGED_INSTRUCTION), and six of them are un-hostable by
# construction: `Kernel chapter` (codex-boot, console-test), `Dev chapter
# CpuInspector` (cpu-inspect), `Works chapter CamCapture` (camera hardware),
# `Works chapter DevDebugger` (bp-symbolic-write). `cpu-builtins` cites NOTHING
# and so no cite-based term can reach it: it calls cpu-read-cr0/cr3 and
# cpu-cpuid-*, which are ring-0 reads, hence the two builtin-name terms.
#
# THE OTHER TWO ARE NOT EXCLUDED AND MUST NOT BE. `classic-games-oracle` and
# `classic-games-run` cite only `Games chapter *`, reach no hardware, and still
# SIGILL on both targets. Excluding them would report the same score as fixing
# them (L-CAPABILITY-LOST); they are a finding, not an ineligible subject.
$excludePattern = 'Device\.|FileSystem|Network|Identity|Audio|Gpu|Media|Concurrent|Process\.|Works chapter Gop|Kernel chapter|Dev chapter CpuInspector|Works chapter CamCapture|Works chapter DevDebugger|cpu-read-cr|cpu-read-dr|cpu-write-dr|cpu-cpuid|__heap-advance|port-in|port-out|read-line|capability|process-spawn|raw-mem|address-of|atomic-|memory-fence'

# The selection recurses. A subject's DIRECTORY is not part of the rule above --
# the rule is what the source asks for -- so a non-recursive glob was excluding
# subjects on a criterion nobody chose, and doing it silently. `codex/test/ops`
# is the proof: `real-approx-negate` is the exact fixture for negate-on-a-Real,
# it exists, x86-64 and the cross battery grade it, and this harness could not
# reach it at ANY -Max. An unreachable fixture reads identical to one nobody
# wrote (L-CONSTRUCT), and only the selection rule tells them apart.
#
# A subject is named by its path under codex\test with forward slashes, so a
# top-level subject keeps exactly the bare name it has always had and a nested
# one is `ops/real-approx-negate`. Consumers join that onto $TestDir unchanged.
# A `.skip` subject is one the bare-metal battery does not grade, with its reason
# in the sidecar, so its `.expected` is not an oracle anyone maintains
# (apps/run-process-full-test expects a dotnet banner; apps/spark-boolean-test
# pins a documented MeshBoolean defect).
#
# Two more drops, COUNTED rather than silent (plugs 2.88): a subject that
# needs a bare-metal machine is not a hosted red. A `.bare-metal` sidecar
# declares it where the need arrives through a cited chapter or a bed, which
# no source term can see without also dropping subjects that pass (a
# `UI chapter KeyInput` term would take ten passing widgets with it); the
# ring-0 builtins below are named in the subject itself. Both lists are
# printed with every run.
$hardwarePattern = 'get-ticks|pit-count|pit-input-hz|rdmsr|wrmsr|vmclear|vmptrld|vmxon|uefi-read-key'
function Get-SubjectCensus {
    $eligible = @(); $bareMetal = @(); $ring0 = @()
    foreach ($f in @(Get-ChildItem $TestDir -Filter '*.codex' -File -Recurse | Where-Object {
        (Test-Path (Join-Path $_.DirectoryName ($_.BaseName + '.expected'))) -and
        -not (Test-Path (Join-Path $_.DirectoryName ($_.BaseName + '.skip'))) -and
        -not (Select-String -Path $_.FullName -Pattern $excludePattern -Quiet)
    })) {
        $name = ($f.FullName.Substring($TestDir.Length + 1) -replace '\\', '/') -replace '\.codex$', ''
        if (Test-Path (Join-Path $f.DirectoryName ($f.BaseName + '.bare-metal'))) { $bareMetal += $name }
        elseif (Select-String -Path $f.FullName -Pattern $hardwarePattern -Quiet) { $ring0 += $name }
        else { $eligible += $name }
    }
    [pscustomobject]@{ Eligible = @($eligible | Sort-Object); BareMetal = @($bareMetal | Sort-Object); Ring0 = @($ring0 | Sort-Object) }
}
function Get-EligibleSubjects { (Get-SubjectCensus).Eligible }
function Format-Dropped($c) {
    "dropped $($c.BareMetal.Count) declared .bare-metal ($($c.BareMetal -join ', ')) and $($c.Ring0.Count) naming a ring-0 builtin ($($c.Ring0 -join ', '))"
}
if ($ListDropped) {
    $c = Get-SubjectCensus
    $c.BareMetal | ForEach-Object { "bare-metal`t$_" }
    $c.Ring0 | ForEach-Object { "ring0`t$_" }
    exit 0
}

# The cap is a STRATIFIED sample, not the first N by name. By name, 55 of the
# first 60 were `apps/*` and ops, forewords, lib and ui drew none (2026-09-25),
# so a regression in those directories reached no capped run, including the
# release gate's wasm-run phase. Each first path segment (top level is one
# stratum) takes an equal share, a share a small stratum cannot fill goes to
# the others, and a stratum's picks are its names of lowest SHA-256 rank.
# Deterministic on purpose: a rotating sample turns a run red with no change.
# Ranked rather than evenly spaced because a new subject then displaces at
# most one pick in its stratum; evenly spaced picks all moved when one file
# was added (2026-09-25, text-accum-owner pulled in scope-handler-clause).
function Get-SubjectRank([string]$Name) {
    [BitConverter]::ToUInt64([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Name)), 0)
}

function Select-StratifiedSubjects([string[]]$Names, [int]$Cap) {
    if ($Cap -le 0 -or $Names.Count -le $Cap) { return $Names }
    $strata = @($Names | Group-Object { if ($_ -match '/') { ($_ -split '/')[0] } else { '.' } } | Sort-Object Name)
    $quota = @{}
    foreach ($g in $strata) { $quota[$g.Name] = 0 }
    $left = $Cap
    while ($left -gt 0) {
        foreach ($g in $strata) {
            if ($left -gt 0 -and $quota[$g.Name] -lt $g.Count) { $quota[$g.Name]++; $left-- }
        }
    }
    @(foreach ($g in $strata) {
        $g.Group | Sort-Object { Get-SubjectRank $_ } | Select-Object -First $quota[$g.Name]
    }) | Sort-Object
}

$hasPattern = @($Subject | Where-Object { $_ -match '[*?\[]' }).Count -gt 0
if ($Subject.Count -gt 0 -and -not $hasPattern) {
    # Naming exact subjects is the focused run, so it does not pay for the scan.
    $subjects = $Subject
} elseif ($Subject.Count -gt 0) {
    $census = Get-SubjectCensus; $eligible = $census.Eligible
    $subjects = @($eligible | Where-Object { $s = $_; @($Subject | Where-Object { $s -like $_ }).Count -gt 0 })
    # A pattern that matches nothing is a typo that would otherwise report a
    # clean run over zero subjects, which is the emptiest possible green.
    if ($subjects.Count -eq 0) {
        Write-Host "REFUSE: -Subject $($Subject -join ',') matched none of the $($eligible.Count) eligible subjects."
        exit 2
    }
} else {
    $census = Get-SubjectCensus; $eligible = $census.Eligible
    $subjects = @(Select-StratifiedSubjects $eligible $Max)
}

if ($ListSubjects) { $subjects | ForEach-Object { $_ }; exit 0 }

# What the score was drawn FROM, printed beside the score itself. This is the
# whole repair for reading a cap as a corpus: "60 of 60" cannot be told from a
# complete pass, and "60 selected of 996 eligible" cannot be mistaken for one.
$drawnFrom = if ($Subject.Count -gt 0 -and -not $hasPattern) { "$($subjects.Count) named on the command line" }
             elseif ($Subject.Count -gt 0) { "$($subjects.Count) matching $($Subject -join ',') of $($eligible.Count) eligible" }
             else { "$($subjects.Count) selected of $($eligible.Count) eligible" }
if (Test-Path variable:census) { $drawnFrom += '; ' + (Format-Dropped $census) }

$compile = Join-Path $Repo 'build\compile.ps1'
$toElf   = Join-Path $PSScriptRoot 'cdx-to-elf.ps1'
$toPe    = Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1'

# A `.hosted-refusal` sidecar names what the hosted targets do not serve. It is
# read HERE and not in the selection rule, because the wasm arm takes its corpus
# from -ListSubjects and serves some of these (web-mux-heap). A line `CDXnnnn`
# requires the compile to be refused with that diagnostic; with no code line the
# subject is not built and its reason is printed.
$refused = 0; $refusedRows = @()
$pass = 0; $fail = 0; $rows = @()
$mode = if ($Calibrate) { 'CALIBRATE' } else { 'GRADE' }
$targets = if ($Target -eq 'both') { @('linux','windows') } else { @($Target) }
foreach ($tgt in $targets) {
if ($tgt -eq 'linux' -and -not (Get-Command wsl -ErrorAction SilentlyContinue)) { Write-Host 'linux arm SKIPPED (no wsl)'; continue }
foreach ($s in $subjects) {
    $src = Join-Path $TestDir "$s.codex"
    $exp = Join-Path $TestDir "$s.expected"
    # A target-specific oracle, as the cross battery takes .expected-<arch>: a subject
    # whose answer is the target itself (builtin-value-names prints hosted-kind).
    $expTgt = Join-Path $TestDir "$s.expected-$tgt"
    if (Test-Path -PathType Leaf $expTgt) { $exp = $expTgt }
    if (-not (Test-Path $src) -or -not (Test-Path $exp)) { $rows += "$s  NO SUBJECT OR ORACLE"; $fail++; continue }
    # A nested subject's name carries a separator, and the work directory is
    # flat, so artifacts are named on a flattened stem. There are no BaseName
    # collisions anywhere under codex\test (measured 2026-09-01), but the stem
    # is the full relative path rather than the leaf so that a collision added
    # later cannot make two subjects share an artifact and grade each other's.
    $a = $s -replace '/', '_'
    $cdx = Join-Path $WorkDir "$a.$tgt.cdx"
    $exe = Join-Path $WorkDir "$a.$tgt.exe"
    $elf = Join-Path $WorkDir "$a.$tgt.elf"
    # A stale artifact from a previous run reads as a pass after a failed compile.
    Remove-Item $cdx -ErrorAction SilentlyContinue
    Remove-Item $elf -ErrorAction SilentlyContinue
    Remove-Item $exe -ErrorAction SilentlyContinue

    $refFile = [IO.Path]::ChangeExtension($src, '.hosted-refusal')
    if (-not $Calibrate -and (Test-Path -PathType Leaf $refFile)) {
        $refLines = @(Get-Content $refFile | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })
        $codes = @($refLines | Where-Object { $_ -match '^CDX\d+' } | ForEach-Object { ($_ -split '\s+')[0] })
        if ($codes.Count -eq 0) { $refused++; $refusedRows += "$tgt $s  $($refLines -join '; ')"; continue }
        $a0 = $s -replace '/', '_'
        $rlog = Join-Path $WorkDir "$a0.$tgt.log"
        $rcdx = Join-Path $WorkDir "$a0.$tgt.cdx"
        Remove-Item $rcdx -ErrorAction SilentlyContinue
        $rflag = if ($tgt -eq 'windows') { 'hosted-windows' } else { 'hosted' }
        & $compile -Src $src -Out $rcdx -Log $rlog -Kernel $Kernel -RawFlags $rflag *> $null
        $rtext = if (Test-Path $rlog) { [IO.File]::ReadAllText($rlog) } else { '' }
        $missing = @($codes | Where-Object { -not $rtext.Contains($_) })
        if (Test-Path $rcdx) { $rows += "$tgt $s  COMPILED; expected refusal $($codes -join ',')"; $fail++ }
        elseif ($missing.Count -gt 0) { $rows += "$tgt $s  REFUSED WITHOUT $($missing -join ',')"; $fail++ }
        else { $refused++; $refusedRows += "$tgt $s  refused by design: $($codes -join ',')" }
        continue
    }
    $useSrc = $src
    if ($Calibrate) {
        $useSrc = Join-Path $WorkDir "$a.calib.codex"
        $text = [System.IO.File]::ReadAllText($src)
        # Break the entry point's name so the subject cannot produce its oracle.
        [System.IO.File]::WriteAllText($useSrc, ($text -replace '(?m)^(\s*)opening\b', '$1opening-calibrated'))
    }

    $flag = if ($tgt -eq 'windows') { 'hosted-windows' } else { 'hosted' }
    & $compile -Src $useSrc -Out $cdx -Log (Join-Path $WorkDir "$a.$tgt.log") -Kernel $Kernel -RawFlags $flag *> $null
    if (-not (Test-Path $cdx)) { $rows += "$tgt $s  COMPILE-REFUSED"; if ($Calibrate) { $pass++ } else { $fail++ }; continue }
    $outFile = Join-Path $WorkDir "$a.$tgt.out"
    # A `.stdin` sidecar is the subject's input and the bare-metal battery feeds
    # it (build/test.ps1, -StdinFile / -input). Without it a subject that reads
    # input prints its banner and stops, which grades as a wrong ANSWER rather
    # than as an unfed bed: apps/diagnostic-boot answered 67 chars of 426 on both
    # targets here and on the wasm arm, and the shared cause was the harnesses.
    # Redirected from the depot path directly, Start-Process opens the file for
    # write and a Perforce-managed file is read-only, so it is copied first.
    $stdinSrc = [IO.Path]::ChangeExtension($src, '.stdin')
    $hasStdin = Test-Path -PathType Leaf $stdinSrc
    if ($hasStdin) {
        $stdinFile = Join-Path $WorkDir "$a.stdin"
        Copy-Item $stdinSrc $stdinFile -Force
        Set-ItemProperty $stdinFile -Name IsReadOnly -Value $false
    }
    if ($tgt -eq 'windows') {
        try { & $toPe -CdxInput $cdx -Out $exe *> $null } catch { $rows += "$tgt $s  WRAP-FAILED: $_"; $fail++; continue }
        $spArgs = @{
            FilePath = $exe
            NoNewWindow = $true
            Wait = $true
            PassThru = $true
            RedirectStandardOutput = $outFile
            RedirectStandardError = (Join-Path $WorkDir "$a.$tgt.err")
        }
        if ($hasStdin) { $spArgs.RedirectStandardInput = $stdinFile }
        $proc = Start-Process @spArgs
        $code = $proc.ExitCode
    } else {
        try { & $toElf -CdxInput $cdx -Out $elf *> $null } catch { $rows += "$tgt $s  WRAP-FAILED: $_"; $fail++; continue }
        $lp = wsl -e wslpath -a $elf
        if ($hasStdin) { Get-Content $stdinFile -Raw | wsl -e $lp > $outFile 2>$null }
        else { wsl -e $lp > $outFile 2>$null }
        $code = $LASTEXITCODE
    }
    $got = (Get-Content $outFile -Raw -ErrorAction SilentlyContinue)
    if ($null -eq $got) { $got = '' }
    $got = Get-HarnessActual $got
    $want = Get-HarnessExpected ([System.IO.File]::ReadAllText($exp))

    if ($Calibrate) {
        if (-not (Test-HarnessMatch $got $want)) { $pass++ } else { $rows += "$tgt $s  CALIBRATION FAILED: mangled subject still produced its oracle"; $fail++ }
    } elseif (Test-HarnessMatch $got $want) {
        $pass++
    } else {
        # A truncated capture and a wrong answer are different claims (L-SHORT).
        $shape = if ($code -ne 0) { "exit $code" }
                 elseif ($want.StartsWith($got) -and $got.Length -lt $want.Length) { "TRUNCATED $($got.Length) of $($want.Length)" }
                 else { "LENGTHS DIFFER got $($got.Length) want $($want.Length)" }
        $rows += "$tgt $s  $shape"
        $fail++
    }
}

}
foreach ($r in $refusedRows) { Write-Host "  hosted-refusal: $r" }
Write-Host "hosted-test [$mode]: $pass pass, $fail fail, $refused refused by design, over $($targets -join '+') ($drawnFrom, each target)"
foreach ($r in $rows) { Write-Host "  $r" }
if ($fail -gt 0) { exit 1 }
exit 0
