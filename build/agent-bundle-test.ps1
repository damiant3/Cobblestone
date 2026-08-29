# The bundled-agent chain, end to end, with its negative controls.
#
# Builds four boot images and runs the SAME guest binary against each:
#
#   honest      every link intact                    -> BUNDLE OK
#   signature   one byte of the signature flipped    -> refused at the signature
#   digest      the signed manifest claims a digest
#               the model does not have              -> refused at the digest
#   model       the model altered after signing, so
#               the signature still verifies          -> refused at the digest
#
# The last two are the ones worth having. A verifier that checked only the
# signature would pass `model`, because the signature over that manifest is
# genuine. A verifier that checked only the digest would pass `signature`. Only
# a run that refuses all three establishes that the chain is a chain.
#
# Track D item 9 (2026-08-28) added the rest of the chain's refusals, each
# bundle honest at every link EXCEPT the one its arm aims at, so a refusal
# naming the wrong link fails the arm:
#
#   size          model-bytes claims one byte more     -> refused at the size,
#                                                         BEFORE the digest runs
#   not-gguf      magic broken before hashing, so sig
#                 and digest are honest over a non-GGUF -> refused at the parse
#   no-tensors    header declares zero tensors          -> refused at the count
#   no-sig        manifest carries no sig line          -> refused at the scan
#   short-sig     30 bytes of signature hex             -> refused at the length
#   short-pub     30 bytes of pubkey hex                -> refused at the length,
#                                                         never reaching verify
#   no-model      manifest names no model               -> refused at the name
#   missing-model manifest names a file not on disk     -> refused at the lookup
#
# Absent by design: a "manifest not found" arm. build-img refuses a model with
# no manifest (both or neither), so that image is not expressible with the
# shipping builder, and the branch is the same Maybe-None read the
# missing-model arm exercises one call later.
#
# On demand: it boots several VMs and signs with the Codex Ed25519 signer, so
# it is not in `build/build.ps1`. `codex/test/apps/bundled-agent` pins the
# honest answer in the battery.
#
#   pwsh build/agent-bundle-test.ps1
#   pwsh build/agent-bundle-test.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/agent-bundle-test.ps1 -WriteDiskSidecar   # refresh the .disk
#   pwsh build/agent-bundle-test.ps1 -KeepArtifacts
[CmdletBinding()]
param(
    [string]$Kernel = '',
    # Rewrite codex/test/apps/bundled-agent.disk from the honest image. Opens
    # it for edit first; the file is a depot binary.
    [switch]$WriteDiskSidecar,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "FAIL: kernel $Kernel missing"; exit 1 }

$work = Join-Path $Repo 'build-output\agent-bundle'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

$vm      = Join-Path $Repo 'tools\codex-vm.exe'
$mkBundle = Join-Path $PSScriptRoot 'make-agent-bundle.ps1'
$mkImg    = Join-Path $PSScriptRoot 'build-img.ps1'
$compile  = Join-Path $PSScriptRoot 'compile.ps1'

# The ESP needs a BOOTX64.EFI to be a well-formed boot image. This test never
# reads it -- it reads AGENT.GGU and AGENT.MAN -- so a placeholder is honest
# and keeps the fixture small. A real boot payload would make the disk a
# stale copy of the boot path, which is a different test's job.
$placeholderPe = Join-Path $work 'placeholder.efi'
[System.IO.File]::WriteAllBytes($placeholderPe, [byte[]](0x4D,0x5A) + [byte[]]::new(510))

# ---------------------------------------------------------------- guest binary

