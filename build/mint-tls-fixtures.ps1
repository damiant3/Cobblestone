# mint-tls-fixtures.ps1 -- regenerate the Ed25519 test CA and leaf, WITH a SAN.
#
# The old fixtures asserted no identity at all: CN=device.codex.test and no
# subjectAltName. That was survivable only while nothing checked identity, and
# once the TLS client does check it, a certificate that names no host is a
# certificate that matches no host. These carry the names the tests actually
# dial.
#
# Both keys are derived from seeds written down HERE, so this is reproducible.
# There is no security property to protect: it is a test CA whose whole job is
# to be forgeable by anyone reading the repository. The previous fixtures were
# minted outside Codex with a key nobody kept, which is why re-minting them
# needed a new CA rather than a new leaf.
#
# Dates are pinned with `openssl ca -startdate/-enddate` (req -x509 cannot fix
# them), so the bytes do not move with the wall clock and `cal-now` in the
# fixtures stays valid.
#
# Usage: build/mint-tls-fixtures.ps1 [-Emit]     # -Emit prints Codex literals

[CmdletBinding()]
param([switch]$Emit, [switch]$Patch)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out  = Join-Path $Repo 'build-output\tls-fixtures'
$Ssl  = 'C:\Program Files\Git\usr\bin\openssl.exe'
if (-not (Test-Path $Ssl)) { throw "openssl not found at $Ssl" }

# The leaf seed is the one the existing fixtures already carry as `leaf-priv`,
# so leaf-priv and leaf-pub do not move and only the CERTIFICATE is re-minted.
$LeafSeed = [byte[]]@(
  76,205,8,155,40,255,150,218,157,182,195,70,236,17,78,15,
  91,138,49,159,53,171,166,36,218,140,246,237,79,184,166,251)

# The CA seed is new, because the old CA's key was never kept.
$CaSeed = [byte[]]@(
  17,34,51,68,85,102,119,136,153,170,187,204,221,238,255,16,
  32,48,64,80,96,112,128,144,160,176,192,208,224,240,1,2)

$Start = '200101000000Z'
$End   = '400101000000Z'

Remove-Item -Recurse -Force $Out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Out 'newcerts') | Out-Null
Set-Content -Path (Join-Path $Out 'index.txt') -Value '' -NoNewline
Set-Content -Path (Join-Path $Out 'serial')    -Value "01`n" -NoNewline

function Write-Ed25519Key([byte[]]$seed, [string]$path) {
    # PKCS#8 for Ed25519: SEQUENCE, version 0, AlgorithmIdentifier 1.3.101.112,
    # OCTET STRING wrapping an OCTET STRING of the 32-byte seed. openssl has no
    # invocation that accepts a raw seed, so the wrapper is written by hand.
    $pkcs8 = [byte[]]@(0x30,0x2e,0x02,0x01,0x00,0x30,0x05,0x06,0x03,0x2b,0x65,0x70,0x04,0x22,0x04,0x20) + $seed
    $b64 = [System.Convert]::ToBase64String($pkcs8) -split '(.{64})' | Where-Object { $_ }
    Set-Content -Path $path -Encoding ascii -Value (@('-----BEGIN PRIVATE KEY-----') + $b64 + @('-----END PRIVATE KEY-----'))
}

$caKey   = Join-Path $Out 'ca.key'
$leafKey = Join-Path $Out 'leaf.key'
Write-Ed25519Key $CaSeed   $caKey
Write-Ed25519Key $LeafSeed $leafKey

$cfg = Join-Path $Out 'ca.cnf'
$outFwd = $Out.Replace('\','/')
Set-Content -Path $cfg -Encoding ascii -Value @"
[ ca ]
default_ca = CA_default

[ CA_default ]
dir             = $outFwd
database        = `$dir/index.txt
serial          = `$dir/serial
new_certs_dir   = `$dir/newcerts
certificate     = `$dir/ca.pem
private_key     = `$dir/ca.key
default_md      = default
policy          = policy_any
email_in_dn     = no
rand_serial     = no
unique_subject  = no
preserve        = no

[ policy_any ]
commonName = supplied

[ req ]
distinguished_name = dn
prompt = no

[ dn ]
CN = placeholder

[ ca_ext ]
basicConstraints = critical,CA:TRUE

[ leaf_ext ]
basicConstraints = critical,CA:FALSE
subjectAltName = DNS:device.codex.test, DNS:localhost, IP:10.0.2.2
"@

function Invoke-Ssl([string[]]$sslArgs, [string]$what) {
    $err = & $Ssl @sslArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$what failed: $($err -join "`n")" }
}

# --- the CA, self-signed through `ca -selfsign` so its dates are fixed too ---
Invoke-Ssl @('req','-new','-key',$caKey,'-out',(Join-Path $Out 'ca.csr'),
             '-subj','/CN=Codex Test CA','-config',$cfg) 'ca csr'
Invoke-Ssl @('ca','-selfsign','-batch','-notext','-config',$cfg,
             '-keyfile',$caKey,'-in',(Join-Path $Out 'ca.csr'),
             '-out',(Join-Path $Out 'ca.pem'),'-extensions','ca_ext',
             '-startdate',$Start,'-enddate',$End) 'ca selfsign'

