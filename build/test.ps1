# test.ps1 -- Compiler acceptance test harness -- batch mode.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$CodexCdx,
    [int]$Jobs = 8,
    [switch]$ErrorsOnly,
    [switch]$NoErrors,
    [switch]$Apps,
    [switch]$FW,
    [switch]$All,
    [switch]$Lib,
    [switch]$Slow,
    [switch]$Fatal,
    [string[]]$Tier = @(),
    [string]$ApprovedBy,
    [switch]$AllowStaleKernel
)

# Phase 1: batch-compile all non-skipped tests through persistent VM
#           instances (one per job slot, repl-loop reuse).
# Phase 2: run compiled tests that have .expected files (individual VM
#           per test, parallel).
# 
# Usage:
#   build/test.ps1 [-CodexCdx FP] [-Jobs N] [-ErrorsOnly | -NoErrors]
#   build/test.ps1 -Tier lang          # codex\test + codex\test\ops + codex\test\errors (the default)
#   build/test.ps1 -Tier lib           # codex\test\lib
#   build/test.ps1 -Tier fw            # codex\test\forewords
#   build/test.ps1 -Tier apps          # codex\test\apps
#   build/test.ps1 -Tier hardware      # machine-sidecar tests from every dir
#   build/test.ps1 -Tier traps         # the safety set: bounds, trap and fault
#                                      # demos that assert the guest DIES. A
#                                      # filter over .fatal from every dir, and
#                                      # it implies -Fatal
#   build/test.ps1 -Tier slow          # a filter over .slow; implies -Slow
#                                      # (.smp/.vmargs/.disk/.disk2/.keys)
#   build/test.ps1 -Tier oracles       # oracle-scalar/-vector/-cce, the
#                                      # author-owned differential collections
#   build/test.ps1 -Tier all           # every tier above INCLUDING oracles
#   build/test.ps1 -Tier lib,apps      # tiers combine
#   build/test.ps1 -All                # lang+lib+fw+apps+oracles
#   build/test.ps1 -All -Slow          # include slow tests too
#   build/test.ps1 -Fatal              # include fatal tests (GPF/exception demos)
# 
# Legacy switches (-Apps/-FW/-Lib/-All) map onto tiers. -Fuzz was removed
# 2026-07-27: codex\test\fuzz never existed. Every run writes
# test-output\_results\_rollup.txt and a delta against the previous run
# (test-output\last-run.json): newly red, fixed, added, removed.
# 
# Every run requires -ApprovedBy damian. The battery is run by Damian or
# with his per-run go-ahead, never on an agent's initiative -- see
# CLAUDE.md rule 1. The gate below is deliberate; do not work around it.
# 
# Sidecars (all optional, presence-driven):
#   codex*.test\foo.expected  -- compile must SUCCEED, runtime output must match
#   codex*.test\foo.failing   -- compile must FAIL with listed CDX error codes.
#                               A line may also be `CDX2031@33:5`, which pins
#                               the reported line:column as well as the code.
#                               Use it whenever the test exists BECAUSE of a
#                               position: a bare code cannot fail on a lost
#                               one, since a 0,0 span prints no prefix at all.
#   codex*.test\foo.diag      -- compile must SUCCEED and emit each listed CDX
#                               code at any severity (warning/info/error). One
#                               code per line; bare number or CDX-prefixed. Use
#                               with .expected to also check runtime output.
#   codex*.test\foo.disk-src  -- first line names ANOTHER test; that test's
#                               freshly compiled CDX is attached as this
#                               test's disk. A .disk names a file and is
#                               therefore frozen, which is no use to a test
#                               that pins what the CURRENT compiler emits.
#   codex*.test\foo.skip      -- skipped entirely (first line = reason)
#   codex*.test\foo.slow      -- skipped unless -Slow is passed (first line = reason)
#   codex*.test\foo.fatal     -- skipped unless -Fatal is passed (kills VM at runtime)
# 
# Exit status: 0 iff every sample ends in its expected bucket.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ApprovedBy -ne 'damian') {
    Write-Host ''
    Write-Host 'REFUSED: the full battery runs only with explicit human approval.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Agent: this is not part of your verification loop, and passing the'
    Write-Host 'approval flag without Damian saying so in THIS session is a lie.'
    Write-Host 'Verify your change with:'
    Write-Host '    build/build.ps1                                  # the standing gate'
    Write-Host '    build/compile.ps1 -Src <t> -Out <o> -Log <log>   # one test'
    Write-Host ''
    Write-Host 'If you believe this change warrants a battery run, tell Damian why'
    Write-Host 'in one sentence and stop. He runs it, or hands you the command.'
    Write-Host ''
    exit 1
}

Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
$Scope = if ($ErrorsOnly) { 'errors' } elseif ($NoErrors) { 'positive' } else { 'both' }
$OutRoot     = 'test-output'
$ResultsDir  = Join-Path $OutRoot '_results'
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
if (Test-Path $ResultsDir) { Remove-Item -Recurse -Force $ResultsDir }
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

$TestLog = Join-Path $OutRoot 'test.log'
Set-Content -Path $TestLog -Value '' -Encoding UTF8
$env:CODEX_SWEEP_LOG = (Resolve-Path $TestLog).Path


# ===========================================================================
# Tiers. Named subsets so -All stops being the only word for "more". The
# legacy switches map onto them; -ErrorsOnly/-NoErrors still narrow the lang
# tier. `hardware` is a FILTER over every directory (tests that carry a
# machine sidecar), not a directory of its own. `oracles` invokes the
# author-owned differential collections (reek's oracle-scalar, blu's
# oracle-vector) and reports their verdicts. Damian's call 2026-07-27: the
# pin goes wherever it helps, so the oracles run in -All and in the gate
# (build.ps1) as well -- measured at 2s + 1s, they cost nothing worth
# counting. The collections stay author-owned.
# 
# `codex\test\ops` is the operator-correctness-by-operand-type axis, part
# of the lang tier: pins for operators that were right on the operand types
# somebody tested and wrong on one nobody did (the Real-comparison class),
# organized by axis rather than by feature, plus the axis smoke bundles.
# ===========================================================================
$tiers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($t in $Tier) { [void]$tiers.Add($t.Trim()) }
if ($Apps) { [void]$tiers.Add('apps') }
if ($FW)   { [void]$tiers.Add('fw') }
if ($Lib)  { [void]$tiers.Add('lib') }
if ($All)  { foreach ($t in 'lang', 'lib', 'fw', 'apps', 'oracles') { [void]$tiers.Add($t) } }
if ($tiers.Contains('all')) {
    foreach ($t in 'lang', 'lib', 'fw', 'apps', 'oracles') { [void]$tiers.Add($t) }
    [void]$tiers.Remove('all')
}
$known = @('lang', 'lib', 'fw', 'apps', 'hardware', 'oracles', 'traps', 'slow')
foreach ($t in $tiers) {
    if ($t -notin $known) { Write-Host "unknown tier '$t' (known: $($known -join ', '))" -ForegroundColor Red; exit 1 }
}

# A tier that selects tests and then skips them is worse than no tier: it
# reports coverage it did not run. `traps` and `slow` therefore IMPLY their
# switch, so `-Tier traps` cannot land in the pre-filter and be filed SKIPPED.
$runFatal = $Fatal.IsPresent -or $tiers.Contains('traps')
$runSlow  = $Slow.IsPresent  -or $tiers.Contains('slow')
# A bare invocation runs the language tier: the battery's subject is the
# compiler. An explicit tier list is taken at its word -- `-Tier oracles`
# alone runs the collections and no tests.
if ($tiers.Count -eq 0) { [void]$tiers.Add('lang') }

