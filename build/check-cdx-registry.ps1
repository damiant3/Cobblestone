# check-cdx-registry.ps1 -- Nothing in the live tree reads the diagnostic catalogue, so it rots.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot)
)

# Nothing in the live tree reads the diagnostic catalogue, so it rots.
# 
# codex/compiler/Core/CdxCodes.codex is a table of every CDX code with its
# name, severity, phase and a one-line summary. The NUMERIC constants in it
# are used -- the emitters cite them -- but the registry ROWS are read by
# nobody: cdx-lookup has no caller outside archived material, and
# whole-program dead-code elimination prunes the whole table (measured at
# CL 10244: changing one summary left Sut.cdx byte-identical to the seed, so
# a string the compiler reached would have moved the binary and did not).
# 
# The consequence is that every row drifts against the code it describes with
# nothing able to notice. Two wrong summaries were caught by eye and fixed one
# at a time (CDX9004 at CL 10131, CDX6011 which called an instruction-count
# budget a "byte threshold"); there is no reason to think those were the only
# two, because the instrument that would say is the one that did not exist.
# 
# This is that instrument. It reads the catalogue -- the constant->code table,
# the registry rows, every raise site across codex/compiler (code lines only,
# never column-2 prose) and the host's own "error <N>:" raises -- and fails
# the build on the three ways they can disagree:
# 
#   REGISTERED-BUT-NEVER-RAISED  a row describes a code no site emits, so it
#                                is a promise the compiler does not keep (the
#                                CDX2043 class the chapter's own prose warns
#                                about) and its summary can never be seen.
#   RAISED-BUT-UNREGISTERED      a site emits a code with no row, so cdx-lookup
#                                answers "Unregistered CDX code" and the code
#                                is undocumented by construction.
#   NAME-MISMATCH                a row's Name string is not the PascalCase of
#                                its constant, which is the one half of a
#                                summary a machine CAN check.
# 
# It does not verify summary PROSE against behaviour -- no build-time check
# can -- but it makes the catalogue read by the build, which is what stops the
# structural rot.
# 
# Exit 1 on any disagreement.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$codesFile = (Join-Path $Root 'codex/compiler/Core/CdxCodes.codex')
if ((-not (Test-Path -PathType Leaf $codesFile))) {
    Write-Host ([string]'check-cdx-registry: CdxCodes.codex not found at ' + $codesFile)
    exit 1
}


# kebab-case constant -> PascalCase, the transform the registry Name should be.
function ConvertTo-Pascal([string]$kebab) {
    # drop the leading "cdx-", then TitleCase each hyphen segment
    $stem = ($kebab -replace '^cdx-', '')
    return ((($stem -split '-') | ForEach-Object { $(if (($_.Length -eq 0)) { '' } else { ([string]$_.Substring(0, 1).ToUpper() + $_.Substring(1)) }) }) -join '')
}


# 1. Constant declarations: "  cdx-foo-bar : Integer = 1234"
# 2. Registry rows: "mk-cdx cdx-foo-bar "Name" sev phase "summary""
#    A constant appears in a row iff `mk-cdx <const> "` is present.
$constToCode = @{}
$registered = @{}
foreach ($line in ([System.IO.File]::ReadAllLines($codesFile))) {
    if (($line -match '^\s+(cdx-[a-z0-9-]+)\s*:\s*Integer\s*=\s*(\d+)\s*$')) {
        $constToCode[$matches[1]] = [int]$matches[2]
    }
    if (($line -match 'mk-cdx\s+(cdx-[a-z0-9-]+)\s+"([^"]*)"')) {
        $registered[$matches[1]] = $matches[2]
    }
}


# 3. Raise sites: any reference to a cdx-* constant anywhere under
#    codex/compiler EXCEPT its own declaration and registry row in CdxCodes.
#    A referenced constant is one an emitter actually raises.
# 
#    COLUMN-2 PROSE IS SKIPPED, and that is not tidiness. Codex has no
#    comments; prose at column 2 is the commentary layer, and this scan used
#    to read it as code. CDX3010 passed for thirteen days on the strength of
#    one prose line in codex/compiler/opening.codex which SAID the only raise
#    site had been deleted -- the sentence reporting the absence was counted
#    as the presence. An instrument that reads a comment as a raise cannot
#    fail in the direction it exists to detect.

