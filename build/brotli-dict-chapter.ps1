# BACKLOG 5.13 -- turn the recovered RFC 7932 corpus into a Codex chapter.
#
# build/brotli-dict-extract.ps1 recovers the bytes from the oracle; this writes
# them as codex/foreword/compress/BrotliDict.codex. Kept as a script rather than
# hand-maintained source because 122784 bytes is not something a person edits,
# and because regenerating it is how the corpus gets re-proved against .NET.
#
# THE CORPUS IS BASE64 IN TEXT LITERALS, NOT INTEGERS IN LIST LITERALS, AND THE
# DIFFERENCE IS FOUR MEGABYTES. A list literal is emitted as CODE that builds the
# list element by element: measured, the corpus as four list literals cost 4.3 MB
# of binary for 122784 bytes of data, 35 bytes of machine code per byte carried.
# That overruns the 4 MB code segment at 0x100000 and lands in the serial ring
# buffer at 0x500000. A text literal goes into the data buffer as static bytes at
# 1 byte per character, so base64 carries the same corpus in about 164 KB.
#
# Base64 rather than hex because both are ASCII-safe through CCE and base64 is
# two thirds the size. The alphabet was CHECKED through the encoding boundary,
# '+' and '/' included: to-unicode (char-code (char-at s i)) answers exact ASCII
# for all sixty-four characters.
#
#   pwsh build/brotli-dict-chapter.ps1
#   pwsh build/brotli-dict-chapter.ps1 -Bin build-output/brotli-dict.bin

