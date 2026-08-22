# desk-goldens.ps1 -- one captured frame per desk pane, hashed, from a PINNED
# kernel.
#
# WHAT IT IS FOR. A change to the widget layer, the compositor or the theme
# reaches every pane at once and there is no unit test that can say so. The
# acceptance criterion for such a change is this sweep: every pane that the
# change does not concern must hash IDENTICALLY before and after, and only the
# panes it does concern may move. A change that moves all fourteen has done
# something its author did not intend, and that is the reading this exists to
# make cheap.
#
# It has already paid for itself twice. The compositor clip (main 17846) moved
# four panes and nine stayed byte-identical, which is what made the four
# believable; and the sweep is what caught a fill running past x = w and
# wrapping onto the NEXT row's left edge, breaking the left border of two panes
# into bands. Nothing else in the tree was looking at that.
#
# THE KERNEL IS A PARAMETER AND codex-vm IS INVOKED DIRECTLY, and both halves
# of that are deliberate. The first version called build/desk.ps1, which
# rebuilds whenever any .codex is newer than desk.cdx, so editing the subject
# while a sweep ran swapped the binary underneath it and half the set came from
# a different compiler. The guard against that was a comment. Now it is the
# shape of the script: it cannot rebuild anything.
#
# -rtc freezes the clock and the HPET, which is what makes two runs of the same
# kernel produce byte-identical frames. Without it the taskbar clock alone
# moves every capture and nothing can be compared.
#
# THE MONITOR PANE IS BUILD-SENSITIVE BY CONSTRUCTION and this is not a
# regression to chase: its memory row prints the heap frontier as hex, so any
# change to the binary moves those pixels.
#
# USAGE
#   build/desk-goldens.ps1 -Kernel build-output/desk.cdx -Tag before
#   ... make the change, rebuild the kernel to a different path ...
#   build/desk-goldens.ps1 -Kernel build-output/desk-new.cdx -Tag after
#   build/desk-goldens.ps1 -Compare before,after
#   build/desk-goldens.ps1 -SelfCheck after
#
# -SelfCheck reads ONE captured set and asserts the relations that must hold
# inside it whatever the change was. It is what stops an arm passing because
# nothing happened: see the browser event arms below.
#
# -Disk DECIDES WHICH TYPEFACE THE SWEEP IS LOOKING AT, and a sweep pointed at
# the wrong one answers a question nobody asked. The desk loads its TrueType
# face off the ESP; with no -disk there is no ESP, so it falls back to the CBF
# bitmap face and the whole proportional path is DORMANT. Captures taken that
# way are a baseline of the bitmap face and they are perfectly stable, which is
# exactly what makes this quiet: a typography change measured against them
# reports fourteen identical panes and reads as "my change did nothing".
#
# So for anything touching type, pass an image built WITH a font:
#   build/build-boot-img.ps1 ...        (defaults to fonts/cc0/cmunss.ttf)
#   build/desk-goldens.ps1 -Kernel ... -Tag before -Disk <that image>
# The Monitor pane's `font` row is the check: it reads `TrueType CMUNSS.TTF`
# when the face loaded and `CBF bitmap` when it did not. Read it before
# trusting a type sweep. (val, 2026-08-20, ShellRefinement stage 1.)
#
# Stage 1 of docs/Designs/Active/OS/ShellRefinement.md asks for captures at
# three widths, because ui-wscale steps at 1600 and a typography change has to
# be read on both sides of that step:
#   -Width 1024 -Height 768     (scale 1)
#   -Width 1600 -Height 900     (scale 2, the default)
#   -Width 1920 -Height 1080    (scale 2, a wider pane)
# The tag carries the geometry, so three widths are three tag pairs and they do
# not overwrite each other.
#
# Compare exits 1 if any pane moved, so it can gate a change rather than only
# report on one.

