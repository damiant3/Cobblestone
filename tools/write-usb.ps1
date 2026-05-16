# Write a raw disk image to a USB drive.
# Zeros the disk head, then writes the image.
# Usage: write-usb.ps1 -Image path.img -DiskNumber 3
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Image,
    [Parameter(Mandatory)] [int]$DiskNumber
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Leaf $Image)) { throw "Image not found: $Image" }

$disk = Get-Disk -Number $DiskNumber
if ($disk.BusType -ne 'USB') { throw "Disk $DiskNumber is not USB (BusType=$($disk.BusType)). Refusing." }

$imgBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Image).Path)
Write-Host "Image: $($imgBytes.Length) bytes -> Disk $DiskNumber ($($disk.FriendlyName), $([math]::Round($disk.Size/1MB)) MB)"

if ($imgBytes.Length -gt $disk.Size) { throw "Image ($($imgBytes.Length)) exceeds disk size ($($disk.Size))" }

Write-Host "  Clearing disk..."
Clear-Disk -Number $DiskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$diskPath = "\\.\PhysicalDrive$DiskNumber"
$fs = [System.IO.FileStream]::new($diskPath, 'Open', 'ReadWrite', 'ReadWrite', 512, 'WriteThrough')

try {
    Write-Host "  Zeroing first $($imgBytes.Length) bytes..."
    $zeros = New-Object byte[] 1048576
    for ($off = 0; $off -lt $imgBytes.Length; $off += $zeros.Length) {
        $len = [math]::Min($zeros.Length, $imgBytes.Length - $off)
        $fs.Write($zeros, 0, $len)
    }
    $fs.Flush()
    $fs.Seek(0, 'Begin') | Out-Null

    Write-Host "  Writing $($imgBytes.Length) bytes..."
    $chunkSize = 1048576
    for ($off = 0; $off -lt $imgBytes.Length; $off += $chunkSize) {
        $len = [math]::Min($chunkSize, $imgBytes.Length - $off)
        $fs.Write($imgBytes, $off, $len)
    }
    $fs.Flush()

    Write-Host "  Verifying..."
    $fs.Seek(0, 'Begin') | Out-Null
    $checkLen = [math]::Min(4096, $imgBytes.Length)
    $verify = New-Object byte[] $checkLen
    $fs.Read($verify, 0, $checkLen) | Out-Null
    for ($i = 0; $i -lt $checkLen; $i++) {
        if ($verify[$i] -ne $imgBytes[$i]) { throw "Verification FAILED at byte $i (wrote $($imgBytes[$i]), read $($verify[$i]))" }
    }

    Write-Host "Done: $($imgBytes.Length) bytes written and verified on $diskPath"
} finally {
    $fs.Close()
}