# --- the leaf, signed by that CA, carrying the names the tests dial ---
Invoke-Ssl @('req','-new','-key',$leafKey,'-out',(Join-Path $Out 'leaf.csr'),
             '-subj','/CN=device.codex.test','-config',$cfg) 'leaf csr'
Invoke-Ssl @('ca','-batch','-notext','-config',$cfg,
             '-cert',(Join-Path $Out 'ca.pem'),'-keyfile',$caKey,
             '-in',(Join-Path $Out 'leaf.csr'),'-out',(Join-Path $Out 'leaf.pem'),
             '-extensions','leaf_ext','-startdate',$Start,'-enddate',$End) 'leaf sign'

Invoke-Ssl @('x509','-in',(Join-Path $Out 'ca.pem'),  '-outform','DER','-out',(Join-Path $Out 'ca.der'))   'ca der'
Invoke-Ssl @('x509','-in',(Join-Path $Out 'leaf.pem'),'-outform','DER','-out',(Join-Path $Out 'leaf.der')) 'leaf der'

# The leaf public key must still equal the `leaf-pub` the fixtures carry, or
# every server fixture that presents this leaf is signing with the wrong key.
$pubPath = Join-Path $Out 'leaf.pub.der'
Invoke-Ssl @('pkey','-in',$leafKey,'-pubout','-outform','DER','-out',$pubPath) 'leaf pubkey'
$pubAll = [System.IO.File]::ReadAllBytes($pubPath)
$pubBytes = $pubAll[($pubAll.Length - 32)..($pubAll.Length - 1)]

Write-Host "mint-tls-fixtures: ca.der $((Get-Item (Join-Path $Out 'ca.der')).Length) bytes, leaf.der $((Get-Item (Join-Path $Out 'leaf.der')).Length) bytes"
Write-Host "mint-tls-fixtures: leaf public key $(($pubBytes | ForEach-Object { $_ }) -join ',')"
& $Ssl x509 -in (Join-Path $Out 'leaf.pem') -noout -text | Select-String -Pattern 'DNS:|IP Address|Issuer:|Subject:|Not Before|Not After' | ForEach-Object { Write-Host "  $($_.Line.Trim())" }

if ($Emit) {
    function Emit-Literal([string]$name, [string]$path) {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $lines = @()
        for ($i = 0; $i -lt $bytes.Length; $i += 16) {
            $chunk = $bytes[$i..([Math]::Min($i + 15, $bytes.Length - 1))]
            $lines += '     ' + (($chunk | ForEach-Object { [string][int]$_ }) -join ', ')
        }
        $body = ($lines -join ",`n").TrimStart()
        Write-Output "  $name : List Integer ="
        Write-Output "    [$body]"
        Write-Output ''
    }
    Emit-Literal 'ca-cert'   (Join-Path $Out 'ca.der')
    Emit-Literal 'leaf-cert' (Join-Path $Out 'leaf.der')
}

if ($Patch) {
    # Every place the old fixture bytes were pasted. Hand-editing six files of
    # DER by eye is how one of them ends up one byte different from the others
    # and the failure reads as a protocol bug, so the paste is mechanical.
    $targets = @(
        @{ File = 'codex\test\apps\tls-fetch-loopback.codex';  Names = @('ca-cert','leaf-cert') }
        @{ File = 'codex\test\apps\tls-noauth-loopback.codex'; Names = @('ca-cert','leaf-cert') }
        @{ File = 'codex\test\apps\dtls-auth-loopback.codex';  Names = @('ca-cert','leaf-cert') }
        @{ File = 'codex\test\apps\dtls-app-loopback.codex';   Names = @('ca-cert','leaf-cert') }
        @{ File = 'tools\tls-serve.codex';                     Names = @('leaf-cert') }
        @{ File = 'tools\mqtts-client.codex';                  Names = @('mqtts-ca') }
    )
    $bodyFor = @{
        'ca-cert'   = [System.IO.File]::ReadAllBytes((Join-Path $Out 'ca.der'))
        'leaf-cert' = [System.IO.File]::ReadAllBytes((Join-Path $Out 'leaf.der'))
        'mqtts-ca'  = [System.IO.File]::ReadAllBytes((Join-Path $Out 'ca.der'))
    }
    foreach ($t in $targets) {
        $path = Join-Path $Repo $t.File
        $text = [System.IO.File]::ReadAllText($path)
        foreach ($n in $t.Names) {
            $bytes = $bodyFor[$n]
            $lines = @()
            for ($i = 0; $i -lt $bytes.Length; $i += 16) {
                $chunk = $bytes[$i..([Math]::Min($i + 15, $bytes.Length - 1))]
                $lines += '     ' + (($chunk | ForEach-Object { [string][int]$_ }) -join ', ')
            }
            $body = ($lines -join ",`n").TrimStart()
            $re = [regex]("(?s)($([regex]::Escape($n))\s*:\s*List Integer\s*=\s*\[)[^\]]*(\])")
            if (-not $re.IsMatch($text)) { throw "no literal named $n in $($t.File)" }
            $text = $re.Replace($text, "`${1}$body`${2}", 1)
        }
        [System.IO.File]::WriteAllText($path, $text)
        Write-Host "mint-tls-fixtures: patched $($t.File) [$($t.Names -join ', ')]"
    }
}
