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
# never existed in main. That gap now lives in docs/PM/BACKLOG.md, named by
# the chapter it is actually in.
#
# Exit 1 on any sidecar whose .codex sibling is missing.
[CmdletBinding()]
param(
    [string]$TestRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'codex/test')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exts = @('.skip', '.slow', '.fatal', '.expected', '.stdin', '.keys',
          '.disk', '.failing', '.diag', '.smp')

$orphans = [System.Collections.Generic.List[string]]::new()
$checked = 0

foreach ($ext in $exts) {
    Get-ChildItem -Recurse -Path $TestRoot -Filter "*$ext" -File | ForEach-Object {
        $checked++
        $sibling = Join-Path $_.DirectoryName ($_.BaseName + '.codex')
        if (-not (Test-Path -PathType Leaf $sibling)) {
            $orphans.Add($_.FullName.Substring((Split-Path -Parent $PSScriptRoot).Length + 1))
        }
    }
}

if ($orphans.Count -gt 0) {
    Write-Host "FAIL: $($orphans.Count) orphaned sidecar(s) -- no .codex beside them:"
    foreach ($o in $orphans) { Write-Host "  $o" }
    Write-Host 'A sidecar next to no test configures nothing and asserts something.'
    Write-Host 'Move it beside its .codex, or delete it -- but if it records a real'
    Write-Host 'defect, put the defect in docs/PM/BACKLOG.md first.'
    exit 1
}

Write-Host "sidecars ok ($checked checked, 0 orphaned)"
exit 0
