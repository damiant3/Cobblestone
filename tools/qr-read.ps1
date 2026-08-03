<#
.SYNOPSIS
  Read the QR telemetry Codex paints on the glass, from a photograph.

.DESCRIPTION
  The bare-metal boot has no serial port and, on the machines this arc
  exists for, no working storage either -- so it reports by rendering its
  findings as QR codes on the GOP framebuffer (apps/works/GopQr.codex).
  A human photographs the screen; this script turns the photograph back
  into the exact bytes the machine emitted. No transcription, no
  re-typing of hex off a monitor, and nothing to go wrong between the
  guest's memory and the report.

  This is the decoder half of GopQr and is written as its exact inverse:
  version 5, error level L, mask 0, one Reed-Solomon block of 108 data
  codewords plus 26 of parity. Reed-Solomon correction is real -- a
  hand-held photo of a lit screen loses modules to glare and blur, and
  up to 13 corrupt codewords per code are recovered rather than reported
  as failure.

  Only the image decode is borrowed, from Windows itself (System.Drawing);
  the QR decode is ours, and matches the encoder module for module.

.PARAMETER Path
  The photograph (or a screenshot). Any format Windows can open.

.PARAMETER Save
  Optional. Write the reassembled payload to this file.

.PARAMETER Debug
  Report every finder pattern and candidate code found.

.EXAMPLE
  pwsh tools/qr-read.ps1 -Path D:\20260713_131201.jpg
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Path,
  [string]$Save = '',
  [switch]$ShowDebug
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ---------------------------------------------------------------- image

if (-not (Test-Path $Path)) { throw "no such image: $Path" }
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Path).Path)
$W = $bmp.Width; $H = $bmp.Height

