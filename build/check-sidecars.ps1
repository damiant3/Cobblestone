# check-sidecars.ps1 -- A sidecar names a test. This asserts the test exists.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$TestRoot = (Join-Path (Split-Path $PSScriptRoot) 'codex/test')
)

# A sidecar names a test. This asserts the test exists.
# 
# test.ps1 resolves every sidecar next to its source: for codex/test/apps/
# foo.codex it looks for codex/test/apps/foo.skip, and a foo.skip one
# directory up is read by nothing. Such a file is not inert -- it reads like
# a decision. diagnostic-boot carried a .skip in the wrong directory saying
# "blocks waiting for keyboard input"; the skip applied to nothing, the test
# was compiled but never run because it had no .expected, and the reason was
# wrong anyway (the shell reads serial, not the keyboard). Three claims, none
# true, and the skip inventory in docs/ExaminersAssay.md quoted it as a
# legitimate skip for months.
# 
# db-mini-test.skip was the same shape with no test at all behind it: the
# only record of a real CDX2000 defect was a sidecar for a .codex that has
# never existed in main. That gap is recorded in docs/ExaminersAssay.md,
# named by the chapter it is actually in.
# 
# Exit 1 on any sidecar whose .codex sibling is missing.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exts = @('.skip', '.slow', '.fatal', '.expected', '.stdin', '.keys', '.disk', '.disk2', '.disk-src', '.failing', '.diag', '.smp', '.vmargs', '.no-cross', '.cross-refusal', '.cross-budget')
$orphans = @()
$checked = 0
$noeol = @()
$expChecked = 0


$allFiles = @(Get-ChildItem $TestRoot -Recurse -Filter '*' -File)
foreach ($ext in $exts) {
    foreach ($f in ($allFiles | Where-Object { ($_.Extension -eq $ext) })) {
        $checked++
        $sibling = (Join-Path $f.DirectoryName ([string]$f.BaseName + '.codex'))
        if ((-not (Test-Path -PathType Leaf $sibling))) {
            $orphans += $f.FullName.Substring(((Split-Path $PSScriptRoot).Length + 1))
        }
    }
}


# A .disk-src names a SECOND test -- the one whose freshly compiled CDX becomes
# this test's disk -- so it has a second way to point at nothing. A typo there
# does not fail until a battery run, which is the expensive place to find it.

foreach ($f in Get-ChildItem $TestRoot -Recurse -Filter '*.disk-src' -File) {
    $peerRaw = (Get-Content $f.FullName -First 1)
    $peer = $(if ($peerRaw) { $peerRaw.Trim() } else { '' })
    if (($peer -eq '')) {
        $orphans += ([string]$f.Name + ' names no test')
        continue
    }
    if ((@((Get-ChildItem $TestRoot -Recurse -Filter '*.codex' -File | Where-Object { ($_.Name -eq ([string]$peer + '.codex')) })).Count -eq 0)) {
        $orphans += ([string]([string]([string]([string]$f.Name + ' names ''') + $peer) + ''', and no ') + ([string]$peer + '.codex exists'))
    }
}


# An .expected with no trailing newline can never pass. test-run.ps1 writes
# ($lines -join "`n") + "`n", so the actual always ends in one LF, and
# test.ps1 strips CR from the expected side only. LF is not one of the
# characters the culture-sensitive -eq ignores (SOH and NUL are, which is a
# separate and live hazard), so the mismatch is real and the test is simply
# unpassable. Three shipped that way in CL 15313 and sat red until a release
# battery found them: the failure is loud, but something has to be listening.
# Account in docs/ExaminersAssay.md, under the .expected recording rule.

foreach ($f in Get-ChildItem $TestRoot -Recurse -Filter '*.expected' -File) {
    $expChecked++
    $txt = (Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue)
    if ((($txt.Length -gt 0) -and ($txt.Substring(($txt.Length - 1)) -ne "`n"))) {
        $noeol += $f.FullName.Substring(((Split-Path $PSScriptRoot).Length + 1))
    }
}


if ((@($orphans).Count -gt 0)) {
    Write-Host ([string]([string]'FAIL: ' + @($orphans).Count) + ' orphaned sidecar(s) -- no .codex beside them:')
    foreach ($o in $orphans) {
        Write-Host ([string]'  ' + $o)
    }
    Write-Host 'A sidecar next to no test configures nothing and asserts something.'
    Write-Host 'Move it beside its .codex, or delete it -- but if it records a real'
    Write-Host 'defect, write the defect down beside the code it describes first.'
    exit 1
}
if ((@($noeol).Count -gt 0)) {
    Write-Host ([string]([string]'FAIL: ' + @($noeol).Count) + ' .expected file(s) with no trailing newline -- they cannot pass:')
    foreach ($o in $noeol) {
        Write-Host ([string]'  ' + $o)
    }
    Write-Host 'test-run.ps1 always terminates the actual with one LF, so a sidecar'
    Write-Host 'without one can never match. Re-record it through build/test-run.ps1'
    Write-Host 'rather than appending a byte by hand -- the same rule catches the CR,'
    Write-Host 'the HEAP: lines and the leading SOH.'
    exit 1
}
Write-Host ([string]([string]([string]([string]'sidecars ok (' + $checked) + ' checked, 0 orphaned; ') + $expChecked) + ' .expected, 0 without a trailing newline)')
exit 0
