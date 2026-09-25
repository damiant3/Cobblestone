# The manifest a plug bundle records over the sources it was assembled from.
#
# Hand-written and dot-sourced from BOTH sides on purpose: the assembler
# (codex/plugs/common/plug-build-lib.ps1) writes the manifest beside the bundle
# and build/deck-headroom.ps1 re-hashes it. Two copies of this arithmetic
# would answer differently the first time either side changed, and a
# staleness check that disagrees with its own writer reads every bundle as
# stale.
#
# It keys on CONTENT because mtime lies: `p4 sync -f`, which the gate dance
# requires, restamps every tracked source and left all 56 bundles reading
# stale at once, so the gate's plug deck phase measured nothing and failed.
#
# The manifest lists every file the assembler READ (the compiler declaration
# chapters, Lir, the plugs/common chapters and every foreword chapter a cite
# pulled in), plus the plug directory's own chapters and its build.ps1, so a
# chapter added there reads stale too. One line per input, the path relative to
# the repository and its SHA-256, tab-separated. A file that predates the
# manifest holds one bare hex digest and reads as no manifest.

$script:PlugDigestRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-PlugSourceDigestPath {
    param([string]$BundleSrc)
    return ([System.IO.Path]::ChangeExtension($BundleSrc, '.sources'))
}

function Get-PlugOwnInputs {
    param([string]$PlugDir)
    $own = @(Get-ChildItem $PlugDir -Filter '*.codex' -File -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch '\\build-output\\' } |
             ForEach-Object { $_.FullName })
    $build = Join-Path $PlugDir 'build.ps1'
    if (Test-Path -PathType Leaf $build) { $own += $build }
    return $own
}

function Get-PlugRelPath {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    return [System.IO.Path]::GetRelativePath($script:PlugDigestRepo, $full).Replace('\', '/')
}

function Write-PlugSourceManifest {
    param([string]$BundleSrc, [string]$PlugDir, [string[]]$Inputs = @())
    $seen = @{}
    $rows = [System.Collections.Generic.List[string]]::new()
    foreach ($p in (@($Inputs) + (Get-PlugOwnInputs $PlugDir))) {
        if (-not $p) { continue }
        $rel = Get-PlugRelPath $p
        if ($seen[$rel]) { continue }
        $seen[$rel] = $true
        $rows.Add($rel + "`t" + (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash)
    }
    $sorted = @($rows | Sort-Object)
    [System.IO.File]::WriteAllText((Get-PlugSourceDigestPath $BundleSrc), (($sorted -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

# $null when every recorded input still hashes as recorded and the plug
# directory holds no chapter the manifest lacks; 'none' when there is no
# manifest; otherwise the first input that moved, named.
function Test-PlugSourceManifest {
    param([string]$BundleSrc, [string]$PlugDir)
    $file = Get-PlugSourceDigestPath $BundleSrc
    if (-not (Test-Path -PathType Leaf $file)) { return 'none' }
    $recorded = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        if ($line -eq '') { continue }
        $cols = $line.Split("`t")
        if ($cols.Count -ne 2) { return 'none' }
        $recorded[$cols[0]] = $cols[1]
    }
    if ($recorded.Count -eq 0) { return 'none' }
    foreach ($rel in ($recorded.Keys | Sort-Object)) {
        $abs = if ([System.IO.Path]::IsPathRooted($rel)) { $rel } else { Join-Path $script:PlugDigestRepo $rel }
        if (-not (Test-Path -PathType Leaf $abs)) { return "$rel is gone" }
        if ((Get-FileHash -LiteralPath $abs -Algorithm SHA256).Hash -ne $recorded[$rel]) { return "$rel changed" }
    }
    foreach ($p in (Get-PlugOwnInputs $PlugDir)) {
        $rel = Get-PlugRelPath $p
        if (-not $recorded.ContainsKey($rel)) { return "$rel is new" }
    }
    return $null
}
