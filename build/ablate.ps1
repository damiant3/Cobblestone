# Ablation runner for the IR pass pipeline (Middle End campaign, step 3).
#
# For each pass configuration, boots the given kernel and measures:
#   1. Compiler self-compile (unless -BenchOnly): concat the compiler source
#      once (build/concat-codex-self.ps1, shared across configs), compile it
#      under the config, record output CDX bytes and wall-clock seconds.
#   2. Benchmarks (unless -CompilerOnly): compile each bench/codex source
#      under the config, record CDX bytes and the benchmark function's body
#      instruction count. Counting uses the SAME mechanism as
#      bench/compare.ps1 (map-guided .text extraction -> COFF wrap ->
#      dumpbin /disasm -> Count-Instructions), so counts are comparable to
#      the historical tables in docs/ArchitectsSketchbook.md and
#      the archived codegen analysis.
#
# What this does NOT measure — read before trusting a row:
#   - NO per-config fixed-point check. Only the shipping default pipeline is
#     gate-verified (build/build.ps1). A non-default config that compiles and
#     scores well here has NOT been shown to self-host to a fixed point.
#   - Wall-clock seconds are a SINGLE run inside a VM on a shared host.
#     Noisy. Treat deltas under ~10% as noise; re-run before believing them.
#   - Bench instruction counts are STATIC counts of the benchmark function
#     body, not cycles, not dynamic counts, not whole-program size.
#
# Usage:
#   build/ablate.ps1                                   # seed kernel, full grid
#   build/ablate.ps1 -Kernel build\output\b2\SutNew.cdx -Configs @('default','none')
#   build/ablate.ps1 -BenchOnly                        # skip self-compiles
#
# 'default' as a config name means: omit -Passes (the kernel's built-in
# pipeline, currently fold-constants,inline-leaf-calls). Any other config
# string is passed through as compile.ps1 -Passes '<config>' ('none' = empty
# pipeline; comma-separated names select passes in order).
#
# Output: <OutDir>/report.md (GitHub markdown table, also echoed to stdout),
#         <OutDir>/results.csv (machine-readable),
#         <OutDir>/logs/ and <OutDir>/<config>/ (per-compile artifacts).
[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string[]]$Configs = @('default', 'none', 'fold-constants', 'inline-leaf-calls'),
    [switch]$BenchOnly,
    [switch]$CompilerOnly,
    [string]$OutDir = 'build\output\ablate'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Compile = Join-Path $Repo 'build\compile.ps1'
$Concat  = Join-Path $Repo 'build\concat-codex-self.ps1'
$BenchSrcDir = Join-Path $Repo 'bench\codex'

if ($BenchOnly -and $CompilerOnly) { Write-Error '-BenchOnly and -CompilerOnly are mutually exclusive'; exit 1 }
if ($Configs.Count -eq 0) { Write-Error 'At least one config is required'; exit 1 }
if (-not (Test-Path -PathType Leaf $Compile)) { Write-Error "compile.ps1 not found at $Compile"; exit 1 }

# Resolve the kernel against cwd first, then the repo root.
$KernelPath = $Kernel
if (-not (Test-Path -PathType Leaf $KernelPath)) { $KernelPath = Join-Path $Repo $Kernel }
if (-not (Test-Path -PathType Leaf $KernelPath)) { Write-Error "Kernel not found: $Kernel"; exit 1 }
$KernelPath = (Resolve-Path $KernelPath).Path
$kernelHash = (Get-FileHash -Algorithm SHA256 $KernelPath).Hash.Substring(0, 16)

if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $Repo $OutDir }
$LogDir = Join-Path $OutDir 'logs'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# Bench function mapping — same names bench/compare.ps1 uses ($benchConfig),
# so counts line up with the historical tables. A source in bench/codex with
# no entry here is reported as skipped, never silently dropped.
$BenchFuncs = [ordered]@{
    'fib'       = @('fib')
    'fact'      = @('fact')
    'gcd'       = @('my-gcd')
    'sum'       = @('sum-to')
    'ack'       = @('ack')
    'tak'       = @('tak')
    'collatz'   = @('collatz')
    'locals'    = @('compute')
    'regright'  = @('regright')
    'regstress' = @('regstress')
}

