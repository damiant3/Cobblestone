# Cross-architecture test battery with parallel Renode execution.
# Supports ARM64 and RISC-V via -Arch parameter.
[CmdletBinding()]
param(
    [ValidateSet('arm64','riscv64')]
    [string]$Arch = 'arm64',
    # Eight, not four. Four was chosen because eight Renode slots flaked -- a
    # passing test would come back FAIL_RUNTIME with no uart after ~2s -- and
    # that was tried twice, filed as "cause not found", and worked around by
    # halving the parallelism. The cause was not in this harness: the box's
    # DDR5 was running on an XMP profile it was not stable at. With the memory
    # back in spec the machine has run 25+ concurrent VMs without a fault
    # (Damian, 2026-07-22), so the workaround is retired rather than kept as
    # folklore. If slot-count flakes ever come back, suspect the hardware
    # before the harness: this one cost two investigations that could not have
    # succeeded.
    [int]$Jobs = 8,
    # Emulator budget per test -- a CEILING, not a sentence. A run ends the
    # moment its output is COMPLETE against .expected (filtered line count
    # reached, final newline seen): a complete answer, right or wrong, is a
    # real one. The ceiling only decides the fate of runs that never complete:
    # zero bytes is the dead-silent contention class, anything else is
    # FAIL_STARVED. Completeness, not silence, is the exit signal -- a quiet
    # window was tried first and refuted the same day: av-codec-test prints
    # line 1 in ~1s then computes 15s+ before line 2, so any inter-byte
    # window reads a compute gap as termination. One second used to cut
    # correct runs short; a flat ten was then paid in full by every test,
    # which was most of the phase.
    [int]$RenoTimeout = 10,
    # Renode's CreateFileBackend puts nothing on disk until teardown (measured;
    # see the run block), so the live channel is a server-socket uart terminal.
    # Every test gets its own port (base + index) so parallel slots cannot
    # collide.
    [int]$UartPortBase = 42000,
    # Wall-clock budget for one compile. It is a parameter and not a literal
    # because the serial-retry pass below is only meaningful if a timeout can
    # actually be provoked: set it low (-CompileTimeoutSec 2) and the contended
    # compiles time out while the same tests pass alone, which is the exact
    # behaviour the retry exists for and the only way to see it fire on demand.
    [int]$CompileTimeoutSec = 120,
    [string]$Filter = "",
    [switch]$UseQemu
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

$plugName = if ($Arch -eq 'arm64') { 'arm64' } else { 'riscv' }
$testDir = Join-Path $Repo 'codex\test'
$compileScript = Join-Path $Repo "codex\plugs\$plugName\compile-$plugName.ps1"
. (Join-Path $PSScriptRoot 'renode-config.ps1')
$renodeExe = Get-RenodeExe -Repo $Repo
if (-not $renodeExe -and -not $UseQemu) { Write-RenodeSkip; exit 0 }
$boardRepl = Join-Path $Repo "tools\renode\codex\codex-${Arch}.repl"
$outRoot = Join-Path $Repo "test-output-cross\$Arch"

$seedCdx = Join-Path $Repo 'seed\Codex.cdx'
$stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $stage0) | Out-Null
if (-not (Test-Path $stage0)) { Copy-Item -Force $seedCdx $stage0 }

# Discover eligible tests
$allTests = Get-ChildItem "$testDir\*.codex", "$testDir\ops\*.codex" | Sort-Object Name
$eligible = [System.Collections.Generic.List[object]]::new()
$skipCount = 0

foreach ($tf in $allTests) {
    $name = $tf.BaseName
    $dir = $tf.DirectoryName
    if ($Filter -and $name -notlike "*$Filter*") { continue }
    $skipReason = $null
    if (Test-Path "$dir\$name.skip")    { $skipReason = (Get-Content -TotalCount 1 "$dir\$name.skip") }
    elseif (Test-Path "$dir\$name.slow")    { $skipReason = "slow" }
    elseif (Test-Path "$dir\$name.fatal")   { $skipReason = "fatal" }
    elseif (Test-Path "$dir\$name.failing") { $skipReason = "error test" }
    elseif (Test-Path "$dir\$name.smp")     { $skipReason = "multi-core (build/test-cross-smp.ps1)" }
    elseif (Test-Path "$dir\$name.no-cross") { $skipReason = "no-cross: " + (Get-Content -TotalCount 1 "$dir\$name.no-cross") }
    else {
        # A machine-sidecar test names its own ineligibility: the fixture is
        # the x86 codex-vm (a disk image, an attached CDX, VM flags, a
        # scancode timeline), and no cross board can mount it. Excluded here
        # the way .smp routes multi-core tests elsewhere, so the exclusion
        # cannot rot the way a hand-kept list would (BatteryReorg step 10).
        foreach ($mc in 'disk','disk2','disk-src','vmargs','keys') {
            if (Test-Path "$dir\$name.$mc") { $skipReason = "machine sidecar (.$mc)"; break }
        }
    }
    if ($skipReason) { $skipCount++; Write-Host "SKIP $name ($skipReason)"; continue }
    $eligible.Add(@{ File = $tf; Name = $name; Dir = $dir })
}

Write-Host "`n=== $($Arch.ToUpper()) Cross Battery: $($eligible.Count) eligible, $skipCount skipped, $Jobs parallel slots ==="
$batteryStart = Get-Date

