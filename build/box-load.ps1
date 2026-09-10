# box-load.ps1 -- sustained, memory-bounded load on the box, for arms that can
# only be read under contention (diag-arm.ps1's b3-record and b3-clockstuck).
# Design: docs/Designs/Active/Tools/BatteryReorg.md, step 12.
#
# The envelope is a FLOOR on free memory, not a count of guests: every wave
# measures free memory and derives its slot count from it, so the subject's
# guests are never the ones squeezed. Below the floor a wave is SKIPPED and
# counted, never run anyway.
#
# The load unit is a COMPILE guest, chosen because it carries its own verdict,
# so a generator that has stopped doing work says so instead of merely
# continuing to run. What one costs depends entirely on the subject and is
# MEASURED per wave, never assumed: field-range-proven costs about 0.17 GiB a
# slot and apps/foreword-all-compile about 0.88 (measured 2026-09-09).
#
# DUTY is the point of the summary and it is graded: the fraction of wall clock
# with at least one guest in flight. A rehearsal beside a run whose duty was
# 0.4 did not meet the load its verdict assumes, and this script exits non-zero
# rather than let that pass for a load test (L-FASTER).
#
#   build/box-load.ps1 -Minutes 50 -StopFile build-output\stop-load
#   build/box-load.ps1 -Minutes 1 -MaxSlots 2        # a cheap self-check
#
# It takes no token and it is not a gate.
[CmdletBinding()]
param(
    [double]$Minutes = 10,
    [double]$FloorGiB = 1.5,
    [double]$PerSlotGiB = 0.35,
    [int]$MaxSlots = 8,
    [double]$MinDuty = 0.8,
    [string]$StopFile = '',
    [string]$Src = 'codex\test\field-range-proven.codex',
    [string]$Kernel = 'seed\Codex.cdx',
    [string]$WorkDir = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compile = Join-Path $PSScriptRoot 'compile.ps1'
if (-not $WorkDir) { $WorkDir = Join-Path $repo 'build-output\box-load' }
if (-not (Test-Path -PathType Container $WorkDir)) { New-Item -ItemType Directory -Force $WorkDir | Out-Null }

$srcPath = if ([System.IO.Path]::IsPathRooted($Src)) { $Src } else { Join-Path $repo $Src }
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $repo $Kernel }
foreach ($f in @($compile, $srcPath, $kernelPath)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "box-load: missing $f"; exit 2 }
}

function Get-FreeGiB { [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2) }

$kdigest = (Get-FileHash $kernelPath -Algorithm SHA256).Hash.Substring(0, 16)
Write-Host "box-load: kernel $kernelPath [$kdigest]"
Write-Host ("box-load: floor {0} GiB, {1} GiB per slot, at most {2} slots, {3} minutes, duty floor {4}" -f $FloorGiB, $PerSlotGiB, $MaxSlots, $Minutes, $MinDuty)

# PerSlotGiB is a REQUEST until a wave has run; what a slot COSTS is measured
# (L-REQUEST). The first wave is capped at two slots and is the learning one:
# after it, and after every wave since, the envelope is derived from the
# largest per-slot cost actually observed, never from the parameter alone. A
# subject four times heavier than the default guess would otherwise be granted
# four times too many slots, which is how a floor stated in a parameter gets
# crossed while the run reports that it was obeying one.
$obsPerSlot = $PerSlotGiB
$lastSlots = 0
$learnCap = [math]::Min(2, $MaxSlots)

$deadline = (Get-Date).AddMinutes($Minutes)
$startedAt = Get-Date
$waves = 0
$started = 0
$failed = 0
$stalls = 0
$busyMs = 0.0
$minFree = Get-FreeGiB
$peakSlots = 0
$stopped = ''

