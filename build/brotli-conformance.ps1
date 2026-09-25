# RFC 7932 DECODER CONFORMANCE, one arm per section of the format. Each arm is a
# stream written here by hand from the RFC text, not by any encoder, so it can
# reach constructs no encoder we can run chooses to emit.
#
#   pwsh build/brotli-conformance.ps1            # build every arm, grade it with .NET
#   pwsh build/brotli-conformance.ps1 -Emit      # and write the test chapter + .expected
#   pwsh build/brotli-conformance.ps1 -Only msb6 # one arm, printing its stream
#
# The expected line of every arm comes from .NET's BrotliStream decoding the
# stream, never from this script's own idea of what the stream means. The
# script also computes the bytes it INTENDED and refuses to emit an arm whose
# stream .NET decodes to anything else, so a writer bug here cannot become an
# expectation. A REFUSE arm is a stream the RFC calls invalid; .NET must throw
# on it, or the arm is dropped as not demonstrating what it names.
#
# Runs on the host only: no guest, no compile.

param(
  [switch]$Emit,
  [string]$Only = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# ---------------------------------------------------------------- bit writer

class BitW {
  [System.Collections.Generic.List[byte]]$B = [System.Collections.Generic.List[byte]]::new()
  [int]$Pos = 0
  [void] Bits([long]$v, [int]$n) {
    for ($i = 0; $i -lt $n; $i++) {
      if (($this.Pos % 8) -eq 0) { $this.B.Add(0) }
      if ((($v -shr $i) -band 1) -eq 1) {
        $k = [math]::Floor($this.Pos / 8)
        $this.B[$k] = [byte]($this.B[$k] -bor (1 -shl ($this.Pos % 8)))
      }
      $this.Pos++
    }
  }
  # A prefix code is packed starting with its most significant bit (RFC 7932 1.5.1).
  [void] Code([int]$code, [int]$len) {
    for ($i = $len - 1; $i -ge 0; $i--) { $this.Bits((($code -shr $i) -band 1), 1) }
  }
  [void] Align() { while (($this.Pos % 8) -ne 0) { $this.Bits(0, 1) } }
  [byte[]] Bytes() { return $this.B.ToArray() }
}

# ---------------------------------------------------------------- tables (RFC 7932 5, 6)

$InsBase  = @(0,1,2,3,4,5,6,8,10,14,18,26,34,50,66,98,130,194,322,578,1090,2114,6210,22594)
$InsExtra = @(0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,7,8,9,10,12,14,24)
$CpyBase  = @(2,3,4,5,6,7,8,9,10,12,14,18,22,30,38,54,70,102,134,198,326,582,1094,2118)
$CpyExtra = @(0,0,0,0,0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,7,8,9,10,24)
$BlkBase  = @(1,5,9,13,17,25,33,41,49,65,81,97,113,145,177,209,241,305,369,497,753,1265,2289,4337,8433,16625)
$BlkExtra = @(2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,7,8,9,10,11,12,13,24)
$ClOrder  = @(1,2,3,4,0,5,17,6,16,7,8,9,10,11,12,13,14,15)
# The fixed code for code-length code lengths (3.5), as value and width read LSB first.
$ClfVal = @{ 0 = @(0,2); 1 = @(7,4); 2 = @(3,3); 3 = @(2,2); 4 = @(1,2); 5 = @(15,4) }

function Get-CodeOf([int[]]$base, [int[]]$extra, [int]$v) {
  for ($c = $base.Count - 1; $c -ge 0; $c--) {
    if ($v -ge $base[$c] -and $v -lt $base[$c] + (1 -shl $extra[$c])) { return $c }
  }
  throw "no code for $v"
}

function Get-AlphaBits([int]$n) { $b = 0; while ((1 -shl $b) -lt $n) { $b++ }; return $b }

# Canonical codes from lengths, equal lengths in increasing symbol order (3.2).
function Get-Canon([int[]]$lens) {
  $max = ($lens | Measure-Object -Maximum).Maximum
  $blc = New-Object int[] ($max + 2)
  foreach ($l in $lens) { if ($l -gt 0) { $blc[$l]++ } }
  $next = New-Object int[] ($max + 2); $code = 0
  for ($b = 1; $b -le $max; $b++) { $code = ($code + $blc[$b - 1]) -shl 1; $next[$b] = $code }
  $codes = @{}
  for ($s = 0; $s -lt $lens.Count; $s++) {
    $l = $lens[$s]
    if ($l -gt 0) { $codes[$s] = @($next[$l], $l); $next[$l]++ }
  }
  return $codes
}

# Huffman lengths for the used symbols, flattened when deeper than the limit.
function Get-HuffLens([hashtable]$freq, [int]$alpha, [int]$limit) {
  $lens = New-Object int[] $alpha
  $syms = @($freq.Keys | Sort-Object)
  if ($syms.Count -eq 1) { $lens[$syms[0]] = 1; return ,$lens }
  $nodes = [System.Collections.Generic.List[object]]::new()
  foreach ($s in $syms) { $nodes.Add(@{ F = [long]$freq[$s]; S = @($s) }) }
  $depth = @{}; foreach ($s in $syms) { $depth[$s] = 0 }
  while ($nodes.Count -gt 1) {
    $sorted = @($nodes | Sort-Object { $_.F })
    $a = $sorted[0]; $b = $sorted[1]
    [void]$nodes.Remove($a); [void]$nodes.Remove($b)
    foreach ($s in ($a.S + $b.S)) { $depth[$s]++ }
    $nodes.Add(@{ F = $a.F + $b.F; S = ($a.S + $b.S) })
  }
  $deep = ($depth.Values | Measure-Object -Maximum).Maximum
  if ($deep -gt $limit) {
    # Flat and complete: k symbols over lengths L and L+1.
    $k = $syms.Count; $L = 0; while ((1 -shl ($L + 1)) -le $k) { $L++ }
    $longN = 2 * ($k - (1 -shl $L))
    for ($i = 0; $i -lt $k; $i++) { $lens[$syms[$i]] = if ($i -lt $k - $longN) { $L } else { $L + 1 } }
    return ,$lens
  }
  foreach ($s in $syms) { $lens[$s] = $depth[$s] }
  return ,$lens
}

# ---------------------------------------------------------------- prefix code writers (3.4, 3.5)

# A simple code. $syms in the ORDER written, which decides the lengths for NSYM 3 and 4.
function Write-Simple([BitW]$w, [int[]]$syms, [int]$alpha, [int]$treeSel) {
  $ab = Get-AlphaBits $alpha
  $w.Bits(1, 2); $w.Bits($syms.Count - 1, 2)
  foreach ($s in $syms) { $w.Bits($s, $ab) }
  if ($syms.Count -eq 4) { $w.Bits($treeSel, 1) }
  $lens = New-Object int[] $alpha
  $pos = switch ($syms.Count) { 1 { @(0) } 2 { @(1,1) } 3 { @(1,2,2) } 4 { if ($treeSel -eq 0) { @(2,2,2,2) } else { @(1,2,3,3) } } }
  if ($syms.Count -eq 1) { return @{ Single = $syms[0] } }
  for ($i = 0; $i -lt $syms.Count; $i++) { $lens[$syms[$i]] = $pos[$i] }
  return @{ Codes = (Get-Canon $lens) }
}

# The fewest repeat codes that make exactly $n entries: a chain of one code whose
# counts compound, count' = (count - 2) << shift + (min..max) (3.5). $null when
# $n is not reachable by one chain.
function Get-RepeatChain([int]$n, [int]$shift) {
  $min = 3; $max = 2 + (1 -shl $shift)
  if ($n -ge $min -and $n -le $max) { return ,@($n - $min) }
  for ($e = 0; $e -le $max - $min; $e++) {
    $rest = $n - $min - $e
    if ($rest -gt 0 -and ($rest % (1 -shl $shift)) -eq 0) {
      $prev = ($rest -shr $shift) + 2
      $chain = Get-RepeatChain $prev $shift
      if ($null -ne $chain) { return ,(@($chain) + @($e)) }
    }
  }
  return $null
}

# The code-length symbols that spell $lens: literal lengths, zero runs as 17
# chains, and (when $use16) runs of the previous non-zero length as 16 chains.
function Get-ClSeq([int[]]$lens, [bool]$use16) {
  $last = -1; for ($i = 0; $i -lt $lens.Count; $i++) { if ($lens[$i] -ne 0) { $last = $i } }
  $seq = [System.Collections.Generic.List[object]]::new()
  $i = 0; $prev = 8
  while ($i -le $last) {
    $v = $lens[$i]; $j = $i; while ($j -le $last -and $lens[$j] -eq $v) { $j++ }
    $run = $j - $i
    if ($v -eq 0 -and $run -ge 3) {
      $chain = $null; $lead = 0
      while ($null -eq $chain -and $run - $lead -ge 3) { $chain = Get-RepeatChain ($run - $lead) 3; if ($null -eq $chain) { $lead++ } }
      for ($k = 0; $k -lt $lead; $k++) { $seq.Add(@(0, 0, 0)) }
      if ($null -ne $chain) { foreach ($e in $chain) { $seq.Add(@(17, $e, 3)) } } else { for ($k = $lead; $k -lt $run; $k++) { $seq.Add(@(0, 0, 0)) } }
    } elseif ($v -ne 0 -and $use16 -and $v -eq $prev -and $run -ge 3) {
      $chain = $null; $lead = 0
      while ($null -eq $chain -and $run - $lead -ge 3) { $chain = Get-RepeatChain ($run - $lead) 2; if ($null -eq $chain) { $lead++ } }
      for ($k = 0; $k -lt $lead; $k++) { $seq.Add(@($v, 0, 0)) }
      if ($null -ne $chain) { foreach ($e in $chain) { $seq.Add(@(16, $e, 2)) } } else { for ($k = $lead; $k -lt $run; $k++) { $seq.Add(@($v, 0, 0)) } }
    } else {
      for ($k = 0; $k -lt $run; $k++) { $seq.Add(@($v, 0, 0)) }
    }
    if ($v -ne 0) { $prev = $v }
    $i = $j
  }
  return ,$seq
}

# A complex code for $lens (which must be complete, two or more non-zero).
# $opt.Hskip skips leading code-length-code entries; $opt.Use16 allows 16 runs;
# $opt.ForceSeq supplies the code-length symbol sequence directly.
function Write-Complex([BitW]$w, [int[]]$lens, [hashtable]$opt) {
  $hskip = if ($opt.ContainsKey('Hskip')) { $opt.Hskip } else { 0 }
  $seq = if ($opt.ContainsKey('ForceSeq')) { $opt.ForceSeq } else { Get-ClSeq $lens ($opt.ContainsKey('Use16') -and $opt.Use16) }
  $cf = @{}; foreach ($t in $seq) { $cf[$t[0]] = 1 + $(if ($cf.ContainsKey($t[0])) { $cf[$t[0]] } else { 0 }) }
  $cll = Get-HuffLens $cf 18 5
  $nz = @($cll | Where-Object { $_ -gt 0 }).Count
  for ($k = 0; $k -lt $hskip; $k++) { if ($cll[$ClOrder[$k]] -ne 0) { throw "HSKIP $hskip skips a used code-length symbol" } }
  $lastIdx = 17
  if ($nz -ge 2) { $lastIdx = -1; for ($k = 0; $k -lt 18; $k++) { if ($cll[$ClOrder[$k]] -ne 0) { $lastIdx = $k } } }
  $w.Bits($hskip, 2)
  for ($k = $hskip; $k -le $lastIdx; $k++) { $f = $ClfVal[$cll[$ClOrder[$k]]]; $w.Bits($f[0], $f[1]) }
  $clCodes = if ($nz -ge 2) { Get-Canon $cll } else { $null }
  foreach ($t in $seq) {
    if ($null -ne $clCodes) { $c = $clCodes[$t[0]]; $w.Code($c[0], $c[1]) }
    if ($t[2] -gt 0) { $w.Bits($t[1], $t[2]) }
  }
  return @{ Codes = (Get-Canon $lens) }
}

# A code for the used symbols. $form: 'auto' (simple when four or fewer), 'complex',
# or an explicit simple order @{ Simple = @(syms...); Tree = 0|1 }.
function Write-Code([BitW]$w, [hashtable]$freq, [int]$alpha, $form) {
  if ($form -is [hashtable] -and $form.ContainsKey('Simple')) {
    $t = if ($form.ContainsKey('Tree')) { $form.Tree } else { 0 }
    return Write-Simple $w $form.Simple $alpha $t
  }
  if ($form -is [hashtable] -and $form.ContainsKey('Lens')) { return Write-Complex $w $form.Lens $form }
  $syms = @($freq.Keys | Sort-Object)
  if ($syms.Count -eq 0) { $syms = @(0); $freq = @{ 0 = 1 } }
  if ($form -ne 'complex' -and $syms.Count -le 4) {
    if ($syms.Count -eq 4) { return Write-Simple $w $syms $alpha 0 }
    return Write-Simple $w $syms $alpha 0
  }
  if ($syms.Count -eq 1) { $freq = $freq.Clone(); $freq[($syms[0] + 1) % $alpha] = 1 }
  $lens = Get-HuffLens $freq $alpha 15
  $o = if ($form -is [hashtable]) { $form } else { @{} }
  return Write-Complex $w $lens $o
}

function Put-Sym([BitW]$w, $code, [int]$sym) {
  if ($code.ContainsKey('Single')) { if ($sym -ne $code.Single) { throw "symbol $sym through single-symbol code $($code.Single)" }; return }
  $c = $code.Codes[$sym]; if ($null -eq $c) { throw "symbol $sym has no code" }
  $w.Code($c[0], $c[1])
}

# ---------------------------------------------------------------- header fields (9.1, 9.2)

function Write-WBits([BitW]$w, [int]$wb) {
  if ($wb -eq 16) { $w.Bits(0, 1) }
  elseif ($wb -eq 17) { $w.Bits(1, 7) }                       # 0000001
  elseif ($wb -ge 18) { $w.Bits(1, 1); $w.Bits($wb - 17, 3) }
  else { $w.Bits(1, 1); $w.Bits(0, 3); $w.Bits($wb - 8, 3) }
}

function Write-VarLen([BitW]$w, [int]$v) {
  if ($v -le 1) { $w.Bits(0, 1); return }
  $n = 0; while ((2 -shl $n) -le ($v - 1)) { $n++ }
  $w.Bits(1, 1); $w.Bits($n, 3); $w.Bits($v - 1 - (1 -shl $n), $n)
}

function Write-MLen([BitW]$w, [int]$mlen) {
  $nib = 4; while ((($mlen - 1) -shr (4 * $nib)) -ne 0) { $nib++ }
  $w.Bits($nib - 4, 2); $w.Bits($mlen - 1, 4 * $nib)
}

# ---------------------------------------------------------------- context (7.1, 7.2)

$Lut0 = @(0,0,0,0,0,0,0,0,0,4,4,0,0,4,0,0, 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
  8,12,16,12,12,20,12,16,24,28,12,12,32,12,36,12, 44,44,44,44,44,44,44,44,44,44,32,32,24,40,28,12,
  12,48,52,52,52,48,52,52,52,48,52,52,52,52,52,48, 52,52,52,52,52,48,52,52,52,52,52,24,12,28,12,12,
  12,56,60,60,60,56,60,60,60,56,60,60,60,60,60,56, 60,60,60,60,60,56,60,60,60,60,60,24,12,28,12,0) +
  (@(0,1) * 32) + (@(2,3) * 32)
$Lut1 = (@(0) * 32) + @(0) + (@(1) * 15) + (@(2) * 10) + (@(1) * 6) + @(1) + (@(2) * 26) + (@(1) * 5) +
  @(1) + (@(3) * 26) + @(1,1,1,1,0) + (@(0) * 96) + (@(2) * 32)
$Lut2 = @(0) + (@(1) * 15) + (@(2) * 48) + (@(3) * 64) + (@(4) * 64) + (@(5) * 48) + (@(6) * 15) + @(7)
if ($Lut0.Count -ne 256 -or $Lut1.Count -ne 256 -or $Lut2.Count -ne 256) { throw "context table size" }

function Get-Ctx([int]$mode, [int]$p1, [int]$p2) {
  switch ($mode) {
    0 { return $p1 -band 63 }
    1 { return $p1 -shr 2 }
    2 { return $Lut0[$p1] -bor $Lut1[$p2] }
    3 { return ($Lut2[$p1] -shl 3) -bor $Lut2[$p2] }
  }
}
function Get-DCtx([int]$copy) { if ($copy -le 4) { return $copy - 2 } else { return 3 } }

# ---------------------------------------------------------------- distances (4)

# The distance code for a backward distance under NPOSTFIX / NDIRECT, using a
# direct code when one exists.
function Get-DistCode([long]$d, [int]$np, [int]$nd) {
  if ($d -le $nd) { return @{ Code = 15 + $d; Extra = 0; NB = 0 } }
  $dd = $d - $nd - 1
  $lcode = $dd -band ((1 -shl $np) - 1)
  $x = ($dd -shr $np) + 4
  $nb = 0; while ((4L -shl $nb) -le $x) { $nb++ }
  $top = $x -shr $nb                      # 2 or 3
  $h = 2 * ($nb - 1) + ($top - 2)
  $extra = $x - ($top -shl $nb)
  return @{ Code = $nd + 16 + ($h -shl $np) + $lcode; Extra = $extra; NB = $nb }
}

function Resolve-Short([int]$code, [long[]]$ring) {
  # $ring is most recent first
  switch ($code) {
    0 { return $ring[0] } 1 { return $ring[1] } 2 { return $ring[2] } 3 { return $ring[3] }
    4 { return $ring[0] - 1 } 5 { return $ring[0] + 1 } 6 { return $ring[0] - 2 } 7 { return $ring[0] + 2 }
    8 { return $ring[0] - 3 } 9 { return $ring[0] + 3 } 10 { return $ring[1] - 1 } 11 { return $ring[1] + 1 }
    12 { return $ring[1] - 2 } 13 { return $ring[1] + 2 } 14 { return $ring[1] - 3 } 15 { return $ring[1] + 3 }
  }
}

# ---------------------------------------------------------------- insert-and-copy (5)

function Get-IcSym([int]$ins, [int]$cpy, [bool]$implicit) {
  $ic = Get-CodeOf $InsBase $InsExtra $ins
  $cc = Get-CodeOf $CpyBase $CpyExtra $cpy
  if ($implicit) {
    if ($ic -ge 8 -or $cc -ge 16) { throw "implicit distance needs insert code < 8 and copy code < 16" }
    $base = if ($cc -lt 8) { 0 } else { 64 }
  } else {
    $row = [math]::Floor($ic / 8); $col = [math]::Floor($cc / 8)
    $base = (@(@(128,192,384), @(256,320,512), @(448,576,640)))[$row][$col]
  }
  return @{ Sym = $base + (($ic -band 7) -shl 3) + ($cc -band 7); Ic = $ic; Cc = $cc }
}

# ---------------------------------------------------------------- one compressed meta-block

# A block category's switch plan: @( @{ T = type; N = count; Form = 'raw'|'prev'|'next' }, ... ),
# the first entry type 0 (its count goes in the header).
function New-Cat($spec, [int]$n) {
  if ($null -eq $spec) { $spec = @(@{ T = 0; N = 16777216 }) }
  return @{ N = $n; Plan = @($spec); Idx = 0; Left = $spec[0].N; Cur = 0; Prev = 1 }
}

function Get-TypeSym($cat, $entry) {
  $form = if ($entry.ContainsKey('Form')) { $entry.Form } else { 'raw' }
  switch ($form) {
    'prev' { if ($entry.T -ne $cat.Prev) { throw "prev form names $($cat.Prev), not $($entry.T)" }; return 0 }
    'next' { if ($entry.T -ne (($cat.Cur + 1) % $cat.N)) { throw "next form does not name $($entry.T)" }; return 1 }
    default { return $entry.T + 2 }
  }
}

# Walk the commands once to decide every symbol, then write. $m is a hashtable:
#   Cmds     : @( @{ Lit = [byte[]]; Copy = n; Dist = n | ShortCode = k | Implicit = $true; DictDist = n } )
#   NBL, NBI, NBD, LitPlan, IcPlan, DPlan : block types and switch plans
#   Modes    : context mode per literal block type
#   LitMap   : 64*NBL tree indices (default all 0); DMap : 4*NBD
#   NPostfix, NDirectMsb, LitForm (per tree), IcForm (per type), DForm (per tree)
#   CmapLit  : @{ Rle = n; Imtf = $true } encoding options
#   MLen     : override (default: what the commands produce)
function Write-Compressed([BitW]$w, [hashtable]$m, [bool]$isLast, $state) {
  $nbl = if ($m.NBL) { $m.NBL } else { 1 }; $nbi = if ($m.NBI) { $m.NBI } else { 1 }; $nbd = if ($m.NBD) { $m.NBD } else { 1 }
  $modes = if ($m.Modes) { @($m.Modes) } else { @(0) * $nbl }
  $litMap = if ($m.LitMap) { @($m.LitMap) } else { @(0) * (64 * $nbl) }
  $dMap = if ($m.DMap) { @($m.DMap) } else { @(0) * (4 * $nbd) }
  $np = if ($m.NPostfix) { $m.NPostfix } else { 0 }
  $nd = $(if ($m.NDirectMsb) { $m.NDirectMsb } else { 0 }) -shl $np
  $ntl = ($litMap | Measure-Object -Maximum).Maximum + 1
  $ntd = ($dMap | Measure-Object -Maximum).Maximum + 1

  # pass 1: decide symbols
  $out = $state.Out; $ring = $state.Ring
  $L = New-Cat $m.LitPlan $nbl; $I = New-Cat $m.IcPlan $nbi; $D = New-Cat $m.DPlan $nbd
  $events = [System.Collections.Generic.List[object]]::new()
  $litF = @(); for ($t = 0; $t -lt $ntl; $t++) { $litF += @{} }
  $icF = @(); for ($t = 0; $t -lt $nbi; $t++) { $icF += @{} }
  $dF = @(); for ($t = 0; $t -lt $ntd; $t++) { $dF += @{} }
  $swF = @{ L = @(@{}, @{}); I = @(@{}, @{}); D = @(@{}, @{}) }
  $produced = 0
  function Step-Cat($cat, $key) {
    if ($cat.N -lt 2) { return $null }
    if ($cat.Left -gt 0) { return $null }
    $cat.Idx++
    if ($cat.Idx -ge $cat.Plan.Count) { throw "$key block plan exhausted" }
    $e = $cat.Plan[$cat.Idx]
    $ts = Get-TypeSym $cat $e
    $bc = Get-CodeOf $BlkBase $BlkExtra $e.N
    $cat.Prev = $cat.Cur; $cat.Cur = $e.T; $cat.Left = $e.N
    return @{ K = $key; TS = $ts; BC = $bc; BX = $e.N - $BlkBase[$bc] }
  }
  function Count-Sw($ev) {
    $f = $swF[$ev.K]
    $f[0][$ev.TS] = 1 + $(if ($f[0].ContainsKey($ev.TS)) { $f[0][$ev.TS] } else { 0 })
    $f[1][$ev.BC] = 1 + $(if ($f[1].ContainsKey($ev.BC)) { $f[1][$ev.BC] } else { 0 })
  }
  foreach ($c in $m.Cmds) {
    $lit = if ($c.ContainsKey('Lit')) { @($c.Lit) } else { @() }
    $copy = if ($c.ContainsKey('Copy')) { $c.Copy } else { 2 }
    $implicit = $c.ContainsKey('Implicit') -and $c.Implicit
    $sw = Step-Cat $I 'I'; if ($sw) { $events.Add($sw); Count-Sw $sw }
    $ics = Get-IcSym $lit.Count $copy $implicit
    $icF[$I.Cur][$ics.Sym] = 1 + $(if ($icF[$I.Cur].ContainsKey($ics.Sym)) { $icF[$I.Cur][$ics.Sym] } else { 0 })
    $events.Add(@{ K = 'IC'; T = $I.Cur; Sym = $ics.Sym; Ins = $lit.Count; Cpy = $copy; Ic = $ics.Ic; Cc = $ics.Cc })
    $I.Left--
    foreach ($b in $lit) {
      $sw = Step-Cat $L 'L'; if ($sw) { $events.Add($sw); Count-Sw $sw }
      $p1 = if ($out.Count -ge 1) { $out[$out.Count - 1] } else { 0 }
      $p2 = if ($out.Count -ge 2) { $out[$out.Count - 2] } else { 0 }
      $tree = $litMap[64 * $L.Cur + (Get-Ctx $modes[$L.Cur] $p1 $p2)]
      $litF[$tree][[int]$b] = 1 + $(if ($litF[$tree].ContainsKey([int]$b)) { $litF[$tree][[int]$b] } else { 0 })
      $events.Add(@{ K = 'LIT'; Tree = $tree; B = [int]$b })
      $out.Add([byte]$b); $L.Left--; $produced++
    }
    if ($c.ContainsKey('NoCopy')) { continue }
    if ($implicit) {
      $dist = $ring[0]
    } else {
      $sw = Step-Cat $D 'D'; if ($sw) { $events.Add($sw); Count-Sw $sw }
      $dtree = $dMap[4 * $D.Cur + (Get-DCtx $copy)]
      if ($c.ContainsKey('ShortCode')) {
        $dc = @{ Code = $c.ShortCode; Extra = 0; NB = 0 }; $dist = Resolve-Short $c.ShortCode $ring
      } elseif ($c.ContainsKey('DictDist')) {
        $dc = Get-DistCode $c.DictDist $np $nd; $dist = $c.DictDist
      } else {
        $dc = Get-DistCode $c.Dist $np $nd; $dist = $c.Dist
      }
      $dF[$dtree][$dc.Code] = 1 + $(if ($dF[$dtree].ContainsKey($dc.Code)) { $dF[$dtree][$dc.Code] } else { 0 })
      $events.Add(@{ K = 'DIST'; Tree = $dtree; Code = $dc.Code; Extra = $dc.Extra; NB = $dc.NB })
      $D.Left--
    }
    if ($c.ContainsKey('DictDist')) {
      $state.Opaque = $true
      for ($k = 0; $k -lt $copy; $k++) { $out.Add(0) }   # placeholder: only .NET knows the word
      $produced += $copy
    } else {
      if ($dist -gt $out.Count) { throw "copy distance $dist beyond $($out.Count) produced" }
      for ($k = 0; $k -lt $copy; $k++) { $out.Add($out[$out.Count - $dist]) }
      $produced += $copy
      $shortZero = ($c.ContainsKey('ShortCode') -and $c.ShortCode -eq 0) -or $implicit
      if (-not $shortZero) { $ring = @($dist) + $ring[0..2] }
    }
  }
  $state.Ring = $ring
  $mlen = if ($m.ContainsKey('MLen')) { $m.MLen } else { $produced }

  # pass 2: write
  $w.Bits($(if ($isLast) { 1 } else { 0 }), 1)
  if ($isLast) { $w.Bits(0, 1) }
  Write-MLen $w $mlen
  if (-not $isLast) { $w.Bits(0, 1) }
  $swCodes = @{}
  foreach ($pair in @(@('L', $L, $nbl), @('I', $I, $nbi), @('D', $D, $nbd))) {
    $k = $pair[0]; $cat = $pair[1]; $n = $pair[2]
    Write-VarLen $w $n
    if ($n -ge 2) {
      $first = $cat.Plan[0].N
      $fbc = Get-CodeOf $BlkBase $BlkExtra $first
      $bcF = $swF[$k][1].Clone(); $bcF[$fbc] = 1 + $(if ($bcF.ContainsKey($fbc)) { $bcF[$fbc] } else { 0 })
      $tsForm = if ($m.ContainsKey("${k}TypeForm")) { $m["${k}TypeForm"] } else { 'auto' }
      $tc = Write-Code $w $swF[$k][0] ($n + 2) $tsForm
      $cc = Write-Code $w $bcF 26 'auto'
      Put-Sym $w $cc $fbc; $w.Bits($first - $BlkBase[$fbc], $BlkExtra[$fbc])
      $swCodes[$k] = @($tc, $cc)
    }
  }
  $w.Bits($np, 2); $w.Bits($(if ($m.NDirectMsb) { $m.NDirectMsb } else { 0 }), 4)
  foreach ($md in $modes) { $w.Bits($md, 2) }
  Write-VarLen $w $ntl; if ($ntl -ge 2) { Write-CMap $w $litMap $ntl $m.CmapLit }
  Write-VarLen $w $ntd; if ($ntd -ge 2) { Write-CMap $w $dMap $ntd $m.CmapD }
  $litC = @(); for ($t = 0; $t -lt $ntl; $t++) { $f = if ($m.LitForm -and $m.LitForm[$t]) { $m.LitForm[$t] } else { 'auto' }; $litC += ,(Write-Code $w $litF[$t] 256 $f) }
  $icC = @(); for ($t = 0; $t -lt $nbi; $t++) { $f = if ($m.IcForm -and $m.IcForm[$t]) { $m.IcForm[$t] } else { 'auto' }; $icC += ,(Write-Code $w $icF[$t] 704 $f) }
  $dal = 16 + $nd + (48 -shl $np)
  $dC = @(); for ($t = 0; $t -lt $ntd; $t++) { $f = if ($m.DForm -and $m.DForm[$t]) { $m.DForm[$t] } else { 'auto' }; $dC += ,(Write-Code $w $dF[$t] $dal $f) }
  foreach ($ev in $events) {
    switch ($ev.K) {
      'IC' {
        Put-Sym $w $icC[$ev.T] $ev.Sym
        $w.Bits($ev.Ins - $InsBase[$ev.Ic], $InsExtra[$ev.Ic])
        $w.Bits($ev.Cpy - $CpyBase[$ev.Cc], $CpyExtra[$ev.Cc])
      }
      'LIT' { Put-Sym $w $litC[$ev.Tree] $ev.B }
      'DIST' { Put-Sym $w $dC[$ev.Tree] $ev.Code; $w.Bits($ev.Extra, $ev.NB) }
      default {
        $pc = $swCodes[$ev.K]
        Put-Sym $w $pc[0] $ev.TS; Put-Sym $w $pc[1] $ev.BC; $w.Bits($ev.BX, $BlkExtra[$ev.BC])
      }
    }
  }
}

# A context map (7.3). $opt.Rle = RLEMAX (0 = none), $opt.Imtf = apply move-to-front.
function Write-CMap([BitW]$w, [int[]]$map, [int]$ntrees, $opt) {
  if ($null -eq $opt) { $opt = @{} }
  $rle = if ($opt.Rle) { $opt.Rle } else { 0 }
  $vals = @($map)
  if ($opt.Imtf) {
    $mtf = [System.Collections.Generic.List[int]]::new(); for ($i = 0; $i -lt 256; $i++) { $mtf.Add($i) }
    for ($i = 0; $i -lt $vals.Count; $i++) { $ix = $mtf.IndexOf($vals[$i]); $vals[$i] = $ix; $mtf.RemoveAt($ix); $mtf.Insert(0, $map[$i]) }
  }
  $syms = [System.Collections.Generic.List[object]]::new()
  $i = 0
  while ($i -lt $vals.Count) {
    if ($vals[$i] -eq 0 -and $rle -gt 0) {
      $j = $i; while ($j -lt $vals.Count -and $vals[$j] -eq 0) { $j++ }
      $run = $j - $i
      while ($run -gt 0) {
        if ($run -eq 1) { $syms.Add(@(0, 0, 0)); $run = 0; continue }
        $k = 1; while ($k -lt $rle -and (2 -shl $k) -le $run) { $k++ }
        $take = [math]::Min($run, (2 -shl $k) - 1)
        $syms.Add(@($k, ($take - (1 -shl $k)), $k)); $run -= $take
      }
      $i = $j
    } else {
      $syms.Add(@($(if ($vals[$i] -eq 0) { 0 } else { $vals[$i] + $rle }), 0, 0)); $i++
    }
  }
  if ($rle -eq 0) { $w.Bits(0, 1) } else { $w.Bits(1, 1); $w.Bits($rle - 1, 4) }
  $f = @{}; foreach ($s in $syms) { $f[$s[0]] = 1 + $(if ($f.ContainsKey($s[0])) { $f[$s[0]] } else { 0 }) }
  $code = Write-Code $w $f ($ntrees + $rle) 'auto'
  foreach ($s in $syms) { Put-Sym $w $code $s[0]; if ($s[2] -gt 0) { $w.Bits($s[1], $s[2]) } }
  $w.Bits($(if ($opt.Imtf) { 1 } else { 0 }), 1)
}

# ---------------------------------------------------------------- streams

# $blocks: @( @{ Kind = 'c'; M = @{...} } | @{ Kind = 'stored'; Data = bytes } |
#            @{ Kind = 'meta'; Skip = bytes } | @{ Kind = 'end' } ), last one ends the stream.
function New-Stream([int]$wbits, $blocks) {
  $w = [BitW]::new()
  $state = @{ Out = [System.Collections.Generic.List[byte]]::new(); Ring = @(4, 11, 15, 16); Opaque = $false }
  Write-WBits $w $wbits
  for ($i = 0; $i -lt $blocks.Count; $i++) {
    $b = $blocks[$i]; $last = ($i -eq $blocks.Count - 1)
    switch ($b.Kind) {
      'c' { Write-Compressed $w $b.M $last $state }
      'stored' {
        if ($last) { throw "a stored meta-block cannot be last" }
        $w.Bits(0, 1); Write-MLen $w $b.Data.Count; $w.Bits(1, 1); $w.Align()
        foreach ($x in $b.Data) { $w.Bits($x, 8); $state.Out.Add([byte]$x) }
      }
      'meta' {
        if ($last) { throw "a metadata meta-block cannot be last" }
        $w.Bits(0, 1); $w.Bits(3, 2); $w.Bits(0, 1)
        $n = $b.Skip.Count
        if ($n -eq 0) { $w.Bits(0, 2) }
        else { $nb = 1; while ((($n - 1) -shr (8 * $nb)) -ne 0) { $nb++ }; $w.Bits($nb, 2); $w.Bits($n - 1, 8 * $nb) }
        $w.Align(); foreach ($x in $b.Skip) { $w.Bits($x, 8) }
      }
      'end' { $w.Bits(1, 1); $w.Bits(1, 1) }
    }
  }
  $w.Align()
  return @{ Bytes = $w.Bytes(); Intended = $state.Out.ToArray(); Opaque = $state.Opaque }
}

function Invoke-NetDecode([byte[]]$s) {
  try {
    $in = [IO.MemoryStream]::new($s)
    $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
    $o = [IO.MemoryStream]::new(); $ds.CopyTo($o); $ds.Dispose()
    return @{ Ok = $true; Out = $o.ToArray() }
  } catch { return @{ Ok = $false; Err = $_.Exception.Message } }
}

function Get-RollSum([byte[]]$data) { $acc = 0L; foreach ($b in $data) { $acc = (($acc * 31 + [int]$b) % 1000000007) }; return $acc }

function Get-Ascii([string]$s) { return [Text.Encoding]::ASCII.GetBytes($s) }

# ---------------------------------------------------------------- the arms

$arms = [System.Collections.Generic.List[object]]::new()
function Add-Arm([string]$name, [string]$sec, [string]$wrong, $stream, [switch]$Refuse) {
  $arms.Add(@{ Name = $name; Sec = $sec; Wrong = $wrong; S = $stream; Refuse = [bool]$Refuse })
}

$abc = Get-Ascii "abcdefgh"

# 9.1: every width of the WBITS field, each followed by a stored meta-block, so a
# mis-read width lands the meta-block header on the wrong bit.
foreach ($wb in @(10, 16, 17, 18, 24)) {
  Add-Arm "wbits-$wb" "9.1" "a field width read wrong desynchronises the first meta-block header" (New-Stream $wb @(@{ Kind = 'stored'; Data = $abc }, @{ Kind = 'end' }))
}

# 9.1: the window bounds a backward distance, and a distance past it is a
# dictionary word. WBITS 10 gives a 1008-byte window; 1100 bytes are produced and
# then a copy at distance 1009, which with a larger window is a real copy.
$fill = [byte[]](0..1099 | ForEach-Object { 97 + ($_ % 26) })
Add-Arm "window-10" "9.1" "a window taken from the wrong WBITS reads distance 1009 as a copy, not a dictionary word" (New-Stream 10 @(
  @{ Kind = 'stored'; Data = $fill },
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = @(); Copy = 4; DictDist = 1009 }) } }))

