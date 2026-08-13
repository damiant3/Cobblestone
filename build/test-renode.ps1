# test-renode.ps1 -- Run a Codex ELF binary under Renode, capture UART output
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('arm64','riscv64')]
    [string]$Arch,
    [Parameter(Mandatory=$true)]
    [string]$Elf,
    [Parameter(Mandatory=$true)]
    [string]$OutFile,
    [int]$TimeoutSec = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'renode-config.ps1')
$RenodeExe = (Get-RenodeExe -Repo $Repo)
if ((-not $RenodeExe)) {
    [Console]::Error.WriteLine('MISSING: Renode (install to C:\Renode or set CODEX_RENODE_HOME)')
    exit 2
}

$boardRepl = (Join-Path $Repo ([string]([string]'tools\renode\codex\codex-' + $Arch) + '.repl'))
if ((-not (Test-Path -PathType Leaf $boardRepl))) {
    [Console]::Error.WriteLine(([string]'MISSING: board ' + $boardRepl))
    exit 2
}


$elfPath = ((Resolve-Path $Elf).Path -replace '\\', '/')
$boardPath = ((Resolve-Path $boardRepl).Path -replace '\\', '/')
$uartLog = ((Join-Path ([System.IO.Path]::GetTempPath()) ([string]([string]'renode-uart-' + ([guid]::NewGuid()).ToString('N').Substring(0, 8)) + '.log')) -replace '\\', '/')


$rescContent = (@('mach create "codex"', ([string]'machine LoadPlatformDescription @' + $boardPath), ([string]'sysbus LoadELF @' + $elfPath), ([string]([string]'uart0 CreateFileBackend @' + $uartLog) + ' true'), 'start', ([string]'sleep ' + $TimeoutSec), 'quit') -join "`n")
$rescFile = (([System.IO.Path]::GetTempFileName()) -replace '\\', '/')
[System.IO.File]::WriteAllText($rescFile, $rescContent)


$stdoutFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $RenodeExe -ArgumentList @('--disable-xwt', '--console', '-e', ([string]'include @' + $rescFile)) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError ([System.IO.Path]::GetTempFileName())

$wallTimeout = (($TimeoutSec + 15) * 1000)
$proc.WaitForExit($wallTimeout) | Out-Null
if ((-not $proc.HasExited)) {
    try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
}

Start-Sleep -Milliseconds 500


if ((Test-Path -PathType Leaf $uartLog)) {
    $content = [System.IO.File]::ReadAllText($uartLog)
    $lines = ((($content -split '\n') | Select-Object -First 1) -replace '\r', '')
    [System.IO.File]::WriteAllText($OutFile, ([string]$lines + "`n"))
    Write-Host ([string]([string]([string]([string]'[renode] ' + $Arch) + ': ') + ([string]$lines.Length + ' chars -- ')) + $lines)
    Remove-Item -Force -ErrorAction SilentlyContinue $uartLog
} else {
    [System.IO.File]::WriteAllText($OutFile, '')
    Write-Host ([string]([string]'[renode] ' + $Arch) + ': no UART output')
}

Remove-Item -Force -ErrorAction SilentlyContinue $rescFile
Remove-Item -Force -ErrorAction SilentlyContinue $stdoutFile
exit 0