# ---- Phase 1: Compile all tests (parallel) ----
#
# This phase used to run one test at a time and it was the whole battery:
# measured 2026-07-21, 1141s of a 1332s ARM64 run, 352 tests at a 3.2s mean.
# Nothing about it was ever serial by nature -- each test is an independent
# pipeline of two VM boots (seed to IR, plug to wire) and compile.ps1 and
# run.ps1 already name every temporary with GetTempFileName. The one thing
# that was shared is compile-<arch>.ps1's build-output/last-compile.* triple,
# which is why each slot is handed its own -WorkDir here: without it, parallel
# compiles silently swap each other's IR and the ELF you get is another test's.
Write-Host "`n--- Phase 1: Compile ($Jobs parallel slots) ---"
$compiled = [System.Collections.Concurrent.ConcurrentDictionary[string,hashtable]]::new()
$compileStart = Get-Date
# A plain counter does not survive the runspace boundary: `[ref]$arr[0]` binds a
# copy of the element, not the slot, so every slot reported "[1/352]". A
# concurrent collection is shared by reference and its Count is the progress.
$doneBag = [System.Collections.Concurrent.ConcurrentBag[int]]::new()
$totalCount = $eligible.Count

# The body is a variable rather than an inline block so the serial retry pass
# below can run the identical code at ThrottleLimit 1. `$using:` resolves at
# invocation, so a scriptblock in a variable works exactly as an inline one --
# verified rather than assumed before this was restructured.
$compileBlock = {
    $t = $_
    $name = $t.Name
    $outRoot = $using:outRoot
    $compileScript = $using:compileScript
    $done = $using:doneBag
    $total = $using:totalCount

    # WaitForExit(timeout) returns when the child dies, not when its redirected
    # stdout handle is released, so a read that follows immediately can find the
    # log still locked. Share-tolerant open plus a short retry; an unreadable
    # log after that reads as empty, which fails toward FAIL rather than PASS.
    function Read-LogShared([string]$path) {
        for ($ri = 0; $ri -lt 5; $ri++) {
            try {
                $fs = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                try {
                    $sr = [System.IO.StreamReader]::new($fs)
                    return $sr.ReadToEnd()
                } finally { $fs.Dispose() }
            } catch { Start-Sleep -Milliseconds 100 }
        }
        return ''
    }

    $testOutDir = Join-Path $outRoot $name
    New-Item -ItemType Directory -Force $testOutDir | Out-Null
    $elfOut = Join-Path $testOutDir "$name.elf"
    $compileLog = Join-Path $testOutDir 'compile.log'

    $cs = Get-Date
    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-File',$compileScript,'-Src',($t.File).FullName,'-Out',$elfOut,'-WorkDir',$testOutDir) -NoNewWindow -PassThru -RedirectStandardOutput $compileLog -RedirectStandardError (Join-Path $testOutDir 'compile.err')
    # A `compile N` line in the test's .cross-budget raises THIS test's
    # compile ceiling, the same pricing the sidecar's first line gives its
    # run ceiling: the global budget stays tight and the one known-heavy
    # test names its own cost (ui-orchestrator-test's 48 MB IR takes ~8
    # minutes through emit + plug, measured 2026-07-28).
    $budgetSec = $using:CompileTimeoutSec
    $budgetFile = Join-Path ($t.Dir) "$name.cross-budget"
    if (Test-Path -PathType Leaf $budgetFile) {
        foreach ($bl in (Get-Content $budgetFile)) {
            if ($bl -match '^compile\s+(\d+)') { $budgetSec = [int]$Matches[1]; break }
        }
    }
    $finished = $proc.WaitForExit($budgetSec * 1000)
    if (-not $finished) { try { $proc.Kill() } catch {} }
    $compileExit = if ($finished) { $proc.ExitCode } else { 99 }
    $ct = [math]::Round(((Get-Date) - $cs).TotalSeconds, 1)

    $done.Add(1)
    $n = $done.Count
    $hasExp = Test-Path -PathType Leaf (Join-Path ($t.Dir) "$name.expected")
    # .cross-refusal inverts the compile expectation: this test's DESIGNED
    # behavior on a cross lane is a refusal -- one "[UNSUPPORTED] <builtin>"
    # report per named line, and no binary (port and GPU-port
    # surface). The sidecar is what keeps the refusal a tested behavior
    # rather than an unwired guard: lose the plug's refusal arms and the
    # test compiles clean, and this row goes red.
    $refusalFile = Join-Path ($t.Dir) "$name.cross-refusal"
    if (Test-Path -PathType Leaf $refusalFile) {
        $tags = @(Get-Content $refusalFile | Where-Object { $_ -and -not $_.StartsWith('#') })
        if ($compileExit -eq 0 -and (Test-Path $elfOut)) {
            Write-Host "[$n/$total] compile $name ... FAIL (${ct}s, compiled clean; expected refusal)"
            @{ Name = $name; Status = 'FAIL_REFUSAL_MISSING'; CompileTime = $ct; Reason = 'compiled clean; expected [UNSUPPORTED] refusal'; HasExpected = $false; ElfPath = $null; Dir = $t.Dir }
        } elseif ($compileExit -eq 99) {
            # The compile died at the wall-clock budget BEFORE the plug could
            # answer, so "the tags are missing" would blame the test for the
            # box being busy. Report it as the timeout it is; the serial retry
            # pass re-runs this same block alone and classifies for real.
            Write-Host "[$n/$total] compile $name ... FAIL (${ct}s, timeout (${budgetSec}s))"
            @{ Name = $name; Status = 'FAIL_COMPILE'; CompileTime = $ct; Reason = "timeout (${budgetSec}s)"; HasExpected = $false; ElfPath = $null; Dir = $t.Dir }
        } else {
            $logText = ''
            foreach ($lf in @($compileLog, (Join-Path $testOutDir 'compile-ir.log'))) {
                if (Test-Path $lf) { $logText += Read-LogShared $lf }
            }
            $missing = @($tags | Where-Object { -not $logText.Contains("[UNSUPPORTED] $_") })
            if ($missing.Count -eq 0) {
                Write-Host "[$n/$total] compile $name ... REFUSED as designed (${ct}s)"
                @{ Name = $name; Status = 'PASS_REFUSED'; CompileTime = $ct; Reason = "refused: $($tags -join ', ')"; HasExpected = $false; ElfPath = $null; Dir = $t.Dir }
            } else {
                Write-Host "[$n/$total] compile $name ... FAIL (${ct}s, refusal tag(s) missing: $($missing -join ', '))"
                @{ Name = $name; Status = 'FAIL_COMPILE'; CompileTime = $ct; Reason = "compile failed without expected refusal tag(s): $($missing -join ', ')"; HasExpected = $false; ElfPath = $null; Dir = $t.Dir }
            }
        }
    } elseif ($compileExit -ne 0 -or -not (Test-Path $elfOut)) {
        # A non-zero exit is not automatically a real compile error, and telling
        # the two apart is what this scan is for. A Codex compile that REJECTS a
        # program always says so: a CDX-numbered diagnostic, or CODEGEN-HALTED,
        # lands in the log. A compile whose VM was killed under load says
        # nothing at all and exits -1. Before -Jobs 8 that class was rare enough
        # to go unnoticed; at eight slots `chapter-pages` hit it on the first
        # run, reported FAIL_COMPILE, and passed standalone -- a phantom
        # regression of exactly the kind the timeout retry was built to stop.
        # An [UNSUPPORTED] refusal on a test with no .cross-refusal sidecar is
        # a third class: a real, deterministic answer (this program reaches a
        # builtin the lane refuses by design), so it is reported by its first
        # refusal line and never retried.
        $diag = $false
        $refusedLine = $null
        foreach ($lf in @($compileLog, (Join-Path $testOutDir 'compile-ir.log'))) {
            if (Test-Path $lf) {
                if (Select-String -Path $lf -Pattern 'CDX\d{4}|CODEGEN-HALTED' -Quiet) { $diag = $true }
                if (-not $refusedLine) {
                    $m = Select-String -Path $lf -Pattern '\[UNSUPPORTED\][^\r\n]*' | Select-Object -First 1
                    if ($m) { $refusedLine = $m.Matches[0].Value }
                }
            }
        }
        $reason = if ($compileExit -eq 99) { "timeout (${budgetSec}s)" }
                  elseif ($refusedLine) { $refusedLine }
                  elseif (-not $diag) { "exit=$compileExit (no diagnostic)" }
                  else { "exit=$compileExit" }
        Write-Host "[$n/$total] compile $name ... FAIL (${ct}s, $reason)"
        @{ Name = $name; Status = 'FAIL_COMPILE'; CompileTime = $ct; Reason = $reason; HasExpected = $false; ElfPath = $null; Dir = $t.Dir }
    } else {
        Write-Host "[$n/$total] compile $name ... OK (${ct}s)"
        @{ Name = $name; Status = 'COMPILED'; CompileTime = $ct; Reason = $null; HasExpected = $hasExp; ElfPath = $elfOut; Dir = $t.Dir }
    }
}

