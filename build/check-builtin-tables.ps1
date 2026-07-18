# One list of builtins, not three (BACKLOG 2.14). It is one list now, and this
# script is down to its last relation.
#
# It used to check three, across three hand-maintained lists. Two went when
# sorted-builtin-names became DERIVED from x86-builtin-emitters, and the third
# list is now derived too: `builtins` (codex/compiler/Types/Builtins.codex) is
# the single table, and NameResolver.builtin-names and TypeEnv.builtin-type-env
# are both folds over it. So:
#
#   routed -> has an emitter     true by construction (was: -> CDX2042)
#   emitter -> is routed         true by construction (was: dead emitter)
#   name resolves -> is typed    not a relation any more: one row carries both
#
# None of those can be expressed, so none is checked. A guard for a state the
# source cannot reach only ever reports on itself.
#
# ONE relation survives, and it is the reason this file still exists:
#
#   x86-builtin-emitters -> `builtins`   an emitter for a name the table does
#                                        not carry. The name does not resolve
#                                        (CDX3002) and that kills not just the
#                                        call but every chapter downstream of
#                                        the one that made it. write-file sat
#                                        like this and took the entire
#                                        repository-protocol test surface dark
#                                        with it (2.14, 7.15).
#
# The compiler cannot check that itself: the emitter table and `builtins` live
# in two quires (Emit and Types) and are ordinary data, so no single compile
# sees both as tables. Same shape and same reason as check-cap-tables.ps1
# (1.12/1.13's cross-quire drift). Give `builtins` a bs-emit field and this
# whole script goes.
#
# Exit 0 = they agree. Exit 1 = drift, with the names and the failure it makes.

[CmdletBinding()]
param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$builtinsFile = Join-Path $RepoRoot 'codex\compiler\Emit\X86_64Builtins.codex'
$tableFile    = Join-Path $RepoRoot 'codex\compiler\Types\Builtins.codex'
$resolverFile = Join-Path $RepoRoot 'codex\compiler\Semantics\NameResolver.codex'
$typeEnvFile  = Join-Path $RepoRoot 'codex\compiler\Types\TypeEnv.codex'

foreach ($f in @($builtinsFile, $tableFile, $resolverFile, $typeEnvFile)) {
    if (-not (Test-Path -PathType Leaf $f)) {
        Write-Host "check-builtin-tables: MISSING $f" -ForegroundColor Red
        exit 1
    }
}

$bi = Get-Content $builtinsFile -Raw
$tb = Get-Content $tableFile -Raw
$nr = Get-Content $resolverFile -Raw
$te = Get-Content $typeEnvFile -Raw

