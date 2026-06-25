# Emit the entire Codex compiler (codex/compiler/) as a single C# file.
#
#   codex/compiler/*.codex  (54 files, + cited forewords)
#     |
#     v  build/concat-codex-self.ps1
#   Codex.codex  (one quire-prefixed chapter)
#     |
#     v  build/compile.ps1 -IrCce   (seed self-host, in codex-vm)
#   compiler.ir  (CCE IR text, reachable from "opening")
#     |
#     v  plugs/csharp/run.ps1 -Ir   (csharp-plug.cdx over TCP)
#   Codex.cs  (full compiler as C#)
#
# Success criterion: the emitted Codex.cs compiles under `dotnet build`.
#
# Usage:
#   plugs/csharp/emit-compiler.ps1                 # -> build-output/Codex.cs
#   plugs/csharp/emit-compiler.ps1 -Out path.cs
#   plugs/csharp/emit-compiler.ps1 -SkipIr         # reuse existing compiler.ir
[CmdletBinding()]
param(
    [string]$Out,
    [int]$MemMB = 3072,
    [switch]$SkipIr
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$OutDir   = Join-Path $PlugDir 'build-output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$ConcatScript = Join-Path $Repo 'build\concat-codex-self.ps1'
$CompileScript = Join-Path $Repo 'build\compile.ps1'
$RunScript    = Join-Path $PlugDir 'run.ps1'

$CodexSrc = Join-Path $OutDir 'Codex.codex'
$IrFile   = Join-Path $OutDir 'compiler.ir'
$IrLog    = Join-Path $OutDir 'compiler-ir.log'
if (-not $Out) { $Out = Join-Path $OutDir 'Codex.cs' }

if (-not $SkipIr) {
    # -- Phase 1: concatenate the full compiler source ----------------
    Write-Host "[emit-compiler] concatenating codex/compiler -> $CodexSrc"
    & pwsh -NoProfile -File $ConcatScript -OutFile $CodexSrc
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: concat exited $LASTEXITCODE"); exit 3 }
    $srcLen = (Get-Item $CodexSrc).Length
    Write-Host "[emit-compiler] compiler source: $srcLen bytes"

    # -- Phase 2: source -> IR (CCE) ----------------------------------
    Write-Host "[emit-compiler] compiling to IR (mem=${MemMB}MB)..."
    & pwsh -NoProfile -File $CompileScript -Src $CodexSrc -Out $IrFile -Log $IrLog -IrCce -MemMB $MemMB
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: IR emit exited $LASTEXITCODE; see $IrLog")
        Get-Content $IrLog -ErrorAction SilentlyContinue | Select-Object -Last 15 | ForEach-Object { [Console]::Error.WriteLine("  $_") }
        exit 4
    }
}
if (-not (Test-Path -PathType Leaf $IrFile)) {
    [Console]::Error.WriteLine("MISSING: $IrFile (run without -SkipIr first)")
    exit 4
}
Write-Host "[emit-compiler] IR: $((Get-Item $IrFile).Length) bytes"

# -- Phase 3: IR -> C# via the plug ----------------------------------
& pwsh -NoProfile -File $RunScript -Ir $IrFile -Out $Out -MemMB $MemMB
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: plug run exited $LASTEXITCODE"); exit 5 }

$csLen = (Get-Item $Out).Length
Write-Host "[emit-compiler] OK: $Out ($csLen bytes)"
