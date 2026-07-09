# Flash a raw disk image to a USB stick -- reliably.
#
# This is the flasher docs/UsersHandbook.md refers to. Unlike tools/write-usb.ps1
# it does NOT call Clear-Disk (which races Windows disk management against the
# raw write), it forces a real device sync with FlushFileBuffers via
# Flush($true), and it verifies the ENTIRE image after writing -- not just the
# first 4 KB. These three things are the documented causes of "same image,
# same stick, boots sometimes" non-determinism.
#
# Must run elevated (raw \\.\PhysicalDrive access). Find the disk number with:
#   Get-Disk | Where-Object BusType -eq 'USB'
#
# Usage (from an elevated pwsh):
#   build/flash-usb.ps1 -Image build/boot/optiona.img -DiskNumber N
# Or launch elevated in one shot:
#   Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile','-File',
#     'D:\Projects\NewRepository-fester\build\flash-usb.ps1','-Image',
#     'D:\Projects\NewRepository-fester\build\boot\optiona.img','-DiskNumber','N'
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Image,
    [Parameter(Mandatory)] [int]$DiskNumber,
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Leaf $Image)) { throw "Image not found: $Image" }
$imgPath = (Resolve-Path $Image).Path

# --- Safety checks ---
$disk = Get-Disk -Number $DiskNumber
if ($disk.BusType -ne 'USB') {
    throw "Disk $DiskNumber is '$($disk.BusType)', not USB (FriendlyName='$($disk.FriendlyName)'). Refusing to write."
}
$imgBytes = [System.IO.File]::ReadAllBytes($imgPath)
Write-Host "Image : $imgPath"
Write-Host "Size  : $($imgBytes.Length) bytes ($([math]::Round($imgBytes.Length/1MB,2)) MB)"
Write-Host "Target: Disk $DiskNumber  $($disk.FriendlyName)  $([math]::Round($disk.Size/1GB,1)) GB"
if ($imgBytes.Length -gt $disk.Size) { throw "Image ($($imgBytes.Length)) exceeds disk ($($disk.Size))" }

if (-not $Force) {
    $ans = Read-Host "This ERASES disk $DiskNumber ($($disk.FriendlyName)). Type YES to proceed"
    if ($ans -ne 'YES') { Write-Host "Aborted."; return }
}

# Take the disk offline so Windows does not fight the raw write (no Clear-Disk).
try { Set-Disk -Number $DiskNumber -IsOffline $true -ErrorAction Stop } catch { Write-Host "  (could not offline disk: $_)" }
Start-Sleep -Milliseconds 300

$diskPath = "\\.\PhysicalDrive$DiskNumber"
# FileShare.ReadWrite avoids "access denied" on some configurations; WriteThrough
# plus an explicit Flush($true) forces the bytes to the device, not just the OS cache.
$fs = [System.IO.FileStream]::new($diskPath, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite, 1048576,
    [System.IO.FileOptions]::WriteThrough)
try {
    Write-Host "Writing $($imgBytes.Length) bytes..."
    $chunk = 1048576
    for ($off = 0; $off -lt $imgBytes.Length; $off += $chunk) {
        $len = [math]::Min($chunk, $imgBytes.Length - $off)
        $fs.Write($imgBytes, $off, $len)
        if (($off / $chunk) % 8 -eq 0) { Write-Host ("  {0:P0}" -f ($off / $imgBytes.Length)) -NoNewline; Write-Host "`r" -NoNewline }
    }
    $fs.Flush($true)   # FlushFileBuffers -> sync to the physical device
    Write-Host "  100% written, synced.            "

    Write-Host "Verifying full image..."
    $fs.Seek(0, 'Begin') | Out-Null
    $rbuf = New-Object byte[] $chunk
    $bad = -1
    for ($off = 0; $off -lt $imgBytes.Length -and $bad -lt 0; $off += $chunk) {
        $len = [math]::Min($chunk, $imgBytes.Length - $off)
        $got = 0
        while ($got -lt $len) {
            $n = $fs.Read($rbuf, $got, $len - $got)
            if ($n -le 0) { break }
            $got += $n
        }
        for ($i = 0; $i -lt $len; $i++) {
            if ($rbuf[$i] -ne $imgBytes[$off + $i]) { $bad = $off + $i; break }
        }
    }
    if ($bad -ge 0) { throw "VERIFY FAILED at byte $bad (wrote $($imgBytes[$bad]), read back $($rbuf[$bad - $off]))" }
    Write-Host "Verified: all $($imgBytes.Length) bytes match."
}
finally {
    $fs.Close()
    try { Set-Disk -Number $DiskNumber -IsOffline $false -ErrorAction SilentlyContinue } catch {}
}
Write-Host "DONE. Eject the stick, then boot the target from USB (UEFI, CSM/Legacy off)."
