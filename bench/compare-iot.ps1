# IoT codegen comparison: Codex ARM64/RISC-V vs GCC cross-compilers.
#
# Builds all benchmarks, extracts per-function disassembly, and produces
# a side-by-side instruction-count report.
#
# Usage: bench/compare-iot.ps1 [-SkipBuild]
# Output: bench/build-output/report-iot.txt
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$OutRoot  = Join-Path $BenchDir 'build-output'
$Report   = Join-Path $OutRoot 'report-iot.txt'

# --- Step 1: Build ---
if (-not $SkipBuild) {
    Write-Host "=== Building C cross-compiled benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-c-cross.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "C cross build failed"; exit 1 }

    Write-Host "`n=== Building Codex cross-compiled benchmarks ==="
    & pwsh -NoProfile -File (Join-Path $BenchDir 'build-codex-cross.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host "Codex cross build failed (continuing with partial results)" }
}

# --- Step 2: Instruction Counting ---

function Count-CrossInstructions {
    param([string[]]$Lines, [string]$Arch)
    $stats = @{ Total = 0; Branches = 0; MemOps = 0; Compute = 0; Bytes = 0 }
    foreach ($l in $Lines) {
        if ($l -notmatch '^\s+[0-9a-f]+:\s+([0-9a-f]+)\s+(\S+)') { continue }
        $encoding = $matches[1]
        $mnemonic = $matches[2]
        $stats.Total++
        $stats.Bytes += $encoding.Length / 2

        if ($Arch -eq 'arm64') {
            if ($mnemonic -match '^(b|bl|b\.\w+|cbz|cbnz|tbz|tbnz|ret)$') { $stats.Branches++ }
            elseif ($mnemonic -match '^(ldr\w*|str\w*|ldp|stp|ldur\w*|stur\w*|prfm)$') { $stats.MemOps++ }
            elseif ($mnemonic -match '^(add\w*|sub\w*|mul|madd|msub|[su]div|cmp|cmn|neg|and\w*|orr|eor|orn|bic\w*|lsl|lsr|asr|ror|tst|csinc|csel|cset\w*|cinc|sxt\w|uxt\w|[us]bfm|[us]bfx|adr\w*|mov\w*|mvn|nop)$') { $stats.Compute++ }
        } elseif ($Arch -eq 'riscv') {
            if ($mnemonic -match '^(beq|bne|blt|bge|bltu|bgeu|jal|jalr|j|jr|ret|call|bnez|beqz|blez|bgez|bltz|bgtz)$') { $stats.Branches++ }
            elseif ($mnemonic -match '^(ld|lw|lh|lb|lbu|lhu|lwu|sd|sw|sh|sb)$') { $stats.MemOps++ }
            elseif ($mnemonic -match '^(add\w*|sub\w*|mul\w*|div\w*|rem\w*|and\w*|or\w*|xor\w*|sll\w*|srl\w*|sra\w*|slt\w*|lui|auipc|mv|li|la|neg|not|nop|sext\.\w+|zext\.\w+)$') { $stats.Compute++ }
        }
    }
    return $stats
}

function Parse-ObjdumpFunc {
    param([string]$DisasmFile, [string]$FuncName)
    if (-not (Test-Path $DisasmFile)) { return @() }
    $lines = Get-Content $DisasmFile
    $inFunc = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if ($l -match "^[0-9a-f]+\s+<${FuncName}>:") { $inFunc = $true; continue }
        if ($inFunc -and $l -match '^[0-9a-f]+\s+<\w') { break }
        if ($inFunc -and $l -match '^\s+[0-9a-f]+:') { [void]$result.Add($l) }
    }
    return $result.ToArray()
}

function Parse-CodexFunc {
    param([string]$FuncDisasmFile)
    if (-not (Test-Path $FuncDisasmFile)) { return @() }
    $lines = Get-Content $FuncDisasmFile
    $result = @($lines | Where-Object { $_ -match '^\s+[0-9a-f]+:' })
    return $result
}

# --- Step 3: Generate Report ---

$benchmarks = @('fib', 'fact', 'gcd', 'sum', 'ack', 'tak', 'collatz', 'locals')
$benchConfig = @{
    'fib'     = @{ CFunc = 'fib';     CodexFuncs = @('fib') }
    'fact'    = @{ CFunc = 'fact';    CodexFuncs = @('fact') }
    'gcd'     = @{ CFunc = 'gcd';     CodexFuncs = @('my-gcd') }
    'sum'     = @{ CFunc = 'sum';     CodexFuncs = @('sum-to') }
    'ack'     = @{ CFunc = 'ack';     CodexFuncs = @('ack') }
    'tak'     = @{ CFunc = 'tak';     CodexFuncs = @('tak') }
    'collatz' = @{ CFunc = 'collatz'; CodexFuncs = @('collatz') }
    'locals'  = @{ CFunc = 'compute'; CodexFuncs = @('compute') }
}

$archConfigs = @(
    @{ Tag = 'arm64'; CDir = 'c-arm64'; CodexDir = 'codex-arm64'; Label = 'ARM64 (AArch64)' }
    @{ Tag = 'riscv'; CDir = 'c-riscv'; CodexDir = 'codex-riscv'; Label = 'RISC-V (RV64)' }
)
$opts = @('O0', 'O2', 'Os')

