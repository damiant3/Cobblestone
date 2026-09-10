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

# -- FAT16 subdirectories: placement, collisions, and the chain --------------
#
# The arm for the writer's directory placement. The root holds 509 usable
# entries and cannot hold two names that fold to one 8.3 spelling, so a corpus
# larger than that, or one carrying a collision, can only reach an image
# through subdirectories.
#
# Two of these fixtures fold to the same 8.3 name, so a pass requires that the
# two landed in DIFFERENT directories: a writer that placed both in one would
# satisfy every size and content check below on whichever entry it found first.
# -BucketSize 2 forces more than one directory out of five sources.
$dirDir = Join-Path $OutDir 'dirtest'
Remove-Item $dirDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dirDir | Out-Null
$dirFixtures = [ordered]@{
    'collision-one.codex' = ("one " * 700).Trim()
    'collision-two.codex' = "two"
    'delta.codex'         = ("d" * 3000)
    'epsilon.codex'       = "epsilon"
    'zeta.codex'          = ("z" * 40)
}
$dirFiles = @()
foreach ($k in $dirFixtures.Keys) {
    $p = Join-Path $dirDir $k
    [System.IO.File]::WriteAllText($p, $dirFixtures[$k], [System.Text.ASCIIEncoding]::new())
    $dirFiles += $p
}
$dirListFile = Join-Path $OutDir 'dirtest-list.txt'
[System.IO.File]::WriteAllLines($dirListFile, [string[]]$dirFiles)
$dirOut = Join-Path $OutDir 'selfcheck-fat16-dirs.img'
Remove-Item $dirOut -Force -ErrorAction SilentlyContinue
$args = @('-NoProfile', '-File', (Join-Path $PlugDir 'run.ps1'),
          '-PeInput', $Pe, '-CdxInput', $Cdx, '-Out', $dirOut,
          '-TotalSectors', "$TotalSectors", '-Fat16', '-SourceList', $dirListFile,
          '-Bucketed', '-BucketSize', '2')
& pwsh @args | Out-Null

