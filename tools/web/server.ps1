# server.ps1 — Codex web dashboard.
# Usage: tools/web/server.ps1 [-Port 8080]
[CmdletBinding()]
param([int]$Port = 8080)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WebDir = $PSScriptRoot
$Repo   = (Resolve-Path (Join-Path $WebDir '..\..')).Path

# ── Game catalog ──────────────────────────────────────────────────────

$GameCatalog = @(
    @{ Id='royalur';       Name='Royal Game of Ur';    Cat='Board';    Desc='Ancient Mesopotamian race game, 2600 BCE — capture, rosettes, tetrahedral dice'; Players='2'; Icon='&#x1F3DB;' }
    @{ Id='backgammon';    Name='Backgammon';          Cat='Board';    Desc='24-point board with dice-based movement and doubling cube'; Players='2'; Icon='&#x1F3B2;' }
    @{ Id='battleship';    Name='Battleship';          Cat='Strategy'; Desc='10x10 grid naval combat — hunt the hidden fleet'; Players='2'; Icon='&#x1F6A2;' }
    @{ Id='blackjack';     Name='Blackjack';           Cat='Card';     Desc='Beat the dealer to 21 with basic strategy AI'; Players='1'; Icon='&#x1F0CF;' }
    @{ Id='bridge';        Name='Bridge';              Cat='Card';     Desc='4-player contract bridge with HCP bidding'; Players='4'; Icon='&#x2660;' }
    @{ Id='checkers';      Name='Checkers';            Cat='Board';    Desc='English draughts with king promotions and minimax AI'; Players='2'; Icon='&#x26C0;' }
    @{ Id='connect4';      Name='Connect Four';        Cat='Board';    Desc='Drop discs to connect four — alpha-beta pruning AI'; Players='2'; Icon='&#x1F534;' }
    @{ Id='crazyeights';   Name='Crazy Eights';        Cat='Card';     Desc='Eights are wild — match suit or rank to shed cards'; Players='2-4'; Icon='&#x1F0A8;' }
    @{ Id='dotsandboxes';  Name='Dots and Boxes';      Cat='Other';    Desc='Complete boxes on a 4x4 dot grid to score points'; Players='2'; Icon='&#x25A2;' }
    @{ Id='game2048';      Name='2048';                Cat='Puzzle';   Desc='Slide and merge tiles on a 4x4 grid'; Players='1'; Icon='&#x1F522;' }
    @{ Id='go';            Name='Go';                  Cat='Board';    Desc='9x9 board with area scoring and Ko rule'; Players='2'; Icon='&#x26AB;' }
    @{ Id='gofish';        Name='Go Fish';             Cat='Card';     Desc='Ask for ranks to complete books of four'; Players='2-4'; Icon='&#x1F41F;' }
    @{ Id='hexgame';       Name='Hex';                 Cat='Board';    Desc='11x11 board — connect your edges with BFS flood-fill'; Players='2'; Icon='&#x2B22;' }
    @{ Id='hexwar';        Name='Hex War';             Cat='Strategy'; Desc='Hex-and-counter wargame with terrain and CRT'; Players='2'; Icon='&#x2694;' }
    @{ Id='liarsdice';     Name="Liar's Dice";          Cat='Dice';     Desc='Bluff and bid on hidden dice — probability AI'; Players='2-4'; Icon='&#x1F3B2;' }
    @{ Id='life';          Name="Conway's Life";         Cat='Puzzle';   Desc='20x20 toroidal cellular automaton — B3/S23 rules'; Players='0'; Icon='&#x1F9EC;' }
    @{ Id='mahjong';       Name='Mahjong Solitaire';   Cat='Other';    Desc='Shanghai variant — match and remove 144 free tiles'; Players='1'; Icon='&#x1F004;' }
    @{ Id='mancala';       Name='Mancala';             Cat='Board';    Desc='Kalah variant with sowing, capture, and extra turns'; Players='2'; Icon='&#x1F95C;' }
    @{ Id='mastermind';    Name='Mastermind';           Cat='Puzzle';   Desc='Crack the 4-peg color code with elimination AI'; Players='1'; Icon='&#x1F510;' }
    @{ Id='minesweeper';   Name='Minesweeper';         Cat='Puzzle';   Desc='9x9 grid with 10 mines — flood-fill and zero-risk AI'; Players='1'; Icon='&#x1F4A3;' }
    @{ Id='monopoly';      Name='Monopoly';            Cat='Strategy'; Desc='40-space property trading — simplified, no houses'; Players='2-4'; Icon='&#x1F3E0;' }
    @{ Id='othello';       Name='Othello';             Cat='Board';    Desc='8x8 Reversi — flip pieces in all 8 directions'; Players='2'; Icon='&#x25CF;' }
    @{ Id='pinochle';      Name='Pinochle';            Cat='Card';     Desc='4-player trick-taking with 48-card meld deck'; Players='4'; Icon='&#x1F0DB;' }
    @{ Id='poker';         Name='Poker';               Cat='Card';     Desc='5-card draw with 9 hand ranks'; Players='2'; Icon='&#x1F0A1;' }
    @{ Id='pokervariants'; Name='Poker Variants';      Cat='Card';     Desc='7 variants — Stud, Baseball, Hi/Low Chicago, more'; Players='2-6'; Icon='&#x1F0AA;' }
    @{ Id='risk';          Name='Risk';                Cat='Strategy'; Desc='12 territories in 4 continents — conquer the world'; Players='2-4'; Icon='&#x1F30D;' }
    @{ Id='rps';           Name='Rock Paper Scissors'; Cat='Other';    Desc='Best-of-N with history-weighted counter AI'; Players='2'; Icon='&#x270A;' }
    @{ Id='setgame';       Name='The Set Game';        Cat='Other';    Desc='Find sets among 81 cards with 4 attributes'; Players='1+'; Icon='&#x1F0DF;' }
    @{ Id='spider';        Name='Spider Solitaire';    Cat='Card';     Desc='2-suit 104-card solitaire in 10 tableau columns'; Players='1'; Icon='&#x1F578;' }
    @{ Id='sudoku';        Name='Sudoku';              Cat='Puzzle';   Desc='9x9 grid generator and backtracking solver'; Players='1'; Icon='&#x1F9E9;' }
    @{ Id='tictactoe';     Name='Tic-Tac-Toe';        Cat='Board';    Desc='3x3 classic with perfect-play minimax AI'; Players='2'; Icon='&#x274C;' }
    @{ Id='war';           Name='War';                 Cat='Card';     Desc='Flip and compare — ties trigger war rounds'; Players='2'; Icon='&#x1F4A5;' }
    @{ Id='yahtzee';       Name='Yahtzee';             Cat='Dice';     Desc='5 dice, 3 rolls, 13 scoring categories'; Players='1+'; Icon='&#x1F3AF;' }
    @{ Id='minimax';       Name='Minimax';             Cat='Utility';  Desc='Iterative depth-2 AI framework for board games'; Players='-'; Icon='&#x1F9E0;' }
    @{ Id='rng';           Name='RNG';                 Cat='Utility';  Desc='LCG-based deterministic pseudo-random generator'; Players='-'; Icon='&#x1F3B0;' }
    @{ Id='gamesdemo';     Name='Games Demo';          Cat='Utility';  Desc='Entry point that runs all classic games'; Players='-'; Icon='&#x25B6;' }
    @{ Id='magic';         Name='Magic: The Gathering'; Cat='Magic';   Desc='Full rules engine — 20 modules, AI personalities, tournaments'; Players='2'; Icon='&#x2728;' }
)

