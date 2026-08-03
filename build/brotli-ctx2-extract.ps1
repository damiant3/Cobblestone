# BACKLOG 5.9 -- recover RFC 7932's two context-mode-2 (UTF8) lookup tables from
# the oracle.
#
# Same method and same reason as build/brotli-dict-extract.ps1 and
# build/brotli-xform-extract.ps1: the tables have to be byte-exact because a real
# decoder uses ITS copy to pick the literal tree, so an approximated table does
# not cost ratio -- it decodes to the wrong bytes. They are not in this
# environment and cannot honestly be written from memory.
#
# Context mode 2 is `context = Lut0[p1] | Lut1[p2]`, where p1 is the previous
# output byte of the STREAM and p2 the one before it. Both tables are 256 entries.
#
#   pwsh build/brotli-ctx2-extract.ps1
#   pwsh build/brotli-ctx2-extract.ps1 -Out build-output/brotli-ctx2.txt
#
# THE PROBE. A literal's tree is chosen by its context, and a tree that names
# exactly one symbol costs zero bits to use -- so the tree a literal was decoded
# under is legible in the OUTPUT BYTE while the bitstream stays fixed. Two trees
# naming 'A' and 'B', and a context map assigning tree `(context >> k) & 1`, make
# one decoded byte report bit k of the context. Six streams give all six bits.
#
# p1 and p2 are set by a preceding STORED meta-block carrying those two bytes.
# That is what makes the pair arbitrary: the dictionary can only produce the
# bytes it holds, and a literal cannot be forced to a chosen value when the whole
# point of the probe is that its value is unknown. p1/p2 are properties of the
# stream and not of the meta-block, which is what lets the two blocks compose.

param(
  [string]$Out = "build-output/brotli-ctx2.txt"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

class BW {
  [System.Collections.Generic.List[byte]]$bytes = [System.Collections.Generic.List[byte]]::new()
  [int]$cur = 0
  [int]$n = 0
  [void] Bit([int]$b) {
    $this.cur = $this.cur -bor (($b -band 1) -shl $this.n)
    $this.n++
    if ($this.n -eq 8) { $this.bytes.Add([byte]$this.cur); $this.cur = 0; $this.n = 0 }
  }
  # LSB-first within a byte, like Deflate.
  [void] Bits([int]$v, [int]$k) { for ($i = 0; $i -lt $k; $i++) { $this.Bit(($v -shr $i) -band 1) } }
  # Prefix-code symbols and context-map values travel MSB-first.
  [void] Code([int]$v, [int]$k) { for ($i = $k - 1; $i -ge 0; $i--) { $this.Bit(($v -shr $i) -band 1) } }
  [void] Align() { while ($this.n -ne 0) { $this.Bit(0) } }
  [byte[]] Done() { if ($this.n -gt 0) { $this.bytes.Add([byte]$this.cur) }; return $this.bytes.ToArray() }
}

# A simple prefix code naming a single symbol: HSKIP=1, NSYM-1=0, the symbol.
# Costs zero bits at every use, which is the whole basis of the probe.
#
# The symbol LIST of a simple code is a plain fixed-width field and so travels
# LSB-first like every other one. Only CODEWORDS are MSB-first, and the symbols
# named here are not codewords -- they are the alphabet the code is over. Writing
# them MSB-first is rejected outright, which is the friendly version of this
# mistake; the same error inside the context map would decode to wrong trees.
function Write-Simple1([BW]$w, [int]$sym, [int]$abits) {
  $w.Bits(1, 2); $w.Bits(0, 2); $w.Bits($sym, $abits)
}

$icBases = @(128,192,384,256,320,512,448,576,640)
function Get-IcSym([int]$insC, [int]$cpyC) {
  return $icBases[[math]::Floor($insC / 8) * 3 + [math]::Floor($cpyC / 8)] + (($insC % 8) -shl 3) + ($cpyC % 8)
}

# Stream: WBITS, a stored meta-block carrying [p2,p1], then a final compressed
# meta-block of ONE literal under context mode 2 with the bit-k context map.
function New-Ctx2Stream([int]$p2, [int]$p1, [int]$k, [int]$symA, [int]$symB) {
  $w = [BW]::new()
  $w.Bits(0, 1)                      # WBITS: 16-bit window

  # --- stored meta-block: ISLAST=0, 4 nibbles, MLEN=2, ISUNCOMPRESSED=1 ---
  $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits(2 - 1, 16)
  $w.Bits(1, 1)
  $w.Align()
  $w.Bits($p2, 8); $w.Bits($p1, 8)

  # --- compressed meta-block: ISLAST=1, not empty, MLEN=1 ---
  $w.Bits(1, 1); $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits(1 - 1, 16)

  $w.Bits(0, 1)                      # NBLTYPESL = 1
  $w.Bits(0, 1)                      # NBLTYPESI = 1
  $w.Bits(0, 1)                      # NBLTYPESD = 1
  $w.Bits(0, 2)                      # NPOSTFIX = 0
  $w.Bits(0, 4)                      # NDIRECT  = 0
  $w.Bits(2, 2)                      # context mode 2 (UTF8) for literal type 0

  # NTREESL = 2, as the variable-length count of 9.2: a 1 bit, 3 bits N=0, no
  # extra, giving (1 << 0) + 1 + 0.
  $w.Bits(1, 1); $w.Bits(0, 3)

  # Literal context map. RLEMAX=0, then a code over the 2 tree indices, then 64
  # values, then the IMTF flag. The map's code must be the format's SIMPLE one
  # (a flat code cannot be described by the complex form), and because its
  # symbols are written in order a value's code equals the value.
  $w.Bits(0, 1)                      # RLEMAX = 0
  $w.Bits(1, 2); $w.Bits(2 - 1, 2)   # simple, NSYM = 2
  $w.Bits(0, 1); $w.Bits(1, 1)       # symbols 0 and 1, in order (fixed-width)
  for ($c = 0; $c -lt 64; $c++) { $w.Code((($c -shr $k) -band 1), 1) }
  $w.Bits(0, 1)                      # IMTF off

  $w.Bits(0, 1)                      # NTREESD = 1

  Write-Simple1 $w $symA 8           # literal tree 0 names symA
  Write-Simple1 $w $symB 8           # literal tree 1 names symB
  Write-Simple1 $w (Get-IcSym 1 0) 10  # insert 1, copy code 0 (never acted on)
  Write-Simple1 $w 0 6               # distance code, written but unreachable

  # The command. Every code names one symbol, so the symbol itself and both
  # extra-bit fields are empty: insert code 1 is base 1 with no extra bits, copy
  # code 0 is base 2 with none. The decoder reads the single literal, finds MLEN
  # reached, and never asks for a distance.
  return $w.Done()
}

function Invoke-NetBrotli([byte[]]$blob) {
  $in = [IO.MemoryStream]::new($blob)
  $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.MemoryStream]::new()
  $ds.CopyTo($o)
  $ds.Dispose()
  return $o.ToArray()
}

