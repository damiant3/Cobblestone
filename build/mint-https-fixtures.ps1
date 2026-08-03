# mint-https-fixtures.ps1 -- the ECDSA P-256 and RSA test chains for the
# https interop harness.
#
# build/mint-tls-fixtures.ps1 mints the Ed25519 chain and is the model for this
# one, including the reason the dates are pinned with `openssl ca
# -startdate/-enddate`: a fixture whose bytes move with the wall clock is a
# fixture whose `cal-now` stops being valid on a day nobody chose.
#
# Three chains, because the harness needs three answers:
#   ec    -- P-256 CA over a P-256 leaf. The server's CertificateVerify is
#            ecdsa_secp256r1_sha256, and the leaf's own signature is ECDSA too.
#   rsa   -- RSA-2048 CA over an RSA-2048 leaf. The CertificateVerify is
#            rsa_pss_rsae_sha256; the certificate signatures are PKCS#1 v1.5.
#   rogue -- a self-signed P-256 leaf with the same name and no anchor anywhere.
#            The control: it must be refused.
#
# Unlike the Ed25519 minter these keys are not derived from a written-down
# seed, so the PEMs are the source of truth and they are checked in under
# codex/test/fixtures/https/. There is no security property to protect: this is
# a test CA whose whole job is to be forgeable by anyone reading the repository.
#
# Usage: build/mint-https-fixtures.ps1 [-Patch]   # -Patch writes the CA
#                                                 # literals into the guest

[CmdletBinding()]
param([switch]$Patch)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out  = Join-Path $Repo 'codex\test\fixtures\https'
$Work = Join-Path $Repo 'build-output\https-fixtures'
$Ssl  = 'C:\Program Files\Git\usr\bin\openssl.exe'
if (-not (Test-Path $Ssl)) { throw "openssl not found at $Ssl" }

$Start = '200101000000Z'
$End   = '400101000000Z'

Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Work | Out-Null
New-Item -ItemType Directory -Force $Out  | Out-Null

function Invoke-Ssl([string[]]$sslArgs, [string]$what) {
    $err = & $Ssl @sslArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$what failed: $($err -join "`n")" }
}

