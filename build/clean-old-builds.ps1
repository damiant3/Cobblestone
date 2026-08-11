# clean-old-builds.ps1 -- delete archived build output folders across every
# workspace in the NewRepository collection.
#
# `build/build.ps1` does not delete the previous build. It RENAMES
# `build/output` to `build/output-<yyyyMMdd>-<HHmmss>` and starts a fresh one
# (build.ps1:159-161), so every gate run leaves one folder behind forever.
# Measured 2026-08-08: 587 folders, 8.76 GB, oldest 24 days.
#
# DRY RUN BY DEFAULT. It prints what it would remove and removes nothing until
# you pass -Delete. This deletes directories outside the depot across other
# agents' workspaces, so the default has to be the harmless one.
#
#   pwsh build/clean-old-builds.ps1                    # what would go
#   pwsh build/clean-old-builds.ps1 -Delete            # actually delete
#   pwsh build/clean-old-builds.ps1 -Days 7 -Delete
#   pwsh build/clean-old-builds.ps1 -Workspace reek,val -Delete
#
# Nothing here is in Perforce: `.p4ignore` carries `build/output-*/`, and
# `p4 files //Codex/...build/output-*/...` answers "no such file". So this
# cannot lose depot content, and a workspace needs no sync afterwards.
#
# TWO AGES, AND IT NEEDS BOTH TO BE OLD. The folder NAME is stamped when the
# build was retired; the directory MTIME is when its content was last written,
# which is earlier because the rename happens at the start of the NEXT build.
# Deleting only when both exceed the cutoff means a folder written to after it
# was named is kept. Today that spares nothing (measured: 0 folders differ in
# verdict), and it costs one comparison to stay right if that ever changes.
#
# A folder under `build/` whose name starts with `output-` but does not match
# the exact stamp shape is REPORTED AND NEVER DELETED. `build/output` itself,
# the live one, has no suffix and so cannot match.

[CmdletBinding()]
param(
    [int]$Days = 3,
    [string]$Root = 'D:\Projects',
    [string]$Collection = 'NewRepository*',
    [string[]]$Workspace,
    [switch]$Delete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Days -lt 1) {
    Write-Host "clean-old-builds: -Days must be at least 1 (got $Days)." -ForegroundColor Red
    Write-Host "A cutoff of 0 would delete the archive the running build just made."
    exit 2
}

$cutoff = (Get-Date).AddDays(-$Days)
$stampRe = '^output-(\d{8})-(\d{6})$'

# @() around both: a single match returns a scalar, and under StrictMode a
# scalar has no .Count. Same trap build/audit-skips.ps1 records.
$roots = @(Get-ChildItem $Root -Directory -Filter $Collection -ErrorAction SilentlyContinue |
           Sort-Object Name)
if ($Workspace) {
    # `pwsh -File script.ps1 -Workspace reek,val` delivers ONE string "reek,val",
    # not an array, so split before matching or the two-name form finds nothing.
    $names  = @($Workspace | ForEach-Object { $_ -split ',' } |
                ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $wanted = @($names | ForEach-Object { $_; "NewRepository-$_" })
    $roots  = @($roots | Where-Object { $wanted -contains $_.Name })
    if (-not $roots) {
        Write-Host "clean-old-builds: -Workspace matched nothing. Asked for: $($names -join ', ')" -ForegroundColor Yellow
        exit 1
    }
}

if (-not $roots) {
    Write-Host "clean-old-builds: no workspaces matched '$Collection' under $Root." -ForegroundColor Yellow
    exit 1
}

Write-Host "clean-old-builds: cutoff $($cutoff.ToString('yyyy-MM-dd HH:mm')) ($Days days), $($roots.Count) workspace(s)"
if (-not $Delete) { Write-Host "DRY RUN. Nothing will be deleted. Pass -Delete to act." -ForegroundColor Cyan }
Write-Host ''

$totalBytes = 0L
$totalCount = 0
$failures   = @()
$unparsed   = @()

foreach ($ws in $roots) {
    $buildDir = Join-Path $ws.FullName 'build'
    if (-not (Test-Path $buildDir)) { continue }

    $candidates = Get-ChildItem $buildDir -Directory -Filter 'output-*' -ErrorAction SilentlyContinue
    if (-not $candidates) { continue }

    $wsBytes = 0L
    $wsCount = 0

    foreach ($dir in $candidates) {
        $m = [regex]::Match($dir.Name, $stampRe)
        if (-not $m.Success) { $unparsed += $dir.FullName; continue }

        $named = [datetime]::MinValue
        $ok = [datetime]::TryParseExact(
            ($m.Groups[1].Value + $m.Groups[2].Value), 'yyyyMMddHHmmss',
            [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$named)
        if (-not $ok) { $unparsed += $dir.FullName; continue }

        # Both clocks must be past the cutoff.
        if ($named -ge $cutoff -or $dir.LastWriteTime -ge $cutoff) { continue }

        $bytes = 0L
        try {
            $bytes = (Get-ChildItem $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
                      Measure-Object -Property Length -Sum).Sum
            if (-not $bytes) { $bytes = 0L }
        } catch { $bytes = 0L }

        if ($Delete) {
            try {
                Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
            } catch {
                $failures += [pscustomobject]@{ Path = $dir.FullName; Reason = $_.Exception.Message }
                continue
            }
        }

        $wsBytes += $bytes; $wsCount++
    }

    if ($wsCount -gt 0) {
        $verb = if ($Delete) { 'deleted' } else { 'would delete' }
        "  {0,-40} {1,4} folder(s) {2}, {3,8:N2} GB" -f $ws.Name, $wsCount, $verb, ($wsBytes / 1GB) | Write-Host
        $totalBytes += $wsBytes; $totalCount += $wsCount
    }
}

Write-Host ''
$verb = if ($Delete) { 'Deleted' } else { 'Would delete' }
"{0} {1} folder(s), {2:N2} GB" -f $verb, $totalCount, ($totalBytes / 1GB) | Write-Host

if ($unparsed) {
    Write-Host ''
    Write-Host "NOT TOUCHED, name does not match output-<yyyyMMdd>-<HHmmss>:" -ForegroundColor Yellow
    $unparsed | ForEach-Object { Write-Host "  $_" }
}

if ($failures) {
    Write-Host ''
    Write-Host "FAILED to delete $($failures.Count):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $($_.Path)"; Write-Host "    $($_.Reason)" }
    exit 1
}

exit 0
