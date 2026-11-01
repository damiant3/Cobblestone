[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Journal,
    [string]$Response = '',
    [string]$ResponseBase64 = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
function Hash([string]$Text) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($Text))).ToLowerInvariant() }
function Refuse([string]$Why) { Write-Host "REFUSED: $Why"; exit 1 }

if (-not (Test-Path -LiteralPath $Journal -PathType Leaf)) { Refuse "journal not found: $Journal" }
$lines = @([IO.File]::ReadAllText($Journal, $utf8) -split "`n" | Where-Object { $_ -ne '' })
if ($lines.Count -eq 0) { Refuse 'journal holds no record' }
$previous = '0' * 64
$facts = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $m = [regex]::Match($line, '^\{"fact":"([0-9a-f]{64})","record":(.*)\}$')
    if (-not $m.Success) { Refuse "journal line $($i + 1) is not {fact, record}" }
    $fact = $m.Groups[1].Value
    $record = $m.Groups[2].Value
    if ((Hash $record) -cne $fact) { Refuse "journal line $($i + 1): fact does not hash its record" }
    $parsed = $record | ConvertFrom-Json
    if ([string]$parsed.previous -cne $previous) { Refuse "journal line $($i + 1): previous does not link to the preceding fact" }
    $facts[$fact] = $parsed
    $previous = $fact
}
Write-Host "journal: $($lines.Count) record(s), every fact hashes its record and the chain links from 64 zeroes"

if ($ResponseBase64) { $Response = $utf8.GetString([Convert]::FromBase64String($ResponseBase64)) }
if ($Response) {
    $r = [regex]::Match($Response, '^(.*),"fact":"([0-9a-f]{64})"\}$')
    if (-not $r.Success) { Refuse 'response line does not end in a fact property' }
    $fact = $r.Groups[2].Value
    if (-not $facts.ContainsKey($fact)) { Refuse "response fact $fact is in no journal record" }
    $stripped = $r.Groups[1].Value + '}'
    if ((Hash $stripped) -cne [string]$facts[$fact].response_sha256) { Refuse 'response bytes do not hash to the record''s response_sha256' }
    Write-Host "response: fact $fact found, response_sha256 matches the retained bytes"
}
Write-Host 'VERIFIED'
