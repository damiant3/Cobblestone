# BACKLOG 5.13 -- build streams that USE a word transform, for the decoder test.
#
# Nothing our encoder emits carries a transform, so the decoder's transform path
# has nothing to exercise it. These streams are built here, by an independent
# implementation, and their expected output is what .NET decodes them to -- so
# the test compares our decoder against the oracle rather than against itself.
#
# Emits Codex list literals on stdout for pasting into
# codex/test/lib/brotli-dict-test.codex.
#
#   pwsh build/brotli-xform-cases.ps1

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
function Get-DistOffset([int]$dc) { $nb = Get-DistNbits $dc; return ((2 + (($dc - 16) -band 1)) -shl $nb) - 4 }
function Get-DistCode([long]$d) {
  for ($dc = 16; $dc -lt 64; $dc++) {
    $off = Get-DistOffset $dc
    if ($d -gt $off -and $d -le $off + [math]::Pow(2, (Get-DistNbits $dc))) { return $dc }
  }
  return -1
}
function New-DictStream([int]$len, [long]$dist, [int]$mlen) {
  $w = [BW]::new()
  $w.Bits(0, 1); $w.Bits(1, 1); $w.Bits(0, 1)
  $w.Bits(0, 2); $w.Bits($mlen - 1, 16)
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
  $w.Bits([int]($dist - (Get-DistOffset $dc) - 1), (Get-DistNbits $dc))
  return $w.Done()
}
function Invoke-NetBrotli([byte[]]$blob) {
  $in = [IO.MemoryStream]::new($blob)
  $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.MemoryStream]::new(); $ds.CopyTo($o); $ds.Dispose(); return $o.ToArray()
}

$counts = @(1024,1024,2048,2048,1024,1024,1024,1024,1024,512,512,256,128,128,256,128,128,64,64,32,32)
$corpus = [IO.File]::ReadAllBytes((Join-Path $root "build-output/brotli-dict.bin"))
$offs = @(); $a = 0
for ($i = 0; $i -lt 21; $i++) { $offs += $a; $a += $counts[$i] * ($i + 4) }
function Get-Word([int]$len, [int]$idx) { $o = $offs[$len-4] + $idx*$len; return ,($corpus[$o..($o+$len-1)]) }

# Pick a non-ASCII word so the case transform's UTF-8 rule is exercised, not just
# its ASCII shortcut.
$hi = -1
for ($k = 0; $k -lt $counts[2]; $k++) {
  $w = Get-Word 6 $k
  if (($w | Where-Object { $_ -ge 128 }).Count -gt 0) { $hi = $k; break }
}

# len, index, transform, label
$cases = @(
  @(4, 0,   1, 'suffix-space'),
  @(4, 0,   9, 'ferment-first'),
  @(4, 0,   5, 'suffix-the'),
  @(6, 0,  44, 'omit-or-affix'),
  @(6, $hi, 9, 'ferment-non-ascii')
)

foreach ($c in $cases) {
  $len = $c[0]; $idx = $c[1]; $t = $c[2]; $label = $c[3]
  $nw = $counts[$len - 4]
  [long]$dist = 1 + $idx + [long]$t * $nw
  $mlen = -1; $got = $null
  for ($m = 1; $m -le $len + 26; $m++) {
    try { $b = Invoke-NetBrotli (New-DictStream $len $dist $m) } catch { continue }
    if ($null -ne $b -and $b.Length -eq $m) { $mlen = $m; $got = $b; break }
  }
  if ($mlen -lt 0) { Write-Host "  $label : NO LENGTH FITS" -ForegroundColor Red; continue }
  $stream = New-DictStream $len $dist $mlen
  $txt = -join ($got | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
  Write-Host ""
  Write-Host ("  -- {0}: len={1} idx={2} t={3} -> '{4}'" -f $label, $len, $idx, $t, $txt)
  Write-Host ("  xs-{0} : List Integer = [{1}]" -f $label, (($stream | ForEach-Object { $_ }) -join ', '))
  Write-Host ("  want-{0} : List Integer = [{1}]" -f $label, (($got | ForEach-Object { $_ }) -join ', '))
}