$symA = 65
$symB = 66

# Ask the oracle for one bit of context(p1,p2). Returns -1 if the stream did not
# decode to the three bytes it must -- which is a broken probe, never a datum.
function Get-CtxBit([int]$p2, [int]$p1, [int]$k) {
  $b = $null
  try { $b = Invoke-NetBrotli (New-Ctx2Stream $p2 $p1 $k $symA $symB) } catch { return -1 }
  if ($null -eq $b -or $b.Length -ne 3) { return -1 }
  if ($b[0] -ne $p2 -or $b[1] -ne $p1) { return -1 }
  if ($b[2] -eq $symA) { return 0 }
  if ($b[2] -eq $symB) { return 1 }
  return -1
}

function Get-Ctx([int]$p2, [int]$p1) {
  $v = 0
  for ($k = 0; $k -lt 6; $k++) {
    $bit = Get-CtxBit $p2 $p1 $k
    if ($bit -lt 0) { return -1 }
    $v = $v -bor ($bit -shl $k)
  }
  return $v
}

# ---- sanity: the probe must be able to report more than one answer ----
# A probe that always says the same thing looks exactly like one that works, so
# before reading 512 rows off it, confirm two pairs that must differ do differ.
Write-Host "[ctx2] checking the probe can distinguish two contexts"
$s0 = Get-Ctx 0 0
$s1 = Get-Ctx 0 97
if ($s0 -lt 0) { throw "probe failed on (p2=0,p1=0) -- the stream shape is wrong, not the table" }
if ($s1 -lt 0) { throw "probe failed on (p2=0,p1=97) -- the stream shape is wrong, not the table" }
if ($s0 -eq $s1) { throw "probe returns $s0 for both p1=0 and p1='a' -- it cannot fail, so it proves nothing" }
Write-Host "[ctx2]   context(p1=0,p2=0) = $s0, context(p1=97,p2=0) = $s1 -- distinguishable"

# ---- the two reference rows ----
# c(p1,p2) = Lut0[p1] | Lut1[p2]. Sweep p1 with p2 held, and p2 with p1 held.
$p2ref = 0
$p1ref = 0

Write-Host "[ctx2] sweeping p1 = 0..255 at p2 = $p2ref"
$rowP1 = @()
for ($p1 = 0; $p1 -lt 256; $p1++) {
  $c = Get-Ctx $p2ref $p1
  if ($c -lt 0) { throw "probe failed at p1=$p1, p2=$p2ref" }
  $rowP1 += $c
}

Write-Host "[ctx2] sweeping p2 = 0..255 at p1 = $p1ref"
$rowP2 = @()
for ($p2 = 0; $p2 -lt 256; $p2++) {
  $c = Get-Ctx $p2 $p1ref
  if ($c -lt 0) { throw "probe failed at p1=$p1ref, p2=$p2" }
  $rowP2 += $c
}