[CmdletBinding()]
param(
    [string]$Kernel = '',
    [string]$Tag = '',
    [string[]]$Compare = @(),
    [string]$SelfCheck = '',
    [string[]]$ScalePair = @(),
    [int]$Scale = 2,
    [int]$Structural = 8,
    [string[]]$Pane = @(),
    [int]$Width = 1600,
    [int]$Height = 900,
    [int]$ShotDelayMs = 11000,
    [string]$Rtc = '2026-08-20T04:00:00',
    [string]$Disk = '',
    [string]$OutRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = Split-Path -Parent $PSScriptRoot
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
if (-not $OutRoot) { $OutRoot = Join-Path $Repo 'build-output\goldens' }

# The scancode timelines that open each pane. A pane that needs a second key
# lists both with their millisecond stamps. browser-key and browser-scroll are
# the same pane after an event, which is how a repaint is compared against a
# first paint at all.
$Panes = [ordered]@{
    'desk'           = ''
    'files'          = '6000:33'
    'monitor'        = '6000:50'
    'calc'           = '6000:46'
    'console'        = '6000:20'
    'cal'            = '6000:38'
    'issues'         = '6000:23'
    'review'         = '6000:19'
    'style'          = '6000:30'
    'programs'       = '6000:25'
    'clock'          = '6000:37'
    'browser'        = '6000:48'
    'browser-key'    = '6000:48;9000:30'
    'browser-newtab' = '6000:48;9000:29;9200:20;9400:157'
    'browser-scroll' = '6000:48;9000:81'
}

# WHAT THE THREE BROWSER EVENT ARMS ARE FOR, and read this before deciding one
# of them is broken because it agrees with another.
#
# `browser-key` sends scancode 30, which the browser maps to no action. It is
# EXPECTED to hash identically to `browser`, and that identity is the whole of
# what BROWSER-5 was closed on: `gbr-step` releases the memoized widget tree,
# re-dispatches, and repaints in full for ANY non-modifier scancode under 128,
# so the pixels coming back unchanged means a repaint reproduced the first
# paint. Before the fix they differed, because the repaint erased the taskbar.
#
# **On its own that arm cannot fail for the right reason.** An identity is also
# what you get if the keystroke was never delivered, or if the guest never
# reached the repaint. val found the collision in two independent face sweeps
# and asked the question, 2026-08-20, and it is the same green-that-cannot-fail
# shape the -Disk note above warns about.
#
# So `browser-newtab` exists to carry the falsifiable half: Ctrl+T opens a
# second tab and MUST differ from `browser`. Measured 2026-08-20: 12,036 pixels
# across rows 40 to 79 and nowhere else, which is the tab bar band alone. If
# keys stop arriving, that arm goes identical and says so, and only then is
# `browser-key`'s agreement worth anything.
#
# -SelfCheck asserts exactly these relations inside one captured set, so the
# question does not depend on anyone remembering to ask it.
$Relations = @(
    @{ a = 'browser'; b = 'browser-key';    same = $true;
       why = 'a key that means nothing must repaint to the same pixels (BROWSER-5)' },
    @{ a = 'browser'; b = 'browser-newtab'; same = $false;
       why = 'Ctrl+T must open a tab; identical here means keys are not arriving' },
    @{ a = 'browser'; b = 'browser-scroll'; same = $false;
       why = 'Page Down must move the page; identical here means the scroll is dead' }
)

function Suffix { "$Width" + 'x' + "$Height" }

# -Pane narrows the sweep. The scale-pair arm needs one pane at two geometries,
# which is two boots; sweeping all fourteen twice is twenty-eight for no gain.
function Pane-Ids {
    $all = @($Panes.Keys)
    if (-not $Pane -or $Pane.Count -eq 0) { return $all }
    $bad = @($Pane | Where-Object { $all -notcontains $_ })
    if ($bad.Count) { throw ("no such pane(s): {0}. known: {1}" -f ($bad -join ','), ($all -join ',')) }
    return @($Pane)
}

if ($Compare.Count -eq 2) {
    $a = Join-Path $OutRoot ($Compare[0] + '-' + (Suffix))
    $b = Join-Path $OutRoot ($Compare[1] + '-' + (Suffix))
    if (-not (Test-Path $a)) { throw "no capture set at $a" }
    if (-not (Test-Path $b)) { throw "no capture set at $b" }
    $moved = 0; $same = 0; $missing = 0
    foreach ($id in $Panes.Keys) {
        $fa = Join-Path $a "$id.bmp"; $fb = Join-Path $b "$id.bmp"
        if (-not (Test-Path $fa) -or -not (Test-Path $fb)) {
            Write-Output ("{0,-15} MISSING" -f $id); $missing++; continue
        }
        $ha = (Get-FileHash $fa -Algorithm SHA256).Hash
        $hb = (Get-FileHash $fb -Algorithm SHA256).Hash
        if ($ha -eq $hb) { Write-Output ("{0,-15} identical" -f $id); $same++ }
        else { Write-Output ("{0,-15} MOVED" -f $id); $moved++ }
    }
    Write-Output ""
    Write-Output ("{0} identical, {1} moved, {2} missing, at {3}" -f $same, $moved, $missing, (Suffix))
    if ($moved -or $missing) {
        Write-Output "A pane that moved is not automatically wrong. It is wrong if your change had no business reaching it."
        Write-Output "build/bmpdiff.ps1 -A <before>\<pane>.bmp -B <after>\<pane>.bmp says WHICH rows moved."
        exit 1
    }
    exit 0
}

# THE SCALE-PAIR ARM. Stage 2 of ShellRefinement claims an icon renders
# identically at ui-wscale 1 and 2 modulo size. This measures that claim.
#
# THE TWO SETS MUST SHARE A LOGICAL ROOM, and the obvious pair does not. The
# desk lays out in w / ui-wscale by h / ui-wscale and ui-wscale steps at 1600,
# so 1024x768 lays out in 1024x768 while 1600x900 lays out in 800x450: a
# comparison across that pair measures a different room as well as a different
# scale and can say nothing about either. The pair that isolates scale is
# 800x450 (scale 1) against 1600x900 (scale 2), which both lay out in 800x450.
#
# Two counts come back per pane and they mean different things. STRUCTURAL is
# the count at a max channel delta of -Structural or more: a different thing
# drawn, which is what this arm is for. ROUNDING is everything below it, which
# on the desk is dominated by the theme gradient landing one unit apart when it
# is evaluated over twice the rows. Measured 2026-08-20 on desk-h.cdx with the
# bitmap face: 15,029 cells disagree and 14,818 of them are delta 3 or less, so
# a plain equality test reports 4 per cent of the frame and means nothing by it.
#
# The baseline it found on the desk pane, before stage 2 has drawn an icon:
# 60 structural cells, all of them on the corners of the five sidebar buttons
# (Programs, Files, Edit, Console, Shutdown), where the 1x render has sidebar
# background and the 2x render has the button adornment ramp. That is a real
# scale-dependence in the CURRENT chrome and it is stage 2's starting reading,
# not icon breakage to be discovered later and blamed on the icons.
if ($ScalePair.Count -eq 2) {
    $bmpdiff = Join-Path $PSScriptRoot 'bmpdiff.ps1'
    if (-not (Test-Path $bmpdiff)) { throw "no bmpdiff.ps1 beside this script" }
    $a = Join-Path $OutRoot $ScalePair[0]
    $b = Join-Path $OutRoot $ScalePair[1]
    foreach ($d in @($a, $b)) { if (-not (Test-Path $d)) { throw "no capture set at $d" } }

    function Counts([string]$fa, [string]$fb, [int]$minDelta) {
        $line = & $bmpdiff -A $fa -B $fb -Scale $Scale -MinDelta $minDelta -Brief |
                Where-Object { $_ -match '^SCALE ' } | Select-Object -First 1
        if (-not $line) { throw "bmpdiff produced no SCALE line for $fa" }
        $f = $line -split '\s+'
        [pscustomobject]@{ Sub = [int]$f[2]; Blk = [int]$f[3]; Cells = [int]$f[4] }
    }

    Write-Output ("scale pair: {0} against {1}, x{2}, structural at delta >= {3}" -f `
                  $ScalePair[0], $ScalePair[1], $Scale, $Structural)
    Write-Output ""
    $bad = 0; $seen = 0
    foreach ($id in (Pane-Ids)) {
        $fa = Join-Path $a "$id.bmp"; $fb = Join-Path $b "$id.bmp"
        if (-not (Test-Path $fa) -or -not (Test-Path $fb)) { continue }
        $seen++
        $hard = Counts $fa $fb $Structural
        $all  = Counts $fa $fb 1
        $round = $all.Sub - $hard.Sub
        $verdict = if ($hard.Sub -eq 0) { 'identical modulo size' } else { 'STRUCTURAL' }
        Write-Output ("{0,-15} structural {1,6}   rounding {2,7}   of {3} cells   {4}" -f `
                      $id, $hard.Sub, $round, $all.Cells, $verdict)
        if ($hard.Sub) { $bad++ }
    }
    Write-Output ""
    if ($seen -eq 0) { throw "no pane captured in both sets; nothing was compared" }
    Write-Output ("{0} pane(s) compared, {1} carrying a structural difference" -f $seen, $bad)
    if ($bad) {
        Write-Output "build/bmpdiff.ps1 -A <1x>\<pane>.bmp -B <2x>\<pane>.bmp -Scale $Scale -MinDelta $Structural says WHERE."
        exit 1
    }
    exit 0
}

