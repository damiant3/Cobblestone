# Transpile the nine bench/codex programs through the ZIG PLUG, then build
# them with build-zig.ps1 the way the hand-written bench/zig files are built,
# so the report can put "Codex through the zig plug" beside "hand-written zig"
# and "Codex native". The plug is run the way it is graded (codex/plugs/zig/
# run.ps1: -Passes text-plug, the seed as the kernel), and it must be built
# first (codex/plugs/zig/build.ps1).
#
# Output: bench/build-output/zig-codex/src/<name>.zig
#         bench/build-output/zig-codex/<name>/{Debug,ReleaseFast}/{name}.exe, {name}.s
[CmdletBinding()]
param(
    [string]$Zig = 'D:\zig-0.16.0\zig.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$Repo     = Split-Path $BenchDir
$SrcDir   = Join-Path $BenchDir 'codex'
$OutRoot  = Join-Path $BenchDir 'build-output' 'zig-codex'
$ZigSrc   = Join-Path $OutRoot 'src'
$RunPlug  = Join-Path $Repo 'codex' 'plugs' 'zig' 'run.ps1'
$PlugCdx  = Join-Path $Repo 'codex' 'plugs' 'zig' 'build-output' 'zig-plug.cdx'

if (-not (Test-Path $PlugCdx)) { Write-Error "zig plug not built: $PlugCdx (run codex/plugs/zig/build.ps1)"; exit 1 }
New-Item -ItemType Directory -Force -Path $ZigSrc | Out-Null

# The same nine the C, zig and dotnet columns carry; the elaborate
# bench/codex programs with no reference column are not transpiled.
# Bench name -> the measured function as the plug names it (Codex `-` is `_`).
# That function is marked `export` in the emitted source, the same word the
# hand-written bench/zig files carry: it pins the C ABI and external linkage,
# so LLVM neither inlines the body into main nor specialises it for its one
# call site. Measured without it (2026-08-17): ReleaseFast inlined every
# non-recursive one and left no symbol; with only `noinline`, `fact` became a
# constant (`movabs rax, 2432902008176640000; ret`) and `sum_to` vanished.
# The nine measured functions are all i64 -> i64, so `export` is legal.
$names = @{
    'fib' = 'fib'; 'fact' = 'fact'; 'gcd' = 'my_gcd'; 'sum' = 'sum_to'; 'ack' = 'ack';
    'tak' = 'tak'; 'collatz' = 'collatz'; 'locals' = 'compute'; 'regright' = 'regright'
}
$failed = 0
foreach ($n in @('fib', 'fact', 'gcd', 'sum', 'ack', 'tak', 'collatz', 'locals', 'regright')) {
    $src = Join-Path $SrcDir "$n.codex"
    $out = Join-Path $ZigSrc "$n.zig"
    Write-Host "  [zig-plug] $n"
    & pwsh -NoProfile -File $RunPlug -Src $src -Out $out 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { Write-Host "    FAIL (exit $LASTEXITCODE)"; $failed++; continue }
    $text = [System.IO.File]::ReadAllText($out)
    $fence = "fn $($names[$n])("
    if ($text.Contains("`n$fence")) {
        $text = $text.Replace("`n$fence", "`nexport $fence")
        [System.IO.File]::WriteAllText($out, $text, [System.Text.UTF8Encoding]::new($false))
    } else {
        Write-Host "    WARN: '$fence' not found; not fenced"
    }
}
if ($failed -gt 0) { Write-Host "$failed transpile(s) failed" }

& pwsh -NoProfile -File (Join-Path $BenchDir 'build-zig.ps1') -Zig $Zig -SrcDir $ZigSrc -OutName 'zig-codex'
exit $LASTEXITCODE