# One locked pass into a byte[] of luma. GetPixel per pixel on a 12 MP
# photo is minutes; LockBits is milliseconds.
$rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
$bits = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                      [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$stride = $bits.Stride
$raw = New-Object byte[] ($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($bits.Scan0, $raw, 0, $raw.Length)
$bmp.UnlockBits($bits)
$bmp.Dispose()

$gray = New-Object byte[] ($W * $H)
for ($y = 0; $y -lt $H; $y++) {
  $ro = $y * $stride
  $go = $y * $W
  for ($x = 0; $x -lt $W; $x++) {
    $o = $ro + $x * 3
    # BGR, integer luma
    $gray[$go + $x] = [byte](([int]$raw[$o + 2] * 77 + [int]$raw[$o + 1] * 151 + [int]$raw[$o] * 28) -shr 8)
  }
}
$raw = $null
Write-Host "[qr] $Path  ${W}x${H}"

function Get-Gray([int]$x, [int]$y) {
  if ($x -lt 0 -or $y -lt 0 -or $x -ge $W -or $y -ge $H) { return 255 }
  return [int]$gray[$y * $W + $x]
}

# Otsu over the whole frame gives one global cut. The screen is by far the
# brightest thing in these photographs, so a global cut separates the lit
# panel from the room, and inside the panel the codes are printed white-on-
# black at full contrast. Local sampling refines it per code below.
function Get-Otsu([int[]]$hist, [int]$total) {
  $sum = 0.0
  for ($i = 0; $i -lt 256; $i++) { $sum += $i * $hist[$i] }
  # Keep the RANGE of thresholds that achieve the maximum, and answer its
  # midpoint. A screenshot is perfectly bimodal -- every sample is 0 or 255
  # and nothing lands in between -- so every t in 0..254 splits the histogram
  # the same way and scores identically. Keeping the first maximum (a strict
  # -gt and nothing else) answered 0, the module test `gray < 0` was never
  # true, every module read light, and a pixel-perfect capture decoded as
  # nothing while a blurry photo of the same screen decoded fine: noise
  # populates the valley and drags the maximum to a sane place. The midpoint
  # is that sane place by construction, and on a photo the maximum is a
  # single sharp t, where lo = hi and this is what it always was.
  $sumB = 0.0; $wB = 0; $best = -1.0; $lo = 128; $hi = 128
  for ($t = 0; $t -lt 256; $t++) {
    $wB += $hist[$t]
    if ($wB -eq 0) { continue }
    $wF = $total - $wB
    if ($wF -eq 0) { break }
    $sumB += $t * $hist[$t]
    $mB = $sumB / $wB
    $mF = ($sum - $sumB) / $wF
    $between = [double]$wB * $wF * ($mB - $mF) * ($mB - $mF)
    if ($between -gt $best) { $best = $between; $lo = $t; $hi = $t }
    elseif ($between -eq $best) { $hi = $t }
  }
  return [int](($lo + $hi) / 2)
}

$hist = New-Object int[] 256
foreach ($g in $gray) { $hist[$g]++ }
$THR = Get-Otsu $hist ($W * $H)
Write-Host "[qr] global threshold $THR"

function Is-Dark([int]$x, [int]$y, [int]$t) { return (Get-Gray $x $y) -lt $t }

# ------------------------------------------------------- finder patterns
#
# A finder is the 1:1:3:1:1 dark/light/dark/light/dark run that appears on
# every line crossing it. Scan rows for the ratio, then confirm the same
# ratio vertically through the candidate centre; a run that survives both
# is a finder centre and its module size falls out of the run widths.

function Test-Ratio([int[]]$run) {
  $total = 0; foreach ($r in $run) { $total += $r }
  if ($total -lt 7) { return $false }
  $m = $total / 7.0
  $tol = $m * 0.6
  if ([math]::Abs($run[0] - $m) -gt $tol) { return $false }
  if ([math]::Abs($run[1] - $m) -gt $tol) { return $false }
  if ([math]::Abs($run[2] - 3 * $m) -gt 3 * $tol) { return $false }
  if ([math]::Abs($run[3] - $m) -gt $tol) { return $false }
  if ([math]::Abs($run[4] - $m) -gt $tol) { return $false }
  return $true
}

# A finder must show the SAME 1:1:3:1:1 down its centre as across it. The
# data region throws false 1:1:3:1:1 hits on any single scan line -- it is
# full of them -- so a candidate that is not a finder in both directions is
# not a finder. Returns the vertical centre, or -1.

function Measure-Axis([int]$cx, [int]$cy, [int]$dx, [int]$dy, [int]$t) {
  # five runs centred on (cx,cy): [-2 -1 CENTRE +1 +2]
  $r = New-Object int[] 5
  $r[2] = 1
  $x = $cx + $dx; $y = $cy + $dy
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and (Is-Dark $x $y $t)) { $r[2]++; $x += $dx; $y += $dy }
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and -not (Is-Dark $x $y $t)) { $r[3]++; $x += $dx; $y += $dy }
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and (Is-Dark $x $y $t)) { $r[4]++; $x += $dx; $y += $dy }
  $x = $cx - $dx; $y = $cy - $dy
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and (Is-Dark $x $y $t)) { $r[2]++; $x -= $dx; $y -= $dy }
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and -not (Is-Dark $x $y $t)) { $r[1]++; $x -= $dx; $y -= $dy }
  while ($x -ge 0 -and $y -ge 0 -and $x -lt $W -and $y -lt $H -and (Is-Dark $x $y $t)) { $r[0]++; $x -= $dx; $y -= $dy }
  return $r
}

function Test-Vertical([int]$cx, [int]$cy, [double]$mod, [int]$t) {
  if (-not (Is-Dark $cx $cy $t)) { return -1 }
  $v = Measure-Axis $cx $cy 0 1 $t
  if (-not (Test-Ratio $v)) { return -1 }
  $vm = 0; foreach ($q in $v) { $vm += $q }
  $vm = $vm / 7.0
  if ([math]::Abs($vm - $mod) -gt $mod * 0.4) { return -1 }
  # the diagonal must agree too -- this is what kills the data-region hits
  $d = Measure-Axis $cx $cy 1 1 $t
  if (-not (Test-Ratio $d)) { return -1 }
  return $cy
}

