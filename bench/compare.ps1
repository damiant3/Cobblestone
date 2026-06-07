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

    Write-Host "`n=== Building Codex benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-codex.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "Codex build failed"; exit 1 }

    Write-Host "`n=== Extracting Codex disassembly ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'disasm-cdx.ps1')
}

# --- Step 2: Parse and Compare ---
$benchmarks = @('fib', 'fact', 'gcd', 'sum')
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
    'fib'  = @{ CFunc = 'fib';  CodexFuncs = @('fib') }
    'fact' = @{ CFunc = 'fact'; CodexFuncs = @('fact') }
    'gcd'  = @{ CFunc = 'gcd';  CodexFuncs = @('my-gcd') }
    'sum'  = @{ CFunc = 'sum';  CodexFuncs = @('sum-to') }
}

[void]$out.Add("Codex vs C Codegen Comparison Report")
[void]$out.Add("=" * 60)
[void]$out.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
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
    $match = if ($cResult -eq $codexResult) { 'PASS' } else { 'MISMATCH' }
    [void]$out.Add("Correctness: C=$cResult  Codex=$codexResult  $match")
    [void]$out.Add("")

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
    [void]$out.Add("                   C /Od    C /O2    Codex")
    [void]$out.Add("  Instructions:    $('{0,-9}' -f $odStats.Total)$('{0,-9}' -f $o2Stats.Total)$($cdxStats.Total)")
    [void]$out.Add("  Branches:        $('{0,-9}' -f $odStats.Branches)$('{0,-9}' -f $o2Stats.Branches)$($cdxStats.Branches)")
    [void]$out.Add("  Memory ops:      $('{0,-9}' -f $odStats.MemOps)$('{0,-9}' -f $o2Stats.MemOps)$($cdxStats.MemOps)")
    [void]$out.Add("  Moves:           $('{0,-9}' -f $odStats.Moves)$('{0,-9}' -f $o2Stats.Moves)$($cdxStats.Moves)")
    [void]$out.Add("  Arithmetic:      $('{0,-9}' -f $odStats.Arithmetic)$('{0,-9}' -f $o2Stats.Arithmetic)$($cdxStats.Arithmetic)")
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
    [void]$out.Add("")

    # Full listings
    [void]$out.Add("--- C /Od ($($cfg.CFunc)) ---")
    foreach ($l in $odLines) { [void]$out.Add("  $l") }
    [void]$out.Add("")

    [void]$out.Add("--- C /O2 ($($cfg.CFunc)) ---")
    foreach ($l in $o2Lines) { [void]$out.Add("  $l") }
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