$machineSidecars = @('.smp', '.vmargs', '.disk', '.disk2', '.keys')
function Test-MachineSidecar {
    param([string]$Src)
    $dir = [System.IO.Path]::GetDirectoryName($Src)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Src)
    foreach ($ext in $script:machineSidecars) {
        if (Test-Path -PathType Leaf (Join-Path $dir "$name$ext")) { return $true }
    }
    return $false
}

$allDirs = @('codex\test', 'codex\test\ops', 'codex\test\errors', 'codex\test\apps', 'codex\test\forewords', 'codex\test\lib')
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$tests = [System.Collections.Generic.List[string]]::new()
$tierDirs = [System.Collections.Generic.List[string]]::new()
if ($tiers.Contains('lang')) {
    if ($Scope -ne 'errors')   { $tierDirs.Add('codex\test'); $tierDirs.Add('codex\test\ops') }
    if ($Scope -ne 'positive') { $tierDirs.Add('codex\test\errors') }
}
if ($tiers.Contains('apps')) { $tierDirs.Add('codex\test\apps') }
if ($tiers.Contains('fw'))   { $tierDirs.Add('codex\test\forewords') }
if ($tiers.Contains('lib'))  { $tierDirs.Add('codex\test\lib') }
foreach ($d in $tierDirs) {
    Get-ChildItem -Path "$d\*.codex" -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($seen.Add($_.FullName)) { $tests.Add($_.FullName) }
    }
}
if ($tiers.Contains('hardware')) {
    foreach ($d in $allDirs) {
        Get-ChildItem -Path "$d\*.codex" -File -ErrorAction SilentlyContinue | ForEach-Object {
            if ((Test-MachineSidecar $_.FullName) -and $seen.Add($_.FullName)) { $tests.Add($_.FullName) }
        }
    }
}
# `traps` and `slow` are FILTERS over every directory, like `hardware`, not
# directories of their own: these tests live among their neighbours and are
# identified by a sidecar. `traps` is the safety set -- the bounds, trap and
# fault demos that assert the guest DIES.
foreach ($pair in @(@{ Tier = 'traps'; Ext = '.fatal' }, @{ Tier = 'slow'; Ext = '.slow' })) {
    if (-not $tiers.Contains($pair.Tier)) { continue }
    foreach ($d in $allDirs) {
        Get-ChildItem -Path "$d\*.codex" -File -ErrorAction SilentlyContinue | ForEach-Object {
            $sc = Join-Path $_.DirectoryName ([System.IO.Path]::GetFileNameWithoutExtension($_.Name) + $pair.Ext)
            if ((Test-Path -PathType Leaf $sc) -and $seen.Add($_.FullName)) { $tests.Add($_.FullName) }
        }
    }
}


# --- Which compiler is on trial, and is it the one you think? ---------------
# 
# The battery compiles every sample with build-output\bare-metal\Codex.cdx.
# Nothing used to state which compiler that was, or check it: the digest was
# computed at the END and written to the rollup as stage0=..., compared
# against nothing. On 2026-08-03 a release battery ran a whole tree against a
# kernel left behind by an earlier -CodexCdx run -- a compiler six days and
# two seeds stale -- and reported 11 failures that were all the antique
# compiler missing features the tests use. The run cost 12 minutes and nearly
# blocked a release on defects that did not exist.
# 
# So: name the kernel up front, and refuse to run one whose provenance is
# unknown. Legitimate kernels are the depot seed (a fresh workspace) or the
# last build's signed output (the normal edit-build-test loop). Anything else
# is a leftover, and -CodexCdx is how you ask for one on purpose.
$stage0 = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
$kernelBackup = "$stage0.prebattery"
$script:RestoreKernel = $false

function Get-Digest([string]$p) {
    if ($p -and (Test-Path $p)) { return (Get-FileHash -Algorithm SHA256 $p).Hash }
    return $null
}

$seedDigest = Get-Digest 'seed\Codex.cdx'
$sutDigest  = Get-Digest 'build\output\Sut.cdx'

if ($CodexCdx) {
    if (-not (Test-Path $CodexCdx)) { Write-Host "REFUSED: -CodexCdx '$CodexCdx' does not exist."; exit 2 }
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    # Put the working kernel back afterwards. This script used to leave the
    # substitute installed, which is how the stale kernel above was born.
    if (Test-Path $stage0) { Copy-Item -Force $stage0 $kernelBackup; $script:RestoreKernel = $true }
    Copy-Item -Force $CodexCdx $stage0
    Write-Host "battery compiler: EXPLICIT -CodexCdx $CodexCdx"
    Write-Host "                  $((Get-Digest $stage0).Substring(0,16))  $((Get-Item $stage0).Length) bytes"
    if ($script:RestoreKernel) { Write-Host "                  (working kernel saved; restored when the run ends)" }
} else {
    if (-not (Test-Path $stage0)) {
        if (-not $seedDigest) { Write-Host "REFUSED: no battery kernel and no seed\Codex.cdx to install from. Run build\build.ps1."; exit 2 }
        New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
        Copy-Item -Force 'seed\Codex.cdx' $stage0
        Write-Host "battery compiler: installed from seed (no kernel present)"
    }
    $kDigest = Get-Digest $stage0
    $origin = if ($kDigest -eq $sutDigest) { 'last build (build\output\Sut.cdx)' }
              elseif ($kDigest -eq $seedDigest) { 'depot seed (seed\Codex.cdx)' }
              else { $null }
    if (-not $origin) {
        Write-Host ''
        Write-Host '=== REFUSED: the battery kernel has unknown provenance ==='
        Write-Host "  kernel : $($kDigest.Substring(0,16))  $((Get-Item $stage0).Length) bytes  $((Get-Item $stage0).LastWriteTime)"
        Write-Host "  seed   : $(if ($seedDigest) { $seedDigest.Substring(0,16) } else { '<missing>' })"
        Write-Host "  Sut    : $(if ($sutDigest)  { $sutDigest.Substring(0,16)  } else { '<none -- no build in this workspace>' })"
        Write-Host ''
        Write-Host '  It matches neither the depot seed nor the last build, so it is a'
        Write-Host '  leftover -- usually a compiler an earlier -CodexCdx run installed.'
        Write-Host '  A battery against it tests that compiler, not this tree, and its'
        Write-Host '  failures are fiction.'
        Write-Host ''
        Write-Host '  Fix (pick one):'
        Write-Host '    build\build.ps1                     # build this tree, then re-run'
        Write-Host '    Copy-Item -Force seed\Codex.cdx build-output\bare-metal\Codex.cdx'
        Write-Host '    build\test.ps1 -CodexCdx <path> ... # if you meant to test another compiler'
        Write-Host '    build\test.ps1 -AllowStaleKernel ... # you know exactly what this is'
        Write-Host ''
        if (-not $AllowStaleKernel) { exit 2 }
        Write-Host '  -AllowStaleKernel given: proceeding anyway.'
    } else {
        Write-Host "battery compiler: $($kDigest.Substring(0,16))  $((Get-Item $stage0).Length) bytes  <- $origin"
    }
}