function New-CaDir([string]$name) {
    $d = Join-Path $Work $name
    New-Item -ItemType Directory -Force $d | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $d 'newcerts') | Out-Null
    Set-Content -Path (Join-Path $d 'index.txt') -Value '' -NoNewline
    Set-Content -Path (Join-Path $d 'serial')    -Value "01`n" -NoNewline
    $fwd = $d.Replace('\','/')
    Set-Content -Path (Join-Path $d 'ca.cnf') -Encoding ascii -Value @"
[ ca ]
default_ca = CA_default

[ CA_default ]
dir             = $fwd
database        = `$dir/index.txt
serial          = `$dir/serial
new_certs_dir   = `$dir/newcerts
certificate     = `$dir/ca.pem
private_key     = `$dir/ca.key
default_md      = sha256
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
subjectAltName = DNS:localhost, IP:10.0.2.2
"@
    return $d
}

function New-Key([string]$path, [string]$kind) {
    if ($kind -eq 'ec') {
        Invoke-Ssl @('genpkey','-algorithm','EC','-pkeyopt','ec_paramgen_curve:P-256','-out',$path) "$kind key"
    } else {
        Invoke-Ssl @('genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',$path) "$kind key"
    }
}

# One chain: a self-signed CA and a leaf under it, both with pinned dates.
function New-Chain([string]$name, [string]$kind, [string]$caCn, [string]$leafCn) {
    $d    = New-CaDir $name
    $cfg  = Join-Path $d 'ca.cnf'
    $caK  = Join-Path $d 'ca.key'
    $lfK  = Join-Path $d 'leaf.key'
    New-Key $caK $kind
    New-Key $lfK $kind

    Invoke-Ssl @('req','-new','-key',$caK,'-out',(Join-Path $d 'ca.csr'),
                 '-subj',"/CN=$caCn",'-config',$cfg) "$name ca csr"
    Invoke-Ssl @('ca','-selfsign','-batch','-notext','-config',$cfg,
                 '-keyfile',$caK,'-in',(Join-Path $d 'ca.csr'),
                 '-out',(Join-Path $d 'ca.pem'),'-extensions','ca_ext',
                 '-startdate',$Start,'-enddate',$End) "$name ca selfsign"

    Invoke-Ssl @('req','-new','-key',$lfK,'-out',(Join-Path $d 'leaf.csr'),
                 '-subj',"/CN=$leafCn",'-config',$cfg) "$name leaf csr"
    Invoke-Ssl @('ca','-batch','-notext','-config',$cfg,
                 '-cert',(Join-Path $d 'ca.pem'),'-keyfile',$caK,
                 '-in',(Join-Path $d 'leaf.csr'),'-out',(Join-Path $d 'leaf.pem'),
                 '-extensions','leaf_ext','-startdate',$Start,'-enddate',$End) "$name leaf sign"

    Invoke-Ssl @('x509','-in',(Join-Path $d 'ca.pem'),'-outform','DER',
                 '-out',(Join-Path $d 'ca.der')) "$name ca der"

    Copy-Item (Join-Path $d 'ca.pem')   (Join-Path $Out "$name-ca.pem")   -Force
    Copy-Item (Join-Path $d 'leaf.pem') (Join-Path $Out "$name-leaf.pem") -Force
    Copy-Item $lfK                      (Join-Path $Out "$name-leaf.key") -Force
    Write-Host "mint-https-fixtures: $name chain -- ca $((Get-Item (Join-Path $d 'ca.der')).Length) DER bytes"
    return (Join-Path $d 'ca.der')
}

# The rogue: genuine, well formed, correctly named, and signed by nobody the
# guest trusts. The whole point is that it is indistinguishable from the real
# ones except in who vouches for it.
function New-Rogue() {
    $d   = New-CaDir 'rogue'
    $cfg = Join-Path $d 'ca.cnf'
    $k   = Join-Path $d 'leaf.key'
    New-Key $k 'ec'
    Invoke-Ssl @('req','-new','-key',$k,'-out',(Join-Path $d 'leaf.csr'),
                 '-subj','/CN=rogue','-config',$cfg) 'rogue csr'
    Invoke-Ssl @('ca','-selfsign','-batch','-notext','-config',$cfg,
                 '-keyfile',$k,'-in',(Join-Path $d 'leaf.csr'),
                 '-out',(Join-Path $d 'leaf.pem'),'-extensions','leaf_ext',
                 '-startdate',$Start,'-enddate',$End) 'rogue selfsign'
    Copy-Item (Join-Path $d 'leaf.pem') (Join-Path $Out 'rogue-leaf.pem') -Force
    Copy-Item $k                        (Join-Path $Out 'rogue-leaf.key') -Force
    Write-Host "mint-https-fixtures: rogue self-signed leaf"
}

$ecCa  = New-Chain 'ec'  'ec'  'Codex HTTPS EC Test CA'  'localhost'
$rsaCa = New-Chain 'rsa' 'rsa' 'Codex HTTPS RSA Test CA' 'localhost'
New-Rogue

foreach ($n in @('ec','rsa')) {
    & $Ssl x509 -in (Join-Path $Out "$n-leaf.pem") -noout -text |
        Select-String -Pattern 'Signature Algorithm|Public-Key|DNS:|IP Address|Not Before|Not After' |
        Select-Object -First 6 | ForEach-Object { Write-Host "  $n $($_.Line.Trim())" }
}

if ($Patch) {
    $guest = Join-Path $Repo 'tools\https-client.codex'
    $text  = [System.IO.File]::ReadAllText($guest)
    foreach ($p in @(@{ N = 'https-ec-ca'; F = $ecCa }, @{ N = 'https-rsa-ca'; F = $rsaCa })) {
        $bytes = [System.IO.File]::ReadAllBytes($p.F)
        $lines = @()
        for ($i = 0; $i -lt $bytes.Length; $i += 16) {
            $chunk = $bytes[$i..([Math]::Min($i + 15, $bytes.Length - 1))]
            $lines += '     ' + (($chunk | ForEach-Object { [string][int]$_ }) -join ', ')
        }
        $body = ($lines -join ",`n").TrimStart()
        $re = [regex]("(?s)($([regex]::Escape($p.N))\s*:\s*List Integer\s*=\s*\[)[^\]]*(\])")
        if (-not $re.IsMatch($text)) { throw "no literal named $($p.N) in tools/https-client.codex" }
        $text = $re.Replace($text, "`${1}$body`${2}", 1)
    }
    [System.IO.File]::WriteAllText($guest, $text)
    Write-Host "mint-https-fixtures: patched tools/https-client.codex [https-ec-ca, https-rsa-ca]"
}
