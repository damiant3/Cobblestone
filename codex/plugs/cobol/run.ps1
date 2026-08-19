# Run the COBOL plug over a Codex source file via TCP.
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

# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline
# in codex/compiler/IR/Passes.codex.
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\plug-run.ps1') `
    -IrInput $IrFile -Out $Out `
    -PlugCdx (Join-Path $OutDir 'cobol-plug.cdx') `
    -MemMB 3072 -Port 9105
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# A refusal is a hard failure, the rule babbage's runner already applies. Every
# construct this plug cannot express still emits a DISPLAY and MOVE 0, because
# the paragraph has to leave a value in its RET field, so the program COMPILES,
# RUNS and answers zero where the source meant something else. Measured
# 2026-08-18 on plug-oracle-arith: `lit-len [10,20,30,40]` moved four literals
# nowhere and answered 0 for 4. A caller reading only the exit code would take
# that for a working translation.
if (Test-Path $Out) {
    $refusals = @(Select-String -Path $Out -Pattern 'COBOL: .* not supported' | ForEach-Object { $_.Line.Trim() })
    if ($refusals.Count -gt 0) {
        [Console]::Error.WriteLine("REFUSED: the plug cannot express $($refusals.Count) construct(s) in this program:")
        $refusals | Select-Object -Unique | ForEach-Object { [Console]::Error.WriteLine("  $_") }
        [Console]::Error.WriteLine("  program with the refusals is at $Out")
        exit 6
    }
}
exit 0