function Restore-Kernel {
    if ($script:RestoreKernel -and (Test-Path $kernelBackup)) {
        Copy-Item -Force $kernelBackup $stage0
        Remove-Item -Force $kernelBackup
        Write-Host "battery compiler: working kernel restored ($((Get-Digest $stage0).Substring(0,16)))"
    }
}

# ===========================================================================
# Pre-filter: handle skips, partition into compile lists
# ===========================================================================
$toCompile = [System.Collections.Generic.List[string]]::new()
$srcDirOf = @{}
$catOf = @{}
foreach ($src in $tests) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    $dir  = [System.IO.Path]::GetDirectoryName($src)
    $srcDirOf[$name] = $dir
    $leaf = Split-Path -Leaf $dir
    $catOf[$name] = if ($leaf -eq 'test') { 'root' } else { $leaf }
    $skipFile = Join-Path $dir "$name.skip"
    $slowFile = Join-Path $dir "$name.slow"
    if (Test-Path -PathType Leaf $skipFile) {
        $reason = (Get-Content -TotalCount 1 $skipFile)
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t$reason" | Set-Content -Path $resultFile -Encoding UTF8
    } elseif (-not $runSlow -and (Test-Path -PathType Leaf $slowFile)) {
        $reason = (Get-Content -TotalCount 1 $slowFile)
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t(slow) $reason" | Set-Content -Path $resultFile -Encoding UTF8
    } elseif (-not $runFatal -and (Test-Path -PathType Leaf (Join-Path $dir "$name.fatal"))) {
        $reason = (Get-Content -TotalCount 1 (Join-Path $dir "$name.fatal"))
        $resultFile = Join-Path $ResultsDir $name
        "SKIPPED`t$name`t(fatal) $reason" | Set-Content -Path $resultFile -Encoding UTF8
    } else {
        $toCompile.Add($src)
    }
}

# Sort lightest-first: tests with fewer cites compile smaller source concats
# and consume less heap, so the batch VM gets through more tests before
# exhausting the 3GB arena.
$toCompile = @($toCompile | Sort-Object {
    $m = Select-String -Path $_ -Pattern '^\s*cites' -ErrorAction SilentlyContinue
    if ($m) { @($m).Count } else { 0 }
})

Write-Host "Tests: $($tests.Count) total, $($tests.Count - $toCompile.Count) skipped, $($toCompile.Count) to compile ($Jobs batch slots)"


# ===========================================================================
# Phase 1: Batch compile -- one VM per job slot
# ===========================================================================
$batchDir = Join-Path $OutRoot '_batches'
if (Test-Path $batchDir) { Remove-Item -Recurse -Force $batchDir }
New-Item -ItemType Directory -Force -Path $batchDir | Out-Null

$listFiles = @()
$writers = @()
for ($j = 0; $j -lt $Jobs; $j++) {
    $lf = Join-Path $batchDir "batch-$j.txt"
    $listFiles += $lf
    $writers += [System.IO.StreamWriter]::new($lf, $false, [System.Text.UTF8Encoding]::new($false))
}
for ($i = 0; $i -lt $toCompile.Count; $i++) {
    $writers[$i % $Jobs].WriteLine($toCompile[$i])
}
foreach ($w in $writers) { $w.Close() }

$compileScript = Join-Path $PSScriptRoot 'test-compile-batch.ps1'
$phase1Sw = [System.Diagnostics.Stopwatch]::StartNew()
$compileProcs = @()
for ($j = 0; $j -lt $Jobs; $j++) {
    $lf = $listFiles[$j]
    if ((Get-Item $lf).Length -eq 0) { continue }
    $pcore = ($j + 1) % 8
    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', $compileScript,
        '-ListFile', $lf,
        '-OutRoot', $OutRoot,
        '-PCore', $pcore
    ) -PassThru -WindowStyle Hidden
    $compileProcs += $proc
    Write-Host "  batch $j started (pid $($proc.Id), pcore $pcore)"
}

Write-Host "Waiting for $($compileProcs.Count) compile batches..."
foreach ($proc in $compileProcs) {
    $proc.WaitForExit(1800000) | Out-Null
    if (-not $proc.HasExited) {
        Write-Host "  batch pid $($proc.Id) timed out -- killing" -ForegroundColor Yellow
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
}
$phase1Sw.Stop()
Write-Host "Phase 1 (compile) complete in $([int]($phase1Sw.ElapsedMilliseconds/1000))s."
$retrySw = [System.Diagnostics.Stopwatch]::StartNew()

# ===========================================================================
# Phase 1a: Contain batch-VM crashes.
# 
# Exit 99 means the VM died before this test ran. A death also poisons the
# results BEFORE it in the same session: the batch REPL carries state across
# compiles, and a corrupted session reports phantom diagnostics (exit 7) or
# crashes (exit 4) the test does not produce standalone. See
# battery-triage-2026-07-23.
# 
# The old design answered any death by re-running EVERY non-clean result
# standalone and sequentially: 683 tests and 479 s on the 2026-07-27
# baseline. Now, per round: the test that killed the VM (exit 4 with a !EXC
# dump in its build.log) gets one standalone compile, which is
# authoritative -- it can overturn a false failure but never manufacture a
# pass, because a genuine failure fails standalone too. Every OTHER
# non-clean result (4, 7, 99) from a batch that suffered a death is
# re-batched into fresh batch VMs, where a clean session either reproduces
# the failure or clears it. Rounds repeat until a round has no death (its
# results are then real, the prior behaviour for clean runs); after the
# round cap, whatever is left falls back to standalone.
# ===========================================================================
$compileScript2 = Join-Path $PSScriptRoot 'compile.ps1'
$srcOf = @{}
foreach ($src in $toCompile) { $srcOf[[System.IO.Path]::GetFileNameWithoutExtension($src)] = $src }
$confirmed = @{}

function Get-BatchExit {
    param([string]$Name)
    $ef = Join-Path (Join-Path $OutRoot $Name) '.exitcode'
    if (Test-Path $ef) { (Get-Content -TotalCount 1 $ef).Trim() } else { '99' }
}

function Test-CrashDump {
    param([string]$Name)
    $log = Join-Path (Join-Path $OutRoot $Name) 'build.log'
    (Test-Path -PathType Leaf $log) -and (Select-String -Path $log -Pattern '!EXC' -SimpleMatch -Quiet)
}

function Invoke-StandaloneRetry {
    param([string]$Name)
    $src = $srcOf[$Name]
    $out = Join-Path $OutRoot $Name
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    # The retry has to compile the test the way the batch would have: the
    # .flags sidecar rides along, or every .flags test becomes a false
    # failure at once (foreword-all-compile without decks=150, prose-anchor
    # without prose).
    $flagsFile = Join-Path ([System.IO.Path]::GetDirectoryName($src)) "$Name.flags"
    $rawFlags = if (Test-Path -PathType Leaf $flagsFile) { (Get-Content -TotalCount 1 $flagsFile).Trim() } else { '' }
    $retryArgs = @('-NoProfile', '-File', $compileScript2, '-Src', $src,
        '-Out', (Join-Path $out "$Name.cdx"), '-Log', (Join-Path $out 'build.log'))
    if ($rawFlags) { $retryArgs += @('-RawFlags', $rawFlags) }
    $p = Start-Process -FilePath 'pwsh' -ArgumentList $retryArgs -PassThru -WindowStyle Hidden
    $p.WaitForExit(120000) | Out-Null
    if (-not $p.HasExited) { try { Stop-Process -Id $p.Id -Force } catch {} }
    $retryExit = if ($p.HasExited) { $p.ExitCode } else { 4 }
    "$retryExit" | Set-Content -Path (Join-Path $out '.exitcode') -Encoding UTF8
    Write-Host "  standalone $Name`: exit $retryExit"
}

# Splits a death-batch's survivors across fresh batch VMs and returns the
# new membership (array of name arrays) for the next round's death analysis.
function Invoke-RebatchRound {
    param([string[]]$Names, [int]$RoundNum)
    $slots = [Math]::Max(1, [Math]::Min($Jobs, [int][Math]::Ceiling($Names.Count / 20.0)))
    $lists = @(); $rbWriters = @(); $membership = @()
    for ($k = 0; $k -lt $slots; $k++) {
        $lf = Join-Path $batchDir "rebatch-$RoundNum-$k.txt"
        $lists += $lf
        $rbWriters += [System.IO.StreamWriter]::new($lf, $false, [System.Text.UTF8Encoding]::new($false))
        $membership += ,@()
    }
    for ($i = 0; $i -lt $Names.Count; $i++) {
        $k = $i % $slots
        $rbWriters[$k].WriteLine($srcOf[$Names[$i]])
        $membership[$k] += $Names[$i]
    }
    foreach ($w in $rbWriters) { $w.Close() }
    $procs = @()
    for ($k = 0; $k -lt $slots; $k++) {
        $procs += Start-Process -FilePath 'pwsh' -ArgumentList @(
            '-NoProfile', '-File', $compileScript,
            '-ListFile', $lists[$k], '-OutRoot', $OutRoot, '-PCore', (($k + 1) % 8)
        ) -PassThru -WindowStyle Hidden
    }
    foreach ($proc in $procs) {
        $proc.WaitForExit(1800000) | Out-Null
        if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {} }
    }
    return ,$membership
}

