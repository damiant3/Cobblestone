# check-text-plug-passes.ps1 -- the text-plug IR pipeline must preserve calls.
#
# A source-emitting plug resolves a Codex call by its NAME: the browser
# primitives are stubs (set-render (fn) = 0) whose real bodies are the plug's
# own runtime functions. The leaf inliner substitutes such a body and deletes
# the call, and the primitive silently becomes a no-op in the emitted page --
# no error, no warning, a page that builds clean and half-works.
#
# This compiles a fixture whose three shapes are exactly the ones the inliner
# takes and asserts each call survives IR emission under passes=text-plug. It
# is an instrument, not a gate: nothing runs it automatically.
#
#   pwsh build\check-text-plug-passes.ps1
#
# Exit 0 all preserved, 1 a call was erased, 2 the probe could not run.
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'codex-text-plug-check'),
    # Omitted, compile.ps1 picks the build-output kernel, which is what the plug
    # runners get too. Pass seed\Codex.cdx or build\output\Sut.cdx to aim it.
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir | Out-Null }

# Each def is a shape the leaf inliner accepts: a literal body, a body that is
# one parameter, and pure integer arithmetic over a parameter. Every call site
# passes a name or an integer literal, which is what qualifies a site.
$fixture = @'
Chapter: TextPlugPasses

 We say:

Section: Probe

  prim-literal : Integer -> Integer
  prim-literal (x) = 0

  prim-param : Integer, Integer -> Integer
  prim-param (k) (v) = v

  prim-arith : Integer -> Integer
  prim-arith (x) = x + 1

  arg-name : Integer = 7

  opening : [Console] Integer
  opening = act
    let a = prim-literal arg-name
    in let b = prim-param arg-name 42
    in let c = prim-arith arg-name
    in a + b + c
  end
'@

$src = Join-Path $WorkDir 'TextPlugPasses.codex'
[System.IO.File]::WriteAllText($src, $fixture)

# -IrUni dumps the IR into the log; the .ir file itself is not written.
function Get-ProbeIr($tag, $passes) {
    $log = Join-Path $WorkDir "TextPlugPasses-$tag.log"
    $out = Join-Path $WorkDir "TextPlugPasses-$tag.ir"
    $args = @('-Src', $src, '-Out', $out, '-Log', $log, '-IrUni')
    if ($passes) { $args += @('-Passes', $passes) }
    if ($Kernel) { $args += @('-Kernel', $Kernel) }
    # compile.ps1 names the kernel it chose on stdout. Which compiler answered
    # is half the result: with no -Kernel it takes whatever build-output holds,
    # which is not the depot seed and goes stale after a merge-down.
    $stdout = & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') @args 2>&1
    $k = [regex]::Match(($stdout -join "`n"), 'kernel: ([^\r\n]+)')
    $script:KernelUsed = if ($k.Success) { $k.Groups[1].Value.Trim() } else { 'unknown' }
    if (-not (Test-Path $log)) { return $null }
    $t = Get-Content -Raw $log
    if ($t -notmatch 'def "opening"') { return $null }
    return @{ text = $t; log = $log }
}
function Test-Survives($irText, $name) {
    return ($irText -match ('\(apply \(name "' + [regex]::Escape($name) + '"'))
}

# The CONTROL arm. An unknown pass name is the identity transform, so a
# compiler that has never heard of text-plug runs NO passes and preserves
# every call -- passing this check for the opposite of the right reason.
# The default pipeline must therefore be shown to erase these same calls
# first, or the subject arm is measuring nothing.
$control = Get-ProbeIr 'control' $null
if ($null -eq $control) { Write-Host "FAIL: control compile produced no IR" -ForegroundColor Red; exit 2 }
$erased = @(@('prim-literal', 'prim-arith') | Where-Object { -not (Test-Survives $control.text $_) })
if ($erased.Count -lt 2) {
    Write-Host ""
    Write-Host "INCONCLUSIVE: the default pipeline did not erase the probe's calls," -ForegroundColor Yellow
    Write-Host "  so this check cannot tell a working text-plug pipeline from an" -ForegroundColor Yellow
    Write-Host "  unrecognised pass name. The inliner's shapes may have changed;" -ForegroundColor Yellow
    Write-Host "  re-derive the fixture against Lowering.codex 'Leaf Inlining'." -ForegroundColor Yellow
    Write-Host "  IR: $($control.log)"
    Write-Host ""
    exit 2
}

$subject = Get-ProbeIr 'text-plug' 'text-plug'
if ($null -eq $subject) { Write-Host "FAIL: text-plug compile produced no IR" -ForegroundColor Red; exit 2 }
$irText = $subject.text
$log = $subject.log

# An unknown pass name is the identity transform, so a compiler that does not
# resolve text-plug preserves every call while running nothing at all. CDX4030
# reports the pipeline that actually ran, which is what tells the two apart.
$reported = [regex]::Match($irText, 'CDX4030: PIPELINE ([^\r\n]*)')
if (-not $reported.Success) {
    Write-Host "FAIL: no CDX4030 pipeline report; cannot tell which passes ran" -ForegroundColor Red
    Write-Host "      IR: $log"
    exit 2
}
$ran = $reported.Groups[1].Value.Trim()
if ($ran -ne 'fold-constants') {
    Write-Host ""
    Write-Host "FAIL: passes=text-plug resolved to '$ran', not 'fold-constants'." -ForegroundColor Red
    if ($ran -match 'text-plug') {
        Write-Host "      The name was passed through as a pass rather than resolved, so this" -ForegroundColor Red
        Write-Host "      compiler predates pipeline-of knowing it. An unknown pass is the" -ForegroundColor Red
        Write-Host "      identity transform: calls survive here by accident and NO pass ran." -ForegroundColor Red
        Write-Host "      Either the seed predates the change, or build-output holds a stale" -ForegroundColor Red
        Write-Host "      kernel -- build-output is not the depot seed. Kernel that answered:" -ForegroundColor Red
        Write-Host "        $script:KernelUsed" -ForegroundColor Red
    }
    Write-Host "      IR: $log"
    Write-Host ""
    exit 1
}

# The whole question: does a call to each primitive survive as an apply?
$missing = @(@('prim-literal', 'prim-param', 'prim-arith') | Where-Object { -not (Test-Survives $irText $_) })

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "FAIL: the text-plug pipeline erased $($missing.Count) of 3 calls" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "        $m -- inlined away, a plug would never see it" -ForegroundColor Red }
    Write-Host "      Check text-plug-ir-pipeline in codex/compiler/IR/Passes.codex" -ForegroundColor Red
    Write-Host "      and that pipeline-of still resolves passes=text-plug (opening.codex)."
    Write-Host "      IR: $log"
    Write-Host ""
    exit 1
}

Write-Host "OK: default erased $($erased.Count), text-plug preserved 3 of 3 [$script:KernelUsed]" -ForegroundColor Green
exit 0
