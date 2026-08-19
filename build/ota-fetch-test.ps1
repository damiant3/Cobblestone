# ota-fetch-test.ps1 -- the OTA download flow over a real socket, answered by
# a server we did not write.
#
# codex/test/apps/ota-lwm2m-loopback drives fw-feed-response with responses it
# builds itself, so the client half and the server half share every assumption
# (docs/PM/Active/Stories/BrotliBeatsOpus.md). This harness points the
# instrument outward: aiocoap performs the RFC 7959 Block2 segmentation, the
# guest walks it over UDP, and the digest Gate A re-derives inside the guest is
# compared against one computed here on the host.
#
# NOT IN THE BATTERY and not in the gate. It needs python plus aiocoap and a
# free UDP port, the same terms build/coap-interop-test.ps1 runs on.
#
# THE NEGATIVE CONTROL IS THE POINT. -NoMagic serves byte-identical firmware
# except for the three CDX magic bytes, and the guest must move from result=0
# to result=6. Without it a green run cannot distinguish Gate A examining the
# staged image from Gate A waving it through.
#
#   pwsh build/ota-fetch-test.ps1
#   pwsh build/ota-fetch-test.ps1 -NoMagic
[CmdletBinding()]
param(
    [int]$Size = 4096,
    [int]$Port = 5683,
    [switch]$NoMagic,
    [string]$Kernel = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Python = 'D:\Python311\python.exe'
$Server = Join-Path $Repo 'build\ota-server.py'
$Client = Join-Path $Repo 'tools\ota-fetch.codex'
$Vm     = Join-Path $Repo 'tools\codex-vm.exe'
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

function Fail([string]$m) { Write-Host "ota-fetch-test: FAIL -- $m"; exit 1 }

foreach ($f in @($Python, $Server, $Client, $Vm, $Kernel)) {
    if (-not (Test-Path -PathType Leaf $f)) { Fail "missing $f" }
}
& $Python -c "import aiocoap" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "aiocoap is not installed for $Python (pip install aiocoap)" }

$Out = Join-Path $Repo 'build-output\ota-fetch'
New-Item -ItemType Directory -Force $Out | Out-Null

# The expectation is computed HERE, from the same rule the server uses, so the
# guest is never the source of the number it is checked against.
$hashPy = @'
import hashlib, sys
size = int(sys.argv[1]); magic = sys.argv[2] != "nomagic"
p = bytearray((i * 7 + i // 513) % 251 for i in range(size))
if magic and size >= 3: p[0], p[1], p[2] = 67, 68, 88
print(", ".join(str(b) for b in hashlib.sha256(bytes(p)).digest()))
'@
$hashFile = Join-Path $Out 'hash.py'
Set-Content -Path $hashFile -Value $hashPy -Encoding ascii
$magicArg = if ($NoMagic) { 'nomagic' } else { 'magic' }
$expectHash = (& $Python $hashFile $Size $magicArg).Trim()
if (-not $expectHash) { Fail 'could not compute the expected digest' }

# The client carries the manifest, so the digest the harness just computed is
# spliced into a copy of it rather than the depot file being edited.
$srcRel = 'build-output/ota-fetch/OtaFetchRun.codex'
$src    = Join-Path $Repo $srcRel
$body   = (Get-Content $Client -Raw)
$body   = $body -replace 'Chapter: OtaFetch', 'Chapter: OtaFetchRun'
$body   = [regex]::Replace($body, '(?<=of-hash : List Integer = )\[[^\]]*\]', "[$expectHash]")
$body   = [regex]::Replace($body, '(?<=of-size : Integer = )\d+', "$Size")
if ($body -notmatch [regex]::Escape($expectHash)) { Fail 'the digest splice did not take' }
Set-Content -Path $src -Value $body -NoNewline -Encoding ascii

$cdx = Join-Path $Out 'ota-fetch.cdx'
Remove-Item $cdx -Force -ErrorAction SilentlyContinue
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $srcRel -Out $cdx -Log (Join-Path $Out 'compile.log') -Kernel $Kernel *> $null
if (-not (Test-Path -PathType Leaf $cdx)) { Fail "the client did not compile; see $Out\compile.log" }

$srvOut = Join-Path $Out 'server.out'
$srv = Start-Process -FilePath $Python -ArgumentList $Server, $Size, $Port, $magicArg `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $srvOut `
    -RedirectStandardError (Join-Path $Out 'server.err')
try {
    Start-Sleep -Seconds 3
    if ($srv.HasExited) { Fail "the server exited early; see $Out\server.err" }

    $vmOut = Join-Path $Out 'guest.out'
    & $Vm -kernel $cdx -headless -mem 3072 -output $vmOut -timeout 240 `
        2> (Join-Path $Out 'guest.err') | Out-Null
    if (-not (Test-Path -PathType Leaf $vmOut)) { Fail 'the guest produced no output' }
    $line = (Get-Content $vmOut | Where-Object { $_ -match 'ota-fetch ' } | Select-Object -First 1)
    if (-not $line) { Fail "the guest printed no ota-fetch line; see $vmOut" }
} finally {
    if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host $line
$written = if ($line -match 'written=(\d+)') { [int]$Matches[1] } else { -1 }
$result  = if ($line -match 'result=(\d+)')  { [int]$Matches[1] } else { -1 }
$state   = if ($line -match 'state=(\d+)')   { [int]$Matches[1] } else { -1 }

if (-not $KeepArtifacts) { Remove-Item $src -Force -ErrorAction SilentlyContinue }

if ($NoMagic) {
    # The control. The bytes are identical but for three, and Gate A must say so.
    if ($written -ne $Size) { Fail "control: expected written=$Size, got $written" }
    if ($result -ne 6) { Fail "control: expected result=6 (bad-package), got $result -- Gate A is not examining the staged image" }
    Write-Host "ota-fetch-test: OK (control) -- $Size bytes staged and Gate A refused them"
    exit 0
}

if ($written -ne $Size) { Fail "expected written=$Size, got $written" }
if ($result -ne 0) { Fail "expected result=0, got $result" }
if ($state -ne 2) { Fail "expected state=2 (Downloaded), got $state" }
Write-Host "ota-fetch-test: OK -- $Size bytes fetched over UDP and accepted by Gate A"
exit 0
