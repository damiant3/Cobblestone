# Differential oracle for the CCE text predicates.
#
# Every answer here is adjudicated by the HOST's Unicode tables, never by
# another Codex answer. Same design as build/oracle-scalar.ps1 and
# build/oracle-vector.ps1, pointed at the last uncovered axis on that list.
#
# The axis is the CCE/ASCII boundary. Codex text is CCE internally, and CCE
# numbers its code points by FREQUENCY, not by Unicode order: cp 3..12 are the
# digits, 13..38 the lowercase letters, 39..64 the uppercase, 65..96 the
# punctuation and symbols, 97..112 sixteen accented Latin letters, 113..127
# fifteen Cyrillic. So every predicate is a band test on a numbering that
# shares no boundary with ASCII, and a band written with ASCII constants is
# wrong in a way that still type-checks, still runs, and still answers a
# Boolean. CCE 48..57 -- what an ASCII digit test accepts -- is D L C U M W F
# G Y P.
#
# That class is not hypothetical. `text-to-upper` and `text-to-lower` shipped
# subtracting 32 from a CCE code point, which is a no-op on every letter and
# corrupted the one band it did reach; three separate chapters carried the
# same shape in one week. A no-op looks like working code from every angle
# except an assertion on the result.
#
# THE DIRECTION MATTERS. The harness drives by UNICODE, not by CCE: it hands
# the guest a Unicode code point, the guest converts it with from-unicode and
# answers the predicate, and the host says what that character actually is.
# Driving by CCE instead would mean taking the expected answers from
# cce-to-unicode-table, which is guest data -- a decoder checked against its
# paired encoder, which is the shape BrotliBeatsOpus is about. Guest data is
# used only to CHOOSE which inputs are interesting; it never supplies an
# answer.
#
# Two families:
#
#   P  the predicates, over every Unicode code point Tier 0 carries. Nine
#      predicates, the digit value, and both case conversions.
#   R  round-trip and refusal, over a much wider set. from-unicode must
#      answer either the same character back or -1 -- never a DIFFERENT
#      character. Silent replacement is the documented hazard here and it is
#      what a round trip through our own halves cannot see.
#
# R fires in both directions. Every printable ASCII character, plus NUL and
# LF, MUST map (that is 97 code points and it is exactly Tier 0's size). Every
# ASCII control character other than those two MUST refuse -- a carriage
# return is unmapped by design, and a from-unicode that started mapping them
# would otherwise pass a one-directional check silently.
#
# The host mappings are named explicitly rather than left to a convenience
# call, so the choice is deliberate and cannot drift:
#
#   is-punct     IsPunctuation OR IsSymbol. CCE's punct band is 32 slots and
#                holds exactly the 32 ASCII punctuation-and-symbol characters;
#                .NET's IsPunctuation alone excludes + = < > | ~ ^ $ % `.
#   is-accented  a letter in the Latin-1 Supplement. Not a Unicode category:
#                it is a CCE class, so the host states the range itself.
#   is-cyrillic  U+0400..U+04FF, likewise.
#
# ON DEMAND. Not in build.ps1, not in test.ps1. It boots a VM. Run it when you
# touch CCE, a text predicate, the Unicode boundary, or a chapter that
# classifies characters, and before a public push.
#
#   pwsh build/oracle-cce.ps1
#   pwsh build/oracle-cce.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/oracle-cce.ps1 -Keep        # keep the generated source
#
# A failure prints the code point, the character, what the guest said and what
# the host said. Disagreements are also summarised by class, because where
# they concentrate is the finding.
#
# CONTROLS, fired rather than assumed. An instrument that has only ever been
# seen passing is indistinguishable from one that cannot fail, and this one
# carries a suppression mechanism, which is the shape most able to hide a
# defect. All three were run against seed EFC7FCD09CCA6B03 on 2026-07-27:
#
#   It can fail, and only where it should.  `is-punct`'s band narrowed by one
#     slot (96 to 95) gives exactly ONE failure, at U+0025, in the `pu`
#     position, and nothing else moves. The "and no others" half is the half
#     that proves the predicate under test was the one measured.
#   The gap arm does not swallow its class.  `is-lower` widened to 13..127 --
#     the obvious "fix" for G2 -- takes G2+G3 from 31 suppressed cases to 0
#     and raises 89 failures. The arm matches one exact answer, not a class.
#     That run also measured why the obvious fix is wrong: `to-upper` is
#     `if is-lower c then char-code c + 26`, so widening the test sends all 32
#     punctuation characters into the accented and Cyrillic bands. `!` becomes
#     a backtick, `"` becomes e-acute. It is the historical minus-32 bug with
#     a different constant, which is why G2 is recorded as a decision and not
#     taken as a one-line change.
#   A closed gap is reported.  Marking U+0061 as gapped, where the compiler
#     already agrees with the host, prints the gap-closed notice naming that
#     case. Without this the suppression list would rot exactly the way a
#     stale `.skip` reason does, and nothing would ever say so.