function Build-Guest ([string]$src, [string]$tag) {
    $cdx = Join-Path $work "$tag.cdx"
    $log = Join-Path $work "$tag-compile.log"
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $compile -Src $src -Out $cdx -Log $log -Kernel $Kernel 2>&1 | Out-Null
    $rc = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($rc -ne 0 -or -not (Test-Path $cdx)) {
        Write-Host "FAIL: $tag compile failed"
        Get-Content $log -TotalCount 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    return $cdx
}

Write-Host "[agent-bundle-test] compiling the guests..."
$guestCdx = Build-Guest (Join-Path $Repo 'codex\test\apps\bundled-agent.codex') 'bundled-agent'
$heapCdx  = Build-Guest (Join-Path $Repo 'codex\test\apps\bundled-agent-heap.codex') 'bundled-agent-heap'

# ------------------------------------------------------------------- fixtures

# Derive the demo author key once; every bundle below reuses it, which saves a
# VM boot per bundle.
Write-Host "[agent-bundle-test] building fixtures (this signs, so it boots VMs)..."
$probeDir = Join-Path $work 'probe'
& pwsh -NoProfile -File $mkBundle -OutDir $probeDir -Kernel $Kernel | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: could not build the probe bundle'; exit 1 }
$probeMan = Get-Content (Join-Path $probeDir 'AGENT.MAN')
$pubHex = ($probeMan | Where-Object { $_ -like 'pubkey *' }) -replace '^pubkey ', ''
if ($pubHex.Length -ne 64) { Write-Host 'FAIL: could not read the author public key'; exit 1 }

# The two large cases are what the streaming digest is for, and the demo model
# cannot see it: at 642 bytes it is two sectors inside one cluster, so a scan
# that mishandled a cluster boundary would still pass. They also pin the two
# shapes of the final block. 300000 is not a whole number of 64-byte blocks, so
# the last sector ends in a partial one; 524288 is a whole number of sectors AND
# of blocks, so the last block is full and the padding stands alone. The digest
# they are checked against is computed on the host by .NET over the same bytes,
# which is what makes the guest's answer worth anything.
$cases = @(
    @{ Name = 'honest';    Args = @() },
    @{ Name = 'signature'; Args = @('-TamperSignature') },
    @{ Name = 'digest';    Args = @('-TamperDigest') },
    @{ Name = 'model';     Args = @('-TamperModel') },
    @{ Name = 'large';     Args = @('-PadBytes', '299358') },
    @{ Name = 'aligned';   Args = @('-PadBytes', '523646') },
    @{ Name = 'size';          Args = @('-TamperSize') },
    @{ Name = 'not-gguf';      Args = @('-BreakMagic') },
    @{ Name = 'no-tensors';    Args = @('-NoTensors') },
    @{ Name = 'no-sig';        Args = @('-OmitSigLine') },
    @{ Name = 'short-sig';     Args = @('-ShortSig') },
    @{ Name = 'short-pub';     Args = @('-ShortPub') },
    @{ Name = 'no-model';      Args = @('-NoModelLine') },
    @{ Name = 'missing-model'; Args = @('-ModelNameOverride', 'NOPE.GGU') }
)

# The heap pair is the runner for the claim that verification does not hold the
# model. Both models are LARGER than the first parse window, which is what makes
# the pair mean something: under the window the window is the whole file and a
# streaming digest and a whole-file read cost the same. Above it the digest is
# the only thing still reading, and its cost per byte must be nothing.
$heapCases = @(
    @{ Name = 'heap-small'; Args = @('-PadBytes', '2096510'); Sectors = 24576 },
    @{ Name = 'heap-large'; Args = @('-PadBytes', '4193662'); Sectors = 24576 }
)

foreach ($c in ($cases + $heapCases)) {
    $dir = Join-Path $work $c.Name
    $bundleArgs = @('-NoProfile', '-File', $mkBundle, '-OutDir', $dir, '-Kernel', $Kernel, '-PubKeyHex', $pubHex) + $c.Args
    & pwsh @bundleArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: could not build the '$($c.Name)' bundle"; exit 1 }
    $img = Join-Path $work "$($c.Name).img"
    $imgArgs = @('-NoProfile', '-File', $mkImg, '-PeInput', $placeholderPe, '-Out', $img,
                 '-Agent', (Join-Path $dir 'AGENT.GGU'), '-AgentManifest', (Join-Path $dir 'AGENT.MAN'))
    if ($c.ContainsKey('Sectors')) { $imgArgs += @('-TotalSectors', "$($c.Sectors)") }
    & pwsh @imgArgs | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $img)) { Write-Host "FAIL: could not build the '$($c.Name)' image"; exit 1 }
    $c.Img = $img
}

