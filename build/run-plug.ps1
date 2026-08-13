# run-plug.ps1 -- Boot a plug CDX in a VM with NE2K NIC and exchange data over TCP
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Plug,
    [Parameter(Mandatory=$true)]
    [string]$InFile,
    [Parameter(Mandatory=$true)]
    [string]$Output,
    [int]$MemMB = 4096,
    [int]$TimeoutSec = 120,
    [int]$Port = 0,
    [ValidateRange(0, 255)]
    [int]$Tag = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'plug-ports.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ($Port -eq 0) {
    if ($Plug -match '[\\/]plugs[\\/]([^\\/]+)[\\/]') {
        $Port = Get-PlugPort $Matches[1]
    } else {
        throw "run-plug: cannot tell which plug '$Plug' is, so cannot derive its port. Pass -Port, or run it from codex/plugs/<name>/."
    }
}
Assert-PlugPortFree -Port $Port -Plug $Plug


if ((-not (Test-Path -PathType Leaf $Plug))) {
    [Console]::Error.WriteLine("MISSING plug: $Plug")
    exit 2
}
if ((-not (Test-Path -PathType Leaf $InFile))) {
    [Console]::Error.WriteLine("MISSING input: $InFile")
    exit 2
}


$inputBytes = [System.IO.File]::ReadAllBytes($InFile)
Write-Host "[plug] Input: $($inputBytes.Length) bytes"
Write-Host "[plug] Plug: $Plug"

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "[plug] Listening on TCP $Port"


try {
    $vmBin = Join-Path $Repo 'tools\codex-vm.exe'
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $vmArgs = @('-kernel', $Plug, '-mem', "$MemMB", '-headless')
    $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    Write-Host "[plug] VM PID $($proc.Id)"


    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    :wait_conn while ((-not $listener.Pending())) {
        if ($proc.HasExited) {
            $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { '' }
            [Console]::Error.WriteLine("FAIL: VM exited before connecting. stderr: $stderr")
            exit 4
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 60s")
            exit 5
        }
        Start-Sleep -Milliseconds 100
    }
    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $listener.Stop()
    Write-Host '[plug] Connected'


    $frame = [System.Collections.Generic.List[byte]]::new()
    $flen = 1 + $inputBytes.Length
    $frame.Add([byte]($flen -band 0xFF))
    $frame.Add([byte](($flen -shr 8) -band 0xFF))
    $frame.Add([byte](($flen -shr 16) -band 0xFF))
    $frame.Add([byte](($flen -shr 24) -band 0xFF))
    $frame.Add([byte]$Tag)
    $frame.AddRange($inputBytes)
    $frameBytes = $frame.ToArray()
    $stream.Write($frameBytes, 0, $frameBytes.Length)
    $stream.Flush()
    Write-Host "[plug] Sent one frame of $($frameBytes.Length) bytes (tag $Tag), waiting for the reply..."


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
    } catch [System.IO.IOException] {
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

    $raw = $allBytes.ToArray()
    $payload = $raw
    if ($raw.Length -ge 5) {
        $bodyLen = ([int]$raw[0]) -bor (([int]$raw[1]) -shl 8) -bor (([int]$raw[2]) -shl 16) -bor (([int]$raw[3]) -shl 24)
        if ($bodyLen -eq ($raw.Length - 4)) {
            $payload = [byte[]]::new($raw.Length - 5)
            [Array]::Copy($raw, 5, $payload, 0, $payload.Length)
            Write-Host "[plug] Reply was framed (tag $($raw[4]))"
        }
    }


    [System.IO.File]::WriteAllBytes($Output, $payload)
    Write-Host "[plug] OK: $Output ($($payload.Length) bytes)"

} finally {
    if (($proc -and (-not $proc.HasExited))) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    try { $listener.Stop() } catch {}
}
