# Run the Babbage plug over a Codex source file via TCP.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
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

# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline
# in codex/compiler/IR/Passes.codex.
$compileOut = & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' -Kernel $Kernel 2>&1
$compileOut | Where-Object { $_ -match '^kernel: ' } | ForEach-Object { [Console]::Error.WriteLine($_) }
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\plug-run.ps1') `
    -IrInput $IrFile -Out $Out `
    -PlugCdx (Join-Path $OutDir 'babbage-plug.cdx') `
    -MemMB 3072 -Port 9103
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# A refusal is a hard failure, the same rule t3isa's runner applies. The
# Analytical Engine has no text, no lambda and no list, and every site that
# meets one still emits N000 and stores it because the Engine needs a value
# in the column. That leaves a deck that RUNS and computes zero where the
# program meant something else, so a caller reading only the exit code would
# take a wrong answer for a working one.
if (Test-Path $Out) {
    $refusals = @(Select-String -Path $Out -Pattern '!UNSUPPORTED:' | ForEach-Object { $_.Line.Trim() })
    if ($refusals.Count -gt 0) {
        [Console]::Error.WriteLine("REFUSED: the plug cannot express $($refusals.Count) construct(s) in this program:")
        $refusals | Select-Object -Unique | ForEach-Object { [Console]::Error.WriteLine("  $_") }
        [Console]::Error.WriteLine("  deck with the markers is at $Out")
        exit 6
    }
}
exit 0
