# generate-game-audio.ps1 -- Generate UI sounds and ambient music via MusicGen.
# Usage: tools/generate-game-audio.ps1 [-Force]
# Requires MusicGen running at http://localhost:7861
[CmdletBinding()]
param([switch]$Force)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$SfxDir = Join-Path $Repo 'assets\games\sfx'
$MusicDir = Join-Path $Repo 'assets\games\music'
New-Item -ItemType Directory -Force $SfxDir | Out-Null
New-Item -ItemType Directory -Force $MusicDir | Out-Null

$MusicGenUrl = 'http://localhost:7861/gradio_api/call/predict_full'

function Generate-Audio {
    param([string]$Prompt, [double]$Duration, [string]$OutFile, [double]$Temp = 1.0, [double]$Cfg = 3.0)

    if ((Test-Path $OutFile) -and -not $Force) {
        Write-Host "  SKIP $(Split-Path -Leaf $OutFile) (exists)" -ForegroundColor Gray
        return $true
    }

    Write-Host "  Generating $(Split-Path -Leaf $OutFile)..." -ForegroundColor Yellow -NoNewline

    $body = @{
        data = @(
            'facebook/musicgen-medium',
            '',
            'Default',
            $Prompt,
            $null,
            $Duration,
            250,
            0.0,
            $Temp,
            $Cfg
        )
    } | ConvertTo-Json -Depth 10

    try {
        $submit = Invoke-WebRequest -Uri $MusicGenUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30
        $eventId = ($submit.Content | ConvertFrom-Json).event_id

        $pollUrl = "$MusicGenUrl/$eventId"
        $deadline = (Get-Date).AddSeconds(120)
        $audioUrl = $null

        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 2000
            try {
                $poll = Invoke-WebRequest -Uri $pollUrl -UseBasicParsing -TimeoutSec 10
                $text = $poll.Content
                if ($text -match 'event:\s*complete') {
                    $dataLines = $text -split "`n" | Where-Object { $_ -match '^data:\s' }
                    foreach ($dl in $dataLines) {
                        $json = $dl -replace '^data:\s*', ''
                        try {
                            $parsed = $json | ConvertFrom-Json
                            if ($parsed -and $parsed.Count -gt 0 -and $parsed[0].url) {
                                $audioUrl = $parsed[0].url
                                break
                            }
                        } catch {}
                    }
                    break
                }
            } catch {}
        }

        if (-not $audioUrl) {
            Write-Host " FAIL: no audio URL in response" -ForegroundColor Red
            return $false
        }

        $dlUrl = if ($audioUrl.StartsWith('http')) { $audioUrl } else { "http://localhost:7861$audioUrl" }
        $audioResp = Invoke-WebRequest -Uri $dlUrl -UseBasicParsing -TimeoutSec 30
        [System.IO.File]::WriteAllBytes($OutFile, $audioResp.Content)
        $size = [math]::Round((Get-Item $OutFile).Length / 1024, 1)
        Write-Host " OK (${size}KB)" -ForegroundColor Green
        return $true
    } catch {
        Write-Host " FAIL: $_" -ForegroundColor Red
        return $false
    }
}

Write-Host "Codex Game Audio Generator" -ForegroundColor Cyan
Write-Host "SFX: $SfxDir" -ForegroundColor Gray
Write-Host "Music: $MusicDir" -ForegroundColor Gray

try {
    Invoke-WebRequest -Uri 'http://localhost:7861/' -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Host "MusicGen is online." -ForegroundColor Green
} catch {
    Write-Host "MusicGen is not running at localhost:7861." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== UI Sound Effects ===" -ForegroundColor Cyan

$sfx = @(
    @{ Name='card-hover';   Prompt='very short subtle UI hover sound, soft digital shimmer, clean'; Dur=1.0; Temp=0.8; Cfg=4.0 }
    @{ Name='card-click';   Prompt='short satisfying UI button click, crisp digital tap, clean interface'; Dur=1.0; Temp=0.8; Cfg=4.0 }
    @{ Name='tab-switch';   Prompt='soft tab switch click, subtle interface navigation sound, brief'; Dur=1.0; Temp=0.8; Cfg=4.0 }
    @{ Name='game-start';   Prompt='upbeat short game start jingle, chiptune fanfare, 8-bit, energetic'; Dur=2.0; Temp=1.0; Cfg=3.5 }
    @{ Name='game-win';     Prompt='triumphant victory fanfare, short chiptune celebration, 8-bit, joyful'; Dur=3.0; Temp=1.0; Cfg=3.5 }
    @{ Name='game-lose';    Prompt='short sad game over sound, descending chiptune notes, 8-bit, melancholy'; Dur=2.0; Temp=1.0; Cfg=3.5 }
    @{ Name='dice-roll';    Prompt='dice rolling and landing on wooden table, short rattling sound'; Dur=1.5; Temp=0.9; Cfg=3.5 }
    @{ Name='piece-place';  Prompt='short wooden piece placement sound, board game token placed on wood'; Dur=1.0; Temp=0.8; Cfg=4.0 }
    @{ Name='card-deal';    Prompt='playing card being dealt, single card sliding on felt, short crisp'; Dur=1.0; Temp=0.8; Cfg=4.0 }
    @{ Name='notification'; Prompt='gentle pleasant notification chime, short digital ping, clean'; Dur=1.5; Temp=0.8; Cfg=4.0 }
)

foreach ($s in $sfx) {
    Generate-Audio -Prompt $s.Prompt -Duration $s.Dur -OutFile (Join-Path $SfxDir "$($s.Name).wav") -Temp $s.Temp -Cfg $s.Cfg
}

Write-Host ""
Write-Host "=== Background Music ===" -ForegroundColor Cyan

$music = @(
    @{ Name='ambient-menu';  Prompt='lo-fi chiptune ambient background music, retro game menu screen, relaxing calm, 8-bit synthesizer, gentle melody loop'; Dur=30.0; Temp=1.0; Cfg=3.0 }
    @{ Name='ambient-board';  Prompt='calm strategic thinking music, soft piano and ambient pads, contemplative, board game atmosphere, gentle'; Dur=30.0; Temp=1.0; Cfg=3.0 }
    @{ Name='ambient-action'; Prompt='upbeat chiptune action music, retro arcade energy, 8-bit drums and bass, exciting but not overwhelming'; Dur=30.0; Temp=1.0; Cfg=3.0 }
)

foreach ($m in $music) {
    Generate-Audio -Prompt $m.Prompt -Duration $m.Dur -OutFile (Join-Path $MusicDir "$($m.Name).wav") -Temp $m.Temp -Cfg $m.Cfg
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
