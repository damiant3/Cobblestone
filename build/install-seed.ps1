# install-seed.ps1 -- install a proven compiler as seed/Codex.cdx.
#
# Hand-written. There is no generator for this file.
#
# THE POINT OF THIS SCRIPT IS THAT IT READS THE VERDICT OF THE RUN THAT
# PRODUCED THE ARTIFACT. A seed that did not contain its own fix reached main
# (21215) while the gate was on screen refusing it: the gate's refusal is
# prose, `Copy-Item` cannot consult it, and the operator justified the install
# from a one-pass line grepped out of a DIFFERENT CL's log. That is L-BODY --
# the most expensive step in the plan guarded by the weakest guard in the tree.
#
# build.ps1 writes build/output/seed-verdict.txt on every run, naming the
# outcome, the artifact that IS the fixed point, and its whole-file SHA-256.
# This script refuses everything except a one-pass verdict that is newer than
# the artifact it names and whose hash matches the bytes on disk.
#
# IT BOOTS ONE GUEST (test-self-verify.ps1), so it is a run to ask the
# commander for. A seed lands signed and self-verified or not at all.
#
# LANE MODE. build.ps1 is the only writer of seed-verdict.txt and no lane may
# run that gate (CLAUDE.md R-GATE bans -Internal and reserves the bare gate for
# Damian), so a lane following the R-GATE recipe produced a seed this script
# would refuse, and the stopgap was a verdict written by hand. -Lane replaces
# the claim with the proof: it compiles the source WITH the candidate and
# requires the result to be content-identical to the candidate. That is a
# one-pass fixed point re-derived in THIS run, which is the property the whole
# file exists to protect, and unlike a prose verdict it cannot be satisfied by
# handing the script three copies of one binary.
#
# It compares the CONTENT hash at bytes 8..39 and not the whole file, because
# the candidate is signed and the stage it produces is not: the signature lives
# at 40..135 and is stamped by the sign phase, so identical payloads differ
# there by construction (P-SIGNED).
#
# What lane mode does NOT verify, and says so in the verdict it writes: the
# BVT, and the gate's regression phases. Those stay the lane's own runs under
# R-GATE, exactly as they do for a gate-written verdict.
#
# Usage:
#   build/install-seed.ps1              # install, self-verify, refresh digests
#   build/install-seed.ps1 -WhatIf      # say what it would do and why
#   build/install-seed.ps1 -Lane -Cdx build\output\Sut.cdx -Source <self.codex>
#   build/install-seed.ps1 -Lane -Cdx ... -Source ... -CheckOnly   # prove it, install nothing
[CmdletBinding()]
param(
    [string]$Verdict = '',
    [string]$Seed = '',
    [switch]$WhatIf,
    [switch]$Lane,
    [string]$Cdx = '',
    [string]$Source = '',
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$WorkDirDefault = Join-Path $Repo 'build\output'
if (-not $Verdict) { $Verdict = Join-Path $WorkDirDefault 'seed-verdict.txt' }
if (-not $Seed)    { $Seed    = Join-Path $Repo 'seed\Codex.cdx' }
$TechDetails = Join-Path $Repo 'TechnicalDetails.md'

function Deny([string]$Why) {
    Write-Host "REFUSED: $Why"
    exit 1
}

function Get-CdxContentField([string]$Path) {
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $buf = New-Object byte[] 32
        $null = $fs.Seek(8, [System.IO.SeekOrigin]::Begin)
        $read = $fs.Read($buf, 0, 32)
        if ($read -ne 32) { return '' }
        return (($buf | ForEach-Object { $_.ToString('X2') }) -join '')
    } finally { $fs.Dispose() }
}

# -- 1L. lane mode: re-derive the fixed point rather than read a claim about it