$compileResults = @($eligible | ForEach-Object -ThrottleLimit $Jobs -Parallel $compileBlock)

# ---- Phase 1 retry: a compile timeout is contention, not a compile failure ----
#
# The 120s budget is a wall clock and this phase runs $Jobs deep, so a large
# test can exceed it because the box is busy and not because anything is wrong
# with it. The harness used to record that as FAIL_COMPILE, which reads exactly
# like a broken plug. db-full-test, gpu-depth-tree and gpu-panel-border have
# flipped between FAIL_COMPILE and FAIL_OUTPUT across three sessions on that
# alone, and the standing instruction "always re-run a batch FAIL standalone
# before believing it" was a manual step covering for the harness.
#
# So the harness takes the step: every timed-out compile is retried once, alone,
# with the same budget.
#
# Two classes are retried, and both are "the box was busy" rather than "the
# program is wrong": a timeout, and a non-zero exit that produced NO compiler
# diagnostic. The second was added when -Jobs went to 8. A compile that really
# rejects a program always leaves a CDX-numbered error or CODEGEN-HALTED in the
# log, so requiring the absence of one keeps the invariant that matters -- a
# genuine compile error is still never re-run, and cannot be hidden behind load.
# What is retried is the case where the VM died and said nothing.
$compileRetried = 0
$compileRecovered = 0
$timedOut = @($compileResults | Where-Object { $_.Status -eq 'FAIL_COMPILE' -and ($_.Reason -like 'timeout*' -or $_.Reason -like '*no diagnostic*') })
if ($timedOut.Count -gt 0) {
    $timedOutNames = @($timedOut | ForEach-Object { $_.Name })
    Write-Host "`n--- Phase 1 retry: $($timedOut.Count) compile timeout(s), one at a time ---"
    $retryIn = @($eligible | Where-Object { $timedOutNames -contains $_.Name })
    $doneBag = [System.Collections.Concurrent.ConcurrentBag[int]]::new()
    $totalCount = $retryIn.Count
    $retryResults = @($retryIn | ForEach-Object -ThrottleLimit 1 -Parallel $compileBlock)

    $byName = @{}
    foreach ($cr in $compileResults) { $byName[$cr.Name] = $cr }
    foreach ($rr in $retryResults) {
        $compileRetried++
        if ($rr.Status -eq 'COMPILED') {
            $compileRecovered++
            $rr.Reason = 'compiled on serial retry (parallel-phase timeout)'
        } else {
            $rr.Reason = "$($rr.Reason), still failing alone"
        }
        $byName[$rr.Name] = $rr
    }
    $compileResults = @($byName.Values)
    Write-Host "Phase 1 retry: $compileRecovered of $compileRetried recovered"
}

