# Build a bundled-agent package for the boot image: a real GGUF model file and
# a signed manifest that names it.
#
#   AGENT.GGU   a GGUF v3 file (header, metadata key-values, tensor table, data)
#   AGENT.MAN   an ASCII manifest whose last line is an Ed25519 signature over
#               every byte before it
#
# The guest verifies the chain in that order: signature over the manifest
# prefix, then the model's SHA-256 against the digest the manifest claims, then
# the GGUF header and tensor table. Each link is independently corruptible,
# which is what `build/agent-bundle-test.ps1` uses for its negative controls.
#
# The author key is a DEMO key derived from a fixed seed written into this
# script. It is deliberately not a secret and deliberately not the toolchain's
# key: the manifest carries only the public half, and deciding which author
# keys a device actually trusts is the trust lattice's job, not this script's.
# A fixed seed is what makes the generated bundle reproducible on any
# workspace, which is what lets the disk sidecar be a depot artifact.
#
# Usage:
#   build/make-agent-bundle.ps1 -OutDir build-output/agent
#   build/make-agent-bundle.ps1 -OutDir ... -TamperModel      # negative control
#   build/make-agent-bundle.ps1 -OutDir ... -TamperSignature  # negative control
#   build/make-agent-bundle.ps1 -OutDir ... -TamperDigest     # negative control
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$OutDir,
    [string]$Name = 'codex-demo-agent',
    [string]$Version = '0.1.0',
    [string]$Kernel = '',
    # The demo author key is fixed, so its public half is the same every run.
    # A caller building several bundles can derive it once and pass it here to
    # skip one VM boot per bundle. Omitted, it is derived.
    [string]$PubKeyHex = '',
    # Append N bytes of deterministic filler after the tensor data, so the file
    # is large enough to cross sectors and clusters. GGUF is walked from the
    # front and stops at the tensor table, so trailing bytes change nothing the
    # parser reads -- but they are covered by model-bytes and by the digest,
    # which is the point. This is what gives the guest's streaming digest a
    # model big enough to be worth streaming.
    [int]$PadBytes = 0,
    # Flip one byte of the model AFTER the manifest is signed. The signature
    # still verifies; the digest comparison must catch it.
    [switch]$TamperModel,
    # Flip one byte of the signature. The signature check must catch it.
    [switch]$TamperSignature,
    # Flip one hex digit of the claimed digest BEFORE signing, so the manifest
    # is honestly signed and honestly wrong. Only the digest check can catch it.
    [switch]$TamperDigest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "FAIL: kernel $Kernel missing"; exit 1 }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force $OutDir | Out-Null }
$OutDir = (Resolve-Path $OutDir).Path

# ---------------------------------------------------------------- GGUF writer

$bytes = [System.Collections.Generic.List[byte]]::new()
function Put-U8  ([int]$v)    { $bytes.Add([byte]($v -band 0xFF)) }
function Put-U32 ([uint32]$v) { foreach ($b in [BitConverter]::GetBytes($v)) { $bytes.Add($b) } }
function Put-U64 ([uint64]$v) { foreach ($b in [BitConverter]::GetBytes($v)) { $bytes.Add($b) } }
function Put-F32 ([single]$v) { foreach ($b in [BitConverter]::GetBytes($v)) { $bytes.Add($b) } }
function Put-Str ([string]$s) {
    $b = [System.Text.Encoding]::ASCII.GetBytes($s)
    Put-U64 ([uint64]$b.Length)
    foreach ($x in $b) { $bytes.Add($x) }
}

# GGUF value type tags, from the format's own table.
$T_UINT32 = 4
$T_STRING = 8
$T_ARRAY  = 9

function Put-KvString ([string]$k, [string]$v) { Put-Str $k; Put-U32 ([uint32]$T_STRING); Put-Str $v }
function Put-KvU32    ([string]$k, [int]$v)    { Put-Str $k; Put-U32 ([uint32]$T_UINT32); Put-U32 ([uint32]$v) }
function Put-KvStrArr ([string]$k, [string[]]$vs) {
    Put-Str $k
    Put-U32 ([uint32]$T_ARRAY)
    Put-U32 ([uint32]$T_STRING)
    Put-U64 ([uint64]$vs.Length)
    foreach ($v in $vs) { Put-Str $v }
}

$EMBED = 8
$ROWS  = 4
$ELEMS = $EMBED * $ROWS      # 32: exactly one Q8_0 block, so the dequantizer
                             # gets a whole block and not a truncated one
$ALIGN = 32