# 9.2: a metadata meta-block carrying bytes, which the decoder must skip whole and
# realign after; then a stored block.
Add-Arm "metadata-skip" "9.2" "refusing the block, emitting the skipped bytes, or resuming off the byte boundary" (New-Stream 16 @(
  @{ Kind = 'meta'; Skip = (Get-Ascii "XYZ") },
  @{ Kind = 'stored'; Data = $abc },
  @{ Kind = 'end' }))

# 3.4: simple codes with the symbols written OUT of order. NSYM 3 gives the FIRST
# symbol written the one-bit code; NSYM 4 tree-select 1 gives lengths 1, 2, 3, 3
# in written order. Sorting before assigning lengths decodes other bytes.
$lit3 = Get-Ascii "zzazbzaazb"
Add-Arm "simple-nsym3" "3.4" "lengths assigned by sorted symbol instead of written order" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = $lit3; NoCopy = $true }); LitForm = @(@{ Simple = @(122, 97, 98) }) } }))
$lit4 = Get-Ascii "qqqqpqrqsqqp"
Add-Arm "simple-nsym4-tree1" "3.4" "tree-select ignored, or lengths assigned by sorted symbol" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = $lit4; NoCopy = $true }); LitForm = @(@{ Simple = @(113, 115, 112, 114); Tree = 1 }) } }))