param(
    [string]$Kernel = "seed/Codex.cdx",
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
[Environment]::CurrentDirectory = $repo

$out = Join-Path $repo 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null
$src = Join-Path $out 'oracle-cce.codex'
$cdx = Join-Path $out 'oracle-cce.cdx'
$log = Join-Path $out 'oracle-cce.log'
$runOut = Join-Path $out 'oracle-cce.out'

# ------------------------------------------------------------ host semantics
function Host-Flags([int]$u) {
    $c = [char]$u
    $ws = [int][char]::IsWhiteSpace($c)
    $dg = [int][char]::IsDigit($c)
    $lo = [int][char]::IsLower($c)
    $up = [int][char]::IsUpper($c)
    $le = [int][char]::IsLetter($c)
    $pu = [int]([char]::IsPunctuation($c) -or [char]::IsSymbol($c))
    $ac = [int]([char]::IsLetter($c) -and $u -ge 0x00C0 -and $u -le 0x00FF)
    $cy = [int]($u -ge 0x0400 -and $u -le 0x04FF)
    $hx = [int](($u -ge 48 -and $u -le 57) -or ($u -ge 97 -and $u -le 102) -or ($u -ge 65 -and $u -le 70))
    "$ws$dg$lo$up$le$pu$ac$cy$hx"
}

function Host-DigitValue([int]$u) {
    if ([char]::IsDigit([char]$u)) { $u - 48 } else { -1 }
}

# ------------------------------------------------------------- documented gaps
#
# Three clusters disagree with the host for reasons that are decided rather
# than accidental, and they are handled by computing a SECOND expectation
# rather than by an exclusion list. A case may therefore land in one of three
# states: it agrees with the host (clean), it agrees with the gapped answer
# (a known gap, counted and named), or it agrees with neither (a failure).
#
# The point of doing it this way is the third column. An exclusion list is a
# claim with no runner behind it -- it goes on suppressing a case long after
# the case has been fixed, and nothing ever says so. Because the host answer
# is still computed for these cases, a gap that CLOSES is reported as loudly
# as a gap that opens.
#
#   G1  CLOSED 2026-07-28. is-whitespace answered True for NUL, because the
#       band was `char-code c <= 2` and Tier 0's first three slots are NUL, LF
#       and space. Damian ruled NUL is not whitespace, so the band is 1..2 in
#       CCE.codex, in emit-is-whitespace-builtin, and in both plugs. `classify`
#       moved with it (NUL answers Other), since it spells the same band a
#       second time. The arm is deleted rather than left suppressing nothing.
#
#   G2  CLOSED 2026-07-27. is-letter and is-lower answered False for all 31
#       non-ASCII Tier 0 letters, so no accented or Cyrillic letter could
#       appear in an identifier even though the lexer already accepted Tier 1
#       characters through their own multi-byte path. Both are two bands now,
#       in the builtin and in CCE.codex. The arm is deleted rather than left
#       suppressing nothing, which is what the gap-closed notice exists to
#       prompt.
#
#   G3  to-upper is the identity on those same 31 letters. Structural rather
#       than a band error: the uppercase of e-acute is a TIER 1 code point,
#       and a Char carries a Tier 0 byte, so `Char -> Char` cannot express the
#       answer at all. Case above Tier 0 needs a Text-level or code-point-level
#       entry point that does not exist.
#
# G2 and G3 looked like one cluster and were separated on purpose. Closing G2
# without noticing that G3 is a different KIND of problem would have meant
# widening is-lower and letting to-upper keep asking it, which corrupts every
# punctuation character (see the controls below).

# The 31 Tier 0 letters that are neither ASCII lowercase nor ASCII uppercase.
function Is-Tier0NonAscii([int]$u) { $u -ge 0x00C0 -or ($u -ge 0x0400 -and $u -le 0x04FF) }

# Rewrite one position of the nine-flag string. Index order is
# ws dg lo up le pu ac cy hx.
function Patch-Flag([string]$flags, [int]$i, [string]$v) {
    $a = $flags.ToCharArray(); $a[$i] = $v[0]; -join $a
}

# ToLowerInvariant / ToUpperInvariant rather than the culture-sensitive pair.
# A Turkish culture maps 'I' to a dotless lowercase and would make this
# harness adjudicate a locale instead of the character.
function Host-Lower([int]$u) { [int][char]::ToLowerInvariant([char]$u) }
function Host-Upper([int]$u) { [int][char]::ToUpperInvariant([char]$u) }

# ---------------------------------------------------------------- the inputs
#
# Tier 0's 128 slots, named by the Unicode they carry. Which code points these
# are is read off the chapter's own table -- choosing an interesting input is
# not the same as being told the answer -- and every answer below still comes
# from the host.
$asciiMapped = @(0, 10) + (32..126)
$accented    = @(233,232,234,235,225,224,226,228,243,244,246,250,252,241,231,237)
$cyrillic    = @(1072,1086,1077,1080,1085,1090,1089,1088,1074,1083,1082,1084,1076,1087,1091)

$pCases = @()
foreach ($u in $asciiMapped) { $pCases += @{ u = $u; cls = 'ascii' } }
foreach ($u in $accented)    { $pCases += @{ u = $u; cls = 'accented' } }
foreach ($u in $cyrillic)    { $pCases += @{ u = $u; cls = 'cyrillic' } }

# The refusal half. Every ASCII control character except NUL and LF must come
# back -1. A carriage return is the documented case; the rest are here so the
# claim is a band rather than an anecdote.
$mustRefuse = @(1..9) + @(11..31) + @(127)

# The round-trip half, widened well past Tier 0. A code point CCE does not
# carry must be REFUSED, not silently turned into a different character -- the
# hazard the chapter's own UTF-8 prose describes, where a continuation byte
# converts cleanly into the wrong letter. Sampled by stride where a block is
# large, because what is under test is the block arithmetic and not each slot.
$rRanges = @(
    @{ n = 'ascii';        lo = 0;      hi = 127;    step = 1 },
    @{ n = 'latin1';       lo = 0x0080; hi = 0x00FF; step = 1 },
    @{ n = 'latin-ext-a';  lo = 0x0100; hi = 0x017F; step = 1 },
    @{ n = 'latin-ext-b';  lo = 0x0180; hi = 0x01FF; step = 3 },
    @{ n = 'greek';        lo = 0x0370; hi = 0x03FF; step = 1 },
    @{ n = 'cyrillic';     lo = 0x0400; hi = 0x04FF; step = 1 },
    @{ n = 'hebrew';       lo = 0x0590; hi = 0x05FF; step = 3 },
    @{ n = 'arabic';       lo = 0x0600; hi = 0x067F; step = 3 },
    @{ n = 'devanagari';   lo = 0x0900; hi = 0x097F; step = 3 },
    @{ n = 'thai';         lo = 0x0E00; hi = 0x0E7F; step = 3 },
    @{ n = 'hangul-jamo';  lo = 0x1100; hi = 0x117F; step = 3 },
    @{ n = 'gen-punct';    lo = 0x2000; hi = 0x21FF; step = 5 },
    @{ n = 'kana';         lo = 0x3040; hi = 0x30FF; step = 3 },
    @{ n = 'cjk';          lo = 0x4E00; hi = 0x5FFF; step = 53 },
    @{ n = 'hangul-syl';   lo = 0xAC00; hi = 0xD7A3; step = 211 },
    @{ n = 'emoji';        lo = 0x1F300; hi = 0x1F6FF; step = 37 },
    @{ n = 'unassigned';   lo = 0xE000; hi = 0xE0FF; step = 17 }
)

$rCases = @()
foreach ($r in $rRanges) {
    for ($u = $r.lo; $u -le $r.hi; $u += $r.step) {
        $must = ($u -in $asciiMapped)
        $refuse = ($u -in $mustRefuse)
        $rCases += @{ u = $u; cls = $r.n; must = $must; refuse = $refuse }
    }
}

# ------------------------------------------------------------ generate source
#
# Every `&` chain stays on one line: a line beginning with `&` starts a new
# expression rather than continuing the previous one, so the flag string is
# assembled through three let-bound thirds instead of one long chain.
$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine('Chapter: OracleCce')
$null = $sb.AppendLine('  cites Foreword chapter CCE')
$null = $sb.AppendLine('  cites Foreword chapter Parse')
$null = $sb.AppendLine('')
$null = $sb.AppendLine(' Generated by build/oracle-cce.ps1. Do not edit; regenerate.')
$null = $sb.AppendLine('')
$null = $sb.AppendLine(' Each P line prints the round-tripped code point, the nine predicate')
$null = $sb.AppendLine(' answers, the digit value, and both case conversions rendered back into')
$null = $sb.AppendLine(' Unicode. Each R line prints the round-tripped code point alone.')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('Section: Probes')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  b2i : Boolean -> Integer')
$null = $sb.AppendLine('  b2i (b) = if b then 1 else 0')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  flags : Char -> Text')
$null = $sb.AppendLine('  flags (c) =')
$null = $sb.AppendLine('    let a = show (b2i (is-whitespace c)) & show (b2i (is-digit c)) & show (b2i (is-lower c))')
$null = $sb.AppendLine('    in let b = show (b2i (is-upper c)) & show (b2i (is-letter c)) & show (b2i (is-punct c))')
$null = $sb.AppendLine('    in let d = show (b2i (is-accented c)) & show (b2i (is-cyrillic c)) & show (b2i (is-hex-digit c))')
$null = $sb.AppendLine('    in a & b & d')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  dval : Char -> Integer')
$null = $sb.AppendLine('  dval (c) = if is-digit c then digit-value c else (0 - 1)')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  probe : Integer -> Text')
$null = $sb.AppendLine('  probe (u) =')
$null = $sb.AppendLine('    let cp = from-unicode u')
$null = $sb.AppendLine('    in if cp < 0 then "-1 --------- -1 -1 -1"')
$null = $sb.AppendLine('    else if cp >= 128 then show (to-unicode cp) & " tier1 -1 -1 -1"')
$null = $sb.AppendLine('    else let c = code-to-char cp')
$null = $sb.AppendLine('    in let head = show (to-unicode cp) & " " & flags c')
$null = $sb.AppendLine('    in let tail = " " & show (dval c) & " " & show (to-unicode (char-code (to-lower c)))')
$null = $sb.AppendLine('    in head & tail & " " & show (to-unicode (char-code (to-upper c)))')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  rt : Integer -> Integer')
$null = $sb.AppendLine('  rt (u) = let cp = from-unicode u in if cp < 0 then (0 - 1) else to-unicode cp')
$null = $sb.AppendLine('')

$expected = [System.Collections.Generic.List[object]]::new()
$null = $sb.AppendLine('Section: Report')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  opening : [Console] Nothing')
$null = $sb.AppendLine('  opening = act')

$id = 0
foreach ($c in $pCases) {
    $id++
    $key = "P$id"
    $u = $c.u
    $flags = Host-Flags $u
    $lower = Host-Lower $u
    $upper = Host-Upper $u
    $want = "$u $flags $(Host-DigitValue $u) $lower $upper"

    # The same case computed as the documented gaps would answer it.
    $gapFlags = $flags; $gapUpper = $upper; $gap = $null
    if (Is-Tier0NonAscii $u) {
        $gapUpper = $u                              # to-upper is the identity
        $gap = 'G3'
    }
    $wantGap = if ($gap) { "$u $gapFlags $(Host-DigitValue $u) $lower $gapUpper" } else { $null }

    $null = $sb.AppendLine("    print-line-uni (`"$key `" & probe $u)")
    $expected.Add([pscustomobject]@{
        Key = $key; Want = $want; WantGap = $wantGap; Gap = $gap; Class = $c.cls
        What = "U+$('{0:X4}' -f $u) $(if ($u -ge 32 -and $u -ne 127) { "'$([char]$u)'" } else { '(control)' })"
    })
}
foreach ($c in $rCases) {
    $id++
    $key = "R$id"
    $u = $c.u
    if ($c.refuse)   { $want = '-1'; $note = 'must refuse' }
    elseif ($c.must) { $want = "$u";  $note = 'must map' }
    else             { $want = 'EITHER'; $note = 'map or refuse, never another character' }
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (rt $u))")
    $expected.Add([pscustomobject]@{
        Key = $key; Want = $want; Class = $c.cls; U = $u
        What = "U+$('{0:X4}' -f $u) [$($c.cls)] $note"
    })
}
$null = $sb.AppendLine('  end')

Set-Content -Path $src -Value $sb.ToString() -Encoding UTF8
Write-Host "generated $($expected.Count) cases -> $src"

# -------------------------------------------------------------- compile + run
& (Join-Path $repo 'build/compile.ps1') -Src $src -Out $cdx -Log $log -Kernel $Kernel | Out-Null
# compile.ps1 returns 0 both when codegen halted and, under back-to-back runs
# with a VM still lingering, when it wrote a zero-byte binary beside a full
# .map. Check the SIZE; a zero-byte kernel boots silently and reads exactly
# like a test that needs hardware.
if ((-not (Test-Path $cdx)) -or ((Get-Item $cdx).Length -eq 0)) {
    Write-Host "COMPILE FAILED (no binary or zero bytes) - see $log" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error' | Select-Object -First 10 | ForEach-Object { $_.Line }
    exit 1
}
& (Join-Path $repo 'tools/codex-vm.exe') -kernel $cdx -headless -input NUL -output $runOut -mem 3072 | Out-Null

# ----------------------------------------------------------------- adjudicate
$got = @{}
foreach ($line in (Get-Content $runOut)) {
    $t = ($line -replace '[\x00-\x1F]', '').Trim()
    if ($t -match '^([PR]\d+)\s+(.+)$') { $got[$matches[1]] = $matches[2].Trim() }
}

$fail = 0
$byClass = @{}
$mappedByClass = @{}
$gapHit = @{}
$gapClosed = [System.Collections.Generic.List[string]]::new()
foreach ($e in $expected) {
    if (-not $byClass.ContainsKey($e.Class)) { $byClass[$e.Class] = 0 }
    if (-not $got.ContainsKey($e.Key)) {
        Write-Host "MISSING $($e.Key): $($e.What)" -ForegroundColor Red
        $fail++; $byClass[$e.Class]++
        continue
    }
    $a = $got[$e.Key]
    if ($e.Want -eq 'EITHER') {
        # The whole point of this arm: refusing is honest, answering the same
        # character is correct, answering a DIFFERENT character is the silent
        # replacement this family exists to catch.
        if (-not $mappedByClass.ContainsKey($e.Class)) { $mappedByClass[$e.Class] = @(0, 0) }
        $mappedByClass[$e.Class][1]++
        if ($a -eq '-1') { continue }
        if ($a -eq "$($e.U)") { $mappedByClass[$e.Class][0]++; continue }
        Write-Host ("FAIL {0}: {1} -- got '{2}', which is a DIFFERENT character" -f $e.Key, $e.What, $a) -ForegroundColor Red
        $fail++; $byClass[$e.Class]++
        continue
    }
    if ($a -eq $e.Want) {
        # Agreeing with the host is always clean -- and when the case sits in
        # a documented gap, that gap has just closed and must be said so. A
        # suppression nobody re-tests is the exact failure this lane audits.
        if ($e.Gap) { $gapClosed.Add("$($e.Key) $($e.What) [$($e.Gap)]") }
        continue
    }
    if ($e.WantGap -and $a -eq $e.WantGap) {
        if (-not $gapHit.ContainsKey($e.Gap)) { $gapHit[$e.Gap] = 0 }
        $gapHit[$e.Gap]++
        continue
    }
    Write-Host ("FAIL {0}: {1} -- got '{2}' want '{3}'" -f $e.Key, $e.What, $a, $e.Want) -ForegroundColor Red
    $fail++; $byClass[$e.Class]++
}

if (-not $Keep) { Remove-Item $src -ErrorAction SilentlyContinue }

# Report a distribution, not just a verdict: where the disagreements
# concentrate is the finding, and a per-class coverage count makes a
# from-unicode that quietly stopped mapping a block visible even when every
# individual case is still "allowed".
Write-Host ''
Write-Host 'coverage (round-trip family, mapped / probed):'
foreach ($k in ($mappedByClass.Keys | Sort-Object)) {
    Write-Host ("  {0,-14} {1,4} / {2,-4}" -f $k, $mappedByClass[$k][0], $mappedByClass[$k][1])
}
if ($fail -gt 0) {
    Write-Host ''
    Write-Host 'disagreements by class:'
    foreach ($k in ($byClass.Keys | Sort-Object)) {
        if ($byClass[$k] -gt 0) { Write-Host ("  {0,-14} {1}" -f $k, $byClass[$k]) }
    }
}

$gapTotal = 0
foreach ($k in $gapHit.Keys) { $gapTotal += $gapHit[$k] }
if ($gapTotal -gt 0) {
    Write-Host ''
    Write-Host 'documented gaps still present (see the header for each):'
    foreach ($k in ($gapHit.Keys | Sort-Object)) {
        Write-Host ("  {0,-8} {1} cases" -f $k, $gapHit[$k]) -ForegroundColor Yellow
    }
}
if ($gapClosed.Count -gt 0) {
    Write-Host ''
    Write-Host "$($gapClosed.Count) documented gap case(s) now AGREE with the host." -ForegroundColor Cyan
    Write-Host 'The gap has closed; update the header and delete its arm.' -ForegroundColor Cyan
    $gapClosed | Select-Object -First 12 | ForEach-Object { Write-Host "  $_" }
}

$n = $expected.Count
$clean = $n - $fail - $gapTotal
Write-Host ''
if ($fail -eq 0) {
    Write-Host "oracle-cce: $clean/$n agree with the host, $gapTotal in documented gaps, 0 unexplained" -ForegroundColor Green
    exit 0
}
Write-Host "oracle-cce: $clean/$n agree with the host, $gapTotal in documented gaps, $fail DISAGREE" -ForegroundColor Red
exit 1