# Header
Put-U32 ([uint32]0x46554747)   # "GGUF"
Put-U32 ([uint32]3)            # version
Put-U64 ([uint64]2)            # tensor count
Put-U64 ([uint64]7)            # metadata key-value count

# Metadata. The mix is deliberate: strings, fixed-width scalars, and an array
# of strings are the three shapes the metadata walk has to step over, and the
# array is the one a naive walk gets wrong.
Put-KvString 'general.architecture'         'codex-demo'
Put-KvString 'general.name'                 $Name
Put-KvU32    'general.quantization_version' 2
Put-KvU32    'general.alignment'            $ALIGN
Put-KvU32    'codex.embedding_length'       $EMBED
Put-KvU32    'codex.block_count'            1
Put-KvStrArr 'tokenizer.ggml.tokens'        @('<pad>', 'a', 'b', 'c')

# Tensor table. Offsets are relative to the start of the tensor data region.
Put-Str 'token_embd.weight'; Put-U32 ([uint32]2); Put-U64 ([uint64]$EMBED); Put-U64 ([uint64]$ROWS); Put-U32 ([uint32]0); Put-U64 ([uint64]0)
Put-Str 'output.weight';     Put-U32 ([uint32]2); Put-U64 ([uint64]$EMBED); Put-U64 ([uint64]$ROWS); Put-U32 ([uint32]8); Put-U64 ([uint64]($ELEMS * 4))

# Pad to the declared alignment before the data region.
while (($bytes.Count % $ALIGN) -ne 0) { Put-U8 0 }
$dataStart = $bytes.Count

# token_embd.weight, F32: a ramp, so a reader that returns zeros or reads the
# wrong tensor produces a visibly different answer than one that works.
for ($i = 0; $i -lt $ELEMS; $i++) { Put-F32 ([single]($i / 8.0)) }

# output.weight, Q8_0: one block of scale (f16) + 32 signed bytes.
Put-U8 0x00; Put-U8 0x3C          # f16 1.0
for ($i = 0; $i -lt 32; $i++) { Put-U8 ($i - 16) }

# Filler, if a large model was asked for. Deterministic and non-constant: a run
# of one repeated byte would be hashed correctly by a loop that processed one
# block and skipped the rest, so the filler varies.
if ($PadBytes -gt 0) {
    $pad = [byte[]]::new($PadBytes)
    for ($i = 0; $i -lt $PadBytes; $i++) { $pad[$i] = [byte](($i * 37 + 11) % 251) }
    $bytes.AddRange($pad)
}

$model = $bytes.ToArray()

# ------------------------------------------------------------------- manifest

$sha = [System.Security.Cryptography.SHA256]::Create()
$digest = ($sha.ComputeHash($model) | ForEach-Object { $_.ToString('x2') }) -join ''
$sha.Dispose()

if ($TamperDigest) {
    # Signed honestly over a claim that is false. Only the digest comparison
    # can catch this one, which is the point of having it.
    $c = if ($digest[0] -eq '0') { '1' } else { '0' }
    $digest = $c + $digest.Substring(1)
}

# The demo author key. Not a secret; see the header comment.
$authorSeed = 0..31 | ForEach-Object { [byte](($_ * 7 + 13) -band 0xFF) }
$seedList = ($authorSeed | ForEach-Object { $_.ToString() }) -join ', '

$prefix = "codex-agent-manifest 1`n" +
          "name $Name`n" +
          "version $Version`n" +
          "model AGENT.GGU`n" +
          "model-bytes $($model.Length)`n" +
          "model-sha256 $digest`n"

# The signed message is the manifest prefix INCLUDING the pubkey line, so the
# key a verifier reads is itself covered by the signature it is checking. The
# pubkey is only known after the signer runs, so the prefix is assembled in two
# passes: derive the key, then sign prefix + pubkey line.
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$runScript     = Join-Path $PSScriptRoot 'test-run.ps1'
$work          = Join-Path $Repo 'build-output'
if (-not (Test-Path $work)) { New-Item -ItemType Directory -Force $work | Out-Null }

