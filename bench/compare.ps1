# Master codegen comparison harness.
# Builds C and Codex benchmarks, extracts disassembly, produces a
# side-by-side comparison report.
#
# Usage: bench/compare.ps1
# Output: bench/build-output/report.txt
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$OutRoot  = Join-Path $BenchDir 'build-output'
$Report   = Join-Path $OutRoot 'report.txt'

# --- Step 1: Build ---
if (-not $SkipBuild) {
    Write-Host "=== Building C benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-c.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "C build failed"; exit 1 }

    Write-Host "`n=== Building C# and F# benchmarks (RyuJIT listings) ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-dotnet.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "dotnet build failed"; exit 1 }

    Write-Host "`n=== Building Zig benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-zig.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "Zig build failed"; exit 1 }

    Write-Host "`n=== Transpiling Codex through the zig plug and building it ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'transpile-zig.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "zig-plug transpile/build failed (continuing with partial results)" }

    Write-Host "`n=== Building Codex benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-codex.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "Codex build failed"; exit 1 }

}

# --- Step 2: Parse and Compare ---
$benchmarks = @('fib', 'fact', 'gcd', 'sum', 'ack', 'tak', 'collatz', 'locals', 'regright')
$out = [System.Collections.Generic.List[string]]::new()

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

function Parse-Msvc-Asm {
    param([string]$AsmFile, [string]$FuncName)
    if (-not (Test-Path $AsmFile)) { return @() }
    $lines = Get-Content $AsmFile
    $inFunc = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if ($l -match "^${FuncName}\s+PROC") { $inFunc = $true; continue }
        if ($l -match "^${FuncName}\s+ENDP") { break }
        if ($inFunc -and $l.Trim() -ne '' -and $l -notmatch '^\s*;' -and $l -notmatch '^\$' -and $l -notmatch '^\w.*=\s+\d') {
            [void]$result.Add($l)
        }
    }
    return $result.ToArray()
}

# zig -femit-asm writes Intel-syntax GAS: the exported function's body sits
# under the label `<file>.<func>:` (the export is an alias, `func = file.func`)
# and ends at `.seh_endproc`; directives and labels begin with `.` and are
# dropped so that only instruction lines reach Count-Instructions.
function Parse-Zig-Asm {
    param([string]$AsmFile, [string]$FileName, [string]$FuncName)
    if (-not (Test-Path $AsmFile)) { return @() }
    $lines = Get-Content $AsmFile
    $inFunc = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if ($l -match "^$([regex]::Escape("$FileName.$FuncName")):") { $inFunc = $true; continue }
        if ($inFunc -and $l -match '^\s*\.seh_endproc') { break }
        if ($inFunc -and $l -match '^\s+[a-z]') {
            [void]$result.Add($l)
        }
    }
    return $result.ToArray()
}

# RyuJIT listing (DOTNET_JitDisasm): the method's block starts at
# `; Assembly listing for method <Type>:<Method>(` and ends at `; Total bytes
# of code`; instructions are indented, labels `G_M..._IG..:` and comments `;`
# sit at column 0, and `align` padding lines are dropped.
function Parse-Jit-Asm {
    param([string]$JitFile, [string]$MethodName)
    if (-not (Test-Path $JitFile)) { return @() }
    $lines = Get-Content $JitFile
    $inFunc = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if (-not $inFunc -and $l -match "^; Assembly listing for method [^:]+:$([regex]::Escape($MethodName))\(") { $inFunc = $true; continue }
        if ($inFunc -and $l -match '^; Total bytes of code') { break }
        if ($inFunc -and $l -match '^\s+[a-z]' -and $l -notmatch '^\s+align\b') {
            [void]$result.Add($l)
        }
    }
    return $result.ToArray()
}

function Parse-Codex-Disasm {
    param([string]$DisasmFile, [string[]]$FuncNames)
    if (-not (Test-Path $DisasmFile)) { return @() }
    $lines = Get-Content $DisasmFile
    $inFunc = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if ($l -match '^--- (.+?) \(') {
            $fn = $matches[1]
            $inFunc = $FuncNames -contains $fn
            continue
        }
        if ($inFunc -and $l -match '^\s*[0-9A-Fa-f]{16}:') {
            [void]$result.Add($l)
        }
    }
    return $result.ToArray()
}

