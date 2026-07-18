# SPIR-V text plug check. Since the unify, the text plug emits a disassembly
# of the SAME validated word stream the binary plug produces (SpirvBinary
# spv-disasm) -- one IR walk, not two. Ids are numeric (%3 = %f64, %2 = %i64,
# %1 = %bool, the reserved scalar-type ids). These checks confirm the
# disassembly renders the invariants the old symbolic emitter used to assert:
# float ops carry the float result type, constants stay at module scope, and
# no id is used without being defined.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Probe = Join-Path $PSScriptRoot 'test\spirv-probe.codex'
$Out   = Join-Path $PSScriptRoot 'build-output\spirv-probe.spvasm'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $Probe -Out $Out
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: plug run failed"; exit 1 }

$spv   = Get-Content $Out -Raw
$lines = Get-Content $Out
$failed = $false

# Reserved scalar type ids: %1 bool, %2 i64, %3 f64. A float op yields f64, a
# float parameter and a phi joining floats are f64, a signed compare is bool,
# a call returning Real is f64.
$required = @(
    'OpFunctionParameter %3',
    'OpFMul %3',
    'OpFAdd %3',
    'OpPhi %3',
    'OpSGreaterThan %1',
    'OpFunctionCall %3'
)
foreach ($r in $required) {
    if (-not $spv.Contains($r)) { Write-Host "FAIL: missing '$r'"; $failed = $true }
}

# A float op must never carry the i64 result type (%2) -- the defect the old
# emitter produced for every float operation.
foreach ($bad in @('OpFMul %2','OpFSub %2','OpFAdd %2','OpFDiv %2','OpFNegate %2')) {
    if ($spv.Contains($bad)) { Write-Host "FAIL: float op with integer result type: '$bad'"; $failed = $true }
}

# A constant is a module-scope instruction. None may appear after the first
# function begins.
$firstFunc = ($lines | Select-String -Pattern '= OpFunction ' | Select-Object -First 1).LineNumber
if ($firstFunc) {
    $inBody = $lines[$firstFunc..($lines.Count - 1)] | Where-Object { $_ -match 'OpConstant' }
    if ($inBody) {
        Write-Host "FAIL: OpConstant inside a function body (must be module scope):"
        $inBody | ForEach-Object { Write-Host "  $_" }
        $failed = $true
    }
}

# Every id used in the disassembly is defined in it. The words are already
# validated by spv-validate; this is a cross-check that the rendering did not
# invent or drop an id.
$defined = @{}
foreach ($l in $lines) {
    if ($l -match '^\s*(%\d+)\s*=') { $defined[$Matches[1]] = $true }
}
$used = @{}
foreach ($l in $lines) {
    $rhs = if ($l -match '^\s*%\d+\s*=\s*(.*)$') { $Matches[1] } else { $l }
    foreach ($m in [regex]::Matches($rhs, '%\d+')) { $used[$m.Value] = $true }
}
$dangling = $used.Keys | Where-Object { -not $defined.ContainsKey($_) } | Sort-Object
if ($dangling) {
    Write-Host "FAIL: ids used but never defined: $($dangling -join ', ')"
    $failed = $true
}

if ($failed) { Write-Host "SPIRV: FAIL"; exit 1 }
Write-Host "SPIRV: PASS (disassembly types consistent, constants at module scope, no dangling ids)"
exit 0
