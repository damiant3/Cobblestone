# bmpdiff.ps1 -- what CHANGED between two captured frames, by row.
#
# `Get-FileHash` says two frames differ. This says where, which is the
# difference between a finding and a shrug. It reports the count of differing
# pixels, the first and last differing row, a census of every differing row,
# and a few sample pixels with both colours.
#
# THE ROW CENSUS IS THE POINT and it is what a whole-image count cannot do. A
# defect that paints outside its box shows up as a contiguous band of rows with
# a large count; a fill running past x = w and wrapping onto the NEXT row's
# left edge shows up as a long run of rows carrying two or four pixels each,
# which is invisible in a total and unmistakable in a census. That second one
# was found this way in the Browser and Review panes (main 17846) and by
# nothing else.
#
# BMP rows are stored bottom-up unless the height is negative, so y here is the
# SCREEN row from the top and the file offset is computed for that. Reading the
# census against a bottom-up file without this is how a defect at the top of
# the screen gets reported at the bottom.
#
# Exits 1 when the images differ, like diff. A caller that expects differences
# should ignore the code and read the census.
#
# USAGE
#   build/bmpdiff.ps1 -A before\browser.bmp -B after\browser.bmp
#   build/bmpdiff.ps1 -A s1\desk.bmp -B s2\desk.bmp -Scale 2
#
# -Scale N COMPARES A FRAME AGAINST THE SAME FRAME DRAWN N TIMES LARGER, which
# is what measures "identical modulo size" rather than asserting it. It reports
# TWO counts and they fail for different reasons, which is why they are
# separate:
#
#   subsample   A(x,y) against B(Nx,Ny). Nonzero means the two renders disagree
#               about what is drawn -- a layout divergence, not a scaling one.
#   block       every pixel of B's NxN block against that block's top-left.
#               Nonzero means the N-times render is not pixel replication: it
#               resolves detail the 1x render cannot carry. A proportional or
#               antialiased path lands here by construction, and that is a
#               property of the path rather than a defect.
#
# THE PAIR MUST SHARE A LOGICAL ROOM or neither count means anything. The desk
# lays out in w / ui-wscale by h / ui-wscale, so 1024x768 (scale 1) and
# 1600x900 (scale 2) lay out in 1024x768 and 800x450 and are not comparable at
# any scale. 800x450 and 1600x900 both lay out in 800x450.

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$A,
    [Parameter(Mandatory=$true)][string]$B,
    [int]$Samples = 6,
    [int]$Scale = 1,
    [int]$MinDelta = 1,
    [switch]$Brief
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Bmp([string]$path) {
    if (-not (Test-Path $path)) { throw "no such capture: $path" }
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b[0] -ne 0x42 -or $b[1] -ne 0x4D) { throw "$path is not a BMP" }
    $off = [BitConverter]::ToInt32($b, 10)
    $w   = [BitConverter]::ToInt32($b, 18)
    $h   = [BitConverter]::ToInt32($b, 22)
    $bpp = [BitConverter]::ToInt16($b, 28)
    $topDown = $false
    if ($h -lt 0) { $h = -$h; $topDown = $true }
    [pscustomobject]@{ Bytes=$b; Off=$off; W=$w; H=$h; Bpp=$bpp; TopDown=$topDown
                       Stride=[int]((($w * $bpp + 31) -band -32) / 8) }
}

$ia = Read-Bmp $A
$ib = Read-Bmp $B

Write-Output ("A: {0}x{1} {2}bpp stride {3} topdown {4}" -f $ia.W,$ia.H,$ia.Bpp,$ia.Stride,$ia.TopDown)
Write-Output ("B: {0}x{1} {2}bpp stride {3} topdown {4}" -f $ib.W,$ib.H,$ib.Bpp,$ib.Stride,$ib.TopDown)
function Row-Base($img, [int]$y) {
    $src = if ($img.TopDown) { $y } else { $img.H - 1 - $y }
    $img.Off + $src * $img.Stride
}

