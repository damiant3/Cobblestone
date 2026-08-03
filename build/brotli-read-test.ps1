# THE READ HALF OF THE CAPABILITY. Streams produced by a real encoder, read by
# ours, with the expectations computed on the host from the ORIGINAL bytes so this
# cannot pass by agreeing with itself.
#
#   pwsh build/brotli-read-test.ps1
#   pwsh build/brotli-read-test.ps1 -Only cyclic          # iterate on one case
#   pwsh build/brotli-read-test.ps1 -Kernel build/output/Sut.cdx
#
# build/brotli-interop-test.ps1 asks whether .NET can read OUR output. That is the
# other half, and for months it was the only half asked. A decoder checked solely
# against its paired encoder is checked against nothing: the two halves can share
# any assumption at all and agree forever. docs/PM/Active/Stories/BrotliBeatsOpus.md
# is the account of what that cost.
#
# ONE CASE PER BOOT, deliberately. The first cut of this ran all four in one guest
# and the first case exhausted the heap, so the other three never printed and the
# run reported a single line of nothing. A harness where one bad case hides the
# rest cannot be trusted about the rest.

param(
  [string]$Kernel = "seed/Codex.cdx",
  [string]$Only = "",
  [int]$TimeoutSec = 180,
  [string]$OutDir = "test-output/brotli-read"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$outAbs = Join-Path $root $OutDir
New-Item -ItemType Directory -Force $outAbs | Out-Null
$kernelPath = if ([IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

function New-Utf8Prose([int]$n) {
  $w = @("привет","мир","быстрая","коричневая","лиса","прыгает","через","ленивую",
         "собаку","и","затем","日本語","テキスト","です","très","naïve")
  $out = [System.Collections.Generic.List[byte]]::new(); $s = 909
  while ($out.Count -lt $n) {
    $s = (($s * 1103515245 + 12345) -band 2147483647)
    $k = [math]::Floor($s / 4096) % 16
    $sep = if ([math]::Floor($s / 256) % 16 -eq 0) { ". " } else { " " }
    foreach ($b in [Text.Encoding]::UTF8.GetBytes($w[$k] + $sep)) { $out.Add($b) }
  }
  return $out.ToArray()
}
function New-Prose([int]$n) {
  $w = @("the","quick","brown","fox","jumps","over","lazy","dog",
         "and","then","runs","far","away","into","the","woods")
  $out = [System.Collections.Generic.List[byte]]::new(); $s = 4242
  while ($out.Count -lt $n) {
    $s = (($s * 1103515245 + 12345) -band 2147483647)
    $k = [math]::Floor($s / 4096) % 16
    $sep = if ([math]::Floor($s / 256) % 16 -eq 0) { ". " } else { " " }
    foreach ($b in [Text.Encoding]::UTF8.GetBytes($w[$k] + $sep)) { $out.Add($b) }
  }
  return $out.ToArray()
}
function New-Cyclic([int]$n) {
  $pat = @(65,66,65,67,65,66,68,65); $out = New-Object byte[] $n
  for ($i = 0; $i -lt $n; $i++) { $out[$i] = [byte]$pat[$i % 8] }
  return $out
}
function New-Mixed([int]$n) {
  $out = [System.Collections.Generic.List[byte]]::new(); $s = 7
  while ($out.Count -lt $n) {
    $s = (($s * 1103515245 + 12345) -band 2147483647)
    if (($s % 3) -eq 0) { for ($j = 0; $j -lt 40; $j++) { $out.Add([byte](97 + (($s + $j) % 26))) } }
    else {
      $take = [math]::Min(60, $out.Count)
      if ($take -gt 0) {
        $from = $out.Count - $take
        foreach ($b in $out.GetRange($from, $take).ToArray()) { $out.Add($b) }
      } else { for ($j = 0; $j -lt 40; $j++) { $out.Add([byte](32 + ($j % 90))) } }
    }
  }
  return $out.ToArray()[0..($n - 1)]
}

function Compress-Net([byte[]]$data) {
  $ms = [IO.MemoryStream]::new()
  $bs = [IO.Compression.BrotliStream]::new($ms, [IO.Compression.CompressionLevel]::SmallestSize)
  $bs.Write($data, 0, $data.Length); $bs.Dispose()
  return $ms.ToArray()
}

# A MULTI-META-BLOCK STREAM, which nothing else here produces. .NET packs four
# megabytes into ONE meta-block, so every case above is a single one however large
# it is made, and two whole classes of defect were invisible for that reason: a
# metadata meta-block (which a flush emits to reach a byte boundary, and which was
# read as a 28-bit length and took a 2 KB input to 3 GB of heap), and the distance
# ring buffer, which is per STREAM and was being re-initialised per meta-block.
#
# A Flush closes a meta-block, so chunked writes give a small stream with three of
# them. The reset defect is the reason this case compares a HASH and not a length:
# against it the stream decodes to exactly 6000 bytes, all of them wrong.
function Compress-NetFlushed([byte[]]$data, [int]$chunk) {
  $ms = [IO.MemoryStream]::new()
  $bs = [IO.Compression.BrotliStream]::new($ms, [IO.Compression.CompressionLevel]::SmallestSize)
  $off = 0
  while ($off -lt $data.Length) {
    $n = [math]::Min($chunk, $data.Length - $off)
    $bs.Write($data, $off, $n); $bs.Flush(); $off += $n
  }
  $bs.Dispose()
  return $ms.ToArray()
}

# The first meta-block's declared length, read straight off the header. Enough to
# tell a split stream from a single one without decoding anything. Mirrors
# brotli-read-wbits and brotli-meta; MNIBBLES of 3 is a metadata block and carries
# no length, which is reported as 0 rather than misread as one.
function Get-Bits([byte[]]$s, [ref]$pos, [int]$n) {
  $v = 0
  for ($i = 0; $i -lt $n; $i++) {
    $bit = ([int]$s[[math]::Floor($pos.Value / 8)] -shr ($pos.Value % 8)) -band 1
    $v = $v -bor ($bit -shl $i)
    $pos.Value = $pos.Value + 1
  }
  return $v
}
function Get-FirstMetaLen([byte[]]$s) {
  $p = 0
  if ((Get-Bits $s ([ref]$p) 1) -ne 0) {
    if ((Get-Bits $s ([ref]$p) 3) -eq 0) { $null = Get-Bits $s ([ref]$p) 3 }
  }
  $islast = Get-Bits $s ([ref]$p) 1
  if ($islast -eq 1) { if ((Get-Bits $s ([ref]$p) 1) -eq 1) { return 0 } }
  $mnib = Get-Bits $s ([ref]$p) 2
  if ($mnib -eq 3) { return 0 }
  return (Get-Bits $s ([ref]$p) (($mnib + 4) * 4)) + 1
}

# A fixed period makes the matcher emit many copies at the SAME distance, which is
# what puts a cache code at the start of a later meta-block and makes the carry
# load-bearing rather than incidental.
function New-Periodic([int]$n, [int]$period) {
  $rng = 12345; $pat = New-Object byte[] $period
  for ($i = 0; $i -lt $period; $i++) {
    $rng = (($rng * 1103515245 + 12345) -band 2147483647)
    $pat[$i] = [byte](65 + ([math]::Floor($rng / 65536) % 26))
  }
  $out = New-Object byte[] $n
  for ($i = 0; $i -lt $n; $i++) { $out[$i] = $pat[$i % $period] }
  return $out
}

# The same rolling hash the guest computes, so the whole original need not be
# shipped in. Length alone would pass for a decoder that produced the right
# NUMBER of wrong bytes.
function Get-RollSum([byte[]]$data) {
  $acc = 0
  foreach ($b in $data) { $acc = (($acc * 31 + [int]$b) % 1000000007) }
  return $acc
}

$cases = @(
  @{ Name = "utf8";   Data = (New-Utf8Prose 8000) },
  @{ Name = "prose";  Data = (New-Prose 8000) },
  @{ Name = "cyclic"; Data = (New-Cyclic 4000) },
  @{ Name = "mixed";  Data = (New-Mixed 6000) },
  @{ Name = "multimeta"; Data = (New-Periodic 6000 137); Flush = 2000 }
)
if ($Only -ne "") { $cases = @($cases | Where-Object { $_.Name -eq $Only }) }
if ($cases.Count -eq 0) { Write-Host "no such case: $Only" -ForegroundColor Red; exit 2 }

$pass = 0
$results = @()

foreach ($c in $cases) {
  $comp = if ($c.ContainsKey("Flush")) { Compress-NetFlushed $c.Data $c.Flush } else { Compress-Net $c.Data }
  $sum = Get-RollSum $c.Data

  # .NET must read back its own stream, or the case is not a case.
  $in = [IO.MemoryStream]::new($comp)
  $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.MemoryStream]::new(); $ds.CopyTo($o); $ds.Dispose()
  if ($o.ToArray().Length -ne $c.Data.Length) { throw "$($c.Name): .NET did not round-trip its own stream" }

  # A FLUSH CASE MUST ACTUALLY BE SPLIT. If .NET ever stopped closing a meta-block
  # on Flush this case would quietly become an ordinary single-meta-block stream and
  # keep passing while testing nothing, which is the exact shape of a test that
  # cannot fail. The first meta-block's declared length is enough to tell.
  if ($c.ContainsKey("Flush")) {
    $mlen = Get-FirstMetaLen $comp
    if ($mlen -le 0 -or $mlen -ge $c.Data.Length) {
      throw "$($c.Name): expected a multi-meta-block stream, but the first meta-block declares $mlen of $($c.Data.Length). This case no longer tests what it exists to test."
    }
  }

  $blist = ($comp | ForEach-Object { [int]$_ }) -join ", "
  $lines = @()
  $lines += "Chapter: BrotliRead$($c.Name)"
  $lines += "  cites Compress chapter Brotli"
  $lines += ""
  $lines += " GENERATED by build/brotli-read-test.ps1. Do not edit by hand."
  $lines += ""
  $lines += " One stream from a real encoder. The length and rolling sum this prints are"
  $lines += " compared against the ORIGINAL bytes, computed on the host."
  $lines += ""
  $lines += "Section: Body"
  $lines += ""
  $lines += "  rd-in : List Integer = [$blist]"
  $lines += ""
  $lines += "  rd-sum : List Integer, Integer, Integer, Integer -> Integer"
  $lines += "  rd-sum (xs) (i) (n) (acc) ="
  $lines += "    if i >= n then acc"
  $lines += "    else let a2 = acc * 31 + list-at xs i"
  $lines += "    in rd-sum xs (i + 1) n (a2 - (a2 / 1000000007) * 1000000007)"
  $lines += ""
  $lines += "  opening : [Console] Nothing"
  $lines += "  opening = act"
  $lines += "    let d = brotli-decompress rd-in"
  $lines += "    in let n = list-length d"
  $lines += "    in print-line-uni (`"rd $($c.Name) `" & show n & `" `" & show (rd-sum d 0 n 0))"
  $lines += "  end"

  $src = Join-Path $outAbs "read-$($c.Name).codex"
  [IO.File]::WriteAllLines($src, $lines)
  $cdx = Join-Path $outAbs "read-$($c.Name).cdx"
  $log = Join-Path $outAbs "read-$($c.Name).log"
  $vmo = Join-Path $outAbs "read-$($c.Name).out"

  Write-Host ("[read] {0,-7} orig={1}B net={2}B compiling ..." -f $c.Name, $c.Data.Length, $comp.Length)
  Remove-Item $cdx -ErrorAction SilentlyContinue
  & pwsh (Join-Path $root "build/compile.ps1") -Src $src -Out $cdx -Log $log -Kernel $kernelPath *>$null
  if (-not (Test-Path $cdx)) {
    Write-Host "  $($c.Name): FAIL_COMPILE (see $log)" -ForegroundColor Red
    $results += "$($c.Name) FAIL_COMPILE"; continue
  }

  # A misparsed stream can consume the heap, so the boot is bounded and a
  # timeout is reported as itself rather than as a wrong answer.
  Remove-Item $vmo -ErrorAction SilentlyContinue
  $vm = Start-Process -FilePath (Join-Path $root "tools/codex-vm.exe") `
        -ArgumentList @("-kernel", $cdx, "-headless", "-output", $vmo, "-mem", "3072") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $outAbs "vm-$($c.Name).txt")
  if (-not $vm.WaitForExit($TimeoutSec * 1000)) {
    try { $vm.Kill() } catch { }
    Write-Host "  $($c.Name): FAIL_TIMEOUT after ${TimeoutSec}s" -ForegroundColor Red
    $results += "$($c.Name) FAIL_TIMEOUT"; continue
  }

  $got = if (Test-Path $vmo) { (Get-Content $vmo -Raw) } else { "" }
  $got = ($got -replace "`r", '' -replace "`0", '').Trim()
  $want = "rd $($c.Name) $($c.Data.Length) $sum"
  if ($got -eq $want) {
    Write-Host "  $($c.Name): PASS  ($($c.Data.Length)B, sum $sum)" -ForegroundColor Green
    $pass++; $results += "$($c.Name) PASS"
  } else {
    Write-Host "  $($c.Name): FAIL" -ForegroundColor Red
    Write-Host "    got:  $got"
    Write-Host "    want: $want"
    $results += "$($c.Name) FAIL"
  }
}

Write-Host ""
$col = if ($pass -eq $cases.Count) { 'Green' } else { 'Yellow' }
Write-Host ("[brotli-read] READ {0} of {1}" -f $pass, $cases.Count) -ForegroundColor $col
$results | ForEach-Object { "  $_" }
if ($pass -eq $cases.Count) { exit 0 } else { exit 1 }
