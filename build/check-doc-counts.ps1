# check-doc-counts.ps1 -- Never carry a count forward. This is the thing that re-measures them.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [switch]$Quiet,
    [string]$Repo = ''
)

# Never carry a count forward. This is the thing that re-measures them.
# 
# CLAUDE.md says never carry a count forward. ExaminersAssay.md says it twice
# and adds that a count is only ever the number some run actually produced.
# Saying so has not worked, because saying so is not an instrument. Measured
# 2026-07-25, with every one of these sentences already in the tree:
# 
#   claim                                   said    measured
#   compiler files (CLAUDE.md)              55      63
#   compiler files (VisionAndVirtues.md)    60      63
#   codex.foreword modules                  119     120
#   codex.foreword.ui modules               47      48
#   codex.os.kernel modules                 33      34
#   codex/test/errors tests                 132     162
# 
# Nothing could see any of it, for exactly the reason nothing could see an
# orphaned sidecar or an unregistered CDX code: there was no reader. This is
# the reader. Each claim is a regex over a doc plus a measurement over the
# tree, and a disagreement is a failure with both numbers printed.
# 
# A claim whose pattern no longer matches is ALSO a failure. A doc that
# changed shape has quietly stopped being checked, which is the same defect
# one step earlier and is invisible by construction.
# 
# Not wired into build.ps1. Wiring it in is a decision about everyone's gate,
# not this script's to make.
# 
# Exit 1 on any drift or any unmatched pattern.
# 
# -Repo points the whole check at a tree other than this script's own. It
# exists for build/ablate-doctrine.ps1, which stands up a scratch tree a
# candidate agent can edit and then has to score the result without touching
# the real workspace. Default is the parent of this script, so every existing
# caller is unchanged.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$treeRoot = $(if (($Repo -ne '')) { (Resolve-Path $Repo).Path } else { (Split-Path $PSScriptRoot) })


function Get-CodexFileCount([string]$Rel, [bool]$Recurse) {
    $p = (Join-Path $treeRoot $Rel)
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    if ($Recurse) {
        return @(Get-ChildItem $p -Recurse -Filter '*.codex' -File).Count
    }
    return @(Get-ChildItem $p -Filter '*.codex' -File).Count
}

function Get-CodexLineCount([string]$Rel) {
    $p = (Join-Path $treeRoot $Rel)
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    $n = 0
    foreach ($f in Get-ChildItem $p -Recurse -Filter '*.codex' -File) {
        $n += @(([System.IO.File]::ReadAllLines($f.FullName))).Count
    }
    return $n
}


function Get-PlugCount() {
    $p = (Join-Path $treeRoot 'codex/plugs')
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    $n = 0
    foreach ($d in ([System.IO.Directory]::GetDirectories($p))) {
        if ((Test-Path -PathType Leaf (Join-Path $d 'build.ps1'))) {
            $n++
        }
    }
    return $n
}

# Source only. Counting build-output/ made this claim depend on local build
# state: the same depot revision measured 191 in a dev workspace and 147 in
# the main one, because a plug's generated plug-source.codex is a .codex file
# too. A claim whose value moves with what you last built is not a claim, and
# -Repo against a second tree is what caught it.

function Get-PlugModuleCount() {
    $p = (Join-Path $treeRoot 'codex/plugs')
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    $n = 0
    foreach ($d in ([System.IO.Directory]::GetDirectories($p))) {
        if ((-not (Test-Path -PathType Leaf (Join-Path $d 'build.ps1')))) {
            continue
        }
        foreach ($f in Get-ChildItem $d -Recurse -Filter '*.codex' -File) {
            if (($f.FullName -match '[\\/]build-output[\\/]')) {
                continue
            }
            $n++
        }
    }
    return $n
}


function Get-AppDirCount() {
    $p = (Join-Path $treeRoot 'apps')
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    return @(([System.IO.Directory]::GetDirectories($p))).Count
}

# Source only, for the same reason as Get-PlugModuleCount: apps/*/build-output/
# holds generated .codex, so counting it makes the claim depend on what this
# workspace last built rather than on the depot revision.