# ── Data-gathering ────────────────────────────────────────────────────

function Get-SeedInfo {
    $seed = Join-Path $Repo 'seed\Codex.cdx'
    if (-not (Test-Path $seed)) { return @{ Exists = $false } }
    $item = Get-Item $seed
    $hash = (Get-FileHash -Algorithm SHA256 $seed).Hash.Substring(0, 16)
    @{ Exists = $true; Size = $item.Length; Hash = $hash; Modified = $item.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
}

function Get-RecentChanges {
    try { $raw = p4 changes -m 20 //Codex/main/... 2>&1 | Out-String } catch { return @() }
    $lines = $raw -split "`n" | Where-Object { $_ -match '^Change' }
    $lines | ForEach-Object {
        if ($_ -match "^Change (\d+) on (\S+) by (\S+) '(.+)'") {
            @{ CL = $matches[1]; Date = $matches[2]; User = $matches[3]; Desc = $matches[4].TrimEnd("'") }
        }
    }
}

function Get-ModuleCounts {
    $quires = @(
        @{N='Compiler';  P='codex\compiler'}
        @{N='Foreword';  P='codex\foreword\core'}
        @{N='AI';        P='codex\foreword\ai'}
        @{N='Compress';  P='codex\foreword\compress'}
        @{N='Encode';    P='codex\foreword\encode'}
        @{N='Game';      P='codex\foreword\game'}
        @{N='Math';      P='codex\foreword\math'}
        @{N='Signal';    P='codex\foreword\signal'}
        @{N='Sim';       P='codex\foreword\sim'}
        @{N='UI';        P='codex\foreword\ui'}
        @{N='Kernel';    P='codex\os\kernel'}
        @{N='OS';        P='codex\os\core'}
        @{N='Dev';       P='codex\os\dev'}
        @{N='Net';       P='codex\os\net'}
        @{N='Observe';   P='codex\os\observe'}
        @{N='Replay';    P='codex\os\replay'}
        @{N='Sched';     P='codex\os\sched'}
        @{N='Trust';     P='codex\os\trust'}
        @{N='Verify';    P='codex\os\verify'}
        @{N='Works';     P='apps\works'}
        @{N='Games';     P='apps\games'}
    )
    $total = 0
    $rows = foreach ($q in $quires) {
        $dir = Join-Path $Repo $q.P
        $c = if (Test-Path $dir) { (Get-ChildItem "$dir\*.codex" -File -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }
        $total += $c
        @{ Name = $q.N; Path = $q.P; Count = $c }
    }
    @{ Quires = $rows; Total = $total }
}

function Get-TestCounts {
    $dir = Join-Path $Repo 'codex\test'
    $tests = if (Test-Path $dir) { (Get-ChildItem "$dir\*.codex" -File -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }
    $apps = if (Test-Path "$dir\apps") { (Get-ChildItem "$dir\apps\*.codex" -File -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }
    $errors = if (Test-Path "$dir\errors") { (Get-ChildItem "$dir\errors\*.codex" -File -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }
    $forewords = if (Test-Path "$dir\forewords") { (Get-ChildItem "$dir\forewords\*.codex" -File -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }
    $resultsDir = Join-Path $Repo 'test-output\_results'
    $pass = 0; $fail = 0; $skip = 0; $lastRun = ''
    if (Test-Path $resultsDir) {
        foreach ($f in Get-ChildItem -File $resultsDir) {
            $line = Get-Content -TotalCount 1 $f.FullName
            if ($line -match '^PASS') { $pass++ }
            elseif ($line -match '^FAIL') { $fail++ }
            elseif ($line -match '^SKIP') { $skip++ }
        }
        $newest = Get-ChildItem -File $resultsDir | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($newest) { $lastRun = $newest.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
    }
    @{ Tests = $tests; Apps = $apps; Errors = $errors; Forewords = $forewords;
       Pass = $pass; Fail = $fail; Skip = $skip; LastRun = $lastRun }
}

function Get-CompilerLines {
    $dir = Join-Path $Repo 'codex\compiler'
    $total = 0
    if (Test-Path $dir) {
        Get-ChildItem "$dir\*.codex" -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $total += (Get-Content $_.FullName | Measure-Object -Line).Lines
        }
    }
    $total
}

# ── HTML builders ─────────────────────────────────────────────────────

function Build-StatusContent {
    $seed = Get-SeedInfo
    $changes = Get-RecentChanges
    $mods = Get-ModuleCounts
    $tests = Get-TestCounts
    $lines = Get-CompilerLines
    $now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')

    $seedHtml = if ($seed.Exists) {
        "<span class='ok'>$($seed.Size.ToString('N0')) bytes</span> &mdash; sha256: <code>$($seed.Hash)&hellip;</code><br>Last modified: $($seed.Modified)"
    } else { "<span class='warn'>MISSING</span>" }

    $testStatus = if ($tests.LastRun) {
        $color = if ($tests.Fail -gt 0) { 'fail' } else { 'ok' }
        "<span class='$color'>$($tests.Pass) pass</span>"
        if ($tests.Fail -gt 0) { " / <span class='fail'>$($tests.Fail) fail</span>" }
        " / $($tests.Skip) skip &mdash; last run $($tests.LastRun)"
    } else { "<span class='info'>no results yet</span>" }

    $clRows = ($changes | ForEach-Object {
        $user = $_.User -replace '@.*',''
        "<tr><td class='cl'>$($_.CL)</td><td>$($_.Date)</td><td>$user</td><td>$($_.Desc)</td></tr>"
    }) -join "`n"

    $quireRows = ($mods.Quires | ForEach-Object {
        $bar = [string]::new([char]0x2588, [math]::Min($_.Count, 50))
        "<tr><td>$($_.Name)</td><td class='path'>$($_.Path)</td><td class='num'>$($_.Count)</td><td class='bar'>$bar</td></tr>"
    }) -join "`n"

    @"
<div class="subtitle">Self-sustaining compiler &middot; bare-metal x86-64 &middot; $now</div>

<div class="grid">
  <div class="card">
    <h2>Seed CDX</h2>
    <p>$seedHtml</p>
  </div>
  <div class="card">
    <h2>Test Battery</h2>
    <p>$testStatus</p>
    <p style="margin-top:8px;color:#8b949e">$($tests.Tests) samples &middot; $($tests.Apps) apps &middot; $($tests.Errors) error tests &middot; $($tests.Forewords) foreword tests</p>
  </div>
</div>

<div class="card">
  <h2>At a Glance</h2>
  <div class="stats-row">
    <div class="stat-box"><div class="stat">$($mods.Total)</div><div class="stat-label">modules</div></div>
    <div class="stat-box"><div class="stat">$($mods.Quires.Count)</div><div class="stat-label">quires</div></div>
    <div class="stat-box"><div class="stat">$($lines.ToString('N0'))</div><div class="stat-label">compiler lines</div></div>
    <div class="stat-box"><div class="stat">$($tests.Tests + $tests.Apps + $tests.Errors + $tests.Forewords)</div><div class="stat-label">test files</div></div>
  </div>
</div>

<div class="card">
  <h2>Modules by Quire</h2>
  <table>
    <tr><th>Quire</th><th>Path</th><th style="text-align:right">Count</th><th></th></tr>
    $quireRows
    <tr style="font-weight:bold"><td>Total</td><td></td><td class="num">$($mods.Total)</td><td></td></tr>
  </table>
</div>

<div class="card">
  <h2>Recent Changelists</h2>
  <table>
    <tr><th>CL</th><th>Date</th><th>Author</th><th>Description</th></tr>
    $clRows
  </table>
</div>
"@
}

function Build-GamesContent {
    $catColors = @{
        Board='#3fb950'; Card='#f85149'; Strategy='#d29922'; Puzzle='#58a6ff'
        Dice='#bc8cff'; Other='#8b949e'; Magic='#d2a8ff'; Utility='#484f58'
    }
    $cards = ($GameCatalog | ForEach-Object {
        $color = $catColors[$_.Cat]
        $playersHtml = if ($_.Players -ne '-') { "<span class='game-players'>$($_.Players) player$(if($_.Players -ne '1'){'s'})</span>" } else { "<span class='game-players'>framework</span>" }
        @"
  <div class="game-card" data-category="$($_.Cat.ToLower())" data-name="$($_.Name.ToLower())" data-id="$($_.Id)" onclick="location.href='/games/$($_.Id)'" onmouseenter="previewPlay('$($_.Id)')" onmouseleave="previewStop()">
    <div class="game-thumb">
      <img src="/assets/games/thumbs/$($_.Id).png" alt="$($_.Name)"
           onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
      <div class="game-icon-fallback">$($_.Icon)</div>
    </div>
    <div class="game-info">
      <div class="game-title-row">
        <h3 class="game-name">$($_.Name)</h3>
        <span class="game-badge" style="background:${color}22;color:$color;border-color:${color}44">$($_.Cat)</span>
      </div>
      <p class="game-desc">$($_.Desc)</p>
      $playersHtml
    </div>
  </div>
"@
    }) -join "`n"

    $cats = @('All','Board','Card','Strategy','Puzzle','Dice','Other','Magic','Utility')
    $pills = ($cats | ForEach-Object {
        $active = if ($_ -eq 'All') { ' active' } else { '' }
        "<button class='filter-pill$active' data-cat='$($_.ToLower())' onclick='filterGames(""$($_.ToLower())"")'>$_</button>"
    }) -join "`n    "

    @"
<div class="games-toolbar">
  <div class="filter-bar">
    $pills
  </div>
  <div class="search-bar">
    <input type="text" id="game-search" placeholder="Search games..." oninput="filterGames(document.querySelector('.filter-pill.active').dataset.cat)">
    <span id="game-count" class="game-count">Showing $($GameCatalog.Count) of $($GameCatalog.Count) games</span>
    <button class="sound-toggle" id="sound-toggle" onclick="toggleSound()">&#x1F507; Sound Off</button>
  </div>
</div>
<div class="game-grid">
$cards
</div>
"@
}

function Build-MainPage {
    $template = [System.IO.File]::ReadAllText((Join-Path $WebDir 'index.html'))
    $cssModified = if (Test-Path (Join-Path $WebDir 'style.css')) { (Get-Item (Join-Path $WebDir 'style.css')).LastWriteTime.Ticks } else { 0 }
    $template = $template.Replace('{{VERSION}}', $cssModified.ToString())
    $template = $template.Replace('{{STATUS}}', (Build-StatusContent))
    $template = $template.Replace('{{GAMES}}', (Build-GamesContent))
    $template
}

# ── Static file serving ───────────────────────────────────────────────

$MimeTypes = @{
    '.png'  = 'image/png';    '.jpg' = 'image/jpeg'; '.jpeg' = 'image/jpeg'
    '.webp' = 'image/webp';   '.gif' = 'image/gif';  '.svg'  = 'image/svg+xml'
    '.wav'  = 'audio/wav';    '.mp3' = 'audio/mpeg';  '.ogg' = 'audio/ogg'
    '.css'  = 'text/css';     '.js'  = 'application/javascript'
    '.json' = 'application/json'; '.html' = 'text/html'
}

function Send-StaticFile {
    param([System.Net.HttpListenerResponse]$Response, [string]$FilePath)
    if (-not (Test-Path -PathType Leaf $FilePath)) {
        $Response.StatusCode = 404
        return
    }
    $ext = [System.IO.Path]::GetExtension($FilePath).ToLower()
    $Response.ContentType = if ($MimeTypes.ContainsKey($ext)) { $MimeTypes[$ext] } else { 'application/octet-stream' }
    $Response.Headers.Add('Cache-Control', 'no-cache')
    $bytes = [System.IO.File]::ReadAllBytes($FilePath)
    $Response.ContentLength64 = $bytes.Length
    $Response.OutputStream.Write($bytes, 0, $bytes.Length)
}

# ── Game VM (serial bridge) ────────────────────────────────────────────

. (Join-Path $Repo 'build\vm-config.ps1')

$script:GameVm = $null
$script:GameStream = $null
$script:GameBuf = New-Object byte[] 65536

function Start-GameVm {
    $cdx = Join-Path $Repo 'build\output\GameServer.cdx'
    if (-not (Test-Path -PathType Leaf $cdx)) {
        Write-Host "GameServer.cdx not found — compiling..." -ForegroundColor Yellow
        $src = Join-Path $Repo 'apps\games\GameServer.codex'
        $log = Join-Path $Repo 'build\output\game-server.log'
        New-Item -ItemType Directory -Force (Split-Path $cdx) | Out-Null
        & pwsh -NoProfile -File (Join-Path $Repo 'build\test-compile.ps1') -Src $src -Out $cdx -Log $log 2>&1 | Out-Null
        if (-not (Test-Path -PathType Leaf $cdx)) {
            Write-Host "  Compilation failed. Game API unavailable." -ForegroundColor Red
            return
        }
        Write-Host "  Compiled: $((Get-Item $cdx).Length) bytes" -ForegroundColor Green
    }
    Write-Host "Booting game server VM..." -ForegroundColor Yellow
    $run = Start-QemuRun -Kernel $cdx -ConnectTimeoutSec 30 -MemMB 2048
    if (-not $run) {
        Write-Host "  Game VM failed to start." -ForegroundColor Red
        return
    }
    $script:GameVm = $run
    $script:GameStream = $run.Conn.Data.GetStream()
    $script:GameStream.ReadTimeout = 15000
    $ready = ''
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        try {
            $n = $script:GameStream.Read($script:GameBuf, 0, $script:GameBuf.Length)
            if ($n -gt 0) {
                $ready += [System.Text.Encoding]::UTF8.GetString($script:GameBuf, 0, $n)
                if ($ready -match 'READY') { break }
            }
        } catch { break }
    }
    if ($ready -match 'READY') {
        Write-Host "  Game server ready." -ForegroundColor Green
    } else {
        Write-Host "  Game server did not signal READY." -ForegroundColor Red
        Close-Qemu -Conn $run.Conn -Process $run.Process
        $script:GameVm = $null; $script:GameStream = $null
    }
}

function Send-GameRequest {
    param([string]$RequestLine)
    if (-not $script:GameStream) { return $null }
    try {
        $reqBytes = [System.Text.Encoding]::UTF8.GetBytes("$RequestLine`n")
        $script:GameStream.Write($reqBytes, 0, $reqBytes.Length)
        $script:GameStream.Flush()
        $acc = ''
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            try {
                $n = $script:GameStream.Read($script:GameBuf, 0, $script:GameBuf.Length)
                if ($n -gt 0) {
                    $acc += [System.Text.Encoding]::UTF8.GetString($script:GameBuf, 0, $n)
                    if ($acc.Contains("`n")) { return ($acc -split "`n")[0].TrimEnd("`r") }
                }
            } catch { break }
        }
        if ($acc.Length -gt 0) { return $acc.TrimEnd("`r", "`n") }
        return $null
    } catch { return $null }
}

Start-GameVm

# ── HTTP listener ─────────────────────────────────────────────────────

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Codex web server running at http://localhost:$Port/" -ForegroundColor Green
Write-Host "Serving from $WebDir" -ForegroundColor Gray
Write-Host "Press Ctrl+C to stop." -ForegroundColor Gray

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $resp = $ctx.Response
        try {
            $path = $ctx.Request.Url.AbsolutePath

            if ($path -eq '/' -or $path -eq '/index') {
                $html = Build-MainPage
                $buf = [System.Text.Encoding]::UTF8.GetBytes($html)
                $resp.ContentType = 'text/html; charset=utf-8'
                $resp.ContentLength64 = $buf.Length
                $resp.StatusCode = 200
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
            elseif ($path -like '/games/*') {
                $gameId = $path.Substring(7) -replace '[^a-z0-9]',''
                $gamePage = Join-Path $WebDir "$gameId.html"
                if (-not (Test-Path -PathType Leaf $gamePage)) { $gamePage = Join-Path $WebDir 'rungame.html' }
                if (Test-Path -PathType Leaf $gamePage) {
                    $gameHtml = [System.IO.File]::ReadAllText($gamePage)
                    $cssModified = if (Test-Path (Join-Path $WebDir 'style.css')) { (Get-Item (Join-Path $WebDir 'style.css')).LastWriteTime.Ticks } else { 0 }
                    $gameHtml = $gameHtml.Replace('{{VERSION}}', $cssModified.ToString())
                    $buf = [System.Text.Encoding]::UTF8.GetBytes($gameHtml)
                    $resp.ContentType = 'text/html; charset=utf-8'
                    $resp.ContentLength64 = $buf.Length
                    $resp.StatusCode = 200
                    $resp.OutputStream.Write($buf, 0, $buf.Length)
                } else {
                    $resp.StatusCode = 404
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('<html><body style="background:#0d1117;color:#8b949e;font-family:monospace;padding:48px;text-align:center"><h1>404</h1><p>Game not found</p><a href="/#games" style="color:#58a6ff">Back to Games</a></body></html>')
                    $resp.ContentType = 'text/html; charset=utf-8'
                    $resp.ContentLength64 = $buf.Length
                    $resp.OutputStream.Write($buf, 0, $buf.Length)
                }
            }
            elseif ($path -like '/api/*') {
                if (-not $script:GameStream) {
                    Write-Host "  Game VM down — restarting..." -ForegroundColor Yellow
                    if ($script:GameVm) { Close-Qemu -Conn $script:GameVm.Conn -Process $script:GameVm.Process 2>$null }
                    Start-GameVm
                }
                if (-not $script:GameStream) {
                    $resp.StatusCode = 503
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"game server not running"}')
                    $resp.ContentType = 'application/json; charset=utf-8'
                    $resp.ContentLength64 = $buf.Length
                    $resp.OutputStream.Write($buf, 0, $buf.Length)
                } else {
                    $reqLine = "$($ctx.Request.HttpMethod) $($ctx.Request.Url.PathAndQuery)"
                    $respLine = Send-GameRequest -RequestLine $reqLine
                    if ($respLine) {
                        $firstSpace = $respLine.IndexOf(' ')
                        $secondSpace = if ($firstSpace -ge 0) { $respLine.IndexOf(' ', $firstSpace + 1) } else { -1 }
                        if ($firstSpace -gt 0 -and $secondSpace -gt 0) {
                            $statusCode = [int]$respLine.Substring(0, $firstSpace)
                            $contentType = $respLine.Substring($firstSpace + 1, $secondSpace - $firstSpace - 1)
                            $body = $respLine.Substring($secondSpace + 1)
                            $buf = [System.Text.Encoding]::UTF8.GetBytes($body)
                            $resp.StatusCode = $statusCode
                            $resp.ContentType = "$contentType; charset=utf-8"
                            $resp.Headers.Add('Access-Control-Allow-Origin', '*')
                            $resp.ContentLength64 = $buf.Length
                            $resp.OutputStream.Write($buf, 0, $buf.Length)
                        } else {
                            $resp.StatusCode = 502
                            $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"malformed response from game server"}')
                            $resp.ContentType = 'application/json; charset=utf-8'
                            $resp.ContentLength64 = $buf.Length
                            $resp.OutputStream.Write($buf, 0, $buf.Length)
                        }
                    } else {
                        Write-Host "  Game VM timeout — restarting..." -ForegroundColor Yellow
                        if ($script:GameVm) { Close-Qemu -Conn $script:GameVm.Conn -Process $script:GameVm.Process 2>$null }
                        $script:GameVm = $null; $script:GameStream = $null
                        Start-GameVm
                        $resp.StatusCode = 504
                        $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"game server restarting, try again"}')
                        $resp.ContentType = 'application/json; charset=utf-8'
                        $resp.ContentLength64 = $buf.Length
                        $resp.OutputStream.Write($buf, 0, $buf.Length)
                    }
                }
            }
            elseif ($path -like '/web/*') {
                $relFile = $path.Substring(5) -replace '/', '\'
                Send-StaticFile -Response $resp -FilePath (Join-Path $WebDir $relFile)
            }
            elseif ($path -like '/assets/*') {
                $relFile = $path.Substring(1) -replace '/', '\'
                Send-StaticFile -Response $resp -FilePath (Join-Path $Repo $relFile)
            }
            else {
                $resp.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes('<html><body style="background:#0d1117;color:#8b949e;font-family:monospace;padding:48px;text-align:center"><h1>404</h1><p>Not found</p><a href="/" style="color:#58a6ff">Back to Codex</a></body></html>')
                $resp.ContentType = 'text/html; charset=utf-8'
                $resp.ContentLength64 = $buf.Length
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
        } catch {
            $resp.StatusCode = 500
        } finally {
            $resp.Close()
        }
    }
} finally {
    $listener.Stop()
    $listener.Close()
    if ($script:GameVm) {
        Write-Host "Shutting down game VM..." -ForegroundColor Gray
        Close-Qemu -Conn $script:GameVm.Conn -Process $script:GameVm.Process
    }
}
