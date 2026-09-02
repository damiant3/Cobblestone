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
    [ValidateSet('linux','windows','both')][string]$Target = 'both',
    [string]$WorkDir = '',
    # Mangle each subject's entry so a subject that cannot fail is visible.
    [switch]$Calibrate,
    # Print the selected corpus and exit. The wasm parity census grades the SAME
    # subjects as this harness, and a second copy of the selection rule is a set
    # kept equal by hand in two places, which is silent when it drifts. This is
    # the one definition; codex/plugs/wasm/hosted-wasm-test.ps1 asks for it.
    [switch]$ListSubjects
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$TestDir = Join-Path $Repo 'codex\test'
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('hosted-elf-' + [Guid]::NewGuid().ToString('n').Substring(0,8)) }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
if (-not $Kernel) { $Kernel = Join-Path $Repo 'build\output\Sut.cdx' }
if (-not $ListSubjects -and -not (Test-Path $Kernel)) { throw "kernel not found: $Kernel. Pass -Kernel explicitly." }

# The container and the compiler that fills it move TOGETHER (main 20822 changed
# cdx-to-pe-console.ps1, PeWriter.codex and the x86-64 emit chapters in one CL,
# with a new seed). Pairing a new container with an old kernel does not announce
# itself: measured 2026-08-31, build\output\Sut.cdx [B47056219FFEDC23] against
# the head container produced a 62,976-byte .exe that RAN, printed nothing, and
# exited 0x50000000, which reads as a codegen regression and is a version skew.
# build\output\Sut.cdx is whatever this workspace built last, so it is exactly
# the artifact most likely to be stale. Refuse rather than report a red.
if (-not $ListSubjects) {
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
$excludePattern = 'Device\.|FileSystem|Network|Identity|Audio|Gpu|Media|Concurrent|Process\.|Works chapter Gop|__heap-advance|port-in|port-out|read-line|capability|process-spawn|raw-mem|address-of|atomic-|memory-fence'

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
function Get-EligibleSubjects {
    @(Get-ChildItem $TestDir -Filter '*.codex' -File -Recurse | Where-Object {
        (Test-Path (Join-Path $_.DirectoryName ($_.BaseName + '.expected'))) -and
        -not (Select-String -Path $_.FullName -Pattern $excludePattern -Quiet)
    } | ForEach-Object {
        ($_.FullName.Substring($TestDir.Length + 1) -replace '\\', '/') -replace '\.codex$', ''
    } | Sort-Object)
}

$hasPattern = @($Subject | Where-Object { $_ -match '[*?\[]' }).Count -gt 0
if ($Subject.Count -gt 0 -and -not $hasPattern) {
    # Naming exact subjects is the focused run, so it does not pay for the scan.
    $subjects = $Subject
} elseif ($Subject.Count -gt 0) {
    $eligible = Get-EligibleSubjects
    $subjects = @($eligible | Where-Object { $s = $_; @($Subject | Where-Object { $s -like $_ }).Count -gt 0 })
    # A pattern that matches nothing is a typo that would otherwise report a
    # clean run over zero subjects, which is the emptiest possible green.
    if ($subjects.Count -eq 0) {
        Write-Host "REFUSE: -Subject $($Subject -join ',') matched none of the $($eligible.Count) eligible subjects."
        exit 2
    }
} else {
    $eligible = Get-EligibleSubjects
    $subjects = if ($Max -gt 0) { @($eligible | Select-Object -First $Max) } else { $eligible }
}

if ($ListSubjects) { $subjects | ForEach-Object { $_ }; exit 0 }

# What the score was drawn FROM, printed beside the score itself. This is the
# whole repair for reading a cap as a corpus: "60 of 60" cannot be told from a
# complete pass, and "60 selected of 996 eligible" cannot be mistaken for one.
$drawnFrom = if ($Subject.Count -gt 0 -and -not $hasPattern) { "$($subjects.Count) named on the command line" }
             elseif ($Subject.Count -gt 0) { "$($subjects.Count) matching $($Subject -join ',') of $($eligible.Count) eligible" }
             else { "$($subjects.Count) selected of $($eligible.Count) eligible" }

$compile = Join-Path $Repo 'build\compile.ps1'
$toElf   = Join-Path $PSScriptRoot 'cdx-to-elf.ps1'
$toPe    = Join-Path $Repo 'codex\plugs\pe\cdx-to-pe-console.ps1'

$pass = 0; $fail = 0; $rows = @()
$mode = if ($Calibrate) { 'CALIBRATE' } else { 'GRADE' }
$targets = if ($Target -eq 'both') { @('linux','windows') } else { @($Target) }
foreach ($tgt in $targets) {
if ($tgt -eq 'linux' -and -not (Get-Command wsl -ErrorAction SilentlyContinue)) { Write-Host 'linux arm SKIPPED (no wsl)'; continue }
foreach ($s in $subjects) {
    $src = Join-Path $TestDir "$s.codex"
    $exp = Join-Path $TestDir "$s.expected"
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
    $got = $got -replace "`r`n", "`n"
    $want = ([System.IO.File]::ReadAllText($exp)) -replace "`r`n", "`n"

    if ($Calibrate) {
        if ($got -ne $want) { $pass++ } else { $rows += "$tgt $s  CALIBRATION FAILED: mangled subject still produced its oracle"; $fail++ }
    } elseif ($got -eq $want) {
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
Write-Host "hosted-test [$mode]: $pass pass, $fail fail, over $($targets -join '+') ($drawnFrom, each target)"
foreach ($r in $rows) { Write-Host "  $r" }
if ($fail -gt 0) { exit 1 }
exit 0
