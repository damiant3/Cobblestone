[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Capture,
    [Parameter(Mandatory)][int[]]$LoginConnections,
    [string]$Account = 'uoaixtest',
    [string]$Password = 'uoaixtest',
    [ValidateRange(0,255)][int]$LoginFlag = 100
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($LoginConnections.Count -ne 2 -or $LoginConnections[0] -eq $LoginConnections[1]) {
    throw 'Name exactly two distinct real-client login connections.'
}
if ($Account.Length -gt 29 -or $Password.Length -gt 29 -or
    $Account -match '[^\x20-\x7E]' -or $Password -match '[^\x20-\x7E]') {
    throw 'The capture check requires bounded ASCII test credentials.'
}

# POL LoginCrypt::Decrypt_Old and cryptkey.cpp, client 1.25.32.
# https://github.com/polserver/polserver/tree/master/pol-core/pol/crypt
function ConvertFrom-LoginCipher([byte[]]$Bytes, [uint64]$Seed) {
    [uint64]$mask = 4294967295
    [uint64]$low = (((($Seed -bxor $mask) -bxor 0x1357) -shl 16) -bor
        (($Seed -bxor 0xFFFFAAAAUL) -band 65535)) -band $mask
    [uint64]$high = (($Seed -bxor 0x43210000) -shr 16) -bor
        ((($Seed -bxor $mask) -bxor 0xABCDFFFFUL) -band 4294901760)
    $plain = [byte[]]::new($Bytes.Length)
    for ($i = 0; $i -lt $Bytes.Length; $i++) {
        $plain[$i] = $Bytes[$i] -bxor ($low -band 255)
        [uint64]$nextLow = ((($low -shr 1) -bor ($high -shl 31)) -bxor 0x026950C6) -band $mask
        $high = ((($high -shr 1) -bor ($low -shl 31)) -bxor 0x389DE58C) -band $mask
        $low = $nextLow
    }
    return ,$plain
}

$fs = [IO.File]::Open((Resolve-Path -LiteralPath $Capture), [IO.FileMode]::Open,
    [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
$reader = [IO.StreamReader]::new($fs)
try { $lines = $reader.ReadToEnd() -split '\r?\n' } finally { $reader.Dispose() }
$expected = [byte[]]::new(65)
$expected[0] = 128
[Text.Encoding]::ASCII.GetBytes($Account).CopyTo($expected, 1)
$passwordBytes=[Text.Encoding]::ASCII.GetBytes($Password)
for($i=0;$i -lt $passwordBytes.Length;$i++){ $expected[31+$i]=$passwordBytes[$i]-13 }
$expected[61] = $LoginFlag
$expected[62] = 160
$normalized = @()
foreach ($connection in $LoginConnections) {
    if ($lines -match "^REFUSE \S+ $connection(?: |$)") { throw "Connection $connection was refused." }
    $hex = [Text.StringBuilder]::new()
    foreach ($line in $lines) {
        if ($line -match "^RX $connection ([0-9A-F]+)$") { [void]$hex.Append($Matches[1]) }
    }
    $wire = [Convert]::FromHexString($hex.ToString())
    if ($wire.Length -lt 69 -or $wire.Length -gt 4096) {
        throw "Connection $connection has $($wire.Length) bytes; require complete seed, login and shard selection within capture bound."
    }
    [uint64]$seed = [uint64]$wire[0] * 16777216 + [uint64]$wire[1] * 65536 + [uint64]$wire[2] * 256 + $wire[3]
    $payload = [byte[]]$wire[4..($wire.Length-1)]
    $plain = ConvertFrom-LoginCipher $payload $seed
    $mode = 'old-login-xor'
    if ([Convert]::ToHexString($payload[0..61]) -eq [Convert]::ToHexString($expected[0..61])) {
        $plain=$payload; $mode='plaintext'
    }
    if ([Convert]::ToHexString($plain[0..61]) -ne [Convert]::ToHexString($expected[0..61])) {
        throw "Connection $connection does not match the complete 1.25.32 test login and shard selection under either hypothesis."
    }
    $cursor=62
    $hardware=0
    if($plain[$cursor] -eq 164){
        if($plain.Length -lt $cursor+149+3){throw "Connection $connection has incomplete hardware-info/selection."}
        $cursor+=149
        $hardware=1
    }
    if($plain.Length -ne $cursor+3 -or [Convert]::ToHexString($plain[$cursor..($cursor+2)]) -ne 'A00000'){
        throw "Connection $connection lacks exactly one complete shard-zero selection after login/optional hardware-info."
    }
    if ($lines -notcontains "TX-COMPLETE $connection shard-list") { throw "Connection $connection lacks completed shard-list send." }
    if ($lines -notcontains "RELAY $connection True") { throw "Connection $connection lacks completed relay send." }
    $normalized += [Convert]::ToHexString($plain)
    "connection=$connection mode=$mode seed=$($seed.ToString('X8')) login-and-selection=exact hardware-info=$hardware"
}
if ($normalized[0] -cne $normalized[1]) { throw 'Normalized streams differ.' }
'PASS: both complete login streams agree after seed-based keystream removal; no payload fields masked.'
