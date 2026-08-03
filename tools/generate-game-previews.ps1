# generate-game-previews.ps1 -- Generate per-game hover audio previews via MusicGen.
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'assets\games\previews'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$MusicGenUrl = 'http://localhost:7861/gradio_api/call/predict_full'

function Generate-Audio {
    param([string]$Prompt, [double]$Duration, [string]$OutFile, [double]$Temp = 1.0, [double]$Cfg = 3.5)
    if ((Test-Path $OutFile) -and -not $Force) { Write-Host "  SKIP $(Split-Path -Leaf $OutFile)" -ForegroundColor Gray; return }
    Write-Host "  $(Split-Path -Leaf $OutFile)..." -ForegroundColor Yellow -NoNewline
    $body = @{ data = @('facebook/musicgen-medium','','Default',$Prompt,$null,$Duration,250,0.0,$Temp,$Cfg) } | ConvertTo-Json -Depth 10
    try {
        $submit = Invoke-WebRequest -Uri $MusicGenUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30
        $eventId = ($submit.Content | ConvertFrom-Json).event_id
        $deadline = (Get-Date).AddSeconds(90)
        $audioUrl = $null
        while ((Get-Date) -lt $deadline) {
            Start-Sleep 2
            try {
                $poll = Invoke-WebRequest -Uri "$MusicGenUrl/$eventId" -UseBasicParsing -TimeoutSec 10
                if ($poll.Content -match 'event:\s*complete') {
                    $poll.Content -split "`n" | Where-Object { $_ -match '^data:\s' } | ForEach-Object {
                        $json = $_ -replace '^data:\s*',''
                        try { $p = $json | ConvertFrom-Json; if ($p -and $p.Count -gt 0 -and $p[0].url) { $audioUrl = $p[0].url } } catch {}
                    }
                    break
                }
            } catch {}
        }
        if (-not $audioUrl) { Write-Host " FAIL" -ForegroundColor Red; return }
        $dlUrl = if ($audioUrl.StartsWith('http')) { $audioUrl } else { "http://localhost:7861$audioUrl" }
        [System.IO.File]::WriteAllBytes($OutFile, (Invoke-WebRequest -Uri $dlUrl -UseBasicParsing -TimeoutSec 30).Content)
        Write-Host " OK ($([math]::Round((Get-Item $OutFile).Length/1024,1))KB)" -ForegroundColor Green
    } catch { Write-Host " FAIL: $_" -ForegroundColor Red }
}

$Games = @(
    @{ Id='backgammon';    Prompt='dice rolling on wooden backgammon board, pieces sliding, short gameplay' }
    @{ Id='battleship';    Prompt='naval sonar ping then explosion splash, battleship combat, short dramatic' }
    @{ Id='blackjack';     Prompt='casino cards being dealt on felt table, chips clinking, blackjack dealing' }
    @{ Id='bridge';        Prompt='cards being sorted in hand, gentle shuffling, bridge card game atmosphere' }
    @{ Id='checkers';      Prompt='wooden checkers piece sliding and capturing with a double jump sound' }
    @{ Id='connect4';      Prompt='plastic disc dropping into slot and clicking into place, connect four' }
    @{ Id='crazyeights';   Prompt='fast card slapping down on table, wild card played, energetic card game' }
    @{ Id='dotsandboxes';  Prompt='pencil drawing lines on paper, short scratching, dots and boxes' }
    @{ Id='game2048';      Prompt='digital tiles sliding and merging with ascending chime, puzzle game' }
    @{ Id='go';            Prompt='stone placed on wooden go board with a resonant click, zen atmosphere' }
    @{ Id='gofish';        Prompt='splashing water with card flip, playful fishing sound, whimsical' }
    @{ Id='hexgame';       Prompt='hexagonal tile placement click with strategic tension, abstract board game' }
    @{ Id='hexwar';        Prompt='military radio static then artillery boom, wargame battlefield, tactical' }
    @{ Id='liarsdice';     Prompt='dice shaking in cup then slammed down on table, tense reveal' }
    @{ Id='life';          Prompt='electronic cellular growth sounds, digital organisms pulsing, generative' }
    @{ Id='mahjong';       Prompt='mahjong tiles clacking together, ceramic shuffling, satisfying clicks' }
    @{ Id='mancala';       Prompt='small stones dropping into wooden bowl pits, mancala sowing rhythm' }
    @{ Id='mastermind';    Prompt='code pegs clicking into place, electronic correct answer beep, puzzle' }
    @{ Id='minesweeper';   Prompt='ticking tension then safe cell reveal chime, minesweeper suspense' }
    @{ Id='monopoly';      Prompt='dice rolling then metal token sliding on board, cash register ching' }
    @{ Id='othello';       Prompt='reversi disc flipping with cascading clicks, multiple pieces turning' }
    @{ Id='pinochle';      Prompt='cards being played in trick, old fashioned card table, classic' }
    @{ Id='poker';         Prompt='poker chips being tossed into pot, cards flipped for showdown, tension' }
    @{ Id='pokervariants'; Prompt='rapid card dealing to multiple players, poker chips shuffling, casino' }
    @{ Id='risk';          Prompt='war drums building then dice clash for battle, global conquest drama' }
    @{ Id='rps';           Prompt='fist pounding three times then reveal with whoosh, rock paper scissors' }
    @{ Id='setgame';       Prompt='rapid pattern matching beeps, cards flipping fast, set found chime' }
    @{ Id='spider';        Prompt='cards cascading in solitaire tableau, satisfying sequence completion' }
    @{ Id='sudoku';        Prompt='pencil writing numbers, erasing and rewriting, logical deduction, calm' }
    @{ Id='tictactoe';     Prompt='marker drawing X and O on board with squeaky pen, quick game' }
    @{ Id='war';           Prompt='two cards slapped down simultaneously, dramatic reveal, war card clash' }
    @{ Id='yahtzee';       Prompt='five dice shaking vigorously in cup then rolling across table, yahtzee' }
    @{ Id='minimax';       Prompt='computer processing sounds, electronic thinking beeps, AI calculating' }
    @{ Id='rng';           Prompt='slot machine spinning reels then stopping one by one, random numbers' }
    @{ Id='gamesdemo';     Prompt='retro arcade startup sounds, multiple game jingles overlapping, 8-bit' }
    @{ Id='magic';         Prompt='magical spell casting with crystalline mana sounds, fantasy card game' }
)

Write-Host "Generating $($Games.Count) game preview clips..." -ForegroundColor Cyan
foreach ($g in $Games) {
    Generate-Audio -Prompt $g.Prompt -Duration 3.0 -OutFile (Join-Path $OutDir "$($g.Id).wav") -Temp 0.9 -Cfg 3.5
}
Write-Host "Done." -ForegroundColor Cyan
