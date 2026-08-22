# check-plug-guards.ps1 -- which source-emitting plugs DROP a match guard?
#
# plugs 1.46 found that a guarded match arm was emitted as a bare prong by
# every source plug, so `is Num (n) when n > 100 -> 2` fired on any `Num`.
# The oracle (build/plug-oracle-test.ps1) catches that only for the plugs
# whose RUNTIME is on this box. It does not have to run the program: a
# dropped guard is visible in the emitted source, which is what this reads.
#
# The subject is codex/test/plug-oracle-arith.codex, whose `classify` has two
# arms on one constructor plus a guarded catch-all. Only the guarded arm
# `is Num (n) when n > 100 -> 2` can put a comparison against 100 in the
# emitted classify body, so the body containing 100 is the verdict.
#
# SCOPING THE SEARCH IS THE WHOLE INSTRUMENT, and two ways of widening it
# have each published a wrong number (2026-08-19, 2026-08-20):
#
#   whole file       reports wasm DROPPED: WAT writes (i64.const 100).
#   bare 100         reports fortran KEPT: 100 sits in its prelude.
#   fixed N lines    reported ada KEPT: its Classify emitted `when Tag_Num`
#                    three times with nothing to tell the arms apart, and a
#                    120-line window ran past the function into the main
#                    body, whose own literals answered for it.
#
# So the body ends where the NEXT subject function (`band`) begins, and the
# run is calibrated against known answers before it is believed: -Calibrate
# checks zig and python and ada KEPT, cobol DROPPED, and refuses to report
# anything if those four do not come back as expected.
#
# cobol is a STABLE negative control and that is measured, not lucky: its
# drop is downstream of a missing feature rather than a missing guard. It
# declares a variant parameter as a scalar PIC and erases the constructor,
# so no guard fix can turn it KEPT (plugs 1.46). Do not "fix" the control.
#
# THREE DELIVERIES, not one. Most plugs answer over TCP through
# run-plug.ps1. `csharp` and `wasm` do not (`run-plug.ps1` waits for a reply
# that never comes) and take -Ir on their own run.ps1. `html`, `maui` and
# `winforms` have NO PORT in build/plug-ports.ps1 at all, so run-plug.ps1
# refuses them by name, and their run.ps1 takes -Src only and compiles the
# subject itself.
#
# Usage:
#   build/check-plug-guards.ps1                 # every source-emitting plug
#   build/check-plug-guards.ps1 -Only ruby
#   build/check-plug-guards.ps1 -Calibrate      # the four controls only
#   build/check-plug-guards.ps1 -SkipBuild      # trust the plug CDXs on disk
#
# This is a MEASUREMENT, not a gate. It is wired to nothing, it fails
# nothing, and a DROPPED verdict is a finding for codex/plugs/plugs-backlog.md
# rather than a red build.
#
# KEPT IS EVIDENCE ABOUT `classify` AND NOTHING ELSE, and that blind spot has
# already hidden a real defect. `classify`'s guarded arms are all CONSTRUCTOR
# patterns, so a plug that threads the guard through its constructor arm and
# drops it on its VARIABLE arm reads KEPT here while `band`'s
# `is x when x < 0` still fires on every value. Measured 2026-08-20 on `gtk`,
# where exactly that was true after a first pass.
#
# What caught it was not this script but an invariant worth keeping beside
# it: fixing one plug changes the emitted subject by FOUR LINES, the two
# guard-carrying functions before and after (or sixteen where the emitter
# writes multi-line blocks). gtk changed TWO, which is `classify` moving and
# `band` standing still. So diff the emitted subject before and after and
# count the lines; a count that is not 4 or 16 wants explaining before the
# verdict here is believed.
#
# TAKE THE BASELINE BEFORE YOU EDIT, because that check needs a before. Run
# this against the plug you are about to change and keep the output:
#
#   build/check-plug-guards.ps1 -Only <plug>
#   copy build-output/plug-guards/<plug>/subject.txt <somewhere>   # the BEFORE
#   ... edit the emitter, rebuild the plug ...
#   build/check-plug-guards.ps1 -Only <plug> -SkipBuild
#   compare the two and count the differing lines
#
# Recovering a lost baseline means checking the emitter out at its previous
# revision and re-emitting, which works and costs a build. Taking it first
# costs nothing.
[CmdletBinding()]
param(
    [string]$Only = '',
    [switch]$Calibrate,
    [switch]$SkipBuild,
    [int]$Jobs = 6,
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

$Work = Join-Path $Repo 'build-output\plug-guards'
New-Item -ItemType Directory -Force $Work | Out-Null

# Machine-code and container plugs emit no source to read.
$notSource = @('arm64','riscv','t3isa','elf','pe','img','ptx','spirv','wgsl','evidence','recheck')
# Their own run.ps1, because run-plug.ps1 cannot reach them (see the header).
$ownRunIr  = @('csharp','wasm')
$ownRunSrc = @('html','maui','winforms')

$controls = @{ zig = 'KEPT'; python = 'KEPT'; ada = 'KEPT'; cobol = 'DROPPED' }

$plugs = @(Get-ChildItem (Join-Path $Repo 'codex\plugs') -Directory |
    Where-Object { (Test-Path (Join-Path $_.FullName 'build.ps1')) -and (Test-Path (Join-Path $_.FullName 'run.ps1')) } |
    Where-Object { $notSource -notcontains $_.Name })
if ($Only)     { $plugs = @($plugs | Where-Object { $_.Name -eq $Only }) }
if ($Calibrate){ $plugs = @($plugs | Where-Object { $controls.ContainsKey($_.Name) }) }
if ($plugs.Count -eq 0) { Write-Host "no plugs selected"; exit 2 }

$Subject = Join-Path $Repo 'codex\test\plug-oracle-arith.codex'
$ir = Join-Path $Work 'oracle.ir'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $ir -Log (Join-Path $Work 'oracle.ir.log') -Kernel $Kernel -IrCce -Passes 'text-plug' | Out-Null
if (-not (Test-Path -PathType Leaf $ir)) { Write-Host "FAIL: no IR from $Subject"; exit 2 }
Write-Host "[plug-guards] $($plugs.Count) source-emitting plugs, kernel $(Split-Path $Kernel -Leaf), IR $((Get-Item $ir).Length) bytes"

$block = {
    $p = $_
    $repo = $using:Repo; $work = $using:Work; $ir = $using:ir
    $ownIr = $using:ownRunIr; $ownSrc = $using:ownRunSrc; $skipBuild = $using:SkipBuild
    $name = $p.Name
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null
    if (-not $skipBuild) {
        $b = & pwsh -NoProfile -File (Join-Path $p.FullName 'build.ps1') 2>&1
        $b | Out-File (Join-Path $dir 'build.log') -Encoding utf8
        if ($LASTEXITCODE -ne 0) { return [pscustomobject]@{ Plug = $name; Verdict = 'BUILD-FAIL' } }
    }
    $out = Join-Path $dir 'subject.txt'
    if (Test-Path $out) { Remove-Item $out -Force }
    $log = Join-Path $dir 'run.log'
    try {
        if ($ownIr -contains $name) {
            & pwsh -NoProfile -File (Join-Path $p.FullName 'run.ps1') -Ir $ir -Out $out 2>&1 | Out-File $log -Encoding utf8
        } elseif ($ownSrc -contains $name) {
            & pwsh -NoProfile -File (Join-Path $p.FullName 'run.ps1') -Src (Join-Path $repo 'codex\test\plug-oracle-arith.codex') -Out $out 2>&1 | Out-File $log -Encoding utf8
        } else {
            $cdx = Join-Path $p.FullName "build-output\$name-plug.cdx"
            & pwsh -NoProfile -File (Join-Path $repo 'build\run-plug.ps1') -Plug $cdx -InFile $ir -Output $out -TimeoutSec 180 2>&1 | Out-File $log -Encoding utf8
        }
    } catch { }
    if (-not (Test-Path -PathType Leaf $out)) { return [pscustomobject]@{ Plug = $name; Verdict = 'NO-OUTPUT' } }
    $lines = [IO.File]::ReadAllText($out) -split "`r?`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match 'classify') { $start = $i; break } }
    if ($start -lt 0) { return [pscustomobject]@{ Plug = $name; Verdict = 'NO-CLASSIFY' } }
    $end = -1
    for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match 'band') { $end = $i - 1; break } }
    if ($end -lt 0) { return [pscustomobject]@{ Plug = $name; Verdict = 'NO-BAND' } }
    $body = ($lines[$start..$end] -join "`n")
    [pscustomobject]@{ Plug = $name; Verdict = $(if ($body -match '100') { 'KEPT' } else { 'DROPPED' }) }
}

$res = @($plugs | ForEach-Object -ThrottleLimit $Jobs -Parallel $block) | Sort-Object Plug

$bad = @()
foreach ($c in $controls.Keys) {
    $row = $res | Where-Object { $_.Plug -eq $c }
    if ($row -and $row.Verdict -ne $controls[$c]) { $bad += "$c read $($row.Verdict), expected $($controls[$c])" }
}
if ($bad.Count -gt 0) {
    Write-Host "CONTROLS FAILED -- the instrument is wrong, not the plugs:" -ForegroundColor Red
    $bad | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

foreach ($v in @('KEPT','DROPPED','NO-OUTPUT','NO-CLASSIFY','NO-BAND','BUILD-FAIL')) {
    $n = @($res | Where-Object { $_.Verdict -eq $v })
    if ($n.Count -gt 0) { Write-Host ("{0,-11} {1,3}  {2}" -f $v, $n.Count, (($n.Plug) -join ' ')) }
}
$res | Export-Csv (Join-Path $Work 'guards.csv') -NoTypeInformation
Write-Host "[plug-guards] $((Join-Path $Work 'guards.csv'))"
exit 0