function Invoke-CodexSigner ([byte[]]$message) {
    $msgList = ($message | ForEach-Object { $_.ToString() }) -join ', '
    $src = Join-Path $work 'agent-sign-inline.codex'
    $text = @"
Chapter: AgentSignInline
  cites Foreword chapter Console
  cites Foreword chapter Ed25519
  cites Foreword chapter Sha512
Section: Helpers
  bytes-to-csv : List Integer, Integer, Integer, Text -> Text
  bytes-to-csv (bs) (i) (len) (acc) =
    if i >= len then acc
    else let sep = if i == 0 then "" else ","
    in bytes-to-csv bs (i + 1) len (acc & sep & show (list-at bs i))
Section: Body
  opening : [Console] Nothing = act
    let key = [$seedList]
    in let msg = [$msgList]
    in let pub = ed25519-public-key key
    in let sig = ed25519-sign key pub msg
    in act
      print-line-uni (bytes-to-csv pub 0 32 "")
      print-line-uni (bytes-to-csv sig 0 64 "")
    end
  end
"@
    [System.IO.File]::WriteAllText($src, $text)
    $cdx = Join-Path $work 'agent-sign.cdx'
    $log = Join-Path $work 'agent-sign.log'
    $out = Join-Path $work 'agent-sign.out'
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $compileScript -Src $src -Out $cdx -Log $log -Kernel $Kernel 2>&1 | Out-Null
    $rc = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($rc -ne 0) {
        Write-Host 'FAIL: agent signer compile failed'
        Get-Content $log -TotalCount 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $runScript -Kernel $cdx -OutFile $out 2>&1 | Out-Null
    $rc = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($rc -ne 0) { Write-Host 'FAIL: agent signer run failed'; exit 1 }
    $raw = [System.IO.File]::ReadAllText($out) -replace '[^\x20-\x7E\r\n]', ''
    $lines = $raw -split "`n" | Where-Object { $_.Trim() -match '^\d' }
    if ($lines.Count -lt 2) { Write-Host 'FAIL: agent signer produced no key/signature'; exit 1 }
    $pub = $lines[0].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    $sig = $lines[1].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    if ($pub.Count -ne 32 -or $sig.Count -ne 64) { Write-Host "FAIL: bad signer output (pub=$($pub.Count) sig=$($sig.Count))"; exit 1 }
    return @{ Pub = [byte[]]$pub; Sig = [byte[]]$sig }
}

Write-Host "[agent-bundle] signing manifest (boots a VM)..."
if ($PubKeyHex) {
    if ($PubKeyHex.Length -ne 64) { Write-Host "FAIL: -PubKeyHex must be 64 hex characters"; exit 1 }
    $pubHex = $PubKeyHex
} else {
    # First pass derives the public key. The message is irrelevant here; only
    # the key half of the answer is used.
    $keyOnly = Invoke-CodexSigner ([System.Text.Encoding]::ASCII.GetBytes('x'))
    $pubHex = ($keyOnly.Pub | ForEach-Object { $_.ToString('x2') }) -join ''
}

$signed = $prefix + "pubkey $pubHex`n"
$signedBytes = [System.Text.Encoding]::ASCII.GetBytes($signed)
$result = Invoke-CodexSigner $signedBytes
$actualPub = ($result.Pub | ForEach-Object { $_.ToString('x2') }) -join ''
if ($actualPub -ne $pubHex) {
    # A wrong -PubKeyHex would produce a manifest whose key line does not
    # belong to the signature over it. That refuses in the guest for a reason
    # that reads like a real tamper, so it is caught here instead.
    Write-Host "FAIL: -PubKeyHex does not match the signing key"
    exit 1
}
$sigBytes = $result.Sig

if ($TamperSignature) { $sigBytes[0] = [byte](($sigBytes[0] -bxor 0x01)) }

$sigHex = ($sigBytes | ForEach-Object { $_.ToString('x2') }) -join ''
$manifest = $signed + "sig $sigHex`n"

if ($TamperModel) {
    # Flip a byte in the tensor data, after the manifest was signed over the
    # honest digest. The signature still verifies; the digest must not.
    $model[$dataStart + 1] = [byte]($model[$dataStart + 1] -bxor 0x01)
}

$modelPath    = Join-Path $OutDir 'AGENT.GGU'
$manifestPath = Join-Path $OutDir 'AGENT.MAN'
[System.IO.File]::WriteAllBytes($modelPath, $model)
[System.IO.File]::WriteAllBytes($manifestPath, [System.Text.Encoding]::ASCII.GetBytes($manifest))

$tags = @()
if ($TamperModel) { $tags += 'model' }
if ($TamperSignature) { $tags += 'signature' }
if ($TamperDigest) { $tags += 'digest' }
$tamper = if ($tags.Count -gt 0) { " TAMPERED: $($tags -join ',')" } else { '' }
Write-Host "[agent-bundle] OK: $modelPath ($($model.Length) bytes) + $manifestPath$tamper"
