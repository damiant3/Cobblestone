# Verify Brotli's three data tables against RFC 7932's PUBLISHED bytes.
#
#   pwsh build/brotli-tables-verify.ps1
#
# The dictionary, the transforms and the context lookup tables are DATA, and they are
# the one part of this codec where an error is invisible to every other test we have.
# A round trip cannot see a wrong dictionary because both halves share the error, and
# the interop harness cannot see it either as long as .NET happens to agree with our
# extractor. They were recovered by probing .NET (build/brotli-dict-extract.ps1 and
# siblings) at a time when nobody had a copy of the spec, and until 2026-07-26 their
# correctness rested entirely on those scripts.
#
# RFC 7932 prints all three, each with a CRC-32, so the archaeology is checkable
# against the publication. It checks out: all four checksums matched first time, which
# makes the extraction an INDEPENDENT derivation agreeing with the standard rather
# than a thing we hope is right. This harness is what keeps it that way.
#
# No VM, no compile, no network. Seconds.

param(
  [string]$Rfc = "docs/Reference/RFC7932-Brotli.txt"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# The published check values, from RFC 7932 sections 7.1, Appendix A and Appendix B.
$EXPECT = @{
  dict      = @{ len = 122784; crc = "0x5136cb04"; where = "Appendix A" }
  transform = @{ len = 648;    crc = "0x3d965f81"; where = "Appendix B" }
  lut0      = @{ len = 256;    crc = "0x8e91efb7"; where = "section 7.1" }
  lut1      = @{ len = 256;    crc = "0xd01a32f4"; where = "section 7.1" }
}

# Appendix C's function. The constants are DECIMAL deliberately: PowerShell parses
# 0xFFFFFFFF as int32 -1 and a later cast to int64 preserves the -1, so
# [int64]0xFFFFFFFF is a mask that masks nothing. That bug produced a confident,
# plausible-looking checksum that disagreed with the RFC's own bytes, which reads
# exactly like "our dictionary is corrupt" and would have sent someone to rebuild a
# 122 KB table that was already correct.
$mask = [int64]4294967295          # 0xFFFFFFFF
$poly = [int64]3988292384          # 0xEDB88320
$crcTable = New-Object int64[] 256
for ($i = 0; $i -lt 256; $i++) {
  $c = [int64]$i
  for ($k = 0; $k -lt 8; $k++) {
    if ($c -band 1) { $c = ($poly -bxor ($c -shr 1)) -band $mask } else { $c = ($c -shr 1) -band $mask }
  }
  $crcTable[$i] = $c
}
function Get-Crc32([byte[]]$data) {
  $crc = [int64]4294967295
  foreach ($b in $data) {
    $crc = ($crcTable[[int](($crc -bxor $b) -band 255)] -bxor ($crc -shr 8)) -band $mask
  }
  return "0x" + ((($crc -bxor $mask) -band $mask)).ToString("x8")
}

# The routine is asserted against the standard vector BEFORE it is used to judge
# anything. A checksum that is quietly wrong turns a correct table into a false alarm.
$vector = Get-Crc32 ([Text.Encoding]::ASCII.GetBytes("123456789"))
if ($vector -ne "0xcbf43926") {
  Write-Host "[brotli-tables] FAIL: CRC-32 self-test got $vector, expected 0xcbf43926" -ForegroundColor Red
  exit 1
}
Write-Host "[brotli-tables] CRC-32 self-test OK (0xcbf43926)"

function Get-QuotedText([string[]]$src, [string]$name) {
  $line = $src | Where-Object { $_ -match ("^\s+" + [regex]::Escape($name) + "\s*:\s*Text\s*=") }
  if (-not $line) { throw "$name not found" }
  $q1 = $line.IndexOf('"'); $q2 = $line.LastIndexOf('"')
  return $line.Substring($q1 + 1, $q2 - $q1 - 1)
}
function Get-IntList([string[]]$src, [string]$name) {
  $line = $src | Where-Object { $_ -match ("^\s+" + [regex]::Escape($name) + "\s*:\s*List Integer\s*=") }
  if (-not $line) { throw "$name not found" }
  $b1 = $line.IndexOf('['); $b2 = $line.LastIndexOf(']')
  return [int[]]($line.Substring($b1 + 1, $b2 - $b1 - 1).Split(',') | ForEach-Object { [int]$_.Trim() })
}

$dictSrc = Get-Content (Join-Path $root "codex/foreword/compress/BrotliDict.codex")
$brSrc = Get-Content (Join-Path $root "codex/foreword/compress/Brotli.codex")

$ok = $true
function Test-Table([string]$key, [byte[]]$bytes) {
  $e = $script:EXPECT[$key]
  $crc = Get-Crc32 $bytes
  $good = ($bytes.Length -eq $e.len) -and ($crc -eq $e.crc)
  $verdict = if ($good) { "OK" } else { "MISMATCH" }
  $colour = if ($good) { "Green" } else { "Red" }
  Write-Host ("  {0,-10} {1,7} bytes  {2}  vs RFC {3} {4}  {5}" -f `
    $key, $bytes.Length, $crc, $e.where, $e.crc, $verdict) -ForegroundColor $colour
  if (-not $good) { $script:ok = $false }
}

# The corpus, four base64 chunks. Each chunk is a whole number of base64 groups.
$b64 = -join (@("brdict-t0", "brdict-t1", "brdict-t2", "brdict-t3") | ForEach-Object { Get-QuotedText $dictSrc $_ })
$ourDict = [Convert]::FromBase64String($b64)

# The transforms, rebuilt into Appendix B's serialisation: per transform the prefix
# bytes, a zero, a byte naming the transform, the suffix bytes, a zero. Ours are stored
# as offsets and lengths into an affix blob, so this one check tests the offsets, the
# lengths, the blob and the type codes at once.
$xfType = Get-IntList $dictSrc "brdict-xf-type"
$xfPreOff = Get-IntList $dictSrc "brdict-xf-pre-off"
$xfPreLen = Get-IntList $dictSrc "brdict-xf-pre-len"
$xfSufOff = Get-IntList $dictSrc "brdict-xf-suf-off"
$xfSufLen = Get-IntList $dictSrc "brdict-xf-suf-len"
$affix = [Convert]::FromBase64String((Get-QuotedText $dictSrc "brdict-xf-blob"))
$ser = New-Object System.Collections.Generic.List[byte]
for ($i = 0; $i -lt $xfType.Count; $i++) {
  for ($k = 0; $k -lt $xfPreLen[$i]; $k++) { $ser.Add($affix[$xfPreOff[$i] + $k]) }
  $ser.Add(0)
  $ser.Add([byte]$xfType[$i])
  for ($k = 0; $k -lt $xfSufLen[$i]; $k++) { $ser.Add($affix[$xfSufOff[$i] + $k]) }
  $ser.Add(0)
}

# The context tables are carried as one base64 CHARACTER PER VALUE, Lut0 then Lut1,
# because every value fits one: Lut0 tops out at 60 and Lut1 at 3.
$alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
$lutText = Get-QuotedText $brSrc "brctx2-lut-text"
$lut = New-Object byte[] $lutText.Length
for ($i = 0; $i -lt $lutText.Length; $i++) {
  $v = $alpha.IndexOf($lutText[$i])
  if ($v -lt 0) { throw "brctx2-lut-text is not base64 at index $i" }
  $lut[$i] = [byte]$v
}

Write-Host "[brotli-tables] our tables, recovered from .NET, against the publication:"
Test-Table "dict" $ourDict
Test-Table "transform" $ser.ToArray()
if ($lut.Length -ne 512) {
  Write-Host ("  lut        {0} characters, expected 512" -f $lut.Length) -ForegroundColor Red
  $ok = $false
} else {
  Test-Table "lut0" $lut[0..255]
  Test-Table "lut1" $lut[256..511]
}

# If the RFC itself is on disk, compare the dictionary BYTE FOR BYTE rather than by
# checksum, and confirm the parse against the RFC's own check value so a bad parse
# cannot pass as agreement. This is what makes the landed copy load-bearing instead
# of decorative.
$rfcPath = if ([IO.Path]::IsPathRooted($Rfc)) { $Rfc } else { Join-Path $root $Rfc }
if (-not (Test-Path $rfcPath)) {
  Write-Host "[brotli-tables] note: $Rfc not present, checksum comparison only"
} else {
  $hex = New-Object System.Text.StringBuilder
  $inA = $false
  foreach ($ln in (Get-Content $rfcPath)) {
    if ($ln -match '^Appendix A\.') { $inA = $true; continue }
    if ($ln -match '^Appendix B\.') { break }
    if ($inA -and $ln -match '^\s{6}([0-9a-f]{64})\s*$') { [void]$hex.Append($matches[1]) }
  }
  $h = $hex.ToString()
  $rfcDict = New-Object byte[] ($h.Length / 2)
  for ($i = 0; $i -lt $rfcDict.Length; $i++) { $rfcDict[$i] = [Convert]::ToByte($h.Substring($i * 2, 2), 16) }
  $parsed = Get-Crc32 $rfcDict
  if ($parsed -ne $EXPECT.dict.crc) {
    Write-Host "  RFC parse FAILED: Appendix A read as $parsed, its own check value is $($EXPECT.dict.crc)" -ForegroundColor Red
    $ok = $false
  } else {
    $same = $rfcDict.Length -eq $ourDict.Length
    if ($same) {
      for ($i = 0; $i -lt $rfcDict.Length; $i++) {
        if ($rfcDict[$i] -ne $ourDict[$i]) {
          Write-Host ("  dictionary DIFFERS from Appendix A at offset {0}: ours {1}, RFC {2}" -f $i, $ourDict[$i], $rfcDict[$i]) -ForegroundColor Red
          $same = $false; $ok = $false; break
        }
      }
    }
    if ($same) { Write-Host "  dict       byte-for-byte identical to Appendix A (parse confirmed by its own CRC)" -ForegroundColor Green }
  }
}

if ($ok) {
  Write-Host "[brotli-tables] PASS: every table matches RFC 7932" -ForegroundColor Green
  exit 0
} else {
  Write-Host "[brotli-tables] FAIL: a table does not match the standard -- streams will decode to wrong bytes" -ForegroundColor Red
  exit 1
}
