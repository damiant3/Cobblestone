# qr-decode-test.ps1 -- the QR telemetry channel, decoder end.
#
# GopQr paints findings on the GOP framebuffer and tools/qr-read.ps1 turns a
# photograph back into the bytes. Only the encoder had a test
# (codex/test/qr-encode.codex). The decoder is the half a hardware sitting
# actually depends on -- it is what R-1 of TheSilentKeyboard.md promises --
# and it had nothing pointed at it.
#
# It was broken. Get-Otsu kept the FIRST threshold achieving maximum
# between-class variance, and a screenshot is perfectly bimodal: every sample
# is 0 or 255, so every t in 0..254 scores identically and the answer was 0.
# The module test `gray < 0` is never true, every module read light, and a
# pixel-perfect capture decoded as nothing. A blurry photograph of the same
# screen decoded fine, because noise populates the valley and drags the
# maximum somewhere sane -- so the only validation ever run (simulated
# hand-held photos) could not see it.
#
# Both directions are therefore checked here, and the crisp one is the one
# that regressed:
#   1. the fixture as rendered  (build/fixtures/qr-kbddiag.png)
#   2. the same fixture put through a deterministic simulated phone photo
#      (perspective skew, glare, defocus, sensor noise, JPEG)
#
#   pwsh build/qr-decode-test.ps1
[CmdletBinding()]
param([string]$Fixture = 'build/fixtures/qr-kbddiag.png')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reader = Join-Path $repo 'tools/qr-read.ps1'
$fix = Join-Path $repo $Fixture
if (-not (Test-Path $reader)) { Write-Host "qr-decode-test: no decoder at $reader"; exit 1 }
if (-not (Test-Path $fix))    { Write-Host "qr-decode-test: no fixture at $fix"; exit 1 }

$EXPECTED = @(
    'KBDDIAG v8'
    'uk-ok=y slot=2 dci=3 speed=1'
    'EPINT=0 EP0=0 OTH=1 LATCH=0'
    'code=01 resid=0 ctl=02008001'
    'trb=b7f13e80 ring=b7f13e80'
    'REPORT 00 00 00 00 00 00 00 00'
    'SCANS=0 last=00 st=1'
    'PHASE=1 released=0 reclaim=0 ps2=0 ps2last=00'
    'reacq kbd=0 disk=0 mount=0'
)

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("qrdec-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null

# A hand-held photograph of a lit screen, made reproducible: fixed seed, fixed
# geometry. This is the shape the sitting actually produces.
function New-SimulatedPhoto([string]$In, [string]$Out, [int]$Quality, [int]$Seed) {
    $src = [System.Drawing.Bitmap]::FromFile($In)
    $W = $src.Width; $H = $src.Height
    $skew = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($skew)
    $g.Clear([System.Drawing.Color]::FromArgb(12, 12, 14))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    # a parallelogram destination: the camera was not square to the glass
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([float](0.045 * $W)), ([float](0.030 * $H)))
        (New-Object System.Drawing.PointF ([float](0.965 * $W)), ([float](0.012 * $H)))
        (New-Object System.Drawing.PointF ([float](0.020 * $W)), ([float](0.972 * $H)))
    )
    $g.DrawImage($src, $pts)
    $g.Dispose(); $src.Dispose()

    # defocus, as a downscale/upscale round trip
    $sw = [int]($W * 0.62); $sh = [int]($H * 0.62)
    $small = New-Object System.Drawing.Bitmap $sw, $sh
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
    $g2.DrawImage($skew, 0, 0, $sw, $sh); $g2.Dispose(); $skew.Dispose()
    $blur = New-Object System.Drawing.Bitmap $W, $H
    $g3 = [System.Drawing.Graphics]::FromImage($blur)
    $g3.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBilinear
    $g3.DrawImage($small, 0, 0, $W, $H); $g3.Dispose(); $small.Dispose()

    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    $bits = $blur.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
                           [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $stride = $bits.Stride
    $buf = New-Object byte[] ($stride * $H)
    [System.Runtime.InteropServices.Marshal]::Copy($bits.Scan0, $buf, 0, $buf.Length)
    $rnd = New-Object System.Random $Seed
    for ($y = 0; $y -lt $H; $y++) {
        $ro = $y * $stride
        for ($x = 0; $x -lt $W; $x++) {
            $o = $ro + $x * 3
            $dx = ($x / [double]$W) - 0.72; $dy = ($y / [double]$H) - 0.28
            $glare = 46.0 * [math]::Exp(-(($dx * $dx + $dy * $dy) / 0.055))
            $ex = ($x / [double]$W) - 0.5; $ey = ($y / [double]$H) - 0.5
            $vig = 1.0 - 0.22 * [math]::Sqrt($ex * $ex + $ey * $ey)
            for ($k = 0; $k -lt 3; $k++) {
                $v = ([int]$buf[$o + $k]) * $vig + $glare + ($rnd.NextDouble() - 0.5) * 17.0
                if ($v -lt 0) { $v = 0 }; if ($v -gt 255) { $v = 255 }
                $buf[$o + $k] = [byte][int]$v
            }
        }
    }
    [System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $bits.Scan0, $buf.Length)
    $blur.UnlockBits($bits)
    $enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $ps = New-Object System.Drawing.Imaging.EncoderParameters 1
    $ps.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), ([long]$Quality)
    $blur.Save($Out, $enc, $ps)
    $blur.Dispose()
}

function Test-Decode([string]$Label, [string]$Image) {
    $save = Join-Path $tmp ((Split-Path $Image -Leaf) + '.txt')
    & pwsh -NoProfile -File $reader -Path $Image -Save $save 2>&1 | Out-Null
    if (-not (Test-Path $save)) {
        Write-Host "  $Label : FAIL -- decoded nothing"
        return $false
    }
    $got = @([IO.File]::ReadAllText($save) -split "`r?`n" | ForEach-Object { $_.TrimEnd() } |
             Where-Object { $_ -ne '' })
    if ($got.Count -ne $EXPECTED.Count) {
        Write-Host "  $Label : FAIL -- $($got.Count) lines, expected $($EXPECTED.Count)"
        $got | ForEach-Object { Write-Host "      got: $_" }
        return $false
    }
    for ($i = 0; $i -lt $EXPECTED.Count; $i++) {
        if ($got[$i] -cne $EXPECTED[$i]) {
            Write-Host "  $Label : FAIL -- line $($i+1)"
            Write-Host "      expected: $($EXPECTED[$i])"
            Write-Host "      got     : $($got[$i])"
            return $false
        }
    }
    Write-Host "  $Label : ok ($($got.Count) lines)"
    return $true
}

Write-Host 'qr-decode-test: the QR telemetry channel'
$fail = 0

# 1. as rendered. This is the direction that silently broke.
if (-not (Test-Decode 'as rendered      ' $fix)) { $fail++ }

# 2. as photographed off the glass. This is the direction the sitting uses.
$photo = Join-Path $tmp 'photo.jpg'
New-SimulatedPhoto $fix $photo 70 12345
if (-not (Test-Decode 'simulated photo  ' $photo)) { $fail++ }

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue

if ($fail -gt 0) { Write-Host "qr-decode-test: $fail FAILED"; exit 1 }
Write-Host 'qr-decode-test: all checks passed'
