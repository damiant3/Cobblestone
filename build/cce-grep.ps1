# Grep a CCE-encoded file.
#
# WHY THIS EXISTS. Anything the compiler writes with -IrCce, and anything on a
# plug wire, is Codex Character Encoding, not ASCII and not UTF-8. Select-String
# over one of those files does not fail -- it returns ZERO MATCHES, which reads
# exactly like "the thing is not in there". On 2026-08-06 that cost a false
# reading: a 15.3 MB -IrCce artifact was grepped for `type-defs`, `builtins` and
# `sum-ctors`, answered 0 for all three, and the honest conclusion was that the
# grep had measured the encoding rather than the content. "I saw nothing" is a
# claim about the instrument until the instrument is shown able to see
# something.
#
# So this tool decodes first, and -- the part that matters -- it REFUSES to
# report a clean negative it cannot stand behind. If nothing matched and any
# code point could not be mapped, it says so and exits 2 rather than 1.
#
# THE FRAMING is exact and comes from codex/foreword/core/CCE.codex
# "Multi-byte Framing": UTF-8-shaped continuation bits, but the code point is
# BIASED per tier -- 1 byte below 128, 2 bytes below 2176, 3 bytes below 67712,
# 4 above, with the tier base added back on decode. It is not UTF-8 and
# decoding it as UTF-8 gives wrong code points above 127. The em-dash makes the
# difference concrete: U+2014 is e2 80 94 in UTF-8 and e9 a5 b8 in CCE.
#
# Decoding is ConvertFrom-CceBytesDetailed in build/vm-config.ps1, which covers
# all three tiers. It still counts what it could not map, and this tool still
# refuses a negative when that count is non-zero -- a complete decoder is not a
# reason to drop the check, because the check is about what the instrument can
# see rather than about which tiers it nominally implements.
#
#   build/cce-grep.ps1 -Pattern 'type-defs' -Path x.ir
#   build/cce-grep.ps1 -Pattern '\(def "builtins"' -Path x.ir -Context 1
#   build/cce-grep.ps1 -Pattern . -Path x.ir -Decode out.txt   # dump and stop
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Pattern,
    [Parameter(Mandatory)][string]$Path,
    [switch]$SimpleMatch,
    [int]$Context = 0,
    [switch]$List,
    [int]$Head = 40,
    [string]$Decode = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vm-config.ps1')

if (-not (Test-Path -PathType Leaf $Path)) {
    [Console]::Error.WriteLine("cce-grep: no such file: $Path"); exit 2
}
$bytes = [System.IO.File]::ReadAllBytes($Path)
$decoded = ConvertFrom-CceBytesDetailed $bytes
$text = $decoded.Text
$undecodable = $decoded.Unmapped + $decoded.Malformed
$truncated = $decoded.Truncated

if ($Decode) {
    [System.IO.File]::WriteAllText($Decode, $text)
    Write-Host ("cce-grep: decoded {0:N0} bytes -> {1:N0} chars into {2}" -f $bytes.Length, $text.Length, $Decode)
}

$lines = $text -split "`n"
$hits = [System.Collections.Generic.List[object]]::new()
for ($n = 0; $n -lt $lines.Count; $n++) {
    $isHit = if ($SimpleMatch) { $lines[$n].Contains($Pattern) } else { $lines[$n] -match $Pattern }
    if ($isHit) { $hits.Add([pscustomobject]@{ line = $n + 1; text = $lines[$n] }) }
}

Write-Host ("cce-grep: {0}  bytes={1:N0}  chars={2:N0}  lines={3:N0}  matches={4:N0}  unmapped-codepoints={5:N0}" -f `
    (Split-Path $Path -Leaf), $bytes.Length, $text.Length, $lines.Count, $hits.Count, $undecodable)
if ($truncated -gt 0) { Write-Host "  WARNING: file ends mid-sequence; last character dropped" }

if ($List) {
    if ($hits.Count -gt 0) { Write-Host "  MATCHES in $Path" }
} else {
    foreach ($h in ($hits | Select-Object -First $Head)) {
        if ($Context -gt 0) {
            $lo = [Math]::Max(0, $h.line - 1 - $Context)
            $hi = [Math]::Min($lines.Count - 1, $h.line - 1 + $Context)
            for ($k = $lo; $k -le $hi; $k++) {
                $mark = if (($k + 1) -eq $h.line) { '>' } else { ' ' }
                Write-Host ("  {0}{1,7}: {2}" -f $mark, ($k + 1), $lines[$k])
            }
            Write-Host '  --'
        } else {
            Write-Host ("  {0,7}: {1}" -f $h.line, $h.text)
        }
    }
    if ($hits.Count -gt $Head) { Write-Host ("  ... and {0:N0} more (raise -Head)" -f ($hits.Count - $Head)) }
}

# A negative is only reportable if everything decoded. Otherwise the search may
# have missed the answer inside a character this tool cannot render, which is
# the precise failure it exists to prevent.
if ($hits.Count -eq 0 -and $undecodable -gt 0) {
    Write-Host ""
    Write-Host ("  UNTRUSTWORTHY NEGATIVE: no match, but {0:N0} code points could not be" -f $undecodable)
    Write-Host  "  mapped and rendered as U+FFFD. This tool cannot say the pattern is absent."
    exit 2
}
exit $(if ($hits.Count -gt 0) { 0 } else { 1 })