if ($Lane) {
    if (-not $Cdx)    { Deny '-Lane needs -Cdx <candidate>, the signed compiler you propose to install.' }
    if (-not $Source) { Deny '-Lane needs -Source <file>, the concatenated compiler source the candidate was built from (build/concat-codex-self.ps1 writes it).' }
    if (-not (Test-Path -PathType Leaf $Cdx))    { Deny "no such candidate: $Cdx" }
    if (-not (Test-Path -PathType Leaf $Source)) { Deny "no such source: $Source" }

    $artifact = (Resolve-Path -LiteralPath $Cdx).Path
    $artifactName = Split-Path -Leaf $artifact
    $outcome = 'one-pass'

    if ($CheckOnly -and $WhatIf) { Deny 'pick one of -CheckOnly and -WhatIf.' }

    if ($WhatIf) {
        Write-Host "would compile $Source with $artifactName, require the result to be content-identical to it,"
        Write-Host "then install over $Seed, self-verify, and refresh the four seed/Codex.cdx claims in TechnicalDetails.md"
        Write-Host 'NOTE: -WhatIf does NOT run the confirming compile, so it proves nothing about the fixed point.'
        exit 0
    }

    $laneDir = Join-Path $WorkDirDefault 'lane-proof'
    New-Item -ItemType Directory -Force -Path $laneDir | Out-Null
    $stage = Join-Path $laneDir 'confirm.cdx'
    $stageLog = Join-Path $laneDir 'confirm.log'
    Remove-Item -LiteralPath $stage -ErrorAction SilentlyContinue

    Write-Host "confirming the fixed point: compiling $(Split-Path -Leaf $Source) with $artifactName ..."
    $compile = Join-Path $PSScriptRoot 'compile.ps1'
    & pwsh -NoProfile -File $compile -Src $Source -Out $stage -Log $stageLog -Kernel $artifact -Repl -MemMB 3072 *> (Join-Path $laneDir 'confirm.console')
    if ($LASTEXITCODE -ne 0) { Deny "the candidate could not compile the source. Its log is $stageLog. A compiler that cannot build the source is not a seed." }
    if (-not (Test-Path -PathType Leaf $stage)) { Deny "the confirming compile reported success and emitted no binary. Log: $stageLog." }

    $candField = Get-CdxContentField $artifact
    $stageField = Get-CdxContentField $stage
    if (-not $candField -or -not $stageField) { Deny 'could not read the content-hash field at bytes 8..39 from one of the two binaries.' }

    if ($candField -ne $stageField) {
        Write-Host 'REFUSED: the candidate is NOT a one-pass fixed point.'
        Write-Host "         candidate content hash: $candField"
        Write-Host "         it compiled the source to: $stageField"
        Write-Host '         The compiler it produces is not itself, so installing it ships a seed'
        Write-Host '         that does not reproduce itself (PerforceProcess 4.3a, P-STAGE2).'
        Write-Host "         Install $stage, rebuild from it, and confirm again."
        exit 1
    }
    Write-Host "fixed point CONFIRMED here: content hash $candField, both binaries."

    if ($CheckOnly) {
        Write-Host ''
        Write-Host 'CheckOnly: nothing installed, no verdict written.'
        exit 0
    }

    $statedHash = (Get-FileHash -Algorithm SHA256 $artifact).Hash

    # The verdict goes BESIDE the artifact it names, because section 1 resolves
    # the artifact relative to the verdict's own directory. For a candidate in
    # build/output, the usual place, that is build/output/seed-verdict.txt and
    # nothing moves. Found by running the whole path rather than -CheckOnly:
    # a candidate anywhere else produced a verdict naming a file that was not
    # where the reader looked for it.
    $Verdict = Join-Path (Split-Path -Parent $artifact) 'seed-verdict.txt'
    $laneLines = @(
        "$outcome $artifactName $statedHash",
        '',
        'Written by build/install-seed.ps1 -Lane, not by build.ps1.',
        '',
        'VERIFIED IN THIS RUN:',
        "  one-pass fixed point: $(Split-Path -Leaf $Source) compiled with $artifactName produced",
        "  a binary whose content hash at bytes 8..39 equals the candidate's, $candField.",
        '  The self-verify below is run against the installed seed and must pass or it is rolled back.',
        '',
        'NOT VERIFIED HERE, and still the lane''s own runs under R-GATE:',
        '  the BVT, and the gate regression phases (jonquil, the plug phases, gen-scripts,',
        '  deck-headroom, vm-differential, app-sweep, the text phases). The next full gate',
        '  is the first thing that will have run them.'
    )
    Set-Content -LiteralPath $Verdict -Encoding ascii -Value $laneLines
    Write-Host "verdict written: $Verdict"
}

# -- 1. the verdict must exist, and it must be THIS run's

if (-not (Test-Path -PathType Leaf $Verdict)) {
    Deny "no verdict file at $Verdict. Only a gate run writes one, or this script's own -Lane mode after it re-derives the fixed point. A seed install without one is the 21215 failure exactly."
}

$line = (Get-Content -LiteralPath $Verdict -TotalCount 1).Trim()
$parts = $line -split '\s+'
if ($parts.Count -ne 3) { Deny "verdict file is malformed: '$line'. Expected '<outcome> <artifact> <sha256>'." }
$outcome, $artifactName, $statedHash = $parts

if ($outcome -eq 'core-skipped') {
    Deny 'the gate DEFERRED its core, so it built no compiler and named no fixed point. Nothing to install.'
}
if ($outcome -eq 'two-pass') {
    Deny @'
the gate converged on the SECOND pass. The fixed point is stage1, and
         build\output\Sut.cdx is the PRE-CONVERGENCE binary: installing it ships a
         compiler that does not reproduce itself (PerforceProcess 4.3a, P-STAGE2).
         Install build\output\NewSeed.cdx by hand, re-run the gate, THEN run this.
'@
}
if ($outcome -ne 'one-pass') { Deny "unrecognised verdict '$outcome'." }

$artifact = Join-Path (Split-Path -Parent $Verdict) $artifactName
if (-not (Test-Path -PathType Leaf $artifact)) { Deny "the verdict names $artifactName and it is not on disk at $artifact." }

