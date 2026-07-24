# Refuse to proceed while an opened file is behind the depot.
#
# WHY THIS EXISTS
#
# `p4 unshelve` restores the shelved content and opens the file at the
# revision it was shelved AT -- not at head. If the depot moved while the work
# was shelved (which is exactly what a merge-down does), the workspace file is
# now the shelved content, and every revision submitted in between is missing
# from it.
#
# Perforce does not schedule the resolve at unshelve time. `p4 resolve -n`
# answers "No file(s) to resolve", and `p4 fstat` carries no `unresolved` flag.
# So the one command you would reach for to check says you are clean when you
# are not. Measured on 2026-07-13: after a merge-down, an unshelved doc
# sat at haveRev 29 against headRev 31, with resolve reporting nothing to do.
#
# `p4 submit` does block on this ("out of date"), so the depot is not at risk
# from doing nothing. The risk is the recovery: an agent who has just been told
# there is nothing to resolve reaches for `p4 resolve -ay` (accept yours) to get
# moving, and that silently drops every revision another agent submitted while
# the work was shelved.
#
# THE FIX, and it is two commands:
#
#     p4 unshelve -s <CL> -c <CL>
#     p4 sync                  # THIS is what schedules the resolve
#     p4 resolve -am           # three-way merge: shelved content + new head
#     build/p4-stale-check.ps1 # assert it worked
#
# `p4 sync` on an opened, out-of-date file is what flags it unresolved. Verified:
# the auto-merge then keeps both sides.
[CmdletBinding()]
param(
    [string]$Change = ''   # optional: restrict to one changelist
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$opened = if ($Change) { p4 opened -c $Change 2>&1 } else { p4 opened 2>&1 }
if (-not $opened -or ($opened -join '') -match 'not opened on this client') {
    Write-Host "p4-stale-check: OK (nothing opened)"
    exit 0
}

# -Ro reports the opened files; one fstat call covers them all.
$tag = p4 -ztag fstat -Ro //... 2>&1

$file = $null; $have = $null; $head = $null; $action = $null; $unres = $null
$stale = @()
$unresolved = @()

function Test-Record {
    if (-not $script:file) { return }
    # An 'add' has no depot revision to be behind.
    if ($script:action -eq 'add' -or $script:action -eq 'branch') { return }
    if ($script:unres) { $script:unresolved += $script:file; return }
    if ($script:have -and $script:head -and ([int]$script:have -lt [int]$script:head)) {
        $script:stale += "$($script:file)  (opened at #$($script:have), depot head is #$($script:head))"
    }
}

foreach ($line in $tag) {
    if ($line -match '^\.\.\. depotFile (.+)$') {
        Test-Record
        $file = $Matches[1]; $have = $null; $head = $null; $action = $null; $unres = $null
    }
    elseif ($line -match '^\.\.\. haveRev (\d+)$')  { $have = $Matches[1] }
    elseif ($line -match '^\.\.\. headRev (\d+)$')  { $head = $Matches[1] }
    elseif ($line -match '^\.\.\. action (\S+)$')   { $action = $Matches[1] }
    elseif ($line -match '^\.\.\. unresolved')      { $unres = $true }
}
Test-Record

$bad = $false

if ($unresolved) {
    Write-Host ""
    Write-Host "UNRESOLVED -- these files need 'p4 resolve' before they can be submitted:"
    $unresolved | ForEach-Object { Write-Host "    $_" }
    $bad = $true
}

if ($stale) {
    Write-Host ""
    Write-Host "STALE OPEN FILES -- these are BEHIND the depot."
    Write-Host "Your copy does not contain what was submitted while it was shelved."
    Write-Host "Do NOT resolve with -ay (accept yours): that drops the other agent's work."
    Write-Host ""
    $stale | ForEach-Object { Write-Host "    $_" }
    Write-Host ""
    Write-Host "  p4 sync           # schedules the resolve that unshelve did not"
    Write-Host "  p4 resolve -am    # three-way merge, keeps both sides"
    Write-Host "  build/p4-stale-check.ps1"
    $bad = $true
}

# A file that is on disk and not in the depot.
#
# This is the one that actually cost us. `p4 unshelve` prints
#
#     Can't clobber writable file <path>
#
# for a file opened for ADD whose copy is already on disk -- which is always,
# because reverting an add leaves the file behind. It does NOT open the file.
# The line above it says "unshelved, opened for add", so it reads like a
# harmless warning. It is not: the add is gone from the changelist, the edits
# in the same CL submit perfectly, and the new file is silently left out.
#
# Every test added during 2026-07-13 was lost this way -- smp-cores, smp-tss,
# the SPIR-V probe and its checker -- while the register cheerfully named them as
# pinned. Nothing caught it, because a dropped add is not a conflict, not a
# stale revision, and not an unresolved file. It is simply absent.
#
# Reported as a warning, not a failure: scratch and lock files land here too,
# and the operator is the one who knows which is which. Read the list.
$untracked = @(p4 status 2>&1 | Select-String 'reconcile to add' | ForEach-Object { $_.Line })
if ($untracked) {
    Write-Host ""
    Write-Host "ON DISK BUT NOT IN THE DEPOT -- is one of these a p4 add that got dropped?"
    Write-Host "(p4 unshelve reports it cannot clobber a writable file, and then does not open the add.)"
    Write-Host ""
    $untracked | ForEach-Object { Write-Host "    $_" }
    Write-Host ""
    Write-Host "  If a file here belongs in your change:  p4 add -c <CL> <file>"
}

if ($bad) { Write-Host ""; Write-Host "p4-stale-check: FAIL"; exit 1 }
Write-Host "p4-stale-check: OK (every open file is at depot head and resolved)"
exit 0
