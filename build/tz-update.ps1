# tz-update.ps1 -- vendor a new IANA tzdata release, or prove the vendored one.
#
# The update procedure of TimeZones.md (rung 6, ruling T4). The archive is the
# IANA release tarball (https://data.iana.org/time-zones/releases/tzdata<R>.tar.gz)
# and its SHA-256 is supplied by the operator, never taken from the archive.
#
#   pwsh build/tz-update.ps1 -Release 2026g -Archive tzdata2026g.tar.gz -ArchiveSha256 <hex> -Kernel seed\Codex.cdx
#   pwsh build/tz-update.ps1 -Release 2026f -Archive tzdata2026f.tar.gz -ArchiveSha256 <hex> -Kernel seed\Codex.cdx -Check
#
# Without -Check it refuses a release directory that already exists, then
# writes codex/foreword/core/tzdata/<R>/ (the ten rule files, LICENSE and
# MANIFEST.txt), regenerates TzSource.codex (build/gen-tz-source.ps1) and
# writes <R>/TZDB.BIN (build/tz-db.ps1), and prints what is left to the
# operator: the Perforce opens, the tests whose output names the release, and
# the oracle comparison. With -Check it writes nothing in the tree: the
# archive's rule files must equal the vendored ones byte for byte, TzSource.codex
# must be what gen-tz-source would write, and a TZDB.BIN rebuilt in scratch must
# equal the vendored one.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Release,
    [Parameter(Mandatory = $true)][string]$Archive,
    [Parameter(Mandatory = $true)][string]$ArchiveSha256,
    [Parameter(Mandatory = $true)][string]$Kernel,
    [switch]$Check
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$names = @('africa', 'antarctica', 'asia', 'australasia', 'europe', 'northamerica', 'southamerica', 'etcetera', 'backward', 'factory')
if ($Release -cnotmatch '^\d{4}[a-z]$') { throw "release must be a year and one letter, as 2026f: $Release" }
if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw "no archive at $Archive" }
$want = $ArchiveSha256.ToUpperInvariant()
$got = (Get-FileHash -LiteralPath $Archive).Hash
if ($got -ne $want) { throw "archive SHA-256 is $got, not the $want supplied" }
$target = Join-Path $repo "codex/foreword/core/tzdata/$Release"
if (-not $Check -and (Test-Path -LiteralPath $target)) { throw "$target exists; a vendored release is never rewritten" }
if ($Check -and -not (Test-Path -LiteralPath $target)) { throw "-Check needs the vendored release at $target" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('tz-update-' + [guid]::NewGuid().ToString('N'))
$unpack = Join-Path $work 'archive'
[void](New-Item -ItemType Directory -Path $unpack)
try {
    & "$env:SystemRoot\System32\tar.exe" -xzf (Resolve-Path -LiteralPath $Archive).Path -C $unpack
    if ($LASTEXITCODE -ne 0) { throw "tar could not unpack $Archive" }
    $versionFile = Join-Path $unpack 'version'
    if (-not (Test-Path -LiteralPath $versionFile)) { throw 'the archive has no version file' }
    $version = ([System.IO.File]::ReadAllText($versionFile)).Trim()
    if ($version -cne $Release) { throw "the archive is release $version, not $Release" }
    foreach ($n in $names + @('LICENSE')) {
        if (-not (Test-Path -LiteralPath (Join-Path $unpack $n) -PathType Leaf)) { throw "the archive has no $n" }
    }

    if ($Check) {
        $bad = @()
        foreach ($n in $names + @('LICENSE')) {
            if ((Get-FileHash -LiteralPath (Join-Path $unpack $n)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $n)).Hash) { $bad += $n }
        }
        $manifest = [System.IO.File]::ReadAllText((Join-Path $target 'MANIFEST.txt'))
        if (-not $manifest.Contains("archive SHA-256 $want")) { $bad += 'MANIFEST.txt archive hash' }
        & pwsh -NoProfile -File (Join-Path $repo 'build/gen-tz-source.ps1') -Release $Release -Check
        if ($LASTEXITCODE -ne 0) { $bad += 'TzSource.codex' }
        $bin = Join-Path $work 'TZDB.BIN'
        & pwsh -NoProfile -File (Join-Path $repo 'build/tz-db.ps1') -Kernel $Kernel -Out $bin
        if (-not (Test-Path -LiteralPath $bin) -or (Get-FileHash -LiteralPath $bin).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target 'TZDB.BIN')).Hash) { $bad += 'TZDB.BIN' }
        if ($bad.Count -gt 0) { Write-Host "tz-update: release $Release differs from the tree: $($bad -join ', ')"; exit 1 }
        Write-Host "tz-update: release $Release reproduces the tree: 10 rule files, LICENSE, MANIFEST archive hash, TzSource.codex and TZDB.BIN"
        exit 0
    }

    [void](New-Item -ItemType Directory -Path $target)
    $lines = @("# IANA time zone database $Release, from https://data.iana.org/time-zones/releases/tzdata$Release.tar.gz (archive SHA-256 $want, $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'))). Public domain. SHA-256 per file, in parse order.")
    foreach ($n in $names) {
        Copy-Item -LiteralPath (Join-Path $unpack $n) -Destination (Join-Path $target $n)
        $lines += "$((Get-FileHash -LiteralPath (Join-Path $target $n)).Hash)  $n"
    }
    Copy-Item -LiteralPath (Join-Path $unpack 'LICENSE') -Destination (Join-Path $target 'LICENSE')
    [System.IO.File]::WriteAllText((Join-Path $target 'MANIFEST.txt'), (($lines -join "`r`n") + "`r`n"), [System.Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -File (Join-Path $repo 'build/gen-tz-source.ps1') -Release $Release
    if ($LASTEXITCODE -ne 0) { throw 'gen-tz-source refused the release' }
    & pwsh -NoProfile -File (Join-Path $repo 'build/tz-db.ps1') -Kernel $Kernel -Out (Join-Path $target 'TZDB.BIN')
    if ($LASTEXITCODE -ne 0) { throw 'tz-db could not build TZDB.BIN' }
    $pip = "$($Release.Substring(0, 4)).$([int][char]$Release[4] - [int][char]'a' + 1)"
    Write-Host "tz-update: vendored $Release. Left to the operator:"
    Write-Host "  p4 add codex/foreword/core/tzdata/$Release/... ; p4 edit codex/foreword/core/TzSource.codex"
    Write-Host "  build/cite-gate.ps1 -Files codex/foreword/core/TzSource.codex -Kernel <kernel>: re-record each golden that names the release or its counts, accounting every changed line"
    Write-Host "  pip tzdata $pip into scratch, then build/tz-oracle.ps1 -Release $Release -PyTarget <dir> with -Names, -Compiled, -Local and -Future against each test's output"
    Write-Host "  point the ESP image's TZDB.BIN at the new directory (TimeZones.md section 7, item 1)"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