# Which batches suffered a death, and what inside them needs what.
function Get-DeathRetrySets {
    param([object[]]$Batches)
    $crashers = [System.Collections.Generic.List[string]]::new()
    $rebatch  = [System.Collections.Generic.List[string]]::new()
    foreach ($members in $Batches) {
        $dead = $false
        foreach ($m in $members) {
            $e = Get-BatchExit $m
            if ($e -eq '99' -or ($e -eq '4' -and (Test-CrashDump $m))) { $dead = $true; break }
        }
        if (-not $dead) { continue }
        foreach ($m in $members) {
            if ($confirmed.ContainsKey($m)) { continue }
            $e = Get-BatchExit $m
            if ($e -eq '4' -and (Test-CrashDump $m)) { $crashers.Add($m) }
            elseif ($e -in '4', '7', '99') { $rebatch.Add($m) }
        }
    }
    @{ Crashers = $crashers; Rebatch = $rebatch }
}

$batches = @()
foreach ($lf in $listFiles) {
    $members = @(Get-Content $lf | Where-Object { $_.Trim() -ne '' } |
        ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })
    if ($members.Count -gt 0) { $batches += ,$members }
}

$roundCap = 5
$round = 0
while ($true) {
    $sets = Get-DeathRetrySets -Batches $batches
    foreach ($name in $sets.Crashers) {
        if ($confirmed.ContainsKey($name)) { continue }
        Write-Host "  batch-killer $name -- confirming standalone"
        Invoke-StandaloneRetry $name
        $confirmed[$name] = $true
    }
    if ($sets.Rebatch.Count -eq 0) { break }
    $round++
    if ($round -gt $roundCap) {
        Write-Host "Phase 1a: round cap reached; $($sets.Rebatch.Count) tests fall back to standalone..."
        foreach ($name in $sets.Rebatch) { Invoke-StandaloneRetry $name }
        break
    }
    Write-Host "Phase 1a round ${round}: re-batching $($sets.Rebatch.Count) tests from death-batches..."
    $batches = Invoke-RebatchRound -Names $sets.Rebatch -RoundNum $round
}

# A deck overflow (CDX9002) reported from a batch session can be the
# session's cumulative arena state rather than the test's own demand:
# foreword-all-compile hit LOWER's deck floor in a shared VM (2026-07-27)
# and compiles clean standalone with the same flags and seed. Batch deck
# overflows are therefore always confirmed standalone -- they are rare,
# and a genuine one fails standalone too.
foreach ($src in $toCompile) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    if ($confirmed.ContainsKey($name)) { continue }
    $ef = Join-Path (Join-Path $OutRoot $name) '.exitcode'
    if ((Test-Path $ef) -and ((Get-Content -TotalCount 1 $ef).Trim() -eq '7')) {
        $log = Join-Path (Join-Path $OutRoot $name) 'build.log'
        if ((Test-Path -PathType Leaf $log) -and (Select-String -Path $log -Pattern 'CDX9002' -SimpleMatch -Quiet)) {
            Write-Host "  deck overflow in batch: $name -- confirming standalone"
            Invoke-StandaloneRetry $name
        }
    }
}

$retrySw.Stop()


