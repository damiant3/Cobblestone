# Drive a GOP application with scripted input and compare the resulting frame
# against an expected image. Runs headless: codex-vm injects the pointer and
# keyboard straight into the guest, so no host cursor moves and no window takes
# focus. UI tests can therefore run beside other work without fighting anyone
# for the physical mouse.
#
#   build/test-gui.ps1 -Kernel build/output/circuits.cdx `
#                      -Script codex/test/gui/circuits-drag.uiscript
#
# The .uiscript sidecar names the expected image beside it (<name>.expected.bmp).
# -Accept records the current frame AS the expected image (for a new test, or
# after an intended visual change -- look at the picture before you accept it).

param(
    [Parameter(Mandatory = $true)][string]$Kernel,
    [Parameter(Mandatory = $true)][string]$Script,
    [string]$Expected,
    [string]$OutDir = "test-output/gui",
    [int]$Width  = 1024,
    [int]$Height = 768,
    [int]$Mem    = 3072,
    [int]$Tolerance = 0,          # allowed differing pixels
    [int]$Retries = 3,            # extra attempts when a run captures no frame / blanks under load
    [int]$RetrySettleMs = 2500,   # extra wall-clock added to the screenshot deadline per retry
    [switch]$Accept,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$vm = "tools/codex-vm.exe"

if (-not (Test-Path $Kernel)) { throw "kernel not found: $Kernel" }
if (-not (Test-Path $Script)) { throw "script not found: $Script" }
if (-not $Expected) {
    $Expected = [IO.Path]::ChangeExtension($Script, $null).TrimEnd('.') + ".expected.bmp"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$name = [IO.Path]::GetFileNameWithoutExtension($Script)

# ---- Compile the .uiscript into codex-vm input timelines --------------------
#
# Commands (one per line, '#' comments):
#   boot <ms>                     when the app is up and the script may start (default 8000)
#   move <x> <y>                  move the pointer
#   press <x> <y> [btn]           button down   (btn: 1 left, 2 right, 4 middle; default 1)
#   release <x> <y> [btn]         button up
#   click <x> <y> [btn]           press + release
#   drag <x1> <y1> <x2> <y2> [steps]   press, move in steps, release
#   key <scancode>                Set-1 make code
#   wait <ms>                     advance the clock
#   settle <ms>                   extra time after the last event before the frame is taken
#   vmargs <flag> [flag ...]      extra codex-vm flags for this test (repeatable)
#   mask <x> <y> <w> <h>          exclude a rect from the comparison (repeatable)
#   expect-ink <x> <y> <w> <h> <min> <max>   lit-pixel count in a rect must be
#                                 within [min,max] (repeatable)
#
# expect-ink is the cheap half of the answer to "what checks the masked area?".
# It does not read the value -- it counts the pixels the app lit -- so it is a
# heuristic and is documented as one. What it buys is that a masked rect stops
# being WHOLLY unchecked: a field that went blank, doubled in length, or filled
# with garbage moves the count out of range, and before this a golden could not
# tell any of those from the value merely changing.
#
# Reading the value itself needs a glyph atlas, and that is not possible for
# GuiOS today: booted without a disk it falls back to a block font that draws
# 's', 't' and 'b' as one identical bitmap (likewise 'd'/'o', 'O'/'0', '5'/'9'),
# so the value is not in the pixels to be read. Fix the font and a real
# expect-text becomes worth building; until then this is the honest check.
#
# Derive min and max by MEASURING across the range the field actually takes,
# then leave margin. The harness prints the observed count on every run.
#
# mask is for a widget whose value is legitimately not a function of the
# program -- an uptime counter, a heap figure, a gauge driven by one. Freezing
# such a value would be a lie and a tolerance would hide real regressions
# anywhere in the frame, so the area is declared instead. Two rules make a
# declared hole safer than a hidden one: the mask is written in the test beside
# the reason for it, and every run PRINTS the masked area, so nobody discovers
# a year later that the interesting half of the frame was never compared.
#
# Derive the rect by MEASURING, not by reading coordinates off a picture:
# capture the same frame twice with the volatile input different (two
# screenshot delays), diff, and take the bounding box of the differing pixels.
#
# vmargs exists because some frames are only reproducible on a machine that has
# been told to hold still. The standing case is the clock: an app that paints
# the time cannot be compared against a recorded image while codex-vm answers
# the RTC from the host, so such a test carries `vmargs -rtc <stamp>`. Keeping
# it in the .uiscript rather than in a harness switch means the test states its
# own machine, the way a .vmargs sidecar does for the serial battery.

$mouseEvents = New-Object System.Collections.Generic.List[string]
$keyEvents   = New-Object System.Collections.Generic.List[string]
$extraVmArgs = New-Object System.Collections.Generic.List[string]
$masks       = New-Object System.Collections.Generic.List[int[]]
$inkChecks   = New-Object System.Collections.Generic.List[int[]]

$t       = 8000.0     # clock, ms since boot
$settle  = 1200.0
$curX    = 0
$curY    = 0
$curBtn  = 0

function Emit-Mouse([double]$at, [int]$x, [int]$y, [int]$btn) {
    $script:mouseEvents.Add(("{0}:{1},{2},{3}" -f [int]$at, $x, $y, $btn))
    $script:curX = $x; $script:curY = $y; $script:curBtn = $btn
}

$lineNo = 0
foreach ($raw in Get-Content $Script) {
    $lineNo++
    $line = $raw.Trim()
    if ($line -eq "" -or $line.StartsWith("#")) { continue }
    $p = $line -split '\s+'
    switch ($p[0].ToLower()) {
        'boot'    { $t = [double]$p[1] }
        'wait'    { $t += [double]$p[1] }
        'settle'  { $settle = [double]$p[1] }
        'move'    { Emit-Mouse $t ([int]$p[1]) ([int]$p[2]) $curBtn; $t += 120 }
        'press'   { $b = if ($p.Count -gt 3) { [int]$p[3] } else { 1 }
                    Emit-Mouse $t ([int]$p[1]) ([int]$p[2]) $b; $t += 150 }
        'release' { $b = if ($p.Count -gt 3) { [int]$p[3] } else { 0 }
                    Emit-Mouse $t ([int]$p[1]) ([int]$p[2]) 0; $t += 150 }
        'click'   {
            $b = if ($p.Count -gt 3) { [int]$p[3] } else { 1 }
            $x = [int]$p[1]; $y = [int]$p[2]
            Emit-Mouse $t $x $y 0;  $t += 120     # hover first, as a hand would
            Emit-Mouse $t $x $y $b; $t += 120     # down
            Emit-Mouse $t $x $y 0;  $t += 250     # up
        }
        'drag'    {
            $x1 = [int]$p[1]; $y1 = [int]$p[2]; $x2 = [int]$p[3]; $y2 = [int]$p[4]
            $steps = if ($p.Count -gt 5) { [int]$p[5] } else { 20 }
            Emit-Mouse $t $x1 $y1 0; $t += 120        # hover
            Emit-Mouse $t $x1 $y1 1; $t += 150        # press
            for ($s = 1; $s -le $steps; $s++) {
                $x = [int]($x1 + ($x2 - $x1) * $s / $steps)
                $y = [int]($y1 + ($y2 - $y1) * $s / $steps)
                Emit-Mouse $t $x $y 1; $t += 60       # move, held
            }
            Emit-Mouse $t $x2 $y2 0; $t += 250        # release
        }
        'key'     { $keyEvents.Add(("{0}:{1}" -f [int]$t, [int]$p[1])); $t += 250 }
        'vmargs'  {
            if ($p.Count -lt 2) { throw "$Script line ${lineNo}: vmargs needs at least one flag" }
            for ($k = 1; $k -lt $p.Count; $k++) { $extraVmArgs.Add($p[$k]) }
        }
        'mask'    {
            if ($p.Count -ne 5) { throw "$Script line ${lineNo}: mask needs x y w h" }
            $mw = [int]$p[3]; $mh = [int]$p[4]
            if ($mw -le 0 -or $mh -le 0) { throw "$Script line ${lineNo}: mask w and h must be positive" }
            $masks.Add(@([int]$p[1], [int]$p[2], $mw, $mh))
        }
        'expect-ink' {
            if ($p.Count -ne 7) { throw "$Script line ${lineNo}: expect-ink needs x y w h min max" }
            if ([int]$p[3] -le 0 -or [int]$p[4] -le 0) { throw "$Script line ${lineNo}: expect-ink w and h must be positive" }
            if ([int]$p[5] -gt [int]$p[6]) { throw "$Script line ${lineNo}: expect-ink min is greater than max" }
            $inkChecks.Add(@([int]$p[1], [int]$p[2], [int]$p[3], [int]$p[4], [int]$p[5], [int]$p[6]))
        }
        default   { throw "$Script line ${lineNo}: unknown command '$($p[0])'" }
    }
}

$shotAt = [int]($t + $settle)

$mouseFile = Join-Path $OutDir "$name.mouse"
$keysFile  = Join-Path $OutDir "$name.keys"
$actual    = Join-Path $OutDir "$name.actual.bmp"
Set-Content $mouseFile -Value ($mouseEvents -join "`n") -NoNewline
if ($keyEvents.Count -gt 0) { Set-Content $keysFile -Value ($keyEvents -join "`n") -NoNewline }

# ---- Run headless -----------------------------------------------------------
# Boot the app and capture one frame at $delay ms. Under parallel host load the
# guest can miss a fixed deadline, so retries push the deadline out (see below).
function Run-Vm([int]$delay) {
    $a = @(
        "-kernel", $Kernel, "-headless",
        "-gop-width", $Width, "-gop-height", $Height, "-mem", $Mem,
        "-mouse-file", $mouseFile,
        "-screenshot", $actual, "-screenshot-delay", $delay
    )
    if ($keyEvents.Count -gt 0) { $a += @("-keys-file", $keysFile) }
    if ($extraVmArgs.Count -gt 0) { $a += $extraVmArgs }
    Remove-Item $actual -Force -ErrorAction SilentlyContinue
    & $vm @a 2>&1 | Out-Null
}

Write-Host ("[gui] {0}: {1} pointer events, {2} keys, frame at {3} ms{4}" -f `
    $name, $mouseEvents.Count, $keyEvents.Count, $shotAt,
    $(if ($extraVmArgs.Count -gt 0) { " [" + ($extraVmArgs -join " ") + "]" } else { "" }))

# Count pixels differing between the captured frame and the expected image.
# Returns -1 when no frame was captured. Loading the expected image lazily lets
# the -Accept path (record only) skip the comparison entirely.
function Compare-Frame {
    if (-not (Test-Path $actual)) { return -1 }
    Add-Type -AssemblyName System.Drawing
    $a = [System.Drawing.Bitmap]::new((Resolve-Path $actual).Path)
    $e = [System.Drawing.Bitmap]::new((Resolve-Path $Expected).Path)
    try {
        if ($a.Width -ne $e.Width -or $a.Height -ne $e.Height) {
            Write-Host ("[gui] {0} -- size {1}x{2}, expected {3}x{4}" -f $name, $a.Width, $a.Height, $e.Width, $e.Height)
            return [int]::MaxValue
        }
        # A masked pixel is not compared. Build a coverage map once rather than
        # testing every rect per pixel, and count it, because the count is what
        # gets reported: a hole nobody can see the size of is the failure mode
        # this whole mechanism has to avoid.
        $skip = $null
        $maskedCount = 0
        if ($masks.Count -gt 0) {
            $skip = New-Object 'bool[]' ($a.Width * $a.Height)
            foreach ($m in $masks) {
                $mx0 = [Math]::Max(0, $m[0]); $my0 = [Math]::Max(0, $m[1])
                $mx1 = [Math]::Min($a.Width  - 1, $m[0] + $m[2] - 1)
                $my1 = [Math]::Min($a.Height - 1, $m[1] + $m[3] - 1)
                for ($y = $my0; $y -le $my1; $y++) {
                    for ($x = $mx0; $x -le $mx1; $x++) {
                        $i = $y * $a.Width + $x
                        if (-not $skip[$i]) { $skip[$i] = $true; $maskedCount++ }
                    }
                }
            }
        }
        $diff = 0
        for ($y = 0; $y -lt $a.Height; $y++) {
            for ($x = 0; $x -lt $a.Width; $x++) {
                if ($skip -and $skip[$y * $a.Width + $x]) { continue }
                if ($a.GetPixel($x, $y).ToArgb() -ne $e.GetPixel($x, $y).ToArgb()) { $diff++ }
            }
        }
        if ($maskedCount -gt 0) {
            $total = $a.Width * $a.Height
            $pct = 100.0 * $maskedCount / $total
            Write-Host ("[gui] {0}: {1} rect(s) masked, {2} pixels not compared ({3:N2}% of frame)" -f `
                $name, $masks.Count, $maskedCount, $pct)
            if ($pct -ge 10.0) {
                Write-Host ("[gui] WARNING {0} -- a tenth of the frame is excluded; this golden proves less than it looks like it does" -f $name)
            }
        }
        return $diff
    } finally {
        $a.Dispose(); $e.Dispose()
    }
}

