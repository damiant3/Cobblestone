# Run the Zig plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [int]$MemMB = 3072
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$OutDir  = Join-Path $PSScriptRoot 'build-output'
# Scratch is keyed to the run so two concurrent runs cannot cross
# their IR and their log (plugs 2.26).
$RunTag  = if ($Out) { [System.IO.Path]::GetFileNameWithoutExtension($Out) } else { $PID }
$IrFile  = Join-Path $OutDir "last-run-$RunTag.ir"
$LogFile = Join-Path $OutDir "run-$RunTag.log"

# Nothing else creates this, so in a fresh checkout compile.ps1 was asked to
# write a log into a directory that does not exist, and the failure surfaced as
# "IR compile failed; see <that log>" -- naming a file it had just been unable
# to create.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# The IR comes from the seed, never from build-output\bare-metal, which holds
# whichever compiler ran last and can be several seeds stale. Passing it
# explicitly also puts the choice in the `kernel:` digest line, where a reader
# can see which compiler produced the IR.
$KernelArgs = @('-Kernel', (Join-Path $Repo 'seed' 'Codex.cdx'))

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
    -MemMB $MemMB -Port 9145
exit $LASTEXITCODE