# ===========================================================================
# Phase 1b: Classify compile results
# ===========================================================================
$needsRun = [System.Collections.Generic.List[hashtable]]::new()
foreach ($src in $toCompile) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($src)
    $dir  = [System.IO.Path]::GetDirectoryName($src)
    $out  = Join-Path $OutRoot $name
    $resultFile = Join-Path $ResultsDir $name
    $failingFile  = Join-Path $dir "$name.failing"
    $expectedFile = Join-Path $dir "$name.expected"
    $stdinFile    = Join-Path $dir "$name.stdin"
    # .keys holds a scancode timeline (`t:scancode` per line, t = ms since
    # boot). .stdin reaches the serial ring; this reaches the PS/2 key cell,
    # which is the only path a keyboard read can see.
    $keysFile     = Join-Path $dir "$name.keys"
    $diskFile     = Join-Path $dir "$name.disk"
    # .disk-src names another test whose FRESHLY COMPILED cdx becomes this
    # test's disk. A .disk is a file that was built at some other time by
    # some other compiler, which is exactly what a pin on the current
    # compiler's output cannot use: the manifest-scope pin sat skipped with a
    # shell script beside it for want of this one line.
    $diskSrcFile  = Join-Path $dir "$name.disk-src"
    # .disk2 is a SECOND image, attached as the primary channel's slave. A test
    # whose subject is which drive it reached needs two, and with one image
    # behind every position a working drive-select and a missing one produce
    # identical output -- which is how block-select came to do nothing for as
    # long as it did.
    $disk2File    = Join-Path $dir "$name.disk2"
    # .smp holds a core count: the test is booted with -smp N.
    $smpFile      = Join-Path $dir "$name.smp"
    # .vmargs holds extra codex-vm flags, for a test whose subject is the
    # machine rather than the program -- a bus topology, an absent device.
    $vmArgsFile   = Join-Path $dir "$name.vmargs"
    $log = Join-Path $out 'build.log'
    $bin = Join-Path $out "$name.cdx"
    $exitFile = Join-Path $out '.exitcode'

    $exitCode = if (Test-Path $exitFile) { (Get-Content -TotalCount 1 $exitFile).Trim() } else { '99' }
    $compileOk = ($exitCode -eq '0')

    if (Test-Path -PathType Leaf $failingFile) {
        if ($compileOk) {
            "FAIL_EXPECTED_BUT_COMPILED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            continue
        }
        $codesOk = $true
        $logText = if (Test-Path $log) { Get-Content -Raw -Path $log } else { '' }
        # A bare `CDX2031` checks only that the code fired. `CDX2031@33:5`
        # also pins WHERE, which a bare code cannot: a diagnostic reported at
        # a synthetic 0,0 span prints with no line:column prefix at all, so a
        # code-only check passes against a compiler that lost the position
        # entirely. That is what the act-block span defect looked
        # like, and it is why a test written for a position needs a form that
        # can fail on one.
        foreach ($code in (Get-Content $failingFile)) {
            $code = $code.Trim()
            if (-not $code) { continue }
            if ($code -match '^(?<c>(CDX)?\d+)@(?<l>\d+):(?<col>\d+)$') {
                $c = $matches['c'] -replace '^CDX', ''
                $pat = "\b$($matches['l']):$($matches['col']):\s*error (CDX)?0*$c\b"
                if ($logText -notmatch $pat) { $codesOk = $false; break }
            } elseif ($logText -notmatch "error (CDX)?0*$code\b") { $codesOk = $false; break }
        }
        if ($codesOk) {
            "PASS_FAILING`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        } else {
            "FAIL_WRONG_DIAGNOSTIC`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        }
        continue
    }

    if (-not $compileOk) {
        "FAIL_COMPILE`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        continue
    }

    $diagFile = Join-Path $dir "$name.diag"
    if (Test-Path -PathType Leaf $diagFile) {
        $codesOk = $true
        $missing = ''
        $logText = if (Test-Path $log) { Get-Content -Raw -Path $log } else { '' }
        foreach ($code in (Get-Content $diagFile)) {
            $code = ($code.Trim() -replace '^CDX','')
            if (-not $code) { continue }
            if ($logText -notmatch "(error|warning|info|hint|deprecated) (CDX)?0*$code\b") { $codesOk = $false; $missing = $code; break }
        }
        if (-not $codesOk) {
            "FAIL_MISSING_DIAGNOSTIC`t$name`tCDX$missing not emitted" | Set-Content -Path $resultFile -Encoding UTF8
            continue
        }
        if (-not (Test-Path -PathType Leaf $expectedFile)) {
            "PASS_DIAG`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            continue
        }
    }

    # A .fatal test asserts that the guest DIES, and it deliberately carries no
    # .expected: the !EXC dump's RIP moves with every codegen change, so a
    # byte-compare would be brittle. That is why this arm must come BEFORE the
    # no-expected arm below. Without it every one of them lands in
    # PASS_UNVERIFIED -- compiled, never run, and counted as a pass. Measured
    # 2026-07-28: all 15 were in that state, so -Fatal added 15 to the pass
    # column and executed none of them. A green that cannot fail reads as
    # coverage, which is strictly worse than the skip it replaced.
    $fatalFile = Join-Path $dir "$name.fatal"
    if ((Test-Path -PathType Leaf $fatalFile) -and $runFatal) {
        # An optional second line `exc: NN` pins WHICH fault. It matters: a
        # bounds check that fired traps with UD2 (06), while one that was
        # removed reads a wild pointer and page-faults, and "it died" alone
        # cannot tell those apart.
        $expectExc = ''
        foreach ($l in @(Get-Content $fatalFile)) {
            if ($l -match '^\s*exc:\s*([0-9A-Fa-f]{1,2})\s*$') { $expectExc = $matches[1].ToUpper(); break }
        }
        $needsRun.Add(@{
            Name = $name; Bin = $bin; Expected = ''; Fatal = $true; ExpectExc = $expectExc
            Stdin = $stdinFile; Keys = $keysFile; Disk = $diskFile; Smp = 0
            Disk2 = $disk2File; VmArgs = $vmArgsFile
        })
        continue
    }

    if (-not (Test-Path -PathType Leaf $expectedFile)) {
        "PASS_UNVERIFIED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
        continue
    }

    $smpCores = 0
    if (Test-Path -PathType Leaf $smpFile) {
        $smpCores = [int]((Get-Content -TotalCount 1 $smpFile).Trim())
    }

    if (Test-Path -PathType Leaf $diskSrcFile) {
        $peer = (Get-Content -TotalCount 1 $diskSrcFile).Trim()
        $peerBin = Join-Path (Join-Path $OutRoot $peer) "$peer.cdx"
        if (-not (Test-Path -PathType Leaf $peerBin)) {
            "FAIL_DISK_SOURCE`t$name`t$peer produced no cdx" | Set-Content -Path $resultFile -Encoding UTF8
            continue
        }
        $diskFile = $peerBin
    }

    $needsRun.Add(@{
        Name = $name; Bin = $bin; Expected = $expectedFile;
        Stdin = $stdinFile; Keys = $keysFile; Disk = $diskFile; Smp = $smpCores
        Disk2 = $disk2File
        VmArgs = $vmArgsFile
    })
}