# 3.5: RFC 7932's own example. Every literal at length 8, written with a
# code-length code whose ONLY symbol is 16, which therefore has a zero-bit code:
# four 16s compound 5 -> 17 -> 65 -> 256. A reader that consumes a bit per symbol
# of a one-symbol code misreads everything after the first.
$all8 = [int[]](@(8) * 256)
$lit8 = [byte[]](0..39 | ForEach-Object { ($_ * 37 + 11) % 256 })
Add-Arm "complex-single-cl-symbol" "3.5" "a one-symbol code-length code read as consuming bits" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = $lit8; NoCopy = $true }); LitForm = @(@{ Lens = $all8; ForceSeq = @(@(16, 2, 2), @(16, 2, 2), @(16, 2, 2), @(16, 1, 2)) }) } }))

# 3.5: HSKIP 3 and a compounding 17 chain. Sixteen letters at length 4, so the
# code-length code uses only 4 and 17 and HSKIP 3 may skip 1, 2 and 3; the zero run
# of 97 before them is three 17s in a row, 3 -> 13 -> 97.
$hi = Get-Ascii "pamblcfkdjegoinhaph"
$hsLens = New-Object int[] 256; foreach ($s in 97..112) { $hsLens[$s] = 4 }
Add-Arm "complex-hskip3-rep17" "3.5" "HSKIP read as zero, or consecutive 17s added instead of compounded" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = $hi; NoCopy = $true }); LitForm = @(@{ Lens = $hsLens; Hskip = 3 }) } }))

