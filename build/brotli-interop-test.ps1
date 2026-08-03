# BACKLOG 5.13 - real Brotli interop.
#
# Our Brotli chapter round-trips through its OWN decoder, and that cannot see a
# bit-layout bug both halves share. For a long time it could not have seen the
# biggest one of all: the chapter emitted byte-aligned framing with a Deflate
# payload and was not RFC 7932 in any respect, while every test stayed green.
#
# This hands our output to .NET's BrotliStream -- an independent RFC 7932
# implementation that ships with the runtime, the same way the Deflate work used
# DeflateStream -- and requires it to reproduce the original bytes.
#
# It also runs a NEGATIVE CONTROL: a deliberately corrupted stream must be
# REJECTED. Without that, a harness that silently accepted anything would report
# success forever.
#
#   pwsh build/brotli-interop-test.ps1
#   pwsh build/brotli-interop-test.ps1 -Kernel build/output/Sut.cdx

param(
  [string]$Kernel = "seed/Codex.cdx"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/brotli-interop"
New-Item -ItemType Directory -Force $out | Out-Null

$probeCdx   = Join-Path $out "probe.cdx"
$probeOut   = Join-Path $out "probe.out"
$compile    = Join-Path $root "build/compile.ps1"
$vm         = Join-Path $root "tools/codex-vm.exe"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

Write-Host "[brotli-interop] compiling probe with $Kernel ..."
# DELETE THE OLD BINARY FIRST. Without this, a failed compile leaves the previous
# run's probe.cdx in place, the Test-Path below passes, and the harness measures
# the OLD encoder while reporting it as the new one. That is exactly what it did:
# a change to the matcher's length cap came back byte-for-byte identical on all
# fourteen cases, which reads as "the change did nothing" and actually meant
# "the change did not compile".
Remove-Item $probeCdx -ErrorAction SilentlyContinue
pwsh $compile -Src (Join-Path $root "codex/test/brotli-interop.codex") -Out $probeCdx -Log (Join-Path $out "probe.log") -Kernel $kernelPath | Out-Null
if (-not (Test-Path $probeCdx)) {
  Write-Host "[brotli-interop] FAIL: probe did not compile" -ForegroundColor Red
  Select-String -Path (Join-Path $out "probe.log") -Pattern 'error CDX|CODEGEN-HALTED' |
    Select-Object -First 10 | ForEach-Object { "  " + $_.Line.Trim() }
  exit 1
}

Write-Host "[brotli-interop] running probe ..."
& $vm -kernel $probeCdx -headless -output $probeOut -mem 3072 | Out-Null
if (-not (Test-Path $probeOut)) { Write-Host "[brotli-interop] FAIL: probe produced no output" -ForegroundColor Red; exit 1 }

$lines = Get-Content $probeOut
function Get-Tagged([string]$tag) {
  foreach ($ln in $lines) {
    $i = $ln.IndexOf($tag + ":")
    if ($i -ge 0) {
      $rest = $ln.Substring($i + $tag.Length + 1)
      $nums = [regex]::Matches($rest, '\d+') | ForEach-Object { [byte][int]$_.Value }
      return , ([byte[]]$nums)
    }
  }
  return $null
}

function Invoke-NetBrotli([byte[]]$blob) {
  $in = [IO.MemoryStream]::new($blob)
  $ds = [IO.Compression.BrotliStream]::new($in, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.MemoryStream]::new()
  $ds.CopyTo($o)
  $ds.Dispose()
  return $o.ToArray()
}

# The head-to-head. The oracle proves our output is VALID; it says nothing about
# whether it is SMALL. Compressing the same original with .NET at SmallestSize is
# what turns "a real decoder accepts it" into a number that can be beaten, and it
# costs nothing, because the probe already ships every original out of the guest.
function Measure-NetBrotli([byte[]]$data) {
  $ms = [IO.MemoryStream]::new()
  $bs = [IO.Compression.BrotliStream]::new($ms, [IO.Compression.CompressionLevel]::SmallestSize)
  $bs.Write($data, 0, $data.Length)
  $bs.Dispose()
  return $ms.ToArray().Length
}

$ourTotal = 0
$netTotal = 0
$wins = 0
$losses = 0
$ties = 0

$ok = $true
$tags = @("ascii", "cyclic", "high", "runs", "mixed", "random", "small", "groups", "text", "utf8", "dict", "xform", "ctx8")
foreach ($tag in $tags) {
  $orig = Get-Tagged ($tag + "-orig")
  $comp = Get-Tagged ($tag + "-comp")
  if ($null -eq $orig -or $null -eq $comp) {
    Write-Host "$tag : MISSING from probe output" -ForegroundColor Red; $ok = $false; continue
  }
  try {
    $got = Invoke-NetBrotli $comp
    if ($got.Length -ne $orig.Length) {
      Write-Host "$tag : LENGTH $($got.Length) vs $($orig.Length)" -ForegroundColor Red; $ok = $false; continue
    }
    $same = $true
    for ($i = 0; $i -lt $got.Length; $i++) { if ($got[$i] -ne $orig[$i]) { $same = $false; break } }
    if ($same) {
      $pct = if ($orig.Length -gt 0) { [int](100 * $comp.Length / $orig.Length) } else { 0 }
      $net = Measure-NetBrotli $orig
      $ourTotal += $comp.Length
      $netTotal += $net
      $verdict = if ($comp.Length -lt $net) { $wins++; "WIN" }
                 elseif ($comp.Length -gt $net) { $losses++; "loss" }
                 else { $ties++; "tie" }
      $delta = $comp.Length - $net
      Write-Host ("{0}: orig={1}B comp={2}B ({3}%) net={4}B {5:+#;-#;0} {6} -> PASS" -f `
        $tag, $orig.Length, $comp.Length, $pct, $net, $delta, $verdict)
    } else {
      Write-Host "$tag : MISMATCH" -ForegroundColor Red; $ok = $false
    }
  } catch {
    Write-Host "$tag : DECODE-ERROR $($_.Exception.Message)" -ForegroundColor Red; $ok = $false
  }
}

# The cross-region case. Its original is NOT shipped out of the guest -- at 78000
# bytes the probe's per-byte Text append is quadratic and never finishes -- so the
# expected bytes are regenerated here from the same formula. That makes the
# comparison one against an independent implementation rather than against our own
# output, which is what the rest of this harness is for.
#
# It is the only case longer than one 65536-byte meta-block, so it is the only one
# that exercises a copy reaching ACROSS a meta-block boundary and the only one that
# runs the carried previous byte between regions.
$farComp = Get-Tagged "far-comp"
if ($null -eq $farComp) {
  Write-Host "far : MISSING from probe output" -ForegroundColor Red; $ok = $false
} else {
  $base = New-Object byte[] 70000
  for ($i = 0; $i -lt 70000; $i++) {
    $v = $i * 37 + [math]::Floor($i / 13) * 7
    $base[$i] = [byte](32 + ($v % 90))
  }
  $expected = New-Object byte[] 78000
  [Array]::Copy($base, 0, $expected, 0, 70000)
  [Array]::Copy($base, 45000, $expected, 70000, 8000)
  try {
    $got = Invoke-NetBrotli $farComp
    if ($got.Length -ne 78000) {
      Write-Host "far : LENGTH $($got.Length) vs 78000" -ForegroundColor Red; $ok = $false
    } else {
      $same = $true
      for ($i = 0; $i -lt 78000; $i++) { if ($got[$i] -ne $expected[$i]) { $same = $false; break } }
      if ($same) {
        $pct = [int](100 * $farComp.Length / 78000)
        $net = Measure-NetBrotli $expected
        $ourTotal += $farComp.Length
        $netTotal += $net
        $verdict = if ($farComp.Length -lt $net) { $wins++; "WIN" }
                   elseif ($farComp.Length -gt $net) { $losses++; "loss" }
                   else { $ties++; "tie" }
        $delta = $farComp.Length - $net
        Write-Host ("far: orig=78000B comp={0}B ({1}%) net={2}B {3:+#;-#;0} {4} -> PASS" -f `
          $farComp.Length, $pct, $net, $delta, $verdict)
      } else {
        Write-Host "far : MISMATCH at byte $i" -ForegroundColor Red; $ok = $false
      }
    }
  } catch {
    Write-Host "far : DECODE-ERROR $($_.Exception.Message)" -ForegroundColor Red; $ok = $false
  }
}