if ($SelfCheck) {
    $d = Join-Path $OutRoot ($SelfCheck + '-' + (Suffix))
    if (-not (Test-Path $d)) { throw "no capture set at $d" }
    $bad = 0
    foreach ($r in $Relations) {
        $fa = Join-Path $d ($r.a + '.bmp'); $fb = Join-Path $d ($r.b + '.bmp')
        if (-not (Test-Path $fa) -or -not (Test-Path $fb)) {
            Write-Output ("{0,-15} vs {1,-15} MISSING" -f $r.a, $r.b); $bad++; continue
        }
        $eq = (Get-FileHash $fa -Algorithm SHA256).Hash -eq (Get-FileHash $fb -Algorithm SHA256).Hash
        $want = if ($r.same) { 'identical' } else { 'different' }
        $got  = if ($eq) { 'identical' } else { 'different' }
        if ($eq -eq $r.same) { Write-Output ("{0,-15} vs {1,-15} ok, {2}" -f $r.a, $r.b, $got) }
        else {
            Write-Output ("{0,-15} vs {1,-15} WRONG: wanted {2}, got {3}" -f $r.a, $r.b, $want, $got)
            Write-Output ("                {0}" -f $r.why)
            $bad++
        }
    }
    Write-Output ""
    if ($bad) { Write-Output ("{0} relation(s) wrong in {1}" -f $bad, $d); exit 1 }
    Write-Output ("all {0} relations hold in {1}" -f $Relations.Count, $d)
    exit 0
}