# A verdict older than the artifact it names is the same error one step over:
# the file was rebuilt after the run that judged it, so the judgement is about
# bytes that are gone.
$vTime = (Get-Item -LiteralPath $Verdict).LastWriteTimeUtc
$aTime = (Get-Item -LiteralPath $artifact).LastWriteTimeUtc
if ($vTime -lt $aTime) {
    Deny "the verdict ($($vTime.ToString('s'))Z) is OLDER than $artifactName ($($aTime.ToString('s'))Z). The artifact was rebuilt after the run that judged it. Re-run the gate."
}

# -- 2. the bytes about to be copied must be the bytes that were judged

$actualHash = (Get-FileHash -Algorithm SHA256 $artifact).Hash
if ($actualHash -ne $statedHash) {
    Write-Host "REFUSED: $artifactName does not match the hash in the verdict."
    Write-Host "         verdict: $statedHash"
    Write-Host "         on disk: $actualHash"
    exit 1
}

Write-Host "verdict:  $outcome $artifactName"
Write-Host "artifact: $artifact"
Write-Host "sha256:   $actualHash"

if ($WhatIf) {
    Write-Host ''
    Write-Host "would install over $Seed, self-verify, and refresh the four seed/Codex.cdx claims in TechnicalDetails.md"
    exit 0
}

# -- 3. install

foreach ($f in @($Seed, $TechDetails)) {
    if ((Test-Path -PathType Leaf $f) -and (Get-Item -LiteralPath $f).IsReadOnly) {
        Deny "$f is read-only. Open it first: p4 edit $f"
    }
}

$backup = Join-Path $env:TEMP ("Codex-seed-before-install-" + (Get-Date).ToString('yyyyMMdd-HHmmss') + ".cdx")
Copy-Item -LiteralPath $Seed -Destination $backup -Force
Copy-Item -LiteralPath $artifact -Destination $Seed -Force
Write-Host "installed. previous seed kept at $backup"

# -- 4. self-verify, and put the old seed back if it fails

$selfVerify = Join-Path $PSScriptRoot 'test-self-verify.ps1'
$svOut = & pwsh -NoProfile -File $selfVerify -Seed $Seed -Kernel $Seed 2>&1
$svOut | ForEach-Object { Write-Host "  $_" }
if (($LASTEXITCODE -ne 0) -or (-not ($svOut -match 'THE SEED VERIFIES ITSELF'))) {
    Copy-Item -LiteralPath $backup -Destination $Seed -Force
    Write-Host ''
    Write-Host 'FAIL: the installed seed does not verify itself. The previous seed has been restored.'
    exit 1
}

# -- 5. the four seed/Codex.cdx claims in TechnicalDetails.md
#
# check-doc-counts.ps1 owns the patterns and is the reader; this only rewrites
# the four values it measures over seed/Codex.cdx, because they are stale by
# construction the moment the seed moves.

$bytes = (Get-Item -LiteralPath $Seed).Length
$sha = (Get-FileHash -Algorithm SHA256 $Seed).Hash.ToUpperInvariant()
$md5 = (Get-FileHash -Algorithm MD5 $Seed).Hash.ToUpperInvariant()
$seedBytes = [System.IO.File]::ReadAllBytes($Seed)
$contentPrefix = (($seedBytes[8..15] | ForEach-Object { $_.ToString('X2') }) -join '')

$text = [System.IO.File]::ReadAllText($TechDetails)
$before = $text
$text = [regex]::Replace($text, '(\*\*`seed/Codex\.cdx`\*\* \()[\d,]+( bytes)', { param($m) $m.Groups[1].Value + $bytes.ToString('N0') + $m.Groups[2].Value })
$text = [regex]::Replace($text, '(seed/Codex\.cdx[\s\S]*?\| Content hash prefix \| `)[0-9A-Fa-f]{16}', { param($m) $m.Groups[1].Value + $contentPrefix })
$text = [regex]::Replace($text, '(seed/Codex\.cdx[\s\S]*?\| SHA-256 \| `)[0-9A-Fa-f]{64}', { param($m) $m.Groups[1].Value + $sha })
$text = [regex]::Replace($text, '(seed/Codex\.cdx[\s\S]*?\| MD5 \| `)[0-9A-Fa-f]{32}', { param($m) $m.Groups[1].Value + $md5 })
if ($text -ne $before) {
    [System.IO.File]::WriteAllText($TechDetails, $text)
    Write-Host 'TechnicalDetails.md: seed bytes, content prefix, SHA-256 and MD5 refreshed'
    Write-Host '                     the date and reason beside the byte count are yours to write'
} else {
    Write-Host 'TechnicalDetails.md: already current'
}

# The independent reader. Other claims may legitimately be drifted (a lane
# does not fix unrelated counts to satisfy a dev gate); the four this script
# just wrote may not be.
$dc = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-doc-counts.ps1') 2>&1
$seedRows = $dc | Where-Object { $_ -match 'seed cdx' }
$seedRows | ForEach-Object { Write-Host "  $_" }
if ($seedRows -match 'DRIFT') {
    Write-Host ''
    Write-Host 'FAIL: a seed claim in TechnicalDetails.md is still drifted after the refresh.'
    exit 1
}

Write-Host ''
Write-Host 'THE SEED VERIFIES ITSELF. Installed and recorded.'
