# p4-purify.ps1 -- return one Perforce workspace to depot purity.
#
# Deletes every file under the client root that the depot does not track,
# except files or directories whose name starts with a dot (.p4config,
# .agentgrid, .git, .claude/settings.local.json and the like are local
# configuration, not build residue). Build outputs, temps, intermediates,
# stray copies and the empty directories they leave are all removed. Then
# `p4 sync -f` rewrites every tracked file from the depot, and a dev-stream
# client merges down from //Codex/main and submits the merge when it resolves
# cleanly. A main-stream client stops after the sync.
#
# It refuses a client with files open: shelve and revert first, so that the
# sweep never deletes work in flight. Shelves live in the depot and are not
# touched.
#
# This deletes across a whole workspace, so it requires -ApprovedBy damian,
# exactly as merge-down-all.ps1 does. -DryRun needs no approval: it lists what
# would go and runs nothing.
#
#   pwsh build/p4-purify.ps1 -Client BigWhite_Codex_val -DryRun
#   pwsh build/p4-purify.ps1 -Client BigWhite_Codex_val -ApprovedBy damian
#   pwsh build/p4-purify.ps1 -Client BigWhite_Codex_val_main -ApprovedBy damian
#
# Exit 0 when the workspace is pure and (for a dev stream) level with main.
# Exit 1 when it refused, or the merge left files to resolve.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Client,
    [switch]$DryRun,
    [string]$Port = 'localhost:1666',
    [string]$User = 'damian',
    [string]$Mainline = '//Codex/main',
    [string]$ApprovedBy
)

$ErrorActionPreference = 'Stop'
# p4.exe, not p4: a bare `p4` here resolves to this very function and recurses
# until the box runs out of memory (it did, 2026-09-07, 11 GB in one pwsh).
function P4 { & p4.exe -p $Port -u $User -c $Client @args 2>&1 }

if (-not $DryRun -and $ApprovedBy -ne 'damian') {
    Write-Host "REFUSED: this deletes across a whole workspace. Pass -ApprovedBy damian, or -DryRun to preview."
    exit 1
}

$spec = P4 client -o
$root = ($spec | Where-Object { $_ -match '^Root:\s+(.+)$' } | ForEach-Object { $Matches[1].Trim() } | Select-Object -First 1)
$stream = ($spec | Where-Object { $_ -match '^Stream:\s+(.+)$' } | ForEach-Object { $Matches[1].Trim() } | Select-Object -First 1)
if (-not $root -or -not (Test-Path -LiteralPath $root)) { Write-Host "REFUSED: client $Client has no root on disk ($root)."; exit 1 }
if (-not $stream) { Write-Host "REFUSED: client $Client is not a stream client."; exit 1 }
$root = (Resolve-Path -LiteralPath $root).Path.TrimEnd('\')
Write-Host "client $Client  root $root  stream $stream"

$opened = @(P4 opened | Where-Object { $_ -notmatch 'not opened on this client' })
if ($opened.Count -gt 0) {
    Write-Host "REFUSED: $($opened.Count) file(s) open on $Client. Shelve (p4 shelve -f -c <CL>) and revert them, then rerun."
    $opened | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# 1. The have set: every local path the depot tracks on this client.
$have = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($line in (P4 have)) {
    $i = $line.IndexOf(' - ')
    if ($i -gt 0) { [void]$have.Add($line.Substring($i + 3).Trim()) }
}
Write-Host "tracked files: $($have.Count)"

# 2. The sweep: every file not in the have set and not under a dot segment.
$prefix = $root.Length + 1
$doomed = [System.Collections.Generic.List[string]]::new()
$bytes = [long]0
foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -Force -File) {
    $rel = $f.FullName.Substring($prefix)
    if ($rel.Split('\') | Where-Object { $_.StartsWith('.') }) { continue }
    if ($have.Contains($f.FullName)) { continue }
    $doomed.Add($f.FullName); $bytes += $f.Length
}
Write-Host ("untracked files to delete: {0} ({1:N1} MB)" -f $doomed.Count, ($bytes / 1MB))
$topLevel = $doomed | ForEach-Object { $_.Substring($prefix).Split('\')[0] } | Group-Object | Sort-Object Count -Descending
$topLevel | Select-Object -First 15 | ForEach-Object { Write-Host ("  {0,7}  {1}" -f $_.Count, $_.Name) }

if ($DryRun) { Write-Host "dry run: nothing deleted, nothing synced."; exit 0 }

foreach ($path in $doomed) {
    Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $path -Force
}

# 3. Empty directories, deepest first, never a dot directory or its children.
$dirs = Get-ChildItem -LiteralPath $root -Recurse -Force -Directory |
    Where-Object { -not ($_.FullName.Substring($prefix).Split('\') | Where-Object { $_.StartsWith('.') }) } |
    Sort-Object { $_.FullName.Length } -Descending
$removedDirs = 0
foreach ($d in $dirs) {
    if (-not (Get-ChildItem -LiteralPath $d.FullName -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $d.FullName -Force; $removedDirs++
    }
}
Write-Host "deleted $($doomed.Count) files, $removedDirs empty directories"

# 4. Force sync: every tracked file rewritten from the depot head.
$sync = @(P4 sync -f | Where-Object { $_ -notmatch 'up-to-date' })
Write-Host "sync -f: $($sync.Count) file(s) rewritten"

# 5. Merge down, dev streams only.
if ($stream -ne $Mainline) {
    $merge = @(P4 merge -S $stream -r)
    if ($merge -match 'already integrated|no such file|No such file|no target file') {
        Write-Host "merge-down: nothing to merge"
    } else {
        [void](P4 resolve -am)
        $unresolved = @(P4 resolve -n | Where-Object { $_ -notmatch 'No file\(s\) to resolve' })
        if ($unresolved.Count -gt 0) {
            Write-Host "MERGE LEFT $($unresolved.Count) file(s) to resolve by hand; changelist stays open:"
            $unresolved | ForEach-Object { Write-Host "  $_" }
            exit 1
        }
        $submit = @(P4 submit -d "merge-down: $Mainline into $stream (p4-purify)")
        $cl = ($submit | Where-Object { $_ -match 'Change (\d+) submitted' } | ForEach-Object { $Matches[1] } | Select-Object -First 1)
        Write-Host "merge-down submitted as $cl"
    }
    $drift = @(P4 diff2 -q "$stream/..." "$Mainline/..." | Where-Object { $_ -notmatch 'no differing files' })
    if ($drift.Count -gt 0) {
        Write-Host "NOT LEVEL WITH MAIN: $($drift.Count) file(s) differ"
        $drift | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host "level with $Mainline"
}

# 6. Re-measure against the have set as it stands after the sync and merge.
$have.Clear()
foreach ($line in (P4 have)) {
    $i = $line.IndexOf(' - ')
    if ($i -gt 0) { [void]$have.Add($line.Substring($i + 3).Trim()) }
}
$left = 0
foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -Force -File) {
    $rel = $f.FullName.Substring($prefix)
    if ($rel.Split('\') | Where-Object { $_.StartsWith('.') }) { continue }
    if (-not $have.Contains($f.FullName)) { $left++ }
}
if ($left -gt 0) { Write-Host "NOT PURE: $left untracked file(s) remain"; exit 1 }
Write-Host "PURE: $Client"
exit 0