param(
  [string]$Bin = "build-output/brotli-dict.bin",
  [string]$Xf  = "build-output/brotli-xform.txt",
  [string]$Out = "codex/foreword/compress/BrotliDict.codex"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$binPath = if ([System.IO.Path]::IsPathRooted($Bin)) { $Bin } else { Join-Path $root $Bin }
if (-not (Test-Path $binPath)) {
  throw "no corpus at $binPath -- run build/brotli-dict-extract.ps1 first"
}
$bytes = [IO.File]::ReadAllBytes($binPath)
if ($bytes.Length -ne 122784) {
  throw "corpus is $($bytes.Length) bytes, not the 122784 RFC 7932 states -- do not generate from it"
}

# Discovered by the extractor's walk, not remembered. Re-derived here from the
# same rule the walk used so the two cannot drift: the counts must multiply back
# to the corpus size.
$counts = @(1024,1024,2048,2048,1024,1024,1024,1024,1024,512,512,256,128,128,256,128,128,64,64,32,32)
$sum = 0
for ($i = 0; $i -lt 21; $i++) { $sum += $counts[$i] * ($i + 4) }
if ($sum -ne $bytes.Length) {
  throw "counts multiply to $sum, corpus is $($bytes.Length) -- one of them is wrong"
}

$offsets = @()
$acc = 0
for ($i = 0; $i -lt 21; $i++) { $offsets += $acc; $acc += $counts[$i] * ($i + 4) }

# --- transforms -------------------------------------------------------------
$xfPath = if ([System.IO.Path]::IsPathRooted($Xf)) { $Xf } else { Join-Path $root $Xf }
if (-not (Test-Path $xfPath)) { throw "no transform table at $xfPath -- run build/brotli-xform-extract.ps1 first" }
$xrows = Get-Content $xfPath | ForEach-Object {
  $p = $_ -split "`t"
  [PSCustomObject]@{ T = [int]$p[0]; Pre = $p[1]; Ty = $p[2]; Suf = $p[3] }
}
if ($xrows.Count -ne 121) { throw "transform table has $($xrows.Count) rows, not the 121 RFC 7932 states" }

$typeCode = @{ 'Identity' = 0; 'FermentFirst' = 1; 'FermentAll' = 2 }
for ($k = 1; $k -le 9; $k++) { $typeCode["OmitFirst$k"] = 2 + $k }
for ($k = 1; $k -le 9; $k++) { $typeCode["OmitLast$k"]  = 11 + $k }

# Affixes are pooled into one blob and referenced by offset and length, so an
# affix used by several transforms is stored once.
$blob = [System.Collections.Generic.List[byte]]::new()
$pool = @{}
function Add-Affix([string]$csv) {
  if ($csv -eq '') { return @(0, 0) }
  if ($pool.ContainsKey($csv)) { return $pool[$csv] }
  $bytes = $csv -split ',' | ForEach-Object { [byte][int]$_ }
  $off = $blob.Count
  foreach ($bb in $bytes) { $blob.Add($bb) }
  $pool[$csv] = @($off, $bytes.Count)
  return $pool[$csv]
}

$xType = @(); $xPreOff = @(); $xPreLen = @(); $xSufOff = @(); $xSufLen = @()
foreach ($r in ($xrows | Sort-Object T)) {
  if (-not $typeCode.ContainsKey($r.Ty)) { throw "unknown transform type $($r.Ty)" }
  $xType += $typeCode[$r.Ty]
  $p = Add-Affix $r.Pre; $xPreOff += $p[0]; $xPreLen += $p[1]
  $s = Add-Affix $r.Suf; $xSufOff += $s[0]; $xSufLen += $s[1]
}
$affixReal = $blob.Count
while ($blob.Count % 3 -ne 0) { $blob.Add(0) }
$affixB64 = [Convert]::ToBase64String($blob.ToArray())

$nChunks = 4
$chunk = $bytes.Length / $nChunks
# Each chunk must be a whole number of base64 groups, or a chunk boundary needs
# padding and the decoder needs a case it otherwise never has.
if ($chunk % 3 -ne 0) { throw "chunk of $chunk is not a multiple of 3; base64 would need padding" }

$sb = [System.Text.StringBuilder]::new(700000)
[void]$sb.Append(@"
Chapter: BrotliDict

 RFC 7932's static dictionary: 13504 words in 122784 bytes, plus the per-length
 counts and offsets that index them. A Brotli copy whose distance exceeds the
 maximum backward distance is a reference into this corpus rather than into the
 output already produced.

 THESE BYTES ARE NOT WRITABLE FROM MEMORY AND MUST NOT BE APPROXIMATED. A real
 decoder looks the word up in ITS copy of the corpus, so a corpus that is close
 but not exact does not cost ratio -- it produces streams that decode to the
 wrong bytes, and our own round-trip cannot see it because both halves would
 share the error. They were recovered from .NET's BrotliStream by
 build/brotli-dict-extract.ps1 and this chapter is generated from them by
 build/brotli-dict-chapter.ps1. Regenerate rather than edit.

 THE CORPUS IS CARRIED AS BASE64 TEXT, AND THAT IS A SIZE DECISION WORTH KNOWING
 ABOUT. A list literal is emitted as code that builds the list one element at a
 time: measured, these bytes as four list literals cost 4.3 MB of binary, 35
 bytes of machine code for every byte carried, which overruns the 4 MB code
 segment and lands in the serial ring buffer. A text literal is static data in
 the data buffer at a byte per character, so the same corpus costs about 164 KB.
 Decoding it back is 122784 pushes, paid once per stream.

 It arrives in four pieces only to keep any one literal a comfortable size, not
 because anything about the format is chunked. Each piece is a whole number of
 base64 groups, so no chunk boundary needs padding.

 To brdict-load gives the whole corpus as one list.
 To brdict-count gives how many words have a given length.
 To brdict-offset gives where that length's words start.

 We say:

Section: Shape

 Word lengths run four to twenty-four. The counts were DISCOVERED by the
 extractor's walk rather than remembered, and they multiply back to 122784,
 which is the check that the walk neither stopped early nor ran long.

  brdict-min-len : Integer = 4
  brdict-max-len : Integer = 24
  brdict-total : Integer = $($bytes.Length)
  brdict-words : Integer = 13504

  brdict-counts : List Integer = [$($counts -join ', ')]
  brdict-offsets : List Integer = [$($offsets -join ', ')]

  brdict-count : Integer -> Integer
  brdict-count (len) =
    if len < brdict-min-len then 0
    else if len > brdict-max-len then 0
    else list-at brdict-counts (len - brdict-min-len)

  brdict-offset : Integer -> Integer
  brdict-offset (len) =
    if len < brdict-min-len then 0
    else if len > brdict-max-len then 0
    else list-at brdict-offsets (len - brdict-min-len)

 The byte offset of one word, by its length and its index within that length.

  brdict-word-at : Integer, Integer -> Integer
  brdict-word-at (len) (idx) = brdict-offset len + idx * len

Section: Corpus

 DO NOT REACH FOR to-unicode HERE. The obvious way to read a base64 character is
 to-unicode of its code point, which converts CCE back to the ASCII the alphabet
 is written in -- and it allocates about a kilobyte per call. MEASURED: 120000
 characters read that way grew the heap 125 MB, and the same 120000 read with
 char-code-at alone cost EIGHTY BYTES. Reading the whole corpus through
 to-unicode cost 172 MB a load, and the round-trip test ran the heap into the
 stack and double-faulted.

 So the alphabet is indexed by its CCE code point directly. The table is built
 once per load by asking the alphabet itself what its code points are, rather
 than by writing them down: they are 3 to 81 in an order that is CCE's business
 and not this chapter's, and a table nobody typed cannot be typed wrong.

  brdict-b64-alpha : Text = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

  brdict-zeros : Integer, List Integer -> List Integer
  brdict-zeros (n) (acc) =
    if n <= 0 then acc
    else brdict-zeros (n - 1) (list-push acc 0)

  brdict-b64-fill : Integer, List Integer -> List Integer
  brdict-b64-fill (k) (acc) =
    if k >= 64 then acc
    else brdict-b64-fill (k + 1) (list-set-at acc (char-code-at brdict-b64-alpha k) k)

  brdict-b64-table : Integer -> List Integer
  brdict-b64-table (ignored) = brdict-b64-fill 0 (brdict-zeros 128 [])

 Four characters carry three bytes. Every chunk is a whole number of groups, so
 there is no partial group and no padding to handle.

  brdict-b64 : List Integer, Text, Integer, Integer, List Integer -> List Integer
  brdict-b64 (tb) (s) (i) (n) (acc) =
    if i >= n then acc
    else let a = list-at tb (char-code-at s i)
    in let b = list-at tb (char-code-at s (i + 1))
    in let c = list-at tb (char-code-at s (i + 2))
    in let d = list-at tb (char-code-at s (i + 3))
    in let b0 = bit-or (bit-shl a 2) (bit-shru b 4)
    in let b1 = bit-and (bit-or (bit-shl b 4) (bit-shru c 2)) 255
    in let b2 = bit-and (bit-or (bit-shl c 6) d) 255
    in brdict-b64 tb s (i + 4) n (list-push (list-push (list-push acc b0) b1) b2)

 Built once per stream and threaded from there. Every reference to a constant
 rebuilds what it names, so a caller that loads this per position instead of per
 stream pays for the whole corpus each time.

  brdict-chunk : List Integer, Text, List Integer -> List Integer
  brdict-chunk (tb) (t) (acc) = brdict-b64 tb t 0 (text-length t) acc

 The accumulator is pre-sized to the whole corpus. Grown from empty it would
 double as it filled, and every abandoned backing buffer stays on the heap until
 this function returns, which doubles what a load costs for nothing.

  brdict-load : Integer -> List Integer
  brdict-load (ignored) =
    let tb = brdict-b64-table 0
    in brdict-chunk tb brdict-t3 (brdict-chunk tb brdict-t2 (brdict-chunk tb brdict-t1 (brdict-chunk tb brdict-t0 (__list-with-capacity brdict-total))))

"@)

for ($c = 0; $c -lt $nChunks; $c++) {
  $start = $c * $chunk
  $slice = New-Object byte[] $chunk
  [Array]::Copy($bytes, $start, $slice, 0, $chunk)
  [void]$sb.Append("  brdict-t$c : Text = `"")
  [void]$sb.Append([Convert]::ToBase64String($slice))
  [void]$sb.Append("`"`n")
}

[void]$sb.Append(@"

Section: Transforms

 RFC 7932's 121 word transforms. A word id carries one: the id divided by the
 count of words at that length IS the transform, and the remainder is the word.
 Each transform is a prefix, one of 21 operations on the word, and a suffix.

 THESE WERE DERIVED FROM THE ORACLE, NOT WRITTEN DOWN, by
 build/brotli-xform-extract.ps1: it asks .NET for a known word under each
 transform and works out which triple reproduces every probe. The derivation used
 one word length and was then verified against a DIFFERENT length -- 798 word and
 transform pairs, 456 of them non-ASCII.

 The non-ASCII half of that check is the one that matters. RFC 7932's case
 transform is defined on UTF-8 BYTES, not characters: below 0xC0 flip bit 5 of an
 ASCII lower-case letter, below 0xE0 flip bit 5 of the SECOND byte, otherwise flip
 bit 2 of the THIRD. The corpus carries 23059 non-ASCII bytes, so a table derived
 through ordinary string casing agrees on every English word and is wrong on the
 Arabic and CJK ones.

 The 21 operations, in the RFC's order and numbered as this chapter stores them:
 0 identity, 1 upper-case the first character, 2 upper-case all of them, 3 to 11
 omit the first one to nine bytes, 12 to 20 omit the last one to nine.

  brdict-xf-count : Integer = 121

  brdict-xf-identity : Integer = 0
  brdict-xf-ferment-first : Integer = 1
  brdict-xf-ferment-all : Integer = 2
  brdict-xf-omit-first : Integer = 3
  brdict-xf-omit-last : Integer = 12

  brdict-xf-type : List Integer = [$($xType -join ', ')]
  brdict-xf-pre-off : List Integer = [$($xPreOff -join ', ')]
  brdict-xf-pre-len : List Integer = [$($xPreLen -join ', ')]
  brdict-xf-suf-off : List Integer = [$($xSufOff -join ', ')]
  brdict-xf-suf-len : List Integer = [$($xSufLen -join ', ')]

 Every prefix and suffix pooled into one blob and referenced by offset and
 length, so an affix several transforms share is stored once. $affixReal bytes,
 padded to a multiple of three so the base64 needs no padding case.

  brdict-xf-affix-bytes : Integer = $affixReal
  brdict-xf-blob : Text = "$affixB64"

  brdict-xf-affix : Integer -> List Integer
  brdict-xf-affix (ignored) =
    brdict-b64 (brdict-b64-table 0) brdict-xf-blob 0 (text-length brdict-xf-blob) (__list-with-capacity $($blob.Count))

 THE TABLES ABOVE ARE CONSTANTS, AND A CONSTANT IS REBUILT AT EVERY MENTION.
 Measured: 100000 reads of one element of a 121-element list constant cost 98.4
 MB of heap -- 984 bytes a read, the whole list re-materialised each time --
 against 1 KB for the same 100000 reads from a list passed in as a parameter. A
 91000-fold difference, and it is invisible in the source, where both read
 `list-at xs i`.

 That is survivable where a table is touched once per dictionary reference and
 fatal where it is touched inside a search loop: the transform matcher reads
 these tables 121 times per candidate word, per chain slot, per input byte, and
 the first version of it ran the heap into the stack and double-faulted.

 So they are loaded ONCE into a record and threaded from there. Every constant
 below is mentioned exactly once in this chapter, here.

  BrXf = record {
    bxf-type : List Integer,
    bxf-pre-off : List Integer,
    bxf-pre-len : List Integer,
    bxf-suf-off : List Integer,
    bxf-suf-len : List Integer,
    bxf-aff : List Integer
  }

  brdict-xf-load : Integer -> BrXf
  brdict-xf-load (ignored) = BrXf {
    bxf-type = brdict-xf-type,
    bxf-pre-off = brdict-xf-pre-off,
    bxf-pre-len = brdict-xf-pre-len,
    bxf-suf-off = brdict-xf-suf-off,
    bxf-suf-len = brdict-xf-suf-len,
    bxf-aff = brdict-xf-affix 0
  }

"@)

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
[IO.File]::WriteAllText($outPath, $sb.ToString().Replace("`r`n", "`n"))

$size = (Get-Item $outPath).Length
Write-Host ("[dict-chapter] {0} bytes of corpus -> {1} ({2:N0} bytes of source, {3} chunks of {4})" -f `
  $bytes.Length, $Out, $size, $nChunks, $chunk)
