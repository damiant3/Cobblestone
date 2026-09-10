# Hold a pipeline stage's DECLARED verdicts against the exits its own body spells.
#
# `ps-verdict` is the return of the pipeline-model campaign: it says what an exit
# code MEANS, where the script says only the number. Nothing checked it, and on
# 2026-09-08 two generators shipped `PoExit 1` for scripts that exit 2, 4, 5 and 6
# and never 1 (reek, main 24706). A verdict that states the wrong number is worse
# than no verdict, because a reader trusts it and no run contradicts it.
#
# WHAT IS DECIDABLE HERE, AND WHAT IS NOT. This reads the GENERATOR, not the
# shipped script, and that choice is measured rather than tidy. A census of
# `exit N` over the emitted PowerShell is not decidable by text: `exit` appears
# inside prose and inside strings (26 shipped scripts carry such a match), and a
# real code can reach `exit` through a variable, as `test-compile-batch` does with
# `$exitCode = '7'`. Both directions were measured before this instrument was
# chosen. The generator is structured data instead, so `ScExit (SeInt N)` in a
# code position is exact.
#
# The blind spot is therefore a RAW PAYLOAD: an `exit` inside `ScRaw` or `SeRaw`
# text is shell text the model cannot see into, so a verdict about it cannot be
# decided here. Those are counted and REPORTED, never failed, and a generator
# carrying any of them is exempt from the invented-code arm. That is honest rather
# than convenient: the raw payloads are what `check-shell-raw` exists to ratchet
# down, and every one removed makes a stage's verdict checkable.
#
# Usage:
#   check-pipe-verdicts.ps1           # compare against the record, fail a lie
#   check-pipe-verdicts.ps1 -Update   # rewrite build/pipe-verdict-baseline.txt
#   check-pipe-verdicts.ps1 -List     # per-generator table, no verdict
#
# Two arms, and only the first is a hard failure.
#
#   INVENTED: a `PoExit N` whose stage bodies never spell `ScExit (SeInt N)`, in a
#   generator with NO raw-payload exits to explain it. That is a wrong number and
#   it fails, with no residue: there is nothing to baseline about a false claim.
#
#   UNDECLARED: an `ScExit (SeInt N)` no stage declares. Filling these is per-stage
#   judgement, so the known set lives in build/pipe-verdict-baseline.txt and the
#   check fails only on a NEW one, or on one that is declared while the record
#   still lists it.
#
# It boots nothing and reads text.
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$List
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$GenDir = Join-Path $Repo 'codex\build'
$Baseline = Join-Path $PSScriptRoot 'pipe-verdict-baseline.txt'

# A string literal is stripped before any constructor is counted, so a
# constructor NAMED inside a comment or a payload is not read as a use. This is
# the same separation check-shell-raw makes, and it exists because wiring that
# check reported a comment naming ScRaw as a use of ScRaw.
function Split-CodeAndText([string]$text) {
    $code = [regex]::Replace($text, '"(?:[^"\\]|\\.)*"', '""')
    $strings = [regex]::Matches($text, '"(?:[^"\\]|\\.)*"') | ForEach-Object { $_.Value }
    return @{ Code = $code; Strings = @($strings) }
}

