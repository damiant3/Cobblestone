# a64-emit-corpus.ps1 -- emit every cross-eligible subject to ARM64 and hash it.
#
# WHAT A ROW HERE DOES NOT MEAN. A row is the SHA-256 of an emitted ELF and
# nothing else. It does not say the subject runs, that the binary is correct,
# or that any particular code path was reached. Its only use is a DIFF of two
# passes taken on ONE tree: a subject whose hash is identical under two
# compilers did not have its emission decided by whatever differs between
# them.
#
# WHY IT EXISTS. COMPILER-72: the ARM64 backend resolves record field offsets
# through a hand-written name-to-index table that nothing at runtime observes.
# The question this harness answers is whether the table's FALLBACK is reached
# at all: emit the corpus with the table intact, make the fallback answer -1,
# rebuild the plug, emit again. An identical hash means the fallback decided
# nothing for that subject.
#
# BOTH SIDES MUST BE TAKEN ON ONE TREE (L-SAMEVER). A baseline banked before a
# CL that changes any record's field list will differ from a later pass for
# that reason rather than for the fallback, which is the exact false positive
# this experiment exists to avoid. Re-take the baseline rather than reusing a
# stale one; the harness is cheap.
#
# IT STARTS NOTHING. There is no Renode phase and no guest. The two earlier
# attempts at this measurement were killed by the box during a Renode phase
# the experiment never needed, which is why this does not use
# test-cross-batch.ps1.
#
# IT IS RESUMABLE. Every row is appended the moment it is measured, and a
# subject already in the output file is skipped, so a killed run is continued
# by re-invoking with the same -Out. Expect to do that: this box dips below
# the launch bar on other lanes' work, and a long background run is killed as
# collateral rather than for anything this harness did. Two kills at 85 and
# 108 of 559 cost only the subject in flight.
#
# PUT THE BANK SOMEWHERE THAT OUTLIVES THE SESSION, by convention
# build-output/a64-emit-baseline.txt and build-output/a64-emit-sabotage.txt.
# A session-scoped scratchpad is not that: the first version of this harness
# was written into one, was never committed, and was gone by the next
# session, which is why the measurement has been restarted three times.

[CmdletBinding()]
param(
    # Output file. Rows are "<name> <sha256>" or "<name> FAIL". Appended to.
    [Parameter(Mandatory=$true)]
    [string]$Out,
    # Restrict to subjects whose name matches, for a smoke run.
    [string]$Filter = '',
    # Print the selection and the denominator, measure nothing.
    [switch]$ListOnly,
    # Memory each compile guest ASKS FOR. compile-arm64.ps1 defaults to 3072,
    # and that default is what kills a corpus run on this box: 559 serial
    # guests each requesting 3 GiB. Measured 2026-09-09: aesgcm256 compiles at
    # 1024 and FAILS at 512 and 384, and net-recv-heap, the largest subject in
    # the corpus at 31,055 bytes, compiles at 1024. Both give a BYTE-IDENTICAL
    # hash at 1024 and at the larger request, which is the control that says
    # lowering this does not change what is emitted.
    #
    # Keep one value for a whole pass. A baseline taken at one request and a
    # sabotage pass at another would differ for that reason if the control
    # above ever stopped holding, and the diff could not tell the two apart.
    [int]$MemMB = 1024
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testDir = Join-Path $Repo 'codex\test'
$compileScript = Join-Path $Repo 'codex\plugs\arm64\compile-arm64.ps1'

if (-not (Test-Path $compileScript)) { throw "missing $compileScript" }

# --- selector, copied verbatim from test-cross-batch.ps1 -------------------
# Asking the directory instead would put skipped, fatal and machine-sidecar
# subjects in the denominator (L-DENOM). This block must stay a copy of that
# runner's selection, so that the corpus measured here is the corpus that
# runner would route.
$allTests = Get-ChildItem "$testDir\*.codex", "$testDir\ops\*.codex" | Sort-Object Name
$eligible = [System.Collections.Generic.List[object]]::new()
$skipCount = 0

foreach ($tf in $allTests) {
    $name = $tf.BaseName
    $dir = $tf.DirectoryName
    if ($Filter -and $name -notlike "*$Filter*") { continue }
    $skipReason = $null
    if (Test-Path "$dir\$name.qemudev") { $skipReason = "QEMU device" }
    elseif (Test-Path "$dir\$name.skip")    { $skipReason = (Get-Content -TotalCount 1 "$dir\$name.skip") }
    elseif (Test-Path "$dir\$name.slow")    { $skipReason = "slow" }
    elseif (Test-Path "$dir\$name.fatal")   { $skipReason = "fatal" }
    elseif (Test-Path "$dir\$name.failing") { $skipReason = "error test" }
    elseif (Test-Path "$dir\$name.smp")     { $skipReason = "multi-core" }
    elseif (Test-Path "$dir\$name.disk")    { $skipReason = "block device" }
    elseif (Test-Path "$dir\$name.no-cross") { $skipReason = "no-cross" }
    else {
        foreach ($mc in 'disk2','disk-src','vmargs','keys') {
            if (Test-Path "$dir\$name.$mc") { $skipReason = "machine sidecar (.$mc)"; break }
        }
    }
    if ($skipReason) { $skipCount++; continue }
    $eligible.Add(@{ File = $tf; Name = $name })
}

Write-Host ("=== a64 emit corpus: {0} selected from, {1} eligible, {2} skipped, {3} MB per compile guest ===" -f $allTests.Count, $eligible.Count, $skipCount, $MemMB)

if ($ListOnly) { $eligible | ForEach-Object { $_.Name }; exit 0 }

# --- resume ----------------------------------------------------------------
$done = @{}
if (Test-Path $Out) {
    foreach ($line in [IO.File]::ReadAllLines($Out)) {
        if ($line -match '^\s*(\S+)\s') { $done[$Matches[1]] = $true }
    }
    Write-Host ("resuming: {0} rows already in {1}" -f $done.Count, $Out)
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("a64emit-" + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $work | Out-Null

$measured = 0
$failed = 0
$start = Get-Date

try {
    foreach ($e in $eligible) {
        if ($done.ContainsKey($e.Name)) { continue }
        $elf = Join-Path $work ($e.Name + '.elf')
        $log = Join-Path $work ($e.Name + '.compile.log')
        $row = $null
        try {
            & $compileScript -Src $e.File.FullName -Out $elf -WorkDir $work -MemMB $MemMB *> $log
            if ((Test-Path $elf) -and (Get-Item $elf).Length -gt 0) {
                $row = "{0} {1}" -f $e.Name, (Get-FileHash -Algorithm SHA256 $elf).Hash
            } else {
                $row = "{0} FAIL" -f $e.Name
                $failed++
            }
        } catch {
            $row = "{0} FAIL" -f $e.Name
            $failed++
        }
        # Appended immediately: a killed run keeps every row it had measured.
        Add-Content -Path $Out -Value $row
        Remove-Item -Force -ErrorAction SilentlyContinue $elf, $log
        $measured++
        if (($measured % 25) -eq 0) {
            $el = (Get-Date) - $start
            Write-Host ("  {0} measured, {1} failed, {2:n0}s elapsed" -f $measured, $failed, $el.TotalSeconds)
        }
    }
} finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $work
}

$el = (Get-Date) - $start
Write-Host ("=== done: {0} measured this run, {1} failed, {2:n0}s ===" -f $measured, $failed, $el.TotalSeconds)
Write-Host ("total rows in {0}: {1}" -f $Out, (@([IO.File]::ReadAllLines($Out))).Count)