$cands = New-Object System.Collections.ArrayList
$step = [math]::Max(1, [int]($H / 1200))   # subsample rows on huge photos
for ($y = 0; $y -lt $H; $y += $step) {
  # decompose the row into runs of one colour, then slide a five-run window
  # looking for dark,light,dark,light,dark in 1:1:3:1:1. Explicit runs beat
  # a rolling window: the ratios are checked against real boundaries, so a
  # finder is measured rather than guessed at.
  $starts = New-Object System.Collections.ArrayList
  $lens = New-Object System.Collections.ArrayList
  $darks = New-Object System.Collections.ArrayList
  $cur = Is-Dark 0 $y $THR
  $st = 0
  for ($x = 1; $x -le $W; $x++) {
    $d = if ($x -lt $W) { Is-Dark $x $y $THR } else { -not $cur }
    if ($d -ne $cur) {
      [void]$starts.Add($st); [void]$lens.Add($x - $st); [void]$darks.Add($cur)
      $cur = $d; $st = $x
    }
  }
  for ($i = 0; $i + 4 -lt $lens.Count; $i++) {
    if (-not $darks[$i]) { continue }
    if ($darks[$i + 1] -or -not $darks[$i + 2] -or $darks[$i + 3] -or -not $darks[$i + 4]) { continue }
    $run = @($lens[$i], $lens[$i + 1], $lens[$i + 2], $lens[$i + 3], $lens[$i + 4])
    if (-not (Test-Ratio $run)) { continue }
    $total = 0; foreach ($r in $run) { $total += $r }
    $mod = $total / 7.0
    $cx = [int]($starts[$i + 2] + $lens[$i + 2] / 2)
    if ((Test-Vertical $cx $y $mod $THR) -ge 0) {
      [void]$cands.Add([pscustomobject]@{ X = $cx; Y = $y; M = $mod })
    }
  }
}

# cluster the row hits into one point per physical finder
$fs = New-Object System.Collections.ArrayList
foreach ($c in $cands) {
  $hit = $null
  foreach ($f in $fs) {
    if ([math]::Abs($f.X - $c.X) -lt $c.M * 2 -and [math]::Abs($f.Y - $c.Y) -lt $c.M * 3) { $hit = $f; break }
  }
  if ($null -eq $hit) {
    [void]$fs.Add([pscustomobject]@{ X = [double]$c.X; Y = [double]$c.Y; M = [double]$c.M; N = 1 })
  } else {
    $hit.X = ($hit.X * $hit.N + $c.X) / ($hit.N + 1)
    $hit.Y = ($hit.Y * $hit.N + $c.Y) / ($hit.N + 1)
    $hit.M = ($hit.M * $hit.N + $c.M) / ($hit.N + 1)
    $hit.N++
  }
}
# a real finder is seen on many rows; a one-row coincidence is noise
$fs = @($fs | Where-Object { $_.N -ge 2 })
Write-Host "[qr] finder patterns: $($fs.Count)"
if ($ShowDebug) { $fs | ForEach-Object { "      ({0,6:N0},{1,6:N0}) module {2,5:N2}  rows {3}" -f $_.X, $_.Y, $_.M, $_.N } }

# ---------------------------------------------------- group into codes
#
# Three finders make a code. The two furthest apart are the diagonal; the
# third is the top-left corner. Modules must agree and the diagonal must
# be sqrt(2)*30 modules for a version-5 symbol.

function Dist($a, $b) { return [math]::Sqrt(($a.X - $b.X) * ($a.X - $b.X) + ($a.Y - $b.Y) * ($a.Y - $b.Y)) }

