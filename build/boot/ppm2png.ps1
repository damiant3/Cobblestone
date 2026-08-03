# P6 PPM -> PNG. Replaces ppm2png.py: the OVMF gate ran on every boot and
# shelled out to Python to do it, which put a Python install between us and a
# screenshot (CLAUDE.md rule 6 -- PowerShell or Codex, nothing else).
#
# The PNG is written by hand rather than through System.Drawing on purpose.
# GDI+ needs a per-pixel SetPixel loop or LockBits marshalling to fill a
# 1280x800 frame, and the first is far too slow in PowerShell while the second
# is more fragile than the 40 lines below. DeflateStream gets us the only hard
# part.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)] [string]$Ppm,
    [Parameter(Mandatory=$true, Position=1)] [string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Leaf $Ppm)) { Write-Host "FAIL: no such PPM: $Ppm"; exit 1 }
$d = [IO.File]::ReadAllBytes($Ppm)
if ($d.Length -lt 2 -or $d[0] -ne 0x50 -or $d[1] -ne 0x36) { Write-Host "FAIL: not a P6 PPM: $Ppm"; exit 1 }

# Header: three integers (width, height, maxval), whitespace separated, with
# '#' comments legal between them. Exactly one whitespace byte follows maxval
# before the pixel data starts.
$i = 2
$vals = @()
while ($vals.Count -lt 3) {
    while ($i -lt $d.Length -and ($d[$i] -eq 32 -or $d[$i] -eq 9 -or $d[$i] -eq 10 -or $d[$i] -eq 13)) { $i++ }
    if ($i -ge $d.Length) { Write-Host "FAIL: truncated PPM header"; exit 1 }
    if ($d[$i] -eq 0x23) { while ($i -lt $d.Length -and $d[$i] -ne 10) { $i++ }; continue }
    $j = $i
    while ($j -lt $d.Length -and -not ($d[$j] -eq 32 -or $d[$j] -eq 9 -or $d[$j] -eq 10 -or $d[$j] -eq 13)) { $j++ }
    $vals += [int][Text.Encoding]::ASCII.GetString($d, $i, $j - $i)
    $i = $j
}
$w = $vals[0]; $h = $vals[1]; $mx = $vals[2]
if ($mx -ne 255) { Write-Host "FAIL: maxval $mx unsupported (need 255)"; exit 1 }
$i++
$need = $w * $h * 3
if ($i + $need -gt $d.Length) { Write-Host "FAIL: PPM has $($d.Length - $i) pixel bytes, need $need"; exit 1 }

# PNG scanlines: filter byte 0 then the row's RGB, which is the PPM's own layout.
$raw = New-Object byte[] ($h * (1 + $w * 3))
for ($y = 0; $y -lt $h; $y++) {
    $raw[$y * (1 + $w * 3)] = 0
    [Array]::Copy($d, $i + $y * $w * 3, $raw, $y * (1 + $w * 3) + 1, $w * 3)
}

# All of this stays in masked [long] arithmetic. PowerShell widens hex literals
# and [uint32] operands unpredictably across -bxor and -shr, and the first
# version of this file died on "cannot convert -1 to UInt32" for exactly that
# reason.
$crcTable = New-Object long[] 256
for ($n = 0; $n -lt 256; $n++) {
    [long]$c = $n
    for ($k = 0; $k -lt 8; $k++) {
        if ($c -band 1) { $c = (0xEDB88320 -bxor ($c -shr 1)) -band 0xFFFFFFFF }
        else { $c = ($c -shr 1) -band 0xFFFFFFFF }
    }
    $crcTable[$n] = $c
}
function Get-Crc32([byte[]]$buf) {
    [long]$c = 0xFFFFFFFF
    foreach ($b in $buf) {
        $c = ($script:crcTable[[int](($c -bxor $b) -band 0xFF)] -bxor (($c -shr 8) -band 0xFFFFFF)) -band 0xFFFFFFFF
    }
    return (($c -bxor 0xFFFFFFFF) -band 0xFFFFFFFF)
}
function Get-Adler32([byte[]]$buf) {
    [long]$a = 1; [long]$b = 0
    foreach ($x in $buf) { $a = ($a + $x) % 65521; $b = ($b + $a) % 65521 }
    return ((($b -shl 16) -bor $a) -band 0xFFFFFFFF)
}
function Write-BE32([IO.MemoryStream]$s, [long]$v) {
    $s.WriteByte([byte](($v -shr 24) -band 0xFF)); $s.WriteByte([byte](($v -shr 16) -band 0xFF))
    $s.WriteByte([byte](($v -shr 8) -band 0xFF));  $s.WriteByte([byte]($v -band 0xFF))
}
function Write-Chunk([IO.MemoryStream]$s, [string]$type, [byte[]]$data) {
    Write-BE32 $s ([long]$data.Length)
    $tb = [Text.Encoding]::ASCII.GetBytes($type)
    $body = New-Object byte[] ($tb.Length + $data.Length)
    [Array]::Copy($tb, 0, $body, 0, $tb.Length)
    if ($data.Length -gt 0) { [Array]::Copy($data, 0, $body, $tb.Length, $data.Length) }
    $s.Write($body, 0, $body.Length)
    Write-BE32 $s (Get-Crc32 $body)
}

# IDAT carries a zlib stream: 0x78 0x01 header, raw deflate, adler32 of the
# UNCOMPRESSED bytes. DeflateStream emits the raw deflate only, so both ends
# are ours to write.
$ms = [IO.MemoryStream]::new()
$dz = [IO.Compression.DeflateStream]::new($ms, [IO.Compression.CompressionLevel]::Optimal, $true)
$dz.Write($raw, 0, $raw.Length)
$dz.Dispose()
$deflated = $ms.ToArray()
$ms.Dispose()

$zlib = [IO.MemoryStream]::new()
$zlib.WriteByte(0x78); $zlib.WriteByte(0x01)
$zlib.Write($deflated, 0, $deflated.Length)
Write-BE32 $zlib (Get-Adler32 $raw)
$idat = $zlib.ToArray()
$zlib.Dispose()

$ihdr = [IO.MemoryStream]::new()
Write-BE32 $ihdr ([long]$w); Write-BE32 $ihdr ([long]$h)
$ihdr.WriteByte(8)   # bit depth
$ihdr.WriteByte(2)   # colour type 2 = truecolour RGB
$ihdr.WriteByte(0); $ihdr.WriteByte(0); $ihdr.WriteByte(0)
$ihdrBytes = $ihdr.ToArray()
$ihdr.Dispose()

$png = [IO.MemoryStream]::new()
$png.Write([byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), 0, 8)
Write-Chunk $png 'IHDR' $ihdrBytes
Write-Chunk $png 'IDAT' $idat
Write-Chunk $png 'IEND' (New-Object byte[] 0)
[IO.File]::WriteAllBytes($Out, $png.ToArray())
$png.Dispose()

Write-Host "  ${w}x${h} -> $Out"