foreach ($cr in $compileResults) {
    [void]$compiled.TryAdd($cr.Name, @{
        Status = $cr.Status; CompileTime = $cr.CompileTime; RunTime = $null
        Reason = $cr.Reason; HasExpected = $cr.HasExpected; ElfPath = $cr.ElfPath; Dir = $cr.Dir
    })
}
$compileEnd = Get-Date
Write-Host "Compile phase: $([math]::Round(($compileEnd - $compileStart).TotalSeconds, 1))s"

# ---- Phase 2: Run (parallel) ----
$emulatorLabel = if ($UseQemu) { "QEMU" } else { "Renode" }
# The run phase stays at $Jobs under Renode. Eight slots was tried twice and
# flakes both times -- a passing test comes back FAIL_RUNTIME with no uart
# output after ~2s. The obvious suspect was the 240 MB memsz every ELF carries
# ("Loading block of 251669168 bytes" in renode.log), so that was measured and
# then dropped as a cause: the zero-fill is ~130 ms of a 12.6s test, and eight
# slots flake with or without it.
#
# This phase used to spend RenoTimeout in full on every test: 12.6s = a flat
# 10s sleep + ~2.6s of Renode start and teardown, most of the phase's wall
# time. Both legs now exit the moment the output is complete against
# .expected and the budget is only a ceiling; see the run block for how each
# leg watches the uart, because the mechanism is different on each. The
# expected line count is precomputed here, with the compare's own
# normalization, so the runner knows what "complete" means for each test.
$runJobs = if ($UseQemu) { [Math]::Max($Jobs, 8) } else { $Jobs }
Write-Host "`n--- Phase 2: Run via $emulatorLabel (${runJobs} parallel slots, exit on complete output, ceiling ${RenoTimeout}s) ---"
$toRun = [System.Collections.Generic.List[hashtable]]::new()
foreach ($kv in $compiled.GetEnumerator()) {
    $v = $kv.Value
    if ($v.Status -eq 'COMPILED' -and $v.HasExpected) {
        $expText = [System.IO.File]::ReadAllText((Join-Path $v.Dir "$($kv.Key).expected")) -replace "`r",''
        $expArr = @($expText -split "`n")
        $expN = $expArr.Count
        while ($expN -gt 0 -and $expArr[$expN - 1] -eq '') { $expN-- }
        # A .cross-budget sidecar raises THIS test's ceiling (first line,
        # seconds). The crypto cluster is correct under emulation and just
        # slow -- all six passed alone at a raised ceiling, 14-41s -- and the
        # completeness exit means a passing test pays its true runtime, never
        # the ceiling, so the sidecar costs wall time only when the test is
        # actually broken.
        $ceil = $RenoTimeout
        $budgetFile = Join-Path $v.Dir "$($kv.Key).cross-budget"
        if (Test-Path -PathType Leaf $budgetFile) {
            $bv = 0
            if ([int]::TryParse((Get-Content -TotalCount 1 $budgetFile).Trim(), [ref]$bv) -and $bv -gt 0) { $ceil = $bv }
        }
        $toRun.Add(@{ Name = $kv.Key; Elf = $v.ElfPath; Dir = $v.Dir; Index = $toRun.Count; ExpLines = $expN; CeilSec = $ceil })
    }
}
Write-Host "$($toRun.Count) tests to run"
$runStart = Get-Date

$qemuExe = ''
$loadAddr = ''
if ($UseQemu) {
    $qemuExe = if ($Arch -eq 'riscv64') {
        "D:\Program Files\qemu\qemu-system-riscv64.exe"
    } else {
        "D:\Program Files\qemu\qemu-system-aarch64.exe"
    }
    if (-not (Test-Path $qemuExe)) { Write-Error "QEMU not found: $qemuExe"; exit 1 }
    $loadAddr = if ($Arch -eq 'riscv64') { '0x80000000' } else { '0x40100000' }
}