# ===========================================================================
# Phase 2: Run tests with .expected files (individual VM per test)
# ===========================================================================
if ($needsRun.Count -gt 0) {
    $fatalCount = @($needsRun | Where-Object { $_.ContainsKey('Fatal') -and $_.Fatal }).Count
    $runLabel = "Phase 2: running $($needsRun.Count) tests"
    if ($fatalCount -gt 0) { $runLabel += " ($fatalCount judged on the fault, not on .expected)" }
    Write-Host "$runLabel, $Jobs parallel..."

    $coreQueue = [System.Collections.Concurrent.ConcurrentQueue[int]]::new()
    for ($i = 0; $i -lt $Jobs; $i++) { $coreQueue.Enqueue(($i + 1) % 8) }

    $runWorker = {
        $t = $_
        $name = $t.Name
        $bin  = $t.Bin
        $expectedFile = $t.Expected
        $stdinFile    = $t.Stdin
        $keysFile     = $t.Keys
        $diskFile     = $t.Disk
        $disk2File    = $t.Disk2
        $smpCores     = $t.Smp
        $vmArgsFile   = $t.VmArgs
        $out  = Join-Path $using:OutRoot $name
        $resultFile = Join-Path $using:ResultsDir $name
        $runScript  = Join-Path $using:PSScriptRoot 'test-run.ps1'
        $actual = Join-Path $out 'runtime.actual'

        # ANY UNCAUGHT THROW HERE ENDS THE WHOLE BATTERY, not one test.
        # WaitForExit returns before Windows releases a redirected handle,
        # so a bare read of runtime.actual can land on a locked file while
        # ErrorActionPreference is Stop. test-cross-batch.ps1 lost 457 tests
        # to exactly that and stranded five guests at 115,597 CPU-seconds,
        # because the harness that dies is also the thing enforcing every
        # child ceiling. Unreadable reads as empty, which fails toward a
        # reported failure rather than a silent pass.
        function Read-LogShared([string]$path) {
            for ($ri = 0; $ri -lt 5; $ri++) {
                try {
                    $fs = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                    try { $sr = [System.IO.StreamReader]::new($fs); return $sr.ReadToEnd() } finally { $fs.Dispose() }
                } catch { Start-Sleep -Milliseconds 100 }
            }
            return ''
        }

        $cq = $using:coreQueue
        $pcore = 1
        [void]$cq.TryDequeue([ref]$pcore)

        try {
            $runArgs = @('-NoProfile', '-File', $runScript, '-Kernel', $bin, '-OutFile', $actual, '-PCore', $pcore)
            if (Test-Path -PathType Leaf $stdinFile) { $runArgs += @('-StdinFile', $stdinFile) }
            if (Test-Path -PathType Leaf $keysFile)  { $runArgs += @('-KeysFile', $keysFile) }
            if (Test-Path -PathType Leaf $diskFile)  { $runArgs += @('-DiskFile', $diskFile) }
            if (Test-Path -PathType Leaf $disk2File) { $runArgs += @('-Disk2File', $disk2File) }
            if ($smpCores -gt 1)                     { $runArgs += @('-Smp', $smpCores) }
            if (Test-Path -PathType Leaf $vmArgsFile) { $runArgs += @('-VmArgsFile', $vmArgsFile) }
            $runSw = [System.Diagnostics.Stopwatch]::StartNew()
            & pwsh @runArgs
            $runSw.Stop()
            "$($runSw.ElapsedMilliseconds)" | Set-Content -Path (Join-Path $out '.run-ms') -Encoding UTF8
            # A fatal test is judged on the fault, not on a byte-compare.
            # ContainsKey rather than a property read: the other entries in
            # $needsRun do not carry this key.
            if ($t.ContainsKey('Fatal') -and $t.Fatal) {
                $txt = if (Test-Path $actual) { Read-LogShared $actual } else { '' }
                if ($txt -match '!EXC=([0-9A-Fa-f]{1,2})') {
                    $got = $matches[1].ToUpper()
                    if ($t.ExpectExc -and ($got -ne $t.ExpectExc)) {
                        "FAIL_FATAL_EXC`t$name`tfaulted with EXC=$got, expected EXC=$($t.ExpectExc)" | Set-Content -Path $resultFile -Encoding UTF8
                    } else {
                        "PASS_FATAL`t$name`tEXC=$got" | Set-Content -Path $resultFile -Encoding UTF8
                    }
                } elseif ($LASTEXITCODE -ne 0 -or $txt -eq '') {
                    # No output at all is not survival and must not be read as
                    # one: it is a hang, a wall-budget kill, or a VM that died
                    # without printing. Say so rather than guessing.
                    "FAIL_RUNTIME`t$name`tno output, so the fault could not be observed" | Set-Content -Path $resultFile -Encoding UTF8
                } else {
                    # THE CASE THIS TIER EXISTS FOR. The program ran to
                    # completion where it was supposed to die, which is what a
                    # bounds check that stopped firing looks like.
                    "FAIL_FATAL_SURVIVED`t$name`tran to completion without faulting" | Set-Content -Path $resultFile -Encoding UTF8
                }
                return
            }

            if ($LASTEXITCODE -ne 0) {
                "FAIL_RUNTIME`t$name`trun failed" | Set-Content -Path $resultFile -Encoding UTF8
                return
            }

            $expectedBytes = (Read-LogShared $expectedFile) -replace "`r",''
            $actualBytes   = if (Test-Path $actual) { Read-LogShared $actual } else { '' }
            if ($expectedBytes -eq $actualBytes) {
                "PASS_EXPECTED`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            } else {
                "FAIL_OUTPUT`t$name`t" | Set-Content -Path $resultFile -Encoding UTF8
            }
        } finally {
            $cq.Enqueue($pcore)
        }
    }

    $phase2Sw = [System.Diagnostics.Stopwatch]::StartNew()
    $needsRun | ForEach-Object -Parallel $runWorker -ThrottleLimit $Jobs
    $phase2Sw.Stop()
    Write-Host "Phase 2 (run) complete in $([int]($phase2Sw.ElapsedMilliseconds/1000))s."
} else {
    $phase2Sw = $null
    Write-Host "Phase 2 (run) complete."
}


# ===========================================================================
# Oracles tier: the author-owned differential collections, invoked and
# reported here, never gated. reek owns oracle-scalar, blu owns
# oracle-vector; this harness only runs them and carries their verdicts
# into the rollup and the exit code.
# ===========================================================================
$oracleResults = @()
if ($tiers.Contains('oracles')) {
    $stage0Path = Join-Path (Resolve-Path .).Path 'build-output\bare-metal\Codex.cdx'
    foreach ($o in 'oracle-scalar', 'oracle-vector', 'oracle-cce') {
        $oscript = Join-Path $PSScriptRoot "$o.ps1"
        if (-not (Test-Path -PathType Leaf $oscript)) {
            $oracleResults += [pscustomobject]@{ Name = $o; Ok = $false; Seconds = 0; Note = 'script missing' }
            continue
        }
        Write-Host "Oracle: $o..."
        $osw = [System.Diagnostics.Stopwatch]::StartNew()
        $olog = Join-Path $OutRoot "_oracle-$o.log"
        & pwsh -NoProfile -File $oscript -Kernel $stage0Path *> $olog
        $osw.Stop()
        $ok = ($LASTEXITCODE -eq 0)
        $tail = @(Get-Content $olog -Tail 1 -ErrorAction SilentlyContinue) -join ''
        $oracleResults += [pscustomobject]@{ Name = $o; Ok = $ok; Seconds = [int]($osw.ElapsedMilliseconds / 1000); Note = $tail }
        Write-Host "  $o : $(if ($ok) { 'PASS' } else { 'FAIL' }) ($([int]($osw.ElapsedMilliseconds / 1000))s)  $tail"
    }
}

# ===========================================================================
# Aggregate results
# ===========================================================================
$buckets = @{
    PASS_EXPECTED = @(); PASS_UNVERIFIED = @(); PASS_FAILING = @(); PASS_DIAG = @(); PASS_FATAL = @()
    SKIPPED = @(); FAIL_COMPILE = @(); FAIL_RUNTIME = @()
    FAIL_OUTPUT = @(); FAIL_EXPECTED_BUT_COMPILED = @(); FAIL_WRONG_DIAGNOSTIC = @(); FAIL_MISSING_DIAGNOSTIC = @()
    FAIL_DISK_SOURCE = @(); FAIL_FATAL_SURVIVED = @(); FAIL_FATAL_EXC = @()
}
$statusOf = @{}
foreach ($f in Get-ChildItem -File $ResultsDir) {
    if ($f.Name -like '_*') { continue }
    $line = Get-Content -TotalCount 1 $f.FullName
    if (-not $line) { continue }
    $parts = $line -split "`t", 3
    $status = $parts[0]; $name = $parts[1]; $detail = if ($parts.Count -ge 3) { $parts[2] } else { '' }
    $statusOf[$name] = $status
    if (-not $buckets.ContainsKey($status)) {
        Write-Host "unknown result status '$status' for $name" -ForegroundColor Yellow
        continue
    }
    if ($status -in 'SKIPPED','FAIL_RUNTIME','FAIL_DISK_SOURCE','PASS_FATAL','FAIL_FATAL_SURVIVED','FAIL_FATAL_EXC') {
        $buckets[$status] += "$name`: $detail"
    } else {
        $buckets[$status] += $name
    }
}