# Negative control: corrupt a stream and require the oracle to reject it. A
# harness that cannot fail proves nothing about the one that passes.
$c = Get-Tagged "ascii-comp"
if ($null -ne $c -and $c.Length -gt 4) {
  $bad = [byte[]]$c.Clone()
  $bad[2] = $bad[2] -bxor 0xFF
  $rejected = $false
  try { $null = Invoke-NetBrotli $bad } catch { $rejected = $true }
  if ($rejected) { Write-Host "control: corrupted stream REJECTED -> PASS" }
  else { Write-Host "control: corrupted stream was ACCEPTED -- this harness cannot fail" -ForegroundColor Red; $ok = $false }
}

# The WRITE half of the capability scorecard. Validity and size are different
# claims and this harness now makes both. A total that is larger than .NET's is
# not a failure of correctness, so it does not set $ok -- it is reported as the
# number it is, because a ratio nobody prints is a ratio nobody improves.
Write-Host ""
$ratio = if ($netTotal -gt 0) { 100.0 * $ourTotal / $netTotal } else { 0 }
Write-Host ("[brotli-interop] BYTES ours={0} net={1} ({2:N1}% of .NET) win={3} loss={4} tie={5}" -f `
  $ourTotal, $netTotal, $ratio, $wins, $losses, $ties)

if ($ok) {
  Write-Host "[brotli-interop] PASS: .NET BrotliStream decodes our RFC 7932 output" -ForegroundColor Green
  exit 0
} else {
  Write-Host "[brotli-interop] FAIL: real brotli rejected our output" -ForegroundColor Red
  exit 1
}
