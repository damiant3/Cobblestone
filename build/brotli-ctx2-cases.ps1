# BACKLOG 5.9 -- generate the context-mode-2 decoder test.
#
# Our encoder writes context mode ZERO, so no round trip through our own halves
# can exercise mode two at all. The streams here are built by an independent
# implementation (this script, against the format) and their expected bytes are
# what .NET's BrotliStream decodes them to, which is the same arrangement
# build/brotli-xform-cases.ps1 uses for the word transforms.
#
#   pwsh build/brotli-ctx2-cases.ps1
#
# THE CASES ARE CHOSEN TO DISAGREE. Each is a (p2, p1, bit) triple for which the
# mode-zero context (p1 & 0x3f) and the mode-two context (Lut0[p1] | Lut1[p2])
# differ IN THAT BIT, so the two modes select different literal trees and the
# stream decodes to a different byte under each. A decoder that ignores the
# context mode field -- which this one did until 2026-07-19 -- produces 'B' where
# 'A' is required, or the reverse. A case where the two modes agree would pass
# either way and would be a test that cannot fail.

param(
  [string]$OutDir = "codex/test/lib"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$lutPath = Join-Path $root "build-output/brotli-ctx2.txt"
if (-not (Test-Path $lutPath)) { throw "run build/brotli-ctx2-extract.ps1 first (need the recovered tables)" }
$ll = Get-Content $lutPath
$lut0 = ($ll | Where-Object { $_ -like 'lut0*' }).Split("`t")[1].Split(',') | ForEach-Object { [int]$_ }
$lut1 = ($ll | Where-Object { $_ -like 'lut1*' }).Split("`t")[1].Split(',') | ForEach-Object { [int]$_ }

class BW {
  [System.Collections.Generic.List[byte]]$bytes = [System.Collections.Generic.List[byte]]::new()
  [int]$cur = 0
  [int]$n = 0
  [void] Bit([int]$b) {
    $this.cur = $this.cur -bor (($b -band 1) -shl $this.n)
    $this.n++
    if ($this.n -eq 8) { $this.bytes.Add([byte]$this.cur); $this.cur = 0; $this.n = 0 }
  }
  [void] Bits([int]$v, [int]$k) { for ($i = 0; $i -lt $k; $i++) { $this.Bit(($v -shr $i) -band 1) } }
  [void] Code([int]$v, [int]$k) { for ($i = $k - 1; $i -ge 0; $i--) { $this.Bit(($v -shr $i) -band 1) } }
  [void] Align() { while ($this.n -ne 0) { $this.Bit(0) } }
  [byte[]] Done() { if ($this.n -gt 0) { $this.bytes.Add([byte]$this.cur) }; return $this.bytes.ToArray() }
}

# A simple code naming one symbol. The symbol list is a plain fixed-width field
# and travels LSB-first; only codewords are MSB-first.
function Write-Simple1([BW]$w, [int]$sym, [int]$abits) { $w.Bits(1, 2); $w.Bits(0, 2); $w.Bits($sym, $abits) }

$icBases = @(128,192,384,256,320,512,448,576,640)
function Get-IcSym([int]$insC, [int]$cpyC) {
  return $icBases[[math]::Floor($insC / 8) * 3 + [math]::Floor($cpyC / 8)] + (($insC % 8) -shl 3) + ($cpyC % 8)
}

# A stored meta-block carrying [p2,p1] sets the stream's previous two bytes;
# they are properties of the STREAM, not of the meta-block, which is what lets a
# stored block and a compressed one compose this way.
function New-Ctx2Stream([int]$p2, [int]$p1, [int]$k, [int]$mode, [int]$symA, [int]$symB) {
  $w = [BW]::new()
  $w.Bits(0, 1)
  $w.Bits(0, 1); $w.Bits(0, 2); $w.Bits(2 - 1, 16); $w.Bits(1, 1)
  $w.Align(); $w.Bits($p2, 8); $w.Bits($p1, 8)
  $w.Bits(1, 1); $w.Bits(0, 1); $w.Bits(0, 2); $w.Bits(1 - 1, 16)
  $w.Bits(0, 1); $w.Bits(0, 1); $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits(0, 4); $w.Bits($mode, 2)
  $w.Bits(1, 1); $w.Bits(0, 3)
  $w.Bits(0, 1); $w.Bits(1, 2); $w.Bits(2 - 1, 2); $w.Bits(0, 1); $w.Bits(1, 1)
  for ($c = 0; $c -lt 64; $c++) { $w.Code((($c -shr $k) -band 1), 1) }
  $w.Bits(0, 1)
  $w.Bits(0, 1)
  Write-Simple1 $w $symA 8
  Write-Simple1 $w $symB 8
  Write-Simple1 $w (Get-IcSym 1 0) 10
  Write-Simple1 $w 0 6
  return $w.Done()
}

function Invoke-NetBrotli([byte[]]$blob) {
  $in = [IO.MemoryStream]::new($blob)
  $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.MemoryStream]::new(); $ds.CopyTo($o); $ds.Dispose(); return $o.ToArray()
}