$codes = New-Object System.Collections.ArrayList
$n = $fs.Count
for ($i = 0; $i -lt $n; $i++) {
  for ($j = $i + 1; $j -lt $n; $j++) {
    for ($k = $j + 1; $k -lt $n; $k++) {
      $a = $fs[$i]; $b = $fs[$j]; $c = $fs[$k]
      $mm = ($a.M + $b.M + $c.M) / 3.0
      if ([math]::Abs($a.M - $mm) -gt $mm * 0.35) { continue }
      if ([math]::Abs($b.M - $mm) -gt $mm * 0.35) { continue }
      if ([math]::Abs($c.M - $mm) -gt $mm * 0.35) { continue }
      $ab = Dist $a $b; $ac = Dist $a $c; $bc = Dist $b $c
      # the corner is opposite the longest side
      if ($ab -ge $ac -and $ab -ge $bc) { $tl = $c; $p = $a; $q = $b; $diag = $ab }
      elseif ($ac -ge $ab -and $ac -ge $bc) { $tl = $b; $p = $a; $q = $c; $diag = $ac }
      else { $tl = $a; $p = $b; $q = $c; $diag = $bc }
      $side = 30.0 * $mm
      if ([math]::Abs($diag - $side * 1.4142) -gt $side * 0.30) { continue }
      if ([math]::Abs((Dist $tl $p) - $side) -gt $side * 0.25) { continue }
      if ([math]::Abs((Dist $tl $q) - $side) -gt $side * 0.25) { continue }
      # orient: cross product decides which of p,q is the column axis (TR)
      $vx1 = $p.X - $tl.X; $vy1 = $p.Y - $tl.Y
      $vx2 = $q.X - $tl.X; $vy2 = $q.Y - $tl.Y
      $cross = $vx1 * $vy2 - $vy1 * $vx2
      if ($cross -gt 0) { $tr = $p; $bl = $q } else { $tr = $q; $bl = $p }
      [void]$codes.Add([pscustomobject]@{ TL = $tl; TR = $tr; BL = $bl; M = $mm })
    }
  }
}
Write-Host "[qr] candidate codes: $($codes.Count)"

# ------------------------------------------------------ sample a symbol

$SIZE = 37

function Sample-Matrix($code) {
  $tl = $code.TL; $tr = $code.TR; $bl = $code.BL
  # module (r,c) centre in continuous module coords is (r+0.5, c+0.5);
  # the finder centres sit at 3.5 and 33.5, thirty modules apart.
  $m = New-Object 'int[,]' $SIZE, $SIZE
  # local threshold from the symbol's own pixels beats the global cut
  $lh = New-Object int[] 256
  for ($r = 0; $r -lt $SIZE; $r++) {
    for ($c = 0; $c -lt $SIZE; $c++) {
      $u = ($c + 0.5 - 3.5) / 30.0
      $v = ($r + 0.5 - 3.5) / 30.0
      $x = [int][math]::Round($tl.X + $u * ($tr.X - $tl.X) + $v * ($bl.X - $tl.X))
      $y = [int][math]::Round($tl.Y + $u * ($tr.Y - $tl.Y) + $v * ($bl.Y - $tl.Y))
      $lh[(Get-Gray $x $y)]++
    }
  }
  $lt = Get-Otsu $lh ($SIZE * $SIZE)
  for ($r = 0; $r -lt $SIZE; $r++) {
    for ($c = 0; $c -lt $SIZE; $c++) {
      $u = ($c + 0.5 - 3.5) / 30.0
      $v = ($r + 0.5 - 3.5) / 30.0
      $x = [int][math]::Round($tl.X + $u * ($tr.X - $tl.X) + $v * ($bl.X - $tl.X))
      $y = [int][math]::Round($tl.Y + $u * ($tr.Y - $tl.Y) + $v * ($bl.Y - $tl.Y))
      # a 3x3 median-ish vote: one blurred pixel must not decide a module
      $dark = 0
      for ($dy = -1; $dy -le 1; $dy++) {
        for ($dx = -1; $dx -le 1; $dx++) {
          if ((Get-Gray ($x + $dx) ($y + $dy)) -lt $lt) { $dark++ }
        }
      }
      $m[$r, $c] = [int]($dark -ge 5)
    }
  }
  # the comma is load-bearing: PowerShell unrolls an array on output, and a
  # rank-2 array unrolls to 1369 loose integers that still INDEX without
  # complaint -- $m[$r,$c] on the flattened form quietly returns a two-element
  # slice instead of a module. Wrap it so the matrix arrives as a matrix.
  return , $m
}

