# Prepare the arcade's art for the static site.
#
# assets/games/ holds 70 MB, most of it 1280x800 PNG scenes at about 1.5 MB
# each. Shipping those verbatim would put roughly 40 MB of background into a
# landing bundle whose whole hero set is under 800 KB, so the scenes are
# re-encoded to JPEG here and the small things are copied as they are.
#
# The output is BUILD OUTPUT and .p4ignore'd, exactly like the wasm modules
# beside it: the tracked source is assets/games/, and the bundle is
# reproducible from the depot rather than from whoever last ran this.
#
# Backgammon is deliberately absent from the scene list. Its photographic
# board was the one Damian called out as looking bad, and the arcade draws
# that board itself instead (2026-09-01).
#
# Usage: pwsh apps/games/build-art.ps1 [-Quality 80] [-MaxWidth 1280]
[CmdletBinding()]
param(
    [int]$Quality = 80,
    [int]$MaxWidth = 1280,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Src  = Join-Path $Repo 'assets\games'
$Out  = Join-Path $Repo 'apps\landing\web\games\art'

if (-not (Test-Path -PathType Container $Src)) {
    Write-Host "[art] REFUSE: no assets at $Src"; exit 2
}

# Games whose scene is worth carrying. A game not listed simply has no
# backdrop and falls back to the page's own colour, which is not a defect.
$Scenes = @(
    'battleship','blackjack','bridge','checkers','connect4','crazyeights',
    'dotsandboxes','game2048','go','gofish','hexgame','hexwar','liarsdice',
    'life','mahjong','mancala','minesweeper','monopoly','othello','pinochle',
    'poker','pokervariants','risk','royalur','rps','setgame','spider',
    'sudoku','tictactoe','war','yahtzee'
)

# Board and table textures that carry a game's look on their own.
$Boards = @{
    'checkers' = 'checkers\board.png'
    'mancala'  = 'mancala\board.png'
    'royalur'  = 'royalur\board.png'
    'blackjack'= 'blackjack\felt.png'
    'battleship'= 'battleship\grid.png'
    'bridge'   = 'bridge\table.png'
}

$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' }
$params = New-Object System.Drawing.Imaging.EncoderParameters 1
$params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
    [System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)

# The parameter names matter: PowerShell variables are case-insensitive, so
# an $out parameter here IS the script's $Out and the second call writes
# inside the first call's output path.
function Convert-Scene([string]$srcFile, [string]$dstFile, [int]$maxW) {
    $img = [System.Drawing.Image]::FromFile($srcFile)
    try {
        $w = $img.Width; $h = $img.Height
        if ($w -gt $maxW) { $h = [int]($h * $maxW / $w); $w = $maxW }
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = 'HighQualityBicubic'
                $g.SmoothingMode = 'HighQuality'
                # JPEG has no alpha. Compose onto the page's own background
                # so a transparent PNG does not come out on black.
                $g.Clear([System.Drawing.Color]::FromArgb(10, 10, 18))
                $g.DrawImage($img, 0, 0, $w, $h)
            } finally { $g.Dispose() }
            $bmp.Save($dstFile, $jpeg, $params)
        } finally { $bmp.Dispose() }
    } finally { $img.Dispose() }
}

foreach ($d in 'scenes','boards','cards','thumbs') {
    $p = Join-Path $Out $d
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Force $p | Out-Null }
}

$made = 0; $bytes = 0; $skipped = @()

foreach ($g in $Scenes) {
    $srcPng = Join-Path $Src "$g\bg-scene.png"
    if (-not (Test-Path -PathType Leaf $srcPng)) { $skipped += $g; continue }
    $dst = Join-Path $Out "scenes\$g.jpg"
    if ($Force -or -not (Test-Path $dst) -or (Get-Item $srcPng).LastWriteTimeUtc -gt (Get-Item $dst).LastWriteTimeUtc) {
        Convert-Scene $srcPng $dst $MaxWidth
    }
    $made++; $bytes += (Get-Item $dst).Length
}

foreach ($g in $Boards.Keys) {
    $srcPng = Join-Path $Src $Boards[$g]
    if (-not (Test-Path -PathType Leaf $srcPng)) { continue }
    $dst = Join-Path $Out "boards\$g.jpg"
    if ($Force -or -not (Test-Path $dst) -or (Get-Item $srcPng).LastWriteTimeUtc -gt (Get-Item $dst).LastWriteTimeUtc) {
        Convert-Scene $srcPng $dst 1024
    }
    $bytes += (Get-Item $dst).Length
}

# The painted card faces are NOT carried. Cards are drawn by the page now:
# a rank in the corner and a pip through the middle, which is what a tableau
# ten columns wide can actually be read from. The art was 704 KB to say less.
$cardDir = Join-Path $Out 'cards'
if (Test-Path $cardDir) { Remove-Item $cardDir -Recurse -Force }
foreach ($f in Get-ChildItem (Join-Path $Src 'thumbs') -Filter *.png) {
    Copy-Item $f.FullName (Join-Path $Out "thumbs\$($f.Name)") -Force
    $bytes += $f.Length
}

Write-Host ("[art] {0} scenes, {1} boards and the thumbnails -> {2:N1} MB total" -f `
    $made, $Boards.Count, ($bytes / 1MB))
if ($skipped.Count) { Write-Host "[art] no scene for: $($skipped -join ', ')" }
Write-Host "[art] OK: $Out"
