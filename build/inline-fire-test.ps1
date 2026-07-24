# Does the single-caller inliner FIRE, and does it decline what it must?
#
# The battery cannot answer this. A correct optimizer
# changes no answer, so codex/test/inline-single-caller.expected reads exactly
# the same whether the pass ran or not -- it pins that the pass does not
# MISCOMPILE, and nothing more. Firing is visible in one place: the emitted
# symbol map. An inlined definition loses its only reference, whole-program
# dead-code elimination drops it at emit, and its name is gone from the map.
#
# So this harness compiles ONE subject TWICE with the same compiler, once
# without the pass and once with it, and reads the two maps.
#
#   isc-pick   single caller, body has an `if`  -> must vanish when on
#   isc-four   single caller, four parameters   -> must vanish when on
#   isc-add-g  single caller, names a global    -> must vanish when on
#   isc-add-h  same body, but the call site has a local of that global's
#              name in scope                    -> must SURVIVE when on
#
# The last row is the point. Three vanishing names prove the pass fires;
# without a name that must survive alongside them, a pass that inlined
# indiscriminately -- capture and all -- would look identical here. And the
# pass-off run is the other half: every name must be present there, or the
# map is not reporting what we think it is and the absences mean nothing.
#
#   pwsh build/inline-fire-test.ps1
#   pwsh build/inline-fire-test.ps1 -Kernel build/output/Sut.cdx
#
# The kernel must be a compiler that has the pass in its default pipeline.
# Against an older seed every name survives both runs and the harness reports
# the pass as not firing, which is then true rather than a harness fault.
[CmdletBinding()]
param(
    [string]$Kernel = 'seed/Codex.cdx',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo    = Split-Path $PSScriptRoot -Parent
$subject = Join-Path $repo 'codex/test/inline-single-caller.codex'
$outDir  = Join-Path $repo 'build-output'
$fails   = 0

if (-not (Test-Path $subject)) { Write-Host "MISSING subject: $subject"; exit 1 }
if (-not (Test-Path (Join-Path $repo $Kernel))) { Write-Host "MISSING kernel: $Kernel"; exit 1 }

function Compile-Subject([string]$tag, [string]$passes) {
    $out = Join-Path $outDir "isc-$tag.cdx"
    $log = Join-Path $outDir "isc-$tag.log"
    # Hashtable splat, not an array: an array splats POSITIONALLY, so
    # '-Src' itself lands in $Src and every argument shifts by one. And the
    # variable must not be called $args, which is automatic.
    $cargs = @{ Src = $subject; Out = $out; Log = $log; Kernel = $Kernel }
    if ($passes -ne '') { $cargs.Passes = $passes }
    & (Join-Path $PSScriptRoot 'compile.ps1') @cargs *> $null
    $map = Join-Path $outDir "isc-$tag.map"
    if (-not (Test-Path $map)) { Write-Host "FAIL [$tag] no symbol map emitted"; return $null }
    return (Get-Content $map -Raw)
}

# The pass is in default-ir-pipeline, so the OFF leg has to take it back
# out. `passes=a,b` REPLACES the default (only `+name` adds), so naming the
# other two is how you get a pipeline without this one. The ON leg passes
# nothing and takes the default -- which means these two runs together also
# pin that the default still CONTAINS the pass. Reverse them and the harness
# silently tests nothing, which is what it did while the pass was opt-in and
# the default was the off case.
Write-Host "kernel: $Kernel"
$mapOff = Compile-Subject 'off' 'fold-constants,inline-leaf-calls'
$mapOn  = Compile-Subject 'on'  ''
if ($null -eq $mapOff -or $null -eq $mapOn) { exit 1 }

# A name is "in the map" only as a whole symbol. isc-add-g is a prefix of
# nothing here, but matching loosely is how a harness starts lying later.
function Has-Symbol([string]$map, [string]$name) {
    return [bool]([regex]::IsMatch($map, "(?m)\s$([regex]::Escape($name))\s*$"))
}

$mustVanish = @('isc-pick', 'isc-four', 'isc-add-g')
$mustSurvive = @('isc-add-h')

# Control first: with the pass off, every helper must be present. If this
# fails the map is not reporting these symbols and no absence below means
# anything at all.
foreach ($n in ($mustVanish + $mustSurvive)) {
    if (Has-Symbol $mapOff $n) {
        Write-Host "  ok   [off] $n present"
    } else {
        Write-Host "  FAIL [off] $n absent with the pass OFF -- the map cannot see it, so the ON run proves nothing"
        $fails++
    }
}

foreach ($n in $mustVanish) {
    if (Has-Symbol $mapOn $n) {
        Write-Host "  FAIL [on]  $n still present -- single-caller inlining did not fire"
        $fails++
    } else {
        Write-Host "  ok   [on]  $n inlined and dropped"
    }
}

foreach ($n in $mustSurvive) {
    if (Has-Symbol $mapOn $n) {
        Write-Host "  ok   [on]  $n declined (its free name is shadowed at the call site)"
    } else {
        Write-Host "  FAIL [on]  $n was inlined into a site that shadows its free name -- capture"
        $fails++
    }
}

# The answers still have to be right. A pass that fired and computed the
# wrong thing would satisfy every check above.
$expected = Join-Path $repo 'codex/test/inline-single-caller.expected'
if (Test-Path $expected) {
    $runOut = Join-Path $outDir 'isc-on.out'
    & (Join-Path $repo 'tools/codex-vm.exe') -kernel (Join-Path $outDir 'isc-on.cdx') -headless -output $runOut *> $null
    $exp = ([System.IO.File]::ReadAllText($expected)).Replace("`r", "")
    $act = [System.IO.File]::ReadAllText($runOut)
    if ($exp -ceq $act) {
        Write-Host "  ok   [on]  runtime output matches .expected"
    } else {
        Write-Host "  FAIL [on]  runtime output differs from .expected"
        Write-Host "expected: $exp"
        Write-Host "actual  : $act"
        $fails++
    }
} else {
    Write-Host "  FAIL no .expected beside the subject"
    $fails++
}

if (-not $KeepArtifacts) {
    Remove-Item (Join-Path $outDir 'isc-off.cdx') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'isc-on.cdx') -ErrorAction SilentlyContinue
}

if ($fails -gt 0) { Write-Host "inline-fire-test: $fails FAILED"; exit 1 }
Write-Host "inline-fire-test: PASS"
exit 0
