# IMG plug self-check: the plug's own output is the assertion, so this needs
# no target toolchain. It is the arm for 1.25, where the plug page-faulted
# before it sent and the host wrote a 1,400-byte file under an OK line: a
# structural check on the delivered image catches that and an exit code does
# not.
#
# Both filesystem paths, because the FAT16 and FAT32 writers are separate code
# and the 1.25 fault reached neither.
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [int]$TotalSectors = 32768
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PlugDir = (Resolve-Path $PSScriptRoot).Path
$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$OutDir  = Join-Path $PlugDir 'build-output'
$PlugCdx = Join-Path $OutDir 'img-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "MISSING plug; run codex/plugs/img/build.ps1"
    exit 2
}
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

$Pe  = Join-Path $Repo 'build\boot\blockladder.efi'
$Cdx = $Kernel
foreach ($f in @($Pe, $Cdx)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "MISSING input: $f"; exit 2 }
}

$expected = $TotalSectors * 512
$failed = 0

foreach ($fs in @('fat32', 'fat16')) {
    $out = Join-Path $OutDir "selfcheck-$fs.img"
    Remove-Item $out -Force -ErrorAction SilentlyContinue

    $args = @('-NoProfile', '-File', (Join-Path $PlugDir 'run.ps1'),
              '-PeInput', $Pe, '-CdxInput', $Cdx, '-Out', $out,
              '-TotalSectors', "$TotalSectors")
    if ($fs -eq 'fat16') { $args += '-Fat16' }
    & pwsh @args | Out-Null

    if (-not (Test-Path -PathType Leaf $out)) {
        Write-Host "IMG-$($fs.ToUpper()): FAIL -- no image produced"
        $failed++
        continue
    }

    # The image is the assertion. A guest that dies mid-stream leaves a short
    # file, and one that never wrote the tables leaves a long one with no
    # signatures, so length and both signatures are checked rather than either.
    $bytes = [System.IO.File]::ReadAllBytes($out)
    $len   = $bytes.Length
    $mbr   = ($len -ge 512 -and $bytes[510] -eq 0x55 -and $bytes[511] -eq 0xAA)
    $gpt   = ($len -ge 520 -and [System.Text.Encoding]::ASCII.GetString($bytes, 512, 8) -eq 'EFI PART')

    $bad = @()
    if ($len -ne $expected) { $bad += ("length {0:N0}, expected {1:N0}" -f $len, $expected) }
    if (-not $mbr) { $bad += 'no 55AA at offset 510' }
    if (-not $gpt) { $bad += 'no "EFI PART" at offset 512' }

    if ($bad.Count -gt 0) {
        Write-Host ("IMG-$($fs.ToUpper()): FAIL -- " + ($bad -join '; '))
        $failed++
    } else {
        Write-Host ("IMG-$($fs.ToUpper()): PASS ({0:N0} bytes, protective MBR, GPT header)" -f $len)
    }
}

if ($failed -gt 0) { Write-Host "IMG: FAIL"; exit 1 }
Write-Host "IMG: PASS"
exit 0