function Get-AppModuleCount() {
    $p = (Join-Path $treeRoot 'apps')
    if ((-not (Test-Path -PathType Container $p))) {
        return -1
    }
    return @((Get-ChildItem $p -Recurse -Filter '*.codex' -File | Where-Object { (-not ($_.FullName -match '[\\/]build-output[\\/]')) })).Count
}


# The BVT's own test list, read out of build/bvt.ps1. README states its size
# and that number went unmanaged: the line said "75 tests in 18.8s" with the
# 18.8s from a run nobody could reproduce and no claim on the check count at
# all. The counts are structural (the list, and how many entries have an
# .expected to run) so they belong here; the elapsed time does not, and was
# removed from the doc rather than pinned.

function Get-BvtTestPaths() {
    $p = (Join-Path $treeRoot 'build/bvt.ps1')
    if ((-not (Test-Path -PathType Leaf $p))) {
        return @()
    }
    $m = ([regex]::Match(([System.IO.File]::ReadAllText($p)), '\$BvtTests\s*=\s*@\((.*?)\n\)', 'Singleline'))
    if ((-not $m.Success)) {
        return @()
    }
    return @((([regex]::Matches($m.Groups[1].Value, '''([^'']+\.codex)''')) | ForEach-Object { $_.Groups[1].Value }))
}

function Get-BvtTestCount() {
    return @((Get-BvtTestPaths)).Count
}

function Get-BvtCheckCount() {
    $paths = @((Get-BvtTestPaths))
    if ((@($paths).Count -eq 0)) {
        return -1
    }
    # Every test is compiled; the ones carrying an .expected are also run.
    $run = @(($paths | Where-Object { (Test-Path -PathType Leaf (Join-Path $treeRoot ($_ -replace '\.codex$', '.expected'))) }))
    return (@($paths).Count + @($run).Count)
}


# The seed digests are the reason this file grew a text mode. README ships
# them to the public, nothing re-read them, and on 2026-07-31 every one was
# wrong: the size by 108,817 bytes, the SHA-256 and MD5 entirely, and the
# same artifact was stated at three different sizes in three places. A digest
# is exactly as mechanically checkable as a count and rots the same way.

function Get-FileBytes([string]$Rel) {
    $p = (Join-Path $treeRoot $Rel)
    if ((-not (Test-Path -PathType Leaf $p))) {
        return -1
    }
    return (Get-Item $p).Length
}

function Get-FileDigest([string]$Rel, [string]$Algorithm) {
    $p = (Join-Path $treeRoot $Rel)
    if ((-not (Test-Path -PathType Leaf $p))) {
        return ''
    }
    if (($Algorithm -eq 'MD5')) {
        $h = (Get-FileHash -Algorithm MD5 $p).Hash
        return $h
    }
    $h = (Get-FileHash -Algorithm SHA256 $p).Hash
    return $h
}

# Head AND tail. Printing a leading slice alone made a one-digit tail
# difference render as two identical strings, which reads as the checker
# malfunctioning rather than as the digest being wrong. Found by sabotage:
# flipping the last hex digit produced a DRIFT whose two columns matched.

function Format-Digest([string]$D) {
    if (($D.Length -le 20)) {
        return $D
    }
    return ([string]$D.Substring(0, 8) + ([string]'..' + $D.Substring(($D.Length - 8))))
}


# A claim names its measurement by verb rather than carrying a scriptblock.
# An unknown verb THROWS: a silent $null would compare as 0 or -1 and read as
# a drift or a missing directory, which is a wrong answer wearing the costume
# of a real one.

