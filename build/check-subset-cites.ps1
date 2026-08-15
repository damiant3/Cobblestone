# check-subset-cites.ps1 -- compile every chapter with ONLY what it cites.
#
# The compiler is assembled by GLOB (concat-codex-self.ps1 sweeps
# codex\compiler), so a chapter that uses a name it never cited still
# compiles: whatever defines that name is in the unit because something else
# dragged it in. The cite list is decoration there, and nothing measures it.
#
# It stops being decoration the moment a SUBSET of these chapters is bundled,
# which is exactly what a plug bundle is -- plug-build-lib.ps1 carries Name,
# SourceText, CodexType, AstNodes and IRChapter into every transpiler plug and
# takes only what is named or cited. A borrowed name that no cite declares is
# then simply absent, and the failure lands on whoever assembled the subset
# rather than on whoever wrote the chapter.
#
# GitHub PR 64 found two of these from the outside (Steve Howell): the
# deck-record intercept firing on a name PlugTypes also defines, and
# TypeChecker using capability-names out of Foreword Capability with no cite.
# This is the instrument for the rest of that class, so the next one is ours.
#
# HOW, and the method is the point. It does not reason about the source: it
# BUILDS each chapter as its own unit -- the chapter, plus the transitive
# closure of what it cites, plus a trivial entry point -- and compiles it with
# the real compiler. A static version of this was written first and thrown
# away. It cannot be made to work: `mc` is a local here and a definition in two
# app quires, record field names collide with app definitions by the dozen, and
# `map-list` needs no cite at all because Resolve-CiteOrder walks ListUtils and
# Tuple unconditionally. The static filter reported 575 findings of which none
# were real. The compiler's own verdict has no such problem.
#
# READING THE OUTPUT. Most undefined names are INTERNAL -- defined by a
# sibling chapter in the same tree, which is the glob assembly model and not a
# defect. Measured 2026-08-15 over the compiler: 93 internal names against ONE
# external, and the external one was real (BootPaint using to-unicode from
# Foreword CCE, citing nothing at all). Only the EXTERNAL list is actionable.
#
# Not in build.ps1: it is 46 compiles, about ten minutes at -Jobs 8. Run it
# after touching cites, after adding a chapter, or when a plug bundle fails to
# resolve a name.
[CmdletBinding()]
param(
    [string]$Root = 'codex\compiler',
    [string]$Only = '',
    [int]$Jobs = 8,
    [string]$Kernel = '',
    [switch]$KeepUnits
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'quire-map.ps1')

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path $Kernel)) { throw "kernel not found: $Kernel" }

$rootDir = Join-Path $Repo $Root
if (-not (Test-Path $rootDir)) { throw "not found: $rootDir" }

$work = Join-Path $env:TEMP ("subsetcite-" + $PID)
New-Item -ItemType Directory -Force -Path $work | Out-Null

# A chapter can span several files (X86-64 Code Generator is fourteen), so the
# unit is the CHAPTER and never the file. Probing one file of a chapter reports
# its siblings' definitions as missing and means nothing.
$byChapter = @{}
foreach ($fi in (Get-ChildItem $rootDir -Filter '*.codex' -Recurse -File)) {
    $head = Get-Content $fi.FullName -TotalCount 1
    if ($head -match '^Chapter:\s*(.+?)\s*$') {
        $name = $matches[1]
        if (-not $byChapter.ContainsKey($name)) { $byChapter[$name] = @() }
        $byChapter[$name] += $fi.FullName
    }
}

# Cites inside this tree name the quire `Codex`, which is not registered and
# never can be: it is satisfied by co-presence in the glob. Resolve it against
# the tree itself, by chapter header and by file name, or every unit fails on
# the cite instead of on the thing being measured.
$selfIdx = @{}
foreach ($fi in (Get-ChildItem $rootDir -Filter '*.codex' -Recurse -File)) {
    $head = Get-Content $fi.FullName -TotalCount 1
    if ($head -match '^Chapter:\s*(.+?)\s*$') { $selfIdx[(Get-CiteKey $matches[1])] = $fi.FullName }
    $selfIdx[(Get-CiteKey $fi.BaseName)] = $fi.FullName
}
$override = { param($quire, $name) if ($selfIdx.ContainsKey((Get-CiteKey $name))) { return $selfIdx[(Get-CiteKey $name)] } return $null }.GetNewClosure()

