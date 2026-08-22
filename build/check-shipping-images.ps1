# Refuse to ship an image that carries a SITTING config.
#
# build/boot/diag.img SHIPS to the public GitHub and GitLab mirrors by design
# (docs/Agents/PublicPush.md, DiagnosticStick.md step 4): the stranger's
# instrument, 16 MB, no seed, no identity, its SHA-256 in the release notes.
#
# A SITTING image is the same file with questions baked onto its ESP, and those
# questions name the box. diag-sitting6.cfg is two lines and one of them is
#   b3 peer=192.168.6.141:7 ip=192.168.6.200
# which is Damian's LAN. On 2026-08-21 a bulk `p4 copy --from` carried a sitting
# image to main head under a changelist about harness timing, and nothing
# anywhere would have refused it. It did not reach a mirror only because no push
# happened in the window. That is luck, and luck is not a control.
#
# WHAT COUNTS AS SHIPPING, revised 2026-08-21 the same evening (red). The first
# cut of this check refused ANY DIAG.CFG. Later that day build-diag.ps1 started
# REFUSING to build an image whose cfg leaves a non-passive stage unnamed (root,
# main 18645, the guard that exists because sitting 7 flew on a belief about a
# default), and it bakes build/boot/diag-default.cfg onto the ESP when no -Cfg
# is given. So every buildable image carries a DIAG.CFG and the two runners
# contradicted each other: no shipping image could exist. The shape that ships
# is therefore the image whose baked cfg is BYTE-IDENTICAL (after line-ending
# and trailing-whitespace normalisation) to the checked-in default, which names
# every stage and no address. Anything else is a sitting and is refused with
# the first differing line named. Still blunt: no address scanning, no argument
# about which addresses are private enough to publish.
[CmdletBinding()]
param(
    [string]$Img = 'build/boot/diag.img',
    [string]$Default = 'build/boot/diag-default.cfg',
    [string]$Log = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$imgAbs = if ([IO.Path]::IsPathRooted($Img)) { $Img } else { Join-Path $repo $Img }
$defAbs = if ([IO.Path]::IsPathRooted($Default)) { $Default } else { Join-Path $repo $Default }
if (-not (Test-Path -PathType Leaf $imgAbs)) { Write-Host "FAIL: $imgAbs missing"; exit 1 }
if (-not (Test-Path -PathType Leaf $defAbs)) { Write-Host "FAIL: $defAbs missing; the shipping shape is defined by it"; exit 1 }

$hash = (Get-FileHash $imgAbs -Algorithm SHA256).Hash
Write-Host "check-shipping-images: $(Split-Path $imgAbs -Leaf) $($hash.Substring(0,8))"

function Normalize-Cfg([string]$text) {
    $lines = ($text -replace "`r`n", "`n") -split "`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -ne '' }
    return ,@($lines)
}

$dir = Join-Path ([IO.Path]::GetTempPath()) ("shipcheck-" + (Split-Path $repo -Leaf))
if (Test-Path $dir) { [IO.Directory]::Delete($dir, $true) }
& pwsh -NoProfile -File (Join-Path $repo 'build\read-stick.ps1') `
    -ImageFile $imgAbs -Name 'DIAG.CFG' -OutDir $dir 2>&1 | Out-Null

$cfg = Join-Path $dir 'DIAG.CFG'
if (-not (Test-Path $cfg)) {
    Write-Host "OK: no DIAG.CFG on the ESP; this image is safe to publish."
    if ($Log) { "OK $hash (no cfg)" | Set-Content $Log }
    exit 0
}

$baked = Normalize-Cfg (Get-Content $cfg -Raw)
$want  = Normalize-Cfg (Get-Content $defAbs -Raw)
$differs = ''
$n = [Math]::Max($baked.Count, $want.Count)
for ($i = 0; $i -lt $n; $i++) {
    $b = if ($i -lt $baked.Count) { $baked[$i] } else { '<missing>' }
    $w = if ($i -lt $want.Count)  { $want[$i] }  else { '<missing>' }
    if ($b -ne $w) { $differs = "line $($i + 1): baked [$b] default [$w]"; break }
}
if ($differs -eq '') {
    Write-Host "OK: the baked DIAG.CFG is the checked-in default ($(Split-Path $defAbs -Leaf)), which names every stage and no address; this image is safe to publish."
    if ($Log) { "OK $hash (default cfg)" | Set-Content $Log }
    exit 0
}

Write-Host "REFUSED: $(Split-Path $imgAbs -Leaf) carries a DIAG.CFG that is NOT the checked-in default, so it is a sitting image, and this image SHIPS to the public mirrors."
Write-Host "  first difference: $differs"
Write-Host "  the baked config is:"
$baked | ForEach-Object { Write-Host "    $_" }
Write-Host "  Build the DEFAULT image before a release or a copy-up that reaches main:"
Write-Host "    build/boot/build-diag.ps1        # no -Cfg, no -StdinCfg"
Write-Host "  A sitting image is for the flight and belongs in the flying lane, not on main."
if ($Log) { "REFUSED $hash`n$differs" | Set-Content $Log }
exit 1
