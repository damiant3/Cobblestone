# hosted-wasm-test.ps1 -- run the wasm plug over the SAME corpus the hosted
# x86-64 lift is graded on, and grade it against the SAME .expected sidecar the
# bare-metal battery grades against.
#
# The oracle is independent of this target, so a match is agreement with bare
# metal rather than agreement with itself. Every red is a parity gap between the
# wasm plug and the hosted x86-64 arm, and the list of them IS the campaign
# ("the wasm plug at parity with the hosted x86-64 lift", CurrentPlan).
#
# The corpus is NOT restated here. codex/plugs/elf/hosted-elf-test.ps1 owns the
# selection rule and answers -ListSubjects; a second copy is a set kept equal by
# hand in two places, and the two scores would stop being comparable the moment
# one drifted.
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [string[]]$Subject = @(),
    # 0 means the whole eligible corpus; the default stays a cap so a bare
    # invocation is not a sweep. See hosted-elf-test.ps1, which owns the rule:
    # this arm published the same "60 of 60" and inherited the same L-DENOM
    # defect, and the repair in both is the denominator, not the cap.
    [int]$Max = 60,
    [string]$WorkDir = '',
    [int]$Jobs = 4,
    # Mangle each subject's entry so a subject that cannot fail is visible.
    [switch]$Calibrate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$TestDir = Join-Path $Repo 'codex\test'
$PlugCdx = Join-Path $PSScriptRoot 'build-output\wasm-plug.cdx'
$RunPs1  = Join-Path $PSScriptRoot 'run.ps1'
$ElfTest = Join-Path $Repo 'codex\plugs\elf\hosted-elf-test.ps1'
# The plug is built against the seed (codex/plugs/common/plug-build-lib.ps1), so
# that is the kernel whose compiler produced the emitter under test.
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('hosted-wasm-' + [Guid]::NewGuid().ToString('n').Substring(0,8)) }

# A missing toolchain must REFUSE, not skip. A skipped arm and a passing arm read
# the same in a summary line, and this harness exists to be believed.
foreach ($tool in @('wat2wasm', 'wasmtime')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH, so this harness cannot grade anything."
        exit 2
    }
}
if (-not (Test-Path -PathType Leaf $PlugCdx)) { Write-Host "REFUSE: missing $PlugCdx"; exit 2 }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: kernel not found: $Kernel"; exit 2 }

# A plug binary older than its source or than the kernel is a confident wrong
# answer in either direction: nothing here runs the .codex, every step runs the
# .cdx beside it (L-SAMEVER).
$plugAge = (Get-Item $PlugCdx).LastWriteTime
foreach ($src in @((Join-Path $PSScriptRoot 'WasmEmitter.codex'), (Join-Path $PSScriptRoot 'WasmPlug.codex'), $Kernel)) {
    if ((Get-Item $src).LastWriteTime -gt $plugAge) {
        Write-Host "REFUSE: $PlugCdx is older than $src. Run codex/plugs/wasm/build.ps1 first."
        exit 2
    }
}

