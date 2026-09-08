# check-tools.ps1 -- every tools/*.codex is named by at least one build script.
#
# Hand-maintained, like the other check-*.ps1 in this directory.
#
# WHAT THIS CHECK CANNOT SEE, stated first because its PASS is the thing most
# likely to be misread. It does NOT compile anything, so it cannot observe a
# tool that has drifted out of type with a chapter it cites. That is exactly
# what happened to tools/ota-fetch.codex, which sat at head with two CDX2001
# type errors while THIS check would have called it covered: build/
# ota-fetch-test.ps1 names it, and nothing runs build/ota-fetch-test.ps1,
# because it wants a real socket. Referenced and compiled are different
# claims, and a green line below is only the first one (L-NOGATE, L-REQUEST).
#
# WHAT IT DOES SEE, and it is worth seeing: a tool no script names at all has
# nobody who could compile it even in principle, and it is invisible to every
# interop script, sweep and release. Two such tools existed when this was
# written. A new tool added without a runner is the drift this catches on the
# day it lands rather than months later.
#
# The real repair for the compile question is a sweep that COMPILES the tools,
# which needs a guest apiece and therefore belongs to the battery, not here.

[CmdletBinding()]
param(
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$toolsDir = Join-Path $root 'tools'
$buildDir = Join-Path $root 'build'

if (-not (Test-Path -PathType Container $toolsDir)) {
    Write-Host "REFUSED: no tools directory at $toolsDir" -ForegroundColor Red
    exit 1
}

$tools = @(Get-ChildItem -Path $toolsDir -Filter '*.codex' -File | Sort-Object Name)
if ($tools.Count -eq 0) {
    # An empty subject list compares clean against anything, so refuse rather
    # than report a green over nothing.
    Write-Host "REFUSED: no .codex found under tools/" -ForegroundColor Red
    exit 1
}

# This file names several tools in its own header, so counting itself would
# report coverage it invented.
$scripts = @(Get-ChildItem -Path $buildDir -Recurse -Filter '*.ps1' -File |
             Where-Object { $_.Name -ne 'check-tools.ps1' })
if ($scripts.Count -eq 0) {
    Write-Host "REFUSED: no .ps1 found under build/" -ForegroundColor Red
    exit 1
}

# Read each script once. Select-String over the file set per tool re-reads
# every script 14 times and the cost shows on a cold cache.
$blobs = @{}
foreach ($s in $scripts) { $blobs[$s.Name] = [System.IO.File]::ReadAllText($s.FullName) }

$fail = 0
foreach ($t in $tools) {
    # Match the STEM, not the filename. A script can reach a tool through its
    # built artifact (build/output/cdx-sign.cdx) and never name the source,
    # and asking only for '<name>.codex' calls such a tool uncovered. The
    # narrower pattern was tried first and answered wrong twice.
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($t.Name)
    $named = @()
    foreach ($name in $blobs.Keys) {
        # Plain substring, not a regex: a tool name carries '.' and '-', and
        # escaping the pattern while also asking for a literal match is the
        # mistake that answers zero for text that is present.
        if ($blobs[$name].Contains($stem)) { $named += $name }
    }
    if ($named.Count -eq 0) {
        Write-Host "FAIL: tools/$($t.Name) is named by no build script -- nothing can compile it" -ForegroundColor Red
        $fail++
    } elseif (-not $Quiet) {
        Write-Host "ok:   tools/$($t.Name) <- $(($named | Sort-Object) -join ', ')"
    }
}

if ($fail -gt 0) {
    Write-Host ""
    Write-Host "check-tools: $fail of $($tools.Count) tools have no runner." -ForegroundColor Red
    Write-Host "Give the tool a build script that compiles it, or delete the tool."
    exit 1
}

Write-Host "check-tools: $($tools.Count) tools, each named by a build script (NOT a compile check -- see the header)."
exit 0