$symA = 65; $symB = 66
$cands = @(
  @(0,   97), @(0,   65), @(32,  97), @(97,  32),
  @(48,  57), @(128, 200), @(224, 160), @(10, 122),
  @(46,  32), @(255, 254)
)

$cases = @()
foreach ($pair in $cands) {
  $p2 = $pair[0]; $p1 = $pair[1]
  $c0 = $p1 -band 63
  $c2 = $lut0[$p1] -bor $lut1[$p2]
  for ($k = 0; $k -lt 6; $k++) {
    if ((($c0 -shr $k) -band 1) -ne (($c2 -shr $k) -band 1)) {
      $blob = New-Ctx2Stream $p2 $p1 $k 2 $symA $symB
      $got = Invoke-NetBrotli $blob
      if ($null -eq $got -or $got.Length -ne 3) { throw "oracle refused the mode-2 stream for p1=$p1 p2=$p2 bit=$k" }
      if ($got[0] -ne $p2 -or $got[1] -ne $p1) { throw "oracle produced the wrong prefix for p1=$p1 p2=$p2" }
      $want = $got[2]
      $modeZeroWould = if ((($c0 -shr $k) -band 1) -eq 0) { $symA } else { $symB }
      if ($want -eq $modeZeroWould) { throw "case p1=$p1 p2=$p2 bit=$k cannot fail -- both modes give the same byte" }
      $cases += [PSCustomObject]@{ P2 = $p2; P1 = $p1; K = $k; Bytes = $blob; Want = $want }
      break
    }
  }
}
if ($cases.Count -lt 6) { throw "only $($cases.Count) disagreeing cases found; expected at least 6" }

Write-Host "[ctx2-cases] $($cases.Count) streams built, all decoded by .NET, all chosen where mode 0 and mode 2 disagree"
foreach ($c in $cases) {
  $mz = if ((($c.P1 -band 63) -shr $c.K) -band 1) { $symB } else { $symA }
  Write-Host ("  p1={0,3} p2={1,3} bit={2}  mode2 -> {3}   mode0 would give {4}" -f $c.P1, $c.P2, $c.K, [char]$c.Want, [char]$mz)
}

$lines = @()
$lines += "Chapter: BrotliCtx2Test"
$lines += "  cites Compress chapter Brotli"
$lines += ""
$lines += " RFC 7932 context mode 2 (UTF8), read by our decoder."
$lines += ""
$lines += " GENERATED by build/brotli-ctx2-cases.ps1. Do not edit by hand."
$lines += ""
$lines += " Our encoder writes context mode ZERO, so nothing our two halves do together"
$lines += " can exercise mode two. Every stream below was built by an independent"
$lines += " implementation and its expected byte is what .NET's BrotliStream decodes it"
$lines += " to, which is the only thing that can judge a reader of a format we do not"
$lines += " write."
$lines += ""
$lines += " EACH CASE IS CHOSEN WHERE THE TWO MODES DISAGREE. A stream sets the previous"
$lines += " two bytes with a stored meta-block, then carries one literal under two trees"
$lines += " naming A and B, with a context map assigning the tree by one bit of the"
$lines += " context. The mode-zero context and the mode-two context differ in that bit,"
$lines += " so a decoder that ignores the context mode field answers the other letter."
$lines += " Before 2026-07-19 this reader ignored it and every case here answered wrong."
$lines += ""
$i = 0
foreach ($c in $cases) {
  $i++
  $blist = ($c.Bytes | ForEach-Object { [int]$_ }) -join ", "
  $lines += "  ctx2-in-$i : List Integer = [$blist]"
  $lines += "  ctx2-want-$i : Integer = $($c.Want)"
  $lines += ""
}
$lines += "  ctx2-third : List Integer -> Integer"
$lines += "  ctx2-third (bs) = if list-length bs < 3 then 0 else list-at bs 2"
$lines += ""
$lines += "  opening : [Console] Nothing"
$lines += "  opening = act"
for ($j = 1; $j -le $cases.Count; $j++) {
  $lines += "    print-line-uni (`"ctx2-$j `" & show (ctx2-third (brotli-decompress ctx2-in-$j)))"
}
$lines += "  end"

$codexPath = Join-Path $root "$OutDir/brotli-ctx2-test.codex"
[IO.File]::WriteAllLines($codexPath, $lines)

$exp = @()
for ($j = 1; $j -le $cases.Count; $j++) { $exp += "ctx2-$j $($cases[$j-1].Want)" }
$expPath = Join-Path $root "$OutDir/brotli-ctx2-test.expected"
[IO.File]::WriteAllLines($expPath, $exp)

Write-Host ""
Write-Host ("[ctx2-cases] wrote {0} and its .expected" -f $codexPath) -ForegroundColor Green