# Take the text of a literal, from its opening bracket to the one that closes
# it, counting depth and ignoring brackets inside text literals.
#
# This used to stop at the first "])" after the anchor, which is not the end of
# anything in particular. builtin-names closes on a bare "]", so the scan ran 44
# lines past it and collected "Duplicate definition: " out of the function
# below as though it were a builtin. Harmless by luck -- no builtin is named
# that -- but a guard whose parse does not stop where the data stops is one
# string literal away from passing a real drift, and the drift it would pass is
# the CDX3002 one that takes a whole chapter's citers down with it.
function Get-ListBlock {
    param([string]$Text, [string]$Anchor, [string]$What)
    $m = [regex]::Match($Text, $Anchor)
    if (-not $m.Success) { throw "check-builtin-tables: could not find $What ($Anchor)" }

    # Start at the first bracket at or after the anchor's end.
    $i = $Text.IndexOfAny([char[]]@('[', '('), $m.Index)
    if ($i -lt 0) { throw "check-builtin-tables: no opening bracket for $What" }

    $depth = 0
    $inStr = $false
    for ($j = $i; $j -lt $Text.Length; $j++) {
        $c = $Text[$j]
        if ($inStr) {
            if ($c -eq '\') { $j++; continue }
            if ($c -eq '"') { $inStr = $false }
            continue
        }
        switch ($c) {
            '"' { $inStr = $true }
            '[' { $depth++ }
            '(' { $depth++ }
            ']' { $depth-- }
            ')' { $depth-- }
        }
        if ($depth -eq 0 -and $j -gt $i) { return $Text.Substring($i, $j - $i + 1) }
    }
    throw "check-builtin-tables: unbalanced brackets for $What"
}

# The emitter table IS the routing table now: sorted-builtin-names is derived
# from it. So there is one list to read here, and it is the emitters.
$emBlock  = Get-ListBlock $bi 'x86-builtin-emitters\s*=\s*sort-x86-emitters' 'x86-builtin-emitters'
$routed   = [regex]::Matches($emBlock, 'BuiltinX86Emitter\s*\{\s*name\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }

$tbBlock  = Get-ListBlock $tb 'builtins\s*=\s*\[' 'the builtins table'
$resolved = [regex]::Matches($tbBlock, 'bs-name\s*=\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }

# Every relation this script stopped checking, it stopped checking because a
# derivation made the bad state unreachable. Each derivation is therefore load-
# bearing: put the literal back and the drift is expressible again with nothing
# watching for it. So the shapes are asserted, not assumed.
$derivations = @(
    @{ Text = $bi; Pattern = 'sorted-builtin-names\s*=\s*\r?\n\s*let es = x86-builtin-emitters'
       What = 'sorted-builtin-names is derived from x86-builtin-emitters'
       Lost = 'routed->has-an-emitter and emitter->is-routed' }
    @{ Text = $nr; Pattern = 'builtin-names\s*=\s*builtin-names-from builtins'
       What = "NameResolver's builtin-names is derived from the builtins table"
       Lost = 'name-resolves->is-in-the-table' }
    @{ Text = $te; Pattern = 'builtin-type-env\s*=\s*builtin-env-fold builtins'
       What = 'builtin-type-env is a fold over the builtins table'
       Lost = 'name-resolves->is-typed' }
)
foreach ($d in $derivations) {
    if ($d.Text -notmatch $d.Pattern) {
        Write-Host ''
        Write-Host "check-builtin-tables: FAIL -- $($d.What) -- not any more" -ForegroundColor Red
        Write-Host "  This script deleted its check for $($d.Lost) because that derivation made the" -ForegroundColor Yellow
        Write-Host '  bad state unreachable. The derivation is gone, so the drift is reachable again' -ForegroundColor Yellow
        Write-Host '  and nothing is watching it. Restore the derivation, or restore the check.' -ForegroundColor Yellow
        Write-Host ''
        exit 1
    }
}

if ($routed.Count -lt 50 -or $resolved.Count -lt 50) {
    Write-Host "check-builtin-tables: parsed implausibly few names (routed=$($routed.Count) resolved=$($resolved.Count)) -- the parse broke, not the tables" -ForegroundColor Red
    exit 1
}

$problems = @()

# A routed name NameResolver does not know dies CDX3002 -- and takes every
# chapter downstream of the caller with it. This is the write-file shape, and
# it is the last of the three that can still happen.
$noResolve = $routed | Where-Object { $resolved -notcontains $_ } | Sort-Object
foreach ($n in $noResolve) {
    $problems += "  $n : has an x86-64 emitter and is therefore routed, but is absent from the builtins table -> CDX3002, and every citer of the calling chapter dies with it"
}

# The reverse direction (a NameResolver name not routed) is NOT checked: it is
# legitimate and common. Constructors (True/False/Nothing), typeclass
# derivations (__dderiv-*), and the ~100 syscall/effect-op names resolve and are
# emitted by paths other than emit-builtin. Flagging those would be noise, and a
# guard that cries wolf gets switched off.

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'check-builtin-tables: FAIL -- the builtin tables disagree (BACKLOG 2.14)' -ForegroundColor Red
    $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host ''
    Write-Host '  A name the compiler routes but cannot keep is worse than no name at all:' -ForegroundColor Yellow
    Write-Host '  it is claimed loudly enough to shadow an effect op of the same name, and' -ForegroundColor Yellow
    Write-Host '  then fails at a distance from whatever asked for it.' -ForegroundColor Yellow
    Write-Host "  Real implementations live in Foreword chapter Fat16 (write-file, file-exists)." -ForegroundColor Yellow
    Write-Host ''
    exit 1
}

Write-Host "check-builtin-tables: OK -- $($routed.Count) routed (derived from the emitter table), $($resolved.Count) in the builtins table (names and types derived from it); the last relation holds"
exit 0