# Lit-pixel count per expect-ink rect, checked against its declared range.
# Returns the number of FAILING checks; always prints what it observed, so the
# range can be widened from evidence rather than from taste.
function Test-InkChecks {
    if ($inkChecks.Count -eq 0) { return 0 }
    if (-not (Test-Path $actual)) { return 0 }
    Add-Type -AssemblyName System.Drawing
    $a = [System.Drawing.Bitmap]::new((Resolve-Path $actual).Path)
    try {
        $failed = 0
        foreach ($k in $inkChecks) {
            $x0 = [Math]::Max(0, $k[0]); $y0 = [Math]::Max(0, $k[1])
            $x1 = [Math]::Min($a.Width  - 1, $k[0] + $k[2] - 1)
            $y1 = [Math]::Min($a.Height - 1, $k[1] + $k[3] - 1)
            $ink = 0
            for ($y = $y0; $y -le $y1; $y++) {
                for ($x = $x0; $x -le $x1; $x++) {
                    $c = $a.GetPixel($x, $y)
                    if ((0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B) -gt 96) { $ink++ }
                }
            }
            $ok = ($ink -ge $k[4] -and $ink -le $k[5])
            if (-not $ok) { $failed++ }
            Write-Host ("[gui] {0}: ink at {1},{2} {3}x{4} = {5} (expected {6}..{7}) {8}" -f `
                $name, $k[0], $k[1], $k[2], $k[3], $ink, $k[4], $k[5], $(if ($ok) { "OK" } else { "OUT OF RANGE" }))
        }
        return $failed
    } finally {
        $a.Dispose()
    }
}

# -Accept records the frame from a single clean run and never compares.
if ($Accept) {
    Run-Vm $shotAt
    if (-not (Test-Path $actual)) { Write-Host "[gui] FAIL $name -- no frame captured"; exit 1 }
    Copy-Item $actual $Expected -Force
    Write-Host "[gui] RECORDED $name -> $Expected (look at it before you trust it)"
    exit 0
}
if (-not (Test-Path $Expected)) {
    Write-Host "[gui] FAIL $name -- no expected image. Run with -Accept to record: $Expected"
    exit 1
}

# ---- Run and compare, retrying a bad capture --------------------------------
#
# A screenshot is taken at a fixed wall-clock offset. Under parallel host load
# the guest can miss that deadline and hand back a blank (or no) frame, which
# reads as a spurious mismatch. Each retry pushes the deadline out by
# RetrySettleMs so the guest gets more time to render -- a re-roll at the same
# deadline is just another dice throw under the same load. A genuine regression
# renders a stable wrong frame and fails at every deadline, so retrying cannot
# mask one; it only absorbs the contention blank. The first clean pass wins.
$attempts = $Retries + 1
$diff = [int]::MaxValue
for ($attempt = 1; $attempt -le $attempts; $attempt++) {
    Run-Vm ($shotAt + ($attempt - 1) * $RetrySettleMs)
    $diff = Compare-Frame
    if ($diff -ge 0 -and $diff -le $Tolerance) { break }
    if ($attempt -lt $attempts) {
        $why = if ($diff -lt 0) { "no frame captured" } else { "$diff pixels differ" }
        Write-Host ("[gui] retry {0}/{1} {2} -- {3}" -f $attempt, ($attempts - 1), $name, $why)
    }
}

if ($diff -lt 0) {
    Write-Host ("[gui] FAIL {0} -- no frame captured after {1} attempts" -f $name, $attempts)
    exit 1
}
if ($diff -gt $Tolerance) {
    Write-Host ("[gui] FAIL {0} -- {1} pixels differ (tolerance {2}). Actual: {3}" -f $name, $diff, $Tolerance, $actual)
    exit 1
}
# The ink checks run on the frame that just passed the pixel comparison, so a
# failure here is specifically "the masked area holds the wrong amount of
# content" and cannot be confused with a layout regression.
$inkFailed = Test-InkChecks
if ($inkFailed -gt 0) {
    Write-Host ("[gui] FAIL {0} -- {1} ink check(s) out of range. Actual: {2}" -f $name, $inkFailed, $actual)
    exit 1
}
if (-not $KeepArtifacts) { Remove-Item $mouseFile, $actual -Force -ErrorAction SilentlyContinue }
Write-Host ("[gui] PASS {0} ({1} pixels differ)" -f $name, $diff)
exit 0
