# disk-runner-arm.ps1 -- the arm for apps/works/DiskTestRunner (WORKS-63).
#
# The runner mounts the boot volume, lists the root and COMPILES each entry
# alone, with no cite resolution, printing a verdict per entry and a failure
# count. The app sweep compiled the runner and nothing ran it.
#
# THE FIXTURE IS BUILT HERE, NOT CHECKED IN (root's ruling, 2026-09-09: no
# 8 MB image per arm in the depot; the depot holds the recipe). Everything this
# script writes lands in build-output/disk-runner-arm/, which is untracked.
#
#   build/disk-runner-arm.ps1
#   build/disk-runner-arm.ps1 -Compiler <a kernel>   # grade a compiler change
#
# TWO ARMS AND THE SECOND IS THE CONTROL. With the volume attached the runner
# must list it and read the subject back; with no disk at all it must REFUSE.
# One arm alone cannot tell a runner that reads a volume from one that prints
# a plausible line whatever it finds.
[CmdletBinding()]
param(
    [string]$Compiler = '',
    [string]$Sample = 'codex\test\field-range-proven.codex'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Compiler) { $Compiler = Join-Path $repo 'seed\Codex.cdx' }
$work = Join-Path $repo 'build-output\disk-runner-arm'
if (-not (Test-Path -PathType Container $work)) { New-Item -ItemType Directory -Force $work | Out-Null }

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$testRun = Join-Path $PSScriptRoot 'test-run.ps1'
$samplePath = if ([System.IO.Path]::IsPathRooted($Sample)) { $Sample } else { Join-Path $repo $Sample }
foreach ($f in @($compile, $testRun, $samplePath, $Compiler)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "disk-runner-arm: missing $f"; exit 2 }
}
Write-Host ("disk-runner-arm: compiler {0} [{1}]" -f $Compiler, (Get-FileHash $Compiler -Algorithm SHA256).Hash.Substring(0, 16))

function Invoke-Step {
    # NOT $Args: a parameter of that name binds nothing, PowerShell's automatic
    # $args shadows it, and the splat then starts an interactive pwsh.
    param([string]$What, [string[]]$ArgList, [string]$Produces)
    & pwsh @ArgList
    if (($LASTEXITCODE -ne 0) -or ($Produces -and -not (Test-Path -PathType Leaf $Produces))) {
        Write-Host "FAIL: $What" -ForegroundColor Red
        exit 3
    }
}

# -- the fixture: a FAT16 volume carrying the sample as SOURCE.SRC --
$sampleCdx = Join-Path $work 'sample.cdx'
$samplePe  = Join-Path $work 'sample.efi'
$image     = Join-Path $work 'fixture.img'
Write-Host 'disk-runner-arm: building the fixture...'
Invoke-Step 'sample compile' @('-NoProfile', '-File', $compile, '-Src', $samplePath, '-Out', $sampleCdx, '-Log', (Join-Path $work 'sample.log'), '-Kernel', $Compiler) $sampleCdx
Invoke-Step 'PE plug' @('-NoProfile', '-File', (Join-Path $repo 'codex\plugs\pe\run.ps1'), '-CdxInput', $sampleCdx, '-Out', $samplePe) $samplePe
Invoke-Step 'IMG plug' @('-NoProfile', '-File', (Join-Path $repo 'codex\plugs\img\run.ps1'), '-PeInput', $samplePe, '-CdxInput', $sampleCdx, '-Out', $image, '-Fat16', '-Source', $samplePath) $image
Write-Host ("  fixture: {0:N0} bytes" -f (Get-Item $image).Length)

# -- the runner itself --
$runnerCdx = Join-Path $work 'disktestrunner.cdx'
Write-Host 'disk-runner-arm: compiling the runner...'
Invoke-Step 'runner compile' @('-NoProfile', '-File', $compile, '-Src', (Join-Path $repo 'apps\works\DiskTestRunner.codex'), '-Out', $runnerCdx, '-Log', (Join-Path $work 'runner.log'), '-Kernel', $Compiler) $runnerCdx

function Get-Run {
    param([string]$Name, [string[]]$Extra)
    $out = Join-Path $work "$Name.actual"
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    $a = @('-NoProfile', '-File', $testRun, '-Kernel', $runnerCdx, '-OutFile', $out) + $Extra
    & pwsh @a *> $null
    if (-not (Test-Path -PathType Leaf $out)) { return '' }
    (Get-Content -Raw $out)
}

$fails = @()

Write-Host 'disk-runner-arm: arm 1, the volume is attached...'
$withDisk = Get-Run 'with-disk' @('-DiskFile', $image)
Write-Host ($withDisk.TrimEnd() -split "`r?`n" | ForEach-Object { "  $_" }) -Separator "`n"
$sampleBytes = (Get-Item $sampleCdx).Length
foreach ($claim in @('volume    : bps=512 ok=yes', 'summary   : failed=0', '=== runner done ===')) {
    if ($withDisk -notlike "*$claim*") { $fails += "arm 1 is missing: $claim" }
}
if ($withDisk -notmatch 'entries\s+:\s+(\d+) on the root') { $fails += 'arm 1 printed no entry count' }
elseif ([int]$Matches[1] -le 0) { $fails += 'arm 1 listed 0 entries on the root' }
# The subject was compiled ON THE VOLUME iff the guest reports the byte count the
# host's own compile of the same chapter produced. A count alone is a weaker claim.
if ($withDisk -notlike "*ok=yes cdx=$sampleBytes*") {
    $fails += "arm 1 did not compile the subject to $sampleBytes bytes, which is what the host compile produced"
}

Write-Host 'disk-runner-arm: arm 2, the control, no disk at all...'
$noDisk = Get-Run 'no-disk' @()
Write-Host ($noDisk.TrimEnd() -split "`r?`n" | Select-Object -First 4 | ForEach-Object { "  $_" }) -Separator "`n"
if ($noDisk -notlike '*REFUSED*') { $fails += 'the control did not REFUSE with no volume attached' }
if ($noDisk -notlike '*volume    : bps=0 ok=no*') { $fails += 'the control did not report an unusable volume' }
if ($noDisk -match 'entries\s+:\s+\d+ on the root') { $fails += 'the control listed a volume that is not there' }
if ($noDisk -like '*ok=yes cdx=*') { $fails += 'the control compiled something with no volume attached' }

Write-Host ''
if ($fails.Count -gt 0) {
    foreach ($f in $fails) { Write-Host "FAIL: $f" -ForegroundColor Red }
    exit 1
}
Write-Host 'disk-runner-arm: PASS -- the runner reads the volume, and refuses without one' -ForegroundColor Green
exit 0
