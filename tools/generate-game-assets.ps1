# generate-game-assets.ps1 -- Generate game thumbnails via Stable Diffusion Forge.
# Usage: tools/generate-game-assets.ps1 [-Force]
# Requires Forge running at http://127.0.0.1:7860
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'assets\games\thumbs'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$ForgeUrl = 'http://127.0.0.1:7860/sdapi/v1/txt2img'

$StylePrefix = 'fantasy concept art, cinematic dramatic lighting, detailed illustration, game art, '
$NegPrompt = 'text, watermark, blurry, ugly, deformed, noisy, grainy, low quality, amateur, photo, photograph'

$Games = @(
    @{ Id='backgammon';    Prompt='backgammon board with triangular points and dice, wooden board texture, warm lighting' }
    @{ Id='battleship';    Prompt='naval battleship grid with explosion markers and ship silhouettes, blue ocean' }
    @{ Id='blackjack';     Prompt='blackjack cards ace and king on green felt table with chips, casino glow' }
    @{ Id='bridge';        Prompt='four hands of playing cards spread in a fan, bridge game, elegant' }
    @{ Id='checkers';      Prompt='checkerboard with red and black pieces, some kings crowned, dramatic angle' }
    @{ Id='connect4';      Prompt='connect four vertical board with red and yellow discs dropping, blue frame' }
    @{ Id='crazyeights';   Prompt='playing cards with an eight card glowing, wild card energy, colorful spread' }
    @{ Id='dotsandboxes';  Prompt='dots and lines grid forming colored boxes, pencil game, paper texture' }
    @{ Id='game2048';      Prompt='2048 number tiles sliding on a grid, glowing numbers 2 4 8 16 32, minimal' }
    @{ Id='go';            Prompt='go board with black and white stones on wooden surface, zen garden atmosphere' }
    @{ Id='gofish';        Prompt='colorful fish swimming among playing cards underwater, whimsical' }
    @{ Id='hexgame';       Prompt='hexagonal game board with blue and red hex tiles, geometric pattern' }
    @{ Id='hexwar';        Prompt='hex wargame map with military unit counters, terrain features, tactical' }
    @{ Id='liarsdice';     Prompt='five dice hidden under cups, one revealed, poker face bluffing, dramatic' }
    @{ Id='life';          Prompt='conway game of life cellular automaton, glowing green cells on dark grid, organic patterns' }
    @{ Id='mahjong';       Prompt='mahjong tiles stacked in pyramid formation, ornate chinese characters, silk' }
    @{ Id='mancala';       Prompt='wooden mancala board with colorful stones in carved pits, african art style' }
    @{ Id='mastermind';    Prompt='colored code pegs in a row with black and white hint pegs, code breaking' }
    @{ Id='minesweeper';   Prompt='minesweeper grid with numbers revealed and hidden mines, tense, one bomb exposed' }
    @{ Id='monopoly';      Prompt='monopoly board corner with houses hotels and dice, money bills scattered' }
    @{ Id='othello';       Prompt='othello reversi board with black and white discs flipping, green felt' }
    @{ Id='pinochle';      Prompt='trick-taking card game with melds displayed, classic playing cards, vintage' }
    @{ Id='poker';         Prompt='poker hand royal flush with chips stacked, green table, dramatic spotlight' }
    @{ Id='pokervariants'; Prompt='multiple poker hands fanned out with variant labels, casino variety' }
    @{ Id='risk';          Prompt='world map with colored army pieces and dice, global conquest, dramatic' }
    @{ Id='rps';           Prompt='rock paper scissors hands in pixel art, three choices glowing, versus' }
    @{ Id='setgame';       Prompt='set game cards with shapes colors and patterns, three matching cards highlighted' }
    @{ Id='spider';        Prompt='spider solitaire card tableau with suits descending, spider web overlay' }
    @{ Id='sudoku';        Prompt='sudoku number grid partially filled, pencil marks, clean mathematical' }
    @{ Id='tictactoe';     Prompt='tic tac toe grid with glowing X and O marks, neon lines on dark' }
    @{ Id='war';           Prompt='two playing cards face up in battle, war card game, explosive clash' }
    @{ Id='yahtzee';       Prompt='five dice showing yahtzee five of a kind, scorecard, celebration' }
    @{ Id='minimax';       Prompt='game tree diagram with nodes branching, AI brain, strategic thinking' }
    @{ Id='rng';           Prompt='spinning slot machine reels with random numbers, probability, mathematical' }
    @{ Id='gamesdemo';     Prompt='arcade cabinet with multiple game screens, retro gaming collection' }
    @{ Id='magic';         Prompt='magic the gathering spell book with glowing mana crystals and cards floating, fantasy' }
)

Write-Host "Codex Game Asset Generator" -ForegroundColor Cyan
Write-Host "Output: $OutDir" -ForegroundColor Gray
Write-Host "Checking Forge at $ForgeUrl..." -ForegroundColor Gray

try {
    Invoke-WebRequest -Uri 'http://127.0.0.1:7860/sdapi/v1/options' -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Host "  Forge is online." -ForegroundColor Green
} catch {
    Write-Host "  Forge is not running. Start it and try again." -ForegroundColor Red
    exit 1
}

$generated = 0; $skipped = 0; $failed = 0

foreach ($game in $Games) {
    $outFile = Join-Path $OutDir "$($game.Id).png"
    if ((Test-Path $outFile) -and -not $Force) {
        Write-Host "  SKIP $($game.Id) (exists)" -ForegroundColor Gray
        $skipped++
        continue
    }

    Write-Host "  Generating $($game.Id)..." -ForegroundColor Yellow -NoNewline

    $body = @{
        prompt = $StylePrefix + $game.Prompt
        negative_prompt = $NegPrompt
        width = 1024
        height = 680
        steps = 6
        cfg_scale = 2.0
        sampler_name = 'DPM++ SDE'
        scheduler = 'karras'
        seed = -1
        batch_size = 1
        n_iter = 1
        save_images = $false
        send_images = $true
    } | ConvertTo-Json

    try {
        $resp = Invoke-WebRequest -Uri $ForgeUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 120
        $result = $resp.Content | ConvertFrom-Json
        $imgBytes = [Convert]::FromBase64String($result.images[0])
        $ms = [System.IO.MemoryStream]::new($imgBytes)
        $bmp = [System.Drawing.Bitmap]::new($ms)
        $thumb = [System.Drawing.Bitmap]::new(512, 340)
        $g = [System.Drawing.Graphics]::FromImage($thumb)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($bmp, 0, 0, 512, 340)
        $g.Dispose()
        $thumb.Save($outFile, [System.Drawing.Imaging.ImageFormat]::Png)
        $thumb.Dispose(); $bmp.Dispose(); $ms.Dispose()
        $size = [math]::Round((Get-Item $outFile).Length / 1024, 1)
        Write-Host " OK (${size}KB)" -ForegroundColor Green
        $generated++
    } catch {
        Write-Host " FAIL: $_" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done: $generated generated, $skipped skipped, $failed failed." -ForegroundColor Cyan
Write-Host "Assets in: $OutDir" -ForegroundColor Gray