# ----------------------------------------------------------------------- runs

function Invoke-Guest ([string]$img, [string]$tag, [string]$cdx) {
    $out = Join-Path $work "$tag.out"
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $vm -kernel $cdx -disk $img -headless -output $out -mem 3072 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if (-not (Test-Path $out)) { return '' }
    return ((Get-Content $out -Raw) -replace '[^\x20-\x7E\r\n]', '')
}

$fail = 0
$results = @{}
foreach ($c in $cases) {
    Write-Host "[agent-bundle-test] running '$($c.Name)'..."
    $results[$c.Name] = Invoke-Guest $c.Img $c.Name $guestCdx
}
foreach ($c in $heapCases) {
    Write-Host "[agent-bundle-test] running '$($c.Name)' (heap probe)..."
    $results[$c.Name] = Invoke-Guest $c.Img $c.Name $heapCdx
}

function Assert-Contains ([string]$tag, [string]$needle) {
    $got = $results[$tag]
    if ($got -like "*$needle*") {
        Write-Host "  PASS  $tag : $needle"
    } else {
        Write-Host "  FAIL  $tag : expected '$needle'"
        ($got -split "`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -First 6) | ForEach-Object { Write-Host "        | $_" }
        $script:fail++
    }
}

Write-Host ''
Write-Host '--- the positive: every link intact ---'
Assert-Contains 'honest' 'BUNDLE OK'
Assert-Contains 'honest' 'codex-demo-agent v0.1.0'
Assert-Contains 'honest' 'architecture: codex-demo'
Assert-Contains 'honest' 'tensors: 2 first=token_embd.weight'

Write-Host ''
Write-Host '--- the digest is streamed: models that cross sectors and clusters ---'
Assert-Contains 'large'   'BUNDLE OK'
Assert-Contains 'large'   'model: AGENT.GGU (300000 bytes)'
Assert-Contains 'large'   'tensors: 2 first=token_embd.weight'
Assert-Contains 'aligned' 'BUNDLE OK'
Assert-Contains 'aligned' 'model: AGENT.GGU (524288 bytes)'
Assert-Contains 'aligned' 'tensors: 2 first=token_embd.weight'

Write-Host ''
Write-Host '--- the negatives: one link broken each ---'
Assert-Contains 'signature' 'BUNDLE REFUSED: manifest signature does not verify'
Assert-Contains 'digest'    'BUNDLE REFUSED: model digest does not match the signed manifest'
Assert-Contains 'model'     'BUNDLE REFUSED: model digest does not match the signed manifest'

# A refusal that names the wrong link is a chain that only looks like one, so
# the two digest cases must NOT be refused at the signature.
foreach ($t in @('digest', 'model')) {
    if ($results[$t] -like '*signature does not verify*') {
        Write-Host "  FAIL  $t : refused at the signature, but its signature is genuine"
        $fail++
    }
}

Write-Host ''
Write-Host '--- the rest of the chain: every refusal site, one arm each ---'
Assert-Contains 'size'          'BUNDLE REFUSED: model is 642 bytes, manifest claims 643'
Assert-Contains 'not-gguf'      'BUNDLE REFUSED: model is not a GGUF file'
Assert-Contains 'no-tensors'    'BUNDLE REFUSED: model declares no tensors'
Assert-Contains 'no-sig'        'BUNDLE REFUSED: manifest carries no signature'
Assert-Contains 'short-sig'     'BUNDLE REFUSED: signature is not 64 bytes'
Assert-Contains 'short-pub'     'BUNDLE REFUSED: public key is not 32 bytes'
Assert-Contains 'no-model'      'BUNDLE REFUSED: manifest names no model'
Assert-Contains 'missing-model' 'BUNDLE REFUSED: model not found: NOPE.GGU'

# The wrong-link discriminations. Each of these bundles is honest at the link
# named, so a refusal there means the chain's order or a check is broken:
# `size` and `not-gguf` carry honest digests (size must refuse BEFORE the
# digest runs; not-gguf's digest is honest over the broken bytes), `no-tensors`
# is a valid GGUF header, and the two truncations must refuse on length
# without reaching the verify.
$wrongLink = @(
    @{ Tag = 'size';       Not = 'digest does not match'; Why = 'its digest is honest; size must refuse first' },
    @{ Tag = 'not-gguf';   Not = 'digest does not match'; Why = 'its digest is honest over the broken bytes' },
    @{ Tag = 'no-tensors'; Not = 'not a GGUF';            Why = 'its header is valid' },
    @{ Tag = 'short-sig';  Not = 'signature does not verify'; Why = 'the length check must fire first' },
    @{ Tag = 'short-pub';  Not = 'signature does not verify'; Why = 'the length check must fire first' }
)
foreach ($w in $wrongLink) {
    if ($results[$w.Tag] -like "*$($w.Not)*") {
        Write-Host "  FAIL  $($w.Tag) : refused at the wrong link ($($w.Not)) -- $($w.Why)"
        $fail++
    }
}

Write-Host ''
Write-Host '--- verifying does not hold the model: heap across a 2x model ---'

function Get-HeapDelta ([string]$tag) {
    $m = [regex]::Match($results[$tag], '(?m)^\s*heap\s+(\d+)\s*$')
    if (-not $m.Success) { return -1 }
    return [int64]$m.Groups[1].Value
}

$hSmall = Get-HeapDelta 'heap-small'
$hLarge = Get-HeapDelta 'heap-large'
Write-Host "  2097152-byte model: $hSmall bytes of heap"
Write-Host "  4194304-byte model: $hLarge bytes of heap"

if ($hSmall -lt 0 -or $hLarge -lt 0) {
    Write-Host '  FAIL  heap probe produced no reading'
    $fail++
} elseif (($results['heap-small'] -notlike '*ok*') -or ($results['heap-large'] -notlike '*ok*')) {
    Write-Host '  FAIL  heap probe did not verify its bundle, so its reading measures nothing'
    $fail++
} else {
    # Measured 2026-07-27: identical, 27451800 both. The depot version this
    # replaced read the whole model, and the same pair read 250797424 and
    # 493018496 -- so a reintroduced whole-file read misses this by 230x. The
    # slack is for incidental allocation drift, not for growth per model byte.
    $slack = 1048576
    $growth = $hLarge - $hSmall
    if ($growth -lt $slack) {
        Write-Host "  PASS  heap grew by $growth bytes over 2097152 more model bytes"
    } else {
        Write-Host "  FAIL  heap grew by $growth bytes over 2097152 more model bytes; verification is holding the model"
        $fail++
    }
}

Write-Host ''
if ($fail -gt 0) {
    Write-Host "agent-bundle-test: $fail assertion(s) FAILED"
    Write-Host "artifacts kept in $work"
    exit 1
}

if ($WriteDiskSidecar) {
    $sidecar = Join-Path $Repo 'codex\test\apps\bundled-agent.disk'
    if (Test-Path $sidecar) { & p4 edit $sidecar | Out-Null }
    Copy-Item -Force ($cases[0].Img) $sidecar
    Write-Host "wrote $sidecar (p4 add it as type binary if it is new)"
}

if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
Write-Host 'agent-bundle-test: PASS'
exit 0
