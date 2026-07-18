# Cross-architecture test battery with parallel Renode execution.
# Supports ARM64 and RISC-V via -Arch parameter.
[CmdletBinding()]
param(
    [ValidateSet('arm64','riscv64')]
    [string]$Arch = 'arm64',
    [int]$Jobs = 4,
    [int]$RenoTimeout = 1,
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
$allTests = Get-ChildItem "$testDir\*.codex" | Sort-Object Name
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
    if ($skipReason) { $skipCount++; Write-Host "SKIP $name ($skipReason)"; continue }
    $eligible.Add(@{ File = $tf; Name = $name; Dir = $dir })
}

Write-Host "`n=== $($Arch.ToUpper()) Cross Battery: $($eligible.Count) eligible, $skipCount skipped, $Jobs parallel slots ==="
$batteryStart = Get-Date

# ---- Phase 1: Compile all tests (sequential — each needs seed VM + plug VM) ----
Write-Host "`n--- Phase 1: Compile ---"
$compiled = [System.Collections.Concurrent.ConcurrentDictionary[string,hashtable]]::new()
$compileStart = Get-Date

for ($idx = 0; $idx -lt $eligible.Count; $idx++) {
    $t = $eligible[$idx]
    $name = $t.Name
    Write-Host -NoNewline "[$($idx+1)/$($eligible.Count)] compile $name ... "
    $testOutDir = Join-Path $outRoot $name
    New-Item -ItemType Directory -Force $testOutDir | Out-Null
    $elfOut = Join-Path $testOutDir "$name.elf"
    $compileLog = Join-Path $testOutDir 'compile.log'

    $cs = Get-Date
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-File',$compileScript,'-Src',($t.File).FullName,'-Out',$elfOut) -NoNewWindow -PassThru -RedirectStandardOutput $compileLog -RedirectStandardError (Join-Path $testOutDir 'compile.err')
    $finished = $proc.WaitForExit(120000)
    if (-not $finished) { try { $proc.Kill() } catch {} }
    $compileExit = if ($finished) { $proc.ExitCode } else { 99 }
    $ErrorActionPreference = $prev
    $ce = Get-Date
    $ct = [math]::Round(($ce - $cs).TotalSeconds, 1)

    if ($compileExit -ne 0 -or -not (Test-Path $elfOut)) {
        $reason = if ($compileExit -eq 99) { "timeout (120s)" } else { "exit=$compileExit" }
        Write-Host "FAIL (${ct}s, $reason)"
        [void]$compiled.TryAdd($name, @{ Status = 'FAIL_COMPILE'; CompileTime = $ct; RunTime = $null; Reason = $reason; HasExpected = $false })
    } else {
        $hasExp = Test-Path -PathType Leaf (Join-Path ($t.Dir) "$name.expected")
        Write-Host "OK (${ct}s)"
        [void]$compiled.TryAdd($name, @{ Status = 'COMPILED'; CompileTime = $ct; RunTime = $null; Reason = $null; HasExpected = $hasExp; ElfPath = $elfOut; Dir = $t.Dir })
    }
}
$compileEnd = Get-Date
Write-Host "Compile phase: $([math]::Round(($compileEnd - $compileStart).TotalSeconds, 1))s"