$rows = @()
foreach ($g in (Get-ChildItem (Join-Path $GenDir '*Script.codex') | Sort-Object Name)) {
    $text = [System.IO.File]::ReadAllText($g.FullName)
    if ($text -notmatch 'pl-stages') { continue }
    $name = [regex]::Match($text, 'pl-name\s*=\s*"([^"]+)"').Groups[1].Value
    if (-not $name) { $name = $g.BaseName }

    $split = Split-CodeAndText $text
    $spelled = @([regex]::Matches($split.Code, 'ScExit \(SeInt (\d+)\)') |
        ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
    $declared = @([regex]::Matches($split.Code, 'PoExit (\d+)') |
        ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)

    # An exit the model cannot see: shell text inside a raw payload.
    $rawExits = 0
    foreach ($s in $split.Strings) {
        $rawExits += ([regex]::Matches($s, '(?<![\w-])exit\s+\$?\w+')).Count
    }
    # A non-literal ScExit is the computed-exit vocabulary gap, not a lie either.
    $computed = ([regex]::Matches($split.Code, 'ScExit \((?!SeInt \d)')).Count
    $passthrough = ([regex]::Matches($split.Code, 'PoPassthrough')).Count

    $rows += [pscustomobject]@{
        Name       = $name
        Generator  = $g.BaseName
        Spelled    = $spelled
        Declared   = $declared
        RawExits   = $rawExits
        Computed   = $computed
        Passthrough = $passthrough
        Invented   = @($declared | Where-Object { $spelled -notcontains $_ })
        Undeclared = @($spelled | Where-Object { $declared -notcontains $_ })
    }
}

if ($List) {
    '{0,-26} {1,-16} {2,-16} {3,4} {4,4}' -f 'generator', 'ScExit', 'PoExit', 'raw', 'comp' | Write-Host
    foreach ($r in $rows) {
        '{0,-26} {1,-16} {2,-16} {3,4} {4,4}' -f $r.Name, ($r.Spelled -join ','), ($r.Declared -join ','), $r.RawExits, $r.Computed | Write-Host
    }
    Write-Host "$($rows.Count) migrated generators"
    exit 0
}

# Only a generator with NO unreadable exit can be held to the invented arm: where
# a raw payload or a computed exit could account for the code, the model cannot
# decide the claim and must not call it false.
$decidable = @($rows | Where-Object { $_.RawExits -eq 0 -and $_.Computed -eq 0 })
$lies = @($decidable | Where-Object { $_.Invented.Count -gt 0 })

function Read-VerdictBaseline([string]$path) {
    $t = @{}
    if (-not (Test-Path -PathType Leaf $path)) { return $t }
    foreach ($line in Get-Content $path) {
        $bare = ($line -split '#')[0]
        if ($bare.Trim() -eq '') { continue }
        $p = $bare -split '\s+' | Where-Object { $_ -ne '' }
        if ($p.Count -ge 2) { $t[$p[0]] = @($p[1] -split ',' | Where-Object { $_ -ne '' } | ForEach-Object { [int]$_ }) }
    }
    return $t
}

if ($Update) {
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('# pipe-verdict-baseline.txt -- generated by build/check-pipe-verdicts.ps1 -Update')
    $out.Add('#')
    $out.Add('# Exit codes a migrated generator SPELLS and no stage DECLARES, as')
    $out.Add('#   <generator> <codes>')
    $out.Add('#')
    $out.Add('# ps-verdict says what an exit code means. A code with no verdict is a')
    $out.Add('# refusal the model cannot explain; filling it is per-stage judgement, which')
    $out.Add('# is why this is a record and not an immediate failure. The check fails on a')
    $out.Add('# NEW undeclared code and on one that has been declared while this file still')
    $out.Add('# lists it, so the record shrinks as the verdicts are written.')
    $out.Add('#')
    $out.Add('# A verdict that names a code the generator never exits is a LIE and is never')
    $out.Add('# recorded here: that arm fails outright.')
    $out.Add('#')
    $out.Add("# Measured $((Get-Date).ToString('yyyy-MM-dd')) over $($rows.Count) migrated generators.")
    $out.Add('')
    foreach ($r in ($rows | Where-Object { $_.Undeclared.Count -gt 0 })) {
        $out.Add(('{0} {1}' -f $r.Name, (($r.Undeclared | Sort-Object) -join ',')))
    }
    Set-Content -Path $Baseline -Value $out -Encoding ascii
    Write-Host "check-pipe-verdicts: baseline written, $(@($rows | Where-Object { $_.Undeclared.Count -gt 0 }).Count) generator(s) with an undeclared exit"
    exit 0
}

$base = Read-VerdictBaseline $Baseline
$newUnd = New-Object System.Collections.Generic.List[string]
$fixedUnd = New-Object System.Collections.Generic.List[string]
foreach ($r in $rows) {
    $was = if ($base.ContainsKey($r.Name)) { $base[$r.Name] } else { @() }
    $fresh = @($r.Undeclared | Where-Object { $was -notcontains $_ })
    $gone  = @($was | Where-Object { $r.Undeclared -notcontains $_ })
    if ($fresh.Count -gt 0) { $newUnd.Add(("  {0}: exits {1} and no stage declares it" -f $r.Name, ($fresh -join ','))) }
    if ($gone.Count -gt 0)  { $fixedUnd.Add(("  {0}: {1} is declared now" -f $r.Name, ($gone -join ','))) }
}

Write-Host "check-pipe-verdicts: $($rows.Count) migrated generators, $($decidable.Count) with every exit readable by the model"

if ($lies.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL: $($lies.Count) generator(s) declare an exit code the script never exits."
    foreach ($l in $lies) {
        Write-Host ("  {0}: declares {1}, spells {2}" -f $l.Name, ($l.Invented -join ','), (($l.Spelled -join ',')))
    }
    Write-Host '  A verdict that states the wrong number is worse than no verdict: a reader'
    Write-Host '  trusts it and no run contradicts it. Read the ScExit sites and correct the'
    Write-Host '  PoExit code, or delete the outcome. This arm has no baseline.'
    exit 1
}

if ($newUnd.Count -gt 0 -or $fixedUnd.Count -gt 0) {
    Write-Host ''
    if ($newUnd.Count -gt 0) {
        Write-Host "FAIL: $($newUnd.Count) generator(s) gained an exit code no stage declares."
        $newUnd | ForEach-Object { Write-Host $_ }
        Write-Host '  Write the outcome on the stage that exits, or record it: -Update'
    }
    if ($fixedUnd.Count -gt 0) {
        Write-Host "FAIL: $($fixedUnd.Count) generator(s) declared a recorded code and the record still lists it."
        $fixedUnd | ForEach-Object { Write-Host $_ }
        Write-Host '  Lower the record in the same changelist: -Update'
    }
    exit 1
}

Write-Host 'check-pipe-verdicts: level with the baseline'
exit 0