# ------------------------------------------- reserved modules (mirror of
# GopQr's qr-functions: the decoder must skip exactly what the encoder did)

$FN = New-Object 'int[,]' $SIZE, $SIZE
function Mark-Rect([int]$r0, [int]$c0, [int]$h, [int]$w) {
  for ($r = $r0; $r -lt $r0 + $h; $r++) { for ($c = $c0; $c -lt $c0 + $w; $c++) { $FN[$r, $c] = 1 } }
}
Mark-Rect 0 0 9 9
Mark-Rect 0 29 9 8
Mark-Rect 29 0 8 9
Mark-Rect 28 28 5 5
for ($i = 8; $i -le 28; $i++) { $FN[6, $i] = 1; $FN[$i, 6] = 1 }

# ------------------------------------------------------- GF(256), RS

$EXP = New-Object int[] 512
$LOG = New-Object int[] 256
$xv = 1
for ($i = 0; $i -lt 255; $i++) {
  $EXP[$i] = $xv
  $LOG[$xv] = $i
  $xv = $xv -shl 1
  if ($xv -band 0x100) { $xv = $xv -bxor 0x11D }
}
for ($i = 255; $i -lt 512; $i++) { $EXP[$i] = $EXP[$i - 255] }

function GfMul([int]$a, [int]$b) {
  if ($a -eq 0 -or $b -eq 0) { return 0 }
  return $EXP[$LOG[$a] + $LOG[$b]]
}
function GfInv([int]$a) { return $EXP[255 - $LOG[$a]] }

$NECC = 26

# Syndromes, Berlekamp-Massey, Chien search, Forney. A photograph of a lit
# screen loses modules to glare; this is what makes the difference between
# "decoded" and "try again, walk back to the machine".
# One convention throughout, so the indices cannot drift: every polynomial
# is an int[] in ASCENDING degree, p[k] being the coefficient of x^k.
# Codeword i of the received block carries x^(n-1-i), so position i has
# locator alpha^(n-1-i).

function Poly-Eval([int[]]$p, [int]$x) {
  $acc = 0
  for ($k = $p.Count - 1; $k -ge 0; $k--) { $acc = (GfMul $acc $x) -bxor $p[$k] }
  return $acc
}

# GopQr builds its generator as the product of (x - a^i) for i in 0..25, so
# the first root is alpha^0, not alpha^1. Syndromes must be evaluated at the
# SAME roots the encoder used, and Forney's magnitude then carries a single
# factor of X. Get this off by one root and every syndrome is nonzero, every
# correction is garbage, and a perfectly sampled symbol reports as unreadable.
function Rs-Syndromes([int[]]$cw) {
  $n = $cw.Count
  $s = New-Object int[] $NECC
  for ($i = 0; $i -lt $NECC; $i++) {
    $a = $EXP[$i % 255]
    $acc = 0
    for ($j = 0; $j -lt $n; $j++) { $acc = (GfMul $acc $a) -bxor $cw[$j] }
    $s[$i] = $acc
  }
  return $s
}