function Measure-Claim([string]$Fn, [string]$Arg) {
    if (($Fn -eq 'files-r')) {
        return (Get-CodexFileCount $Arg $true)
    }
    if (($Fn -eq 'files')) {
        return (Get-CodexFileCount $Arg $false)
    }
    if (($Fn -eq 'lines')) {
        return (Get-CodexLineCount $Arg)
    }
    if (($Fn -eq 'bytes')) {
        return (Get-FileBytes $Arg)
    }
    if (($Fn -eq 'sha256')) {
        return (Get-FileDigest $Arg 'SHA256')
    }
    if (($Fn -eq 'md5')) {
        return (Get-FileDigest $Arg 'MD5')
    }
    if (($Fn -eq 'plugs')) {
        return (Get-PlugCount)
    }
    if (($Fn -eq 'plug-modules')) {
        return (Get-PlugModuleCount)
    }
    if (($Fn -eq 'app-dirs')) {
        return (Get-AppDirCount)
    }
    if (($Fn -eq 'app-modules')) {
        return (Get-AppModuleCount)
    }
    if (($Fn -eq 'bvt-tests')) {
        return (Get-BvtTestCount)
    }
    if (($Fn -eq 'bvt-checks')) {
        return (Get-BvtCheckCount)
    }
    if (($Fn -eq 'lib-modules')) {
        return ((Get-CodexFileCount 'codex/foreword' $true) + (Get-CodexFileCount 'codex/os' $true))
    }
    throw ([string]([string]'Measure-Claim: no such measurement ''' + $Fn) + '''')
}


$claims = @()
$claims += @{ Name = 'compiler files (CLAUDE)'; Doc = 'CLAUDE.md'; Pattern = 'The compiler is ~[\d,]+ lines of Codex across\s+(\d+)\s+files'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/compiler' }
$claims += @{ Name = 'compiler lines (CLAUDE)'; Doc = 'CLAUDE.md'; Pattern = 'The compiler is ~([\d,]+) lines of Codex across\s+\d+\s+files'; Group = 1; TolPct = 2; Kind = 'number'; Fn = 'lines'; Arg = 'codex/compiler' }
$claims += @{ Name = 'compiler files (Vision)'; Doc = 'docs/VisionAndVirtues.md'; Pattern = 'The compiler is ~[\d,]+ lines across\s+(\d+)\s+files'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/compiler' }
$claims += @{ Name = 'compiler lines (Vision)'; Doc = 'docs/VisionAndVirtues.md'; Pattern = 'The compiler is ~([\d,]+) lines across\s+\d+\s+files'; Group = 1; TolPct = 2; Kind = 'number'; Fn = 'lines'; Arg = 'codex/compiler' }
$claims += @{ Name = 'codex.foreword modules'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex\.foreword \((\d+) modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/foreword/core' }
$claims += @{ Name = 'codex.foreword.encode modules'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex\.foreword\.encode \((\d+) modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/foreword/encode' }
$claims += @{ Name = 'codex.foreword.ui modules'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex\.foreword\.ui \((\d+) modules\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/foreword/ui' }
$claims += @{ Name = 'compiler modules (Rulebook)'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex \((\d+) modules\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/compiler' }
$claims += @{ Name = 'codex.os modules'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex\.os \((\d+) modules\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/os' }
$claims += @{ Name = 'codex.plugs count'; Doc = 'docs/DevelopersRulebook.md'; Pattern = '### codex\.plugs \((\d+) plugs'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'plugs'; Arg = '' }
$claims += @{ Name = 'ui modules (Portal)'; Doc = 'docs/TheShimmeringPortal.md'; Pattern = 'codex/foreword/ui/\s+(\d+) modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/foreword/ui' }
$claims += @{ Name = 'errors tests (Assay, state)'; Doc = 'docs/ExaminersAssay.md'; Pattern = 'holds \*\*(\d+)\*\* expected-failure tests'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/test/errors' }
$claims += @{ Name = 'errors tests (Assay, section)'; Doc = 'docs/ExaminersAssay.md'; Pattern = '(\d+) tests in `codex/test/errors/` verify'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = 'codex/test/errors' }


# README.md is the public face and was the one doc with no coverage here.
# Audited 2026-07-31: every count and digest below had drifted, several by
# more than 2x, and the same fact appeared in three places disagreeing with
# itself. These rows exist so that cannot recur silently.

$claims += @{ Name = 'seed cdx bytes (README)'; Doc = 'README.md'; Pattern = '\*\*`seed/Codex\.cdx`\*\* \(([\d,]+) bytes'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'bytes'; Arg = 'seed/Codex.cdx' }
$claims += @{ Name = 'seed cdx sha256 (README)'; Doc = 'README.md'; Pattern = 'seed/Codex\.cdx.*?\| SHA-256 \| `([0-9A-Fa-f]{64})`'; Group = 1; TolPct = 0; Kind = 'text'; Fn = 'sha256'; Arg = 'seed/Codex.cdx' }
$claims += @{ Name = 'seed cdx md5 (README)'; Doc = 'README.md'; Pattern = 'seed/Codex\.cdx.*?\| MD5 \| `([0-9A-Fa-f]{32})`'; Group = 1; TolPct = 0; Kind = 'text'; Fn = 'md5'; Arg = 'seed/Codex.cdx' }
$claims += @{ Name = 'seed img bytes (README)'; Doc = 'README.md'; Pattern = '\*\*`seed/Codex\.img`\*\* \(([\d,]+) bytes'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'bytes'; Arg = 'seed/Codex.img' }
$claims += @{ Name = 'seed img sha256 (README)'; Doc = 'README.md'; Pattern = 'seed/Codex\.img.*?\| SHA-256 \| `([0-9A-Fa-f]{64})`'; Group = 1; TolPct = 0; Kind = 'text'; Fn = 'sha256'; Arg = 'seed/Codex.img' }


# Repointed 2026-08-12. README was rewritten at rev 25 and every pattern
# below stopped matching, so eight claims plus all sixteen quire rows went
# silently unchecked -- the NOMATCH failure this script was written to make
# visible, arriving for real. The counts they had stopped watching had
# drifted by then: foreword 430 -> 431, plug modules 145 -> 148, apps
# modules 1,010 -> 1,008, compiler 63 files -> 64. Patterns now track the
# README's current shape; the quire table simply lost its bold.

$claims += @{ Name = 'compiler files (README)'; Doc = 'README.md'; Pattern = 'Self-hosted compiler \((\d+) files, [\d,]+ lines\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/compiler' }
$claims += @{ Name = 'compiler lines (README)'; Doc = 'README.md'; Pattern = 'Self-hosted compiler \(\d+ files, ([\d,]+) lines\)'; Group = 1; TolPct = 2; Kind = 'number'; Fn = 'lines'; Arg = 'codex/compiler' }
$claims += @{ Name = 'library modules (README)'; Doc = 'README.md'; Pattern = '\*\*(\d+) library modules across \d+ quires\*\* \(\d+ foreword \+ \d+ OS\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'lib-modules'; Arg = '' }
$claims += @{ Name = 'foreword modules (README)'; Doc = 'README.md'; Pattern = '\*\*\d+ library modules across \d+ quires\*\* \((\d+) foreword \+ \d+ OS\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/foreword' }
$claims += @{ Name = 'os modules (README)'; Doc = 'README.md'; Pattern = '\*\*\d+ library modules across \d+ quires\*\* \(\d+ foreword \+ (\d+) OS\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/os' }


# The same three facts again in the Library Quires preamble. They disagreed
# with the headline by a module on 2026-07-31 and again on 2026-08-12, so
# both statements are checked rather than one.

$claims += @{ Name = 'quire preamble modules (README)'; Doc = 'README.md'; Pattern = '\*\*(\d+) modules\*\* \(\d+ foreword, \d+ OS\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'lib-modules'; Arg = '' }
$claims += @{ Name = 'quire preamble foreword (README)'; Doc = 'README.md'; Pattern = '\*\*\d+ modules\*\* \((\d+) foreword, \d+ OS\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/foreword' }
$claims += @{ Name = 'foreword modules (README tree)'; Doc = 'README.md'; Pattern = 'foreword/\s+(\d+) library modules across \d+ quires'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/foreword' }
$claims += @{ Name = 'os modules (README tree)'; Doc = 'README.md'; Pattern = 'observe \((\d+) modules\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/os' }
$claims += @{ Name = 'plug count (README)'; Doc = 'README.md'; Pattern = '(\d+) plugs, \d+ source modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'plugs'; Arg = '' }
$claims += @{ Name = 'plug modules (README)'; Doc = 'README.md'; Pattern = '\d+ plugs, ([\d,]+) source modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'plug-modules'; Arg = '' }
$claims += @{ Name = 'apps (README)'; Doc = 'README.md'; Pattern = '(\d+) applications, [\d,]+ modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'app-dirs'; Arg = '' }
$claims += @{ Name = 'app modules (README)'; Doc = 'README.md'; Pattern = '\d+ applications, ([\d,]+) modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'app-modules'; Arg = '' }


# Both statements of the apps fact. The headline and the tree block
# disagreed on 2026-08-12 (1,010 against 1,008) because only one of them
# was ever in front of a reader who was editing it. A regex takes the FIRST
# match, so a second copy of a fact is unchecked unless it is anchored.

$claims += @{ Name = 'apps (README tree)'; Doc = 'README.md'; Pattern = 'apps/\s+(\d+) applications, [\d,]+ modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'app-dirs'; Arg = '' }
$claims += @{ Name = 'app modules (README tree)'; Doc = 'README.md'; Pattern = 'apps/\s+\d+ applications, ([\d,]+) modules'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'app-modules'; Arg = '' }
$claims += @{ Name = 'test files (README)'; Doc = 'README.md'; Pattern = 'OS integration tests \(([\d,]+) files\)'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = 'codex/test' }
$claims += @{ Name = 'BVT tests (README)'; Doc = 'README.md'; Pattern = 'gates on is (\d+) tests'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'bvt-tests'; Arg = '' }
$claims += @{ Name = 'BVT checks (README)'; Doc = 'README.md'; Pattern = 'for (\d+) checks'; Group = 1; TolPct = 0; Kind = 'number'; Fn = 'bvt-checks'; Arg = '' }



# The README per-quire table. Same generated shape as the codex.os rows below,
# and the same reason: these are the numbers that drift fastest.
$readmeQuires = @(@{ Label = 'Foreword'; Path = 'codex/foreword/core' }, @{ Label = 'Game'; Path = 'codex/foreword/game' }, @{ Label = 'AI'; Path = 'codex/foreword/ai' }, @{ Label = 'UI'; Path = 'codex/foreword/ui' }, @{ Label = 'Signal'; Path = 'codex/foreword/signal' }, @{ Label = 'Compress'; Path = 'codex/foreword/compress' }, @{ Label = 'Encode'; Path = 'codex/foreword/encode' }, @{ Label = 'Math'; Path = 'codex/foreword/math' }, @{ Label = 'Sim'; Path = 'codex/foreword/sim' }, @{ Label = 'Punctual'; Path = 'codex/foreword/punctual' }, @{ Label = 'Engine'; Path = 'codex/foreword/engine' }, @{ Label = 'GPU'; Path = 'codex/foreword/gpu' }, @{ Label = 'Shell'; Path = 'codex/foreword/shell' }, @{ Label = 'Boards'; Path = 'codex/boards' }, @{ Label = 'Net'; Path = 'codex/os/net' }, @{ Label = 'Kernel'; Path = 'codex/os/kernel' })
foreach ($q in $readmeQuires) {
    $claims += @{ Name = ([string]'README quire ' + $q.Label); Doc = 'README.md'; Pattern = ([string]'\| ' + ([string]$q.Label + ([string]' \| `' + ([string]$q.Path + '/` \| (\d+) \|')))); Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files-r'; Arg = $q.Path }
}

# The per-sub-quire rows of the codex.os table. These drift most and are the
# cheapest to check, so they are generated rather than written out.
foreach ($q in @('core', 'dev', 'kernel', 'net', 'observe', 'replay', 'sched', 'trust', 'verify')) {
    $claims += @{ Name = ([string]'codex.os.' + ([string]$q + ' modules')); Doc = 'docs/DevelopersRulebook.md'; Pattern = ([string]'\| codex\.os\.' + ([string]$q + ' \| (\d+) \|')); Group = 1; TolPct = 0; Kind = 'number'; Fn = 'files'; Arg = ([string]'codex/os/' + $q) }
}


$cache = @{}
$rows = @()
$bad = 0
foreach ($c in $claims) {
    $docPath = (Join-Path $treeRoot $c.Doc)
    if ((-not $cache.ContainsKey($c.Doc))) {
        if ((-not (Test-Path -PathType Leaf $docPath))) {
            $rows += @{ Status = 'NODOC'; Claim = $c.Name; Doc = $c.Doc; Said = '-'; Measured = '-' }
            $bad++
            continue
        }
        $cache[$c.Doc] = ([System.IO.File]::ReadAllText($docPath))
    }
    $text = $cache[$c.Doc]
    $m = ([regex]::Match($text, $c.Pattern, 'Singleline'))
    if ((-not $m.Success)) {
        $rows += @{ Status = 'NOMATCH'; Claim = $c.Name; Doc = $c.Doc; Said = '-'; Measured = '-' }
        $bad++
        continue
    }
    if (($c.Kind -eq 'text')) {
        # Digests. Compared case-insensitively because the doc may write them
        # in either case; an empty measurement means the artifact is missing.

        $said = $m.Groups[$c.Group].Value
        $measured = (Measure-Claim $c.Fn $c.Arg)
        if (([string]::IsNullOrEmpty($measured))) {
            $rows += @{ Status = 'NOPATH'; Claim = $c.Name; Doc = $c.Doc; Said = (Format-Digest $said); Measured = '-' }
            $bad++
            continue
        }
        $ok = ($said -eq $measured)
        $rows += @{ Status = $(if ($ok) { 'ok' } else { 'DRIFT' }); Claim = $c.Name; Doc = $c.Doc; Said = (Format-Digest $said); Measured = (Format-Digest $measured) }
        if ((-not $ok)) {
            $bad++
        }
        continue
    }
    $said = [int]($m.Groups[$c.Group].Value -replace ',', '')
    $measured = (Measure-Claim $c.Fn $c.Arg)
    if (($measured -lt 0)) {
        $rows += @{ Status = 'NOPATH'; Claim = $c.Name; Doc = $c.Doc; Said = $said; Measured = '-' }
        $bad++
        continue
    }
    $ok = $(if ((($c.TolPct -gt 0) -and ($measured -gt 0))) { (((([math]::Abs(($said - $measured))) * 100.0) / $measured) -le $c.TolPct) } else { ($said -eq $measured) })
    $rows += @{ Status = $(if ($ok) { 'ok' } else { 'DRIFT' }); Claim = $c.Name; Doc = $c.Doc; Said = $said; Measured = $measured }
    if ((-not $ok)) {
        $bad++
    }

}


if ((-not $Quiet)) {
    foreach ($r in $rows) {
        Write-Host ('{0,-8} {1,-30} said {2,-8} measured {3,-8} {4}' -f $r.Status, $r.Claim, $r.Said, $r.Measured, $r.Doc)
    }
    Write-Host ''
}

if (($bad -gt 0)) {
    Write-Host ([string]'FAIL: ' + ([string]$bad + ([string]' of ' + ([string]@($rows).Count + ' doc count claim(s) do not hold.'))))
    Write-Host 'DRIFT   the doc states a number the tree no longer produces. Re-measure and edit the doc.'
    Write-Host 'NOMATCH the claim pattern no longer matches. The doc changed shape and stopped being'
    Write-Host '        checked; fix the pattern here, or the doc, but do not leave it unmatched.'
    Write-Host 'NOPATH  the directory a claim measures does not exist.'

    exit 1
}
Write-Host ([string]'doc counts ok (' + ([string]@($rows).Count + ' claims checked, 0 drifted)'))
exit 0
