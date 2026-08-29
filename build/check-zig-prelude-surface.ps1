# Derive the zig plug's reserved identifier surface from EMITTED OUTPUT and
# refuse when zig-prelude-decls does not cover it.
#
# Zig forbids a local shadowing a container-level declaration, so every
# identifier the emitted prelude uses privately is reserved for every Codex
# program this plug compiles, exactly as firmly as one the prelude declares.
# A by-eye extraction counted const and var only and missed every parameter,
# which is how the surface was recorded at 66 when it is 102; this counts both.
#
# AND IT COUNTS FUNCTION NAMES, which it did not until 2026-08-28. The regex
# that harvests parameters reads past `fn NAME` to reach the parameter list and
# dropped the name on the way, so this script printed OK over a surface with
# none of the prelude's 74 functions in it. A Codex program with a top-level
# named `cx-print` emitted a second `fn cx_print` and the file would not
# compile. Finding 67.
#
# THE PRELUDE IS NOT THE SAME IN EVERY PROGRAM ANY MORE. It is tree-shaken:
# each program carries the parts it reaches, so requiring the emitted preludes
# to be IDENTICAL -- which this did -- now fails by design. The replacement is
# stronger, not weaker. Every emitted prelude must be a SUB-SELECTION of one
# known whole, in table order: walk the parts, consume the ones that match at
# the cursor, skip the rest, and require the cursor to land exactly at the end.
# A prelude that reordered, duplicated, truncated or invented anything fails
# that walk, and "they are all identical" tested none of it.
#
# The whole comes from the emitter's own `zig-prelude-parts` table, so the
# surface is derived from EVERY part rather than from whichever ones one
# subject happened to reach. That keeps zig-prelude-decls a union, which is
# what its own prose requires.
#
# Not wired into any gate. Run it after changing zig-prelude.
[CmdletBinding()]
param(
    [string]$Subjects = 'queue-test,osc-noise,hamt-test,unit-family',
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertFrom-CodexEscapes([string]$s) {
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $s.Length; $i++) {
        if ($s[$i] -eq '\' -and $i + 1 -lt $s.Length) {
            $i++
            switch ($s[$i]) {
                'n' { [void]$sb.Append("`n") }
                't' { [void]$sb.Append("`t") }
                '"' { [void]$sb.Append('"') }
                '\' { [void]$sb.Append('\') }
                default { [void]$sb.Append('\'); [void]$sb.Append($s[$i]) }
            }
        } else { [void]$sb.Append($s[$i]) }
    }
    $sb.ToString()
}

# The parts, in table order, reconstructed from the emitter's own generated
# table. A part's text is the concatenation of its fragments' payloads --
# ShakeLit and ShakeUse alike, because ShakeUse is text that is ALSO an edge.
function Get-ShakeParts([string]$src) {
    $frag = @{}
    foreach ($m in [regex]::Matches($src, '(?m)^  (zig-p-[a-z0-9-]+) = \[(.*)\]\s*$')) {
        $sb = New-Object System.Text.StringBuilder
        foreach ($f in [regex]::Matches($m.Groups[2].Value, 'Shake(?:Lit|Use) "((?:\\.|[^"\\])*)"')) {
            [void]$sb.Append((ConvertFrom-CodexEscapes $f.Groups[1].Value))
        }
        $frag[$m.Groups[1].Value] = $sb.ToString()
    }
    if ($frag.Count -eq 0) { throw "no zig-p-* fragment lists in ZigEmitter.codex: is the prelude restructured?" }
    $order = @()
    foreach ($m in [regex]::Matches($src, 'ShakePart \{ name = "([^"]*)", frags = ([a-z0-9-]+) \}')) {
        $key = $m.Groups[2].Value
        if (-not $frag.ContainsKey($key)) { throw "zig-prelude-parts names $key and no such fragment list exists" }
        $order += ,@($m.Groups[1].Value, $frag[$key])
    }
    if ($order.Count -eq 0) { throw "no ShakePart rows in zig-prelude-parts" }
    ,$order
}

# Emitted must be the parts, in table order, with some omitted. Nothing else.
function Test-SubSelection($parts, [string]$emitted) {
    $cur = 0
    $kept = 0
    foreach ($p in $parts) {
        $t = $p[1]
        if ($cur + $t.Length -le $emitted.Length -and
            [string]::CompareOrdinal($emitted, $cur, $t, 0, $t.Length) -eq 0) {
            $cur += $t.Length
            $kept++
        }
    }
    @{ ok = ($cur -eq $emitted.Length); consumed = $cur; kept = $kept }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'build-output\zig-prelude-surface' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$names = $Subjects -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($names.Count -lt 1) { throw "need at least one subject" }

$emitterSrc = Get-Content (Join-Path $repo 'codex\plugs\zig\ZigEmitter.codex') -Raw
$parts = Get-ShakeParts $emitterSrc
$whole = -join ($parts | ForEach-Object { $_[1] })

$emitted = @()
foreach ($n in $names) {
    $src = Join-Path $repo "codex\test\$n.codex"
    if (-not (Test-Path $src)) { throw "no such subject: $src" }
    $zig = Join-Path $OutDir "$n.zig"
    & pwsh -NoProfile -File (Join-Path $repo 'codex\plugs\zig\run.ps1') -Src $src -Out $zig | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zig)) { throw "emit failed for $n" }
    $emitted += $zig
}

# Each emitted prelude must be a sub-selection of $whole, in table order.
$bannerLine = '// THE PRELUDE. Everything ABOVE this line is the transpiled program.'
foreach ($f in $emitted) {
    $body = Get-Content $f -Raw
    $i = $body.IndexOf($bannerLine)
    if ($i -lt 0) { throw "no postlude banner in $f : the prelude is derived from it" }
    $start = -1
    foreach ($p in $parts) {
        $k = $body.IndexOf($p[1], $i)
        if ($k -ge 0 -and ($start -lt 0 -or $k -lt $start)) { $start = $k }
    }
    if ($start -lt 0) { throw "no part text after the banner in $f : the parts table does not describe this output" }
    $r = Test-SubSelection $parts $body.Substring($start)
    if (-not $r.ok) {
        throw ("emitted prelude in {0} is not a sub-selection of zig-prelude-parts: consumed {1} of {2} bytes after {3} parts. It reordered, duplicated, truncated or invented something." -f `
            $f, $r.consumed, ($body.Length - $start), $r.kept)
    }
    "[zig-prelude-surface] {0}: {1}/{2} parts kept, {3} bytes, sub-selection OK" -f `
        (Split-Path $f -Leaf), $r.kept, $parts.Count, $r.consumed
}

# The surface comes from the WHOLE, never from one program's selection.
$surface = New-Object System.Collections.Generic.HashSet[string]
foreach ($line in ($whole -split "`n")) {
    foreach ($m in [regex]::Matches($line, '\b(?:const|var)\s+([A-Za-z_][A-Za-z0-9_]*)')) {
        [void]$surface.Add($m.Groups[1].Value)
    }
    # The function's OWN name, which this script read past for months. A zig
    # file is a struct; `fn cx_print` is a member of it and a user top-level
    # spelled the same is a duplicate, not a shadow. Finding 67.
    foreach ($m in [regex]::Matches($line, '(?m)^(?:pub\s+)?fn\s+([A-Za-z_][A-Za-z0-9_]*)')) {
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

"[zig-prelude-surface] whole prelude {0} parts / {1} bytes over {2} programs; surface {3} names; zig-prelude-decls carries {4}" -f `
    $parts.Count, $whole.Length, $emitted.Count, $surface.Count, $declared.Count

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
