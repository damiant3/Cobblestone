# Generate a minimal valid TrueType font for testing the Codex TTF parser.
# Produces a TTF with 3 glyphs: .notdef (empty), space, and 'A' (triangle).
# All values are big-endian per the TTF spec.
param(
    [string]$Out = "test-output/test-font.ttf"
)

$ErrorActionPreference = 'Stop'
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

function Write-U16BE([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $bw.Write($b) }
function Write-I16BE([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $bw.Write($b) }
function Write-U32BE([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $bw.Write($b) }
function Write-Tag([string]$t)   { $bw.Write([System.Text.Encoding]::ASCII.GetBytes($t)) }
function Pad4 { while ($ms.Position % 4 -ne 0) { $bw.Write([byte]0) } }

# We'll build tables in memory then assemble with correct offsets
$tables = @{}

# ---- head table (54 bytes) ----
$headMs = [System.IO.MemoryStream]::new()
$hw = [System.IO.BinaryWriter]::new($headMs)
function HU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hw.Write($b) }
function HI16([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hw.Write($b) }
function HU32([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hw.Write($b) }
HU32 0x00010000  # version
HU32 0x00005000  # fontRevision
HU32 0           # checksumAdjust
HU32 0x5F0F3CF5  # magicNumber
HU16 0x000B      # flags (baseline at y=0, integer scaling)
HU16 1024        # unitsPerEm
$hw.Write([byte[]]::new(8)) # created (LONGDATETIME)
$hw.Write([byte[]]::new(8)) # modified
HI16 0           # xMin
HI16 0           # yMin
HI16 512         # xMax
HI16 700         # yMax
HU16 0           # macStyle
HU16 8           # lowestRecPPEM
HI16 2           # fontDirectionHint
HI16 1           # indexToLocFormat (long)
HI16 0           # glyphDataFormat
$tables['head'] = $headMs.ToArray()
$hw.Close(); $headMs.Close()

# ---- maxp table (6 bytes for TrueType) ----
$maxpMs = [System.IO.MemoryStream]::new()
$mw = [System.IO.BinaryWriter]::new($maxpMs)
function MU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $mw.Write($b) }
function MU32([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $mw.Write($b) }
MU32 0x00010000  # version
MU16 3           # numGlyphs
$tables['maxp'] = $maxpMs.ToArray()
$mw.Close(); $maxpMs.Close()

# ---- hhea table (36 bytes) ----
$hheaMs = [System.IO.MemoryStream]::new()
$hhw = [System.IO.BinaryWriter]::new($hheaMs)
function HHU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hhw.Write($b) }
function HHI16([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hhw.Write($b) }
function HHU32([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hhw.Write($b) }
HHU32 0x00010000 # version
HHI16 700        # ascender
HHI16 0          # descender
HHI16 0          # lineGap
HHU16 512        # advanceWidthMax
HHI16 0          # minLeftSideBearing
HHI16 0          # minRightSideBearing
HHI16 512        # xMaxExtent
HHI16 1          # caretSlopeRise
HHI16 0          # caretSlopeRun
HHI16 0          # caretOffset
HHI16 0; HHI16 0; HHI16 0; HHI16 0 # reserved
HHI16 0          # metricDataFormat
HHU16 3          # numberOfHMetrics
$tables['hhea'] = $hheaMs.ToArray()
$hhw.Close(); $hheaMs.Close()

# ---- hmtx table (3 entries x 4 bytes = 12 bytes) ----
$hmtxMs = [System.IO.MemoryStream]::new()
$hmw = [System.IO.BinaryWriter]::new($hmtxMs)
function HMU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hmw.Write($b) }
function HMI16([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $hmw.Write($b) }
HMU16 512; HMI16 0    # glyph 0 (.notdef): advance=512, lsb=0
HMU16 256; HMI16 0    # glyph 1 (space): advance=256, lsb=0
HMU16 512; HMI16 0    # glyph 2 (A): advance=512, lsb=0
$tables['hmtx'] = $hmtxMs.ToArray()
$hmw.Close(); $hmtxMs.Close()

# ---- glyf table ----
# Glyph 0: .notdef (empty, 0 contours) -- just header
# Glyph 1: space (empty, 0 contours)
# Glyph 2: 'A' as a triangle (1 contour, 3 on-curve points)
$glyfMs = [System.IO.MemoryStream]::new()
$gw = [System.IO.BinaryWriter]::new($glyfMs)
function GU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $gw.Write($b) }
function GI16([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $gw.Write($b) }
function GU8([byte]$v)    { $gw.Write($v) }

# Glyph 0 offset: 0
$glyph0Off = $glyfMs.Position
GI16 0     # numberOfContours = 0 (empty)
GI16 0; GI16 0; GI16 0; GI16 0  # xMin,yMin,xMax,yMax

# Glyph 1 offset
$glyph1Off = $glyfMs.Position
GI16 0     # numberOfContours = 0 (empty)
GI16 0; GI16 0; GI16 0; GI16 0

# Glyph 2: Triangle 'A' with points (0,0), (256,700), (512,0)
$glyph2Off = $glyfMs.Position
GI16 1       # numberOfContours = 1
GI16 0       # xMin
GI16 0       # yMin
GI16 512     # xMax
GI16 700     # yMax
GU16 2       # endPtsOfContours[0] = 2 (3 points: 0,1,2)
GU16 0       # instructionLength = 0
# Flags: all on-curve, x and y are 2-byte signed
# Flag byte: bit0=onCurve=1, bit1=xShort=0, bit2=yShort=0, bit3=repeat=0, bit4=xSame=0, bit5=ySame=0
GU8 1        # point 0 flags: on-curve
GU8 1        # point 1 flags: on-curve
GU8 1        # point 2 flags: on-curve
# X coordinates (deltas): first=0, delta to 256, delta to 256
GI16 0       # x0 = 0
GI16 256     # x1 = 0+256 = 256
GI16 256     # x2 = 256+256 = 512
# Y coordinates (deltas): first=0, delta to 700, delta to -700
GI16 0       # y0 = 0
GI16 700     # y1 = 0+700 = 700
GI16 (-700)  # y2 = 700-700 = 0

# Pad to 4 bytes
while ($glyfMs.Position % 4 -ne 0) { GU8 0 }
$glyfEnd = $glyfMs.Position

$tables['glyf'] = $glyfMs.ToArray()
$gw.Close(); $glyfMs.Close()

# ---- loca table (long format, 4 entries for 3 glyphs) ----
$locaMs = [System.IO.MemoryStream]::new()
$lw = [System.IO.BinaryWriter]::new($locaMs)
function LU32([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $lw.Write($b) }
LU32 ([uint32]$glyph0Off)
LU32 ([uint32]$glyph1Off)
LU32 ([uint32]$glyph2Off)
LU32 ([uint32]$glyfEnd)   # end sentinel
$tables['loca'] = $locaMs.ToArray()
$lw.Close(); $locaMs.Close()

# ---- cmap table (format 4, maps U+0020=space->1, U+0041='A'->2) ----
$cmapMs = [System.IO.MemoryStream]::new()
$cw = [System.IO.BinaryWriter]::new($cmapMs)
function CU16([uint16]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $cw.Write($b) }
function CI16([int16]$v)  { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $cw.Write($b) }
function CU32([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); $cw.Write($b) }

# cmap header
CU16 0      # version
CU16 1      # numTables (1 subtable)
# Encoding record: platform=3 (Windows), encoding=1 (Unicode BMP)
CU16 3      # platformID
CU16 1      # encodingID
CU32 12     # offset to subtable (after this 12-byte header)

# Format 4 subtable
# 3 segments: space (0x20-0x20), A (0x41-0x41), sentinel (0xFFFF)
$segCount = 3
$fmt4Len = 14 + $segCount * 8 + 2  # header + arrays + reservedPad
CU16 4           # format
CU16 ([uint16]$fmt4Len)  # length
CU16 0           # language
CU16 ([uint16]($segCount * 2))  # segCountX2
CU16 4           # searchRange
CU16 1           # entrySelector
CU16 2           # rangeShift

# endCode array
CU16 0x0020      # segment 0: space
CU16 0x0041      # segment 1: A
CU16 0xFFFF      # segment 2: sentinel

CU16 0           # reservedPad

# startCode array
CU16 0x0020      # segment 0: space
CU16 0x0041      # segment 1: A
CU16 0xFFFF      # segment 2: sentinel

# idDelta array (delta = glyphIndex - charCode)
CI16 (-31)       # space: glyph 1, charCode 0x20=32, delta = 1-32 = -31
CI16 (-63)       # A: glyph 2, charCode 0x41=65, delta = 2-65 = -63
CI16 1           # sentinel

# idRangeOffset array
CU16 0; CU16 0; CU16 0  # all zero (use delta)

$tables['cmap'] = $cmapMs.ToArray()
$cw.Close(); $cmapMs.Close()

# ---- Assemble the font file ----
$numTables = $tables.Count  # 7
$searchRange = 64  # largest power of 2 <= numTables*16 = 112 -> 64
$entrySelector = 2 # log2(4) where 4 is largest power of 2 <= 7
$rangeShift = $numTables * 16 - $searchRange

# Offset table (12 bytes) + table directory (numTables * 16 bytes)
$headerSize = 12 + $numTables * 16

# Sort tables by tag
$tagOrder = @('cmap','glyf','head','hhea','hmtx','loca','maxp')
$offsets = @{}
$currentOff = $headerSize
foreach ($tag in $tagOrder) {
    $offsets[$tag] = $currentOff
    $len = $tables[$tag].Length
    $padded = [Math]::Ceiling($len / 4) * 4
    $currentOff += $padded
}

# Write offset table
Write-U32BE 0x00010000  # sfntVersion (TrueType)
Write-U16BE ([uint16]$numTables)
Write-U16BE ([uint16]$searchRange)
Write-U16BE ([uint16]$entrySelector)
Write-U16BE ([uint16]$rangeShift)

# Write table directory entries
foreach ($tag in $tagOrder) {
    Write-Tag $tag
    Write-U32BE 0  # checksum (skip for test font)
    Write-U32BE ([uint32]$offsets[$tag])
    Write-U32BE ([uint32]$tables[$tag].Length)
}

# Write table data
foreach ($tag in $tagOrder) {
    $bw.Write($tables[$tag])
    Pad4
}

$bw.Flush()
$bytes = $ms.ToArray()
$bw.Close(); $ms.Close()

$dir = [System.IO.Path]::GetDirectoryName($Out)
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
[System.IO.File]::WriteAllBytes($Out, $bytes)
Write-Host "Generated test TTF: $Out ($($bytes.Length) bytes, 3 glyphs)"
Write-Host "  Glyph 0: .notdef (empty)"
Write-Host "  Glyph 1: space (U+0020, advance=256)"
Write-Host "  Glyph 2: 'A' triangle (U+0041, advance=512, 3 points)"
