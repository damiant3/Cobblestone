# Run the IMG plug: send PE + CDX bytes and receive a GPT disk image.
#
# Usage:
#   plugs/img/run.ps1 -PeInput <file.efi> -CdxInput <file.cdx> -Out <file.img> [-Fat16] [-Source <file>]
#
# Default is FAT32. Pass -Fat16 for FAT16 with optional source embedding.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PeInput,
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [switch]$Fat16,
    [string]$Source = '',
    [int]$TotalSectors = 16384
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\img-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/img/build.ps1 first")
    exit 2
}
foreach ($f in @($PeInput, $CdxInput)) {
    if (-not (Test-Path -PathType Leaf $f)) { [Console]::Error.WriteLine("MISSING: $f"); exit 2 }
}

$peBytes = [System.IO.File]::ReadAllBytes($PeInput)
$cdxBytes = [System.IO.File]::ReadAllBytes($CdxInput)
if ($Source -and (Test-Path -PathType Leaf $Source)) {
    [byte[]]$srcBytes = [System.IO.File]::ReadAllBytes($Source)
} else {
    [byte[]]$srcBytes = [byte[]]::new(0)
}

# Build payload: [fs-type(1)] [total-sectors(4)] [pe-size(4)] [cdx-size(4)] [src-size(4)] [pe][cdx][src]
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
$bw.Write([byte]$(if ($Fat16) { 1 } else { 0 }))
$bw.Write([int]$TotalSectors)
$bw.Write([int]$peBytes.Length)
$bw.Write([int]$cdxBytes.Length)
$bw.Write([int]$srcBytes.Length)
$bw.Write($peBytes)
$bw.Write($cdxBytes)
if ($srcBytes.Length -gt 0) { $bw.Write($srcBytes) }
$bw.Flush()
$inputBytes = $ms.ToArray()

Write-Host "[img-run] PE=$($peBytes.Length) CDX=$($cdxBytes.Length) src=$($srcBytes.Length) sectors=$TotalSectors fs=$(if ($Fat16) {'FAT16'} else {'FAT32'})"

# -- Start TCP listener ----------------------------------------------
$plugPort = 9118
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()

# -- Boot plug CDX ---------------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
$deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 30s")
            exit 5
        }
        Start-Sleep -Milliseconds 50
    }
    $tcpClient = $listener.AcceptTcpClient()
    $tcpStream = $tcpClient.GetStream()
    $listener.Stop()
    Write-Host "[img-run] Plug connected"

    # -- Send payload as framed message (tag=5) ----------------------
    $msgLen = $inputBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(5)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $inputBytes.Length) {
        $n = [Math]::Min($chunkSize, $inputBytes.Length - $off)
        $tcpStream.Write($inputBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $inputBytes.Length) { Start-Sleep -Milliseconds 50 }
    }
    Write-Host "[img-run] Sent $($inputBytes.Length) bytes (tag=5)"

    # -- Receive IMG output ------------------------------------------
    $tcpStream.ReadTimeout = 600000
    $allBytes = [System.Collections.Generic.List[byte]]::new(1048576)
    $readBuf = [byte[]]::new(65536)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    Write-Host "[img-run] OK: $Out ($($allBytes.Count) bytes)"

    $tcpClient.Close()

} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
