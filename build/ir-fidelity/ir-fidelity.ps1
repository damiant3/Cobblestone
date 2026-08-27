param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string]$CaseDir = '',
    [string]$Only = '',
    [switch]$Passes,  # run WITH the optimizer pipeline instead of -Passes none
    [switch]$Grade    # grade the instrument itself, then run the real cases
)

# Does the IR carry what the checker knew?
#
# Each case is three programs and one wire position:
#
#   a, b     differ in exactly ONE respect and BOTH compile clean
#   knows    a program the checker REFUSES with a named diagnostic, which is
#            what establishes that the checker distinguishes that respect
#   path     the cell on the IR wire to compare between a and b
#
# The verdict follows from those three, and the knows arm is what keeps it
# honest: without it, cells that agree cannot be told apart from a checker that
# never knew the difference either. That is Steve Howell's discriminator (docs/
# Reference/ZigDemandingCustomer_Notes.md) made mechanical -- did the checker
# compute an answer the IR failed to carry, or does the program genuinely not
# constrain one?
#
#   CARRIED       checker knows, cells differ. The wire carried the fact.
#   DROPPED       checker knows, cells agree. The compiler dropped it: upstream.
#   UNCONSTRAINED the knows arm did not refuse, so no claim is made either way.
#   UNSUPPORTED   the reader could not locate the cell. An instrument gap.
#   ERROR         a or b failed to compile, or the wire was absent.
#
# UNCONSTRAINED and UNSUPPORTED are deliberately NOT passes: a skip reported as
# a pass is indistinguishable from a check that asks and agrees
# (L-CAPABILITY-LOST).
#
# The default is -Passes none, which audits the sentence the author WROTE. That
# is the right reading for this question, because what the checker knew is a
# fact about the author's program. -Passes runs the optimizer pipeline instead
# and audits what actually runs; expect UNSUPPORTED there for any case whose
# subject is inlined away, which is honest rather than a gap -- measured
# 2026-08-27, linear-param and effect-row both go UNSUPPORTED under the
# pipeline because `consume` and `f` no longer exist as defs, while
# lambda-param-type still reports DROPPED. A run that does not say which
# reading it took is not interpretable, so both modes print it.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ir-wire.ps1')

# Resolved before anything uses it, -Grade included: the kernel default is
# repo-relative, and -Grade hands it to two child invocations.
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repoRoot $Kernel }

if ($Grade) {
    # An arm whose verdicts have never been shown to fail is not evidence
    # (L-FALSIF). This grades the reader, then runs grade-cases/, which is three
    # ablations aimed at one verdict path each: a knows arm that cannot fire
    # must fall to UNCONSTRAINED even though the cells agree; an unlocatable
    # path must report UNSUPPORTED and not agreement; and a pair read at a cell
    # that cannot carry its respect must report DROPPED.
    Write-Output '--- grading the reader ---'
    & (Join-Path $PSScriptRoot 'ir-wire-selftest.ps1') -Kernel $Kernel
    if ($LASTEXITCODE -ne 0) { Write-Output 'GRADE: reader self-test FAILED'; exit 1 }
    Write-Output ''
    Write-Output '--- grading the verdicts ---'
    & $PSCommandPath -Kernel $Kernel -CaseDir (Join-Path $PSScriptRoot 'grade-cases')
    if ($LASTEXITCODE -ne 0) { Write-Output 'GRADE: a verdict path did not fire'; exit 1 }
    Write-Output ''
    Write-Output '--- the cases ---'
    & $PSCommandPath -Kernel $Kernel
    exit $LASTEXITCODE
}

if (-not $CaseDir) { $CaseDir = Join-Path $PSScriptRoot 'cases' }
# Derived from the script's own location, not the working directory: a fleet
# tool that reads its paths out of the cwd runs somebody else's tree, or none
# (L-SHARED).
$repo = $repoRoot
$work = Join-Path $env:TEMP ("irfid-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Force $work | Out-Null

if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repo $Kernel }
if (-not (Test-Path $Kernel)) { Write-Output "MISSING kernel: $Kernel"; exit 2 }
$kernelHash = (Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0, 16)
Write-Output "kernel: $Kernel [$kernelHash]"
Write-Output "passes: $(if ($Passes) { 'pipeline' } else { 'none' })"
Write-Output ''

$script:compileSeconds = 0.0
$script:compileCount = 0