# 3.5: a zero run past the end of the alphabet is invalid. Three 17s compound
# 5 -> 34 -> 260 in a 256-symbol literal alphabet.
$ovLens = New-Object int[] 256; $ovLens[97] = 1; $ovLens[98] = 1
Add-Arm "complex-repeat-overrun" "3.5" "a run past the alphabet skipped or clamped instead of refused" -Refuse (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = (Get-Ascii "ab"); NoCopy = $true }); LitForm = @(@{ Lens = $ovLens; ForceSeq = @(@(17, 2, 3), @(17, 7, 3), @(17, 1, 3), @(1, 0, 0), @(1, 0, 0)) }) } }))

# 4: NPOSTFIX 2 and NDIRECT 8 (field 2), a direct code, a general code whose
# low postfix bits are non-zero, and the short codes: code 0 must NOT push the ring,
# so the code 1 after it names the distance before the repeat.
$base = Get-Ascii "0123456789ABCDEFGHIJ"
Add-Arm "dist-postfix-direct-ring" "4" "NDIRECT unscaled, postfix bits dropped, or distance code 0 pushed to the ring" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ NPostfix = 2; NDirectMsb = 2; Cmds = @(
      @{ Lit = $base; Copy = 3; Dist = 7 },
      @{ Lit = (Get-Ascii "k"); Copy = 4; Dist = 19 },
      @{ Lit = (Get-Ascii "m"); Copy = 3; ShortCode = 0 },
      @{ Lit = (Get-Ascii "n"); Copy = 5; ShortCode = 1 },
      @{ Lit = (Get-Ascii "p"); Copy = 2; ShortCode = 11 }) } }))

