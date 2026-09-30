# test-disk-runner.ps1 -- grade the on-disk runner's verdicts against the
# sidecars on the host.
#
# Build.md Phase B stage 2 ends here: the guest compiles each chapter it finds
# on a FAT16 volume and prints a verdict per chapter, and this pairs those
# verdicts with `.failing` on the host. The sidecars are deliberately NOT on the
# image; putting them there is a namespace question rather than a runner one.
#
# Executing what was compiled is stage 3 and is another page's mechanism
# (docs/Designs/Active/OS/DeskBuildLoop.md, a nested VT-x guest, pending metal).
# Nothing here runs a compiled subject, so an `.expected` cannot be diffed and
# is not claimed to be.
#
# EACH SUBJECT IS BUNDLED ON THE HOST (Build.md Phase B, Option B): the image
# entry is the unit compile.ps1 would hand the compiler, every cited chapter
# followed by the subject, built by the same Resolve-CiteOrder and
# Format-CiteChapters, so the guest needs no resolver. The guest reports unit
# positions and Get-DiagRegions maps them back to the file, as compile.ps1 does.
#
# WHICH SUBJECTS ARE COMPARABLE, and each exclusion is a real difference rather
# than tidiness:
#   - A chapter with a `.flags` sidecar needs compile flags the runner does not
#     pass, so it would fail on its invocation rather than on itself (L-SIDECAR).
#     Excluded.
#   - A chapter with a `.skip` sidecar is excluded, as everywhere else.
# The counts of each exclusion are printed, because a denominator that hides
# what it dropped is the L-DENOM shape.
#
# The verdict-to-source mapping comes from the image writer's OWN manifest
# (`run.ps1 -ManifestOut`), not from folding the names a second time here: a
# second derivation agrees with a shared mistake by construction (L-BOTHARMS).
#
# Each shard boots two guests, one after the other: one to build the image
# through the img plug, one to run the runner. Measure free memory first.
[CmdletBinding()]
param(
    [string[]]$Subjects = @(),
    [string]$SubjectsFile = '',
    [string]$Kernel = '',
    [string]$RunnerCdx = '',
    # 0 sizes each shard's volume from its bundles: a bundle is about ten times
    # its subject, and every file rounds up to a cluster.
    [int]$TotalSectors = 0,
    # Subjects per image and boot, under test-run.ps1's 60-second guest budget.
    # Measured 2026-09-29: the guest compiles 20 bundles in about 1 s and the
    # img plug spends about 3 minutes per image, so a shard is as large as the
    # budget comfortably allows.
    [int]$ShardSize = 250,
    # Bundle bytes per image. The img plug receives the kernel and every source
    # as ONE message and aborted the connection on a 250-subject shard of about
    # 20 MB (2026-09-29); shards of 1.7 MB and 6 MB image cleanly.
    [long]$ShardBytes = 6MB,
    [int]$BucketSize = 256,
    [switch]$Keep,
    # The selection and its exclusions, no guest. Run it before spending an
    # image build and a boot on a subject list you have not counted.
    [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
. (Join-Path $PSScriptRoot 'quire-map.ps1')
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
foreach ($f in @($Kernel, (Join-Path $Repo 'build\boot\blockladder.efi'))) {
    if (-not (Test-Path -PathType Leaf $f)) { [Console]::Error.WriteLine("MISSING: $f"); exit 2 }
}

$paths = @($Subjects)
if ($SubjectsFile) {
    if (-not (Test-Path -PathType Leaf $SubjectsFile)) { [Console]::Error.WriteLine("MISSING -SubjectsFile: $SubjectsFile"); exit 2 }
    $paths += @(Get-Content $SubjectsFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}
if ($paths.Count -eq 0) {
    # codex\test\errors carries nearly every .failing sidecar.
    $paths = @((Get-ChildItem (Join-Path $Repo 'codex\test'), (Join-Path $Repo 'codex\test\errors') -Filter *.codex -File).FullName)
    Write-Host "[disk-runner] no subjects named, taking codex\test and codex\test\errors: $($paths.Count) chapter(s)"
}

# -- selection ---------------------------------------------------------------
$dropFlags = 0; $dropSkip = 0; $dropArch = 0; $dropMissing = 0
$selected = @()
foreach ($p in $paths) {
    if (-not (Test-Path -PathType Leaf $p)) { $dropMissing++; continue }
    if (Test-Path ($p -replace '\.codex$', '.skip')) { $dropSkip++; continue }
    if (Test-Path ($p -replace '\.codex$', '.flags')) { $dropFlags++; continue }
    # The guest is x86-64, and bvt.ps1 drops an .arch-only subject that does not
    # name x86-64 by the same test.
    $ao = $p -replace '\.codex$', '.arch-only'
    if ((Test-Path -PathType Leaf $ao) -and -not (@(Get-Content $ao | ForEach-Object { $_.Trim() }) -contains 'x86-64')) { $dropArch++; continue }
    $selected += (Resolve-Path $p).Path
}
Write-Host "[disk-runner] selected $($selected.Count) of $($paths.Count): dropped $dropFlags with .flags, $dropSkip with .skip, $dropArch arch-only, $dropMissing missing"
if ($selected.Count -eq 0) { Write-Host '[disk-runner] nothing comparable to run.'; exit 0 }
if ($ListOnly) {
    foreach ($s in $selected) { Write-Host "  $($s.Substring($Repo.Length + 1))" }
    exit 0
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "disk-runner-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    # -- the bundles -----------------------------------------------------------
    # One directory per subject, because two subjects share a file name
    # (codex\test and codex\test\errors) and the bundle keeps the subject's name
    # so the image folds it to the same 8.3 spelling.
    $srcOf = @{}; $regionsOf = @{}; $bundles = @(); $unresolved = @()
    $n = 0
    foreach ($s in $selected) {
        $n++
        $rootLines = [System.IO.File]::ReadAllLines($s)
        $seen = @{}
        foreach ($l in $rootLines) { if ($l -match '^Chapter:\s*(\w+)--(.+?)\s*$') { $seen["$($matches[1])::$($matches[2])"] = $true } }
        try { $ordered = Resolve-CiteOrder -RootLines $rootLines -Repo $Repo -SeedSeen $seen }
        catch { $unresolved += [pscustomobject]@{ Src = $s; Why = $_.Exception.Message }; continue }
        $d = Join-Path $work "bundles\$n"
        New-Item -ItemType Directory -Force $d | Out-Null
        $b = Join-Path $d ([System.IO.Path]::GetFileName($s))
        $sw = [System.IO.StreamWriter]::new($b, $false, [System.Text.UTF8Encoding]::new($false))
        foreach ($l in (Format-CiteChapters -Ordered $ordered)) { $sw.Write($l); $sw.Write("`n") }
        foreach ($l in $rootLines) { $sw.Write($l); $sw.Write("`n") }
        $sw.Dispose()
        $srcOf[$b] = $s
        $regionsOf[$b] = Get-DiagRegions -Ordered $ordered -SrcPath $s
        $bundles += $b
    }
    $bundleBytes = [long]0
    foreach ($b in $bundles) { $bundleBytes += (Get-Item $b).Length }
    # A shard closes at $ShardSize subjects or $ShardBytes bytes, whichever
    # comes first; a single bundle larger than the cap is a shard of its own.
    $shards = [System.Collections.Generic.List[object]]::new()
    $cur = [System.Collections.Generic.List[string]]::new(); $curBytes = [long]0
    foreach ($b in $bundles) {
        $len = (Get-Item $b).Length
        if ($cur.Count -gt 0 -and ($cur.Count -ge $ShardSize -or $curBytes + $len -gt $ShardBytes)) {
            $shards.Add([string[]]$cur.ToArray()); $cur.Clear(); $curBytes = 0
        }
        $cur.Add($b); $curBytes += $len
    }
    if ($cur.Count -gt 0) { $shards.Add([string[]]$cur.ToArray()) }
    $shardCount = $shards.Count
    Write-Host "[disk-runner] bundled $($bundles.Count) subject(s), $bundleBytes bytes, $($unresolved.Count) unresolved; $shardCount shard(s) of up to $ShardSize subjects or $ShardBytes bytes"
    if ($bundles.Count -eq 0) {
        foreach ($u in $unresolved) { Write-Host "  UNRESOLVED  $([System.IO.Path]::GetFileName($u.Src)) : $($u.Why)" }
        Write-Host 'DISK RUNNER: FAIL -- no subject bundled, so nothing reached the image'; exit 1
    }

    # -- the runner ----------------------------------------------------------
    if ($RunnerCdx -eq '') {
        $RunnerCdx = Join-Path $work 'DiskTestRunner.cdx'
        $cArgs = @('-NoProfile', '-File', (Join-Path $Repo 'build\compile.ps1'),
                   '-Src', (Join-Path $Repo 'apps\works\DiskTestRunner.codex'),
                   '-Out', $RunnerCdx, '-Log', (Join-Path $work 'runner-compile.log'),
                   '-Kernel', $Kernel)
        & pwsh @cArgs | ForEach-Object { Write-Host "  $_" }
        if (-not (Test-Path -PathType Leaf $RunnerCdx)) {
            [Console]::Error.WriteLine('FAIL: the runner did not compile'); exit 4
        }
    }

    # -- the shards ------------------------------------------------------------
    # test-run.ps1 gives a guest a fixed 60-second wall budget, so the corpus is
    # imaged and booted in shards, one guest at a time. A shard is keyed into
    # the verdicts by its index, because every shard reuses the names SRC0...
    $verdicts = @(); $byName = @{}; $deadShards = @()
    for ($k = 0; $k -lt $shardCount; $k++) {
        $shard = @($shards[$k])
        $shardBytes = [long]0
        foreach ($b in $shard) { $shardBytes += (Get-Item $b).Length }
        $sectors = if ($TotalSectors -gt 0) { $TotalSectors } else { [int][Math]::Max(32768, [Math]::Ceiling(($shardBytes * 1.25 + $shard.Count * 16384 + 4MB) / 512)) }
        $listFile = Join-Path $work "subjects-$k.txt"
        [System.IO.File]::WriteAllLines($listFile, [string[]]$shard)
        $img = Join-Path $work "subjects-$k.img"
        $manifest = Join-Path $work "manifest-$k.tsv"
        $imgArgs = @('-NoProfile', '-File', (Join-Path $Repo 'codex\plugs\img\run.ps1'),
                     '-PeInput', (Join-Path $Repo 'build\boot\blockladder.efi'),
                     '-CdxInput', $Kernel, '-Out', $img, '-Fat16',
                     '-TotalSectors', "$sectors", '-SourceList', $listFile,
                     '-Bucketed', '-BucketSize', "$BucketSize", '-ManifestOut', $manifest)
        & pwsh @imgArgs | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $img)) {
            [Console]::Error.WriteLine("FAIL: shard $k's image was not produced"); exit 3
        }
        foreach ($row in (Get-Content $manifest)) {
            $c = $row -split "`t"
            if ($c.Count -lt 3) { continue }
            # The guest prints the 8.3 name with its padding removed by the FAT
            # reader, so the key is matched on the same shape the guest prints.
            $bare = ($c[1].Substring(0, 8).TrimEnd() + '.' + $c[1].Substring(8, 3).TrimEnd()).TrimEnd('.')
            $k2 = if ($c[0] -eq '') { $bare } else { "$($c[0])/$bare" }
            $byName["$k|$k2"] = $c[2]
        }
        $console = Join-Path $work "runner-$k.txt"
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        & pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $RunnerCdx -OutFile $console -DiskFile $img | Out-Null
        $secs = [int]$clock.Elapsed.TotalSeconds
        $lines = if (Test-Path -PathType Leaf $console) { @(Get-Content $console) } else { @() }
        # A guest that died mid-run leaves a console with no closing line, which
        # is not the same as a run where every subject passed. Its subjects with
        # no verdict line fail below as holes.
        $done = [bool]($lines -match '=== runner done ===')
        Write-Host "[disk-runner] shard $k : $($shard.Count) subject(s), $shardBytes bytes, $sectors sectors, guest ${secs}s, $(if ($done) { 'done' } else { 'DID NOT FINISH' })"
        if (-not $done) {
            $deadShards += $k
            $lines | Select-Object -Last 4 | ForEach-Object { Write-Host "    guest: $_" }
        }
        foreach ($l in $lines) { $verdicts += [pscustomobject]@{ Shard = $k; Line = $l } }
        if (-not $Keep) { Remove-Item -Force $img -ErrorAction SilentlyContinue }
    }

    # -- the pairing ---------------------------------------------------------
    $pass = 0; $fails = @(); $unmatched = @(); $graded = @{}

    # compile.ps1 turns a resolve failure into `error 3010`, so a sidecar that
    # records 3010 expects exactly this.
    foreach ($u in $unresolved) {
        $base = [System.IO.Path]::GetFileName($u.Src)
        $failFile = $u.Src -replace '\.codex$', '.failing'
        $want = if (Test-Path -PathType Leaf $failFile) { @(Get-Content $failFile | ForEach-Object { $_.Trim() -replace '^CDX', '' }) } else { @() }
        if ($want -contains '3010') { $pass++ } else { $fails += "$base : the host could not resolve its cites: $($u.Why)" }
    }

    foreach ($v in $verdicts) {
        if ($v.Line -notmatch '^\s*(?<name>\S+)\s+:\s+(?<rest>errors=.*|READ FAILED.*|frontend ok.*)$') { continue }
        $name = $matches['name']; $rest = $matches['rest']
        $bundle = $byName["$($v.Shard)|$name"]
        if (-not $bundle -or -not $srcOf.ContainsKey($bundle)) { $unmatched += $name; continue }
        $src = $srcOf[$bundle]
        $graded[$bundle] = $true
        $base = [System.IO.Path]::GetFileName($src)
        $failFile = $src -replace '\.codex$', '.failing'
        $expectFail = Test-Path -PathType Leaf $failFile
        if ($rest -match '^READ FAILED') { $fails += "$base : the guest could not read it off the image"; continue }
        # A codegen error is graded exactly like a frontend one: bvt.ps1 reads
        # both off the same log. errors/builtin-as-value's CDX2040 is raised in
        # codegen.
        $phase = if ($rest -match '^frontend ok, CODEGEN ') { 'codegen' } else { 'frontend' }
        $errs = if ($rest -match 'errors=(?<n>\d+)') { [int]$matches['n'] } else { -1 }
        $cs = if ($rest -match ' codes=(?<cs>\S*)') { $matches['cs'] } else { '' }
        if (-not $expectFail) {
            if ($errs -eq 0) { $pass++ } else { $fails += "$base : expected a clean compile, the guest reported $phase errors=$errs codes=$cs" }
            continue
        }
        if ($errs -le 0) { $fails += "$base : expected a compile failure, the guest reported errors=$errs"; continue }
        # Every unit position the guest reported, mapped back to its file by the
        # same regions compile.ps1 uses.
        $got = @()
        foreach ($g in ($cs -split ',')) {
            if ($g -notmatch '^(?<c>\d+)@(?<l>\d+):(?<col>\d+)$') { continue }
            $code = [int]$matches['c']; $ul = [int]$matches['l']; $ucol = [int]$matches['col']
            # A position no region holds (a synthetic span at line 0) passes
            # through Convert-DiagLine unchanged and is kept as reported.
            $m = Convert-DiagLine -Line "${ul}:${ucol}: x" -Regions $regionsOf[$bundle]
            if ($m -match '.:(?<l>\d+):(?<col>\d+): x$') { $ul = [int]$matches['l']; $ucol = [int]$matches['col'] }
            $got += [pscustomobject]@{ Code = $code; Line = $ul; Col = $ucol }
        }
        if ($got.Count -ne $errs) { $fails += "$base : the guest reported errors=$errs but $($got.Count) readable codes ($cs)"; continue }
        # bvt.ps1's adjudication: EVERY recorded code appears, at its position
        # when the sidecar gives one.
        $want = @(Get-Content $failFile | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($want.Count -eq 0) { $fails += "$base : .failing records no code, so its verdict cannot be graded"; continue }
        $missing = @()
        foreach ($w in $want) {
            $hit = $false
            if ($w -match '^(CDX)?(?<c>\d+)@(?<l>\d+):(?<col>\d+)$') {
                $wc = [int]$matches['c']; $wl = [int]$matches['l']; $wcol = [int]$matches['col']
                $hit = @($got | Where-Object { $_.Code -eq $wc -and $_.Line -eq $wl -and $_.Col -eq $wcol }).Count -gt 0
            } elseif ($w -match '^(CDX)?(?<c>\d+)$') {
                $wc = [int]$matches['c']
                $hit = @($got | Where-Object { $_.Code -eq $wc }).Count -gt 0
            }
            if (-not $hit) { $missing += $w }
        }
        if ($missing.Count -eq 0) { $pass++ }
        else { $fails += "$base : the sidecar's $($missing -join ',') not among the guest's " + (($got | ForEach-Object { "$($_.Code)@$($_.Line):$($_.Col)" }) -join ',') }
    }
    # A subject on the image that printed no verdict is a hole in the run, not
    # a pass by omission.
    foreach ($b in $bundles) {
        if (-not $graded.ContainsKey($b)) { $fails += "$([System.IO.Path]::GetFileName($srcOf[$b])) : no verdict line from the guest" }
    }

    Write-Host ''
    Write-Host "--- disk runner against the sidecars ---"
    Write-Host "  graded: $pass pass, $($fails.Count) fail, of $($selected.Count) selected"
    if ($unmatched.Count -gt 0) {
        # A verdict for a name the manifest does not carry means the image and
        # the mapping disagree, which invalidates every row rather than one.
        Write-Host "  UNMATCHED verdict lines: $($unmatched.Count) -- $($unmatched -join ', ')"
    }
    if ($deadShards.Count -gt 0) { Write-Host "  SHARDS THAT DID NOT FINISH: $($deadShards -join ', ')" }
    foreach ($f in $fails) { Write-Host "  FAIL  $f" }
    if ($fails.Count -gt 0 -or $unmatched.Count -gt 0) { Write-Host 'DISK RUNNER: FAIL'; exit 1 }
    # A run that graded NOTHING is not a pass: every verdict line was skipped or
    # no subject reached the image.
    if ($pass -eq 0) { Write-Host 'DISK RUNNER: FAIL -- no verdict was graded, so nothing was measured'; exit 1 }
    Write-Host 'DISK RUNNER: PASS'
    exit 0
} finally {
    if (-not $Keep) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
    else { Write-Host "[disk-runner] kept: $work" }
}
