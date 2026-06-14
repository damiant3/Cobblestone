# Run the ARM64 codegen plug: send IR text, receive machine code + metadata.
#
# Usage:
#   plugs/arm64/run.ps1 -IrInput <file.ir> -Out <file.bin>
#
# The output is the binary wire protocol (same as x86 output):
#   [4B code-len] [4B data-len] [4B func-count]
#   [code bytes] [data bytes]
#   [func entries: 2B name-len + name + 4B offset each]
#
# Chain with the ELF plug to produce a runnable binary:
#   plugs/arm64/run.ps1 -IrInput hello.ir -Out hello.x86out
#   plugs/elf/run.ps1 -X86Input hello.x86out -Out hello.elf
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$IrInput,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\arm64-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/arm64/build.ps1 first")
    exit 2
}
if (-not (Test-Path -PathType Leaf $IrInput)) {
    [Console]::Error.WriteLine("MISSING: $IrInput")
    exit 2
}

$inputBytes = [System.IO.File]::ReadAllBytes($IrInput)
Write-Host "[arm64-run] Input: $($inputBytes.Length) bytes from $IrInput"

$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[arm64-run] Listening on port $plugPort"

$stderrFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PlugCdx, '-mem', '4096', '-headless') `
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
    Write-Host "[arm64-run] Plug connected"

    $msgLen = $inputBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $inputBytes.Length) {
        $n = [Math]::Min($chunkSize, $inputBytes.Length - $off)
        $tcpStream.Write($inputBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $inputBytes.Length) { Start-Sleep -Milliseconds 50 }
    }
    Write-Host "[arm64-run] Sent $($inputBytes.Length) bytes (IR text)"

    $tcpStream.ReadTimeout = 600000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    Write-Host "[arm64-run] OK: $Out ($($allBytes.Count) bytes)"

    $tcpClient.Close()
} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}
