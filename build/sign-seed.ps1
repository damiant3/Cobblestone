# sign-seed.ps1 -- sign a CDX in place, the way the gate signs the seed.
#
# Hand-maintained, like the other lane tools in this directory.
#
# WHY THIS EXISTS. CLAUDE.md prescribes "the signer compiled and run over the
# candidate" as a granted single-guest run before a seed CL lands, and until
# 2026-09-07 there was no lane-invocable signer: the only implementation was
# generated INLINE inside build.ps1's sign phase, and the documented route to a
# signed artifact was to run the whole gate. So a lane following the rule had
# nothing to run, and lanes signed with scratch mirrors of the phase that die
# with the session. This is that phase lifted verbatim, which is R-SIGN's own
# remedy: if the sign step is in the way, fix the build scripts.
#
# THE KEY LOCATION IS READ OUT OF build.ps1 AND NOT COPIED HERE. One occurrence
# in the tree stays one occurrence; a second copy is a second thing to leak, to
# rotate, and to get wrong. build.ps1 remains the authority for where it lives.
#
# WHAT THE SIGNATURE COVERS, because getting this wrong produces a file that
# looks signed and verifies nowhere: the content hash at bytes 8..39 is the
# signed payload, the public key is patched at 40..71 and the signature at
# 72..135, all IN PLACE. The file's length never changes. That is also why a
# whole-file hash cannot tell a signed seed from an unsigned candidate, which
# is P-SIGNED in PerforceProcess.md.
#
#   pwsh build/sign-seed.ps1 -Cdx build\output\Sut.cdx
#   pwsh build/sign-seed.ps1 -Cdx <candidate> -WorkDir <dir>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Cdx,
    [string]$WorkDir = '',
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $WorkDir) { $WorkDir = Join-Path $PSScriptRoot 'output' }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

if (-not (Test-Path -PathType Leaf $Cdx)) { Write-Host "FAIL: no such CDX: $Cdx"; exit 1 }
$Cdx = (Resolve-Path $Cdx).Path
if (-not $Kernel) { $Kernel = $Cdx }
$Kernel = (Resolve-Path $Kernel).Path

# One occurrence in the tree: build.ps1 declares it, this reads it.
$buildPs1 = Join-Path $PSScriptRoot 'build.ps1'
$keyLine = @(Get-Content $buildPs1 | Where-Object { $_ -match "^\s*\`$SigningKey\s*=\s*'" }) | Select-Object -First 1
if (-not $keyLine) { Write-Host 'FAIL: build.ps1 no longer declares the signing key location; this script reads it from there'; exit 1 }
$SigningKey = ($keyLine -replace "^\s*\`$SigningKey\s*=\s*'", '') -replace "'.*$", ''

if (-not (Test-Path -PathType Leaf $SigningKey)) {
    # The gate skips signing when the key is absent, so a box without it is not
    # a failure of this script. Say so rather than exiting 0 in silence: a lane
    # that believes it signed and did not will hand a wrong byte to the token.
    Write-Host 'SKIPPED: no signing key on this box; the CDX is UNCHANGED and is NOT signed.'
    exit 3
}

$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$runScript = Join-Path $PSScriptRoot 'test-run.ps1'
$cdxRaw = [System.IO.File]::ReadAllBytes($Cdx)
$keyBytes = [System.IO.File]::ReadAllBytes($SigningKey)
$hashBytes = $cdxRaw[8..39]
$keyList = ($keyBytes | ForEach-Object { $_.ToString() }) -join ', '
$hashList = ($hashBytes | ForEach-Object { $_.ToString() }) -join ', '
$signSrc = Join-Path $WorkDir 'cdx-sign-inline.codex'
$signSrcText = @"
Chapter: CdxSignInline
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
    let key = [$keyList]
    in let hash = [$hashList]
    in let pub = ed25519-public-key key
    in let sig = ed25519-sign key pub hash
    in act
      print-line-uni (bytes-to-csv pub 0 32 "")
      print-line-uni (bytes-to-csv sig 0 64 "")
    end
  end
"@
[System.IO.File]::WriteAllText($signSrc, $signSrcText)
$signCdx = Join-Path $WorkDir 'cdx-sign.cdx'
$signLog = Join-Path $WorkDir 'cdx-sign.log'
$signOut = Join-Path $WorkDir 'cdx-sign.out'
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File $compileScript -Src $signSrc -Out $signCdx -Log $signLog -Kernel $Kernel 2>&1 | Out-Null
$ErrorActionPreference = $prev
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool compile failed'; Get-Content $signLog -TotalCount 10 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }; exit 1 }
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File $runScript -Kernel $signCdx -OutFile $signOut 2>&1 | Out-Null
$ErrorActionPreference = $prev
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool run failed'; exit 1 }
$signRaw = [System.IO.File]::ReadAllText($signOut)
$signClean = $signRaw -replace '[^\x20-\x7E\r\n]', ''
$signLines = $signClean -split "`n" | Where-Object { $_.Trim() -match '^\d' }
$pubBytes = $signLines[0].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
$sigBytes = $signLines[1].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
if ($pubBytes.Count -ne 32 -or $sigBytes.Count -ne 64) { Write-Host "FAIL: bad sign output (pub=$($pubBytes.Count) sig=$($sigBytes.Count))"; exit 1 }
for ($i = 0; $i -lt 32; $i++) { $cdxRaw[40 + $i] = $pubBytes[$i] }
for ($i = 0; $i -lt 64; $i++) { $cdxRaw[72 + $i] = $sigBytes[$i] }
[System.IO.File]::WriteAllBytes($Cdx, $cdxRaw)
Write-Host "signed in place: $Cdx"
exit 0