# Map benchmark name to C function name and Codex function names
$benchConfig = @{
    'fib'     = @{ CFunc = 'fib';     CodexFuncs = @('fib');      CsMethod = 'Fib';      FsMethod = 'fib' }
    'fact'    = @{ CFunc = 'fact';    CodexFuncs = @('fact');     CsMethod = 'Fact';     FsMethod = 'fact' }
    'gcd'     = @{ CFunc = 'gcd';     CodexFuncs = @('my-gcd');   CsMethod = 'Gcd';      FsMethod = 'gcd' }
    'sum'     = @{ CFunc = 'sum';     CodexFuncs = @('sum-to');   CsMethod = 'Sum';      FsMethod = 'sum' }
    'ack'     = @{ CFunc = 'ack';     CodexFuncs = @('ack');      CsMethod = 'Ack';      FsMethod = 'ack' }
    'tak'     = @{ CFunc = 'tak';     CodexFuncs = @('tak');      CsMethod = 'Tak';      FsMethod = 'tak' }
    'collatz' = @{ CFunc = 'collatz'; CodexFuncs = @('collatz');  CsMethod = 'Collatz';  FsMethod = 'collatz' }
    'locals'  = @{ CFunc = 'compute'; CodexFuncs = @('compute');  CsMethod = 'Compute';  FsMethod = 'compute' }
    'regright' = @{ CFunc = 'regright'; CodexFuncs = @('regright'); CsMethod = 'Regright'; FsMethod = 'regright' }
}

if (-not $SkipBuild) {
    Write-Host "`n=== Extracting Codex disassembly ==="
    foreach ($b in $benchmarks) {
        $funcs = $benchConfig[$b].CodexFuncs
        & pwsh -NoProfile -File (Join-Path $BenchDir 'disasm-cdx.ps1') -Name $b -Functions $funcs
    }
}

[void]$out.Add("Codex vs C Codegen Comparison Report")
[void]$out.Add("=" * 60)
[void]$out.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$kernelFile = Join-Path $OutRoot 'codex' 'kernel.txt'
if (Test-Path $kernelFile) { [void]$out.Add("Codex kernel: $((Get-Content $kernelFile -Raw).Trim())") }
$zigVersionFile = Join-Path $OutRoot 'zig' 'zig-version.txt'
if (Test-Path $zigVersionFile) { [void]$out.Add("Zig: $((Get-Content $zigVersionFile -Raw).Trim()) (D:\zig-0.16.0)") }
$dotnetVersionFile = Join-Path $OutRoot 'dotnet' 'dotnet-version.txt'
if (Test-Path $dotnetVersionFile) { [void]$out.Add(".NET SDK: $((Get-Content $dotnetVersionFile -Raw).Trim()) (RyuJIT FullOpts, TieredCompilation=0)") }
[void]$out.Add("")