function Invoke-IrCompile {
    # Returns @{ Wire = <text or $null>; Diags = <string[]>; Seconds = <double> }
    #
    # compile.ps1 in -IrUni mode emits no SIZE: line, so it never writes -Out and
    # always falls through to exit 4 with the whole guest output in -Log. The
    # exit code carries no information here; IR-BEGIN/IR-END and the diagnostic
    # lines do. Gating on the exit code would report every case as a failure.
    param([string]$Src, [string]$Tag)

    $log = Join-Path $work "$Tag.log"
    $out = Join-Path $work "$Tag.ir"
    # Named splatting, not an array: compile.ps1 takes -Src/-Out/-Log
    # positionally too, and an array splat binds -Out's value to -PCore.
    $cargs = @{ Src = $Src; Out = $out; Log = $log; Kernel = $Kernel; IrUni = $true }
    if (-not $Passes) { $cargs['Passes'] = 'none' }

    # compile.ps1 resolves some of its own paths against the working directory,
    # so pin it rather than requiring the caller to have cd'd to the root.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $repo
    try { & (Join-Path $repo 'build\compile.ps1') @cargs 2>&1 | Out-Null }
    finally { Pop-Location }
    $sw.Stop()
    $script:compileSeconds += $sw.Elapsed.TotalSeconds
    $script:compileCount++

    $wire = $null
    try { $wire = Get-IrWireText -LogPath $log } catch { $wire = $null }
    $diags = @()
    if (Test-Path $log) {
        $diags = @(Get-Content $log | Where-Object { $_ -match 'error CDX\d+' })
    }
    return @{ Wire = $wire; Diags = $diags; Seconds = $sw.Elapsed.TotalSeconds }
}

function Get-Cell {
    param([string]$Wire, [string]$Path)
    if (-not $Wire) { return $null }
    $tree = ConvertFrom-IrWire -Text $Wire
    $cell = Get-IrCell -Chapter $tree -Path $Path
    if ($null -eq $cell) { return $null }
    return (Format-IrNode $cell)
}

$cases = @(Get-ChildItem -Path $CaseDir -Filter 'case.psd1' -Recurse | Sort-Object FullName)
if ($Only) { $cases = @($cases | Where-Object { $_.Directory.Name -like $Only }) }
if ($cases.Count -eq 0) { Write-Output "no cases under $CaseDir"; exit 2 }

$rows = @()
$unexpected = 0

foreach ($cf in $cases) {
    $dir = $cf.Directory.FullName
    $name = $cf.Directory.Name
    $c = Import-PowerShellDataFile -LiteralPath $cf.FullName

    $ra = Invoke-IrCompile -Src (Join-Path $dir $c.a) -Tag "$name-a"
    $rb = Invoke-IrCompile -Src (Join-Path $dir $c.b) -Tag "$name-b"

    $verdict = ''
    $evidence = ''

    if (-not $ra.Wire -or -not $rb.Wire) {
        $verdict = 'ERROR'
        $which = if (-not $ra.Wire) { 'a' } else { 'b' }
        $d = if (-not $ra.Wire) { $ra.Diags } else { $rb.Diags }
        $evidence = "arm $which emitted no IR: $($d -join '; ')"
    }
    else {
        $ca = Get-Cell -Wire $ra.Wire -Path $c.path
        $cb = Get-Cell -Wire $rb.Wire -Path $c.path

        if ($null -eq $ca -or $null -eq $cb) {
            $verdict = 'UNSUPPORTED'
            $evidence = "reader could not locate $($c.path)"
        }
        else {
            # The knows arm is what licenses a CARRIED/DROPPED claim at all.
            $knows = $false
            $knowsDetail = 'no knows arm'
            if ($c.ContainsKey('knows')) {
                $rk = Invoke-IrCompile -Src (Join-Path $dir $c.knows) -Tag "$name-knows"
                $hit = @($rk.Diags | Where-Object { $_ -match [regex]::Escape($c.knowsCode) })
                if ($hit.Count -gt 0) {
                    $knows = $true
                    $knowsDetail = $c.knowsCode
                } else {
                    $knowsDetail = "expected $($c.knowsCode), got: $(if ($rk.Diags) { ($rk.Diags -join '; ') } else { 'no diagnostic -- the checker ACCEPTED it' })"
                }
            }

            if (-not $knows) {
                $verdict = 'UNCONSTRAINED'
                $evidence = $knowsDetail
            }
            elseif ($ca -ne $cb) {
                $verdict = 'CARRIED'
                $evidence = "$ca  |  $cb"
            }
            else {
                $verdict = 'DROPPED'
                $evidence = "both arms: $ca"
            }
        }
    }

    $expected = $c.expect
    $flag = if ($verdict -eq $expected) { '   ' } else { $unexpected++; '>>>' }
    Write-Output "$flag $($name.PadRight(22)) $($verdict.PadRight(14)) (expected $expected)"
    Write-Output "      respect: $($c.respect)"
    Write-Output "      $($c.path): $evidence"
    Write-Output ''
    $rows += [pscustomobject]@{ Case = $name; Verdict = $verdict; Expected = $expected }
}

$avg = if ($script:compileCount) { $script:compileSeconds / $script:compileCount } else { 0 }
Write-Output "cost: $($script:compileCount) compiles, $([math]::Round($script:compileSeconds,1)) s total, $([math]::Round($avg,1)) s per compile"
Write-Output "cases: $($rows.Count), unexpected: $unexpected"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if ($unexpected -gt 0) { exit 1 }
exit 0