$hasPattern = @($Subject | Where-Object { $_ -match '[*?\[]' }).Count -gt 0
if ($Subject.Count -gt 0 -and -not $hasPattern) {
    $subjects = $Subject
    $drawnFrom = "$($subjects.Count) named on the command line"
} elseif ($Subject.Count -gt 0) {
    # Let the owner of the rule expand the pattern, so a slice here and a slice
    # in the x86-64 arm are the same subjects and the two scores stay comparable.
    $eligible = @(& pwsh -NoProfile -File $ElfTest -ListSubjects -Max 0)
    if ($LASTEXITCODE -ne 0 -or $eligible.Count -eq 0) {
        Write-Host "REFUSE: $ElfTest -ListSubjects returned no corpus."
        exit 2
    }
    $subjects = @($eligible | Where-Object { $s = $_; @($Subject | Where-Object { $s -like $_ }).Count -gt 0 })
    if ($subjects.Count -eq 0) {
        Write-Host "REFUSE: -Subject $($Subject -join ',') matched none of the $($eligible.Count) eligible subjects."
        exit 2
    }
    $drawnFrom = "$($subjects.Count) matching $($Subject -join ',') of $($eligible.Count) eligible"
} else {
    # Ask for the WHOLE eligible set and cap it here, so this arm knows the
    # population it drew from and can say so. Asking the owner for a capped
    # list hands back a number that cannot be told from a corpus (L-DENOM).
    # -Max 0: ask for the WHOLE eligible set and cap it here. Asking the owner
    # for a capped list hands back a number whose two halves are the same cap,
    # which is the L-DENOM defect this harness exists downstream of.
    $eligible = @(& pwsh -NoProfile -File $ElfTest -ListSubjects -Max 0)
    if ($LASTEXITCODE -ne 0 -or $eligible.Count -eq 0) {
        Write-Host "REFUSE: $ElfTest -ListSubjects returned no corpus."
        exit 2
    }
    $subjects = if ($Max -gt 0) { @($eligible | Select-Object -First $Max) } else { $eligible }
    $drawnFrom = "$($subjects.Count) selected of $($eligible.Count) eligible"
}

New-Item -ItemType Directory -Force $WorkDir | Out-Null
$mode = if ($Calibrate) { 'CALIBRATE' } else { 'GRADE' }
Write-Host "[hosted-wasm] $mode over $drawnFrom, kernel $(Split-Path -Leaf $Kernel), -Jobs $Jobs"

