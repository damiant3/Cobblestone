# plug-selfcheck.ps1 -- the tier for plugs whose output can be checked WITHOUT
# a target runtime.
#
# WHY IT EXISTS. build/plug-oracle-test.ps1 is the only thing in the tree that
# runs a plug's OUTPUT, and it does it by EXECUTING the emitted source, so it
# can only reach a plug whose target toolchain is on this box: python,
# javascript, csharp, zig, wasm. Every binary and image emitter is outside it
# by construction -- there is no SPIR-V runtime here and never will be one
# just to grade a plug -- and those plugs had no runner at all. spirv is the
# instance that named the gap (plugs-backlog 1.24): test-spirv.ps1 and
# test-binary.ps1 ran only by hand, nothing in build/ named spirv, and
# ExaminersAssay.md did not mention it.
#
# WHY NOT IN build/build.ps1. Every entry here boots a VM, several of them
# twice, for plugs the seed does not depend on. The gate stays what it is.
# This tier is run by hand, before a release, or whenever a plug changes.
#
# WHAT BELONGS HERE. A check whose ASSERTION IS THE EMITTED ARTIFACT: a word
# stream that validates, a container with the right magic, an image with its
# tables written. Not an exit code -- the img plug printed OK and wrote 1,400
# bytes of a 16 MB image for as long as it had been broken (1.25), and every
# exit code in that chain was 0.
#
# Usage:
#   build/plug-selfcheck.ps1                 # build each plug, then check it
#   build/plug-selfcheck.ps1 -Only spirv
#   build/plug-selfcheck.ps1 -NoBuild        # use the plug binaries as they are
#   build/plug-selfcheck.ps1 -Kernel path    # default seed/Codex.cdx
[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Kernel = '',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) {
    [Console]::Error.WriteLine("MISSING kernel: $Kernel")
    exit 2
}

# Each entry names the plug it belongs to, the build script that produces the
# binary it checks, and the checks themselves. A check that takes -Kernel is
# given one: a probe compiled against whatever build.ps1 last left in
# build-output is measuring the wrong compiler, which is the trap CLAUDE.md
# names and which test-spirv.ps1 was falling into.
$Entries = @(
    @{ Name  = 'spirv'
       Build = @('codex\plugs\spirv\build.ps1', 'codex\plugs\spirv\build-bin.ps1')
       Checks = @(
         @{ Label = 'spirv-text';   Script = 'codex\plugs\spirv\test-spirv.ps1';  Kernel = $true  },
         @{ Label = 'spirv-binary'; Script = 'codex\plugs\spirv\test-binary.ps1'; Kernel = $false },
         @{ Label = 'spirv-emit';   Script = 'codex\plugs\spirv\test-emit.ps1';   Kernel = $false }
       ) }
    @{ Name  = 'img'
       Build = @('codex\plugs\img\build.ps1')
       Checks = @(
         @{ Label = 'img-image'; Script = 'codex\plugs\img\test-img.ps1'; Kernel = $true }
       ) }
)

$selected = if ($Only) { $Entries | Where-Object { $_.Name -eq $Only } } else { $Entries }
if ($Only -and -not $selected) {
    [Console]::Error.WriteLine("no such plug in this tier: $Only")
    [Console]::Error.WriteLine("known: " + (($Entries | ForEach-Object { $_.Name }) -join ', '))
    exit 2
}

$pass = 0; $fail = 0; $failed = @()
$sw = [Diagnostics.Stopwatch]::StartNew()

foreach ($e in $selected) {
    if (-not $NoBuild) {
        foreach ($b in $e.Build) {
            Write-Host "[selfcheck] building $b"
            & pwsh -NoProfile -File (Join-Path $Repo $b) | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  $($e.Name): BUILD FAILED ($b)"
                $fail++; $failed += "$($e.Name) build"
            }
        }
    }
    foreach ($c in $e.Checks) {
        $args = @('-NoProfile', '-File', (Join-Path $Repo $c.Script))
        if ($c.Kernel) { $args += @('-Kernel', $Kernel) }
        $out = & pwsh @args 2>&1
        $code = $LASTEXITCODE
        if ($code -eq 0) {
            Write-Host "  $($c.Label): PASS"
            $pass++
        } else {
            Write-Host "  $($c.Label): FAIL (exit $code)"
            $out | Select-Object -Last 6 | ForEach-Object { Write-Host "      $_" }
            $fail++; $failed += $c.Label
        }
    }
}

Write-Host ""
Write-Host ("plug-selfcheck: {0} passed, {1} failed  ({2:N0}s)" -f $pass, $fail, $sw.Elapsed.TotalSeconds)
if ($fail -gt 0) {
    Write-Host ("  failed: " + ($failed -join ', '))
    exit 1
}
exit 0
