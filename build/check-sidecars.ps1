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
Write-Host ([string]([string]'sidecars ok (' + $checked) + ' checked, 0 orphaned)')
exit 0