# 5: long insert and copy lengths with extra bits, and an implicit-distance command.
$long = [byte[]](0..299 | ForEach-Object { 33 + (($_ * 7) % 90) })
Add-Arm "insert-copy-lengths" "5" "insert or copy extra bits read at the wrong width" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(
      @{ Lit = $long; Copy = 150; Dist = 97 },
      @{ Lit = (Get-Ascii "xy"); Copy = 9; Implicit = $true },
      @{ Lit = (Get-Ascii "z"); Copy = 70; Dist = 300 }) } }))

# 6: insert-and-copy block types switched with symbol 0 (the PREVIOUS type) and
# symbol 1 (current + 1, wrapping), each type with its own code.
Add-Arm "blocks-insert-copy" "6" "block type symbol 0 read as stay, or 1 without wrapping" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ NBI = 3;
      IcPlan = @(@{ T = 0; N = 1 }, @{ T = 2; N = 1 }, @{ T = 0; N = 1; Form = 'prev' }, @{ T = 1; N = 1; Form = 'next' }, @{ T = 2; N = 1; Form = 'next' }, @{ T = 0; N = 4; Form = 'next' });
      Cmds = @(
        @{ Lit = (Get-Ascii "ab"); Copy = 2; Dist = 2 },
        @{ Lit = (Get-Ascii "cde"); Copy = 3; Dist = 3 },
        @{ Lit = (Get-Ascii "f"); Copy = 4; Dist = 5 },
        @{ Lit = (Get-Ascii "gh"); Copy = 2; Dist = 4 },
        @{ Lit = (Get-Ascii "ijkl"); Copy = 6; Dist = 9 },
        @{ Lit = (Get-Ascii "m"); NoCopy = $true }) } }))

