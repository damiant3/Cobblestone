# Record the source facts of a submitted changelist into the fact store.
#
# Nothing used to record source facts after a p4 submit. This is the
# answer. Given a submitted changelist, it reads each source file back OUT OF
# THE DEPOT at the revision that changelist created and stores it as a signed,
# content-addressed work. That is the post-submit half of the cutover: Perforce
# stays the thing people submit to, and every submit also lands in the store,
# so the store is a live mirror rather than a snapshot somebody took once.
#
# The content comes from `p4 print` at the submitted revision, never from the
# workspace. The workspace moves on -- a later edit, a merge-down, a revert --
# and the fact this changelist created is the depot revision, not whatever is
# on disk when the hook happens to run. Reading the workspace would file the
# work under an address for text that changelist never contained.
#
# Re-running is safe and is expected to be a no-op: the store is
# content-addressed, so the same revision yields the same digest.
#
# Usage:
#   build/store-changelist.ps1 -Change 9583
#   build/store-changelist.ps1 -Change 9583 -Disk build-output/repo-store.img
#   build/store-changelist.ps1 -Change 9583 -WhatIf
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [int]$Change,
    [string]$Disk = 'build-output/repo-store.img',
    [string]$Kernel = '',
    [string]$Filter = '\.codex$',
    [switch]$WhatIf
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }

# The changelist must be submitted. A pending changelist has no revisions to
# read, so storing one would either fail obscurely or store the wrong text.
$desc = & p4 describe -s $Change 2>&1
if ($LASTEXITCODE -ne 0) { Fail "cannot describe change $Change" }
$descText = ($desc | Out-String)
if ($descText -match '\*pending\*') { Fail "change $Change is pending, not submitted; there is nothing to record yet" }

# `p4 describe -s` lists one line per file as "... //depot/path#rev action".
# Deletes carry no content and are skipped rather than guessed at: a tombstone
# is a different kind of fact and the store models it separately.
$files = @()
$skippedDelete = 0
$skippedFilter = 0
foreach ($line in ($descText -split "`r?`n")) {
    if ($line -match '^\.\.\.\s+(//[^#]+)#(\d+)\s+(\w+)$') {
        $depot = $matches[1]; $rev = [int]$matches[2]; $action = $matches[3]
        if ($action -eq 'delete' -or $action -eq 'move/delete') { $skippedDelete++; continue }
        if ($depot -notmatch $Filter) { $skippedFilter++; continue }
        $files += [pscustomobject]@{ Depot = $depot; Rev = $rev; Action = $action }
    }
}

if ($files.Count -eq 0) {
    Write-Host ("change {0} carries no source files to record (skipped {1} delete(s), {2} non-source)" -f $Change, $skippedDelete, $skippedFilter)
    exit 0
}

Write-Host ("change {0}: {1} source file(s) to record" -f $Change, $files.Count) -ForegroundColor Cyan
if ($skippedDelete -gt 0) { Write-Host "  $skippedDelete delete(s) skipped: a tombstone is a separate fact and is not modelled here" }
if ($skippedFilter -gt 0) { Write-Host "  $skippedFilter file(s) outside the source filter ($Filter)" }

if (-not $WhatIf) {
    $diskFull = Join-Path (Get-Location) $Disk
    New-Item -ItemType Directory -Force (Split-Path $diskFull) | Out-Null
    if (-not (Test-Path $diskFull)) {
        Write-Host "  creating a new store at $Disk"
        [System.IO.File]::WriteAllBytes($diskFull, (New-Object byte[] 8388608))
    }
}

$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
$stored = 0
$failed = 0
foreach ($f in $files) {
    # The depot path minus the stream prefix is the store key, so the same file
    # keys identically whichever stream recorded it.
    $storePath = $f.Depot -replace '^//[^/]+/[^/]+/', ''
    $quire = Split-Path (Split-Path $storePath -Parent) -Leaf
    if ($quire) { $quire = $quire.Substring(0, 1).ToUpper() + $quire.Substring(1) } else { $quire = 'Unknown' }

    if ($WhatIf) { Write-Host ("  would record {0}#{1} as {2}" -f $storePath, $f.Rev, $quire); continue }

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        & p4 print -q -o $tmp ("{0}#{1}" -f $f.Depot, $f.Rev) 2>&1 | Out-Null
        if (-not (Test-Path $tmp) -or (Get-Item $tmp).Length -eq 0) {
            Write-Host ("  FAIL {0}#{1}: p4 print returned nothing" -f $storePath, $f.Rev) -ForegroundColor Red
            $failed++
            continue
        }
        $chapter = ''
        foreach ($line in (Get-Content -TotalCount 8 $tmp)) {
            if ($line -match '^\s*Chapter:\s*(.+?)\s*$') { $chapter = $matches[1]; break }
        }
        if (-not $chapter) { $chapter = [System.IO.Path]::GetFileNameWithoutExtension($storePath) }

        $out = & pwsh -NoProfile -File 'build/store-source.ps1' -Src $tmp -Disk $Disk -Path $storePath -Quire $quire -Chapter $chapter @kArg 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("  FAIL {0}#{1}: {2}" -f $storePath, $f.Rev, (($out | Out-String).Trim())) -ForegroundColor Red
            $failed++
        }
        else {
            $digest = ''
            if (($out | Out-String) -match 'stored ([0-9a-f]{64})') { $digest = $matches[1] }
            Write-Host ("  {0}  {1}#{2}" -f $digest.Substring(0, 12), $storePath, $f.Rev)
            $stored++
        }
    }
    finally {
        if (Test-Path $tmp) { Remove-Item -Force $tmp -ErrorAction SilentlyContinue }
    }
}

if ($WhatIf) { Write-Host 'WhatIf: nothing was recorded.'; exit 0 }

Write-Host ''
if ($failed -gt 0) {
    Write-Host "recorded $stored of $($files.Count); $failed failed" -ForegroundColor Red
    exit 1
}
Write-Host "recorded $stored source fact(s) from change $Change into $Disk" -ForegroundColor Green
exit 0
