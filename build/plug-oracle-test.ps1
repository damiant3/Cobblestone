# plug-oracle-test.ps1 -- run a plug's OUTPUT, not just its exit code.
#
# Nothing in the tree executed a transpiler plug's emitted source. The IR
# wire is checked (`check-plug-types.ps1`) and the two NATIVE plugs are
# executed by the cross-arch battery, but for the ~45 language plugs the
# only thing ever asserted was that the plug produced bytes. An arm that
# emits syntactically valid and semantically wrong code was invisible.
#
# That is not hypothetical. Auditing the integer arms once (2026-07-22)
# found six plugs lowering `/` to a FLOORED quotient while the compiler
# truncates, and seven emitting a remainder that was floored or Euclidean
# where Codex means truncating. `-7 / 2` answered -4 through those plugs
# and -3 on x86-64. Every one compiled fine. Every one passed everything
# the tree could ask.
#
# This harness asks the only question that finds that class: compile ONE
# subject two ways, RUN BOTH, and require the answers to agree.
#
#     codex/test/plug-oracle-arith.codex
#         -> compile.ps1 (CDX)    -> codex-vm  -> the truth
#         -> compile.ps1 (-IrCce) -> the plug  -> that language's runtime
#
# x86-64 is the reference because it is the fixed point: the compiler is
# a fixed point of itself there, so its arithmetic is the definition.
#
# Usage:
#   build/plug-oracle-test.ps1                 # every wired plug
#   build/plug-oracle-test.ps1 -Only python
#   build/plug-oracle-test.ps1 -KeepArtifacts  # keep emitted source + output
#
# A plug is wired here only when BOTH its CDX and a runtime for its
# language exist on the box. Anything else is reported as SKIPPED with the
# reason, never as a pass -- a harness that silently covers nothing is the
# failure it exists to prevent.
[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Kernel = '',
    [switch]$KeepArtifacts,
    [int]$TimeoutSec = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Subject = Join-Path $Repo 'codex\test\plug-oracle-arith.codex'
$Work    = Join-Path $Repo 'build-output\plug-oracle'
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

# Each entry: the plug CDX, and how to run what it emits. `Ext` is only
# cosmetic; `Runner` is the executable and `RunArgs` the argv it takes.
$Plugs = @(
    @{ Name = 'python'
       Cdx  = 'codex\plugs\python\build-output\python-plug.cdx'
       Ext  = 'py'
       Exe  = 'python'
       Args = { param($f) @($f) } }
    @{ Name = 'javascript'
       Cdx  = 'codex\plugs\javascript\build-output\javascript-plug.cdx'
       Ext  = 'js'
       Exe  = 'node'
       Args = { param($f) @($f) } }
)

if (-not (Test-Path $Subject)) { Write-Host "MISSING subject: $Subject"; exit 2 }
if (-not (Test-Path $Kernel))  { Write-Host "MISSING kernel: $Kernel";   exit 2 }
New-Item -ItemType Directory -Force $Work | Out-Null

# ---------------------------------------------------------------------------
# The truth: the subject on x86-64.
# ---------------------------------------------------------------------------
$truthCdx = Join-Path $Work 'subject.cdx'
$truthOut = Join-Path $Work 'subject.x86.out'
$truthLog = Join-Path $Work 'subject.compile.log'

Write-Host "[oracle] compiling the subject for x86-64..."
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $truthCdx -Log $truthLog -Kernel $Kernel | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: subject did not compile to CDX; see $truthLog"; exit 3 }

& (Join-Path $Repo 'tools\codex-vm.exe') -kernel $truthCdx -headless -output $truthOut 2>&1 | Out-Null
$truth = @(Get-Content $truthOut -ErrorAction SilentlyContinue | Where-Object { $_.Trim().Length -gt 0 })
if ($truth.Count -eq 0) { Write-Host "FAIL: the subject produced no output on x86-64"; exit 3 }
Write-Host "[oracle] truth: $($truth.Count) values from x86-64"

# The subject must exercise negative operands, or every rounding rule on
# earth passes it. Guard the harness against its own subject being
# weakened later.
if (-not ($truth -match '^-')) {
    Write-Host "FAIL: no negative results in the truth set -- the subject cannot discriminate"
    exit 3
}

# ---------------------------------------------------------------------------
# The IR the plugs receive. CCE, because the wire is CCE.
# ---------------------------------------------------------------------------
$irFile = Join-Path $Work 'subject.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $irFile -Log (Join-Path $Work 'subject.ir.log') -Kernel $Kernel -IrCce | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: subject did not compile to IR"; exit 3 }

# ---------------------------------------------------------------------------
# Each plug.
# ---------------------------------------------------------------------------
$pass = 0; $fail = 0; $skip = 0
foreach ($p in $Plugs) {
    if ($Only -and $p.Name -ne $Only) { continue }

    $cdx = Join-Path $Repo $p.Cdx
    if (-not (Test-Path $cdx)) {
        Write-Host "  $($p.Name): SKIPPED -- no plug binary at $($p.Cdx) (build it with codex/plugs/$($p.Name)/build.ps1)"
        $skip++; continue
    }
    $exe = Get-Command $p.Exe -ErrorAction SilentlyContinue
    if (-not $exe) {
        Write-Host "  $($p.Name): SKIPPED -- '$($p.Exe)' is not on PATH, so its output cannot be run"
        $skip++; continue
    }

    $srcOut = Join-Path $Work "subject.$($p.Ext)"
    & pwsh -NoProfile -File (Join-Path $Repo 'build\run-plug.ps1') -Plug $cdx -InFile $irFile -Output $srcOut -TimeoutSec $TimeoutSec | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $srcOut) -or (Get-Item $srcOut).Length -eq 0) {
        Write-Host "  $($p.Name): FAIL -- the plug emitted nothing"
        $fail++; continue
    }

    $runOut = & $exe.Source @(& $p.Args $srcOut) 2>&1
    $got = @($runOut | ForEach-Object { "$_" } | Where-Object { $_.Trim().Length -gt 0 })

    if ($got.Count -eq 0) {
        Write-Host "  $($p.Name): FAIL -- the emitted program produced no output"
        $fail++; continue
    }

    $diff = Compare-Object $truth $got -SyncWindow 0
    if ($diff) {
        Write-Host "  $($p.Name): FAIL -- $($diff.Count) line(s) differ from x86-64"
        for ($i = 0; $i -lt [Math]::Max($truth.Count, $got.Count); $i++) {
            $t = if ($i -lt $truth.Count) { $truth[$i] } else { '<none>' }
            $g = if ($i -lt $got.Count)   { $got[$i] }   else { '<none>' }
            if ($t -ne $g) { Write-Host "      line $($i + 1): x86-64 $t, $($p.Name) $g" }
        }
        $fail++
    } else {
        Write-Host "  $($p.Name): PASS -- $($got.Count) values match x86-64"
        $pass++
    }
}

if (-not $KeepArtifacts) {
    Remove-Item $truthCdx, $truthOut, $truthLog -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "plug-oracle: $pass passed, $fail failed, $skip skipped"
if ($fail -gt 0) { exit 1 }
if ($pass -eq 0) { Write-Host "plug-oracle: nothing was actually checked"; exit 1 }
exit 0
