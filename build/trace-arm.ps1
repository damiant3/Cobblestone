# The -Trace switch must reach codegen, and the stack instrument must narrow.
#
# This arm exists because the capability died of exactly one thing: nothing
# tested it. `trace-alloc` was severed at three points at once -- the mode word
# `build/compile.ps1 -Trace` set was never read, and the emitter's `trace`
# argument was a literal False at both call sites -- so `emit-alloc-trace`,
# `emit-trace-serial-dump` and the per-prologue stack high-water store were
# unreachable for as long as anyone can date, while `-Trace` kept exiting 0 and
# handing back an ordinary binary. Found by val 2026-08-19 (a -Trace desk was
# byte-for-byte the SAME SIZE as an ordinary one), reconnected by root
# 2026-08-20. A dead switch that answers success is worse than a missing one.
#
# Two arms, same source and same kernel, the switch the only difference:
#
#   SIZE     the -Trace binary must DIFFER from the plain one and be LARGER.
#            A per-function-prologue store cannot be free, so equal bytes is
#            the exact reading val took off the broken tree.
#   NARROW   build/trace-probe.codex reports whether stack-min-rsp-addr (28736)
#            has fallen below ram-size. The boot prologue seeds that cell with
#            ram-size unconditionally, and ONLY a trace build emits the store
#            that narrows it -- so plain must answer 0 and trace must answer 1.
#            This is the arm that distinguishes "the flag is wired" from "the
#            instrument reports", which the size check alone cannot do.
#
# The plain arm answering 1 would mean the store is emitted unconditionally and
# every shipping binary pays for it; that is a failure here, not a bonus.
#
#   pwsh build/trace-arm.ps1                              # vs the seed
#   pwsh build/trace-arm.ps1 -Kernel build\output\Sut.cdx # vs a fresh SUT
[CmdletBinding()]
param(
  [string]$Kernel = 'seed\Codex.cdx'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $repo 'build-output\trace-arm'
New-Item -ItemType Directory -Force $out | Out-Null

$kern = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $repo $Kernel }
$src  = Join-Path $repo 'build\trace-probe.codex'
if (-not (Test-Path $kern)) { throw "kernel not found: $kern" }

Write-Host ""
Write-Host "trace-arm: kernel $kern" -ForegroundColor Cyan

function Invoke-TraceArm {
  param([string]$Tag, [switch]$Trace)
  $bin = Join-Path $out "$Tag.cdx"
  $log = Join-Path $out "$Tag.log"
  $res = Join-Path $out "$Tag.out"
  if (Test-Path $bin) { Remove-Item $bin -Force }
  $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  if ($Trace) {
    & (Join-Path $repo 'build\compile.ps1') -Src $src -Out $bin -Log $log -Kernel $kern -Trace 2>&1 | Out-Null
  } else {
    & (Join-Path $repo 'build\compile.ps1') -Src $src -Out $bin -Log $log -Kernel $kern 2>&1 | Out-Null
  }
  $ErrorActionPreference = $prev
  if (-not (Test-Path $bin)) {
    Write-Host "  FAIL: $Tag did not compile (see $log)" -ForegroundColor Red
    return $null
  }
  $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  & (Join-Path $repo 'tools\codex-vm.exe') -kernel $bin -headless -output $res 2>&1 | Out-Null
  $ErrorActionPreference = $prev
  $text = if (Test-Path $res) { ((Get-Content $res -Raw) -replace "`r", '') } else { '' }
  return [pscustomobject]@{ Size = (Get-Item $bin).Length; Text = $text }
}

$failures = 0

$plain = Invoke-TraceArm -Tag 'plain'
$trace = Invoke-TraceArm -Tag 'trace' -Trace
if ($null -eq $plain -or $null -eq $trace) { Write-Host "trace-arm: FAILED (compile)" -ForegroundColor Red; exit 1 }

Write-Host "  plain $($plain.Size) bytes / trace $($trace.Size) bytes"

if ($trace.Size -eq $plain.Size) {
  Write-Host "  FAIL: identical size -- -Trace is not reaching codegen (the 2026-08-19 reading)" -ForegroundColor Red
  $failures++
} elseif ($trace.Size -lt $plain.Size) {
  Write-Host "  FAIL: the -Trace binary is SMALLER; the prologue store cannot shrink a binary" -ForegroundColor Red
  $failures++
} else {
  Write-Host "  ok: -Trace is larger by $($trace.Size - $plain.Size) bytes" -ForegroundColor Green
}

function Get-Narrowed {
  param([string]$Text)
  if ($Text -match 'narrowed:\s*(\d+)') { return [int]$matches[1] }
  return -1
}

$np = Get-Narrowed $plain.Text
$nt = Get-Narrowed $trace.Text

if ($np -lt 0 -or $nt -lt 0) {
  Write-Host "  FAIL: the probe did not report (plain='$($plain.Text.Trim())' trace='$($trace.Text.Trim())')" -ForegroundColor Red
  $failures++
} else {
  if ($np -ne 0) {
    Write-Host "  FAIL: the plain build narrowed the cell -- every shipping binary is paying for the instrument" -ForegroundColor Red
    $failures++
  }
  if ($nt -ne 1) {
    Write-Host "  FAIL: the -Trace build did not narrow stack-min-rsp-addr -- the flag is wired but the instrument is not reporting" -ForegroundColor Red
    $failures++
  }
  if ($np -eq 0 -and $nt -eq 1) {
    Write-Host "  ok: stack-min-rsp-addr narrows under -Trace and only under -Trace" -ForegroundColor Green
  }
}

Write-Host ""
if ($failures -gt 0) { Write-Host "trace-arm: FAILED ($failures)" -ForegroundColor Red; exit 1 }
Write-Host "trace-arm: PASS (-Trace reaches codegen and the stack instrument reports)" -ForegroundColor Green
exit 0
