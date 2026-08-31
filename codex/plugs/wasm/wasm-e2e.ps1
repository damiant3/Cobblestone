# Grade the wasm plug end to end: source -> IR -> plug -> WAT -> wat2wasm ->
# module -> wasmtime, against the same source compiled for x86-64.
#
# The x86-64 run is the truth. Emitting is not assembling and assembling is not
# answering, so each subject must clear all three: the WAT must name no function
# it does not define, wat2wasm must accept it, and wasmtime must print what bare
# metal printed.
[CmdletBinding()]
param([string]$Subject, [string]$Kernel)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = $PSScriptRoot
$PlugCdx = Join-Path $PlugDir 'build-output\wasm-plug.cdx'
$TestDir = Join-Path $PlugDir 'test'
$Work = Join-Path $PlugDir 'build-output\e2e'
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

# A missing toolchain must REFUSE, not skip. A skipped arm and a passing arm
# read the same in a summary line, and this harness exists to be believed.
foreach ($tool in @('wat2wasm', 'wasmtime')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH, so this harness cannot grade anything."
        exit 2
    }
}

if (-not (Test-Path -PathType Leaf $PlugCdx)) { Write-Host "REFUSE: missing $PlugCdx"; exit 2 }

# A plug binary older than its source or than the seed is a confident wrong
# answer in either direction: nothing here runs the .codex, every step runs the
# .cdx beside it.
$plugAge = (Get-Item $PlugCdx).LastWriteTime
foreach ($src in @((Join-Path $PlugDir 'WasmEmitter.codex'), (Join-Path $PlugDir 'WasmPlug.codex'), $Kernel)) {
    if ((Get-Item $src).LastWriteTime -gt $plugAge) {
        Write-Host "REFUSE: $PlugCdx is older than $src. Rebuild the plug first."
        exit 2
    }
}

$subjects = if ($Subject) { @((Resolve-Path $Subject).Path) }
            else { @((Get-ChildItem $TestDir -Filter '*.codex' | Sort-Object Name).FullName) }
if (@($subjects).Count -eq 0) { Write-Host "REFUSE: no subjects in $TestDir"; exit 2 }
Write-Host "[wasm-e2e] grading $(@($subjects).Count) subject(s) against x86-64"

New-Item -ItemType Directory -Force -Path $Work | Out-Null
$pass = 0; $fail = 0

