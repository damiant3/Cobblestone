param(
    [string]$OutFile = "fonts/font-disk.img",
    [string]$FontDir = "fonts/cc0",
    [int]$SizeMB = 8
)

$ErrorActionPreference = "Stop"

$fontFiles = @(
    @{ Src = "cmunrm.ttf";                          Fat = "CMUNRM  TTF" }
    @{ Src = "cmunbx.ttf";                          Fat = "CMUNBX  TTF" }
    @{ Src = "cmunti.ttf";                          Fat = "CMUNTI  TTF" }
    @{ Src = "cmuntb.ttf";                          Fat = "CMUNTB  TTF" }
    @{ Src = "LiberStructRegular-5yDOB.ttf";        Fat = "LIBERSTRTTF" }
    @{ Src = "cmunss.ttf";                          Fat = "CMUNSS  TTF" }
    @{ Src = "cmunsx.ttf";                          Fat = "CMUNSX  TTF" }
    @{ Src = "HomeVideo-BLG6G.ttf";                 Fat = "HOMEVID TTF" }
    @{ Src = "HomeVideoBold-R90Dv.ttf";             Fat = "HOMEBOLDTTF" }
    @{ Src = "SecolineRegular-aYmdx.ttf";           Fat = "SECOLINETTF" }
    @{ Src = "cmuntt.ttf";                          Fat = "CMUNTT  TTF" }
    @{ Src = "PublicPixel-rv0pA.ttf";               Fat = "PUBPIXELTTF" }
    @{ Src = "Unitblock-JpJma.ttf";                 Fat = "UNITBLK TTF" }
)

$totalSize = $SizeMB * 1024 * 1024
$bytesPerSector = 512
$sectorsPerCluster = 8
$bytesPerCluster = $bytesPerSector * $sectorsPerCluster
$totalSectors = $totalSize / $bytesPerSector
$reservedSectors = 32
$numFats = 2
$totalClusters = ($totalSectors - $reservedSectors) / $sectorsPerCluster
$fatEntries = $totalClusters + 2
$fatSectors = [math]::Ceiling($fatEntries * 4 / $bytesPerSector)
$dataSectorStart = $reservedSectors + $numFats * $fatSectors
$rootCluster = 2

$img = [byte[]]::new($totalSize)

function Write-U8($off, $val) { $img[$off] = [byte]($val -band 0xFF) }
function Write-U16($off, $val) {
    $img[$off]   = [byte]($val -band 0xFF)
    $img[$off+1] = [byte](($val -shr 8) -band 0xFF)
}
function Write-U32($off, $val) {
    $img[$off]   = [byte]($val -band 0xFF)
    $img[$off+1] = [byte](($val -shr 8) -band 0xFF)
    $img[$off+2] = [byte](($val -shr 16) -band 0xFF)
    $img[$off+3] = [byte](($val -shr 24) -band 0xFF)
}

$img[0] = 0xEB; $img[1] = 0x58; $img[2] = 0x90
[System.Text.Encoding]::ASCII.GetBytes("MSDOS5.0").CopyTo($img, 3)
Write-U16 11 $bytesPerSector
Write-U8  13 $sectorsPerCluster
Write-U16 14 $reservedSectors
Write-U8  16 $numFats
Write-U16 17 0
Write-U16 19 0
Write-U8  21 0xF8
Write-U16 22 0
Write-U16 24 63
Write-U16 26 255
Write-U32 28 0
Write-U32 32 $totalSectors
Write-U32 36 $fatSectors
Write-U16 40 0
Write-U16 42 0
Write-U32 44 $rootCluster
Write-U16 48 1
Write-U16 50 6
Write-U16 52 0
Write-U8  66 0x29
Write-U32 67 0x12345678
[System.Text.Encoding]::ASCII.GetBytes("CODEX FONTS").CopyTo($img, 71)
[System.Text.Encoding]::ASCII.GetBytes("FAT32   ").CopyTo($img, 82)
$img[510] = 0x55; $img[511] = 0xAA

function ClusterToSector($c) { return $dataSectorStart + ($c - 2) * $sectorsPerCluster }
function ClusterToOffset($c) { return (ClusterToSector $c) * $bytesPerSector }

function WriteFatEntry($cluster, $value) {
    $fatOff = $reservedSectors * $bytesPerSector + $cluster * 4
    Write-U32 $fatOff $value
    $fat2Off = ($reservedSectors + $fatSectors) * $bytesPerSector + $cluster * 4
    Write-U32 $fat2Off $value
}

WriteFatEntry 0 0x0FFFFFF8
WriteFatEntry 1 0x0FFFFFFF
WriteFatEntry $rootCluster 0x0FFFFFFF

$rootOff = ClusterToOffset $rootCluster
[System.Text.Encoding]::ASCII.GetBytes("CODEX FONTS").CopyTo($img, $rootOff)
$img[$rootOff + 11] = 0x08

$nextCluster = 3
$dirEntryOff = $rootOff + 32

foreach ($f in $fontFiles) {
    $srcPath = Join-Path $FontDir $f.Src
    if (-not (Test-Path $srcPath)) {
        $subDirs = Get-ChildItem -Path $FontDir -Recurse -Filter $f.Src
        if ($subDirs.Count -gt 0) { $srcPath = $subDirs[0].FullName }
        else { Write-Warning "Font not found: $($f.Src)"; continue }
    }
    $data = [System.IO.File]::ReadAllBytes($srcPath)
    $clustersNeeded = [math]::Ceiling($data.Length / $bytesPerCluster)
    $startCluster = $nextCluster

    for ($i = 0; $i -lt $clustersNeeded; $i++) {
        $c = $nextCluster + $i
        if ($i -lt $clustersNeeded - 1) {
            WriteFatEntry $c ($c + 1)
        } else {
            WriteFatEntry $c 0x0FFFFFFF
        }
        $cOff = ClusterToOffset $c
        $srcOff = $i * $bytesPerCluster
        $len = [math]::Min($bytesPerCluster, $data.Length - $srcOff)
        [Array]::Copy($data, $srcOff, $img, $cOff, $len)
    }
    $nextCluster += $clustersNeeded

    $fatName = $f.Fat
    [System.Text.Encoding]::ASCII.GetBytes($fatName).CopyTo($img, $dirEntryOff)
    $img[$dirEntryOff + 11] = 0x20
    Write-U16 ($dirEntryOff + 20) (($startCluster -shr 16) -band 0xFFFF)
    Write-U16 ($dirEntryOff + 26) ($startCluster -band 0xFFFF)
    Write-U32 ($dirEntryOff + 28) $data.Length
    $dirEntryOff += 32
}

[System.IO.File]::WriteAllBytes($OutFile, $img)
$hash = (Get-FileHash -Algorithm SHA256 $OutFile).Hash.Substring(0, 16)
Write-Host "Created $OutFile ($SizeMB MB, $($fontFiles.Count) fonts, SHA256=$hash)"
