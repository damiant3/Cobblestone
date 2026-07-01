# Build a disk image containing C64 ROM files for the emulator.
#
# The emulator reads ROMs from the IDE disk at boot:
#   Sectors 0-15:   BASIC ROM    (8192 bytes)
#   Sectors 16-31:  KERNAL ROM   (8192 bytes)
#   Sectors 32-39:  CHARGEN ROM  (4096 bytes)
#
# ROM files can be sourced from VICE (vice/C64/) or any C64 archive.
# Standard filenames: basic.901226-01.bin, kernal.901227-03.bin, chargen.901225-01.bin
#
# Usage:
#   pwsh apps/c64/build-rom-disk.ps1 -BasicRom <path> -KernalRom <path> -ChargenRom <path>
#   pwsh apps/c64/build-rom-disk.ps1 -ViceDir "C:\tools\vice\C64"
param(
    [string]$BasicRom,
    [string]$KernalRom,
    [string]$ChargenRom,
    [string]$ViceDir,
    [string]$Out = "apps/c64/c64-roms.disk"
)
$ErrorActionPreference = 'Stop'

if ($ViceDir) {
    if (-not $BasicRom)   { $BasicRom   = Join-Path $ViceDir "basic" }
    if (-not $KernalRom)  { $KernalRom  = Join-Path $ViceDir "kernal" }
    if (-not $ChargenRom) { $ChargenRom = Join-Path $ViceDir "chargen" }
}

foreach ($p in @(@{N='BASIC';P=$BasicRom;S=8192}, @{N='KERNAL';P=$KernalRom;S=8192}, @{N='CHARGEN';P=$ChargenRom;S=4096})) {
    if (-not $p.P -or -not (Test-Path $p.P)) {
        Write-Error "Missing $($p.N) ROM: $($p.P). Provide -ViceDir or individual -BasicRom/-KernalRom/-ChargenRom paths."
        exit 1
    }
    $sz = (Get-Item $p.P).Length
    if ($sz -ne $p.S) { Write-Warning "$($p.N) ROM is $sz bytes (expected $($p.S))" }
}

$diskSize = 512 * 40  # 40 sectors = 20 KB
$disk = [byte[]]::new($diskSize)

$basic = [System.IO.File]::ReadAllBytes($BasicRom)
$kernal = [System.IO.File]::ReadAllBytes($KernalRom)
$chargen = [System.IO.File]::ReadAllBytes($ChargenRom)

[Array]::Copy($basic,   0, $disk, 0,             [Math]::Min($basic.Length, 8192))
[Array]::Copy($kernal,  0, $disk, 8192,           [Math]::Min($kernal.Length, 8192))
[Array]::Copy($chargen, 0, $disk, 16384,           [Math]::Min($chargen.Length, 4096))

[System.IO.File]::WriteAllBytes($Out, $disk)
Write-Host "ROM disk image: $Out ($($disk.Length) bytes)"
Write-Host "  BASIC:   $($basic.Length) bytes at sector 0"
Write-Host "  KERNAL:  $($kernal.Length) bytes at sector 16"
Write-Host "  CHARGEN: $($chargen.Length) bytes at sector 32"
