[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' 'codex.build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\javascript-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx - run plugs/javascript/build.ps1 first")
    exit 2
}

$compileScript = Join-Path $Repo 'build\test-compile.ps1'
& $compileScript -Src $Src -Out $IrFile -Log $LogFile -Ir
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR emit step exited $LASTEXITCODE; see $LogFile")
    exit 3
}
Write-Host "[javascript-run] IR: $((Get-Item $IrFile).Length) bytes"

$run = Start-QemuRun -Kernel $PlugCdx -ConnectTimeoutSec 60 -MemMB 4096
if (-not $run) {
    [Console]::Error.WriteLine("FAIL: QEMU did not listen after 4 attempts")
    exit 4
}

try {
    $conn = $run.Conn
    if (-not (Read-QemuReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("READY not received within 30s")
        exit 4
    }
    $stream = $conn.Data.GetStream()

    $irBytes = [System.IO.File]::ReadAllBytes($IrFile)
    $stream.Write($irBytes, 0, $irBytes.Length)
    $stream.WriteByte(4)
    $stream.Flush()

    $lines = [System.Collections.Generic.List[string]]::new()
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 60
        if ($null -eq $line) { break }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            [Console]::Error.WriteLine("FAIL: $line")
            exit 5
        }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line"); continue }
        if ($line.StartsWith('HEAP:')) { break }
        $lines.Add($line)
    }
    $body = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($Out, $body, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[javascript-run] OK: $Out ($($body.Length) bytes)"
    exit 0
} finally {
    if ($run) {
        Close-Qemu -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