$benches = @()
$skipped = @()
if (-not $CompilerOnly) {
    foreach ($src in (Get-ChildItem -Path $BenchSrcDir -Filter '*.codex' | Sort-Object Name)) {
        if ($BenchFuncs.Contains($src.BaseName)) {
            $benches += $src.BaseName
        } else {
            $skipped += "$($src.BaseName) (no benchmark-function mapping; not in bench/compare.ps1's table)"
        }
    }
    # Keep table column order stable: the compare.ps1 order, not alphabetical.
    $benches = @($BenchFuncs.Keys | Where-Object { $benches -contains $_ })
    if ($benches.Count -eq 0) { Write-Error "No mapped bench sources found in $BenchSrcDir"; exit 1 }
}

function Get-ConfigSlug {
    param([string]$Config)
    return ($Config -replace '[^A-Za-z0-9-]', '+')
}

# --- Instruction counting: bench/compare.ps1's mechanism, replicated ---
# (Make-CoffObj + dumpbin come from bench/disasm-cdx.ps1; Count-Instructions
# and the per-function line filter from bench/compare.ps1. Do not change the
# regexes — comparability with the historical tables depends on them.)

$vcvars = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat'
$script:disasmBat = $null
if (-not $CompilerOnly) {
    if (-not (Test-Path $vcvars)) { Write-Error "vcvarsall.bat not found at $vcvars (needed for dumpbin /disasm)"; exit 1 }
    $script:disasmBat = [System.IO.Path]::GetTempFileName() + '.bat'
}

function Make-CoffObj {
    param([byte[]]$Code, [string]$OutPath)
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([uint16]0x8664); $bw.Write([uint16]1); $bw.Write([uint32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([uint16]0); $bw.Write([uint16]0)
    $nameBytes = [System.Text.Encoding]::ASCII.GetBytes('.text')
    $bw.Write($nameBytes)
    for ($i = $nameBytes.Length; $i -lt 8; $i++) { $bw.Write([byte]0) }
    $bw.Write([uint32]$Code.Length); $bw.Write([uint32]0); $bw.Write([uint32]$Code.Length)
    $bw.Write([uint32]60); $bw.Write([uint32]0); $bw.Write([uint32]0)
    $bw.Write([uint16]0); $bw.Write([uint16]0); $bw.Write([uint32]0x60000020)
    $bw.Write($Code)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Close(); $ms.Close()
}

function Disasm-Obj {
    param([string]$ObjPath)
    $batLines = @(
        '@echo off',
        "call `"$vcvars`" x64 >nul 2>&1",
        "dumpbin /disasm `"$ObjPath`""
    )
    [System.IO.File]::WriteAllLines($script:disasmBat, $batLines, [System.Text.Encoding]::ASCII)
    $output = cmd /c $script:disasmBat 2>&1
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in @($output | ForEach-Object { $_.ToString() })) {
        if ($l -match '^\s*[0-9A-Fa-f]{16}:') { [void]$result.Add($l) }
    }
    return $result
}

function Count-Instructions {
    param([string[]]$Lines)
    $stats = @{ Total = 0; Branches = 0; MemOps = 0; Moves = 0; Arithmetic = 0; Bytes = 0 }
    foreach ($l in $Lines) {
        $isInstr = ($l -match '^\s+\S') -or ($l -match '^\s*[0-9A-Fa-f]{16}:')
        if ($isInstr) {
            $stats.Total++
            if ($l -match '\b(jmp|je|jne|jz|jnz|jg|jge|jl|jle|ja|jae|jb|jbe|jc|jnc|jo|jno|js|jns|call|ret|loop|jae|jbe|sete|setne|setg|setl)\b') { $stats.Branches++ }
            if ($l -match '\b(mov|lea)\b.*\[') { $stats.MemOps++ }
            if ($l -match '\b(push|pop)\b') { $stats.MemOps++ }
            if ($l -match '\b(mov|lea|movzx|movsx|movsxd|cmov\w*)\b') { $stats.Moves++ }
            if ($l -match '\b(add|sub|imul|idiv|mul|div|inc|dec|neg|cmp|test|xor|and|or|shl|shr|sar|sal)\b') { $stats.Arithmetic++ }
        }
    }
    return $stats
}

function Get-FunctionInsnCount {
    # Extract the named functions' bytes from a CDX via its .map sidecar,
    # disassemble, count. Returns -1 if the map or any function is missing.
    param([string]$CdxPath, [string]$MapPath, [string[]]$FuncNames)
    if (-not (Test-Path -PathType Leaf $MapPath)) { return -1 }
    $cdx = [System.IO.File]::ReadAllBytes($CdxPath)
    $textOff = [BitConverter]::ToInt64($cdx, 168)
    $textSz  = [BitConverter]::ToInt64($cdx, 176)
    $allLines = [System.Collections.Generic.List[string]]::new()
    $found = 0
    foreach ($line in [System.IO.File]::ReadAllLines($MapPath)) {
        if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
            $name = $matches[3].Trim()
            if ($FuncNames -notcontains $name) { continue }
            $addr = [Convert]::ToInt64($matches[1], 16)
            $size = [int]$matches[2]
            $offsetInText = $addr - 0x100000
            if ($offsetInText -lt 0 -or $offsetInText + $size -gt $textSz) { continue }
            $codeBytes = New-Object byte[] $size
            [Array]::Copy($cdx, $textOff + $offsetInText, $codeBytes, 0, $size)
            $tmpObj = [System.IO.Path]::GetTempFileName() + '.obj'
            try {
                Make-CoffObj -Code $codeBytes -OutPath $tmpObj
                foreach ($al in (Disasm-Obj -ObjPath $tmpObj)) { [void]$allLines.Add($al) }
            } finally {
                Remove-Item -Force $tmpObj -ErrorAction SilentlyContinue
            }
            $found++
        }
    }
    if ($found -eq 0) { return -1 }
    return (Count-Instructions $allLines.ToArray()).Total
}

