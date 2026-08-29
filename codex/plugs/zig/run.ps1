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

# The directory this writes its IR and its log into. Nothing else created it,
# so in a fresh checkout compile.ps1 was asked to write a log into a directory
# that does not exist and the failure surfaced as "IR compile failed; see
# <that log>" -- naming a file it had just been unable to create.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# compile.ps1 wants the SELF-HOSTED kernel that build.ps1 produces, and refuses
# with "run build.ps1 first, or pass -Kernel seed\Codex.cdx" when it is absent.
# Take it at its word: use the self-hosted kernel when there is one and fall
# back to the seed when there is not, rather than requiring a full build.ps1
# before the plug can be run at all. The plug's output does not depend on which
# compiler produced the IR it is handed.
$Kernel     = Join-Path $Repo 'build-output' 'bare-metal' 'Codex.cdx'
$KernelArgs = @()
if (-not (Test-Path $Kernel)) {
    $KernelArgs = @('-Kernel', (Join-Path $Repo 'seed' 'Codex.cdx'))
}

# -Passes 'text-plug' is what a SOURCE plug must receive: the default pipeline
# inlines, and an inlined call site never reaches the emitter at all. Measured
# on plug-oracle-arith, the default inlines the one call whose arguments are
# both literals and both list-literal helpers, so the emitter is graded on a
# program it is not handed in service (plugs-backlog 1.15).
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' @KernelArgs 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\plug-run.ps1') `
    -IrInput $IrFile -Out $Out `
    -PlugCdx (Join-Path $OutDir 'zig-plug.cdx') `
    -MemMB 3072 -Port 9145
exit $LASTEXITCODE