param(
    [string[]]$Chapters,
    [string]$Harness,
    [string]$OutName
)
$ErrorActionPreference = 'Stop'
$ladder = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path  # ladder-root-bootstrap: reaches the LADDER only; the checkout comes from ladder_root
$repo = (& python3 (Join-Path $ladder 'ladder_root.py') codex).Trim()
$here = $PSScriptRoot
. "$repo/codex/plugs/common/plug-build-lib.ps1"
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($ch in $Chapters) {
    $p = Join-Path $repo $ch
    if (-not (Test-Path $p)) { $p = Join-Path $here $ch }
    Add-PlugChapter -Lines $lines -Path $p -Quire 'Lexmi'
}
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Lexmi'
$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'min-subject'
