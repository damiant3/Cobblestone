# Run the Zig plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$OutDir  = Join-Path $PSScriptRoot 'build-output'
$IrFile  = Join-Path $OutDir 'last-run.ir'
$LogFile = Join-Path $OutDir 'run.log'

# -Passes 'text-plug' is what a SOURCE plug must receive: the default pipeline
# inlines, and an inlined call site never reaches the emitter at all. Measured
# on plug-oracle-arith, the default inlines the one call whose arguments are
# both literals and both list-literal helpers, so the emitter is graded on a
# program it is not handed in service (plugs-backlog 1.15).
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\plug-run.ps1') `
    -IrInput $IrFile -Out $Out `
    -PlugCdx (Join-Path $OutDir 'zig-plug.cdx') `
    -MemMB 3072 -Port 9145
exit $LASTEXITCODE