# Census instrumentation: one row per test with whatever timings exist.
# Compile time inside a batch is not observable per test (the VM output file
# flushes on exit), so src-bytes stands in as the compile-cost proxy; run-ms
# is exact. Batch wall times are in the sweep log (batch-done ... wallms=).
$timingsFile = Join-Path $ResultsDir '_timings.tsv'
$timingRows = [System.Collections.Generic.List[string]]::new()
$timingRows.Add("name`tstatus`tsrc_bytes`trun_ms")
foreach ($f in Get-ChildItem -File $ResultsDir) {
    if ($f.Name -like '_*') { continue }
    $line = Get-Content -TotalCount 1 $f.FullName
    if (-not $line) { continue }
    $p = $line -split "`t", 3
    $out = Join-Path $OutRoot $f.Name
    $sb = ''; $rm = ''
    $sbF = Join-Path $out '.src-bytes'; if (Test-Path $sbF) { $sb = (Get-Content -TotalCount 1 $sbF).Trim() }
    $rmF = Join-Path $out '.run-ms';    if (Test-Path $rmF) { $rm = (Get-Content -TotalCount 1 $rmF).Trim() }
    $timingRows.Add("$($f.Name)`t$($p[0])`t$sb`t$rm")
}
[System.IO.File]::WriteAllLines($timingsFile, $timingRows, [System.Text.UTF8Encoding]::new($false))
$phaseSummary = "phase1_compile_s=$([int]($phase1Sw.ElapsedMilliseconds/1000)) retry_s=$([int]($retrySw.ElapsedMilliseconds/1000))"
if ($null -ne $phase2Sw) { $phaseSummary += " phase2_run_s=$([int]($phase2Sw.ElapsedMilliseconds/1000))" }
Write-Host $phaseSummary
Add-Content -Path $timingsFile -Value "# $phaseSummary"