# 6 and 7.3: LITERAL block types. Two types, each with its own context-map row and
# so its own tree, switched mid-insert. The byte stream is the same symbol set in
# both types, coded differently, so a reader that never switches decodes other bytes.
$lm = @(0) * 64 + @(1) * 64
Add-Arm "blocks-literal" "6" "literal block types not read, or the context map row not offset by the type" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ NBL = 2; LitMap = $lm;
      LitPlan = @(@{ T = 0; N = 5 }, @{ T = 1; N = 6 }, @{ T = 0; N = 3; Form = 'prev' }, @{ T = 1; N = 50; Form = 'next' });
      Cmds = @(@{ Lit = (Get-Ascii "aabacaadbbccabdd"); NoCopy = $true });
      LitForm = @(@{ Simple = @(97, 98, 99, 100); Tree = 1 }, @{ Simple = @(100, 99, 98, 97); Tree = 1 }) } }))

# 7.1: each context mode, with a context map that sends the contexts the mode
# separates to DIFFERENT trees. The literal after each byte is chosen where the
# mode under test and LSB6 name different trees.
function New-ModeArm([int]$mode, [byte[]]$bytes) {
  $map = @(0) * 64
  # contexts reached, each given tree (ctx mod 2)+... decided below from the data
  $p1 = 0; $p2 = 0; $seen = @{}
  foreach ($b in $bytes) { $cx = Get-Ctx $mode $p1 $p2; $seen[$cx] = 1; $p2 = $p1; $p1 = [int]$b }
  $k = 0; foreach ($cx in ($seen.Keys | Sort-Object)) { $map[$cx] = 1 + ($k % 2); $k++ }
  return New-Stream 16 @(@{ Kind = 'c'; M = @{ Modes = @($mode); LitMap = $map; Cmds = @(@{ Lit = $bytes; NoCopy = $true }) } })
}
Add-Arm "ctx-msb6" "7.1" "MSB6 computed as LSB6" (New-ModeArm 1 (Get-Ascii '@AP`p@`AaPpqQ1!0 '))
Add-Arm "ctx-utf8" "7.1" "UTF8 computed as LSB6" (New-ModeArm 2 (Get-Ascii "Ab, cD. eF; 12 ab'x"))
Add-Arm "ctx-signed" "7.1" "Signed computed as LSB6" (New-ModeArm 3 ([byte[]]@(0,1,255,254,128,127,64,192,2,253,3,252,0,0,129)))

