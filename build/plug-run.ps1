# plug-run.ps1 -- Boot a plug CDX in a VM and send pre-compiled IR over TCP to produce output
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$IrInput,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [string]$PlugCdx = '',
    [int]$MemMB = 2048,
    [int]$TimeoutSec = 120,
    [int]$Port = 9100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ((-not (Test-Path -PathType Leaf $IrInput))) {
    [Console]::Error.WriteLine("MISSING IR input: $IrInput")
    exit 2
}
if ((-not $PlugCdx)) {
    $PlugCdx = Join-Path $PSScriptRoot 'build-output\plug.cdx'
}
if ((-not (Test-Path -PathType Leaf $PlugCdx))) {
    [Console]::Error.WriteLine("MISSING plug CDX: $PlugCdx")
    exit 2
}


$irBytes = [System.IO.File]::ReadAllBytes($IrInput)
Write-Host "[plug-run] IR input: $($irBytes.Length) bytes"
Write-Host "[plug-run] Plug: $PlugCdx"


try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    Write-Host "[plug-run] Listening on TCP $Port"


    $vmBin = Join-Path $Repo 'tools\codex-vm.exe'
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $vmArgs = @('-kernel', $PlugCdx, '-mem', "$MemMB", '-headless')
    $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    Write-Host "[plug-run] VM PID $($proc.Id)"


    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    :wait_conn while ((-not $listener.Pending())) {
        if ($proc.HasExited) {
            $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { '' }
            [Console]::Error.WriteLine("FAIL: VM exited before connecting. stderr: $stderr")
            exit 4
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine('FAIL: plug did not connect within 60s')
            exit 5
        }
        Start-Sleep -Milliseconds 100
    }
    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $listener.Stop()
    Write-Host '[plug-run] Connected'


    $msgLen = $irBytes.Length + 1
    $hdr = [BitConverter]::GetBytes([int]$msgLen)
    $stream.Write($hdr, 0, 4)
    $stream.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    :send_loop while ($off -lt $irBytes.Length) {
        $n = [Math]::Min($chunkSize, $irBytes.Length - $off)
        $stream.Write($irBytes, $off, $n)
        $stream.Flush()
        $off += $n
        if ($off -lt $irBytes.Length) {
            Start-Sleep -Milliseconds 20
        }
    }
    Write-Host "[plug-run] Sent $($irBytes.Length) bytes"


    $stream.ReadTimeout = $TimeoutSec * 1000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        :recv_loop while ($true) {
            $n = $stream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) {
                break
            }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {
        # Read timeout or connection reset -- normal end
    }
    $client.Close()


    if ($allBytes.Count -eq 0) {
        if ((-not $proc.HasExited)) {
            $proc.WaitForExit(5000)
        }
        $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { '' }
        [Console]::Error.WriteLine("FAIL: plug produced no output. stderr: $stderr")
        exit 6
    }
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    $truncHit = @()
    if (Test-Path $stderrFile) { $truncHit = @(Select-String -Path $stderrFile -Pattern 'TRUNCATED sent=') }
    if ($truncHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug could not send its whole output -- $($truncHit[0].Line.Trim())")
        exit 7
    }
    Write-Host "[plug-run] OK: $Out ($($allBytes.Count) bytes)"

} finally {
    if (($proc -and (-not $proc.HasExited))) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    try { $listener.Stop() } catch {}
}