function Invoke-CompileTimed {
    # Returns @{ Ok; Seconds; Bytes }. Never throws on a compile failure —
    # a failed config is a datum, not an abort.
    param([string]$Src, [string]$Out, [string]$Log, [string]$PassArg)
    $psArgs = @('-NoProfile', '-File', $Compile, '-Src', $Src, '-Out', $Out, '-Log', $Log, '-Kernel', $KernelPath)
    if ($PassArg) { $psArgs += @('-Passes', $PassArg) }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # Route the child's output to the host: anything left on the success
    # stream would leak into this function's return value.
    & pwsh @psArgs 2>&1 | ForEach-Object { Write-Host "    $_" }
    $exit = $LASTEXITCODE
    $sw.Stop()
    if ($exit -ne 0 -or -not (Test-Path -PathType Leaf $Out)) {
        return @{ Ok = $false; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); Bytes = 0 }
    }
    return @{ Ok = $true; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); Bytes = (Get-Item $Out).Length }
}

# --- Concat the compiler source once, shared across configs ---
$compilerSrc = $null
if (-not $BenchOnly) {
    $compilerSrc = Join-Path $OutDir 'Codex.codex'
    Write-Host "=== Concatenating compiler source -> $compilerSrc ==="
    & pwsh -NoProfile -File $Concat -OutFile $compilerSrc
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $compilerSrc)) {
        Write-Error 'concat-codex-self.ps1 failed'; exit 1
    }
}

Write-Host "=== Ablation run ==="
Write-Host "kernel:  $KernelPath [$kernelHash]"
Write-Host "configs: $($Configs -join ' | ')"
if (-not $CompilerOnly) { Write-Host "benches: $($benches -join ', ')" }
foreach ($s in $skipped) { Write-Host "SKIP bench: $s" }