# Every name the tree defines for itself, definitions and constructors alike.
# An undefined name found here is INTERNAL: co-presence explains it.
$ownNames = @{}
foreach ($fi in (Get-ChildItem $rootDir -Filter '*.codex' -Recurse -File)) {
    foreach ($ln in [System.IO.File]::ReadAllLines($fi.FullName)) {
        if ($ln -match '^  ([a-zA-Z_][A-Za-z0-9_-]*)\s*:') { $ownNames[$matches[1]] = $true }
        elseif ($ln -match '^\s*\|\s*([A-Za-z_][A-Za-z0-9_]*)') { $ownNames[$matches[1]] = $true }
        elseif ($ln -match '^  ([A-Za-z_][A-Za-z0-9_]*)\s*=') { $ownNames[$matches[1]] = $true }
    }
}

$units = @()
foreach ($chapter in ($byChapter.Keys | Sort-Object)) {
    if ($Only -and $chapter -notlike "*$Only*") { continue }
    $safe = ($chapter -replace '[^A-Za-z0-9]', '_')
    $body = [System.Collections.Generic.List[string]]::new()
    $firstFile = $true
    foreach ($f in ($byChapter[$chapter] | Sort-Object)) {
        foreach ($ln in [System.IO.File]::ReadAllLines($f)) {
            if ((-not $firstFile) -and $ln.StartsWith('Chapter:')) { continue }
            $body.Add($ln)
        }
        $firstFile = $false
        $body.Add('')
    }
    $ordered = Resolve-CiteOrder -RootLines $body.ToArray() -Repo $Repo -OnMissing skip -PathOverride $override
    $unit = [System.Collections.Generic.List[string]]::new()
    foreach ($e in $ordered) {
        $renamed = $false
        foreach ($ln in $e.Lines) {
            if ((-not $renamed) -and $ln.StartsWith('Chapter:')) {
                $unit.Add("Chapter: $($e.Quire)--" + $ln.Substring(8).Trim()); $renamed = $true
            } else { $unit.Add($ln) }
        }
        $unit.Add(''); $unit.Add('')
    }
    foreach ($ln in $body) { $unit.Add($ln) }
    foreach ($ln in @('', 'Chapter: SubsetCiteEntry', '', 'Section: Body', '', '  opening : Integer', '  opening = 0', '')) { $unit.Add($ln) }
    $src = Join-Path $work "$safe.codex"
    [System.IO.File]::WriteAllLines($src, $unit)
    $units += [pscustomobject]@{ Chapter = $chapter; Src = $src; Safe = $safe }
}

Write-Host "check-subset-cites: $($units.Count) chapter unit(s), -Jobs $Jobs"

$compile = Join-Path $PSScriptRoot 'compile.ps1'
$results = $units | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $u = $_
    $work = $using:work; $compile = $using:compile; $kernel = $using:Kernel
    & pwsh -NoProfile -File $compile -Src $u.Src -Out (Join-Path $work "$($u.Safe).cdx") -Log (Join-Path $work "$($u.Safe).log") -Kernel $kernel *> $null
    [pscustomobject]@{ Chapter = $u.Chapter; Code = $LASTEXITCODE; Log = (Join-Path $work "$($u.Safe).log") }
}

$internal = @{}
$external = @{}
$failed = @()
foreach ($r in $results) {
    if ($r.Code -eq 0) { continue }
    $failed += $r.Chapter
    foreach ($ln in (Get-Content $r.Log -ErrorAction SilentlyContinue)) {
        if ($ln -match 'CDX(3002|2002): (?:Undefined|Unknown) name: (\S+)') {
            $n = $matches[2]
            if ($ownNames[$n]) {
                $internal[$n] = $true
            } else {
                if (-not $external.ContainsKey($n)) { $external[$n] = @() }
                if ($external[$n] -notcontains $r.Chapter) { $external[$n] += $r.Chapter }
            }
        }
    }
}

Write-Host "  $($units.Count - $failed.Count) compiled standalone, $($failed.Count) did not"
Write-Host "  $($internal.Count) undefined name(s) INTERNAL to $Root (co-presence in the glob, not a defect)"

if (-not $KeepUnits) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }

if ($external.Count -eq 0) {
    Write-Host "check-subset-cites: OK (no chapter borrows a name from outside $Root without citing it)"
    exit 0
}

Write-Host ''
Write-Host "FAIL: check-subset-cites -- $($external.Count) name(s) borrowed from outside $Root with no cite"
foreach ($n in ($external.Keys | Sort-Object)) {
    Write-Host "  '$n' -- undefined when these chapters are built alone: $($external[$n] -join ', ')"
}
Write-Host ''
Write-Host "  Find the chapter that defines each name and cite it. The monolithic"
Write-Host "  build hides this; a plug bundle will not have the definition at all."
Write-Host "  Artifacts kept with -KeepUnits."
exit 1
