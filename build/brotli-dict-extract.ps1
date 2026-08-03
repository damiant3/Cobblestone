# Recover RFC 7932's static dictionary from the oracle.
#
# Why this is the way to get it: the corpus has to be byte-exact (a real
# decoder looks words up in ITS copy, so an approximated corpus decodes to
# wrong bytes, not a worse ratio), it is not in this environment, and .NET
# has it. A copy whose distance exceeds max_distance is a dictionary
# reference, and at the start of a stream max_distance is ZERO, so distance
# 1+i selects word i directly. (The feasibility probe that established this,
# build/brotli-dict-probe.ps1, is retired; this script is the method.)
#
# Word ids run index + transform * num_words[len]. Transform 0 is the identity,
# so the raw corpus is the run of ids whose decoded length equals the requested
# copy length; the first id whose output changes length is transform 1 and marks
# the end of that length's words. The per-length counts are DISCOVERED that way
# rather than remembered.
#
#   pwsh build/brotli-dict-extract.ps1
#   pwsh build/brotli-dict-extract.ps1 -Out build-output/brotli-dict.bin

param(
  [string]$Out = "build-output/brotli-dict.bin"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# The stream builder is self-contained here (it originated in the retired
# feasibility probe, which had a main body and could not be dot-sourced).
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
  [byte[]] Done() { if ($this.n -gt 0) { $this.bytes.Add([byte]$this.cur) }; return $this.bytes.ToArray() }
}

$cpyBase  = @(2,3,4,5,6,7,8,9,10,12,14,18,22,30,38,54,70,102,134,198,326,582,1094,2118)
$cpyExtra = @(0,0,0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,7,8,9,10,24)
$icBases  = @(128,192,384,256,320,512,448,576,640)

function Get-CpyCode([int]$len) { for ($i = 23; $i -ge 0; $i--) { if ($len -ge $cpyBase[$i]) { return $i } }; return 0 }
function Get-IcSym([int]$insC, [int]$cpyC) {
  return $icBases[[math]::Floor($insC / 8) * 3 + [math]::Floor($cpyC / 8)] + (($insC % 8) -shl 3) + ($cpyC % 8)
}
function Get-DistNbits([int]$dc) { return 1 + [math]::Floor(($dc - 16) / 2) }
function Get-DistOffset([int]$dc) {
  $nb = Get-DistNbits $dc
  return ((2 + (($dc - 16) -band 1)) -shl $nb) - 4
}
function Get-DistCode([int]$d) {
  for ($dc = 16; $dc -lt 64; $dc++) {
    $off = Get-DistOffset $dc
    if ($d -gt $off -and $d -le $off + [math]::Pow(2, (Get-DistNbits $dc))) { return $dc }
  }
  return -1
}

function New-DictStream([int]$len, [int]$dist) {
  $w = [BW]::new()
  $w.Bits(0, 1)
  $w.Bits(1, 1); $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits($len - 1, 16)
  $w.Bits(0, 1); $w.Bits(0, 1); $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits(0, 4); $w.Bits(0, 2)
  $w.Bits(0, 1); $w.Bits(0, 1)
  $w.Bits(1, 2); $w.Bits(0, 2); $w.Bits(0, 8)
  $cpyC = Get-CpyCode $len
  $w.Bits(1, 2); $w.Bits(0, 2); $w.Bits((Get-IcSym 0 $cpyC), 10)
  $dc = Get-DistCode $dist
  if ($dc -lt 0) { throw "no distance code covers $dist" }
  $w.Bits(1, 2); $w.Bits(0, 2); $w.Bits($dc, 6)
  $w.Bits($len - $cpyBase[$cpyC], $cpyExtra[$cpyC])
  $w.Bits($dist - (Get-DistOffset $dc) - 1, (Get-DistNbits $dc))
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

$all = [System.Collections.Generic.List[byte]]::new()
$counts = @{}
$total = 0

foreach ($len in 4..24) {
  $idx = 0
  $words = [System.Collections.Generic.List[byte[]]]::new()
  while ($true) {
    $dist = 1 + $idx
    $wordBytes = $null
    try { $wordBytes = Invoke-NetBrotli (New-DictStream $len $dist) } catch { break }
    # transform 0 is the identity: the moment the length changes we have walked
    # past this length's words into the transformed ids.
    if ($null -eq $wordBytes -or $wordBytes.Length -ne $len) { break }
    $words.Add($wordBytes)
    $idx++
    if ($idx -gt 40000) { Write-Host "  len=$len runaway, stopping" -ForegroundColor Red; break }
  }
  $counts[$len] = $words.Count
  $total += $words.Count
  foreach ($wd in $words) { $all.AddRange($wd) }
  Write-Host ("  len={0,2}  words={1,5}  bytes={2,6}" -f $len, $words.Count, ($words.Count * $len))
}

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
[IO.File]::WriteAllBytes($outPath, $all.ToArray())

Write-Host ""
Write-Host ("[dict-extract] {0} words, {1} bytes -> {2}" -f $total, $all.Count, $outPath)
Write-Host ("[dict-extract] counts: " + (($counts.Keys | Sort-Object | ForEach-Object { "$_=$($counts[$_])" }) -join " "))
if ($all.Count -eq 122784) {
  Write-Host "[dict-extract] 122784 bytes -- matches RFC 7932's stated dictionary size." -ForegroundColor Green
} else {
  Write-Host ("[dict-extract] NOTE: {0} bytes, not the 122784 the RFC states. Do not ship until this is understood." -f $all.Count) -ForegroundColor Yellow
}