# --- The grid ---
$results = [ordered]@{}
foreach ($config in $Configs) {
    $slug = Get-ConfigSlug $config
    $cfgDir = Join-Path $OutDir $slug
    New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
    $passArg = if ($config -eq 'default') { '' } else { $config }
    $row = @{ CompilerBytes = $null; CompilerSeconds = $null; Bench = [ordered]@{} }

    if (-not $BenchOnly) {
        Write-Host "`n[$config] compiler self-compile..."
        $r = Invoke-CompileTimed -Src $compilerSrc `
            -Out (Join-Path $cfgDir 'Codex.cdx') `
            -Log (Join-Path $LogDir "$slug-compiler.log") -PassArg $passArg
        if ($r.Ok) {
            $row.CompilerBytes = $r.Bytes; $row.CompilerSeconds = $r.Seconds
            Write-Host "  $($r.Bytes) bytes, $($r.Seconds)s"
        } else {
            Write-Host "  FAIL (see $LogDir\$slug-compiler.log)"
        }
    }

    foreach ($bench in $benches) {
        Write-Host "[$config] bench $bench..."
        $out = Join-Path $cfgDir "$bench.cdx"
        $r = Invoke-CompileTimed -Src (Join-Path $BenchSrcDir "$bench.codex") `
            -Out $out -Log (Join-Path $LogDir "$slug-$bench.log") -PassArg $passArg
        if ($r.Ok) {
            $insns = Get-FunctionInsnCount -CdxPath $out `
                -MapPath ([System.IO.Path]::ChangeExtension($out, '.map')) `
                -FuncNames $BenchFuncs[$bench]
            $row.Bench[$bench] = @{ Bytes = $r.Bytes; Insns = $insns }
            $insnText = if ($insns -ge 0) { $insns } else { 'no-count' }
            Write-Host "  $($r.Bytes) bytes, $insnText insns ($($BenchFuncs[$bench] -join ','))"
        } else {
            $row.Bench[$bench] = @{ Bytes = -1; Insns = -1 }
            Write-Host "  FAIL (see $LogDir\$slug-$bench.log)"
        }
    }
    $results[$config] = $row
}

if ($script:disasmBat) { Remove-Item -Force $script:disasmBat -ErrorAction SilentlyContinue }

# --- Report ---
function Format-Cell { param($v) if ($null -eq $v -or $v -lt 0) { 'fail' } else { "$v" } }

$md = [System.Collections.Generic.List[string]]::new()
[void]$md.Add("# Pass-pipeline ablation")
[void]$md.Add("")
[void]$md.Add("- Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$md.Add("- Kernel: ``$KernelPath`` [$kernelHash]")
[void]$md.Add("- Configs: $($Configs -join ' | ') ('default' = the kernel's built-in pipeline)")
if (-not $CompilerOnly) {
    [void]$md.Add("- Bench columns are static instruction counts of the benchmark function body (bench/compare.ps1 mechanism).")
    foreach ($s in $skipped) { [void]$md.Add("- Skipped bench: $s") }
}
[void]$md.Add("- NOT measured: per-config fixed point (only the shipping default is gate-verified); wall time is one noisy run.")
[void]$md.Add("")

$hdr = '| Config |'
$sep = '|---|'
if (-not $BenchOnly) { $hdr += ' Compiler bytes | Compiler s |'; $sep += '---:|---:|' }
foreach ($b in $benches) { $hdr += " $b |"; $sep += '---:|' }
[void]$md.Add($hdr)
[void]$md.Add($sep)
foreach ($config in $Configs) {
    $row = $results[$config]
    $line = "| $config |"
    if (-not $BenchOnly) {
        $line += " $(Format-Cell $row.CompilerBytes) | $(Format-Cell $row.CompilerSeconds) |"
    }
    foreach ($b in $benches) {
        $line += " $(Format-Cell $row.Bench[$b].Insns) |"
    }
    [void]$md.Add($line)
}

$reportPath = Join-Path $OutDir 'report.md'
[System.IO.File]::WriteAllLines($reportPath, $md.ToArray(), [System.Text.UTF8Encoding]::new($false))

$csv = [System.Collections.Generic.List[string]]::new()
$csvHdr = 'config,compiler_bytes,compiler_seconds'
foreach ($b in $benches) { $csvHdr += ",${b}_insns,${b}_bytes" }
[void]$csv.Add($csvHdr)
foreach ($config in $Configs) {
    $row = $results[$config]
    $cb = if ($null -ne $row.CompilerBytes) { $row.CompilerBytes } else { '' }
    $cs = if ($null -ne $row.CompilerSeconds) { $row.CompilerSeconds } else { '' }
    $line = "$config,$cb,$cs"
    foreach ($b in $benches) {
        $cell = $row.Bench[$b]
        $iv = if ($cell.Insns -ge 0) { $cell.Insns } else { '' }
        $bv = if ($cell.Bytes -ge 0) { $cell.Bytes } else { '' }
        $line += ",$iv,$bv"
    }
    [void]$csv.Add($line)
}
$csvPath = Join-Path $OutDir 'results.csv'
[System.IO.File]::WriteAllLines($csvPath, $csv.ToArray(), [System.Text.UTF8Encoding]::new($false))

Write-Host ""
foreach ($l in $md) { Write-Host $l }
Write-Host ""
Write-Host "Report: $reportPath"
Write-Host "CSV:    $csvPath"