foreach ($bench in $benchmarks) {
    $cfg = $benchConfig[$bench]
    [void]$out.Add("=" * 60)
    [void]$out.Add("  $($bench.ToUpper())")
    [void]$out.Add("=" * 60)

    # Correctness check
    $cResult = $null; $codexResult = $null
    $cResultFile = Join-Path $OutRoot 'c' $bench 'O2' 'result.txt'
    $codexResultFile = Join-Path $OutRoot 'codex' $bench 'result.txt'
    if (Test-Path $cResultFile) { $cResult = (Get-Content $cResultFile -Raw).Trim() }
    if (Test-Path $codexResultFile) { $codexResult = (Get-Content $codexResultFile -Raw).Trim() }
    $zigResult = $null
    $zigResultFile = Join-Path $OutRoot 'zig' $bench 'ReleaseFast' 'result.txt'
    if (Test-Path $zigResultFile) { $zigResult = (Get-Content $zigResultFile -Raw).Trim() }
    $zcResult = $null; $csResult = $null; $fsResult = $null
    $zcResultFile = Join-Path $OutRoot 'zig-codex' $bench 'ReleaseFast' 'result.txt'
    if (Test-Path $zcResultFile) { $zcResult = (Get-Content $zcResultFile -Raw).Trim() }
    $csResultFile = Join-Path $OutRoot 'dotnet' 'csharp' $bench 'result.txt'
    if (Test-Path $csResultFile) { $csResult = (Get-Content $csResultFile -Raw).Trim() }
    $fsResultFile = Join-Path $OutRoot 'dotnet' 'fsharp' $bench 'result.txt'
    if (Test-Path $fsResultFile) { $fsResult = (Get-Content $fsResultFile -Raw).Trim() }
    $match = if ($cResult -eq $codexResult -and $cResult -eq $zigResult -and $cResult -eq $zcResult -and $cResult -eq $csResult -and $cResult -eq $fsResult) { 'PASS' } else { 'MISMATCH' }
    [void]$out.Add("Correctness: C=$cResult  C#=$csResult  F#=$fsResult  Zig=$zigResult  Zig(codex)=$zcResult  Codex=$codexResult  $match")
    [void]$out.Add("")

    # C# / F# RyuJIT (FullOpts)
    $csJit = Join-Path $OutRoot 'dotnet' 'csharp' $bench "$bench.jit"
    $csLines = Parse-Jit-Asm -JitFile $csJit -MethodName $cfg.CsMethod
    $csStats = Count-Instructions $csLines
    $fsJit = Join-Path $OutRoot 'dotnet' 'fsharp' $bench "$bench.jit"
    $fsLines = Parse-Jit-Asm -JitFile $fsJit -MethodName $cfg.FsMethod
    $fsStats = Count-Instructions $fsLines

    # Codex transpiled through the zig plug, built by zig at both modes; the
    # plug names the function after the Codex def with `-` written `_`.
    $zcFunc = ($cfg.CodexFuncs[0] -replace '-', '_')
    $zcdAsm = Join-Path $OutRoot 'zig-codex' $bench 'Debug' "$bench.s"
    $zcdLines = Parse-Zig-Asm -AsmFile $zcdAsm -FileName $bench -FuncName $zcFunc
    $zcdStats = Count-Instructions $zcdLines
    $zcfAsm = Join-Path $OutRoot 'zig-codex' $bench 'ReleaseFast' "$bench.s"
    $zcfLines = Parse-Zig-Asm -AsmFile $zcfAsm -FileName $bench -FuncName $zcFunc
    $zcfStats = Count-Instructions $zcfLines

    # Zig -O Debug / -O ReleaseFast
    $zdAsm = Join-Path $OutRoot 'zig' $bench 'Debug' "$bench.s"
    $zdLines = Parse-Zig-Asm -AsmFile $zdAsm -FileName $bench -FuncName $cfg.CFunc
    $zdStats = Count-Instructions $zdLines
    $zfAsm = Join-Path $OutRoot 'zig' $bench 'ReleaseFast' "$bench.s"
    $zfLines = Parse-Zig-Asm -AsmFile $zfAsm -FileName $bench -FuncName $cfg.CFunc
    $zfStats = Count-Instructions $zfLines

    # C /Od
    $odAsm = Join-Path $OutRoot 'c' $bench 'Od' "$bench.asm"
    $odLines = Parse-Msvc-Asm -AsmFile $odAsm -FuncName $cfg.CFunc
    $odStats = Count-Instructions $odLines

    # C /O2
    $o2Asm = Join-Path $OutRoot 'c' $bench 'O2' "$bench.asm"
    $o2Lines = Parse-Msvc-Asm -AsmFile $o2Asm -FuncName $cfg.CFunc
    $o2Stats = Count-Instructions $o2Lines

    # Codex
    $cdxDisasm = Join-Path $OutRoot 'codex' $bench "$bench.disasm"
    $cdxLines = Parse-Codex-Disasm -DisasmFile $cdxDisasm -FuncNames $cfg.CodexFuncs
    $cdxStats = Count-Instructions $cdxLines

    # Stats table
    $cols = @($odStats, $o2Stats, $csStats, $fsStats, $zdStats, $zfStats, $zcdStats, $zcfStats, $cdxStats)
    [void]$out.Add("                   C /Od    C /O2    C# JIT   F# JIT   Zig Dbg  Zig Fast ZigCdxDbg ZigCdxFast Codex")
    foreach ($row in @(@('Instructions:', 'Total'), @('Branches:', 'Branches'), @('Memory ops:', 'MemOps'), @('Moves:', 'Moves'), @('Arithmetic:', 'Arithmetic'))) {
        $line = "  $('{0,-17}' -f $row[0])"
        for ($ci = 0; $ci -lt $cols.Count; $ci++) {
            $w = if ($ci -eq 6) { 10 } elseif ($ci -eq 7) { 11 } else { 9 }
            $fmt = '{0,-' + $w + '}'
            $line += ($fmt -f $cols[$ci][$row[1]])
        }
        [void]$out.Add($line.TrimEnd())
    }
    [void]$out.Add("")

    # Delta vs O2
    if ($o2Stats.Total -gt 0 -and $cdxStats.Total -gt 0) {
        $instrDelta = $cdxStats.Total - $o2Stats.Total
        $instrPct = [math]::Round(($instrDelta / $o2Stats.Total) * 100, 1)
        $memDelta = $cdxStats.MemOps - $o2Stats.MemOps
        $sign = if ($instrDelta -ge 0) { '+' } else { '' }
        $msign = if ($memDelta -ge 0) { '+' } else { '' }
        [void]$out.Add("  vs /O2: ${sign}${instrDelta} instructions (${sign}${instrPct}%), ${msign}${memDelta} memory ops")
    }
    if ($odStats.Total -gt 0 -and $cdxStats.Total -gt 0) {
        $instrDelta = $cdxStats.Total - $odStats.Total
        $instrPct = [math]::Round(($instrDelta / $odStats.Total) * 100, 1)
        $sign = if ($instrDelta -ge 0) { '+' } else { '' }
        [void]$out.Add("  vs /Od: ${sign}${instrDelta} instructions (${sign}${instrPct}%)")
    }
    if ($zfStats.Total -gt 0 -and $cdxStats.Total -gt 0) {
        $instrDelta = $cdxStats.Total - $zfStats.Total
        $instrPct = [math]::Round(($instrDelta / $zfStats.Total) * 100, 1)
        $sign = if ($instrDelta -ge 0) { '+' } else { '' }
        [void]$out.Add("  vs Zig ReleaseFast: ${sign}${instrDelta} instructions (${sign}${instrPct}%)")
    }
    if ($zcfStats.Total -gt 0 -and $zfStats.Total -gt 0) {
        $instrDelta = $zcfStats.Total - $zfStats.Total
        $instrPct = [math]::Round(($instrDelta / $zfStats.Total) * 100, 1)
        $sign = if ($instrDelta -ge 0) { '+' } else { '' }
        [void]$out.Add("  zig plug vs hand-written zig (ReleaseFast): ${sign}${instrDelta} instructions (${sign}${instrPct}%)")
    }
    [void]$out.Add("")

    # Full listings
    [void]$out.Add("--- C /Od ($($cfg.CFunc)) ---")
    foreach ($l in $odLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- C /O2 ($($cfg.CFunc)) ---")
    foreach ($l in $o2Lines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- Zig -O Debug ($($cfg.CFunc)) ---")
    foreach ($l in $zdLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- Zig -O ReleaseFast ($($cfg.CFunc)) ---")
    foreach ($l in $zfLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- C# RyuJIT ($($cfg.CsMethod)) ---")
    foreach ($l in $csLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- F# RyuJIT ($($cfg.FsMethod)) ---")
    foreach ($l in $fsLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- Codex through zig plug, -O Debug ($zcFunc) ---")
    foreach ($l in $zcdLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- Codex through zig plug, -O ReleaseFast ($zcFunc) ---")
    foreach ($l in $zcfLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- Codex ($($cfg.CodexFuncs -join ', ')) ---")
    foreach ($l in $cdxLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")
    [void]$out.Add("")
}

# Write report
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
[System.IO.File]::WriteAllLines($Report, $out.ToArray(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Report: $Report"

# Also dump to stdout
foreach ($l in $out) { Write-Host $l }
