# Run the Python plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [string]$Ir,
    [string]$Kernel = ''
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

# -Kernel names the compiler that produces the IR. Without it compile.ps1 falls
# back to a RELATIVE build-output\bare-metal\Codex.cdx, which is whichever
# compiler ran last in this workspace and resolves against the caller's working
# directory rather than the repo. Resolved absolutely here and always passed, so
# the `kernel:` line below names what actually compiled the subject.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (-not $Kernel) {
    $Kernel = Join-Path $Repo 'build-output' 'bare-metal' 'Codex.cdx'
    if (-not (Test-Path $Kernel)) { $Kernel = Join-Path $Repo 'seed' 'Codex.cdx' }
}

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
    $compileOut = & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' -Kernel $Kernel 2>&1
    $compileOut | Where-Object { $_ -match '^kernel: ' } | ForEach-Object { [Console]::Error.WriteLine($_) }
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
    -PlugCdx (Join-Path $OutDir 'python-plug.cdx') `
    -MemMB 3072 -Port 9131
exit $LASTEXITCODE