# ---- read the two tables straight off the sweeps ----
# c(0,0) measured ZERO, and an OR is zero only when both operands are, so
# Lut0[0] and Lut1[0] are both zero. Each sweep therefore reads its own table
# with the other contributing nothing:
#
#   rowP1[p1] = Lut0[p1] | Lut1[0] = Lut0[p1]
#   rowP2[p2] = Lut0[0]  | Lut1[p2] = Lut1[p2]
#
# It is asserted rather than assumed, because the whole decomposition rests on
# it and a non-zero c(0,0) would make both tables silently too large.
if ($rowP1[0] -ne 0) { throw "c(p1=0,p2=0) = $($rowP1[0]), not 0 -- the sweeps do not isolate the two tables and this decomposition is wrong" }

$lut0 = $rowP1
$lut1 = $rowP2

# Reported as information, NOT enforced. The first cut of this script required
# the two tables to occupy disjoint bit positions and threw when they did not.
# That was an invented constraint: an OR is the right formula whether or not the
# operands overlap, and the decoder computes the same OR either way. The
# held-out verification below is what actually decides.
$mask0 = 0; $mask1 = 0
foreach ($c in $rowP1) { $mask0 = $mask0 -bor $c }
foreach ($c in $rowP2) { $mask1 = $mask1 -bor $c }
Write-Host ("[ctx2] bits reachable from p1: 0x{0:x2}   from p2: 0x{1:x2}   overlap: 0x{2:x2}" -f $mask0, $mask1, ($mask0 -band $mask1))

# ---- verify on pairs the derivation did not use ----
# The two sweeps only ever moved one byte at a time. Every pair below moves BOTH,
# so nothing here is a restatement of what was measured; a table that happens to
# fit its own reference row and nothing else fails at this step.
Write-Host "[ctx2] verifying on pairs where BOTH bytes differ from the reference rows"
$vals = @(1, 9, 32, 47, 48, 57, 64, 65, 90, 91, 96, 97, 122, 123, 127, 128, 160, 192, 200, 224, 240, 250, 254, 255)
$bad = 0
$checked = 0
foreach ($p1 in $vals) {
  foreach ($p2 in $vals) {
    $want = $lut0[$p1] -bor $lut1[$p2]
    $got = Get-Ctx $p2 $p1
    if ($got -lt 0) { Write-Host "[ctx2] VERIFY NO-OUTPUT p1=$p1 p2=$p2" -ForegroundColor Red; $bad++; continue }
    $checked++
    if ($got -ne $want) {
      Write-Host "[ctx2] VERIFY FAIL p1=$p1 p2=$p2 got=$got want=$want" -ForegroundColor Red
      $bad++
    }
  }
}
Write-Host "[ctx2] verified $checked pairs neither sweep visited"
if ($bad -gt 0) { throw "$bad verification mismatches -- the tables are wrong, do not ship them" }

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
$lines = @()
$lines += "# RFC 7932 context mode 2 (UTF8): context = Lut0[p1] | Lut1[p2]"
$lines += "# recovered from System.IO.Compression.BrotliStream by build/brotli-ctx2-extract.ps1"
$lines += ("# mask0=0x{0:x2} mask1=0x{1:x2}" -f $mask0, $mask1)
$lines += "lut0`t" + ($lut0 -join ",")
$lines += "lut1`t" + ($lut1 -join ",")
[IO.File]::WriteAllLines($outPath, $lines)

# Emit the chapter constant too. Every value fits in ONE base64 character --
# lut0 tops out at 60 and lut1 at 3, both under 64 -- so the table is 512
# characters with the value as the alphabet index, and Brotli.codex decodes it
# with the b64 table BrotliDict already builds. No 4-chars-to-3-bytes step.
#
# It is TEXT and not a list literal for the reason BrotliDict's corpus is: a list
# literal is emitted as CODE that builds the list an element at a time, and a
# list CONSTANT is rebuilt at every mention. Text is static data, read once into
# a list, then threaded as a parameter.
$alpha = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
$sb = [Text.StringBuilder]::new()
foreach ($v in $lut0) { if ($v -lt 0 -or $v -gt 63) { throw "lut0 value $v does not fit one base64 character" }; [void]$sb.Append($alpha[$v]) }
foreach ($v in $lut1) { if ($v -lt 0 -or $v -gt 63) { throw "lut1 value $v does not fit one base64 character" }; [void]$sb.Append($alpha[$v]) }
$txt = $sb.ToString()
if ($txt.Length -ne 512) { throw "expected 512 characters, got $($txt.Length)" }

$chapPath = Join-Path $root "build-output/brotli-ctx2-chapter.txt"
[IO.File]::WriteAllLines($chapPath, @("  brctx2-lut-text : Text = `"$txt`""))
Write-Host "[ctx2] chapter constant (512 chars) -> build-output/brotli-ctx2-chapter.txt"

Write-Host ""
Write-Host ("[ctx2] two 256-entry tables derived and verified -> {0}" -f $Out) -ForegroundColor Green
Write-Host ("[ctx2] lut0 distinct values: " + (($lut0 | Sort-Object -Unique) -join ","))
Write-Host ("[ctx2] lut1 distinct values: " + (($lut1 | Sort-Object -Unique) -join ","))