$out = [System.Collections.Generic.List[string]]::new()
[void]$out.Add("Codex IoT Codegen Comparison Report")
[void]$out.Add("=" * 70)
[void]$out.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$out.Add("Compilers: Codex self-hosted, GCC 13.3.0 (cross)")
[void]$out.Add("Metric: static instruction count (lower = tighter codegen)")
[void]$out.Add("")

$summary = [System.Collections.Generic.List[string]]::new()

foreach ($archCfg in $archConfigs) {
    $arch = $archCfg.Tag
    [void]$out.Add("=" * 70)
    [void]$out.Add("  $($archCfg.Label)")
    [void]$out.Add("=" * 70)
    [void]$out.Add("")

    foreach ($bench in $benchmarks) {
        $cfg = $benchConfig[$bench]
        [void]$out.Add("-" * 50)
        [void]$out.Add("  $($bench.ToUpper())")
        [void]$out.Add("-" * 50)

        $allStats = @{}
        $allLines = @{}

        foreach ($opt in $opts) {
            $disasmFile = Join-Path $OutRoot $archCfg.CDir $bench $opt "$bench.disasm"
            $lines = Parse-ObjdumpFunc -DisasmFile $disasmFile -FuncName $cfg.CFunc
            $allLines["GCC -$opt"] = $lines
            $allStats["GCC -$opt"] = Count-CrossInstructions -Lines $lines -Arch $arch
        }

        $codexLines = @()
        foreach ($fn in $cfg.CodexFuncs) {
            $safeFn = $fn -replace '[<>:"/\\|?*]', '_'
            $funcFile = Join-Path $OutRoot $archCfg.CodexDir $bench 'funcs' "$safeFn.disasm"
            $codexLines += Parse-CodexFunc -FuncDisasmFile $funcFile
        }
        $allLines['Codex'] = $codexLines
        $allStats['Codex'] = Count-CrossInstructions -Lines $codexLines -Arch $arch

        # Stats table
        $hdr = "                      "
        foreach ($k in @('GCC -O0', 'GCC -O2', 'GCC -Os', 'Codex')) { $hdr += $k.PadRight(10) }
        [void]$out.Add($hdr)

        foreach ($metric in @('Total', 'Branches', 'MemOps', 'Compute', 'Bytes')) {
            $label = switch ($metric) { 'Total' { 'Instructions' } 'MemOps' { 'Memory ops' } default { $metric } }
            $line = "  $("${label}:".PadRight(20))"
            foreach ($k in @('GCC -O0', 'GCC -O2', 'GCC -Os', 'Codex')) {
                $v = [int]$allStats[$k].$metric
                $line += $(if ($v -gt 0) { "$v" } else { '-' }).PadRight(10)
            }
            [void]$out.Add($line)
        }
        [void]$out.Add("")

        # Delta vs GCC -O2
        $o2Total = [int]$allStats['GCC -O2'].Total
        $cdxTotal = [int]$allStats['Codex'].Total
        $o2Bytes = [int]$allStats['GCC -O2'].Bytes
        $cdxBytes = [int]$allStats['Codex'].Bytes
        if ($o2Total -gt 0 -and $cdxTotal -gt 0) {
            $instrDelta = $cdxTotal - $o2Total
            $instrPct = [math]::Round(($instrDelta / $o2Total) * 100, 1)
            $bytesDelta = $cdxBytes - $o2Bytes
            $bytesPct = if ($o2Bytes -gt 0) { [math]::Round(($bytesDelta / $o2Bytes) * 100, 1) } else { 0 }
            $isign = if ($instrDelta -ge 0) { '+' } else { '' }
            $bsign = if ($bytesDelta -ge 0) { '+' } else { '' }
            [void]$out.Add("  vs GCC -O2: ${isign}${instrDelta} instr (${isign}${instrPct}%), ${bsign}${bytesDelta} bytes (${bsign}${bytesPct}%)")

            $sLine = "  $($bench.PadRight(6)) $($arch.PadRight(8)) $("$o2Total".PadRight(6)) $("$cdxTotal".PadRight(6)) ${isign}${instrPct}%"
            [void]$summary.Add($sLine)
        }
        [void]$out.Add("")

        # Full disassembly listings
        foreach ($k in @('GCC -O0', 'GCC -O2', 'GCC -Os', 'Codex')) {
            $funcLabel = if ($k -eq 'Codex') { $cfg.CodexFuncs -join ', ' } else { $cfg.CFunc }
            [void]$out.Add("--- $k ($funcLabel) ---")
            foreach ($l in $allLines[$k]) { [void]$out.Add("  $l") }
            [void]$out.Add("")
        }
        [void]$out.Add("")
    }
}

# Summary table
[void]$out.Add("=" * 70)
[void]$out.Add("  SUMMARY (vs GCC -O2)")
[void]$out.Add("=" * 70)
[void]$out.Add("  $("Bench".PadRight(6)) $("Arch".PadRight(8)) $("GCC".PadRight(6)) $("Codex".PadRight(6)) Delta")
[void]$out.Add("  " + ("-" * 40))
foreach ($sl in $summary) { [void]$out.Add($sl) }
[void]$out.Add("")

# Write report
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
[System.IO.File]::WriteAllLines($Report, $out.ToArray(), [System.Text.UTF8Encoding]::new($false))

Write-Host "`nReport: $Report"
foreach ($l in $out) { Write-Host $l }