# 7.1: one context mode PER LITERAL BLOCK TYPE. Type 0 LSB6, type 1 MSB6; the
# mode of type 0 applied to both decodes type 1's literals with the wrong trees.
$pm = @(0) * 128
foreach ($cx in 0..63) { $pm[$cx] = $cx % 2; $pm[64 + $cx] = 2 + ($cx % 2) }
Add-Arm "ctx-mode-per-type" "7.1" "the first block type's context mode applied to every type" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ NBL = 2; Modes = @(0, 1); LitMap = $pm;
      LitPlan = @(@{ T = 0; N = 7 }, @{ T = 1; N = 40 });
      Cmds = @(@{ Lit = (Get-Ascii "acegikmBDFHJLNacegikm"); NoCopy = $true }) } }))

# 7.3: a context map written with RLEMAX 4 and the inverse move-to-front flag.
$rm = @(0) * 64; $rm[3] = 2; $rm[9] = 1; $rm[10] = 1; $rm[40] = 2; $rm[41] = 2; $rm[63] = 1
Add-Arm "cmap-rle-imtf" "7.3" "run lengths or the move-to-front transform decoded wrong" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ LitMap = $rm; CmapLit = @{ Rle = 4; Imtf = $true };
      Cmds = @(@{ Lit = (Get-Ascii "C)J*hi?(C)J*"); NoCopy = $true }) } }))

