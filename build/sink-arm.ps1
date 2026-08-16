# Calibrate the sink ladder: run every arm, including the ones that must FAIL.
#
# SinkLadderProbe drives fat16-write-segments at CDX scale and reports as a
# screen colour, because the ASUS has no serial port and ConOut has never been
# shown to render a character there. A colour ladder nobody has watched fail is
# worth nothing, so this forces each state and prints which stage was last
# painted.
#
#   build/sink-arm.ps1                 # every arm
#   build/sink-arm.ps1 -Only pass      # one arm
#   build/sink-arm.ps1 -Keep           # leave the working images for inspection
#
# A FULL RUN IS EXPENSIVE AND -Only IS THE NORMAL WAY TO USE THIS. The probe
# holds its colour by repainting and never exits, so every arm costs its whole
# deadline; three of the five fill and write 2.7 MB, so the wall clock is
# roughly forty minutes. Run one arm while working on one arm, and the full set
# when the payload changes.
#
# Expected, and a differing row is a defect in the ladder, not in the arm:
#
#   pass      verified   WHITE     the 2.7 MB write landed and verified
#   shift     wrote      BLUE      write fine, oracle deliberately wrong
#   nodisk    systab     YELLOW    no EFI_BLOCK_IO to locate
#   badbpb    read       MAGENTA   sector came back, bytes-per-sector is not 512
#   small     bpb        ORANGE    volume too small to hold 2.7 MB
#
# CYAN (the SystemTable cell reading zero) is NOT forced here: the stub primes
# that cell and nothing in the bed can unprime it. It is the one rung that has
# never been seen to fire, exactly as on the block ladder.
#
# The `shift` arm is the positive control and it is the reason this script can
# build a payload of its own: sl-shift is a source constant, so the arm is a
# second image compiled from a spliced copy of the chapter. An oracle that
# cannot report bad is not evidence (L-FALSIF).
[CmdletBinding()]
param(
    [switch]$Keep,
    [string]$Only = '',
    # The write arms take about five minutes in the bed; the rest answer in
    # seconds. The wait is always paid in full, so it is sized per arm.
    [int]$WriteSeconds = 900,
    [int]$FastSeconds = 90
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
$Img  = Join-Path $Repo 'build\boot\sinkladder.img'
foreach ($f in @($Vm, $Img)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing"; exit 1 }
}

# REFUSE TO CALIBRATE A STALE IMAGE. build-option-a.ps1 leaves the previous
# .img in place when the compile fails, so running this straight after a failed
# build calibrates the OLD payload and prints a green result for source that
# never compiled.
#
# THE LIST IS THE WHOLE TREE, NOT THE PROBE'S OWN TWO CHAPTERS, and that is the
# fix for a hole that produced two false greens (fester, 2026-08-16). The guard
# used to name SinkLadderProbe and MetalLadder only. The function this arm
# EXISTS to exercise is `fat16-next-cluster` in the foreword, so a sabotaged
# Fat16.codex -- `cluster <= 100` against a 5,364-cluster chain, which must
# truncate -- passed the check silently, the failed rebuild left the old image
# in place, and the arm printed `verified` twice for source that never
# compiled. A hand-written list goes stale the moment the payload's reach
# changes; the payload reaches the foreword and it always did.
#
# It is derived from the CITE GRAPH rather than from the whole tree, and the
# difference matters in both directions. The whole tree is what `build/desk.ps1`
# stats, because the desk binary really does reach all of it; this payload is
# one chapter and its transitive cites, so statting the tree would refuse the
# arm every time somebody touched a pane it has never heard of, and an arm that
# cries stale on unrelated work gets its guard commented out. The closure below
# was 11 files when this was written and it contains Fat16, which the run
# prints and this comment therefore does not have to be right about.
$Src  = Join-Path $Repo 'apps\works\SinkLadderProbe.codex'
if (-not (Test-Path $Src)) { Write-Host "FAIL: source $Src missing"; exit 1 }

$byName = @{}
foreach ($f in Get-ChildItem -Path (Join-Path $Repo 'apps'), (Join-Path $Repo 'codex') `
                -Recurse -File -Filter *.codex -ErrorAction SilentlyContinue) {
    $n = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    if (-not $byName.ContainsKey($n)) { $byName[$n] = $f }
}
$closure = [ordered]@{}
$unresolved = @()
$queue = [System.Collections.Queue]::new()
$queue.Enqueue([IO.Path]::GetFileNameWithoutExtension($Src))
while ($queue.Count -gt 0) {
    $name = $queue.Dequeue()
    if ($closure.Contains($name)) { continue }
    if (-not $byName.ContainsKey($name)) { $unresolved += $name; continue }
    $file = $byName[$name]
    $closure[$name] = $file
    # `cites <Quire> chapter <Name>` -- the header block, before any Section.
    foreach ($line in Get-Content $file.FullName -ErrorAction SilentlyContinue) {
        if ($line -match '^\s*Section:') { break }
        if ($line -match '^\s*cites\s+\S+\s+chapter\s+(\S+)\s*$') { $queue.Enqueue($Matches[1]) }
    }
}
# A chapter name the closure could not resolve to a file is a HOLE in this
# guard, not a curiosity: whatever it names is unwatched. Say so rather than
# quietly checking a smaller set than advertised.
if ($unresolved.Count -gt 0) {
    Write-Host "note: $($unresolved.Count) cited chapter(s) not resolved to a file, so not watched: $($unresolved -join ', ')"
}
if (-not $closure.Contains('Fat16')) {
    Write-Host 'FAIL: the cite closure does not contain Fat16, which is the chapter this arm exists to exercise.'
    Write-Host '      Either the probe stopped citing it or the closure walk is broken. Do not trust a green from this run.'
    exit 1
}

$imgTime = (Get-Item $Img).LastWriteTimeUtc
# THE KERNEL IS DELIBERATELY NOT AN MTIME INPUT, and it is the one place this
# guard is knowingly incomplete. A new seed does change the compiled payload,
# so by rights `seed/Codex.cdx` belongs here -- but this client is `nomodtime`,
# so a sync stamps the file with the sync time whether or not a byte moved, and
# every merge-down would then refuse the arm for a seed that is identical.
# A guard that refuses on every run is a guard that gets commented out. Its
# hash goes in the provenance line instead: if it differs between two runs, the
# compiler moved and the image is owed a rebuild.
$newer = @(@($closure.Values) | Where-Object { $_ -and $_.LastWriteTimeUtc -gt $imgTime })
if ($newer.Count -gt 0) {
    $show = @($newer | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 5)
    Write-Host "STALE: $($newer.Count) source file(s) are newer than sinkladder.img:"
    foreach ($f in $show) { Write-Host "       $($f.Name)  $($f.LastWriteTimeUtc.ToString('u'))" }
    if ($newer.Count -gt $show.Count) { Write-Host "       ... and $($newer.Count - $show.Count) more" }
    Write-Host "       Rebuild first (and check the compile actually succeeded):"
    Write-Host "       p4 edit build/boot/sinkladder.img"
    Write-Host "       build/boot/build-option-a.ps1 -Src apps/works/SinkLadderProbe.codex -Out build/boot/sinkladder.img -Kernel seed/Codex.cdx -Uefi"
    exit 1
}

# WHICH BYTES ANSWERED. The staleness guard cannot see a rebuild that never
# wrote -- `sinkladder.img` is a depot file, so a rebuild without `p4 edit`
# dies on Access denied and changes nothing, which no mtime comparison can
# distinguish from not having rebuilt at all. So the verdict never travels
# without the identity of the image that produced it: check this hash against
# the one your build printed before believing a green.
$imgHash = (Get-FileHash -Algorithm SHA256 $Img).Hash.Substring(0, 16)
$seedPath = Join-Path $Repo 'seed\Codex.cdx'
$seedHash = if (Test-Path $seedPath) { (Get-FileHash -Algorithm SHA256 $seedPath).Hash.Substring(0, 16) } else { '(no seed)' }
$imgProv = "img $imgHash  $($imgTime.ToString('u'))  seed $seedHash  closure $($closure.Count) chapters"
Write-Host "image: $imgProv"

# Derived from the workspace, never a fixed path: two agents running this at
# once must not read each other's images (L-SHARED).
$Work = Join-Path ([IO.Path]::GetTempPath()) ("sink-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

function New-Copy([string]$name, [string]$from = $Img) {
    $dst = Join-Path $Work $name
    Copy-Item $from $dst -Force
    Set-ItemProperty $dst -Name IsReadOnly -Value $false
    return $dst
}

# AN UNFINISHED ARM IS NOT A RED, SO RETRY IT ONCE AND SAY SO. The 2.7 MB
# write's wall clock on this box swings with whatever else is running: the same
# shift arm finished inside 480s on one run and was still going at 900s on the
# next, with nothing about the payload changed. Reporting the second as a
# calibration failure is a red whose answer is "run it again", which is exactly
# the kind of red that trains its reader to ignore reds. A retry that happens is
# printed, never silent -- if this line starts appearing every run, the box is
# oversubscribed or the write has genuinely regressed.
function Invoke-ArmRetried([string]$name, [string]$kernel, [string]$disk, [int]$Wait) {
    $r = Invoke-Arm $name $kernel $disk $Wait
    if ($r -notlike '(no verdict*') { return $r }
    Write-Host "  note: $name did not finish in ${Wait}s; running it once more"
    $r2 = Invoke-Arm $name $kernel $disk $Wait
    if ($r2 -notlike '(no verdict*') { Write-Host "  note: $name finished on the retry" }
    return $r2
}

function Invoke-Arm([string]$name, [string]$kernel, [string]$disk, [int]$Wait) {
    $err = Join-Path $Work "$name.err"
    $a = @('-kernel', $kernel, '-uefi', '-headless', '-output', (Join-Path $Work "$name.out"))
    if ($disk) { $a += @('-disk', $disk) }
    $p = Start-Process -FilePath $Vm -ArgumentList $a -NoNewWindow -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")

    # DO NOT READ THE VM'S STREAMS WHILE IT IS RUNNING. Watching them to stop as
    # soon as a rung reported would save the wait, and it kills the VM: the pass
    # arm died seconds after the same rung on every polled run, with no fault
    # line in either stream, while five by-hand runs of the identical image with
    # no polling reached the last rung. Opening the files share-write was not
    # enough. `-output` was the obvious suspect and is NOT the cause -- adding it
    # to a by-hand run changed nothing. Wait, then read, which is what
    # build/ladder-arm.ps1 does. See OperatorsManual, "TAILING A LIVE VM LOG".
    #
    # The probe holds its colour by repainting and never exits, so the wait is
    # always paid in full. It is therefore sized per arm rather than one number
    # for all: a single 900s ceiling made a five-arm run cost 75 minutes.
    $deadline = (Get-Date).AddSeconds($Wait)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500

    # Both streams: the guest's console lands on codex-vm's STDERR (ConOut
    # echoes there as of main 14398) and `-output` stays at 6 bytes, so reading
    # one of them reports a healthy arm as silent.
    $text = @()
    foreach ($f in @((Join-Path $Work "$name.out"), $err)) {
        if (Test-Path $f) { $text += @(Get-Content $f -ErrorAction SilentlyContinue) }
    }

    # A HALF-FINISHED RUN IS NOT A VERDICT. Killed mid-write, the last rung to
    # PASS is `bpb`, which is exactly what the `small` arm reports when the
    # volume genuinely cannot hold the payload -- so without this the two are
    # indistinguishable and a run that never finished reads as a calibration
    # mismatch. Every arm ends either with a rung that failed or with the last
    # rung passing; anything else did not finish.
    $failed = @($text | Select-String -Pattern 'SINKLADDER \S+ ok=0' | ForEach-Object { $_.Line })
    $done   = @($text | Select-String -Pattern 'SINKLADDER verified ok=1' | ForEach-Object { $_.Line })

    # The last stage that PASSED, not the last that painted: a failed stage
    # repaints the previous colour, so its own tag names a rung the screen is
    # not showing.
    $painted = @($text | Select-String -Pattern 'ok=1 painted fb=1' | ForEach-Object { $_.Line })
    if ($painted.Count -eq 0) { return '(nothing painted)' }
    $last = (($painted[-1] -split 'SINKLADDER ')[-1] -replace ' ok=.*$', '')

    if ($failed.Count -eq 0 -and $done.Count -eq 0) {
        return "(no verdict in ${Wait}s: unfinished, last rung to pass was '$last')"
    }
    return $last
}

$expected = [ordered]@{ pass = 'verified'; shift = 'wrote'; nodisk = 'systab'; badbpb = 'read'; small = 'bpb' }
$actual   = [ordered]@{}
$wanted   = if ($Only) { @($Only) } else { @($expected.Keys) }
foreach ($n in $wanted) { if (-not $expected.Contains($n)) { Write-Host "no arm named '$n'"; exit 2 } }

if ($wanted -contains 'pass') {
    $k = New-Copy 'k-pass.img'
    $actual['pass'] = Invoke-ArmRetried 'pass' $k $k $WriteSeconds
}

# The positive control. sl-shift is a source constant, so this arm needs its
# own payload: the chapter is copied out, the constant flipped, and a second
# image built from it. The write must still succeed (BLUE) and the verify must
# report every byte bad.
if ($wanted -contains 'shift') {
    # The spliced copy has to live INSIDE the repo: build/bundle-app.ps1 does
    # Resolve-Path on a path it has already joined to the repo root, so an
    # absolute temp path becomes <repo>\C:\Users\... and the bundle fails.
    # build-output is p4-ignored, so nothing here reaches the depot.
    $spliceRel = 'build-output/SinkLadderProbeShift.codex'
    $splice = Join-Path $Repo $spliceRel
    $body = (Get-Content $Src -Raw) -replace 'sl-shift : Integer = 0', 'sl-shift : Integer = 1'
    if ($body -notmatch 'sl-shift : Integer = 1') { Write-Host 'FAIL: could not splice sl-shift'; exit 1 }
    $body = $body -replace 'Chapter: SinkLadderProbe', 'Chapter: SinkLadderProbeShift'
    New-Item -ItemType Directory -Force (Split-Path $splice) | Out-Null
    if (Test-Path $splice) { Set-ItemProperty $splice -Name IsReadOnly -Value $false }
    [IO.File]::WriteAllText($splice, $body, [Text.UTF8Encoding]::new($false))
    $shiftImg = Join-Path $Work 'sinkladder-shift.img'
    Remove-Item $shiftImg -Force -ErrorAction SilentlyContinue
    & pwsh -NoProfile -File (Join-Path $Repo 'build\boot\build-option-a.ps1') `
        -Src $spliceRel -Out $shiftImg -Kernel (Join-Path $Repo 'seed\Codex.cdx') -Uefi *> (Join-Path $Work 'shift-build.log')
    if (-not (Test-Path $shiftImg)) {
        $actual['shift'] = '(skipped: shift payload did not build)'
    } else {
        $k = New-Copy 'k-shift.img' $shiftImg
        $actual['shift'] = Invoke-ArmRetried 'shift' $k $k $WriteSeconds
    }
}

if ($wanted -contains 'nodisk') {
    $k = New-Copy 'k-nodisk.img'
    $actual['nodisk'] = Invoke-ArmRetried 'nodisk' $k '' $FastSeconds
}

# A sector comes back but its bytes-per-sector is not 512. The BAD disk is
# passed as -disk only: codex-vm reads the BPB of -kernel to find BOOTX64.EFI,
# so corrupting the one it boots from would stop the guest before it starts.
if ($wanted -contains 'badbpb') {
    $k = New-Copy 'k-badbpb.img'
    $d = New-Copy 'd-badbpb.img'
    $bytes = [IO.File]::ReadAllBytes($d)
    $bytes[2048 * 512 + 11] = 0
    $bytes[2048 * 512 + 12] = 0
    [IO.File]::WriteAllBytes($d, $bytes)
    $actual['badbpb'] = Invoke-ArmRetried 'badbpb' $k $d $FastSeconds
}

# A volume too small to hold the payload, so the chain cannot be allocated and
# the write rung fails.
#
# THE SIZE IS ASSERTED, NOT ASSUMED, and the first version of this arm got it
# wrong in the direction that passes: -TotalSectors 16384 is 8 MB and still
# leaves ~6 MB free, so the 2.7 MB write succeeded and the arm painted WHITE.
# That is the block ladder's small arm copied without its mechanism -- there it
# failed because LBA 30000 does not exist in an 8 MB image, which has nothing to
# do with free space. 9216 sectors gives 5014 clusters against the 5364 a
# 2,745,998-byte file needs at 512 bytes per cluster, and FAT16 still needs
# >= 4085, so the window is narrow enough to be worth checking rather than
# trusting.
if ($wanted -contains 'small') {
    $k = New-Copy 'k-small.img'
    $efi = Join-Path $Repo 'build-output\optiona.efi'
    $d = Join-Path $Work 'd-small.img'
    if (-not (Test-Path $efi)) {
        $actual['small'] = '(skipped: build-output/optiona.efi missing)'
    } else {
        Remove-Item $d -Force -ErrorAction SilentlyContinue
        $log = Join-Path $Work 'small-build.log'
        & pwsh -NoProfile -File (Join-Path $Repo 'build\build-img.ps1') `
            -PeInput $efi -Out $d -TotalSectors 9216 *> $log
        $clusters = 0
        if (Test-Path $log) {
            $m = Select-String -Path $log -Pattern 'clusters=(\d+)'
            if ($m) { $clusters = [int]$m.Matches[0].Groups[1].Value }
        }
        # The payload's own numbers, read from the chapter rather than repeated
        # here, so a change to either side breaks the arm loudly (L-COUNT).
        $sz = 0
        $ms = Select-String -Path $Src -Pattern 'sl-size : Integer = (\d+)'
        if ($ms) { $sz = [int]$ms.Matches[0].Groups[1].Value }
        $need = [Math]::Ceiling($sz / 512)
        if (-not (Test-Path $d)) {
            $actual['small'] = '(skipped: small volume did not build)'
        } elseif ($clusters -eq 0 -or $sz -eq 0) {
            $actual['small'] = '(skipped: could not read cluster count or sl-size)'
        } elseif ($clusters -ge $need) {
            # Refuse rather than run: a volume that CAN hold the payload makes
            # this arm pass for the wrong reason, which is what it did once.
            $actual['small'] = "(invalid arm: volume holds $clusters clusters, payload needs $need)"
        } else {
            $actual['small'] = Invoke-ArmRetried 'small' $k $d $WriteSeconds
        }
    }
}

$bad = 0
Write-Host ''
Write-Host "image:   $imgProv"
Write-Host 'arm      expected   actual'
Write-Host '-------  ---------  ---------'
foreach ($name in $wanted) {
    $e = $expected[$name]; $a = $actual[$name]
    $mark = if ($a -eq $e) { '' } else { '   <-- MISMATCH' }
    if ($a -ne $e) { $bad++ }
    Write-Host ("{0,-8} {1,-10} {2}{3}" -f $name, $e, $a, $mark)
}
Write-Host ''
if (-not $Keep) { Remove-Item (Join-Path $Work '*.img') -Force -ErrorAction SilentlyContinue }
if ($bad -gt 0) { Write-Host "SINK LADDER NOT CALIBRATED: $bad arm(s) disagree"; exit 1 }
Write-Host 'Sink ladder calibrated: every arm painted the stage it should have.'
exit 0
