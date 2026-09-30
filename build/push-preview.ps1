# push-preview.ps1 -- publish main head as one "Preview @CL" commit on the
# public mirrors' `preview` branch, by docs/Agents/PublicPush.md steps 1-2c.
#
# Without -Push it is a dry run: it stages, scans and builds the commit's tree,
# reports what the commit would carry, and moves no ref and pushes nothing.
#
# The release mirror's working tree is a Perforce client, so this never checks
# out a branch there: a checkout rewrites working files. Everything is staged
# into a temporary copy of the git index; the commit is made with write-tree and
# commit-tree onto the preview tip (master when there is none), and -Push moves
# refs/heads/preview and pushes it to both remotes as a fast-forward. The
# mirror's own index, master and working tree are left as they were.
#
#   pwsh build/push-preview.ps1                  # dry run
#   pwsh build/push-preview.ps1 -Push            # commit and push, no force
[CmdletBinding()]
param(
    [string]$Repo = 'D:\Projects\Cobblestone-red-main',
    [string]$Client = 'BigWhite_Codex_red_main',
    [switch]$NoSync,
    [switch]$DryRun,
    [switch]$Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($DryRun -and $Push) { Write-Host 'REFUSED: pick one of -DryRun and -Push.'; exit 1 }
if (-not (Test-Path -PathType Container (Join-Path $Repo '.git'))) { Write-Host "REFUSED: $Repo has no .git; PublicPush.md 'Where the mirror lives' finds the one at the remote tip."; exit 1 }
$Repo = (Resolve-Path $Repo).Path

# A run on 2026-09-29 grew this process to 28.5 GB and was killed by root with
# the box at 3.2 GiB free; the step was not identified. Every native call
# therefore runs through Invoke-Capped, its output streamed to a file and its
# working set polled, and this process checks its own after every step.
$ChildCapBytes = 4GB
$SelfCapBytes = 2GB
$work = Join-Path $env:TEMP "push-preview-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
$script:callNo = 0

function Assert-SelfCap([string]$Step) {
    $ws = (Get-Process -Id $PID).WorkingSet64
    Write-Host ("  [{0}] this process {1:N0} MB" -f $Step, ($ws / 1MB))
    if ($ws -gt $SelfCapBytes) { Write-Host "ABORTED after '$Step': this process holds $([int]($ws / 1MB)) MB, over the $([int]($SelfCapBytes / 1MB)) MB cap."; exit 2 }
}

function Invoke-Capped([string]$Exe, [string[]]$ArgList, [string]$Step) {
    $script:callNo++
    $out = Join-Path $work "call$($script:callNo).out"; $err = Join-Path $work "call$($script:callNo).err"
    $p = Start-Process -FilePath $Exe -ArgumentList $ArgList -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    $peak = 0
    while (-not $p.HasExited) {
        try { $p.Refresh(); if ($p.WorkingSet64 -gt $peak) { $peak = $p.WorkingSet64 } } catch {}
        if ($peak -gt $ChildCapBytes) { try { $p.Kill($true) } catch {}; Write-Host "ABORTED in '$Step': $Exe reached $([int]($peak / 1MB)) MB, over the $([int]($ChildCapBytes / 1MB)) MB cap."; exit 2 }
        Start-Sleep -Milliseconds 250
    }
    $p.WaitForExit()
    Write-Host ("  [{0}] {1} exit {2}, peak {3:N0} MB" -f $Step, $Exe, $p.ExitCode, ($peak / 1MB))
    Assert-SelfCap $Step
    return [pscustomobject]@{ Code = $p.ExitCode; Out = $out; Err = $err }
}

function Git([string]$Step) {
    $r = Invoke-Capped 'git' (@('-C', $Repo) + $args) $Step
    if ($r.Code -ne 0) { Get-Content $r.Err -TotalCount 20 | ForEach-Object { Write-Host "  $_" }; throw "git $($args -join ' ') failed ($($r.Code))" }
    return $r
}
function First-Line($r) { return "$(Get-Content $r.Out -TotalCount 1)".Trim() }
function Write-LfFile([string]$Path, [string[]]$Lines) { [IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n")) }

# -- 1. main head, and a mirror whose master is the public tip -----------------
Assert-SelfCap 'start'
if (-not $NoSync) {
    $r = Invoke-Capped 'p4' @('-c', $Client, 'sync', '-q') 'p4 sync'
    if ($r.Code -ne 0) { Get-Content $r.Err -TotalCount 20 | ForEach-Object { Write-Host "  $_" }; throw "p4 -c $Client sync failed" }
}
$cl = (First-Line (Invoke-Capped 'p4' @('-c', $Client, 'changes', '-m1', '-s', 'submitted', '//Codex/main/...#have') 'p4 changes')) -replace '^Change (\d+).*', '$1'
if ($cl -notmatch '^\d+$') { throw "cannot read the synced main head for $Client" }
$head = First-Line (Git 'rev-parse' 'rev-parse' 'HEAD')
$remoteTip = ((First-Line (Git 'ls-remote master' 'ls-remote' 'github' 'master')) -split '\s+')[0]
if ($head -ne $remoteTip) { Write-Host "REFUSED: $Repo master is $($head.Substring(0,8)), the public tip is $($remoteTip.Substring(0,8)) (PublicPush.md: push only from the mirror at the remote tip)."; exit 1 }
if ((Get-Item (Git 'index check' 'diff' '--cached' '--name-only').Out).Length -gt 0) { Write-Host 'REFUSED: the mirror''s index holds staged changes; this script leaves it alone and will not build on it.'; exit 1 }
Write-Host "main head CL $cl; mirror master $($head.Substring(0,8)) = public tip"

$tmpIndex = Join-Path $work 'index'
Copy-Item (Join-Path $Repo '.git\index') $tmpIndex -Force
$savedIndex = $env:GIT_INDEX_FILE
$env:GIT_INDEX_FILE = $tmpIndex
try {
    # -- 2. modified and deleted tracked files --------------------------------
    [void](Git 'add -u' 'add' '-u')

    # -- 2b. the depot against the index: a new file nothing decided to withhold
    $root = $Repo.TrimEnd('\') + '\'
    $r = Invoke-Capped 'p4' @('-c', $Client, 'have') 'p4 have'
    $depot = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadLines($r.Out)) {
        $i = $line.IndexOf(' - ')
        if ($i -gt 0) { $p = $line.Substring($i + 3).Trim(); if ($p.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $depot.Add(($p.Substring($root.Length) -replace '\\', '/')) } }
    }
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in [IO.File]::ReadLines((Git 'ls-tree' 'ls-tree' '-r' 'HEAD' '--name-only').Out)) { [void]$tracked.Add($line) }
    $untracked = [Collections.Generic.List[string]]::new()
    foreach ($p in $depot) { if (-not $tracked.Contains($p)) { $untracked.Add($p) } }
    Assert-SelfCap 'reconcile lists'
    $ignored = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($untracked.Count -gt 0) {
        $diffFile = Join-Path $work 'untracked.txt'
        Write-LfFile $diffFile $untracked.ToArray()
        $r = Invoke-Capped 'cmd' @('/c', "git -C `"$Repo`" check-ignore --stdin < `"$diffFile`"") 'check-ignore'
        foreach ($line in [IO.File]::ReadLines($r.Out)) { [void]$ignored.Add($line) }
    }
    $survivors = @($untracked | Where-Object { -not $ignored.Contains($_) })
    if ($survivors.Count -gt 0) {
        $addFile = Join-Path $work 'add.txt'
        Write-LfFile $addFile $survivors
        [void](Git 'add survivors' 'add' "--pathspec-from-file=$addFile")
    }

    # -- 2c and "Do not publish" ----------------------------------------------
    $staged = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadLines((Git 'staged' 'diff' '--cached' '--no-renames' '--name-status' 'HEAD').Out)) {
        $s, $p = $line -split "`t", 2
        $staged.Add([pscustomobject]@{ Status = $s.Substring(0, 1); Path = ($p -split "`t")[-1] })
    }
    $stagedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($x in $staged) { [void]$stagedPaths.Add($x.Path) }
    $lost = @($survivors | Where-Object { -not $stagedPaths.Contains($_) })
    $keep = @('build/boot/diag.img', 'build/boot/kbd-diag-v16.img')
    $unstage = @($staged | Where-Object { $_.Status -eq 'A' -and ($_.Path -match '\.png$' -or ($_.Path -match '^build/boot/[^/]+\.img$' -and $keep -notcontains $_.Path)) })
    foreach ($u in $unstage) { [void](Git 'unstage' 'reset' '-q' 'HEAD' '--' $u.Path) }
    $unPaths = @($unstage | ForEach-Object { $_.Path })
    $staged = @($staged | Where-Object { $unPaths -notcontains $_.Path })
    $withheld = '^(apps/games/magic/|annotations/apps/games/magic/|codex/product/|apps/productbuilder/|apps/wademo/|build/boot/archive/)|^build/boot/diag-sitting[^/]*\.cfg$|\.disk2?$|^docs/Reference/[^/]*_(Specification|Datasheet)\.|(^|/)[Pp]hone/'
    $held = @($staged | Where-Object { $_.Status -ne 'D' -and $_.Path -match $withheld })
    $secretName = '(?i)(\.(pem|pfx|p12|key)$|(^|/)\.env$|(^|[/._-])(credentials?|secrets?|tokens?|passwords?|signing[-_]?keys?)([/._-]|$))'
    $nameFalse = '(?i)(Keyboard\.codex$|identity-keygen|cap-launder-pure-key|^codex/test/fixtures/https/)'
    $secretNames = @($staged | Where-Object { $_.Status -ne 'D' -and $_.Path -match $secretName -and $_.Path -notmatch $nameFalse })
    $r = Invoke-Capped 'git' @('-C', $Repo, 'grep', '--cached', '-l', '-I', '-E', '"^-----BEGIN [A-Z ]*PRIVATE KEY-----[[:space:]]*$"', '--', '.', '":(exclude)codex/test/fixtures/https"') 'key scan'
    $keyText = @(Get-Content $r.Out)
    $ship = Invoke-Capped 'pwsh' @('-NoProfile', '-File', "`"$(Join-Path $Repo 'build\check-shipping-images.ps1')`"") 'check-shipping-images'
    $shipOk = $ship.Code -eq 0

    $a = @($staged | Where-Object Status -eq 'A').Count; $m = @($staged | Where-Object Status -eq 'M').Count; $d = @($staged | Where-Object Status -eq 'D').Count
    Write-Host "staged against master: $a added ($($survivors.Count) of them new to git through the depot reconcile), $m modified, $d deleted"
    foreach ($u in $unstage) { Write-Host "  not published (image or PNG snapshot): $($u.Path)" }
    Write-Host "check-shipping-images: $(if ($shipOk) { 'OK' } else { 'REFUSED' })"
    $refuse = $false
    if ($lost.Count) { $refuse = $true; Write-Host "REFUSED: $($lost.Count) depot file(s) nothing withheld did not stage:"; $lost | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" } }
    if ($held.Count) { $refuse = $true; Write-Host "REFUSED: $($held.Count) withheld path(s) staged:"; $held | Select-Object -First 20 | ForEach-Object { Write-Host "  $($_.Status) $($_.Path)" } }
    if ($secretNames.Count) { $refuse = $true; Write-Host "REFUSED: $($secretNames.Count) staged name(s) look like secrets:"; $secretNames | ForEach-Object { Write-Host "  $($_.Status) $($_.Path)" } }
    if ($keyText.Count) { $refuse = $true; Write-Host "REFUSED: private-key text in $($keyText.Count) staged file(s):"; $keyText | ForEach-Object { Write-Host "  $_" } }
    if (-not $shipOk) { $refuse = $true; Get-Content $ship.Out -TotalCount 20 | ForEach-Object { Write-Host "  $_" } }
    if ($refuse) { exit 1 }

    # -- the commit -----------------------------------------------------------
    $tree = First-Line (Git 'write-tree' 'write-tree')
    $remotePreview = ((First-Line (Git 'ls-remote preview' 'ls-remote' 'github' 'preview')) -split '\s+')[0]
    $rp = Invoke-Capped 'git' @('-C', $Repo, 'rev-parse', '-q', '--verify', 'refs/heads/preview') 'local preview'
    $localPreview = if ($rp.Code -eq 0) { First-Line $rp } else { '' }
    if ($remotePreview -and ($localPreview -ne $remotePreview)) {
        if (-not $Push) { Write-Host "note: github preview is $($remotePreview.Substring(0,8)); a -Push run fetches it first" }
        else { [void](Git 'fetch preview' 'fetch' 'github' 'preview:refs/heads/preview'); $localPreview = First-Line (Git 'rev-parse preview' 'rev-parse' 'refs/heads/preview') }
    }
    $parent = if ($localPreview) { $localPreview } else { $head }
    $parentTree = First-Line (Git 'parent tree' 'rev-parse' "$parent^{tree}")
    if ($tree -eq $parentTree) { Write-Host "nothing to publish: the tree equals preview's tip $($parent.Substring(0,8))"; exit 0 }
    Write-Host "tree $($tree.Substring(0,8)); parent $($parent.Substring(0,8)) ($(if ($localPreview) { 'preview' } else { 'master' }))"
    if (-not $Push) { Write-Host "DRY RUN: would commit 'Preview @$cl' on preview and push github preview and gitlab preview (no force)."; exit 0 }

    $commit = First-Line (Git 'commit-tree' 'commit-tree' $tree '-p' $parent '-m' "`"Preview @$cl`"")
    $old = if ($localPreview) { $localPreview } else { '0000000000000000000000000000000000000000' }
    [void](Git 'update-ref' 'update-ref' 'refs/heads/preview' $commit $old)
    Write-Host "preview -> $($commit.Substring(0,8)) 'Preview @$cl'"
    [void](Git 'push github' 'push' 'github' 'preview:preview')
    [void](Git 'push gitlab' 'push' 'gitlab' 'preview:preview')
    Write-Host 'pushed: github preview, gitlab preview'
} finally {
    $env:GIT_INDEX_FILE = $savedIndex
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