if (-not $Tag)    { throw "pass -Tag <name> with -Kernel, or -Compare a,b" }
if (-not $Kernel) { throw "pass -Kernel <cdx>; the point of this script is that the binary does not move" }
if (-not (Test-Path $Kernel)) { throw "no such kernel: $Kernel" }
if (-not (Test-Path $Vm)) { throw "codex-vm not built: $Vm (build it with tools/build-vm.ps1)" }

Write-Output ("kernel {0}  {1}" -f (Split-Path $Kernel -Leaf), (Get-FileHash $Kernel -Algorithm SHA256).Hash.Substring(0,16))
Write-Output ("geometry {0}  rtc {1}" -f (Suffix), $Rtc)

$outDir = Join-Path $OutRoot ($Tag + '-' + (Suffix))
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$keyDir = Join-Path $outDir '_keys'
if (-not (Test-Path $keyDir)) { New-Item -ItemType Directory -Path $keyDir -Force | Out-Null }

foreach ($id in (Pane-Ids)) {
    $shot = Join-Path $outDir "$id.bmp"
    if (Test-Path $shot) { Remove-Item $shot -Force }
    $vmArgs = @('-kernel', $Kernel, '-gop-width', $Width, '-gop-height', $Height, '-mem', 3072,
                '-hid-combo', '-rtc', $Rtc, '-headless',
                '-screenshot', $shot, '-screenshot-delay', $ShotDelayMs)
    if ($Disk) {
        if (-not (Test-Path $Disk)) { throw "no such disk image: $Disk" }
        $vmArgs += @('-disk', $Disk)
    }
    if ($Panes[$id]) {
        $keyFile = Join-Path $keyDir "keys-$id.txt"
        Set-Content -Path $keyFile -Value ($Panes[$id] -replace ';', "`n")
        $vmArgs += @('-keys-file', $keyFile, '-hid-nak-unchanged')
    }
    # ONE retry, and only for a capture that never appeared. Measured
    # 2026-08-20: a sweep produced 13 of 14 with the guest booting normally and
    # no crash in the log, so the screenshot point was simply not reached in
    # time on a busy box. That is a SILENT LANE, not a wrong answer, and it is
    # the same distinction check-cross-smoke.ps1 draws: a missing result may be
    # re-run, a result that arrived is never re-run. A retried capture says so
    # in the output, because a sweep that quietly retried is a sweep whose
    # timing you cannot trust later.
    & $Vm @vmArgs *> (Join-Path $keyDir "vm-$id.log")
    $retried = ""
    if (-not (Test-Path $shot)) {
        & $Vm @vmArgs *> (Join-Path $keyDir "vm-$id-retry.log")
        $retried = "  (retried; the first run produced no capture)"
    }
    if (-not (Test-Path $shot)) {
        throw "no capture for $id after a retry; the vm logs are $keyDir\vm-$id.log and $keyDir\vm-$id-retry.log"
    }
    Write-Output ("{0,-15} {1}{2}" -f $id, (Get-FileHash $shot -Algorithm SHA256).Hash.Substring(0,16), $retried)
}

Write-Output ""
Write-Output ("captures in {0}" -f $outDir)
exit 0