$results = $subjects | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $s = $_
    $TestDir = $using:TestDir
    $WorkDir = $using:WorkDir
    $RunPs1 = $using:RunPs1
    $Kernel = $using:Kernel
    $Calibrate = $using:Calibrate

    $src = Join-Path $TestDir "$s.codex"
    $exp = Join-Path $TestDir "$s.expected"
    if (-not (Test-Path $src) -or -not (Test-Path $exp)) {
        return [pscustomobject]@{ Name = $s; Ok = $false; Note = 'NO SUBJECT OR ORACLE' }
    }

    # The work directory is flat and a nested subject's name carries a
    # separator, so artifacts take a flattened stem (hosted-elf-test.ps1).
    $a = $s -replace '/', '_'
    $wat  = Join-Path $WorkDir "$a.wat"
    $wasm = Join-Path $WorkDir "$a.wasm"
    # A stale artifact from a previous run reads as a pass after a failed step.
    Remove-Item $wat, $wasm -Force -ErrorAction SilentlyContinue

    $useSrc = $src
    if ($Calibrate) {
        $useSrc = Join-Path $WorkDir "$a.calib.codex"
        $text = [System.IO.File]::ReadAllText($src)
        # Break the entry point's name so the subject cannot produce its oracle.
        [System.IO.File]::WriteAllText($useSrc, ($text -replace '(?m)^(\s*)opening\b', '$1opening-calibrated'))
    }

    & pwsh -NoProfile -File $RunPs1 -Src $useSrc -Out $wat -Kernel $Kernel *> (Join-Path $WorkDir "$a.plug.log")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $wat)) {
        return [pscustomobject]@{ Name = $s; Ok = [bool]$Calibrate; Note = 'PLUG-REFUSED' }
    }

    # wat2wasm IS the undefined-name census, and a grep is not: a builtin the plug
    # has no arm for reaches the funcref path and emits call_indirect against an
    # undeclared local, which no `(call $...)` scan can fire on.
    $watErr = Join-Path $WorkDir "$a.wat2wasm.err"
    & wat2wasm --enable-tail-call $wat -o $wasm 2>$watErr | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $wasm)) {
        $why = if (Test-Path $watErr) { (Get-Content $watErr | Select-Object -First 1) } else { '' }
        return [pscustomobject]@{ Name = $s; Ok = [bool]$Calibrate; Note = "WAT2WASM-REFUSED $why" }
    }

    # x86 runs with an effectively unbounded call stack; wasmtime's default
    # (~512 KB) exhausts inside the text printer's per-def recursion, which is a
    # bed being too STINGY to express correctness rather than a codegen defect.
    $stackFile = [IO.Path]::ChangeExtension($src, '.wasmstack')
    $stackBytes = if (Test-Path -PathType Leaf $stackFile) { (Get-Content $stackFile -Raw).Trim() } else { '16777216' }
    $outFile = Join-Path $WorkDir "$a.out"
    $errFile = Join-Path $WorkDir "$a.err"
    # A `.stdin` sidecar is the subject's input and the bare-metal battery feeds
    # it (build/test.ps1, -StdinFile / -input). Without it a subject that reads
    # input prints its banner and stops, which grades as a wrong ANSWER rather
    # than as an unfed bed: apps/diagnostic-boot answered 67 chars of 426 on this
    # arm and on both hosted x86-64 targets, and the shared cause was here.
    $stdinFile = [IO.Path]::ChangeExtension($src, '.stdin')
    $spArgs = @{
        FilePath = 'wasmtime'
        ArgumentList = @('-W', "max-wasm-stack=$stackBytes", $wasm)
        NoNewWindow = $true
        PassThru = $true
        RedirectStandardOutput = $outFile
        RedirectStandardError = $errFile
    }
    # Redirected from the depot path directly, Start-Process opens the file for
    # write and a Perforce-managed file is read-only, so it is copied first.
    if (Test-Path -PathType Leaf $stdinFile) {
        $stdinCopy = Join-Path $WorkDir "$a.stdin"
        Copy-Item $stdinFile $stdinCopy -Force
        Set-ItemProperty $stdinCopy -Name IsReadOnly -Value $false
        $spArgs.RedirectStandardInput = $stdinCopy
    }
    $p = Start-Process @spArgs
    if (-not $p.WaitForExit(120000)) {
        try { $p.Kill() } catch { }
        return [pscustomobject]@{ Name = $s; Ok = [bool]$Calibrate; Note = 'WASMTIME-TIMEOUT 120s' }
    }
    $code = $p.ExitCode

    $got = (Get-Content $outFile -Raw -ErrorAction SilentlyContinue)
    if ($null -eq $got) { $got = '' }
    $got = $got -replace "`r`n", "`n"
    $want = ([System.IO.File]::ReadAllText($exp)) -replace "`r`n", "`n"

    if ($Calibrate) {
        if ($got -ne $want) { return [pscustomobject]@{ Name = $s; Ok = $true; Note = '' } }
        return [pscustomobject]@{ Name = $s; Ok = $false; Note = 'CALIBRATION FAILED: mangled subject still produced its oracle' }
    }
    if ($got -eq $want) { return [pscustomobject]@{ Name = $s; Ok = $true; Note = '' } }

    # A truncated capture and a wrong answer are different claims (L-SHORT).
    $shape = if ($code -ne 0) {
                 $first = if (Test-Path $errFile) { (Get-Content $errFile | Where-Object { $_.Trim() } | Select-Object -First 1) } else { '' }
                 "exit $code  $first"
             } elseif ($want.StartsWith($got) -and $got.Length -lt $want.Length) {
                 "TRUNCATED $($got.Length) of $($want.Length)"
             } elseif ($got.Length -ne $want.Length) {
                 "LENGTHS DIFFER got $($got.Length) want $($want.Length)"
             } else {
                 # Same length, different bytes: an ordinary wrong answer. Saying
                 # LENGTHS DIFFER here is false on its face and trains the reader
                 # to ignore the word (L-SHORT).
                 "DIFFERS at $($want.Length) chars"
             }
    return [pscustomobject]@{ Name = $s; Ok = $false; Note = $shape }
}

$pass = @($results | Where-Object { $_.Ok }).Count
$fail = @($results | Where-Object { -not $_.Ok }).Count
Write-Host ""
Write-Host "hosted-wasm [$mode]: $pass pass, $fail fail, $drawnFrom"
foreach ($r in ($results | Where-Object { -not $_.Ok } | Sort-Object Name)) {
    Write-Host "  $($r.Name)  $($r.Note)"
}
Write-Host "workdir: $WorkDir"
if ($fail -gt 0) { exit 1 }
exit 0