$boardPath = (Resolve-Path $boardRepl).Path -replace '\\','/'
$runBlock = {
    $t = $_
    $name = $t.Name
    $useQ = $using:UseQemu
    $outRoot = $using:outRoot

    $testOutDir = Join-Path $outRoot $name
    $uartLogWin = Join-Path $testOutDir 'uart.log'
    if (Test-Path $uartLogWin) { Remove-Item $uartLogWin -Force }

    $rs = Get-Date
    $status = 'UNKNOWN'
    $reason = ''
    $ceilingSec = [int]$t.CeilSec
    $expLinesN = [int]$t.ExpLines

    # Completeness is the exit signal, measured with the compare's own
    # normalization (CR stripped, HEAP:/WD:/STACK: dropped, trailing blanks
    # trimmed): the run is over when the filtered line count reaches
    # .expected's and the final line is closed by a newline. Silence is NOT
    # the signal -- av-codec-test prints line 1 in ~1s and then computes 15s+
    # before line 2, so any quiet-window exit reads a compute gap as
    # termination and was refuted by exactly that measurement.
    function Get-UartProgress([string]$rawText) {
        $tx = $rawText -replace "`r",''
        $ls = $tx -split "`n"
        $n = $ls.Count
        while ($n -gt 0 -and $ls[$n - 1] -eq '') { $n-- }
        $c = 0
        for ($i = 0; $i -lt $n; $i++) {
            if ($ls[$i].StartsWith('HEAP:') -or $ls[$i].StartsWith('WD:') -or $ls[$i].StartsWith('STACK:')) { continue }
            $c++
        }
        @{ Lines = $c; Closed = $tx.EndsWith("`n"); Bytes = $rawText.Length }
    }

    if ($useQ) {
        $qemuExe = $using:qemuExe
        $loadAddr = $using:loadAddr
        $arch = $using:Arch
        $binFile = Join-Path $testOutDir "$name.bin"
        if (-not (Test-Path $binFile)) { $binFile = (Get-ChildItem "$testOutDir\*.bin" | Select-Object -First 1).FullName }
        if (-not $binFile -or -not (Test-Path $binFile)) {
            $status = 'FAIL_RUNTIME'; $reason = 'no .bin file'
        } else {
            # RISC-V guest RAM must cover the plug's boot stack pointer. Keep this -m
            # in sync with rv-sp in codex/plugs/riscv/RiscVRuntime.codex (#BFFF0000,
            # top of 1 GB) and the Renode dram size in tools/renode/codex/codex-riscv64.repl
            # (size 0x40000000). All three must describe the same 1 GB @ 0x80000000.
            # The RISC-V code is position-independent (PC-relative), so the flat .bin loaded
            # at 0x80000000 for QEMU and the ELF loaded at 0x80000080 for Renode run identically.
            $machArgs = if ($arch -eq 'riscv64') {
                @('-M','virt','-m','1024M','-display','none','-monitor','none','-bios','none',
                  '-device',"loader,file=$binFile,addr=$loadAddr",'-serial',"file:$uartLogWin")
            } else {
                # This leg had never produced a byte, for three stacked
                # reasons measured 2026-07-28. `-bios none` is a riscv-ism:
                # qemu-system-aarch64 tries to load a ROM literally named
                # "none" and exits before the guest runs (stderr: Could not
                # find ROM image 'none'), so every test read act=[] in ~1s.
                # Behind that, -device loader is not a boot on arm64 (a
                # riscv hart starts at the load address; an arm64 -M virt
                # CPU starts at its reset vector, so the loaded bytes never
                # ran) -- boot the ELF with -kernel so QEMU honours its
                # entry, exactly as test-cross-smp.ps1 does. And the arm64
                # boot stack tops at 0x7F000000, ~1 GB above the RAM base,
                # so -m 256M put the stack outside guest RAM; 1024M matches
                # the smp runner and the Renode board.
                $elfFile = Join-Path $testOutDir "$name.elf"
                @('-M','virt','-cpu','cortex-a53','-m','1024M','-display','none','-monitor','none',
                  '-kernel',$elfFile,'-serial',"file:$uartLogWin")
            }
            $proc = Start-Process -FilePath $qemuExe -ArgumentList $machArgs -PassThru -NoNewWindow
            # QEMU's file chardev writes the log live, so the poller reads it
            # through a share-tolerant open (the directory entry's size is not
            # updated while QEMU holds the handle) and exits at completeness.
            $deadline = (Get-Date).AddSeconds($ceilingSec)
            $prog = @{ Lines = 0; Closed = $false; Bytes = 0 }
            $starved = $false
            while ($true) {
                Start-Sleep -Milliseconds 250
                if (Test-Path $uartLogWin) {
                    try {
                        $fs = [System.IO.FileStream]::new($uartLogWin, [System.IO.FileMode]::Open,
                            [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                        try {
                            $sr = [System.IO.StreamReader]::new($fs)
                            $prog = Get-UartProgress ($sr.ReadToEnd())
                        } finally { $fs.Dispose() }
                    } catch {}
                }
                if ($expLinesN -gt 0 -and $prog.Lines -ge $expLinesN -and $prog.Closed) { break }
                if ($proc.HasExited) { break }
                if ((Get-Date) -ge $deadline) { $starved = ($prog.Bytes -gt 0); break }
            }
            if (-not $proc.HasExited) { try { $proc.Kill() } catch {} }
            if ($starved) {
                $status = 'FAIL_STARVED'
                $reason = "incomplete at ceiling (${ceilingSec}s: $($prog.Lines) of $expLinesN lines, $($prog.Bytes) bytes)"
            }
        }
    } else {
        $renodeExe = $using:renodeExe
        $boardPath = $using:boardPath
        $port = ($using:UartPortBase) + [int]$t.Index
        $elfPath = (Resolve-Path ($t.Elf)).Path -replace '\\','/'

        # Watching a CreateFileBackend log is NOT available as the exit signal:
        # it puts nothing on disk until Renode tears down (measured -- fourteen
        # reads over seven seconds, "no-file" every time), so a kill at the
        # moment the guest is done produces an empty log and a FAIL_RUNTIME.
        # The live channel is a raw server-socket uart terminal instead. The
        # resc neither starts nor quits: the host connects to the socket FIRST
        # and only then issues `start` over Renode's stdin, so a boot-time byte
        # cannot be lost to the connect race, and `quit` follows quiescence.
        # The harness writes uart.log itself from the socket bytes, so
        # everything downstream of the run is unchanged.
        $rescContent = @(
            'mach create "codex"'
            "machine LoadPlatformDescription @$boardPath"
            "sysbus LoadELF @$elfPath"
            "emulation CreateServerSocketTerminal $port ""uart-srv"" false"
            'connector Connect uart0 uart-srv'
        ) -join "`n"
        $rescFile = Join-Path $testOutDir 'run.resc'
        [System.IO.File]::WriteAllText($rescFile, $rescContent)
        $rescPath = ($rescFile -replace '\\','/')

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $renodeExe
        $psi.Arguments = "--disable-xwt --console -e ""include @$rescPath"""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        # Drained asynchronously so a chatty monitor cannot fill the pipe and
        # deadlock the child; the content itself is not interesting.
        $null = $proc.StandardOutput.ReadToEndAsync()
        $null = $proc.StandardError.ReadToEndAsync()

        $client = $null
        $connectDeadline = (Get-Date).AddSeconds(30)
        while (-not $client -and -not $proc.HasExited -and (Get-Date) -lt $connectDeadline) {
            $c = [System.Net.Sockets.TcpClient]::new()
            try { $c.Connect('127.0.0.1', $port); $client = $c }
            catch { $c.Dispose(); Start-Sleep -Milliseconds 200 }
        }

        if (-not $client) {
            # Renode never opened the uart socket: the start itself failed or
            # the box is too loaded to get there. Contention-shaped, so the
            # serial retry pass takes it (same class as dead silence).
            $status = 'FAIL_RUNTIME'; $reason = 'no uart output'
            try { if (-not $proc.HasExited) { $proc.Kill() } } catch {}
        } else {
            $stream = $client.GetStream()
            $proc.StandardInput.WriteLine('start')
            $proc.StandardInput.Flush()

            $buf = [System.IO.MemoryStream]::new()
            $chunk = [byte[]]::new(65536)
            $deadline = (Get-Date).AddSeconds($ceilingSec)
            $prog = @{ Lines = 0; Closed = $false; Bytes = 0 }
            $starved = $false
            while ($true) {
                $got = $false
                while ($stream.DataAvailable) {
                    $n = $stream.Read($chunk, 0, $chunk.Length)
                    if ($n -le 0) { break }
                    $buf.Write($chunk, 0, $n)
                    $got = $true
                }
                if ($got) {
                    $prog = Get-UartProgress ([System.Text.Encoding]::ASCII.GetString($buf.ToArray()))
                }
                if ($expLinesN -gt 0 -and $prog.Lines -ge $expLinesN -and $prog.Closed) { break }
                if ((Get-Date) -ge $deadline) { $starved = ($buf.Length -gt 0); break }
                Start-Sleep -Milliseconds 100
            }

            # quit is a courtesy; nothing downstream needs Renode's teardown
            # (the harness owns uart.log), so a slow exit is killed rather
            # than waited out.
            try { $proc.StandardInput.WriteLine('quit'); $proc.StandardInput.Flush() } catch {}
            if (-not $proc.WaitForExit(500)) { try { $proc.Kill() } catch {} }
            try {
                while ($stream.DataAvailable) {
                    $n = $stream.Read($chunk, 0, $chunk.Length)
                    if ($n -le 0) { break }
                    $buf.Write($chunk, 0, $n)
                }
            } catch {}
            $client.Dispose()

            if ($buf.Length -gt 0) { [System.IO.File]::WriteAllBytes($uartLogWin, $buf.ToArray()) }
            if ($starved) {
                $status = 'FAIL_STARVED'
                $reason = "incomplete at ceiling (${ceilingSec}s: $($prog.Lines) of $expLinesN lines, $($buf.Length) bytes)"
            }
        }
    }

    $re = Get-Date
    $rt = [math]::Round(($re - $rs).TotalSeconds, 1)

    # Only a run that ended with a COMPLETE answer gets compared: FAIL_STARVED
    # means the answer is not finished, and comparing a partial answer files a
    # starved guest as a deterministic wrong one, which is the
    # misclassification this exit exists to end. An empty log is the same fact
    # as a missing one -- the guest never said anything -- so both are the
    # dead-silent retry class instead of FAIL_OUTPUT act=[].
    if ($status -eq 'UNKNOWN') {
        Start-Sleep -Milliseconds 50
        if (-not (Test-Path $uartLogWin) -or (Get-Item $uartLogWin).Length -eq 0) {
            $status = 'FAIL_RUNTIME'; $reason = 'no uart output'
        } else {
            $raw = [System.IO.File]::ReadAllText($uartLogWin) -replace "`r",''
            $allLines = $raw -split "`n"
            $lines = [System.Collections.Generic.List[string]]::new()
            foreach ($l in $allLines) {
                if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
                $lines.Add($l)
            }
            while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') {
                $lines.RemoveAt($lines.Count - 1)
            }

            $expectedFile = Join-Path ($t.Dir) "$name.expected"
            $expectedText = [System.IO.File]::ReadAllText($expectedFile) -replace "`r",''
            $expAllLines = @($expectedText -split "`n")
            while ($expAllLines.Count -gt 0 -and $expAllLines[$expAllLines.Count - 1] -eq '') {
                $expAllLines = $expAllLines[0..($expAllLines.Count - 2)]
            }
            $expLineCount = $expAllLines.Count
            if ($lines.Count -gt $expLineCount -and $expLineCount -gt 0) {
                $lines = [System.Collections.Generic.List[string]]::new(
                    [string[]]@($lines | Select-Object -First $expLineCount))
            }

            $actual = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
            $actualFile = Join-Path $testOutDir 'runtime.actual'
            [System.IO.File]::WriteAllText($actualFile, $actual, [System.Text.UTF8Encoding]::new($false))

            # Same normalization on both sides (see test-cross.ps1): a sidecar
            # without a trailing newline could never pass against the forced
            # final newline on actual.
            $expected = if ($expLineCount -gt 0) { ($expAllLines -join "`n") + "`n" } else { '' }

            if ($expected -eq $actual) {
                $status = 'PASS_EXPECTED'
            } else {
                $status = 'FAIL_OUTPUT'
                # The first differing line, wherever it is. This used to stop
                # at line 3, so a test diverging later reported an empty
                # reason and read as unexplained (network-effect was the case
                # that surfaced it).
                $expLines = $expected -split "`n"
                $actLines = $actual -split "`n"
                $maxL = [Math]::Max($expLines.Count, $actLines.Count)
                for ($i = 0; $i -lt $maxL; $i++) {
                    $e = if ($i -lt $expLines.Count) { $expLines[$i] } else { '(missing)' }
                    $a2 = if ($i -lt $actLines.Count) { $actLines[$i] } else { '(missing)' }
                    if ($e -ne $a2) { $reason = "line $($i+1): exp=[$e] act=[$a2]"; break }
                }
            }
        }
    }
    @{ Name = $name; Status = $status; RunTime = $rt; Reason = $reason }
}

$runResults = @($toRun | ForEach-Object -ThrottleLimit $runJobs -Parallel $runBlock)

# ---- Phase 2 retry: silence or starvation under load is contention, not a wrong answer ----
#
# The run phase has the same class as the compile phase and it has cost more
# sessions: mask-ops and implicit-convert have each gone PASS to FAIL_RUNTIME in
# one sweep and back in the next. Two shapes are contention: a test that
# produced no uart output at all, and a test whose answer was still incomplete
# at the ceiling (FAIL_STARVED) -- emulation under $runJobs-deep load is slow,
# not wrong, and the retry runs it alone at the same ceiling to find out
# which. A test that COMPLETED its answer with the WRONG content is a real
# answer and is deliberately not retried, because re-running a deterministic
# wrong answer only spends time and could mask a genuine intermittent defect
# behind the word "flake". Incomplete alone at the ceiling stays FAIL_STARVED,
# noted "still incomplete alone": a budget verdict, visibly distinct from a
# wrong answer.
$runRetried = 0
$runRecovered = 0
$noOutput = @($runResults | Where-Object {
    ($_.Status -eq 'FAIL_RUNTIME' -and $_.Reason -eq 'no uart output') -or $_.Status -eq 'FAIL_STARVED' })
if ($noOutput.Count -gt 0) {
    $noOutputNames = @($noOutput | ForEach-Object { $_.Name })
    Write-Host "`n--- Phase 2 retry: $($noOutput.Count) silent or starved test(s), one at a time ---"
    $rerunIn = @($toRun | Where-Object { $noOutputNames -contains $_.Name })
    $retryRun = @($rerunIn | ForEach-Object -ThrottleLimit 1 -Parallel $runBlock)

    $byName = @{}
    foreach ($rr in $runResults) { $byName[$rr.Name] = $rr }
    foreach ($rr in $retryRun) {
        $runRetried++
        if ($rr.Status -eq 'PASS_EXPECTED') {
            $runRecovered++
            $rr.Reason = 'passed on serial retry (silent or starved under parallel load)'
        } elseif ($rr.Status -eq 'FAIL_RUNTIME') {
            $rr.Reason = "$($rr.Reason), still silent alone"
        } elseif ($rr.Status -eq 'FAIL_STARVED') {
            $rr.Reason = "$($rr.Reason), still incomplete alone"
        }
        $byName[$rr.Name] = $rr
    }
    $runResults = @($byName.Values)
    Write-Host "Phase 2 retry: $runRecovered of $runRetried recovered"
}

$runEnd = Get-Date
Write-Host "Run phase: $([math]::Round(($runEnd - $runStart).TotalSeconds, 1))s"

# Merge results
foreach ($rr in $runResults) {
    $c = $compiled[$rr.Name]
    $c.Status = $rr.Status
    $c.RunTime = $rr.RunTime
    $c.Reason = $rr.Reason
}

# Mark compile-only
foreach ($kv in $compiled.GetEnumerator()) {
    $v = $kv.Value
    if ($v.Status -eq 'COMPILED' -and -not $v.HasExpected) { $v.Status = 'PASS_COMPILE_ONLY' }
}

$batteryEnd = Get-Date
$totalSec = [math]::Round(($batteryEnd - $batteryStart).TotalSeconds, 1)
$totalMin = [math]::Round($totalSec / 60, 1)

# Tally
$passCount = 0; $failCount = 0; $compileOnlyCount = 0; $refusedCount = 0
foreach ($kv in $compiled.GetEnumerator()) {
    $v = $kv.Value
    if ($v.Status -eq 'PASS_EXPECTED') { $passCount++ }
    elseif ($v.Status -eq 'PASS_COMPILE_ONLY') { $compileOnlyCount++ }
    elseif ($v.Status -eq 'PASS_REFUSED') { $refusedCount++ }
    elseif ($v.Status -like 'FAIL*') { $failCount++ }
}

# Generate markdown
$md = [System.Text.StringBuilder]::new()
$archLabel = if ($Arch -eq 'arm64') { 'ARM64' } else { 'RISC-V 64' }
$boardLabel = if ($Arch -eq 'arm64') { 'codex-arm64.repl, Cortex-A53 + PL011' } else { 'codex-riscv64.repl, RV64GC + NS16550' }
$emuLabel = if ($UseQemu) { "QEMU ($Arch virt)" } else { "Renode (``$boardLabel``)" }
[void]$md.AppendLine("# $archLabel Cross-Compilation Test Results")
[void]$md.AppendLine("")
[void]$md.AppendLine("**Date**: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$md.AppendLine("**Seed**: ``seed/Codex.cdx``")
[void]$md.AppendLine("**Plug**: ``codex/plugs/$plugName/build-output/$plugName-plug.cdx``")
[void]$md.AppendLine("**Emulator**: $emuLabel")
[void]$md.AppendLine("**Parallel slots**: $runJobs")
[void]$md.AppendLine("**Emulator budget**: ${RenoTimeout}s ceiling per test; a run exits the moment its output is complete against .expected")
[void]$md.AppendLine("**Total time**: ${totalMin} min (${totalSec}s)")
# State the retries even when there are none. A run that silently absorbed a
# load flake reads identical to one that never had a flake, and the difference
# is exactly what a per-test diff against a previous run trips over.
[void]$md.AppendLine("**Serial retries**: compile $compileRecovered/$compileRetried recovered, run $runRecovered/$runRetried recovered (retried: compile timeouts, compiles that died with no diagnostic, runs with no output, and runs incomplete at the ceiling; a wrong answer and a real compile error are never re-run)")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Summary")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Status | Count |")
[void]$md.AppendLine("|--------|------:|")
[void]$md.AppendLine("| PASS_EXPECTED | $passCount |")
[void]$md.AppendLine("| PASS_COMPILE_ONLY | $compileOnlyCount |")
[void]$md.AppendLine("| PASS_REFUSED | $refusedCount |")
[void]$md.AppendLine("| FAIL | $failCount |")
[void]$md.AppendLine("| SKIPPED | $skipCount |")
[void]$md.AppendLine("| **Total** | **$($compiled.Count + $skipCount)** |")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Detailed Results")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Test | Status | Compile (s) | Run (s) | Notes |")
[void]$md.AppendLine("|------|--------|------------:|--------:|-------|")

