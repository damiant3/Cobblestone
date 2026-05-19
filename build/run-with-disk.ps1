# Run a bare-metal CDX/ELF kernel with an IDE disk image attached.
# Usage: run-with-disk.ps1 -Kernel <kernel.cdx> -Disk <image.img> [-OutFile <output.actual>]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Kernel,
    [Parameter(Mandatory=$true)] [string]$Disk,
    [string]$OutFile,
    [int]$TimeoutSec = 30,
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$run = Start-QemuRun -Kernel $Kernel -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore -ExtraArgs @('-drive', "file=$Disk,format=raw,if=ide,index=0")
if (-not $run) { Write-Host 'FAIL: VM did not start with disk'; exit 1 }

try {
    if (-not (Read-QemuReady -Conn $run.Conn -TimeoutSec 30)) {
        Write-Host 'FAIL: READY not received'; exit 1
    }
    $stream = $run.Conn.Data.GetStream()
    $lines = [System.Collections.Generic.List[string]]::new()
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $remaining = [int][math]::Max(1, ($deadline - (Get-Date)).TotalSeconds)
        $line = Read-StreamLine -Stream $stream -TimeoutSec $remaining
        if ($null -eq $line) { break }
        if ($line.StartsWith('HEAP:') -or $line.StartsWith('STACK:')) { break }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
        else { $lines.Add($line) }
    }
    $text = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
    if ($OutFile) {
        [System.IO.File]::WriteAllText($OutFile, $text, [System.Text.UTF8Encoding]::new($false))
    } else {
        [Console]::Out.Write($text)
    }
    exit 0
} finally {
    Close-Qemu -Conn $run.Conn -Process $run.Process
    Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
}
