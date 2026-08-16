# Arms for the cdx-to-pe input guard.
#
# A checker that accepts everything and a checker that works are
# indistinguishable on a well-formed CDX, so every refusal arm is paired with
# an acceptance arm, and one of the acceptance arms is deliberately
# malformed-LOOKING: an unsigned CDX has 96 zero bytes where the signature
# goes, which is legal on any build made without the signing key. Requiring a
# signature would refuse ordinary artifacts; that pair is what separates "the
# hash rule is applied" from "nothing was noticed".
param([string]$Src = 'build\output\Sut.cdx', [string]$Work = '.', [string]$Script = 'build\cdx-to-pe.ps1')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

New-Item -ItemType Directory -Force -Path $Work | Out-Null
$good = [System.IO.File]::ReadAllBytes($Src)
function Save([string]$name, [byte[]]$bytes) {
    $p = Join-Path $Work $name
    [System.IO.File]::WriteAllBytes($p, $bytes)
    return $p
}

# ACCEPT: the artifact as built.
$aGood = Save 'arm-good.cdx' $good

# ACCEPT: signature bytes 40..135 zeroed. Unsigned is legal.
$unsigned = [byte[]]::new($good.Length); $good.CopyTo($unsigned, 0)
for ($i = 40; $i -lt 136; $i++) { $unsigned[$i] = 0 }
$aUnsigned = Save 'arm-unsigned.cdx' $unsigned

# ACCEPT: six bytes cut off the END, which lands in the debug map's string
# table. This documents the LIMIT of the guard rather than hiding it: the
# string search is already bounded by $cdx.Length and a symbol it then fails
# to find refuses by the pre-existing __syscall_handler throw, so the tail is
# safe to lose. An arm that expected a refusal here would be asserting a
# property the tool does not have and should not claim.
$tail = $good[0..($good.Length - 7)]
$aTail = Save 'arm-tailcut.cdx' $tail

# REFUSE: truncated INTO the content region, so the header's section lengths
# claim bytes that are not there. This is blu's truncation shape -- header
# intact, length disagreeing -- and the one a checksum alone cannot catch,
# because the hash cannot even be computed over the range the header names.
$dbg = [BitConverter]::ToInt32($good, 220)
$cut = $good[0..([int]($dbg / 2))]
$aCut = Save 'arm-truncated.cdx' $cut

# REFUSE: one bit flipped inside the content region. Every length is correct;
# only the integrity field disagrees. This is the arm a bounds check cannot
# catch and the hash must.
$flip = [byte[]]::new($good.Length); $good.CopyTo($flip, 0)
$flip[1000] = $flip[1000] -bxor 1
$aFlip = Save 'arm-bitflip.cdx' $flip

# REFUSE: magic clobbered.
$mag = [byte[]]::new($good.Length); $good.CopyTo($mag, 0)
$mag[0] = 88
$aMagic = Save 'arm-badmagic.cdx' $mag

# REFUSE: text size overstated by 4 KB. Nothing is truncated and the hash is
# untouched; the header simply claims more than the file holds. Caught by the
# bounds test and NOT by the hash, which is why both exist.
$big = [byte[]]::new($good.Length); $good.CopyTo($big, 0)
$sz = [BitConverter]::ToInt64($big, 176)
[BitConverter]::GetBytes([long]($sz + 4096)).CopyTo($big, 176)
$aBig = Save 'arm-bigtext.cdx' $big

$arms = @(
    @{ N = 'good';        P = $aGood;     Want = 'ACCEPT'; Why = '' }
    @{ N = 'unsigned';    P = $aUnsigned; Want = 'ACCEPT'; Why = '' }
    @{ N = 'tailcut';     P = $aTail;     Want = 'ACCEPT'; Why = '' }
    # Each refusal names the reason it must give. A guard that refused
    # everything for one reason would pass a bare REFUSE/ACCEPT check and fail
    # here, which is the difference between "it said no" and "it said no for
    # the right reason".
    @{ N = 'truncated';   P = $aCut;      Want = 'REFUSE'; Why = 'claims bytes up to' }
    @{ N = 'bitflip';     P = $aFlip;     Want = 'REFUSE'; Why = 'content hash mismatch' }
    @{ N = 'badmagic';    P = $aMagic;    Want = 'REFUSE'; Why = 'magic is not CDX1' }
    @{ N = 'bigtext';     P = $aBig;      Want = 'REFUSE'; Why = 'do not tile' }
)

$fails = 0
foreach ($a in $arms) {
    $out = Join-Path $Work "$($a.N).efi"
    Remove-Item -Force $out -ErrorAction SilentlyContinue
    $log = & pwsh -NoProfile -File $Script -CdxInput $a.P -Out $out 2>&1
    $code = $LASTEXITCODE
    $made = Test-Path $out
    $got = if ($code -eq 0 -and $made) { 'ACCEPT' } else { 'REFUSE' }
    # PowerShell's error formatting ECHOES THE SOURCE LINE before the message,
    # so the first `REFUSED:` in the output is the literal `$why` from the
    # throw statement rather than the interpolated text. It also hard-wraps the
    # message and gutters each continuation with '|'. Strip the gutters,
    # collapse the wrap, and take the LAST match -- the first version of this
    # scored every arm against the source echo and reported four false
    # failures that looked like guard defects.
    $text = (($log | ForEach-Object { "$_" }) -join ' ') -replace '\|', ' ' -replace '\s+', ' '
    $why = ''
    $ms = [regex]::Matches($text, 'REFUSED: ([^.]+)')
    if ($ms.Count -gt 0) { $why = $ms[$ms.Count - 1].Groups[1].Value.Trim() }
    if ($why -eq '$why') { $why = '' }
    # Clamp for DISPLAY only. Scoring against the clamped string silently
    # failed the one arm whose phrase sits past column 74.
    $show = if ($why.Length -gt 74) { $why.Substring(0, 74) } else { $why }
    $ok = ($got -eq $a.Want)
    # An acceptance arm must carry NO refusal text, and a refusal arm must
    # carry the one it named.
    if ($ok -and $a.Want -eq 'REFUSE' -and $why -notmatch [regex]::Escape($a.Why)) { $ok = $false }
    if ($ok -and $a.Want -eq 'ACCEPT' -and $why -ne '') { $ok = $false }
    if (-not $ok) { $fails++ }
    "{0,-10} want={1} got={2} {3}  {4}" -f $a.N, $a.Want, $got, $(if ($ok) { 'ok  ' } else { 'FAIL' }), $show
}
"arms failing: $fails"