foreach ($s in $subjects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($s)
    $cdx = Join-Path $Work "$name.cdx"
    $log = Join-Path $Work "$name.compile.log"
    $truthOut = Join-Path $Work "$name.x86.out"
    $wat = Join-Path $Work "$name.wat"
    $wasm = Join-Path $Work "$name.wasm"

    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $s -Out $cdx -Log $log -Kernel $Kernel | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL $name : did not compile for x86-64; see $log"; $fail++; continue }

    # A subject that READS needs the same bytes on both arms, or the two are
    # not the same program. `<name>.stdin` beside the subject is that sidecar:
    # codex-vm takes it with -input, wasmtime on stdin. Without one, neither
    # arm gets any input, which is the old behaviour and still the common case.
    $stdinFile = [IO.Path]::ChangeExtension($s, '.stdin')
    $hasStdin = Test-Path -PathType Leaf $stdinFile
    $vmArgs = @('-kernel', $cdx, '-headless', '-output', $truthOut)
    if ($hasStdin) { $vmArgs += @('-input', $stdinFile) }
    & (Join-Path $Repo 'tools\codex-vm.exe') @vmArgs 2>&1 | Out-Null
    if (-not (Test-Path $truthOut) -or (Get-Item $truthOut).Length -eq 0) {
        Write-Host "FAIL $name : the x86-64 run produced no output, so there is no truth to grade against."; $fail++; continue
    }
    # codex-vm's capture carries a leading CCE 0x01 the wasmtime run has no
    # equivalent of. Everything after it is the program's own bytes.
    $truthBytes = [IO.File]::ReadAllBytes($truthOut)
    if ($truthBytes.Length -gt 0 -and $truthBytes[0] -eq 1) { $truthBytes = $truthBytes[1..($truthBytes.Length - 1)] }
    $truth = [Text.Encoding]::UTF8.GetString($truthBytes)

    # Same kernel as the truth arm above. Two arms compiled by two different
    # compilers cannot settle a disagreement, because either one could own it.
    & pwsh -NoProfile -File (Join-Path $PlugDir 'run.ps1') -Src $s -Out $wat -Kernel $Kernel | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $wat)) { Write-Host "FAIL $name : the plug did not emit WAT."; $fail++; continue }

    # THE INVARIANTS THIS HARNESS CANNOT GRADE. Everything below compares what
    # the module PRINTS, and two of the emitted runtime's properties are
    # invisible to that: a module that grows memory one page at a time and one
    # that grows in 16 MB steps print the same bytes, agree with x86-64 exactly
    # as well, and differ by 205 seconds on the Codex compiler's own source.
    # They are asserted on the emitted text instead, where they are exact.
    # The runtime is emitted whole into every module, so every subject carries
    # them and the cost is a file read.
    & pwsh -NoProfile -File (Join-Path $PlugDir 'check-emitted-runtime.ps1') $wat | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL $name : the emitted runtime broke an invariant."
        & pwsh -NoProfile -File (Join-Path $PlugDir 'check-emitted-runtime.ps1') $wat |
            ForEach-Object { Write-Host "  $_" }
        $fail++; continue
    }

    # wat2wasm IS the undefined-name census, and a grep is not. A builtin the
    # plug has no arm for does NOT come out as `(call $name)`: the name is
    # treated as a value and reaches the funcref path, so it emits
    # `call_indirect (type $fnN) ... (local.get $name)` against an undeclared
    # local and an arity type nothing built. A `(call $...)` scan cannot fire
    # for that at all -- measured here on `list-insert-at` -- and reads as a
    # clean census while seeing nothing. Keep wat2wasm's own diagnostic: it
    # names the missing name and the line, which is what the next reader needs.
    $watErr = Join-Path $Work "$name.wat2wasm.err"
    # --enable-tail-call: the emitter uses return_call for saturating calls in
    # tail position; the binary form runs unflagged in wasmtime and every
    # major browser, wat2wasm merely needs permission to assemble it.
    & wat2wasm --enable-tail-call $wat -o $wasm 2>$watErr | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $wasm)) {
        Write-Host "FAIL $name : wat2wasm refused the module."
        if (Test-Path $watErr) { Get-Content $watErr | Select-Object -First 8 | ForEach-Object { Write-Host "  $_" } }
        $fail++; continue
    }

    # PowerShell has no `<`, and piping a string re-encodes it and appends a
    # newline. Start-Process takes the FILE, so the guest reads the sidecar's
    # bytes and nothing else.
    #
    # THE COPY IS NOT TIDINESS. Start-Process's stdin redirect opens the file
    # for WRITE and fails "Access to the path is denied" on a read-only one,
    # and Perforce makes every submitted file read-only. Redirecting from the
    # depot path therefore works exactly until the sidecar lands and never
    # again, which is a green that depended on the file not yet being checked
    # in. Copy to a writable temp and redirect from that.
    # x86 runs with an effectively unbounded call stack; wasmtime's default
    # (~512 KB) exhausts inside the text printer's per-def recursion at about
    # 650 KB of source, which is plugs 1.14's shape, not a codegen defect.
    # 16 MB makes the bed match the reference target's envelope (L-ARENA runs
    # the other way: a bed can also be too STINGY to express correctness).
    # A `<name>.wasmstack` sidecar overrides the stack per subject: the
    # tail-call arm pins 1 MB, a browser's real number, so a regression to
    # per-frame mutual recursion fails there while the reference target
    # stays green.
    $stackFile = [IO.Path]::ChangeExtension($s, '.wasmstack')
    $stackBytes = if (Test-Path -PathType Leaf $stackFile) { (Get-Content $stackFile -Raw).Trim() } else { '16777216' }
    $wtArgs = @('-W', "max-wasm-stack=$stackBytes", $wasm)
    if ($hasStdin) {
        $wOut = [IO.Path]::GetTempFileName()
        $wIn = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllBytes($wIn, [IO.File]::ReadAllBytes($stdinFile))
        $p = Start-Process -FilePath 'wasmtime' -ArgumentList $wtArgs -NoNewWindow -PassThru `
             -RedirectStandardInput $wIn -RedirectStandardOutput $wOut -RedirectStandardError "$wOut.err"
        $p.WaitForExit(120000) | Out-Null
        $actual = (Get-Content $wOut -Raw -ErrorAction SilentlyContinue)
        if (-not $actual) { $actual = '' }
        Remove-Item $wOut, "$wOut.err", $wIn -Force -ErrorAction SilentlyContinue
    } else {
        $actual = (& wasmtime @wtArgs 2>&1 | Out-String)
    }

    $t = $truth -replace "`r`n", "`n"
    $a = $actual -replace "`r`n", "`n"
    if ($t.TrimEnd("`n") -eq $a.TrimEnd("`n")) {
        Write-Host "ok   $name : $(@($t.TrimEnd("`n") -split "`n").Count) line(s) agree with x86-64"
        $pass++
    } else {
        Write-Host "FAIL $name : wasm and x86-64 disagree."
        # A truncated answer and a wrong answer are the same colour on a verdict
        # line until the lengths are compared (L-SHORT).
        if ($t.StartsWith($a) -and $a.Length -lt $t.Length) {
            Write-Host "  TRUNCATED: wasm produced $($a.Length) chars of the expected $($t.Length), a strict prefix."
        } elseif ($a.Length -ne $t.Length) {
            Write-Host "  LENGTHS DIFFER: wasm $($a.Length) chars, x86-64 $($t.Length)."
        }
        Write-Host "  x86-64: $($t.TrimEnd("`n"))"
        Write-Host "  wasm  : $($a.TrimEnd("`n"))"
        $fail++
    }
}

Write-Host ""
Write-Host "[wasm-e2e] $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
exit 0
