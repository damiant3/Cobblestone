# Build the C# and F# benchmark projects (.NET 9, Release, tiered compilation
# off) and take one RyuJIT listing per benchmark function.
#
# Method (docs/Designs/Done/Compiler/CodegenAnalysis.md "Instruction Counts"):
# DOTNET_TieredCompilation=0 so the first and only JIT is FullOpts, and
# DOTNET_JitDisasm=<method> so the runtime prints that method's listing.
#
# Output: bench/build-output/dotnet/<lang>/<name>/{name}.jit, result.txt
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$OutRoot  = Join-Path $BenchDir 'build-output' 'dotnet'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Write-Error 'dotnet not on PATH'; exit 1 }
$sdk = (& dotnet --version 2>&1 | Out-String).Trim()
Write-Host "dotnet SDK $sdk"

# Bench name -> the JIT method name in each language (C# methods are
# PascalCase; F# lets are the C names).
$benches = @(
    @{ Name = 'fib';      Cs = 'Fib';      Fs = 'fib' }
    @{ Name = 'fact';     Cs = 'Fact';     Fs = 'fact' }
    @{ Name = 'gcd';      Cs = 'Gcd';      Fs = 'gcd' }
    @{ Name = 'sum';      Cs = 'Sum';      Fs = 'sum' }
    @{ Name = 'ack';      Cs = 'Ack';      Fs = 'ack' }
    @{ Name = 'tak';      Cs = 'Tak';      Fs = 'tak' }
    @{ Name = 'collatz';  Cs = 'Collatz';  Fs = 'collatz' }
    @{ Name = 'locals';   Cs = 'Compute';  Fs = 'compute' }
    @{ Name = 'regright'; Cs = 'Regright'; Fs = 'regright' }
)

foreach ($lang in @('csharp', 'fsharp')) {
    $proj = Join-Path $BenchDir $lang
    $bin  = Join-Path $OutRoot $lang 'bin'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Write-Host "=== $lang : dotnet build -c Release ==="
    $buildLog = & dotnet build $proj -c Release -nologo -v q -o $bin 2>&1
    $buildLog | Out-File -FilePath (Join-Path $OutRoot $lang 'build.log') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  BUILD FAIL ($lang)"; $buildLog | ForEach-Object { Write-Host "    $_" }
        continue
    }
    $exe = Join-Path $bin 'Bench.dll'
    foreach ($b in $benches) {
        $method = if ($lang -eq 'csharp') { $b.Cs } else { $b.Fs }
        $outDir = Join-Path $OutRoot $lang $b.Name
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $env:DOTNET_TieredCompilation = '0'
        $env:DOTNET_JitDisasm = $method
        $env:DOTNET_JitStdOutFile = Join-Path $outDir "$($b.Name).jit"
        try {
            $output = (& dotnet $exe $b.Name 2>&1 | Out-String).Trim()
        } finally {
            Remove-Item Env:DOTNET_TieredCompilation, Env:DOTNET_JitDisasm, Env:DOTNET_JitStdOutFile -ErrorAction SilentlyContinue
        }
        # Older runtimes ignore JitStdOutFile and print to stdout: keep the
        # program's answer (the last line) apart from any listing above it.
        $lines = @($output -split "`r?`n")
        $answer = ($lines | Where-Object { $_ -match '^-?\d+$' } | Select-Object -Last 1)
        if (-not (Test-Path (Join-Path $outDir "$($b.Name).jit")) -or ((Get-Item (Join-Path $outDir "$($b.Name).jit")).Length -eq 0)) {
            $output | Out-File -FilePath (Join-Path $outDir "$($b.Name).jit") -Encoding UTF8
        }
        Write-Host "  [$lang] $($b.Name) ($method): $answer"
        $answer | Out-File -FilePath (Join-Path $outDir 'result.txt') -Encoding UTF8
    }
}

$sdk | Out-File -FilePath (Join-Path $OutRoot 'dotnet-version.txt') -Encoding UTF8
Write-Host "`nDone. JIT listings in $OutRoot"
