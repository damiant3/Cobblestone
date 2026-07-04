# test-renode.ps1 — Run a Codex ELF binary under Renode, capture UART output.
#
# Usage:
#   build/test-renode.ps1 -Arch arm64 -Elf <file.elf> -OutFile <output.txt>
#   build/test-renode.ps1 -Arch riscv64 -Elf <file.elf> -OutFile <output.txt>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('arm64','riscv64')]
    [string]$Arch,

    [Parameter(Mandatory=$true)] [string]$Elf,
    [Parameter(Mandatory=$true)] [string]$OutFile,
    [int]$TimeoutSec = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'renode-config.ps1')
$RenodeExe = Get-RenodeExe -Repo $Repo
if (-not $RenodeExe) {
    [Console]::Error.WriteLine("MISSING: Renode (install to C:\Renode or set CODEX_RENODE_HOME)")
    exit 2
}

$boardRepl = Join-Path $Repo "tools\renode\codex\codex-${Arch}.repl"
if (-not (Test-Path $boardRepl)) {
    [Console]::Error.WriteLine("MISSING: board $boardRepl")
    exit 2
}

$elfPath = (Resolve-Path $Elf).Path -replace '\\','/'
$boardPath = (Resolve-Path $boardRepl).Path -replace '\\','/'
$uartLog = (Join-Path ([System.IO.Path]::GetTempPath()) "renode-uart-$([guid]::NewGuid().ToString('N').Substring(0,8)).log") -replace '\\','/'

$rescContent = "mach create `"codex`"`nmachine LoadPlatformDescription @${boardPath}`nsysbus LoadELF @${elfPath}`nuart0 CreateFileBackend @${uartLog} true`nstart`nsleep ${TimeoutSec}`nquit"
$rescFile = [System.IO.Path]::GetTempFileName() -replace '\\','/'
[System.IO.File]::WriteAllText($rescFile, $rescContent)

$stdoutFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $RenodeExe -ArgumentList @(
    '--disable-xwt', '--console', '-e', "include @$rescFile"
) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError ([System.IO.Path]::GetTempFileName())

$wallTimeout = ($TimeoutSec + 15) * 1000
$proc.WaitForExit($wallTimeout) | Out-Null
if (-not $proc.HasExited) {
    try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
}

Start-Sleep -Milliseconds 500

if (Test-Path $uartLog) {
    $content = [System.IO.File]::ReadAllText($uartLog)
    $lines = ($content -split "`n" | Select-Object -First 1) -replace "`r",""
    [System.IO.File]::WriteAllText($OutFile, $lines + "`n")
    Write-Host "[renode] ${Arch}: $($lines.Length) chars — $lines"
    Remove-Item $uartLog -Force -ErrorAction SilentlyContinue
} else {
    [System.IO.File]::WriteAllText($OutFile, '')
    Write-Host "[renode] ${Arch}: no UART output"
}

Remove-Item $rescFile, $stdoutFile -Force -ErrorAction SilentlyContinue
exit 0
