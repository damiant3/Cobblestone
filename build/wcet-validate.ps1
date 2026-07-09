# Empirical WCET validation: compile a program with punctual functions,
# then observe each function's per-invocation dynamic instruction count
# under codex-vm -wcet (DR0-DR3 entry breakpoints + TF single-step,
# observation only - no guest byte is modified).
#
# Two gates, both hard. The declared contract: observed <= budget
# (CDX6011's bound), machine-verified per invocation. The accounting:
# observed <= static CDX6010 count. The static count is a decode of
# the function's finished bytes (X86_64InsnCount.codex), a superset of
# any dynamic path -- punctual code cannot loop -- so an observation
# above it means the decode or the emitter is lying and the run FAILS.
#
# calls=0 is a WARN: the function ran but its standalone body was never
# entered - the inliner replaced every call site. Coverage gap, not a
# violation. codex-vm arms at most 4 functions per run (one debug
# register each); this harness batches automatically.
#
#   build/wcet-validate.ps1                         # default probe
#   build/wcet-validate.ps1 -Src path/to/prog.codex # any punctual program
#
# Exit 0 = every observed function within budget; exit 1 = budget
# violation or instrumentation failure.
[CmdletBinding()]
param(
    [string]$Src = 'codex/test/wcet-probe.codex',
    [string]$OutDir = 'build/output'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $RepoRoot

$name = [System.IO.Path]::GetFileNameWithoutExtension($Src)
$cdx  = Join-Path $OutDir "$name-wcet.cdx"
$log  = Join-Path $OutDir "$name-wcet.log"
$map  = Join-Path $OutDir "$name-wcet.map"
$out  = Join-Path $OutDir "$name-wcet.out"
$err  = Join-Path $OutDir "$name-wcet.err"

Write-Host "WCET validate: $Src"
& (Join-Path $RepoRoot 'build/compile.ps1') -Src $Src -Out $cdx -Log $log | Out-Null
if (-not (Test-Path $cdx)) { Write-Host "COMPILE FAILED (see $log)"; exit 1 }
if (-not (Test-Path $map)) { Write-Host "NO MAP SIDECAR ($map) - non-repl compile expected"; exit 1 }

# Static claims from the compile log's CDX6010 report lines.
$claims = @{}
foreach ($line in Get-Content $log) {
    if ($line -match "\[WCET\] punctual '(.+?)': (\d+) instructions \((\d+)% of budget (\d+)\)") {
        $claims[$matches[1]] = @{ Static = [int]$matches[2]; Budget = [int]$matches[4] }
    }
}
if ($claims.Count -eq 0) { Write-Host "NO PUNCTUAL FUNCTIONS REPORTED (no CDX6010 in $log)"; exit 1 }
Write-Host "Static claims: $($claims.Count) punctual function(s)"

# Observe under codex-vm -wcet in batches of 4 (one debug register per
# function). Timer off: punctual code is effect-free, and a quiet guest
# keeps single-stepping deterministic.
$observed = @{}
$allFns = @($claims.Keys | Sort-Object)
$prevTimer = $env:CODEX_VM_NO_TIMER
$env:CODEX_VM_NO_TIMER = '1'
for ($b = 0; $b -lt $allFns.Count; $b += 4) {
    $batch = $allFns[$b..([Math]::Min($b + 3, $allFns.Count - 1))]
    $wcetArgs = @()
    foreach ($fn in $batch) { $wcetArgs += @('-wcet', $fn) }
    & tools/codex-vm.exe -kernel $cdx -headless -output $out -map $map @wcetArgs 2>$err | Out-Null
    foreach ($line in Get-Content $err) {
        if ($line -match "^WCET-OBS: (\S+) max=(\d+) calls=(\d+)") {
            $observed[$matches[1]] = @{ Max = [int]$matches[2]; Calls = [int]$matches[3] }
        }
    }
}
$env:CODEX_VM_NO_TIMER = $prevTimer

$fail = 0
"{0,-24} {1,8} {2,8} {3,8} {4,7}  {5}" -f 'function','static','budget','observed','calls','verdict' | Write-Host
foreach ($fn in $allFns) {
    $c = $claims[$fn]
    $verdict = 'PASS'
    $obsMax = '-'; $obsCalls = 0
    if (-not $observed.ContainsKey($fn)) { $verdict = 'FAIL (not instrumented)'; $fail = 1 }
    else {
        $o = $observed[$fn]; $obsMax = $o.Max; $obsCalls = $o.Calls
        if ($o.Calls -eq 0) { $verdict = 'WARN (never entered - inlined at all call sites?)' }
        elseif ($o.Max -gt $c.Budget) { $verdict = 'FAIL (observed > budget)'; $fail = 1 }
        elseif ($o.Max -gt $c.Static) { $verdict = 'FAIL (observed > static; CDX6010 accounting bug)'; $fail = 1 }
    }
    "{0,-24} {1,8} {2,8} {3,8} {4,7}  {5}" -f $fn, $c.Static, $c.Budget, $obsMax, $obsCalls, $verdict | Write-Host
}

if ($fail -eq 0) {
    Write-Host "WCET VALIDATION PASSED ($($claims.Count) function(s))"
    exit 0
}
Write-Host "WCET VALIDATION FAILED (details above; VM stderr in $err)"
exit 1
