# BACKLOG 5.13 -- recover RFC 7932's 121 word transforms from the oracle.
#
# Same method and same reason as build/brotli-dict-extract.ps1: the table has to
# be byte-exact because a real decoder applies ITS copy, so an approximated table
# decodes to the wrong bytes rather than to a worse ratio. It is not in this
# environment and cannot honestly be written from memory.
#
# A word id is `index + transform * NWORDS[len]` (RFC 7932 section 8), and at the
# start of a stream max_distance is zero, so distance `1 + index + t * NWORDS[len]`
# with copy length `len` asks the oracle for word `index` under transform `t`.
#
# Each transform is a (prefix, type, suffix) triple. The type is one of 21:
# identity, uppercase-first, uppercase-all, omit-first-1..9, omit-last-1..9. This
# probes several words per transform and DERIVES the triple rather than assuming
# it -- then verifies the derivation reproduces the oracle for every word tried.
#
#   pwsh build/brotli-xform-extract.ps1
#   pwsh build/brotli-xform-extract.ps1 -Out build-output/brotli-xform.txt

param(
  [string]$Out = "build-output/brotli-xform.txt"
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
function Get-DistCode([long]$d) {
  for ($dc = 16; $dc -lt 64; $dc++) {
    $off = Get-DistOffset $dc
    if ($d -gt $off -and $d -le $off + [math]::Pow(2, (Get-DistNbits $dc))) { return $dc }
  }
  return -1
}

# THE META-BLOCK LENGTH IS THE TRANSFORMED LENGTH, NOT THE WORD LENGTH, and that
# is the whole difficulty of this probe. The copy length selects which length
# bucket the word comes from; what the reference actually PRODUCES is
# prefix + transformed-word + suffix, which is longer. A stream whose MLEN says
# the word length makes the decoder stop mid-copy and return nothing, which is
# why the first cut of this recovered transform 0 and failed the other 120.
#
# The produced length is constant for a given (transform, word length), so it is
# scanned once per transform and then reused.
function New-DictStream([int]$len, [long]$dist, [int]$mlen) {
  $w = [BW]::new()
  $w.Bits(0, 1)
  $w.Bits(1, 1); $w.Bits(0, 1)
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
  $nb = Get-DistNbits $dc
  $w.Bits([int]($dist - (Get-DistOffset $dc) - 1), $nb)
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

$binPath = Join-Path $root "build-output/brotli-dict.bin"
if (-not (Test-Path $binPath)) { throw "run build/brotli-dict-extract.ps1 first (need the corpus to know the words)" }
$corpus = [IO.File]::ReadAllBytes($binPath)
$counts = @(1024,1024,2048,2048,1024,1024,1024,1024,1024,512,512,256,128,128,256,128,128,64,64,32,32)
$offs = @(); $a = 0
for ($i = 0; $i -lt 21; $i++) { $offs += $a; $a += $counts[$i] * ($i + 4) }

function Get-Word([int]$len, [int]$idx) {
  $o = $offs[$len - 4] + $idx * $len
  return ,($corpus[$o..($o + $len - 1)])
}

# EVERYTHING HERE IS BYTES, NOT STRINGS. The corpus carries 23059 non-ASCII bytes
# (Arabic and CJK among them), and RFC 7932's case transform is defined on UTF-8
# bytes, not on characters: below 0xC0 flip bit 5 of an ASCII lower-case letter,
# below 0xE0 flip bit 5 of the SECOND byte, otherwise flip bit 2 of the THIRD.
# Deriving this table through .NET string casing agrees on ASCII words and
# silently disagrees on every other one.
function Up-Bytes([byte[]]$w, [bool]$all) {
  $p = [byte[]]::new($w.Length)
  [Array]::Copy($w, $p, $w.Length)
  $i = 0
  while ($i -lt $p.Length) {
    if ($p[$i] -lt 0xc0) {
      if ($p[$i] -ge 97 -and $p[$i] -le 122) { $p[$i] = $p[$i] -bxor 32 }
      $i += 1
    } elseif ($p[$i] -lt 0xe0) {
      if ($i + 1 -lt $p.Length) { $p[$i+1] = $p[$i+1] -bxor 32 }
      $i += 2
    } else {
      if ($i + 2 -lt $p.Length) { $p[$i+2] = $p[$i+2] -bxor 5 }
      $i += 3
    }
    if (-not $all) { break }
  }
  return ,$p
}

function Bytes-Eq([byte[]]$a, [byte[]]$b) {
  if ($a.Length -ne $b.Length) { return $false }
  for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { return $false } }
  return $true
}

function Hex-Of([byte[]]$a) { if ($null -eq $a -or $a.Length -eq 0) { return "" }; return ($a | ForEach-Object { $_.ToString() }) -join "," }

# The 21 transform types, in the RFC's own order. Each is applied to the word
# BETWEEN the prefix and the suffix.
$types = @('Identity','FermentFirst','FermentAll')
for ($k = 1; $k -le 9; $k++) { $types += "OmitFirst$k" }
for ($k = 1; $k -le 9; $k++) { $types += "OmitLast$k" }

function Apply-Type([string]$t, [byte[]]$w) {
  if ($t -eq 'Identity') { return ,$w }
  if ($t -eq 'FermentFirst') { if ($w.Length -eq 0) { return ,$w }; return ,(Up-Bytes $w $false) }
  if ($t -eq 'FermentAll') { return ,(Up-Bytes $w $true) }
  if ($t -like 'OmitFirst*') { $k = [int]$t.Substring(9); if ($w.Length -le $k) { return ,([byte[]]::new(0)) }; return ,($w[$k..($w.Length-1)]) }
  if ($t -like 'OmitLast*')  { $k = [int]$t.Substring(8); if ($w.Length -le $k) { return ,([byte[]]::new(0)) }; return ,($w[0..($w.Length-$k-1)]) }
  throw "unknown type $t"
}

function Cat-Bytes([byte[]]$a, [byte[]]$b, [byte[]]$c) {
  $r = [byte[]]::new($a.Length + $b.Length + $c.Length)
  [Array]::Copy($a, 0, $r, 0, $a.Length)
  [Array]::Copy($b, 0, $r, $a.Length, $b.Length)
  [Array]::Copy($c, 0, $r, $a.Length + $b.Length, $c.Length)
  return ,$r
}

# Probe words must be LONGER THAN THE LARGEST OMIT, or the transform that drops
# nine characters leaves nothing and produces an empty meta-block, which cannot
# be asked for. Nine-character words failed exactly two transforms for that
# reason. Fourteen leaves five characters under the worst omit.
#
# They must also be all lower case, or FermentFirst and FermentAll are
# indistinguishable from Identity and the derivation picks the wrong type.
$probeLen = 14
$probeIdx = @()
$ix = 0
while ($probeIdx.Count -lt 8 -and $ix -lt $counts[$probeLen - 4]) {
  $pw = Get-Word $probeLen $ix; $lowOnly = $true; foreach ($bb in $pw) { if ($bb -lt 97 -or $bb -gt 122) { $lowOnly = $false; break } }; if ($lowOnly) { $probeIdx += $ix }
  $ix++
}
if ($probeIdx.Count -lt 8) { throw "not enough all-lower-case words at length $probeLen" }
$probeWords = @()
foreach ($k in $probeIdx) { $probeWords += ,(Get-Word $probeLen $k) }
$nw = $counts[$probeLen - 4]

Write-Host "[xform] probe words (len $probeLen): $(($probeWords | ForEach-Object { [Text.Encoding]::ASCII.GetString($_) }) -join ', ')"

function Get-Xformed([int]$len, [long]$dist, [int]$mlen) {
  $b = $null
  try { $b = Invoke-NetBrotli (New-DictStream $len $dist $mlen) } catch { return $null }
  if ($null -eq $b -or $b.Length -ne $mlen) { return $null }
  return ,$b
}

# Scan for the produced length once per transform, then reuse it.
function Find-Mlen([int]$len, [long]$dist) {
  $lo = [Math]::Max(1, $len - 9)
  for ($m = $lo; $m -le $len + 26; $m++) {
    $s = Get-Xformed $len $dist $m
    if ($null -ne $s) { return $m }
  }
  return -1
}

$rows = @()
$failed = 0
for ($t = 0; $t -lt 121; $t++) {
  [long]$d0 = 1 + $probeIdx[0] + [long]$t * $nw
  $mlen = Find-Mlen $probeLen $d0
  if ($mlen -lt 0) { Write-Host "[xform] transform $t : NO LENGTH FITS" -ForegroundColor Red; $failed++; continue }
  $outs = @()
  $ok = $true
  foreach ($ix in $probeIdx) {
    [long]$dist = 1 + $ix + [long]$t * $nw
    $s = Get-Xformed $probeLen $dist $mlen
    if ($null -eq $s) { $ok = $false; break }
    $outs += ,$s
  }
  if (-not $ok) { Write-Host "[xform] transform $t : NO OUTPUT" -ForegroundColor Red; $failed++; continue }

  # Derive (prefix, type, suffix): try every type, and for each compute what
  # prefix and suffix would have to be from the FIRST probe, then require that
  # triple to reproduce EVERY other probe. A type that only fits one word is not
  # the type.
  $found = $null
  foreach ($ty in $types) {
    $core0 = Apply-Type $ty $probeWords[0]
    if ($core0.Length -eq 0) { continue }
    # locate the core inside the first output, by bytes
    $i0 = -1
    for ($s = 0; $s -le $outs[0].Length - $core0.Length; $s++) {
      $m = $true
      for ($q = 0; $q -lt $core0.Length; $q++) { if ($outs[0][$s+$q] -ne $core0[$q]) { $m = $false; break } }
      if ($m) { $i0 = $s; break }
    }
    if ($i0 -lt 0) { continue }
    $pre = [byte[]]::new(0)
    if ($i0 -gt 0) { $pre = [byte[]]($outs[0][0..($i0-1)]) }
    $sufStart = $i0 + $core0.Length
    $suf = [byte[]]::new(0)
    if ($sufStart -lt $outs[0].Length) { $suf = [byte[]]($outs[0][$sufStart..($outs[0].Length-1)]) }
    $all = $true
    for ($j = 0; $j -lt $probeWords.Count; $j++) {
      $want = Cat-Bytes $pre (Apply-Type $ty $probeWords[$j]) $suf
      if (-not (Bytes-Eq $outs[$j] $want)) { $all = $false; break }
    }
    if ($all) { $found = [PSCustomObject]@{ Pre = $pre; Ty = $ty; Suf = $suf }; break }
  }
  if ($null -eq $found) {
    Write-Host "[xform] transform $t : COULD NOT DERIVE (out0=$(Hex-Of $outs[0]))" -ForegroundColor Red
    $failed++
    continue
  }
  $rows += [PSCustomObject]@{ T = $t; Pre = $found.Pre; Ty = $found.Ty; Suf = $found.Suf }
}

if ($failed -gt 0) { throw "$failed transforms could not be derived -- do not ship this table" }

# Independent check: a DIFFERENT length's words, every transform, must also be
# reproduced by the derived table. The derivation used length 9 only.
$vLen = 6
$vnw = $counts[$vLen - 4]

# Deliberately include words that are NOT ASCII. The derivation used all-lower
# ASCII probes, so the case transforms would agree with a string-based reading on
# every one of them; Arabic and CJK words are the only place that reading breaks.
$asciiIdx = @(0, 7, 33)
$hiIdx = @()
for ($k = 0; $k -lt $vnw -and $hiIdx.Count -lt 4; $k++) {
  $w = Get-Word $vLen $k
  if (($w | Where-Object { $_ -ge 128 }).Count -gt 0) { $hiIdx += $k }
}
$vIdx = $asciiIdx + $hiIdx
Write-Host "[xform] verifying on $($asciiIdx.Count) ASCII and $($hiIdx.Count) non-ASCII words at length $vLen"

$bad = 0
$checked = 0
$checkedHi = 0
foreach ($row in $rows) {
  $t = $row.T; $pre = $row.Pre; $ty = $row.Ty; $suf = $row.Suf
  foreach ($ix in $vIdx) {
    $word = Get-Word $vLen $ix
    $want = Cat-Bytes $pre (Apply-Type $ty $word) $suf
    if ($want.Length -eq 0) { continue }
    [long]$dist = 1 + $ix + [long]$t * $vnw
    $got = Get-Xformed $vLen $dist $want.Length
    if ($null -eq $got) { Write-Host "[xform] VERIFY NO-OUTPUT t=$t idx=$ix want=$(Hex-Of $want)" -ForegroundColor Red; $bad++; continue }
    $checked++
    if ($hiIdx -contains $ix) { $checkedHi++ }
    if (-not (Bytes-Eq $got $want)) {
      Write-Host "[xform] VERIFY FAIL t=$t idx=$ix got=$(Hex-Of $got) want=$(Hex-Of $want)" -ForegroundColor Red
      $bad++
    }
  }
}
Write-Host "[xform] verified $checked (transform, word) pairs at length $vLen, $checkedHi of them non-ASCII"
if ($bad -gt 0) { throw "$bad verification mismatches on a length the derivation did not use -- table is wrong" }

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
$lines = $rows | ForEach-Object { "{0}`t{1}`t{2}`t{3}" -f $_.T, (Hex-Of $_.Pre), $_.Ty, (Hex-Of $_.Suf) }
[IO.File]::WriteAllLines($outPath, $lines)

Write-Host ""
Write-Host ("[xform] {0} transforms derived and verified against a second length -> {1}" -f $rows.Count, $Out) -ForegroundColor Green
$rows | Select-Object -First 12 | ForEach-Object { "  t={0,3}  pre=[{1}]  {2}  suf=[{3}]" -f $_.T, (Hex-Of $_.Pre), $_.Ty, (Hex-Of $_.Suf) }