foreach ($kv in ($compiled.GetEnumerator() | Sort-Object Key)) {
    $r = $kv.Value
    $ct = if ($null -ne $r.CompileTime) { "$($r.CompileTime)" } else { "---" }
    $rt = if ($null -ne $r.RunTime) { "$($r.RunTime)" } else { "---" }
    $note = if ($r.Reason) { ($r.Reason -replace '\|','\|') } else { "" }
    if ($note.Length -gt 80) { $note = $note.Substring(0, 77) + "..." }
    [void]$md.AppendLine("| $($kv.Key) | $($r.Status) | $ct | $rt | $note |")
}

# Results go to the untracked output tree, NOT docs/Test (a Perforce-tracked,
# read-only path -- writing there crashed the run, and a per-run artifact does
# not belong in version control).
$outFile = Join-Path $Repo "test-output-cross\${Arch}_cross_results.md"
New-Item -ItemType Directory -Force (Split-Path $outFile) | Out-Null

# Archive the previous run before overwriting it. This file is the only
# per-test record a lane produces, and a before-and-after needs both halves:
# comparing totals across two runs whose populations differ is arithmetic, not
# attribution. CL 9566 had to spend a second 34 minute baseline run to
# recreate a file that had existed and been overwritten.
# The stamp is the archived file's OWN last-write time, so the name records
# when that run happened rather than when it was filed away.
$archivedTo = $null
if (Test-Path $outFile) {
    $histDir = Join-Path $Repo "test-output-cross\history"
    New-Item -ItemType Directory -Force $histDir | Out-Null
    $stamp = (Get-Item $outFile).LastWriteTime.ToString('yyyyMMdd-HHmmss')
    $archivedTo = Join-Path $histDir "${Arch}_${stamp}.md"
    Move-Item -Force $outFile $archivedTo
}

[System.IO.File]::WriteAllText($outFile, $md.ToString(), [System.Text.UTF8Encoding]::new($false))

Write-Host "`n=== $($Arch.ToUpper()) Cross Battery Complete ==="
Write-Host "  PASS_EXPECTED:     $passCount"
Write-Host "  PASS_COMPILE_ONLY: $compileOnlyCount"
Write-Host "  PASS_REFUSED:      $refusedCount"
Write-Host "  FAIL:              $failCount"
Write-Host "  SKIPPED:           $skipCount"
Write-Host "  Total time:        ${totalMin} min"
Write-Host "  Results:           $outFile"
if ($archivedTo) { Write-Host "  Previous run kept: $archivedTo" }