$bad = @()
if (-not (Test-Path -PathType Leaf $dirOut)) {
    $bad += 'no image produced'
} else {
    $b = [System.IO.File]::ReadAllBytes($dirOut)
    $peLba = [BitConverter]::ToUInt64($b, 512 + 72)
    $entry0 = [int]($peLba * 512)
    $partStart = [int]([BitConverter]::ToUInt64($b, $entry0 + 32) * 512)
    $bps      = [BitConverter]::ToUInt16($b, $partStart + 11)
    $spc      = $b[$partStart + 13]
    $reserved = [BitConverter]::ToUInt16($b, $partStart + 14)
    $numFats  = $b[$partStart + 16]
    $rootEnts = [BitConverter]::ToUInt16($b, $partStart + 17)
    $fatSects = [BitConverter]::ToUInt16($b, $partStart + 22)
    $fatOff   = $partStart + $reserved * $bps
    $rootOff  = $partStart + ($reserved + $numFats * $fatSects) * $bps
    $rootSects = [int][Math]::Ceiling(($rootEnts * 32) / $bps)
    $dataOff  = $rootOff + $rootSects * $bps

    # A directory is a cluster CHAIN, so its entries are read by following the
    # FAT rather than by assuming one cluster: a bucket larger than the entries
    # one cluster holds would otherwise read as a writer that lost files.
    function Get-ClusterChain([byte[]]$img, [int]$fatOff, [int]$first) {
        $chain = @()
        $c = $first
        $guard = 0
        while ($c -ge 2 -and $c -lt 0xFFF8 -and $guard -lt 65536) {
            $chain += $c
            $c = [BitConverter]::ToUInt16($img, $fatOff + $c * 2)
            $guard++
        }
        return ,$chain
    }
    function Get-DirEntries([byte[]]$img, [int]$dataOff, [int]$spc, [int]$bps, [int[]]$chain) {
        $out = @()
        foreach ($c in $chain) {
            $base = $dataOff + ($c - 2) * $spc * $bps
            for ($k = 0; $k -lt ($spc * $bps / 32); $k++) {
                $off = $base + $k * 32
                if ($img[$off] -eq 0) { return ,$out }
                $out += [pscustomobject]@{
                    Name    = [System.Text.Encoding]::ASCII.GetString($img, $off, 11)
                    Attr    = $img[$off + 11]
                    Cluster = [BitConverter]::ToUInt16($img, $off + 26)
                    Size    = [BitConverter]::ToUInt32($img, $off + 28)
                }
            }
        }
        return ,$out
    }

    # Root: EFI, SEED, the label, and the subdirectories. No source may be here.
    $rootRows = @()
    for ($i = 0; $i -lt $rootEnts; $i++) {
        $off = $rootOff + $i * 32
        if ($b[$off] -eq 0) { break }
        $rootRows += [pscustomobject]@{
            Name    = [System.Text.Encoding]::ASCII.GetString($b, $off, 11)
            Attr    = $b[$off + 11]
            Cluster = [BitConverter]::ToUInt16($b, $off + 26)
            Size    = [BitConverter]::ToUInt32($b, $off + 28)
        }
    }
    $subDirs = @($rootRows | Where-Object { $_.Name -match '^SRC\d' })
    if ($subDirs.Count -lt 3) {
        $bad += "expected at least 3 SRC subdirectories from 5 sources at -BucketSize 2, found $($subDirs.Count)"
    }
    foreach ($r in $subDirs) {
        if (($r.Attr -band 0x10) -eq 0) { $bad += "root entry '$($r.Name)' is not marked as a directory" }
    }
    $strayFiles = @($rootRows | Where-Object { ($_.Attr -band 0x18) -eq 0 })
    if ($strayFiles.Count -gt 0) {
        $bad += "the root holds $($strayFiles.Count) file entries under -Bucketed: $((($strayFiles.Name) -join ', '))"
    }

    # Every source, by name, size and bytes, wherever its directory put it.
    $placed = @{}
    foreach ($r in $subDirs) {
        $chain = Get-ClusterChain $b $fatOff ([int]$r.Cluster)
        $rows = Get-DirEntries $b $dataOff ([int]$spc) ([int]$bps) $chain
        if ($rows.Count -lt 2) { $bad += "directory '$($r.Name.Trim())' has no dot entries"; continue }
        if ($rows[0].Name -ne '.          ' -or $rows[1].Name -ne '..         ') {
            $bad += "directory '$($r.Name.Trim())' does not open with '.' and '..'"
        }
        if ($rows[0].Cluster -ne $r.Cluster) { $bad += "'.' in $($r.Name.Trim()) names cluster $($rows[0].Cluster), not $($r.Cluster)" }
        if ($rows[1].Cluster -ne 0) { $bad += "'..' in $($r.Name.Trim()) names cluster $($rows[1].Cluster), not the root" }
        foreach ($row in ($rows | Select-Object -Skip 2)) {
            if (-not $placed.ContainsKey($row.Name)) { $placed[$row.Name] = @() }
            $placed[$row.Name] += [pscustomobject]@{ Dir = $r.Name.Trim(); Row = $row }
        }
    }
    foreach ($p in $dirFiles) {
        $want = [System.IO.File]::ReadAllBytes($p)
        $leaf = [System.IO.Path]::GetFileNameWithoutExtension($p).ToUpperInvariant()
        $ext  = [System.IO.Path]::GetExtension($p).TrimStart('.').ToUpperInvariant()
        $n = ($leaf -replace '[^A-Z0-9_\-]', '_'); if ($n.Length -gt 8) { $n = $n.Substring(0,8) }
        $e = ($ext  -replace '[^A-Z0-9_\-]', '_'); if ($e.Length -gt 3) { $e = $e.Substring(0,3) }
        $name83 = $n.PadRight(8) + $e.PadRight(3)
        if (-not $placed.ContainsKey($name83)) { $bad += "no directory entry named '$name83' under any subdirectory"; continue }
        # The content is what says WHICH of two colliding files an entry is, so
        # each candidate is matched by bytes rather than by name alone.
        $hit = $false
        foreach ($cand in $placed[$name83]) {
            if ($cand.Row.Size -ne $want.Length) { continue }
            $fileOff = $dataOff + ($cand.Row.Cluster - 2) * $spc * $bps
            $got = [byte[]]::new($cand.Row.Size)
            [Array]::Copy($b, $fileOff, $got, 0, $cand.Row.Size)
            if ([Convert]::ToBase64String($got) -eq [Convert]::ToBase64String($want)) { $hit = $true; break }
        }
        if (-not $hit) {
            $bad += "'$name83' is on the image but no copy of it has $p's $($want.Length) bytes"
        }
    }
    # The collision itself: one 8.3 name claimed by two files, which is legal
    # only because the two are in different directories.
    $collided = @($placed.Keys | Where-Object { $placed[$_].Count -gt 1 })
    foreach ($c in $collided) {
        $dirs = @($placed[$c] | ForEach-Object { $_.Dir } | Sort-Object -Unique)
        if ($dirs.Count -ne $placed[$c].Count) {
            $bad += "'$c' appears $($placed[$c].Count) times across only $($dirs.Count) directories"
        }
    }
    if ($collided.Count -lt 1) {
        $bad += 'the two colliding fixtures did not produce one 8.3 name in two directories, so this arm never reached the case it exists for'
    }
}

if ($bad.Count -gt 0) {
    Write-Host ("IMG-FAT16-DIRS: FAIL -- " + ($bad -join '; '))
    $failed++
} else {
    Write-Host "IMG-FAT16-DIRS: PASS ($($dirFiles.Count) sources in subdirectories, dots and chains read off the image, one 8.3 collision separated)"
}

if ($failed -gt 0) { Write-Host "IMG: FAIL"; exit 1 }
Write-Host "IMG: PASS"
exit 0
