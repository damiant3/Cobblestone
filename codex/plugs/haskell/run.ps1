# Run the Haskell plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [string]$Ir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$OutDir  = Join-Path $PSScriptRoot 'build-output'
$IrFile  = Join-Path $OutDir 'last-run.ir'
$LogFile = Join-Path $OutDir 'run.log'

# Either consume a pre-built IR file (-Ir) or compile -Src to IR here, the
# shape codex/plugs/csharp/run.ps1 already carries. A fan-out over several
# plugs compiles the same source once and hands every plug the same bytes.
#
# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline
# in codex/compiler/IR/Passes.codex. A caller supplying -Ir owes those flags.
if ($Ir) {
    if (-not (Test-Path -PathType Leaf $Ir)) {
        [Console]::Error.WriteLine("MISSING: -Ir $Ir")
        exit 3
    }
    $IrFile = (Resolve-Path $Ir).Path
} elseif ($Src) {
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
        [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
        exit 4
    }
} else {
    [Console]::Error.WriteLine("FAIL: provide -Src <source.codex> or -Ir <prebuilt.ir>")
    exit 1
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\plug-run.ps1') `
    -IrInput $IrFile -Out $Out `
    -PlugCdx (Join-Path $OutDir 'haskell-plug.cdx') `
    -MemMB 3072 -Port 9117
exit $LASTEXITCODE