# 8: a dictionary reference with a transform. Distance past the produced output
# names word id (distance - max - 1); for copy length 5 the word count is 2^10
# (NDBITS 10), so transform index = id >> 10. Id 3*1024 + 7 takes transform 3.
Add-Arm "dict-transform" "8" "the word or the transform indexed wrong" (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = (Get-Ascii "x"); Copy = 5; DictDist = (1 + 1 + 3 * 1024 + 7) }) } }))

# 8: a transform index past the 121 defined is invalid (RFC 7932 8). Word length 4
# has 1024 words, so id 121 * 1024 is the first invalid one.
Add-Arm "dict-bad-transform" "8" "an undefined transform answered with partial output instead of refusing" -Refuse (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ Cmds = @(@{ Lit = (Get-Ascii "x"); Copy = 4; DictDist = (1 + 1 + 121 * 1024) }) } }))

# 9.3: an insert longer than MLEN is invalid.
Add-Arm "mlen-overrun" "9.3" "literals past MLEN accepted" -Refuse (New-Stream 16 @(
  @{ Kind = 'c'; M = @{ MLen = 3; Cmds = @(@{ Lit = (Get-Ascii "abcdef"); NoCopy = $true }) } }))

# ---------------------------------------------------------------- grade and emit

if ($Only -ne "") { $sel = @($arms | Where-Object { $_.Name -eq $Only }); if ($sel.Count -eq 0) { throw "no arm $Only" } } else { $sel = @($arms) }

$fail = 0
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($a in $sel) {
  $s = $a.S.Bytes
  $net = Invoke-NetDecode $s
  if ($a.Refuse) {
    if ($net.Ok) { Write-Host ("  {0,-26} DROPPED: .NET accepted a stream this arm calls invalid ({1} bytes out)" -f $a.Name, $net.Out.Length) -ForegroundColor Red; $fail++; continue }
    $want = "$($a.Name) len=0"
    Write-Host ("  {0,-26} refuse  .NET: {1}" -f $a.Name, $net.Err) -ForegroundColor Green
  } else {
    if (-not $net.Ok) { Write-Host ("  {0,-26} DROPPED: .NET refused it: {1}" -f $a.Name, $net.Err) -ForegroundColor Red; $fail++; continue }
    $o = $net.Out
    if (-not $a.S.Opaque) {
      $same = ($o.Length -eq $a.S.Intended.Length)
      if ($same) { for ($i = 0; $i -lt $o.Length; $i++) { if ($o[$i] -ne $a.S.Intended[$i]) { $same = $false; break } } }
      if (-not $same) { Write-Host ("  {0,-26} DROPPED: .NET decoded {1} bytes, not the {2} intended" -f $a.Name, $o.Length, $a.S.Intended.Length) -ForegroundColor Red; $fail++; continue }
    }
    $want = "$($a.Name) len=$($o.Length) sum=$(Get-RollSum $o)"
    Write-Host ("  {0,-26} {1,4} stream bytes -> {2}" -f $a.Name, $s.Length, $want) -ForegroundColor Green
  }
  if ($Only -ne "") { Write-Host ("    stream: " + (($s | ForEach-Object { '{0:x2}' -f $_ }) -join ' ')) }
  $rows.Add(@{ A = $a; Want = $want })
}

Write-Host ""
Write-Host ("[brotli-conformance] {0} arms graded by .NET, {1} dropped" -f $rows.Count, $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
if ($fail -gt 0) { exit 1 }
if (-not $Emit) { exit 0 }
if ($Only -ne "") { throw "-Emit writes every arm; do not combine it with -Only" }

$L = [System.Collections.Generic.List[string]]::new()
$L.Add("Chapter: BrotliRfcConformance")
$L.Add("  cites Compress chapter Brotli")
$L.Add("")
$L.Add(" GENERATED by build/brotli-conformance.ps1 -Emit. Do not edit by hand.")
$L.Add("")
$L.Add(" One stream per section of RFC 7932, written by hand from the RFC rather than by")
$L.Add(" any encoder. Every expected line is what .NET's BrotliStream decodes the stream")
$L.Add(" to; a len=0 line is a stream the RFC calls invalid, which .NET refuses.")
$L.Add("")
$L.Add("Section: Streams")
$L.Add("")
foreach ($r in $rows) {
  $a = $r.A
  $L.Add(" RFC 7932 $($a.Sec). The wrong decode this separates: $($a.Wrong).")
  $L.Add("")
  $L.Add("  bc-$($a.Name) : List Integer = [" + (($a.S.Bytes | ForEach-Object { [int]$_ }) -join ", ") + "]")
  $L.Add("")
}
$L.Add("Section: Body")
$L.Add("")
$L.Add("  bc-sum : List Integer, Integer, Integer, Integer -> Integer")
$L.Add("  bc-sum (xs) (i) (n) (acc) =")
$L.Add("    if i >= n then acc")
$L.Add("    else let a2 = acc * 31 + list-at xs i")
$L.Add("    in bc-sum xs (i + 1) n (a2 - (a2 / 1000000007) * 1000000007)")
$L.Add("")
$L.Add("  bc-line : Text, List Integer -> Text")
$L.Add("  bc-line (name) (s) =")
$L.Add("    let d = brotli-decompress s")
$L.Add("    in let n = list-length d")
$L.Add("    in if n == 0 then name & `" len=0`" else name & `" len=`" & show n & `" sum=`" & show (bc-sum d 0 n 0)")
$L.Add("")
$L.Add("  opening : [Console] Nothing")
$L.Add("  opening = act")
foreach ($r in $rows) { $L.Add("    print-line-uni (bc-line `"$($r.A.Name)`" bc-$($r.A.Name))") }
$L.Add("  end")

$src = Join-Path $root "codex/test/brotli-rfc-conformance.codex"
$exp = Join-Path $root "codex/test/brotli-rfc-conformance.expected"
[IO.File]::WriteAllText($src, (($L -join "`r`n") + "`r`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($exp, ((($rows | ForEach-Object { $_.Want }) -join "`r`n") + "`r`n"), [Text.UTF8Encoding]::new($false))
Write-Host "wrote $src"
Write-Host "wrote $exp"
