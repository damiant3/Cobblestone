# Read-only raw dump of a USB stick to a file. No writes, no mount.
#
# The FAT questions a returned flight answers (HardwareSitting: are both shots
# there, do their chains overlap, are clusters allocated to nothing) must be
# asked of the device rather than of a mounted volume: Windows writes
# System Volume Information and ALLOCATES CLUSTERS the moment it mounts a FAT
# volume, which manufactures the exact evidence question 3 asks about.
#
# Must run elevated (raw \\.\PhysicalDrive access).
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int]$DiskNumber,
    [Parameter(Mandatory)] [string]$Out,
    [long]$Bytes = 16777216,
    [string]$Log = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Log) { Start-Transcript -Path $Log -Force | Out-Null }

$disk = Get-Disk -Number $DiskNumber
if ($disk.BusType -ne 'USB') { throw "Disk $DiskNumber is '$($disk.BusType)', not USB. Refusing to read." }
Write-Host "Source: Disk $DiskNumber  $($disk.FriendlyName)  $([math]::Round($disk.Size/1GB,1)) GB"
Write-Host "Bytes : $Bytes -> $Out"

$path = "\\.\PhysicalDrive$DiskNumber"
$fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
try {
    $outFs = [System.IO.File]::Create($Out)
    try {
        $chunk = 1048576
        $buf = New-Object byte[] $chunk
        $done = 0L
        while ($done -lt $Bytes) {
            $want = [math]::Min($chunk, $Bytes - $done)
            $got = $fs.Read($buf, 0, $want)
            if ($got -le 0) { throw "Short read at offset $done" }
            $outFs.Write($buf, 0, $got)
            $done += $got
        }
        Write-Host "Read $done bytes."
    } finally { $outFs.Close() }
} finally { $fs.Close() }

Write-Host "SHA256: $((Get-FileHash $Out -Algorithm SHA256).Hash)"
Write-Host "DONE."
if ($Log) { Stop-Transcript | Out-Null }