$total = ($buckets.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
$passed = $buckets.PASS_EXPECTED.Count + $buckets.PASS_FAILING.Count + $buckets.PASS_UNVERIFIED.Count `
        + $buckets.PASS_DIAG.Count + $buckets.PASS_FATAL.Count
$unexpected = $buckets.FAIL_COMPILE.Count + $buckets.FAIL_EXPECTED_BUT_COMPILED.Count `
            + $buckets.FAIL_WRONG_DIAGNOSTIC.Count + $buckets.FAIL_OUTPUT.Count + $buckets.FAIL_RUNTIME.Count `
            + $buckets.FAIL_MISSING_DIAGNOSTIC.Count + $buckets.FAIL_DISK_SOURCE.Count `
            + $buckets.FAIL_FATAL_SURVIVED.Count + $buckets.FAIL_FATAL_EXC.Count

Write-Host "total=$total  pass=$passed  fail=$unexpected  skip=$($buckets.SKIPPED.Count)"

# ===========================================================================
# Rollup and run-over-run delta. The rollup is one file a reader can act on
# without excavating 600 lines; the delta makes a regression read as a diff.
# test-output\last-run.json survives across runs (only _results is wiped).
# ===========================================================================
function Get-FailHint {
    param([string]$Name, [string]$Status)
    $out = Join-Path $OutRoot $Name
    if ($Status -eq 'FAIL_OUTPUT') {
        $dir = $srcDirOf[$Name]
        if (-not $dir) { return '' }
        # LENGTH BEFORE CONTENT (L-SHORT). A truncated artifact and a wrong one
        # are the same colour on a verdict line, and the line walk below reports
        # the first differing line either way -- which reads as codegen. Seven
        # poison subjects went red at Update 48 with ideas-test holding 204 bytes
        # of a 2,199-byte output and the compiler byte-identical; a re-run cleared
        # all seven. The normalisation is the SAME one the FAIL_OUTPUT verdict was
        # decided with (CR stripped from expected only), so this cannot disagree
        # with it -- comparing raw file bytes here would report SHORT for every
        # CRLF/LF pair and be worse than the gap it closes.
        # Read-LogShared is defined INSIDE $runWorker and is not in scope here,
        # so this reads the two files directly. Same text either way: the worker
        # only needs the sharing retry because it reads while other jobs write.
        #
        # A bare length difference does NOT prove truncation -- most ordinary
        # wrong-output reds differ in length too, so reporting every one of them
        # as SHORT would train the reader to ignore the word. A STRICT PREFIX
        # does prove it: that is what a capture cut off mid-stream looks like and
        # a miscompile has no reason to produce it. The two are reported as
        # different claims, and the same-length case says nothing at all and
        # falls through to the line walk untouched.
        $efile = Join-Path $dir "$Name.expected"
        $afile = Join-Path $out 'runtime.actual'
        $etxt = if (Test-Path -PathType Leaf $efile) { ([System.IO.File]::ReadAllText($efile)) -replace "`r", '' } else { '' }
        $atxt = if (Test-Path -PathType Leaf $afile) { [System.IO.File]::ReadAllText($afile) } else { '' }
        if ($etxt.Length -ne $atxt.Length) {
            $lim = [Math]::Min($etxt.Length, $atxt.Length)
            $off = 0
            while ($off -lt $lim -and $etxt[$off] -eq $atxt[$off]) { $off++ }
            if ($atxt.Length -lt $etxt.Length -and $off -eq $atxt.Length) {
                return "TRUNCATED: actual $($atxt.Length) chars is a strict PREFIX of the expected $($etxt.Length) -- the capture is SHORT, not wrong; re-run before reading this as codegen (L-SHORT)"
            }
            return "LENGTHS DIFFER: actual $($atxt.Length) chars, expected $($etxt.Length), first difference at offset $off -- not a prefix, so this is a content difference and the line below is the one to read (L-SHORT)"
        }
        $e = @(@(Get-Content (Join-Path $dir "$Name.expected") -ErrorAction SilentlyContinue) -replace "`r", '')
        $a = @(Get-Content (Join-Path $out 'runtime.actual') -ErrorAction SilentlyContinue)
        $n = [Math]::Max($e.Count, $a.Count)
        for ($i = 0; $i -lt $n; $i++) {
            $el = if ($i -lt $e.Count) { $e[$i] } else { '<absent>' }
            $al = if ($i -lt $a.Count) { $a[$i] } else { '<absent>' }
            if ($el -cne $al) { return "line $($i + 1): expected [$el] actual [$al]" }
        }
        return ''
    }
    if ($Status -in 'FAIL_COMPILE', 'FAIL_WRONG_DIAGNOSTIC', 'FAIL_EXPECTED_BUT_COMPILED', 'FAIL_MISSING_DIAGNOSTIC') {
        $log = Join-Path $out 'build.log'
        if (Test-Path -PathType Leaf $log) {
            $hit = Select-String -Path $log -Pattern 'error (CDX)?\d+' | Select-Object -First 1
            if ($hit) { return $hit.Line.Trim() }
        }
    }
    return ''
}

$seedStamp = if (Test-Path 'build-output\bare-metal\Codex.cdx') { (Get-FileHash -Algorithm SHA256 'build-output\bare-metal\Codex.cdx').Hash.Substring(0, 16) } else { 'unknown' }
$tierLabel = (@($tiers) | Sort-Object) -join ','
$runStamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

$prevFile = Join-Path $OutRoot 'last-run.json'
$prev = @{}
$prevStamp = ''
$prevTiers = ''
if (Test-Path -PathType Leaf $prevFile) {
    try {
        $pj = Get-Content $prevFile -Raw | ConvertFrom-Json
        $prevStamp = "$($pj.stamp)"
        $prevTiers = "$($pj.tiers)"
        foreach ($p in $pj.status.PSObject.Properties) { $prev[$p.Name] = "$($p.Value)" }
    } catch { $prev = @{} }
}

$newlyRed = [System.Collections.Generic.List[string]]::new()
$fixedNow = [System.Collections.Generic.List[string]]::new()
$addedRed = [System.Collections.Generic.List[string]]::new()
$addedCount = 0
foreach ($n in $statusOf.Keys) {
    $now = $statusOf[$n]
    if (-not $prev.ContainsKey($n)) {
        $addedCount++
        if ($now -like 'FAIL_*') { $addedRed.Add("$n ($now)") }
        continue
    }
    $was = $prev[$n]
    if ($now -like 'FAIL_*' -and $was -notlike 'FAIL_*') { $newlyRed.Add("$n ($was -> $now)") }
    elseif ($now -notlike 'FAIL_*' -and $was -like 'FAIL_*') { $fixedNow.Add("$n ($was -> $now)") }
}
# Removed means DELETED FROM THE TREE, not merely outside this run's tiers:
# a lib-only run must not report the whole apps tier as gone.
$existingNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($d in $allDirs) {
    Get-ChildItem -Path "$d\*.codex" -File -ErrorAction SilentlyContinue | ForEach-Object {
        [void]$existingNames.Add([System.IO.Path]::GetFileNameWithoutExtension($_.Name))
    }
}
$removed = [System.Collections.Generic.List[string]]::new()
foreach ($n in $prev.Keys) {
    if (-not $statusOf.ContainsKey($n) -and -not $existingNames.Contains($n)) { $removed.Add($n) }
}

$roll = [System.Collections.Generic.List[string]]::new()
$roll.Add("battery rollup  $runStamp  stage0=$seedStamp  tiers=$tierLabel")
$roll.Add("total=$total  pass=$passed  fail=$unexpected  skip=$($buckets.SKIPPED.Count)")
$roll.Add('')
$roll.Add("category      pass  fail  skip")
$catStats = @{}
foreach ($n in $statusOf.Keys) {
    $c = if ($catOf.ContainsKey($n)) { $catOf[$n] } else { '?' }
    if (-not $catStats.ContainsKey($c)) { $catStats[$c] = @{ pass = 0; fail = 0; skip = 0 } }
    $s = $statusOf[$n]
    if ($s -like 'FAIL_*') { $catStats[$c].fail++ }
    elseif ($s -eq 'SKIPPED') { $catStats[$c].skip++ }
    else { $catStats[$c].pass++ }
}
foreach ($c in ($catStats.Keys | Sort-Object)) {
    $roll.Add(("{0,-12}  {1,4}  {2,4}  {3,4}" -f $c, $catStats[$c].pass, $catStats[$c].fail, $catStats[$c].skip))
}
$roll.Add('')
$roll.Add('slowest 10 (run phase, ms):')
# Not $slow: that is the [switch]$Slow parameter, and PowerShell variable
# names are case-insensitive.
$slowRows = [System.Collections.Generic.List[object]]::new()
foreach ($n in $statusOf.Keys) {
    $rmF = Join-Path (Join-Path $OutRoot $n) '.run-ms'
    if (Test-Path -PathType Leaf $rmF) {
        $ms = 0
        if ([int]::TryParse((Get-Content -TotalCount 1 $rmF).Trim(), [ref]$ms)) { $slowRows.Add([pscustomobject]@{ Name = $n; Ms = $ms }) }
    }
}
foreach ($s in ($slowRows | Sort-Object -Property Ms -Descending | Select-Object -First 10)) {
    $roll.Add(("  {0,7}  {1}" -f $s.Ms, $s.Name))
}
$failNames = @($statusOf.Keys | Where-Object { $statusOf[$_] -like 'FAIL_*' } | Sort-Object)
if ($failNames.Count -gt 0) {
    $roll.Add('')
    $roll.Add('failures:')
    foreach ($n in $failNames) {
        $hint = Get-FailHint -Name $n -Status $statusOf[$n]
        $roll.Add("  $n  $($statusOf[$n])$(if ($hint) { "  -- $hint" })")
    }
}
foreach ($or in $oracleResults) {
    $roll.Add("oracle $($or.Name): $(if ($or.Ok) { 'PASS' } else { 'FAIL' }) ($($or.Seconds)s)  $($or.Note)")
}
$roll.Add('')
if ($prev.Count -gt 0) {
    $roll.Add("delta vs previous run ($prevStamp, tiers=$prevTiers):")
    $roll.Add("  newly red: $($newlyRed.Count)$(if ($newlyRed.Count) { '  ' + (($newlyRed | Select-Object -First 20) -join '; ') })")
    $roll.Add("  fixed:     $($fixedNow.Count)$(if ($fixedNow.Count) { '  ' + (($fixedNow | Select-Object -First 20) -join '; ') })")
    $roll.Add("  new tests: $addedCount$(if ($addedRed.Count) { '  RED AMONG THEM: ' + ($addedRed -join '; ') })")
    $roll.Add("  removed:   $($removed.Count)$(if ($removed.Count) { '  ' + (($removed | Select-Object -First 20) -join '; ') })")
} else {
    $roll.Add('delta: no previous run recorded (first rollup)')
}
[System.IO.File]::WriteAllLines((Join-Path $ResultsDir '_rollup.txt'), $roll, [System.Text.UTF8Encoding]::new($false))
foreach ($l in ($roll | Select-Object -Skip ($roll.Count - [Math]::Min(6, $roll.Count)))) { Write-Host $l }

# Persist this run for the next delta. A tier subset only re-records the
# tests it ran; statuses from other tiers carry forward untouched, and only
# a test deleted from the tree drops out of the carried state.
$carried = @{}
foreach ($k in $prev.Keys) { if ($existingNames.Contains($k)) { $carried[$k] = $prev[$k] } }
foreach ($k in $statusOf.Keys) { $carried[$k] = $statusOf[$k] }
@{ stamp = $runStamp; stage0 = $seedStamp; tiers = $tierLabel; status = $carried } |
    ConvertTo-Json -Depth 4 | Set-Content -Path $prevFile -Encoding UTF8

$oracleFails = @($oracleResults | Where-Object { -not $_.Ok }).Count
if ($unexpected -gt 0 -or $oracleFails -gt 0) {
    function Show-Failures { param([string]$Label, [object[]]$Items); if ($Items.Count -gt 0) { Write-Host "${Label}:"; foreach ($i in $Items) { Write-Host "  $i" } } }
    Show-Failures 'compile failed'              $buckets.FAIL_COMPILE
    Show-Failures 'expected error but compiled' $buckets.FAIL_EXPECTED_BUT_COMPILED
    Show-Failures 'wrong diagnostic'            $buckets.FAIL_WRONG_DIAGNOSTIC
    Show-Failures 'missing diagnostic'          $buckets.FAIL_MISSING_DIAGNOSTIC
    Show-Failures 'output mismatch'             $buckets.FAIL_OUTPUT
    Show-Failures 'runtime error'               $buckets.FAIL_RUNTIME
    Show-Failures 'disk source missing'         $buckets.FAIL_DISK_SOURCE
    Show-Failures 'oracle collections'          @($oracleResults | Where-Object { -not $_.Ok } | ForEach-Object { $_.Name })
    Restore-Kernel
    exit 1
}
Restore-Kernel
exit 0