if ($Scale -gt 1) {
    if ($ia.Bpp -ne $ib.Bpp) { throw "bpp differs; nothing to compare" }
    if ($ib.W -ne $ia.W * $Scale -or $ib.H -ne $ia.H * $Scale) {
        throw ("B must be A scaled by {0}: A is {1}x{2}, B is {3}x{4}, expected {5}x{6}" -f `
               $Scale, $ia.W, $ia.H, $ib.W, $ib.H, ($ia.W * $Scale), ($ia.H * $Scale))
    }
    $w = $ia.W; $h = $ia.H; $bytes = [int]($ia.Bpp / 8)
    $ba = $ia.Bytes; $bb = $ib.Bytes
    $subRow = New-Object int[] $h
    $blkRow = New-Object int[] $h
    $sub = 0; $blk = 0
    $subSamples = New-Object System.Collections.Generic.List[string]
    $blkSamples = New-Object System.Collections.Generic.List[string]
    # A CENSUS OF MAGNITUDES, not a count of inequalities. A gradient evaluated
    # over h rows and over h*N rows rounds differently and lands one unit apart
    # on a large area; a widget drawn in the wrong place lands far apart on a
    # small one. Both are "disagree" and only the split tells them apart.
    $subMag = New-Object int[] 5
    $blkMag = New-Object int[] 5
    $magNames = @('1', '2-3', '4-7', '8-31', '32+')
    function Mag([int]$d) {
        if ($d -le 1) { return 0 } elseif ($d -le 3) { return 1 }
        elseif ($d -le 7) { return 2 } elseif ($d -le 31) { return 3 } else { return 4 }
    }

    for ($y = 0; $y -lt $h; $y++) {
        $ra = Row-Base $ia $y
        $brows = New-Object int[] $Scale
        for ($k = 0; $k -lt $Scale; $k++) { $brows[$k] = Row-Base $ib ($y * $Scale + $k) }
        for ($x = 0; $x -lt $w; $x++) {
            $pa = $ra + $x * $bytes
            $rep = $brows[0] + $x * $Scale * $bytes
            $md = 0
            for ($k = 0; $k -lt 3; $k++) {
                $d = [int]$ba[$pa+$k] - [int]$bb[$rep+$k]; if ($d -lt 0) { $d = -$d }
                if ($d -gt $md) { $md = $d }
            }
            if ($md -ge $MinDelta) {
                $sub++; $subRow[$y]++; $subMag[(Mag $md)]++
                if ($subSamples.Count -lt $Samples) {
                    $subSamples.Add(("  subsample y={0} x={1} A=#{2:X2}{3:X2}{4:X2} B=#{5:X2}{6:X2}{7:X2}" -f `
                        $y, $x, $ba[$pa+2], $ba[$pa+1], $ba[$pa], $bb[$rep+2], $bb[$rep+1], $bb[$rep]))
                }
            }
            for ($dy = 0; $dy -lt $Scale; $dy++) {
                for ($dx = 0; $dx -lt $Scale; $dx++) {
                    if ($dx -eq 0 -and $dy -eq 0) { continue }
                    $pb = $brows[$dy] + ($x * $Scale + $dx) * $bytes
                    $bd = 0
                    for ($k = 0; $k -lt 3; $k++) {
                        $d = [int]$bb[$pb+$k] - [int]$bb[$rep+$k]; if ($d -lt 0) { $d = -$d }
                        if ($d -gt $bd) { $bd = $d }
                    }
                    if ($bd -ge $MinDelta) {
                        $blk++; $blkRow[$y]++; $blkMag[(Mag $bd)]++
                        if ($blkSamples.Count -lt $Samples) {
                            $blkSamples.Add(("  block y={0} x={1} +{2},{3} rep=#{4:X2}{5:X2}{6:X2} got=#{7:X2}{8:X2}{9:X2}" -f `
                                $y, $x, $dx, $dy, $bb[$rep+2], $bb[$rep+1], $bb[$rep], $bb[$pb+2], $bb[$pb+1], $bb[$pb]))
                        }
                    }
                }
            }
        }
    }

    $cells = $w * $h
    if ($Brief) {
        Write-Output ("SCALE {0} {1} {2} {3}" -f $Scale, $sub, $blk, $cells)
        if ($sub -eq 0 -and $blk -eq 0) { exit 0 }
        exit 1
    }
    Write-Output ""
    Write-Output ("scale {0}: A {1}x{2} against B {3}x{4}, {5} logical cells, counting max channel delta >= {6}" -f $Scale,$w,$h,$ib.W,$ib.H,$cells,$MinDelta)
    Write-Output ("subsample: {0} of {1} cells disagree" -f $sub, $cells)
    Write-Output ("block:     {0} of {1} sub-pixels are not their block's colour" -f $blk, ($cells * ($Scale*$Scale - 1)))
    foreach ($pair in @(@{n='subsample';m=$subMag}, @{n='block';m=$blkMag})) {
        $parts = @()
        for ($i = 0; $i -lt 5; $i++) { if ($pair.m[$i]) { $parts += ("{0}:{1}" -f $magNames[$i], $pair.m[$i]) } }
        if ($parts.Count) { Write-Output ("{0,-10} by max channel delta -- {1}" -f $pair.n, ($parts -join '  ')) }
    }

    foreach ($pair in @(@{n='subsample';c=$subRow;t=$sub}, @{n='block';c=$blkRow;t=$blk})) {
        if ($pair.t -eq 0) { continue }
        $first = -1; $last = -1; $nrows = 0
        for ($y = 0; $y -lt $h; $y++) {
            if ($pair.c[$y] -gt 0) { if ($first -lt 0) { $first = $y }; $last = $y; $nrows++ }
        }
        Write-Output ("{0} rows: y {1}..{2} of {3}, {4} rows carry a difference" -f $pair.n,$first,$last,$h,$nrows)
        $line = ""
        for ($y = 0; $y -lt $h; $y++) {
            if ($pair.c[$y] -gt 0) {
                $line += ("{0}:{1}  " -f $y, $pair.c[$y])
                if ($line.Length -gt 100) { Write-Output ("  " + $line); $line = "" }
            }
        }
        if ($line) { Write-Output ("  " + $line) }
    }
    $subSamples | ForEach-Object { Write-Output $_ }
    $blkSamples | ForEach-Object { Write-Output $_ }

    if ($sub -eq 0 -and $blk -eq 0) { Write-Output "IDENTICAL MODULO SIZE"; exit 0 }
    exit 1
}

if ($ia.W -ne $ib.W -or $ia.H -ne $ib.H -or $ia.Bpp -ne $ib.Bpp) { throw "geometry differs; nothing to diff" }

$w = $ia.W; $h = $ia.H; $bytes = [int]($ia.Bpp / 8)
$rowCount = New-Object int[] $h
$total = 0
$sampleLines = New-Object System.Collections.Generic.List[string]

$ba = $ia.Bytes; $bb = $ib.Bytes
for ($y = 0; $y -lt $h; $y++) {
    $srcRow = if ($ia.TopDown) { $y } else { $h - 1 - $y }
    $ra = $ia.Off + $srcRow * $ia.Stride
    $rb = $ib.Off + $srcRow * $ib.Stride
    $n = 0
    for ($x = 0; $x -lt $w; $x++) {
        $pa = $ra + $x * $bytes
        $pb = $rb + $x * $bytes
        $same = $true
        for ($k = 0; $k -lt 3; $k++) { if ($ba[$pa+$k] -ne $bb[$pb+$k]) { $same = $false; break } }
        if (-not $same) {
            $n++
            if ($sampleLines.Count -lt $Samples) {
                $sampleLines.Add(("  sample y={0} x={1} A=#{2:X2}{3:X2}{4:X2} B=#{5:X2}{6:X2}{7:X2}" -f `
                    $y, $x, $ba[$pa+2], $ba[$pa+1], $ba[$pa], $bb[$pb+2], $bb[$pb+1], $bb[$pb]))
            }
        }
    }
    $rowCount[$y] = $n
    $total += $n
}

Write-Output ("differing pixels: {0} of {1}" -f $total, ($w * $h))
if ($total -eq 0) { Write-Output "IDENTICAL"; exit 0 }

$first = -1; $last = -1; $nrows = 0
for ($y = 0; $y -lt $h; $y++) {
    if ($rowCount[$y] -gt 0) { if ($first -lt 0) { $first = $y }; $last = $y; $nrows++ }
}
Write-Output ("differing rows: y {0}..{1}, {2} rows carry a difference" -f $first,$last,$nrows)

Write-Output "per-row census (every differing row, y:count):"
$line = ""
for ($y = 0; $y -lt $h; $y++) {
    if ($rowCount[$y] -gt 0) {
        $line += ("{0}:{1}  " -f $y, $rowCount[$y])
        if ($line.Length -gt 100) { Write-Output ("  " + $line); $line = "" }
    }
}
if ($line) { Write-Output ("  " + $line) }

$sampleLines | ForEach-Object { Write-Output $_ }
exit 1
