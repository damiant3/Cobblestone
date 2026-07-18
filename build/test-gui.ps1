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

$mouseEvents = New-Object System.Collections.Generic.List[string]
$keyEvents   = New-Object System.Collections.Generic.List[string]

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
$args = @(
    "-kernel", $Kernel, "-headless",
    "-gop-width", $Width, "-gop-height", $Height, "-mem", $Mem,
    "-mouse-file", $mouseFile,
    "-screenshot", $actual, "-screenshot-delay", $shotAt
)
if ($keyEvents.Count -gt 0) { $args += @("-keys-file", $keysFile) }

Write-Host ("[gui] {0}: {1} pointer events, {2} keys, frame at {3} ms" -f `
    $name, $mouseEvents.Count, $keyEvents.Count, $shotAt)

& $vm @args 2>&1 | Out-Null
if (-not (Test-Path $actual)) { Write-Host "[gui] FAIL $name -- no frame captured"; exit 1 }

# ---- Compare ----------------------------------------------------------------
if ($Accept) {
    Copy-Item $actual $Expected -Force
    Write-Host "[gui] RECORDED $name -> $Expected (look at it before you trust it)"
    exit 0
}
if (-not (Test-Path $Expected)) {
    Write-Host "[gui] FAIL $name -- no expected image. Run with -Accept to record: $Expected"
    exit 1
}

Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::new((Resolve-Path $actual).Path)
$e = [System.Drawing.Bitmap]::new((Resolve-Path $Expected).Path)
try {
    if ($a.Width -ne $e.Width -or $a.Height -ne $e.Height) {
        Write-Host ("[gui] FAIL {0} -- size {1}x{2}, expected {3}x{4}" -f $name, $a.Width, $a.Height, $e.Width, $e.Height)
        exit 1
    }
    $diff = 0
    for ($y = 0; $y -lt $a.Height; $y++) {
        for ($x = 0; $x -lt $a.Width; $x++) {
            if ($a.GetPixel($x, $y).ToArgb() -ne $e.GetPixel($x, $y).ToArgb()) { $diff++ }
        }
    }
} finally {
    $a.Dispose(); $e.Dispose()
}

if ($diff -gt $Tolerance) {
    Write-Host ("[gui] FAIL {0} -- {1} pixels differ (tolerance {2}). Actual: {3}" -f $name, $diff, $Tolerance, $actual)
    exit 1
}
if (-not $KeepArtifacts) { Remove-Item $mouseFile, $actual -Force -ErrorAction SilentlyContinue }
Write-Host ("[gui] PASS {0} ({1} pixels differ)" -f $name, $diff)
exit 0