# ---- Phase 2: Run (parallel) ----
$emulatorLabel = if ($UseQemu) { "QEMU" } else { "Renode" }
$qemuTimeoutMs = 3000
$runJobs = if ($UseQemu) { [Math]::Max($Jobs, 8) } else { $Jobs }
Write-Host "`n--- Phase 2: Run via $emulatorLabel (${runJobs} parallel slots) ---"
$toRun = [System.Collections.Generic.List[hashtable]]::new()
foreach ($kv in $compiled.GetEnumerator()) {
    $v = $kv.Value
    if ($v.Status -eq 'COMPILED' -and $v.HasExpected) { $toRun.Add(@{ Name = $kv.Key; Elf = $v.ElfPath; Dir = $v.Dir }) }
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
$runResults = $toRun | ForEach-Object -ThrottleLimit $runJobs -Parallel {
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

    if ($useQ) {
        $qemuExe = $using:qemuExe
        $loadAddr = $using:loadAddr
        $arch = $using:Arch
        $timeoutMs = $using:qemuTimeoutMs
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
                @('-M','virt','-cpu','cortex-a53','-m','256M','-display','none','-monitor','none','-bios','none',
                  '-device',"loader,file=$binFile,addr=$loadAddr",'-serial',"file:$uartLogWin")
            }
            $proc = Start-Process -FilePath $qemuExe -ArgumentList $machArgs -PassThru -NoNewWindow
            $proc.WaitForExit($timeoutMs) | Out-Null
            if (-not $proc.HasExited) { try { $proc.Kill() } catch {} }
        }
    } else {
        $renodeExe = $using:renodeExe
        $boardPath = $using:boardPath
        $timeoutSec = $using:RenoTimeout
        $elfPath = (Resolve-Path ($t.Elf)).Path -replace '\\','/'
        $uartLog = $uartLogWin -replace '\\','/'
        $rescContent = @(
            'mach create "codex"'
            "machine LoadPlatformDescription @$boardPath"
            "sysbus LoadELF @$elfPath"
            "uart0 CreateFileBackend @$uartLog true"
            'start'
            "sleep $timeoutSec"
            'quit'
        ) -join "`n"
        $rescFile = Join-Path $testOutDir 'run.resc'
        [System.IO.File]::WriteAllText($rescFile, $rescContent)
        $rescPath = ($rescFile -replace '\\','/')
        & $renodeExe --disable-xwt --console -e "include @$rescPath" 2>&1 | Out-Null
        Start-Sleep -Milliseconds 200
    }

    $re = Get-Date
    $rt = [math]::Round(($re - $rs).TotalSeconds, 1)

    if ($status -ne 'FAIL_RUNTIME') {
        Start-Sleep -Milliseconds 50
        if (-not (Test-Path $uartLogWin)) {
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

            if ($expectedText -eq $actual) {
                $status = 'PASS_EXPECTED'
            } else {
                $status = 'FAIL_OUTPUT'
                $expLines = $expectedText -split "`n"
                $actLines = $actual -split "`n"
                $maxL = [Math]::Max($expLines.Count, $actLines.Count)
                for ($i = 0; $i -lt [Math]::Min($maxL, 3); $i++) {
                    $e = if ($i -lt $expLines.Count) { $expLines[$i] } else { '(missing)' }
                    $a2 = if ($i -lt $actLines.Count) { $actLines[$i] } else { '(missing)' }
                    if ($e -ne $a2) { $reason = "line $($i+1): exp=[$e] act=[$a2]"; break }
                }
            }
        }
    }
    @{ Name = $name; Status = $status; RunTime = $rt; Reason = $reason }
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
$passCount = 0; $failCount = 0; $compileOnlyCount = 0
foreach ($kv in $compiled.GetEnumerator()) {
    $v = $kv.Value
    if ($v.Status -eq 'PASS_EXPECTED') { $passCount++ }
    elseif ($v.Status -eq 'PASS_COMPILE_ONLY') { $compileOnlyCount++ }
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
[void]$md.AppendLine("**Total time**: ${totalMin} min (${totalSec}s)")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Summary")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Status | Count |")
[void]$md.AppendLine("|--------|------:|")
[void]$md.AppendLine("| PASS_EXPECTED | $passCount |")
[void]$md.AppendLine("| PASS_COMPILE_ONLY | $compileOnlyCount |")
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
[System.IO.File]::WriteAllText($outFile, $md.ToString(), [System.Text.UTF8Encoding]::new($false))

Write-Host "`n=== $($Arch.ToUpper()) Cross Battery Complete ==="
Write-Host "  PASS_EXPECTED:     $passCount"
Write-Host "  PASS_COMPILE_ONLY: $compileOnlyCount"
Write-Host "  FAIL:              $failCount"
Write-Host "  SKIPPED:           $skipCount"
Write-Host "  Total time:        ${totalMin} min (was 27.7 min)"
Write-Host "  Results:           $outFile"