while ((Get-Date) -lt $deadline) {
    if ($StopFile -and (Test-Path $StopFile)) { $stopped = 'stop file'; break }

    $free = Get-FreeGiB
    if ($free -lt $minFree) { $minFree = $free }
    # The learned figure is priced 25% high and the slot count grows by at most
    # one a wave, because the estimate is refined only AFTER a wave has run: on
    # the measured subject an unmargined estimate opened three slots on its
    # second wave and took free memory to 1.26 GiB against a 1.5 GiB floor.
    $slots = [int][math]::Floor(($free - $FloorGiB) / ($obsPerSlot * 1.25))
    if ($slots -gt $MaxSlots) { $slots = $MaxSlots }
    if (($waves -lt 1) -and ($slots -gt $learnCap)) { $slots = $learnCap }
    if (($waves -gt 0) -and ($slots -gt ($lastSlots + 1))) { $slots = $lastSlots + 1 }
    if ($slots -lt 1) {
        $stalls++
        Write-Host ("  stall: free {0} GiB leaves no slot above the {1} GiB floor at {2} GiB per slot" -f $free, $FloorGiB, $obsPerSlot)
        Start-Sleep -Seconds 5
        continue
    }
    if ($slots -gt $peakSlots) { $peakSlots = $slots }

    $waveStart = Get-Date
    $waveFloorSeen = $free
    $procs = @()
    for ($i = 0; $i -lt $slots; $i++) {
        $out = Join-Path $WorkDir "slot$i.cdx"
        $log = Join-Path $WorkDir "slot$i.log"
        $a = @('-NoProfile', '-File', $compile, '-Src', $srcPath, '-Out', $out, '-Log', $log, '-Kernel', $kernelPath)
        $procs += (Start-Process pwsh -ArgumentList $a -WorkingDirectory $repo -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $WorkDir "slot$i.out") -RedirectStandardError (Join-Path $WorkDir "slot$i.err"))
        $started++
    }
    while ($procs | Where-Object { -not $_.HasExited }) {
        Start-Sleep -Milliseconds 500
        $f = Get-FreeGiB
        if ($f -lt $minFree) { $minFree = $f }
        if ($f -lt $waveFloorSeen) { $waveFloorSeen = $f }
        if ((Get-Date) -gt $deadline.AddMinutes(5)) { break }
    }
    $seen = [math]::Round((($free - $waveFloorSeen) / $slots), 2)
    if ($seen -gt $obsPerSlot) {
        Write-Host ("  measured: a slot of this subject costs {0} GiB, not {1}; the envelope follows the measurement" -f $seen, $obsPerSlot)
        $obsPerSlot = $seen
    }
    foreach ($p in $procs) {
        if (-not $p.HasExited) { try { $p.Kill() } catch {} ; $failed++ ; continue }
        if ($p.ExitCode -ne 0) { $failed++ }
    }
    $lastSlots = $slots
    $busyMs += ((Get-Date) - $waveStart).TotalMilliseconds
    $waves++
    Write-Host ("  wave {0}: {1} slot(s), free {2} GiB at launch, {3:N1}s, failures so far {4}" -f $waves, $slots, $free, ((Get-Date) - $waveStart).TotalSeconds, $failed)
}
if (-not $stopped) { $stopped = 'duration' }

$elapsedMs = ((Get-Date) - $startedAt).TotalMilliseconds
$duty = if ($elapsedMs -gt 0) { [math]::Round($busyMs / $elapsedMs, 3) } else { 0 }

Write-Host ''
Write-Host '=== box-load summary ==='
Write-Host ("  stopped by      : {0}" -f $stopped)
Write-Host ("  elapsed         : {0:N1} s" -f ($elapsedMs / 1000))
Write-Host ("  waves           : {0}, peak {1} slot(s)" -f $waves, $peakSlots)
Write-Host ("  guests started  : {0}, failed {1}" -f $started, $failed)
Write-Host ("  stalls          : {0}" -f $stalls)
Write-Host ("  per slot        : {0} GiB measured, {1} requested" -f $obsPerSlot, $PerSlotGiB)
Write-Host ("  min free memory : {0} GiB (floor {1})" -f $minFree, $FloorGiB)
Write-Host ("  duty            : {0} (floor {1})" -f $duty, $MinDuty)

$bad = @()
if ($waves -lt 1) { $bad += 'no wave ever started' }
if ($failed -gt 0) { $bad += "$failed compile(s) failed" }
if ($duty -lt $MinDuty) { $bad += "duty $duty is below the $MinDuty floor" }
if (($waves -gt 0) -and ($minFree -lt $FloorGiB)) { $bad += "free memory reached $minFree GiB, below the $FloorGiB GiB floor this run promised" }
if ($bad.Count -gt 0) {
    Write-Host ''
    Write-Host ("box-load: FAIL -- " + ($bad -join '; ')) -ForegroundColor Red
    Write-Host '  A run that did not load the box is not a load test: the arm beside it proved nothing.'
    exit 1
}
Write-Host ''
Write-Host ("box-load: OK -- {0} guest(s) over {1:N1} s at duty {2}" -f $started, ($elapsedMs / 1000), $duty) -ForegroundColor Green
exit 0
