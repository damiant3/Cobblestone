# check-compile-drop.ps1 -- compile.ps1 must REFUSE a capture that lost bytes,
# not write the short binary it can still see.
#
# Hand-maintained, like the other check-*.ps1 in this directory.
#
# WHY THIS EXISTS. codex-vm reported dropped guest serial bytes correctly for as
# long as the report existed, and compile.ps1 read that stderr in its two
# FAILURE branches only (no output; output but no SIZE: line) and deleted it in
# the finally. A truncated capture still carries a SIZE: line, so it took the
# path that SUCCEEDS, and the report reached nobody: measured 2026-09-07, a
# 3,995-byte .cdx where the control wrote 93,248, different hash, at EXIT 0 with
# no diagnostic anywhere. A correct detector firing into a closed channel
# (L-UNHEARD), on the one script every lane runs on every change.
#
# It is also the runner for the `blit growth failed` drop cause: a real
# occurrence needs the host out of memory AND 16MB of guest output to reach
# the branch, so it sat live, correct and never once executed.
# CODEX_VM_FAIL_GROW_AT is what produces it. On codex-vm every print goes by
# blit, so a compile never reaches output_buf_write's growth branch; that
# cause's runner is check-run-list.ps1 arm 9.
#
#   pwsh build/check-compile-drop.ps1
#   pwsh build/check-compile-drop.ps1 -Src codex\test\act-let-scope.codex
[CmdletBinding()]
param(
    [string]$Src = 'codex\test\act-let-scope.codex',
    [string]$Kernel = '',
    [int]$Cap = 4096,
    [switch]$KeepWork
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
Set-Location $root
if (-not $Kernel) { $Kernel = Join-Path $root 'seed\Codex.cdx' }
$Kernel = (Resolve-Path $Kernel).Path
Write-Host "kernel: $Kernel [$((Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0,16))]"

$fail = 0
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; $script:fail++ }
function Ok([string]$m)   { Write-Host "  ok  $m" }

$work = Join-Path $root 'build-output\check-compile-drop'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$ctlCdx = Join-Path $work 'ctl.cdx'; $ctlLog = Join-Path $work 'ctl.log'
$injCdx = Join-Path $work 'inj.cdx'; $injLog = Join-Path $work 'inj.log'

# ---- arm 1: the control. A clean compile is unmoved by the refusal above it --
& pwsh -NoProfile -File $compile -Src $Src -Out $ctlCdx -Log $ctlLog -Kernel $Kernel *> $null
$ctlExit = $LASTEXITCODE
$ctlLen = if (Test-Path -PathType Leaf $ctlCdx) { (Get-Item $ctlCdx).Length } else { 0 }
if ($ctlExit -ne 0) { Fail "arm1 the control compile exited $ctlExit; nothing below can be read" }
elseif ($ctlLen -le 0) { Fail 'arm1 the control wrote no binary' }
else { Ok "arm1 the control compiles clean: exit 0, $ctlLen byte(s)" }

# The vacuity guard, and it is not a formality. If the subject's whole capture
# fits under the cap the buffer never fills, no growth is attempted, nothing is
# dropped, and the injected arm below would pass by never reaching the branch --
# a sabotage that changes no colour is the corpus saying it cannot express the
# case, not a passing control (L-VACUOUS, L-CONSTRUCT).
if ($ctlLen -gt 0 -and $ctlLen -le $Cap) {
    Fail "arm2 the control's binary is $ctlLen byte(s), at or under the $Cap-byte cap, so the injection could not fire; raise -Cap's subject or lower -Cap"
}
elseif ($ctlExit -eq 0) {
    # ---- arm 2: the injection. Both growth causes, and the refusal ----------
    $env:CODEX_VM_FAIL_GROW_AT = "$Cap"
    try {
        & pwsh -NoProfile -File $compile -Src $Src -Out $injCdx -Log $injLog -Kernel $Kernel *> $null
        $injExit = $LASTEXITCODE
    } finally { Remove-Item Env:\CODEX_VM_FAIL_GROW_AT -ErrorAction SilentlyContinue }

    $logText = if (Test-Path -PathType Leaf $injLog) { Get-Content $injLog -Raw } else { '' }
    $dropped = [regex]::Matches($logText, ' guest serial byte\(s\) DROPPED').Count

    if ($injExit -eq 0) { Fail 'arm2 the injected compile exited 0 -- a lost capture was accepted' }
    elseif ($injExit -ne 6) { Fail "arm2 the injected compile exited $injExit, not the 6 that names a dropped capture" }
    elseif (Test-Path -PathType Leaf $injCdx) { Fail "arm2 a binary was WRITTEN ($((Get-Item $injCdx).Length) bytes) from a capture known to be short" }
    elseif ($dropped -eq 0) { Fail 'arm2 the log carries no DROPPED line, so the refusal fired on something else' }
    else { Ok "arm2 a dropped capture is refused: exit 6, no binary written, $dropped DROPPED line(s) logged" }

    # ---- arm 3: the blit growth cause is the one that fired -----------------
    # Naming it is the point: a refusal that fired on some other line would
    # leave blit_guest_output's branch as unexercised as it was before.
    if ($logText -match 'blit growth failed') { Ok "arm3 'blit growth failed' fired" }
    else { Fail "arm3 'blit growth failed' did not fire; that cause still has no runner" }
}

if (-not $KeepWork -and $fail -eq 0) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
if ($fail -gt 0) { Write-Host "check-compile-drop: $fail failure(s)" -ForegroundColor Red; exit 1 }
Write-Host 'check-compile-drop: OK'
exit 0