$raised = @{}
$compilerFiles = @(Get-ChildItem (Join-Path $Root 'codex/compiler') -Recurse -Filter '*.codex' -File)
foreach ($f in $compilerFiles) {
    $isCodesFile = ($f.FullName -eq (Resolve-Path $codesFile).Path)
    foreach ($line in ([System.IO.File]::ReadAllLines($f.FullName))) {
        # Column-2 prose is commentary, never a raise site.
        if (($line -match '^ [^ ]')) {
            continue
        }
        # In CdxCodes.codex, a bare "cdx-foo :" declaration and a "mk-cdx cdx-foo"
        # row are not raises; skip those two shapes there.
        if ($isCodesFile) {
            if (($line -match '^\s+cdx-[a-z0-9-]+\s*:\s*Integer\s*=')) {
                continue
            }
            if (($line -match 'mk-cdx\s+cdx-')) {
                continue
            }
        }
        foreach ($m in ([regex]::Matches($line, 'cdx-[a-z0-9-]+'))) {
            $name = $m.Value
            if ($constToCode.ContainsKey($name)) {
                $raised[$name] = $true
            }
        }
    }
}


# 3b. Host raise sites. The catalogue documents the whole compiler's
#     diagnostic surface, and not all of it is emitted from Codex: the build
#     resolver raises 3010 (cdx-missing-cite) because an unresolvable cite is a
#     condition the compiler never sees -- the host splices cited chapters in
#     before the compiler reads a byte. The host spells a raise "error <N>:",
#     so scan for that rather than carrying an allowlist, which would be a
#     second thing to drift.

$codeToConst = @{}
foreach ($k in $constToCode.Keys) {
    $codeToConst[$constToCode[$k]] = $k
}
$hostFiles = @()
foreach ($hr in @('build', 'tools')) {
    $hostFiles += @(Get-ChildItem (Join-Path $Root $hr) -Recurse -Filter '*.ps1' -File)
    $hostFiles += @(Get-ChildItem (Join-Path $Root $hr) -Recurse -Filter '*.psm1' -File)
}
foreach ($f in $hostFiles) {
    foreach ($m in ([regex]::Matches((Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue), 'error\s+(\d{3,4})\s*:'))) {
        $n = [int]$m.Groups[1].Value
        if ($codeToConst.ContainsKey($n)) {
            $raised[$codeToConst[$n]] = $true
        }
    }
}


$registeredNoRaiser = @()
$raisedNoRow = @()
$nameMismatch = @()

# Sorted, because .NET randomises string hashing per process and a hashtable
# walked in bucket order prints its findings in a different order run to run.
# The verdict does not move; the text does, which is enough to make a failure
# nobody can diff.

foreach ($const in ($constToCode.Keys | Sort-Object)) {
    $isReg = $registered.ContainsKey($const)
    $isRaised = $raised.ContainsKey($const)
    $code = $constToCode[$const]
    if (($isReg -and (-not $isRaised))) {
        $registeredNoRaiser += ('CDX{0:0000}  {1}  -- registered, raised nowhere' -f $code, $const)
    }
    if (($isRaised -and (-not $isReg))) {
        $raisedNoRow += ('CDX{0:0000}  {1}  -- raised, no registry row' -f $code, $const)
    }
    if ($isReg) {
        $want = (ConvertTo-Pascal $const)
        if (($registered[$const] -ne $want)) {
            $nameMismatch += ('CDX{0:0000}  {1}  -- row Name "{2}" is not "{3}"' -f $code, $const, $registered[$const], $want)
        }
    }
}


$total = ((@($registeredNoRaiser).Count + @($raisedNoRow).Count) + @($nameMismatch).Count)
if (($total -eq 0)) {
    Write-Host ('check-cdx-registry: OK ({0} codes, {1} registered, all raised codes documented)' -f $constToCode.Count, $registered.Count)
    exit 0
}

Write-Host 'check-cdx-registry: FAIL -- the diagnostic catalogue disagrees with the code'
if ((@($raisedNoRow).Count -gt 0)) {
    Write-Host ''
    Write-Host '  Raised but not in the registry (cdx-lookup answers ''Unregistered CDX code''):'
    foreach ($r in $raisedNoRow) {
        Write-Host ([string]'    ' + $r)
    }
}

if ((@($registeredNoRaiser).Count -gt 0)) {
    Write-Host ''
    Write-Host '  In the registry but raised by no site (a summary nothing can ever show):'
    foreach ($r in $registeredNoRaiser) {
        Write-Host ([string]'    ' + $r)
    }
}

if ((@($nameMismatch).Count -gt 0)) {
    Write-Host ''
    Write-Host '  Registry Name does not match the constant:'
    foreach ($r in $nameMismatch) {
        Write-Host ([string]'    ' + $r)
    }
}

Write-Host ''
Write-Host '  Add the row, delete the dead code, or fix the Name in CdxCodes.codex.'
exit 1
