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
# WHICH SUBJECTS ARE COMPARABLE, and each exclusion is a real difference rather
# than tidiness:
#   - A chapter carrying `cites` is compiled ALONE by the runner with no cite
#     resolution, so every cited name comes back undefined. Excluded.
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
# It boots two guests: one to build the image through the img plug, one to run
# the runner. Ask the box before a large subject list, and measure free memory.
[CmdletBinding()]
param(
    [string[]]$Subjects = @(),
    [string]$SubjectsFile = '',
    [string]$Kernel = '',
    [string]$RunnerCdx = '',
    [int]$TotalSectors = 32768,
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
    $paths = @((Get-ChildItem (Join-Path $Repo 'codex\test') -Filter *.codex -File).FullName)
    Write-Host "[disk-runner] no subjects named, taking codex\test: $($paths.Count) chapter(s)"
}

# -- selection ---------------------------------------------------------------
$dropCites = 0; $dropFlags = 0; $dropSkip = 0; $dropMissing = 0
$selected = @()
foreach ($p in $paths) {
    if (-not (Test-Path -PathType Leaf $p)) { $dropMissing++; continue }
    if (Test-Path ($p -replace '\.codex$', '.skip')) { $dropSkip++; continue }
    if (Test-Path ($p -replace '\.codex$', '.flags')) { $dropFlags++; continue }
    if (Select-String -Path $p -Pattern '^\s+cites\s' -Quiet) { $dropCites++; continue }
    $selected += (Resolve-Path $p).Path
}
Write-Host "[disk-runner] selected $($selected.Count) of $($paths.Count): dropped $dropCites citing, $dropFlags with .flags, $dropSkip with .skip, $dropMissing missing"
if ($selected.Count -eq 0) { Write-Host '[disk-runner] nothing comparable to run.'; exit 0 }
if ($ListOnly) {
    foreach ($s in $selected) { Write-Host "  $($s.Substring($Repo.Length + 1))" }
    exit 0
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "disk-runner-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $listFile = Join-Path $work 'subjects.txt'
    [System.IO.File]::WriteAllLines($listFile, [string[]]$selected)
    $img = Join-Path $work 'subjects.img'
    $manifest = Join-Path $work 'manifest.tsv'

    # -- the image -----------------------------------------------------------
    $imgArgs = @('-NoProfile', '-File', (Join-Path $Repo 'codex\plugs\img\run.ps1'),
                 '-PeInput', (Join-Path $Repo 'build\boot\blockladder.efi'),
                 '-CdxInput', $Kernel, '-Out', $img, '-Fat16',
                 '-TotalSectors', "$TotalSectors", '-SourceList', $listFile,
                 '-Bucketed', '-BucketSize', "$BucketSize", '-ManifestOut', $manifest)
    & pwsh @imgArgs | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $img)) {
        [Console]::Error.WriteLine('FAIL: the image was not produced'); exit 3
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

    # -- the boot ------------------------------------------------------------
    $console = Join-Path $work 'runner.txt'
    & pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $RunnerCdx -OutFile $console -DiskFile $img | Out-Null
    if (-not (Test-Path -PathType Leaf $console)) { [Console]::Error.WriteLine('FAIL: no console output'); exit 5 }
    $lines = @(Get-Content $console)
    # A guest that died mid-run leaves a console with no closing line, which is
    # not the same as a run where every subject passed and must not read as one.
    if (-not ($lines -match '=== runner done ===')) {
        [Console]::Error.WriteLine('FAIL: the guest did not reach "runner done"; the run is incomplete and its verdicts are not a result')
        $lines | Select-Object -Last 6 | ForEach-Object { [Console]::Error.WriteLine("  guest: $_") }
        exit 6
    }

    # -- the pairing ---------------------------------------------------------
    $byName = @{}
    foreach ($row in (Get-Content $manifest)) {
        $c = $row -split "`t"
        if ($c.Count -lt 3) { continue }
        $key = if ($c[0] -eq '') { $c[1] } else { "$($c[0])/$($c[1])" }
        # The guest prints the 8.3 name with its padding removed by the FAT
        # reader, so the key is matched on the same shape the guest prints.
        $bare = ($c[1].Substring(0, 8).TrimEnd() + '.' + $c[1].Substring(8, 3).TrimEnd()).TrimEnd('.')
        $k2 = if ($c[0] -eq '') { $bare } else { "$($c[0])/$bare" }
        $byName[$k2] = $c[2]
    }

    # Names the DESUGARER writes, read from the two chapters the host resolver
    # walks unconditionally rather than listed here by hand.
    $sugarNames = @()
    foreach ($sf in @('codex\foreword\core\ListUtils.codex', 'codex\foreword\core\Tuple.codex')) {
        $sfp = Join-Path $Repo $sf
        if (Test-Path -PathType Leaf $sfp) {
            $sugarNames += @(Select-String -Path $sfp -Pattern '^\s{0,2}([A-Za-z][A-Za-z0-9_-]*) :' | ForEach-Object { $_.Matches[0].Groups[1].Value })
        }
    }
    $sugarNames = @($sugarNames | Sort-Object -Unique)

    $pass = 0; $fails = @(); $unmatched = @(); $notComparable = @()
    foreach ($l in $lines) {
        if ($l -notmatch '^\s*(?<name>\S+)\s+:\s+(?<rest>errors=.*|READ FAILED.*|frontend ok.*)$') { continue }
        $name = $matches['name']; $rest = $matches['rest']
        $src = $byName[$name]
        if (-not $src) { $unmatched += $name; continue }
        $base = [System.IO.Path]::GetFileName($src)
        $failFile = $src -replace '\.codex$', '.failing'
        $expectFail = Test-Path -PathType Leaf $failFile
        if ($rest -match '^READ FAILED') { $fails += "$base : the guest could not read it off the image"; continue }
        if ($rest -match '^frontend ok, CODEGEN errors=(?<n>\d+) first=CDX(?<c>\d+)') {
            $fails += "$base : codegen errors=$($matches['n']) first=CDX$($matches['c'])"
            continue
        }
        $errs = if ($rest -match 'errors=(?<n>\d+)') { [int]$matches['n'] } else { -1 }
        $first = if ($rest -match 'first=CDX(?<c>\d+)') { [int]$matches['c'] } else { -1 }
        $msg = if ($rest -match ' -- (?<m>.+)$') { $matches['m'].Trim() } else { '' }
        # The desugarer writes names the author never cites: a `for` loop
        # becomes map-list (Foreword ListUtils) and a tuple literal becomes
        # MkTup<N> (Foreword Tuple), so build/quire-map.ps1 walks those two
        # chapters unconditionally. The guest compiles a chapter ALONE and has
        # neither, so such a subject is NOT COMPARABLE rather than failing, and
        # it is counted apart rather than hidden.
        if ($msg -match '^Undefined name: (?<n>\S+)' -and ($sugarNames -contains $matches['n'])) {
            $notComparable += "$base : the guest lacks $($matches['n']), a name the desugarer writes and the host resolver walks unconditionally"
            continue
        }
        if ($expectFail) {
            if ($errs -le 0) { $fails += "$base : expected a compile failure, the guest reported errors=$errs"; continue }
            # The guest reports the FIRST code only, so this asks whether that
            # code is one the sidecar records. bvt.ps1 on the host asks the
            # stronger question, that EVERY recorded code appears.
            $codes = @(Get-Content $failFile | ForEach-Object { ($_ -split '@')[0].Trim() -replace '^CDX', '' } | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
            if ($codes.Count -eq 0) { $fails += "$base : .failing records no code, so its verdict cannot be graded"; continue }
            if ($codes -contains $first) { $pass++ } else { $fails += "$base : first=CDX$first is not among the sidecar's $($codes -join ',')" }
        } else {
            if ($errs -eq 0) { $pass++ } else { $fails += "$base : expected a clean compile, the guest reported errors=$errs first=CDX$first" }
        }
    }

    Write-Host ''
    Write-Host "--- disk runner against the sidecars ---"
    Write-Host "  graded: $pass pass, $($fails.Count) fail, $($notComparable.Count) not comparable, of $($selected.Count) selected"
    if ($unmatched.Count -gt 0) {
        # A verdict for a name the manifest does not carry means the image and
        # the mapping disagree, which invalidates every row rather than one.
        Write-Host "  UNMATCHED verdict lines: $($unmatched.Count) -- $($unmatched -join ', ')"
    }
    foreach ($nc in $notComparable) { Write-Host "  NOT COMPARABLE  $nc" }
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
