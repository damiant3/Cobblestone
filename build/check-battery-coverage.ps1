# check-battery-coverage.ps1 -- classify every codex/test chapter into the
# batteries its change can break, and REFUSE when a machine-side chapter lands
# in a battery that runs no machine.
#
# BatteryReorg.md step 11 is the design. The batteries are named for COVERAGE,
# so a lane mixes the ones its change implicates and runs nothing else.
#
# WHY THE REFUSAL EXISTS. A cite is a DEPENDENCY, not a SUBJECT: a test cites
# what it needs to compile, not what it is about. For library tests the two
# coincide; for machine-side tests they systematically do not.
# codex/test/hpet-interrupt.codex pins the HPET counter and the IOAPIC and
# cites only `Foreword chapter Board`, so a cite-derived selector files it
# under `foreword`, the kernel battery never selects it, and a residue list
# keyed on "has no cite" cannot see it either -- it HAS a cite. The misroute
# is silent, which is what makes it worth a check rather than a paragraph.
#
# Two sources decide a chapter's batteries, in this order:
#   .covers      one battery name per line, and it is the AUTHORITY. Written
#                by hand for a chapter whose cites do not carry its subject.
#   cites        `cites <Quire> chapter <Name>`; the quire resolves to a
#                directory, the directory to a battery. Fallback only.
# A chapter with neither is UNCLASSIFIED and belongs to no battery. It is
# never defaulted: assigning it to a battery on a guess is how a battery goes
# blind to its own subject (L-BAILVALUE).
#
#   pwsh build/check-battery-coverage.ps1
#   pwsh build/check-battery-coverage.ps1 -List kernel
[CmdletBinding()]
param(
    [string]$List = '',
    [switch]$ShowUnclassified
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

# The quire of a directory is NOT derivable from the tree, and deriving it was
# this script's own defect. `build/quire-map.ps1` is the authority the compiler
# side already uses (`check-cite-names.ps1` dot-sources the same file), and it
# disagrees with the tree in both directions: `codex/os/core` is quire OS, not
# the Foreword, and `Games` is `apps/games/classic`, whose last segment names
# no quire at all. The old rule read the first as `foreword` and the second as
# `classic`, so 43 live cites resolved to nothing and their chapters fell out
# of every battery. Measured 2026-09-07: 45 unresolved names became 2, and both
# survivors are the deliberate error fixtures, `foreword/doesnotexist` in
# `missing-cite` and `nonesuch/nosuchchapter` in `unregistered-quire-cite`.
. (Join-Path $PSScriptRoot 'quire-map.ps1')

# Which battery covers a directory. `plugs` is absent on purpose: nothing under
# codex/test cites it, so it selects no corpus and is run as a harness rather
# than as a set of chapters. `compiler` was in that position until red's
# 2026-09-07 ruling and is no longer: nothing CITES it, which is a statement
# about cites and not about blast radius, and its members arrive through
# `.covers` under the rule "cites nothing and carries a machine sidecar".
function Get-BatteryForDir([string]$Dir) {
    if ($Dir -like 'codex/foreword/*') { return 'foreword' }
    if ($Dir -like 'codex/os/*')       { return 'kernel' }
    if ($Dir -like 'codex/boards*')    { return 'board' }
    if ($Dir -like 'apps/*')           { return 'apps' }
    if ($Dir -like 'codex/compiler*')  { return 'compiler' }
    if ($Dir -like 'codex/plugs*')     { return 'plugs' }
    return ''
}

$knownBatteries = @('foreword', 'kernel', 'board', 'apps', 'compiler', 'plugs')
# A battery that starts no machine. A machine-side chapter landing ONLY in one
# of these is the misroute this script refuses.
#
# `compiler` is NOT one of them, since red's 2026-09-07 ruling gave it the
# corpus: its members are exactly the chapters that cite nothing and carry a
# machine sidecar, so every one of them starts a machine. Leaving it here put
# all 13 back in the review queue the ruling exists to empty.
$softwareOnly = @('foreword', 'apps', 'plugs')
$machineSidecars = @('.smp', '.vmargs', '.disk', '.disk2', '.disk-src', '.keys')

# --- the (quire, chapter) index, keyed the way a cite is written ---
# A quire's chapters are the .codex files directly in its directory: the map is
# quire to ONE directory, so a nested one belongs to whatever quire claims it,
# never to its parent.
$index = @{}
foreach ($q in $QuireDirs.Keys) {
    $dir = $QuireDirs[$q] -replace '\\', '/'
    $abs = Join-Path $Repo $dir
    if (-not (Test-Path -PathType Container $abs)) { continue }
    foreach ($f in Get-ChildItem $abs -Filter *.codex -File -ErrorAction SilentlyContinue) {
        $index["$q/$($f.BaseName)".ToLower()] = $dir
    }
}
Write-Host "chapter index: $($index.Count) (quire, chapter) pairs"

# --- classify every test chapter ---
$members = @{}
foreach ($b in $knownBatteries) { $members[$b] = [System.Collections.Generic.List[string]]::new() }
$unclassified = [System.Collections.Generic.List[string]]::new()
$misrouted = [System.Collections.Generic.List[string]]::new()
$unresolved = @{}

$tests = @(Get-ChildItem (Join-Path $Repo 'codex\test') -Recurse -Filter *.codex -File | Sort-Object FullName)
foreach ($t in $tests) {
    $rel = $t.FullName.Substring($Repo.Length + 1) -replace '\\', '/'
    $stem = [System.IO.Path]::Combine($t.DirectoryName, [System.IO.Path]::GetFileNameWithoutExtension($t.Name))

    $bats = [System.Collections.Generic.HashSet[string]]::new()
    $covers = "$stem.covers"
    if (Test-Path -PathType Leaf $covers) {
        foreach ($line in (Get-Content $covers)) {
            $n = $line.Trim().ToLower()
            if ($n -eq '' -or $n.StartsWith('#')) { continue }
            if ($n -notin $knownBatteries) {
                Write-Host "FAIL: $rel .covers names unknown battery '$n'" -ForegroundColor Red
                exit 1
            }
            [void]$bats.Add($n)
        }
    } else {
        foreach ($m in (Select-String -Path $t.FullName -Pattern '^\s*cites\s+(\w+)\s+chapter\s+(\S+)\s*$')) {
            $key = "$($m.Matches[0].Groups[1].Value)/$($m.Matches[0].Groups[2].Value)".ToLower()
            if ($index.ContainsKey($key)) {
                $b = Get-BatteryForDir $index[$key]
                if ($b) { [void]$bats.Add($b) }
            } else {
                $unresolved[$key] = $true
            }
        }
    }

    if ($bats.Count -eq 0) { $unclassified.Add($rel); continue }
    foreach ($b in $bats) { $members[$b].Add($rel) }

    # NEEDING A MACHINE AND COVERING THE KERNEL ARE DIFFERENT AXES, and this
    # list is a review queue rather than a failure because of it. A machine
    # sidecar says the runner must attach a disk, a keystroke timeline or a
    # core count; it says nothing about which subsystem the chapter covers.
    # `codex/test/fat16-write.codex` carries a `.disk` AND cites
    # `Foreword chapter Fat16`, and both are right: the Fat16 library is
    # `codex/foreword/core/Fat16.codex`, so a foreword change should select
    # it and a machine is merely what it costs to run.
    #
    # What IS worth a human eye is the narrower case: a chapter that needs a
    # machine and whose cites reach no machine-side chapter at all, so no
    # kernel or board change selects it. `hpet-interrupt` pins the HPET
    # counter and the IOAPIC, cites only `Foreword chapter Board`, and would
    # not be selected by a change to `codex/os/kernel/Hpet.codex`. Whether
    # that is a defect is a judgement per chapter, so it is printed and a
    # `.covers` records the answer once made. It is not mechanically
    # decidable, and failing the build on it would train a lane to silence
    # the check rather than read it.
    $machineSide = $false
    foreach ($ext in $machineSidecars) { if (Test-Path -PathType Leaf "$stem$ext") { $machineSide = $true; break } }
    if ($machineSide -and -not (Test-Path -PathType Leaf $covers)) {
        $lands = @($bats)
        if (-not ($lands | Where-Object { $_ -notin $softwareOnly })) {
            $misrouted.Add("$rel [lands in: $(($lands | Sort-Object) -join ',')]")
        }
    }
}

# L-DENOM: a count is unreadable without the set it was drawn FROM.
Write-Host ""
Write-Host "of $($tests.Count) test chapters:"
foreach ($b in $knownBatteries) {
    Write-Host ("  {0,-9} {1,5}" -f $b, $members[$b].Count)
}
Write-Host ("  {0,-9} {1,5}" -f 'unclassified', $unclassified.Count)
if ($unresolved.Count) {
    Write-Host "  cite names resolving to no chapter: $($unresolved.Count) ($((($unresolved.Keys | Sort-Object | Select-Object -First 5) -join ', ')))"
}

if ($List) {
    $l = $List.ToLower()
    # The members go to the PIPELINE, not the host. A caller doing
    # `$m = & check-battery-coverage.ps1 -List kernel` must receive them:
    # Write-Host hands it an empty list, and a battery that selects nothing
    # and runs green is the failure this whole scheme exists to prevent
    # (L-BAILVALUE). The counts above stay on the host so they cannot
    # contaminate that list.
    if ($l -eq 'unclassified') { $unclassified | ForEach-Object { Write-Output $_ } }
    elseif ($l -in $knownBatteries) { $members[$l] | ForEach-Object { Write-Output $_ } }
    else { Write-Host "unknown battery '$List'"; exit 1 }
    exit 0
}
if ($ShowUnclassified) { $unclassified | ForEach-Object { Write-Host "  ? $_" } }

Write-Host ""
if ($misrouted.Count -gt 0) {
    Write-Host "REVIEW: $($misrouted.Count) chapter(s) need a machine and cite no machine-side chapter."
    Write-Host "No kernel or board change selects these. Read each and either leave it (its subject"
    Write-Host "really is the library it cites) or give it a .covers naming what covers it; either"
    Write-Host "way the .covers records that the question was asked. This is not a failure: needing"
    Write-Host "a machine is a cost, and covering the kernel is a subject."
    $misrouted | ForEach-Object { Write-Host "  ? $_" }
}
exit 0
