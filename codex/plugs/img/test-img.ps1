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

# -- FAT16 sources: names, sizes and CONTENT off the delivered image ---------
#
# The arm the source path never had. Until 2026-09-09 the writer spelled the
# only source's name as a literal, SOURCE.SRC, and no arm here passed -Source
# at all, so neither the name nor the bytes were observed by anything. The
# checks above cannot see it: an image with no source and an image with the
# wrong source have the same length and the same two signatures.
#
# This reads the image the way a FAT driver would, off its own GPT and BPB,
# rather than recomputing our writer's layout constants. A test built from the
# writer's own arithmetic agrees with the writer by construction and would pass
# over a shared mistake (L-BOTHARMS).
$srcDir = Join-Path $OutDir 'srctest'
Remove-Item $srcDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $srcDir | Out-Null
$fixtures = [ordered]@{
    'alpha.codex'  = ("alpha " * 300).Trim()
    'beta.codex'   = "beta"
    'gamma-long-name.codex' = ("g" * 5000)
}
$srcFiles = @()
foreach ($k in $fixtures.Keys) {
    $p = Join-Path $srcDir $k
    [System.IO.File]::WriteAllText($p, $fixtures[$k], [System.Text.ASCIIEncoding]::new())
    $srcFiles += $p
}

$srcOut = Join-Path $OutDir 'selfcheck-fat16-sources.img'
Remove-Item $srcOut -Force -ErrorAction SilentlyContinue
# Through -SourceList, because that is the path a caller with a directory of
# sources must use: `pwsh -File` cannot bind an array parameter.
$srcListFile = Join-Path $OutDir 'srctest-list.txt'
[System.IO.File]::WriteAllLines($srcListFile, [string[]]$srcFiles)
$args = @('-NoProfile', '-File', (Join-Path $PlugDir 'run.ps1'),
          '-PeInput', $Pe, '-CdxInput', $Cdx, '-Out', $srcOut,
          '-TotalSectors', "$TotalSectors", '-Fat16', '-SourceList', $srcListFile)
& pwsh @args | Out-Null

$bad = @()
if (-not (Test-Path -PathType Leaf $srcOut)) {
    $bad += 'no image produced'
} else {
    $b = [System.IO.File]::ReadAllBytes($srcOut)
    # GPT header at LBA 1 names the partition entry array; entry 0 names the ESP.
    $peLba = [BitConverter]::ToUInt64($b, 512 + 72)
    $entry0 = [int]($peLba * 512)
    $partStart = [int]([BitConverter]::ToUInt64($b, $entry0 + 32) * 512)
    # BPB
    $bps      = [BitConverter]::ToUInt16($b, $partStart + 11)
    $spc      = $b[$partStart + 13]
    $reserved = [BitConverter]::ToUInt16($b, $partStart + 14)
    $numFats  = $b[$partStart + 16]
    $rootEnts = [BitConverter]::ToUInt16($b, $partStart + 17)
    $fatSects = [BitConverter]::ToUInt16($b, $partStart + 22)
    $rootOff  = $partStart + ($reserved + $numFats * $fatSects) * $bps
    # Ceiling by [Math]::Ceiling, not by the (n + d - 1) / d idiom: PowerShell's
    # `/` is floating point, so that idiom rounds an EXACT division up by one
    # and put the data region one sector high, which reads as the writer
    # misplacing every file.
    $rootSects = [int][Math]::Ceiling(($rootEnts * 32) / $bps)
    $dataOff  = $rootOff + $rootSects * $bps

    foreach ($p in $srcFiles) {
        $want = [System.IO.File]::ReadAllBytes($p)
        $leaf = [System.IO.Path]::GetFileNameWithoutExtension($p).ToUpperInvariant()
        $ext  = [System.IO.Path]::GetExtension($p).TrimStart('.').ToUpperInvariant()
        $n = ($leaf -replace '[^A-Z0-9_\-]', '_'); if ($n.Length -gt 8) { $n = $n.Substring(0,8) }
        $e = ($ext  -replace '[^A-Z0-9_\-]', '_'); if ($e.Length -gt 3) { $e = $e.Substring(0,3) }
        $name83 = $n.PadRight(8) + $e.PadRight(3)

        $found = $false
        for ($i = 0; $i -lt $rootEnts; $i++) {
            $off = $rootOff + $i * 32
            if ($b[$off] -eq 0) { break }
            $entName = [System.Text.Encoding]::ASCII.GetString($b, $off, 11)
            if ($entName -ne $name83) { continue }
            $found = $true
            $cluster = [BitConverter]::ToUInt16($b, $off + 26)
            $size    = [BitConverter]::ToUInt32($b, $off + 28)
            if ($size -ne $want.Length) {
                $bad += "$name83 size $size, expected $($want.Length)"
                break
            }
            $fileOff = $dataOff + ($cluster - 2) * $spc * $bps
            $got = [byte[]]::new($size)
            [Array]::Copy($b, $fileOff, $got, 0, $size)
            if ([Convert]::ToBase64String($got) -ne [Convert]::ToBase64String($want)) {
                $bad += "$name83 content differs from $p"
            }
            break
        }
        if (-not $found) { $bad += "no root directory entry named '$name83'" }
    }
    # The old literal must be gone, or a stale writer would satisfy every check
    # above by accident on a single-source image.
    for ($i = 0; $i -lt $rootEnts; $i++) {
        $off = $rootOff + $i * 32
        if ($b[$off] -eq 0) { break }
        if ([System.Text.Encoding]::ASCII.GetString($b, $off, 11) -eq 'SOURCE  SRC') {
            $bad += 'the hardcoded SOURCE.SRC entry is still on the image'
        }
    }
}

if ($bad.Count -gt 0) {
    Write-Host ("IMG-FAT16-SOURCES: FAIL -- " + ($bad -join '; '))
    $failed++
} else {
    Write-Host "IMG-FAT16-SOURCES: PASS ($($srcFiles.Count) sources, each by its own name, size and bytes)"
}

if ($failed -gt 0) { Write-Host "IMG: FAIL"; exit 1 }
Write-Host "IMG: PASS"
exit 0
