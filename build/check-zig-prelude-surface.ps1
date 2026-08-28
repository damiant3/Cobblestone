# Derive the zig plug's reserved identifier surface from EMITTED OUTPUT and
# refuse when zig-prelude-decls does not cover it.
#
# Zig forbids a local shadowing a container-level declaration, so every
# identifier the emitted prelude uses privately is reserved for every Codex
# program this plug compiles, exactly as firmly as one the prelude declares.
# A by-eye extraction counted const and var only and missed every parameter,
# which is how the surface was recorded at 66 when it is 102; this counts both.
#
# The prelude is emitted wholesale and LAST, behind zig-postlude-banner, so it
# is the common SUFFIX of every emitted program and the banner says exactly
# where it starts. Anchor on the banner rather than on a longest-common run:
# a run-length heuristic that lands in the wrong place still yields a surface,
# and a surface that is too small passes silently. When the prelude led the
# file this was a common-prefix scan, and moving the prelude turned it into a
# 24-line scan of the tuple types -- 5 names derived instead of 98, all five
# already reserved, so it printed OK.
#
# Not wired into any gate. Run it after changing zig-prelude.
[CmdletBinding()]
param(
    [string]$Subjects = 'queue-test,osc-noise,hamt-test,unit-family',
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'build-output\zig-prelude-surface' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$names = $Subjects -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($names.Count -lt 2) { throw "need at least two subjects: their preludes are required to agree" }

$emitted = @()
foreach ($n in $names) {
    $src = Join-Path $repo "codex\test\$n.codex"
    if (-not (Test-Path $src)) { throw "no such subject: $src" }
    $zig = Join-Path $OutDir "$n.zig"
    & pwsh -NoProfile -File (Join-Path $repo 'codex\plugs\zig\run.ps1') -Src $src -Out $zig | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zig)) { throw "emit failed for $n" }
    $emitted += $zig
}

$bannerLine = '// THE PRELUDE. Everything ABOVE this line is the transpiled program.'
$prelude = $null
foreach ($f in $emitted) {
    $l = @(Get-Content $f)
    $i = -1
    for ($k = 0; $k -lt $l.Count; $k++) { if ($l[$k] -eq $bannerLine) { $i = $k; break } }
    if ($i -lt 0) { throw "no postlude banner in $f : the prelude is derived from it" }
    $tail = $l[$i..($l.Count - 1)]
    if ($null -eq $prelude) { $prelude = $tail }
    elseif (($prelude -join "`n") -ne ($tail -join "`n")) {
        throw "emitted preludes disagree between subjects ($f): the prelude is supposed to be identical in every file"
    }
}

$surface = New-Object System.Collections.Generic.HashSet[string]
foreach ($line in $prelude) {
    foreach ($m in [regex]::Matches($line, '\b(?:const|var)\s+([A-Za-z_][A-Za-z0-9_]*)')) {
        [void]$surface.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($line, '\|\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:,\s*([A-Za-z_][A-Za-z0-9_]*)\s*)?\|')) {
        [void]$surface.Add($m.Groups[1].Value)
        if ($m.Groups[2].Success) { [void]$surface.Add($m.Groups[2].Value) }
    }
    foreach ($m in [regex]::Matches($line, '\bfn\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)')) {
        foreach ($p in $m.Groups[1].Value -split ',') {
            if ($p.Trim() -match '^(?:comptime\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*:') { [void]$surface.Add($Matches[1]) }
        }
    }
}
[void]$surface.Remove('_')

# zig-sanitize already quotes keywords and primitives, so those need no entry.
$emitterSrc = Get-Content (Join-Path $repo 'codex\plugs\zig\ZigEmitter.codex') -Raw
function Read-CodexList([string]$decl) {
    if ($emitterSrc -notmatch "(?s)$decl\s*:\s*List Text\s*=\s*\[(.*?)\]") { throw "cannot read $decl" }
    [regex]::Matches($Matches[1], '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
}
$declared = Read-CodexList 'zig-prelude-decls'
$keywords = Read-CodexList 'zig-keywords'
$prims    = Read-CodexList 'zig-primitive-names'

$missing = @()
$renamed = @()
foreach ($s in $surface) {
    if ($declared -contains $s) { continue }
    if ($keywords -contains $s -or $prims -contains $s) { continue }
    if ($s.Length -ge 2 -and ($s[0] -eq 'i' -or $s[0] -eq 'u') -and $s.Substring(1) -match '^[0-9]+$') { continue }
    # zig-sanitize renames a reserved name by appending _, so reserving `a`
    # makes an emitted binder read `a_`. Reserving THAT would chase its own
    # tail, one underscore per run. It is reported instead: a user top-level
    # spelled `a_` still collides, and that residue is the cost of the suffix.
    if ($s.EndsWith('_') -and ($declared -contains $s.Substring(0, $s.Length - 1))) { $renamed += $s; continue }
    $missing += $s
}

"[zig-prelude-surface] prelude {0} lines over {1} programs; surface {2} names; zig-prelude-decls carries {3}" -f `
    $prelude.Count, $emitted.Count, $surface.Count, $declared.Count

if ($renamed.Count -gt 0) {
    "[zig-prelude-surface] RESIDUE ({0}): emitted binders that are the sanitized image of a reserved name. A user top-level spelled the same still collides." -f $renamed.Count
    ($renamed | Sort-Object) -join ' '
}

if ($missing.Count -gt 0) {
    "[zig-prelude-surface] MISSING from zig-prelude-decls ({0}):" -f $missing.Count
    ($missing | Sort-Object) -join ' '
    "[zig-prelude-surface] REFUSED: a name the prelude uses can be shadowed by a user top-level."
    exit 1
}

"[zig-prelude-surface] OK: every derived name is reserved."
exit 0