function Rs-Correct([int[]]$cwIn) {
  $cw = [int[]]$cwIn.Clone()
  $n = $cw.Count
  $synd = Rs-Syndromes $cw
  $clean = $true
  foreach ($s in $synd) { if ($s -ne 0) { $clean = $false; break } }
  if ($clean) { return $cw }

  # Berlekamp-Massey (ascending degree; lam[0] = 1 always)
  $lam = New-Object int[] 1; $lam[0] = 1
  $prev = New-Object int[] 1; $prev[0] = 1
  $L = 0
  $m = 1
  $b = 1
  for ($nn = 0; $nn -lt $NECC; $nn++) {
    $d = $synd[$nn]
    for ($i = 1; $i -le $L; $i++) {
      if ($i -lt $lam.Count -and ($nn - $i) -ge 0) {
        $d = $d -bxor (GfMul $lam[$i] $synd[$nn - $i])
      }
    }
    if ($d -eq 0) { $m++ ; continue }

    # T(x) = lam(x) - (d/b) x^m prev(x)
    $scale = GfMul $d (GfInv $b)
    $len = [math]::Max($lam.Count, $prev.Count + $m)
    $T = New-Object int[] $len
    for ($i = 0; $i -lt $lam.Count; $i++) { $T[$i] = $lam[$i] }
    for ($i = 0; $i -lt $prev.Count; $i++) {
      $T[$i + $m] = $T[$i + $m] -bxor (GfMul $scale $prev[$i])
    }
    if (2 * $L -le $nn) {
      $prev = [int[]]$lam.Clone()
      $lam = $T
      $L = $nn + 1 - $L
      $b = $d
      $m = 1
    } else {
      $lam = $T
      $m++
    }
  }

  if ($L -le 0 -or $L -gt [int]($NECC / 2)) { return $null }

  # Chien search: position i is in error when lam(alpha^-(n-1-i)) = 0
  $pos = New-Object System.Collections.ArrayList
  for ($i = 0; $i -lt $n; $i++) {
    $locExp = ($n - 1 - $i) % 255
    $xinv = $EXP[(255 - $locExp) % 255]
    if ((Poly-Eval $lam $xinv) -eq 0) { [void]$pos.Add($i) }
  }
  if ($pos.Count -ne $L) { return $null }

  # omega(x) = [ S(x) * lam(x) ] mod x^NECC
  $omega = New-Object int[] $NECC
  for ($i = 0; $i -lt $NECC; $i++) {
    $acc = 0
    for ($j = 0; $j -le $i; $j++) {
      if ($j -lt $lam.Count) { $acc = $acc -bxor (GfMul $synd[$i - $j] $lam[$j]) }
    }
    $omega[$i] = $acc
  }

  # lambda'(x): formal derivative over GF(2) keeps the odd-degree terms
  $dlen = [math]::Max(1, $lam.Count - 1)
  $dlam = New-Object int[] $dlen
  for ($i = 1; $i -lt $lam.Count; $i++) {
    if (($i % 2) -eq 1) { $dlam[$i - 1] = $lam[$i] } else { $dlam[$i - 1] = 0 }
  }

  foreach ($p in $pos) {
    $locExp = ($n - 1 - $p) % 255
    $x = $EXP[$locExp]
    $xinv = $EXP[(255 - $locExp) % 255]
    $num = Poly-Eval $omega $xinv
    $den = Poly-Eval $dlam $xinv
    if ($den -eq 0) { return $null }
    # e = x * omega(x^-1) / lam'(x^-1)   (b = 1 convention: generator roots alpha^1..)
    $mag = GfMul $x (GfMul $num (GfInv $den))
    $cw[$p] = $cw[$p] -bxor $mag
  }

  $chk = Rs-Syndromes $cw
  foreach ($s in $chk) { if ($s -ne 0) { return $null } }
  return $cw
}

# ------------------------------------------------ read one symbol's bytes

