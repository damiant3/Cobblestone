# plug-fresh.ps1 -- dot-source from a plug's run.ps1; rebuilds the plug CDX when a source it bundles is newer.
#
# A run.ps1 executes the plug .cdx and never the .codex beside it, so a plug built before
# its emitter changed answers with the old emitter and nothing says so (L-SAMEVER; three
# such stale plugs, ptx, wgsl and wasm, were found in one blu session on 2026-10-31).
# The sources compared are the ones Build-TranspilerPlug bundles: the plug's own chapters,
# codex/plugs/common, and the compiler declaration chapters. A rebuild is about two seconds.
# Two concurrent stale runs of one plug both rebuild into the same build-output.

function Update-StalePlug {
    param([Parameter(Mandatory=$true)][string]$PlugDir, [Parameter(Mandatory=$true)][string]$PlugCdx, [string[]]$ExtraDirs = @())
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
    $srcs = @(Get-ChildItem $PlugDir -Filter *.codex -File) + @(Get-ChildItem (Join-Path $repo 'codex\plugs\common') -Filter *.codex -File)
    foreach ($xd in $ExtraDirs) { $srcs += @(Get-ChildItem (Join-Path $repo $xd) -Recurse -Filter *.codex -File | Where-Object { $_.FullName -notmatch '[\\/]build-output[\\/]' }) }
    foreach ($d in 'Core\Name', 'Core\SourceText', 'Types\CodexType', 'Ast\AstNodes', 'IR\IRChapter') {
        $p = Join-Path $repo "codex\compiler\$d.codex"
        if (Test-Path $p) { $srcs += Get-Item $p }
    }
    $newest = $srcs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path $PlugCdx) -and $newest.LastWriteTime -le (Get-Item $PlugCdx).LastWriteTime) { return }
    $why = if (Test-Path $PlugCdx) { "older than $($newest.FullName.Substring($repo.Length + 1))" } else { 'missing' }
    [Console]::Error.WriteLine("[plug-fresh] $([System.IO.Path]::GetFileName($PlugCdx)) $why; rebuilding")
    & pwsh -NoProfile -File (Join-Path $PlugDir 'build.ps1') *> $null
    if (-not (Test-Path $PlugCdx) -or (Get-Item $PlugCdx).LastWriteTime -lt $newest.LastWriteTime) {
        [Console]::Error.WriteLine("FAIL: $PlugCdx did not rebuild; see $(Join-Path $PlugDir 'build-output\build.log')")
        exit 2
    }
}
