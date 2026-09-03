# The digest a plug bundle records over the sources it was assembled from.
#
# Hand-written and dot-sourced from BOTH sides on purpose: the assembler
# (codex/plugs/common/plug-build-lib.ps1) writes the digest beside the bundle
# and build/deck-headroom.ps1 recomputes it. Two copies of this arithmetic
# would answer differently the first time either side changed, and a
# staleness check that disagrees with its own writer reads every bundle as
# stale, which is the failure this file replaces.
#
# It keys on CONTENT because mtime lies: `p4 sync -f`, which the gate dance
# requires, restamps every tracked source and left all 56 bundles reading
# stale at once, so the gate's plug deck phase measured nothing and failed.
#
# Scope note: the set below is the plug's OWN chapters, which is the set the
# mtime check used. Add-PlugChapter also bundles compiler declaration
# chapters, Lir, PlugTypes and IRTextParser, and a change to one of those
# still does not read stale. Recorded in codex/plugs/plugs-backlog.md.

function Get-PlugSourceDigest {
    param([string]$PlugDir)
    $parts = @(Get-ChildItem $PlugDir -Filter '*.codex' -File -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -notmatch '\\build-output\\' } |
               Sort-Object Name |
               ForEach-Object { $_.Name + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash })
    $joined = ($parts -join "`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($joined))
    } finally {
        $sha.Dispose()
    }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-PlugSourceDigestPath {
    param([string]$BundleSrc)
    return ([System.IO.Path]::ChangeExtension($BundleSrc, '.sources'))
}