function Decode-Matrix($mat) {
  # unmask (mask 0: row+col even) and walk the zigzag exactly as qr-place
  $bits = New-Object System.Collections.ArrayList
  $col = 36
  $up = 1
  while ($col -ge 1) {
    for ($i = 0; $i -lt $SIZE; $i++) {
      if ($up -eq 1) { $row = $SIZE - 1 - $i } else { $row = $i }
      $c = $col
      while ($c -ge $col - 1) {
        if ($FN[$row, $c] -eq 0) {
          $v = $mat[$row, $c]
          if ((($row + $c) % 2) -eq 0) { $v = $v -bxor 1 }
          [void]$bits.Add($v)
        }
        $c--
      }
    }
    if ($col -eq 8) { $col = 5 } else { $col = $col - 2 }
    $up = 1 - $up
  }
  $total = 134
  if ($ShowDebug) { Write-Host "[dbg] bits=$($bits.Count) need=$($total*8)" }
  if ($bits.Count -lt $total * 8) { return $null }
  $cw = New-Object int[] $total
  for ($i = 0; $i -lt $total; $i++) {
    $b = 0
    for ($j = 0; $j -lt 8; $j++) { $b = ($b -shl 1) -bor $bits[$i * 8 + $j] }
    $cw[$i] = $b
  }
  if ($ShowDebug) { Write-Host "[dbg] codewords built, entering RS" }
  $fixed = Rs-Correct $cw
  if ($ShowDebug) { Write-Host "[dbg] RS returned $($null -ne $fixed)" }
  if ($null -eq $fixed) { return $null }

  # byte mode: 4 bits mode, 8 bits length, then the bytes
  $data = $fixed[0..107]
  $mode = ($data[0] -shr 4) -band 0xF
  if ($mode -ne 4) { return $null }
  $len = (($data[0] -band 0xF) -shl 4) -bor (($data[1] -shr 4) -band 0xF)
  if ($len -lt 1 -or $len -gt 106) { return $null }
  $out = New-Object byte[] $len
  for ($i = 0; $i -lt $len; $i++) {
    $hi = ($data[1 + $i] -band 0xF)
    $lo = (($data[2 + $i] -shr 4) -band 0xF)
    $out[$i] = [byte]((($hi -shl 4) -bor $lo) -band 0xFF)
  }
  return [System.Text.Encoding]::ASCII.GetString($out)
}

# --------------------------------------------------------------- decode

$payloads = @{}
foreach ($code in $codes) {
  if ($ShowDebug) { Write-Host "[dbg] sampling TL=$($code.TL.X),$($code.TL.Y) TR=$($code.TR.X) BL=$($code.BL.X) M=$($code.M)" }
  $m = Sample-Matrix $code
  if ($ShowDebug) {
    # the timing patterns alternate from module 8 to 28; if the grid is
    # aligned they read exactly 1,0,1,0,... Anything else is a geometry fault
    # and no amount of Reed-Solomon will save it.
    $tr = ''; $tc = ''; $okr = 0
    for ($i = 8; $i -le 28; $i++) {
      $tr += $m[6, $i]; $tc += $m[$i, 6]
      if ($m[6, $i] -eq (1 - ($i % 2))) { $okr++ }
    }
    Write-Host "[dbg] timing row  $tr"
    Write-Host "[dbg] timing col  $tc"
    Write-Host "[dbg] timing row correct $okr/21"
  }
  $t = Decode-Matrix $m
  if ($ShowDebug) { Write-Host "[dbg] decoded=$($null -ne $t)" }
  if ($null -ne $t) {
    if ($ShowDebug) { Write-Host "[qr] decoded at TL ($([int]$code.TL.X),$([int]$code.TL.Y))" }
    $payloads[$t] = $true
  }
}

$texts = @($payloads.Keys)
Write-Host "[qr] decoded $($texts.Count) code(s)"
if ($texts.Count -eq 0) {
  Write-Host "[qr] nothing decoded. Re-shoot straighter, fill the frame with the codes, and kill the glare."
  exit 1
}

# GopQr chunks a long report as "i/n;<payload>" -- reassemble in order.
$chunks = @{}
$plain = @()
foreach ($t in $texts) {
  if ($t -match '^(\d+)/(\d+);(?s)(.*)$') { $chunks[[int]$matches[1]] = $matches[3] }
  else { $plain += $t }
}
$body = ''
if ($chunks.Count -gt 0) {
  foreach ($k in ($chunks.Keys | Sort-Object)) { $body += $chunks[$k] }
  $have = $chunks.Count
  $want = 0
  foreach ($t in $texts) { if ($t -match '^\d+/(\d+);') { $want = [int]$matches[1] } }
  if ($want -gt 0 -and $have -lt $want) {
    Write-Host "[qr] WARNING: $have of $want chunks -- the report is INCOMPLETE" -ForegroundColor Yellow
  }
}
foreach ($p in $plain) { $body += $p }

Write-Host ''
Write-Host '================ REPORT ================'
Write-Host $body
Write-Host '========================================'

if ($Save -ne '') {
  Set-Content -Path $Save -Value $body -Encoding ascii
  Write-Host "[qr] saved -> $Save"
}

