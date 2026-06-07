# server.ps1 — CodexMagic web server
# Boots the codexmagic CDX in a VM, serves web pages, bridges API calls.
[CmdletBinding()]
param([int]$Port = 8180, [int]$AuthPort = 8889)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$WebDir = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $WebDir '..\..\..\..')).Path
$CdxPath = Join-Path $Repo 'build-output\codexmagic.cdx'
. (Join-Path $Repo 'build\vm-config.ps1')

$script:AuthAccounts = @{}; $script:AuthSessions = @{}; $script:AuthNextId = 1
$script:StoreItems = @{}; $script:StoreNextId = 1
$script:MintLog = [System.Collections.Generic.List[hashtable]]::new()
$script:TotalMinted = 0; $script:TotalBurned = 0; $script:CoinForSale = 0; $script:CoinPrice = 1; $script:Clans = @{}
$script:Listings = @{}; $script:ListingNextId = 1
$script:TradeHistory = [System.Collections.Generic.List[hashtable]]::new()
$script:TotalVolume = 0; $script:NextTokenId = 1000
$script:CurrentSeason = @{ name='Age of Embers'; year=2026; quarter=2; banned=@(); restricted=@(); active=$true }

# ── Card Pool (from CardPool.codex) ──────────────────────────────
function New-Card { param($id,$name,$type,$rarity,$w,$u,$b,$r,$g,$gen,$pow,$tou,$def,$kw,$basic)
    @{id=$id;name=$name;type=$type;rarity=$rarity;cost=@{white=$w;blue=$u;black=$b;red=$r;green=$g;generic=$gen};color=@{white=$w-gt0;blue=$u-gt0;black=$b-gt0;red=$r-gt0;green=$g-gt0};power=$pow;toughness=$tou;defense=$def;keywords=$kw;isBasic=[bool]$basic}
}
$script:CardPool = @(
    # Lands 0-4
    (New-Card 0 'Mountain'    'Land' 'Common' 0 0 0 1 0 0 0 0 0 '' $true)
    (New-Card 1 'Plains'      'Land' 'Common' 1 0 0 0 0 0 0 0 0 '' $true)
    (New-Card 2 'Island'      'Land' 'Common' 0 1 0 0 0 0 0 0 0 '' $true)
    (New-Card 3 'Swamp'       'Land' 'Common' 0 0 1 0 0 0 0 0 0 '' $true)
    (New-Card 4 'Forest'      'Land' 'Common' 0 0 0 0 1 0 0 0 0 '' $true)
    # Red Creatures 5-9
    (New-Card 5 'Goblin Raider'    'Creature' 'Common'   0 0 0 1 0 0 2 1 0 'Haste' $false)
    (New-Card 6 'Ember Knight'     'Creature' 'Uncommon' 0 0 0 1 0 2 3 2 0 'First Strike' $false)
    (New-Card 7 'Lava Wurm'        'Creature' 'Rare'     0 0 0 2 0 3 5 4 1 'Trample' $false)
    (New-Card 8 'Fire Imp'         'Creature' 'Common'   0 0 0 1 0 1 2 2 0 'Flying' $false)
    (New-Card 9 'Magma Giant'      'Creature' 'Mythic'   0 0 0 2 0 4 6 5 2 '' $false)
    # White Creatures 10-14
    (New-Card 10 'Ironclad Sentinel' 'Creature' 'Common'   1 0 0 0 0 1 1 4 2 '' $false)
    (New-Card 11 'Dawn Soldier'      'Creature' 'Common'   1 0 0 0 0 0 2 1 0 '' $false)
    (New-Card 12 'Guardian Angel'    'Creature' 'Rare'     2 0 0 0 0 2 3 4 1 'Flying, Vigilance' $false)
    (New-Card 13 'Wall of Light'     'Creature' 'Common'   1 0 0 0 0 2 0 6 3 'Defender' $false)
    (New-Card 14 'Holy Champion'     'Creature' 'Rare'     2 0 0 0 0 3 4 5 1 'Lifelink' $false)
    # Blue Creatures 15-19
    (New-Card 15 'Azure Scholar'   'Creature' 'Common'   0 1 0 0 0 1 1 3 0 '' $false)
    (New-Card 16 'Wind Drake'      'Creature' 'Common'   0 1 0 0 0 2 2 2 0 'Flying' $false)
    (New-Card 17 'Sea Serpent'     'Creature' 'Common'   0 2 0 0 0 4 5 5 0 '' $false)
    (New-Card 18 'Frost Sentinel'  'Creature' 'Common'   0 1 0 0 0 3 2 5 2 '' $false)
    (New-Card 19 'Illusion Weaver' 'Creature' 'Rare'     0 2 0 0 0 1 3 1 0 'Hexproof' $false)
    # Black Creatures 20-24
    (New-Card 20 'Shadow Assassin' 'Creature' 'Common'   0 0 1 0 0 1 2 1 0 'Deathtouch' $false)
    (New-Card 21 'Vampire Knight'  'Creature' 'Common'   0 0 1 0 0 2 3 2 0 'Lifelink' $false)
    (New-Card 22 'Dread Specter'   'Creature' 'Common'   0 0 2 0 0 2 3 3 0 'Flying' $false)
    (New-Card 23 'Bone Colossus'   'Creature' 'Mythic'   0 0 2 0 0 4 6 6 1 '' $false)
    (New-Card 24 'Plague Rat'      'Creature' 'Common'   0 0 1 0 0 0 1 1 0 'Deathtouch' $false)
    # Green Creatures 25-29
    (New-Card 25 'Verdant Wurm'  'Creature' 'Rare'    0 0 0 0 2 3 5 5 1 'Trample' $false)
    (New-Card 26 'Forest Bear'   'Creature' 'Common'  0 0 0 0 1 1 2 2 0 '' $false)
    (New-Card 27 'Giant Spider'  'Creature' 'Common'  0 0 0 0 1 2 2 4 0 'Reach' $false)
    (New-Card 28 'Ancient Oak'   'Creature' 'Rare'    0 0 0 0 2 4 4 7 3 '' $false)
    (New-Card 29 'Elven Archer'  'Creature' 'Common'  0 0 0 0 1 1 2 1 0 'Reach' $false)
    # Colorless Creatures 30-31
    (New-Card 30 'Wall of Stone' 'Creature' 'Common'   0 0 0 0 0 3 0 5 4 'Defender' $false)
    (New-Card 31 'Iron Golem'    'Creature' 'Uncommon' 0 0 0 0 0 5 4 4 2 '' $false)
    # Red Spells 32-34
    (New-Card 32 'Lightning Strike' 'Instant'  'Common'   0 0 0 1 0 1 0 0 0 'Deal 3 damage' $false)
    (New-Card 33 'Fireball'         'Sorcery'  'Uncommon' 0 0 0 1 0 3 0 0 0 'Deal 5 damage' $false)
    (New-Card 34 'Flame Wave'       'Sorcery'  'Uncommon' 0 0 0 2 0 3 0 0 0 'Deal 4 damage to all' $false)
    # White Spells 35-37
    (New-Card 35 'Healing Touch' 'Instant'     'Common'   1 0 0 0 0 0 0 0 0 'Gain 4 life' $false)
    (New-Card 36 'Divine Shield'  'Enchantment' 'Uncommon' 1 0 0 0 0 1 0 0 0 'Gain 2 life' $false)
    (New-Card 37 'Holy Wrath'    'Sorcery'     'Uncommon' 2 0 0 0 0 2 0 0 0 'Destroy target' $false)
    # Blue Spells 38-40
    (New-Card 38 'Divination'  'Sorcery' 'Uncommon' 0 1 0 0 0 2 0 0 0 'Draw 2 cards' $false)
    (New-Card 39 'Counterspell' 'Instant' 'Mythic'  0 2 0 0 0 0 0 0 0 'Counter target spell' $false)
    (New-Card 40 'Brainstorm'  'Instant'  'Common'  0 1 0 0 0 0 0 0 0 'Draw 1 card' $false)
    # Black Spells 41-43
    (New-Card 41 'Dark Ritual' 'Instant' 'Common'   0 0 1 0 0 0 0 0 0 'Add 3 black mana' $false)
    (New-Card 42 'Doom Blade'  'Instant' 'Uncommon' 0 0 1 0 0 1 0 0 0 'Destroy target' $false)
    (New-Card 43 'Drain Life'  'Sorcery' 'Uncommon' 0 0 1 0 0 2 0 0 0 'Deal 3, gain 3 life' $false)
    # Green Spells 44-46
    (New-Card 44 'Giant Growth' 'Instant' 'Common'   0 0 0 0 1 0 0 0 0 '+3/+3/+0' $false)
    (New-Card 45 'Naturalize'  'Instant'  'Uncommon' 0 0 0 0 1 1 0 0 0 'Destroy artifact/enchant' $false)
    (New-Card 46 'Regrowth'    'Sorcery'  'Uncommon' 0 0 0 0 1 1 0 0 0 'Return from graveyard' $false)
    # Artifacts 47-49
    (New-Card 47 'Mana Crystal' 'Artifact' 'Uncommon' 0 0 0 0 0 2 0 0 0 'Tap: add 1 mana' $false)
    (New-Card 48 'Battle Axe'   'Artifact' 'Uncommon' 0 0 0 0 0 3 0 0 0 'Equip: +2/+0/+0' $false)
    (New-Card 49 'Shield Ward'  'Artifact' 'Uncommon' 0 0 0 0 0 2 0 0 0 'Equip: +0/+0/+2' $false)
    # Triggered Creatures 50-59
    (New-Card 50 'War Drummer'       'Creature' 'Uncommon' 0 0 0 1 0 1 2 2 0 'On attack: +1/+0/+0' $false)
    (New-Card 51 'Healer Cleric'     'Creature' 'Uncommon' 1 0 0 0 0 1 1 3 1 'ETB: gain 3 life' $false)
    (New-Card 52 'Thought Wisp'      'Creature' 'Uncommon' 0 1 0 0 0 2 1 1 0 'Flying, ETB: draw 1' $false)
    (New-Card 53 'Blood Seeker'      'Creature' 'Uncommon' 0 0 1 0 0 1 2 2 0 'Combat dmg: gain 2 life' $false)
    (New-Card 54 'Vine Caller'       'Creature' 'Uncommon' 0 0 0 0 1 2 2 3 0 'On cast: +0/+1/+1' $false)
    (New-Card 55 'Armored Rhino'     'Creature' 'Rare'     0 0 0 0 2 2 4 5 3 'Trample' $false)
    (New-Card 56 'Phoenix Hatchling' 'Creature' 'Rare'     0 0 0 1 0 3 3 2 0 'Flying, Haste, Death: deal 2' $false)
    (New-Card 57 'Shield Bearer'     'Creature' 'Rare'     1 0 0 0 0 3 2 6 4 'Vigilance' $false)
    (New-Card 58 'Mind Flayer'       'Creature' 'Mythic'   0 2 0 0 0 3 3 4 0 'Combat dmg: draw 2' $false)
    (New-Card 59 'Grave Titan'       'Creature' 'Mythic'   0 0 2 0 0 4 6 6 2 'Deathtouch, On attack: deal 2' $false)
)
$script:CardIndex = @{}; foreach ($c in $script:CardPool) { $script:CardIndex[$c.id] = $c }
$script:CurrentSeason.poolSize = $script:CardPool.Count

# ── Pack Cracking (spec-compliant) ───────────────────────────────
$script:PoolByRarity = @{ Common=@(); Uncommon=@(); Rare=@(); Mythic=@() }
foreach ($c in $script:CardPool) { if ($c.type -ne 'Land') { $script:PoolByRarity[$c.rarity] += @($c) } }

function Pick-Random { param($arr, $rng); $arr[$rng.Next($arr.Count)] }

function Crack-PackCards {
    param([string]$PackType, [string]$Sub)
    $rng = [System.Random]::new()
    $cards = [System.Collections.Generic.List[hashtable]]::new()
    $packCount = if ($PackType -eq 'draft') { 3 } else { 1 }
    for ($p = 0; $p -lt $packCount; $p++) {
        for ($i = 0; $i -lt 10; $i++) { $c = Pick-Random $script:PoolByRarity.Common $rng; $cards.Add(@{cardId=$c.id;rarity='Common';mintSource='Pack'}) }
        for ($i = 0; $i -lt 3; $i++) { $c = Pick-Random $script:PoolByRarity.Uncommon $rng; $cards.Add(@{cardId=$c.id;rarity='Uncommon';mintSource='Pack'}) }
        $mythicRoll = $rng.Next(8)
        if ($mythicRoll -eq 0 -and $script:PoolByRarity.Mythic.Count -gt 0) { $c = Pick-Random $script:PoolByRarity.Mythic $rng; $cards.Add(@{cardId=$c.id;rarity='Mythic';mintSource='Pack'}) }
        else { $c = Pick-Random $script:PoolByRarity.Rare $rng; $cards.Add(@{cardId=$c.id;rarity='Rare';mintSource='Pack'}) }
        $wcRoll = $rng.Next(100)
        if ($PackType -eq 'premium') { $wcPool = if ($wcRoll -lt 70) { 'Rare' } else { 'Mythic' } }
        else { $wcPool = if ($wcRoll -lt 60) { 'Uncommon' } elseif ($wcRoll -lt 85) { 'Rare' } elseif ($wcRoll -lt 95) { 'Common' } else { 'Mythic' } }
        $c = Pick-Random $script:PoolByRarity[$wcPool] $rng; $cards.Add(@{cardId=$c.id;rarity=$wcPool;mintSource='Pack'})
    }
    $bonus = switch ($Sub) { 'Bronze'{1} 'Silver'{2} 'Gold'{3} 'Platinum'{5} default{0} }
    $upgrades = @{Common='Uncommon';Uncommon='Rare';Rare='Mythic'}
    for ($i = 0; $i -lt $bonus -and $i -lt $cards.Count; $i++) {
        $idx = $rng.Next($cards.Count); $cur = $cards[$idx].rarity
        if ($upgrades.ContainsKey($cur) -and $script:PoolByRarity[$upgrades[$cur]].Count -gt 0) {
            $newRar = $upgrades[$cur]; $nc = Pick-Random $script:PoolByRarity[$newRar] $rng
            $cards[$idx] = @{cardId=$nc.id;rarity=$newRar;mintSource='Pack'}
        }
    }
    foreach ($c in $cards) { $c.tokenId = $script:NextTokenId++ }
    return ,$cards
}

# ── Disk persistence ─────────────────────────────────────────────
$script:DataDir = Join-Path $WebDir 'data'
$script:DbFile  = Join-Path $script:DataDir 'state.json'
New-Item -ItemType Directory -Force $script:DataDir | Out-Null

function Save-State {
    $state = @{
        AuthAccounts = @{}; AuthNextId = $script:AuthNextId
        StoreItems = @{}; StoreNextId = $script:StoreNextId
        MintLog = @($script:MintLog); TotalMinted = $script:TotalMinted; TotalBurned = $script:TotalBurned; CoinForSale = $script:CoinForSale; CoinPrice = $script:CoinPrice
        Clans = @{}
        Listings = @{}; ListingNextId = $script:ListingNextId; NextTokenId = $script:NextTokenId
        TradeHistory = @($script:TradeHistory); TotalVolume = $script:TotalVolume
    }
    foreach ($k in $script:AuthAccounts.Keys) {
        $a = $script:AuthAccounts[$k]
        $owned = if ($a.OwnedCards) { @($a.OwnedCards) } else { @() }
        $state.AuthAccounts[$k] = @{ Id=$a.Id; Handle=$a.Handle; Display=$a.Display; Password=$a.Password; PwScore=$a.PwScore; Tfa=$a.Tfa; Admin=$a.Admin; Banned=$a.Banned; Balance=$a.Balance; OwnedCards=$owned; Wins=$a.Wins; Losses=$a.Losses; Rating=$a.Rating; Subscription=$a.Subscription }
    }
    foreach ($k in $script:StoreItems.Keys) { $state.StoreItems["$k"] = $script:StoreItems[$k] }
    foreach ($k in $script:Clans.Keys) { $state.Clans["$k"] = $script:Clans[$k] }
    foreach ($k in $script:Listings.Keys) {
        $l = $script:Listings[$k].Clone()
        if ($l.createdAt -is [datetime]) { $l.createdAt = $l.createdAt.ToString('o') }
        if ($l.endsAt -is [datetime]) { $l.endsAt = $l.endsAt.ToString('o') }
        $state.Listings["$k"] = $l
    }
    $json = $state | ConvertTo-Json -Depth 5 -Compress
    [System.IO.File]::WriteAllText($script:DbFile, $json, [System.Text.UTF8Encoding]::new($false))
}

function Load-State {
    if (-not (Test-Path $script:DbFile)) { return }
    try {
        $json = [System.IO.File]::ReadAllText($script:DbFile)
        $state = $json | ConvertFrom-Json
        $script:AuthNextId = $state.AuthNextId
        $script:StoreNextId = $state.StoreNextId
        $script:ListingNextId = $state.ListingNextId
        $script:TotalMinted = $state.TotalMinted
        $script:TotalBurned = $state.TotalBurned
        $script:TotalVolume = $state.TotalVolume
        try { $script:CoinForSale = [int]$state.CoinForSale } catch { $script:CoinForSale = 0 }
        try { $script:CoinPrice = [int]$state.CoinPrice; if ($script:CoinPrice -lt 1) { $script:CoinPrice = 1 } } catch { $script:CoinPrice = 1 }
        try { $script:NextTokenId = [int]$state.NextTokenId } catch { $script:NextTokenId = 1000 }
        foreach ($prop in $state.AuthAccounts.PSObject.Properties) {
            $v = $prop.Value
            $oc = [System.Collections.ArrayList]::new()
            try { if ($v.PSObject.Properties['OwnedCards'] -and $v.OwnedCards) {
                foreach ($c in $v.OwnedCards) {
                    if ($c -is [int] -or $c -is [long] -or ($c.PSObject.Properties -and -not $c.PSObject.Properties['cardId'])) {
                        [void]$oc.Add(@{cardId=[int]$c;rarity='Common';mintSource='Legacy';tokenId=-1})
                    } else {
                        [void]$oc.Add(@{cardId=[int]$c.cardId;rarity=$(if($c.rarity){$c.rarity}else{'Common'});mintSource=$(if($c.mintSource){$c.mintSource}else{'Legacy'});tokenId=$(try{[int]$c.tokenId}catch{-1})})
                    }
                }
            }} catch {}
            $script:AuthAccounts[$prop.Name] = @{
                Id = [int]$v.Id; Handle = $v.Handle; Display = $v.Display; Password = $v.Password; PwScore = [int]$v.PwScore; Tfa = [bool]$v.Tfa
                Admin = $(try { [bool]$v.Admin } catch { $false }); Banned = $(try { [bool]$v.Banned } catch { $false })
                Balance = $(try { [int]$v.Balance } catch { 0 }); OwnedCards = $oc
                Wins = $(try { [int]$v.Wins } catch { 0 }); Losses = $(try { [int]$v.Losses } catch { 0 })
                Rating = $(try { if ($v.Rating) { [int]$v.Rating } else { 1000 } } catch { 1000 })
                Subscription = $(try { if ($v.Subscription) { $v.Subscription } else { 'Free' } } catch { 'Free' })
            }
        }
        foreach ($prop in $state.StoreItems.PSObject.Properties) {
            $v = $prop.Value
            $script:StoreItems[[int]$prop.Name] = @{ id=[int]$v.id; name=$v.name; type=$v.type; price=[int]$v.price; qty=[int]$v.qty; desc=$v.desc; discount=[int]$v.discount; saleLabel=$v.saleLabel; sold=[int]$v.sold }
        }
        foreach ($prop in $state.Clans.PSObject.Properties) {
            $v = $prop.Value
            $script:Clans[[int]$prop.Name] = @{ id=[int]$v.id; name=$v.name; tag=$v.tag; founder=$v.founder; members=[int]$v.members; rank=$v.rank; treasury=[int]$v.treasury }
        }
        foreach ($prop in $state.Listings.PSObject.Properties) {
            $v = $prop.Value
            $endsAt = if ($v.endsAt) { try { [datetime]::Parse($v.endsAt) } catch { $null } } else { $null }
            $createdAt = if ($v.createdAt) { try { [datetime]::Parse($v.createdAt) } catch { Get-Date } } else { Get-Date }
            $script:Listings[[int]$prop.Name] = @{ id=[int]$v.id; type=$v.type; tokenId=$v.tokenId; seller=$v.seller; cardName=$v.cardName; cardType=$v.cardType; rarity=$v.rarity; status=$v.status; createdAt=$createdAt; price=[int]$v.price; minBid=[int]$v.minBid; currentBid=[int]$v.currentBid; buyout=[int]$v.buyout; bidCount=[int]$v.bidCount; highBidder=$v.highBidder; endsAt=$endsAt }
        }
        foreach ($e in $state.MintLog) { $script:MintLog.Add(@{ type=$e.type; handle=$e.handle; amount=[int]$e.amount; reason=$e.reason; time=$e.time }) }
        foreach ($e in $state.TradeHistory) { $script:TradeHistory.Add(@{ type=$e.type; cardName=$e.cardName; price=[int]$e.price; buyer=$e.buyer; seller=$e.seller; time=$e.time }) }
        Write-Host "  Loaded $($script:AuthAccounts.Count) accounts, $($script:StoreItems.Count) store items, $($script:Listings.Count) listings from disk" -ForegroundColor Green
    } catch {
        Write-Host "  Failed to load state: $_ — starting fresh" -ForegroundColor Yellow
    }
}

Load-State

function Get-AuthUser { param([string]$Token); if (-not $Token -or -not $script:AuthSessions.ContainsKey($Token)) { return $null }; $h = $script:AuthSessions[$Token]; if (-not $script:AuthAccounts.ContainsKey($h)) { return $null }; return $script:AuthAccounts[$h] }
function Is-Admin { param([string]$Token); $u = Get-AuthUser $Token; if (-not $u) { return $false }; return ($u.Admin -eq $true) }
function Now-Stamp { return (Get-Date).ToString('HH:mm:ss') }
function Score-Password { param([string]$P); if ($P.Length -lt 5) { return 0 }; if ($P -notmatch '^[a-zA-Z0-9]+$') { return 0 }; $s = 1; if ($P.Length -ge 8) { $s++ }; if ($P -cmatch '[a-z]' -and $P -cmatch '[A-Z]') { $s++ }; if ($P -match '[0-9]') { $s++ }; return $s }
function Send-Json { param($Response, [string]$Json, [int]$Status = 200); $buf = [System.Text.Encoding]::UTF8.GetBytes($Json); $Response.StatusCode = $Status; $Response.ContentType = 'application/json; charset=utf-8'; $Response.Headers.Add('Access-Control-Allow-Origin', '*'); $Response.ContentLength64 = $buf.Length; $Response.OutputStream.Write($buf, 0, $buf.Length) }
function Send-AuthJson { param($Response, [string]$Json, [int]$Status = 200); Send-Json $Response $Json $Status }

function Handle-Auth {
    param($Context, $Response)
    $path = $Context.Request.Url.AbsolutePath
    $qs = [System.Web.HttpUtility]::ParseQueryString($Context.Request.Url.Query)
    if ($path -eq '/api/auth/check-handle') {
        $u = $qs['u']
        if (-not $u -or $u.Length -lt 2) { Send-Json $Response '{"available":false,"reason":"handle must be at least 2 characters"}'; return }
        if ($u -notmatch '^[a-zA-Z][a-zA-Z0-9-]*$') { Send-Json $Response '{"available":false,"reason":"letters, numbers, and hyphens only"}'; return }
        if ($u.Length -gt 24) { Send-Json $Response '{"available":false,"reason":"24 character max"}'; return }
        if ($script:AuthAccounts.ContainsKey($u.ToLower())) { Send-Json $Response '{"available":false,"reason":"handle already taken"}' } else { Send-Json $Response '{"available":true}' }
    }
    elseif ($path -eq '/api/auth/check-password') {
        $p = $qs['p']; $score = Score-Password $p; $labels = @('rejected','thin','fair','good','strong')
        $msg = switch ($score) { 0 { if ($p.Length -lt 5) { 'at least 5 characters required' } else { 'letters and numbers only' } }; 1 { 'some features require a stronger password' }; 2 { 'add uppercase and numbers' }; 3 { 'good' }; 4 { 'full access' } }
        Send-Json $Response "{`"score`":$score,`"label`":`"$($labels[$score])`",`"message`":`"$msg`"}"
    }
    elseif ($path -eq '/api/auth/register') {
        $u = $qs['u']; $d = $qs['d']; $p = $qs['p']
        if (-not $u -or -not $p) { Send-Json $Response '{"error":"handle and password required"}' 400; return }
        $ul = $u.ToLower()
        if ($ul.Length -lt 2 -or $ul.Length -gt 24 -or $ul -notmatch '^[a-z][a-z0-9-]*$') { Send-Json $Response '{"error":"invalid handle"}' 400; return }
        $pwScore = Score-Password $p
        if ($pwScore -eq 0) { Send-Json $Response '{"error":"password too weak"}' 400; return }
        if ($script:AuthAccounts.ContainsKey($ul)) { Send-Json $Response '{"error":"handle already taken"}' 400; return }
        $id = $script:AuthNextId++; $display = if ($d) { $d } else { $u }; $isFirst = $script:AuthAccounts.Count -eq 0
        $script:AuthAccounts[$ul] = @{ Id=$id; Handle=$ul; Display=$display; Password=$p; PwScore=$pwScore; Tfa=$false; Admin=$isFirst; Banned=$false; Balance=0; OwnedCards=[System.Collections.ArrayList]::new(); Wins=0; Losses=0; Rating=1000; Subscription='Free' }
        $tok = [guid]::NewGuid().ToString('N'); $script:AuthSessions[$tok] = $ul; $labels = @('rejected','thin','fair','good','strong')
        Send-Json $Response "{`"token`":`"$tok`",`"handle`":`"$ul`",`"display`":`"$display`",`"id`":$id,`"pw-score`":$pwScore,`"pw-label`":`"$($labels[$pwScore])`",`"tfa`":false,`"admin`":$($isFirst.ToString().ToLower())}"
    }
    elseif ($path -eq '/api/auth/login') {
        $u = $qs['u']; $p = $qs['p']
        if (-not $u -or -not $p) { Send-Json $Response '{"error":"handle and password required"}' 400; return }
        $ul = $u.ToLower()
        if (-not $script:AuthAccounts.ContainsKey($ul)) { Send-Json $Response '{"error":"unknown handle"}' 400; return }
        $acct = $script:AuthAccounts[$ul]
        if ($acct.Banned) { Send-Json $Response '{"error":"account banned"}' 403; return }
        if ($acct.Password -ne $p) { Send-Json $Response '{"error":"wrong password"}' 400; return }
        $tok = [guid]::NewGuid().ToString('N'); $script:AuthSessions[$tok] = $ul; $labels = @('rejected','thin','fair','good','strong')
        Send-Json $Response "{`"token`":`"$tok`",`"handle`":`"$ul`",`"display`":`"$($acct.Display)`",`"id`":$($acct.Id),`"pw-score`":$($acct.PwScore),`"pw-label`":`"$($labels[$acct.PwScore])`",`"tfa`":$($acct.Tfa.ToString().ToLower()),`"admin`":$($acct.Admin.ToString().ToLower())}"
    }
    elseif ($path -eq '/api/auth/me') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $acct = $script:AuthAccounts[$u]; $labels = @('rejected','thin','fair','good','strong')
        Send-Json $Response "{`"handle`":`"$u`",`"display`":`"$($acct.Display)`",`"id`":$($acct.Id),`"pw-score`":$($acct.PwScore),`"pw-label`":`"$($labels[$acct.PwScore])`",`"tfa`":$($acct.Tfa.ToString().ToLower()),`"admin`":$($acct.Admin.ToString().ToLower())}"
    }
    elseif ($path -eq '/api/auth/logout') { $t = $qs['t']; if ($t) { $script:AuthSessions.Remove($t) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/auth/profile') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]; $labels = @('rejected','thin','fair','good','strong')
        $ownedCount = if ($a.OwnedCards) { $a.OwnedCards.Count } else { 0 }
        Send-Json $Response "{`"handle`":`"$u`",`"display`":`"$($a.Display)`",`"id`":$($a.Id),`"admin`":$($a.Admin.ToString().ToLower()),`"pw-score`":$($a.PwScore),`"pw-label`":`"$($labels[$a.PwScore])`",`"balance`":$($a.Balance),`"tokens`":$ownedCount,`"wins`":$($a.Wins),`"losses`":$($a.Losses),`"rating`":$($a.Rating),`"subscription`":`"$($a.Subscription)`",`"rank`":`"$(if ($a.Rating -ge 1800) {'Diamond'} elseif ($a.Rating -ge 1500) {'Platinum'} elseif ($a.Rating -ge 1200) {'Gold'} elseif ($a.Rating -ge 900) {'Silver'} else {'Bronze'})`"}"
    }
    elseif ($path -eq '/api/auth/pool') {
        $json = ($script:CardPool | ForEach-Object { $c = $_; $cj = $c.cost | ConvertTo-Json -Compress; $clj = $c.color | ConvertTo-Json -Compress; "{`"id`":$($c.id),`"name`":`"$($c.name)`",`"type`":`"$($c.type)`",`"rarity`":`"$($c.rarity)`",`"cost`":$cj,`"color`":$clj,`"power`":$($c.power),`"toughness`":$($c.toughness),`"defense`":$($c.defense),`"keywords`":`"$($c.keywords)`",`"isBasic`":$($c.isBasic.ToString().ToLower())}" }) -join ','
        Send-Json $Response "{`"cards`":[$json]}"
    }
    elseif ($path -eq '/api/auth/season') {
        $s = $script:CurrentSeason
        Send-Json $Response "{`"name`":`"$($s.name)`",`"year`":$($s.year),`"quarter`":$($s.quarter),`"pool-size`":$($s.poolSize),`"bans`":$($s.banned.Count),`"active`":true}"
    }
    elseif ($path -eq '/api/auth/collection') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"cards":[],"total":0}'; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $owned = if ($a.OwnedCards) { @($a.OwnedCards) } else { @() }
        $grouped = @{}
        foreach ($c in $owned) {
            $cid = if ($c -is [hashtable]) { $c.cardId } else { [int]$c }
            $rar = if ($c -is [hashtable] -and $c.rarity) { $c.rarity } else { 'Common' }
            $key = "$cid`:$rar"
            if (-not $grouped.ContainsKey($key)) { $grouped[$key] = @{cardId=$cid;rarity=$rar;count=0} }
            $grouped[$key].count++
        }
        $items = $grouped.Values | ForEach-Object {
            $t2 = $script:CardIndex[$_.cardId]
            $name = if ($t2) { $t2.name } else { "Card #$($_.cardId)" }
            $type = if ($t2) { $t2.type } else { '?' }
            $kw = if ($t2) { $t2.keywords } else { '' }
            "{`"cardId`":$($_.cardId),`"name`":`"$name`",`"type`":`"$type`",`"rarity`":`"$($_.rarity)`",`"keywords`":`"$kw`",`"count`":$($_.count)}"
        }
        Send-Json $Response "{`"cards`":[$($items -join ',')],`"total`":$($owned.Count)}"
    }
    elseif ($path -eq '/api/auth/crack-pack') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $packType = $qs['type']; $cost = switch ($packType) { 'premium'{300} 'draft'{250} default{100} }
        if ($a.Balance -lt $cost) { Send-Json $Response "{`"error`":`"Not enough MC. Need $cost, have $($a.Balance).`"}" 400; return }
        $a.Balance -= $cost
        if (-not $a.OwnedCards) { $a.OwnedCards = [System.Collections.ArrayList]::new() }
        $pulled = Crack-PackCards -PackType $packType -Sub $a.Subscription
        foreach ($c in $pulled) { [void]$a.OwnedCards.Add($c) }
        $cardsJson = ($pulled | ForEach-Object { $t2 = $script:CardIndex[$_.cardId]; $name = if($t2){$t2.name}else{"Card #$($_.cardId)"}; "{`"cardId`":$($_.cardId),`"name`":`"$name`",`"rarity`":`"$($_.rarity)`",`"tokenId`":$($_.tokenId)}" }) -join ','
        Send-Json $Response "{`"ok`":true,`"balance`":$($a.Balance),`"cards`":[$cardsJson],`"count`":$($pulled.Count)}"
    }
    elseif ($path -eq '/api/auth/buy-store-item') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $itemId = [int]$qs['id']
        if (-not $script:StoreItems.ContainsKey($itemId)) { Send-Json $Response '{"error":"item not found"}' 400; return }
        $item = $script:StoreItems[$itemId]
        if ($item.qty -eq 0) { Send-Json $Response '{"error":"out of stock"}' 400; return }
        $effectivePrice = if ($item.discount -gt 0) { [Math]::Floor($item.price * (1 - $item.discount / 100)) } else { $item.price }
        if ($a.Balance -lt $effectivePrice) { Send-Json $Response "{`"error`":`"Not enough MC. Need $effectivePrice, have $($a.Balance).`"}" 400; return }
        $a.Balance -= $effectivePrice
        if ($item.qty -gt 0) { $item.qty-- }
        $item.sold++
        if (-not $a.OwnedCards) { $a.OwnedCards = [System.Collections.ArrayList]::new() }
        $pulled = @()
        if ($item.type -in @('pack','bundle','starter')) {
            $packCount = switch ($item.type) { 'bundle'{3} 'starter'{1} default{1} }
            for ($pi = 0; $pi -lt $packCount; $pi++) {
                $cards = Crack-PackCards -PackType 'standard' -Sub $a.Subscription
                foreach ($c in $cards) { [void]$a.OwnedCards.Add($c); $pulled += $c }
            }
        }
        $cardsJson = ($pulled | ForEach-Object { $t2 = $script:CardIndex[$_.cardId]; $nm = if($t2){$t2.name}else{"?"}; "{`"cardId`":$($_.cardId),`"name`":`"$nm`",`"rarity`":`"$($_.rarity)`",`"tokenId`":$($_.tokenId)}" }) -join ','
        Send-Json $Response "{`"ok`":true,`"balance`":$($a.Balance),`"cards`":[$cardsJson],`"count`":$($pulled.Count),`"itemName`":`"$($item.name)`"}"
    }
    elseif ($path -eq '/api/auth/buy-coin') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $amt = [int]$qs['amount']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        if ($amt -gt $script:CoinForSale) { Send-Json $Response "{`"error`":`"Only $($script:CoinForSale) MC available for purchase.`"}" 400; return }
        $script:CoinForSale -= $amt; $a.Balance += $amt
        $script:MintLog.Add(@{type='purchase';handle=$u;amount=$amt;reason="bought at store";time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"balance`":$($a.Balance),`"forSale`":$($script:CoinForSale)}"
    }
    elseif ($path -eq '/api/auth/coin-info') {
        Send-Json $Response "{`"forSale`":$($script:CoinForSale),`"price`":$($script:CoinPrice)}"
    }
    elseif ($path -eq '/api/auth/change-password') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $old = $qs['old']; $new = $qs['new']
        if (-not $old -or -not $new) { Send-Json $Response '{"error":"old and new password required"}' 400; return }
        if ($a.Password -ne $old) { Send-Json $Response '{"error":"current password is wrong"}' 400; return }
        $newScore = Score-Password $new
        if ($newScore -eq 0) { Send-Json $Response '{"error":"new password too weak"}' 400; return }
        $a.Password = $new; $a.PwScore = $newScore
        $labels = @('rejected','thin','fair','good','strong')
        Send-Json $Response "{`"ok`":true,`"pw-score`":$newScore,`"pw-label`":`"$($labels[$newScore])`"}"
    }
    elseif ($path -eq '/api/auth/subscribe') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $tier = $qs['tier']; if ($tier -notin @('Free','Bronze','Silver','Gold','Platinum')) { $tier = 'Free' }
        $a.Subscription = $tier
        Send-Json $Response "{`"ok`":true,`"subscription`":`"$tier`"}"
    }
    else { Send-Json $Response '{"error":"unknown auth endpoint"}' 404 }
}

function Handle-Admin {
    param($Context, $Response)
    $path = $Context.Request.Url.AbsolutePath; $qs = [System.Web.HttpUtility]::ParseQueryString($Context.Request.Url.Query); $tok = $qs['t']
    if ($path -eq '/api/admin/check') { Send-Json $Response "{`"admin`":$((Is-Admin $tok).ToString().ToLower())}"; return }
    if (-not (Is-Admin $tok)) { Send-Json $Response '{"error":"admin required"}' 403; return }
    if ($path -eq '/api/admin/stats') {
        $circulating = 0; foreach ($a in $script:AuthAccounts.Values) { $circulating += $a.Balance }
        $totalSupply = $script:TotalMinted - $script:TotalBurned
        $unallocated = $totalSupply - $circulating - $script:CoinForSale
        Send-Json $Response "{`"supply`":$totalSupply,`"minted`":$($script:TotalMinted),`"burned`":$($script:TotalBurned),`"circulating`":$circulating,`"forSale`":$($script:CoinForSale),`"coinPrice`":$($script:CoinPrice),`"unallocated`":$unallocated,`"accounts`":$($script:AuthAccounts.Count)}"
    }
    elseif ($path -eq '/api/admin/mint') {
        $amt = [int]$qs['amount']; $reason = $qs['reason']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        $script:TotalMinted += $amt
        $script:MintLog.Add(@{type='mint';handle='SUPPLY';amount=$amt;reason=$reason;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"supply`":$($script:TotalMinted - $script:TotalBurned),`"minted`":$($script:TotalMinted)}"
    }
    elseif ($path -eq '/api/admin/grant') {
        $hl = ($qs['handle']).ToLower(); $amt = [int]$qs['amount']; $reason = $qs['reason']
        if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown handle"}' 400; return }
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        $circulating = 0; foreach ($a in $script:AuthAccounts.Values) { $circulating += $a.Balance }; $circulating += $script:CoinForSale
        $unallocated = $script:TotalMinted - $script:TotalBurned - $circulating
        if ($amt -gt $unallocated) { Send-Json $Response "{`"error`":`"Only $unallocated MC unallocated in supply. Mint more first.`"}" 400; return }
        $script:AuthAccounts[$hl].Balance += $amt
        $script:MintLog.Add(@{type='grant';handle=$hl;amount=$amt;reason=$reason;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"balance`":$($script:AuthAccounts[$hl].Balance)}"
    }
    elseif ($path -eq '/api/admin/stock-coins') {
        $amt = [int]$qs['amount']; $price = [int]$qs['price']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        $circulating = 0; foreach ($a in $script:AuthAccounts.Values) { $circulating += $a.Balance }; $circulating += $script:CoinForSale
        $unallocated = $script:TotalMinted - $script:TotalBurned - $circulating
        if ($amt -gt $unallocated) { Send-Json $Response "{`"error`":`"Only $unallocated MC unallocated. Mint more first.`"}" 400; return }
        $script:CoinForSale += $amt
        if ($price -ge 1) { $script:CoinPrice = $price }
        $script:MintLog.Add(@{type='stock';handle='STORE';amount=$amt;reason="$($script:CoinForSale) MC now for sale";time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"forSale`":$($script:CoinForSale),`"price`":$($script:CoinPrice)}"
    }
    elseif ($path -eq '/api/admin/burn') {
        $amt = [int]$qs['amount']; $reason = $qs['reason']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        $circulating = 0; foreach ($a in $script:AuthAccounts.Values) { $circulating += $a.Balance }; $circulating += $script:CoinForSale
        $unallocated = $script:TotalMinted - $script:TotalBurned - $circulating
        if ($amt -gt $unallocated) { Send-Json $Response "{`"error`":`"Can only burn unallocated supply ($unallocated MC).`"}" 400; return }
        $script:TotalBurned += $amt
        $script:MintLog.Add(@{type='burn';handle='SUPPLY';amount=$amt;reason=$reason;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"supply`":$($script:TotalMinted - $script:TotalBurned),`"burned`":$($script:TotalBurned)}"
    }
    elseif ($path -eq '/api/admin/mint-log') { $e = $script:MintLog | Select-Object -Last 50; $j = '[' + (($e | ForEach-Object { "{`"type`":`"$($_.type)`",`"handle`":`"$($_.handle)`",`"amount`":$($_.amount),`"reason`":`"$($_.reason)`",`"time`":`"$($_.time)`"}" }) -join ',') + ']'; Send-Json $Response "{`"entries`":$j}" }
    elseif ($path -eq '/api/admin/users') { $u = $script:AuthAccounts.Values | ForEach-Object { "{`"id`":$($_.Id),`"handle`":`"$($_.Handle)`",`"display`":`"$($_.Display)`",`"admin`":$($_.Admin.ToString().ToLower()),`"banned`":$($_.Banned.ToString().ToLower()),`"pwScore`":$($_.PwScore),`"balance`":$($_.Balance)}" }; Send-Json $Response "{`"users`":[$($u -join ',')]}" }
    elseif ($path -eq '/api/admin/set-role') { $hl = ($qs['handle']).ToLower(); if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown"}' 400; return }; $script:AuthAccounts[$hl].Admin = ($qs['admin'] -eq '1'); Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/ban') { $hl = ($qs['handle']).ToLower(); if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown"}' 400; return }; $script:AuthAccounts[$hl].Banned = ($qs['banned'] -eq '1'); Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/add-item') { $name = $qs['name']; if (-not $name) { Send-Json $Response '{"error":"name required"}' 400; return }; $id = $script:StoreNextId++; $script:StoreItems[$id] = @{id=$id;name=$name;type=$qs['type'];price=[int]$qs['price'];qty=[int]$qs['qty'];desc=$qs['desc'];discount=0;saleLabel='';sold=0}; Send-Json $Response "{`"ok`":true,`"id`":$id}" }
    elseif ($path -eq '/api/admin/store-items') { $it = $script:StoreItems.Values | Sort-Object {$_.id} | ForEach-Object { "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"type`":`"$($_.type)`",`"price`":$($_.price),`"qty`":$($_.qty),`"discount`":$($_.discount),`"saleLabel`":`"$($_.saleLabel)`",`"sold`":$($_.sold)}" }; Send-Json $Response "{`"items`":[$($it -join ',')]}" }
    elseif ($path -eq '/api/admin/set-discount') { $id = [int]$qs['id']; if (-not $script:StoreItems.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }; $script:StoreItems[$id].discount = [int]$qs['pct']; $script:StoreItems[$id].saleLabel = if ($qs['label']) { $qs['label'] } else { '' }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/remove-item') { $id = [int]$qs['id']; if ($script:StoreItems.ContainsKey($id)) { $script:StoreItems.Remove($id) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/clans') { $c = $script:Clans.Values | ForEach-Object { "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"tag`":`"$($_.tag)`",`"founder`":`"$($_.founder)`",`"members`":$($_.members),`"rank`":`"$($_.rank)`",`"treasury`":$($_.treasury)}" }; Send-Json $Response "{`"clans`":[$($c -join ',')]}" }
    elseif ($path -eq '/api/admin/disband-clan') { $id = [int]$qs['id']; if ($script:Clans.ContainsKey($id)) { $script:Clans.Remove($id) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/blockchain') { $supply = 0; foreach ($a in $script:AuthAccounts.Values) { $supply += $a.Balance }; $txC = $script:MintLog.Count + $script:TradeHistory.Count; $bc = [Math]::Max(1,[Math]::Floor($txC/3)+1); $hash = '{0:x16}' -f ([Math]::Abs($bc.GetHashCode())*7919); $rt = @(); foreach ($e in ($script:MintLog | Select-Object -Last 5)) { $rt += "{`"time`":`"$($e.time)`",`"type`":`"$($e.type)`",`"detail`":`"$($e.amount) MC $($e.handle)`"}" }; foreach ($e in ($script:TradeHistory | Select-Object -Last 5)) { $rt += "{`"time`":`"$($e.time)`",`"type`":`"trade`",`"detail`":`"$($e.cardName) $($e.price) MC`"}" }; Send-Json $Response "{`"blocks`":$bc,`"transactions`":$txC,`"tokens`":$supply,`"supply`":$supply,`"height`":$bc,`"lastHash`":`"0x$hash`",`"avgBlockTime`":`"12`",`"pendingTx`":0,`"nodeStatus`":`"Online`",`"recentTx`":[$($rt -join ',')]}" }
    else { Send-Json $Response '{"error":"unknown admin endpoint"}' 404 }
}

function Handle-Market {
    param($Context, $Response)
    $path = $Context.Request.Url.AbsolutePath; $qs = [System.Web.HttpUtility]::ParseQueryString($Context.Request.Url.Query); $tok = $qs['t']; $user = Get-AuthUser $tok
    if ($path -eq '/api/market/balance') { Send-Json $Response "{`"balance`":$(if ($user) { $user.Balance } else { 0 })}"; return }
    elseif ($path -eq '/api/market/listings') {
        $active = @($script:Listings.Values | Where-Object { $_.status -eq 'active' })
        if ($qs['type']) { $active = @($active | Where-Object { $_.type -eq $qs['type'] }) }
        if ($qs['rarity']) { $active = @($active | Where-Object { $_.rarity -eq $qs['rarity'] }) }
        if ($qs['search']) { $active = @($active | Where-Object { $_.cardName -like "*$($qs['search'])*" }) }
        $sort = $qs['sort']; if ($sort -eq 'price-low') { $active = @($active | Sort-Object { if ($_.type -eq 'auction') { $_.currentBid } else { $_.price } }) } elseif ($sort -eq 'price-high') { $active = @($active | Sort-Object { if ($_.type -eq 'auction') { $_.currentBid } else { $_.price } } -Descending) } else { $active = @($active | Sort-Object { $_.id } -Descending) }
        $all = @($script:Listings.Values | Where-Object { $_.status -eq 'active' })
        $items = $active | ForEach-Object { $tl = if ($_.endsAt) { $r = ($_.endsAt - (Get-Date)); if ($r.TotalSeconds -gt 0) { '{0}h {1}m' -f [int]$r.TotalHours, $r.Minutes } else { 'ended' } } else { '--' }; "{`"id`":$($_.id),`"type`":`"$($_.type)`",`"cardName`":`"$($_.cardName)`",`"cardType`":`"$($_.cardType)`",`"rarity`":`"$($_.rarity)`",`"price`":$($_.price),`"minBid`":$($_.minBid),`"currentBid`":$($_.currentBid),`"buyout`":$($_.buyout),`"bidCount`":$($_.bidCount),`"seller`":`"$($_.seller)`",`"timeLeft`":`"$tl`"}" }
        Send-Json $Response "{`"listings`":[$($items -join ',')],`"totalActive`":$($all.Count),`"totalSales`":$(@($all|Where-Object{$_.type -eq 'sale'}).Count),`"totalAuctions`":$(@($all|Where-Object{$_.type -eq 'auction'}).Count),`"totalVolume`":$($script:TotalVolume)}"
    }
    elseif ($path -eq '/api/market/create') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        $type = $qs['type']; $tid = [int]$qs['tokenId']
        $cardEntry = $null; $cardIdx = -1
        for ($i = 0; $i -lt $user.OwnedCards.Count; $i++) {
            $c = $user.OwnedCards[$i]
            $ctid = if ($c -is [hashtable]) { $c.tokenId } else { $i }
            if ($ctid -eq $tid) { $cardEntry = $c; $cardIdx = $i; break }
        }
        if (-not $cardEntry) { Send-Json $Response '{"error":"card not found in collection"}' 400; return }
        $cid = if ($cardEntry -is [hashtable]) { $cardEntry.cardId } else { [int]$cardEntry }
        $rar = if ($cardEntry -is [hashtable] -and $cardEntry.rarity) { $cardEntry.rarity } else { 'Common' }
        $tmpl = $script:CardIndex[$cid]
        $cname = if ($tmpl) { $tmpl.name } else { "Card #$cid" }
        $ctype = if ($tmpl) { $tmpl.type } else { '?' }
        $id = $script:ListingNextId++
        $l = @{id=$id;type=$type;tokenId=$tid;cardId=$cid;seller=$user.Handle;cardName=$cname;cardType=$ctype;rarity=$rar;status='active';createdAt=Get-Date;price=0;minBid=0;currentBid=0;buyout=0;bidCount=0;highBidder='';endsAt=$null;escrowCard=$cardEntry}
        if ($type -eq 'sale') { $l.price = [int]$qs['price']; if ($l.price -lt 1) { Send-Json $Response '{"error":"price must be positive"}' 400; return } }
        else { $l.minBid = [int]$qs['minBid']; $l.buyout = [int]$qs['buyout']; $dur = [int]$qs['duration']; if ($dur -lt 1) { $dur = 24 }; $l.currentBid = $l.minBid; $l.endsAt = (Get-Date).AddHours($dur) }
        $user.OwnedCards.RemoveAt($cardIdx)
        $script:Listings[$id] = $l; Send-Json $Response "{`"ok`":true,`"id`":$id}"
    }
    elseif ($path -eq '/api/market/buy') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        $id = [int]$qs['id']; if (-not $script:Listings.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }
        $l = $script:Listings[$id]; if ($l.status -ne 'active' -or $l.type -ne 'sale') { Send-Json $Response '{"error":"not available"}' 400; return }
        if ($l.seller -eq $user.Handle) { Send-Json $Response '{"error":"cannot buy your own"}' 400; return }
        if ($user.Balance -lt $l.price) { Send-Json $Response '{"error":"insufficient balance"}' 400; return }
        $user.Balance -= $l.price; if ($script:AuthAccounts.ContainsKey($l.seller)) { $script:AuthAccounts[$l.seller].Balance += $l.price }
        $buyerCard = @{cardId=$l.cardId;rarity=$l.rarity;mintSource='Trade';tokenId=$script:NextTokenId++}
        if (-not $user.OwnedCards) { $user.OwnedCards = [System.Collections.ArrayList]::new() }
        [void]$user.OwnedCards.Add($buyerCard)
        $l.status = 'sold'; $script:TotalVolume += $l.price; $script:TradeHistory.Add(@{type='bought';cardName=$l.cardName;price=$l.price;buyer=$user.Handle;seller=$l.seller;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"balance`":$($user.Balance)}"
    }
    elseif ($path -eq '/api/market/bid') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        $id = [int]$qs['id']; $amt = [int]$qs['amount']; if (-not $script:Listings.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }
        $l = $script:Listings[$id]; if ($l.status -ne 'active' -or $l.type -ne 'auction') { Send-Json $Response '{"error":"not an active auction"}' 400; return }
        if ($l.seller -eq $user.Handle) { Send-Json $Response '{"error":"cannot bid on your own"}' 400; return }
        $minN = if ($l.bidCount -gt 0) { $l.currentBid + 1 } else { $l.minBid }
        if ($amt -lt $minN -or $user.Balance -lt $amt) { Send-Json $Response '{"error":"invalid bid"}' 400; return }
        if ($l.highBidder -and $script:AuthAccounts.ContainsKey($l.highBidder)) { $script:AuthAccounts[$l.highBidder].Balance += $l.currentBid }
        $user.Balance -= $amt; $l.currentBid = $amt; $l.highBidder = $user.Handle; $l.bidCount++
        Send-Json $Response "{`"ok`":true,`"balance`":$($user.Balance)}"
    }
    elseif ($path -eq '/api/market/buyout') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        $id = [int]$qs['id']; if (-not $script:Listings.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }
        $l = $script:Listings[$id]; if ($l.status -ne 'active' -or $l.type -ne 'auction' -or $l.buyout -le 0) { Send-Json $Response '{"error":"invalid"}' 400; return }
        if ($l.seller -eq $user.Handle -or $user.Balance -lt $l.buyout) { Send-Json $Response '{"error":"cannot buyout"}' 400; return }
        if ($l.highBidder -and $script:AuthAccounts.ContainsKey($l.highBidder)) { $script:AuthAccounts[$l.highBidder].Balance += $l.currentBid }
        $user.Balance -= $l.buyout; if ($script:AuthAccounts.ContainsKey($l.seller)) { $script:AuthAccounts[$l.seller].Balance += $l.buyout }
        $buyerCard = @{cardId=$l.cardId;rarity=$l.rarity;mintSource='Trade';tokenId=$script:NextTokenId++}
        if (-not $user.OwnedCards) { $user.OwnedCards = [System.Collections.ArrayList]::new() }
        [void]$user.OwnedCards.Add($buyerCard)
        $l.status = 'sold'; $script:TotalVolume += $l.buyout; $script:TradeHistory.Add(@{type='bought';cardName=$l.cardName;price=$l.buyout;buyer=$user.Handle;seller=$l.seller;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"balance`":$($user.Balance)}"
    }
    elseif ($path -eq '/api/market/cancel') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        $id = [int]$qs['id']; if (-not $script:Listings.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }
        $l = $script:Listings[$id]; if ($l.seller -ne $user.Handle) { Send-Json $Response '{"error":"not your listing"}' 403; return }
        if ($l.highBidder -and $script:AuthAccounts.ContainsKey($l.highBidder)) { $script:AuthAccounts[$l.highBidder].Balance += $l.currentBid }
        if ($l.escrowCard) { if (-not $user.OwnedCards) { $user.OwnedCards = [System.Collections.ArrayList]::new() }; [void]$user.OwnedCards.Add($l.escrowCard) }
        $l.status = 'cancelled'; Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/market/my-listings') {
        if (-not $user) { Send-Json $Response '{"listings":[]}'; return }
        $mine = @($script:Listings.Values | Where-Object { $_.seller -eq $user.Handle -and $_.status -eq 'active' })
        $items = $mine | ForEach-Object { $tl = if ($_.endsAt) { $r = ($_.endsAt-(Get-Date)); if ($r.TotalSeconds -gt 0) { '{0}h {1}m' -f [int]$r.TotalHours,$r.Minutes } else { 'ended' } } else { '--' }; "{`"id`":$($_.id),`"type`":`"$($_.type)`",`"cardName`":`"$($_.cardName)`",`"price`":$($_.price),`"currentBid`":$($_.currentBid),`"bidCount`":$($_.bidCount),`"timeLeft`":`"$tl`"}" }
        Send-Json $Response "{`"listings`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/market/my-cards') {
        if (-not $user) { Send-Json $Response '{"cards":[]}'; return }
        $cards = @()
        for ($i = 0; $i -lt $user.OwnedCards.Count; $i++) {
            $c = $user.OwnedCards[$i]; $cid = if ($c -is [hashtable]) { $c.cardId } else { [int]$c }
            $rar = if ($c -is [hashtable] -and $c.rarity) { $c.rarity } else { 'Common' }
            $tid = if ($c -is [hashtable]) { $c.tokenId } else { $i }
            $tmpl = $script:CardIndex[$cid]; $nm = if ($tmpl) { $tmpl.name } else { "Card #$cid" }; $tp = if ($tmpl) { $tmpl.type } else { '?' }
            $cards += "{`"tokenId`":$tid,`"cardId`":$cid,`"rarity`":`"$rar`",`"name`":`"$nm`",`"type`":`"$tp`"}"
        }
        Send-Json $Response "{`"cards`":[$($cards -join ',')]}"
    }
    elseif ($path -eq '/api/market/history') {
        if (-not $user) { Send-Json $Response '{"transactions":[]}'; return }
        $mine = @($script:TradeHistory | Where-Object { $_.buyer -eq $user.Handle -or $_.seller -eq $user.Handle }) | Select-Object -Last 20
        $items = $mine | ForEach-Object { $t = if ($_.seller -eq $user.Handle) { 'sold' } else { 'bought' }; "{`"type`":`"$t`",`"cardName`":`"$($_.cardName)`",`"price`":$($_.price),`"buyer`":`"$($_.buyer)`",`"seller`":`"$($_.seller)`",`"time`":`"$($_.time)`"}" }
        Send-Json $Response "{`"transactions`":[$($items -join ',')]}"
    }
    else { Send-Json $Response '{"error":"unknown market endpoint"}' 404 }
}

$script:GameVm = $null; $script:TcpListener = $null; $script:TcpClient = $null; $script:TcpStream = $null; $TcpBridgePort = 9200
function Start-TcpBridge { $script:TcpListener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,$TcpBridgePort); $script:TcpListener.Server.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket,[System.Net.Sockets.SocketOptionName]::ReuseAddress,$true); $script:TcpListener.Start(); Write-Host "  TCP bridge on $TcpBridgePort" -ForegroundColor Cyan }
function Wait-GameTcpConnection { param([int]$TimeoutSec=30); $dl=(Get-Date).AddSeconds($TimeoutSec); while((Get-Date) -lt $dl){ if($script:TcpListener.Pending()){ $script:TcpClient=$script:TcpListener.AcceptTcpClient(); $script:TcpClient.NoDelay=$true; $script:TcpClient.ReceiveTimeout=15000; $script:TcpStream=$script:TcpClient.GetStream(); return $true }; Start-Sleep -Milliseconds 100 }; return $false }
function Start-GameVm { if(-not(Test-Path -PathType Leaf $CdxPath)){Write-Host "CDX not found: $CdxPath" -ForegroundColor Red;return}; Write-Host "Booting VM..." -ForegroundColor Cyan; $ef=[System.IO.Path]::GetTempFileName(); $script:GameVm=Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel',$CdxPath,'-mem','2048','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $ef; if($script:GameVm.HasExited){Write-Host "  VM exited." -ForegroundColor Red;return}; Write-Host "  VM PID: $($script:GameVm.Id)" -ForegroundColor Cyan; if(-not(Wait-GameTcpConnection -TimeoutSec 30)){Write-Host "  No TCP connect." -ForegroundColor Red; try{Stop-Process -Id $script:GameVm.Id -Force}catch{}; $script:GameVm=$null;return}; Write-Host "  VM ready." -ForegroundColor Green }
function Send-FramedMsg { param([byte[]]$Body); if(-not $script:TcpStream){return}; $h=[BitConverter]::GetBytes([int](1+$Body.Length)); $script:TcpStream.Write($h,0,4); $script:TcpStream.WriteByte([byte]1); if($Body.Length -gt 0){$script:TcpStream.Write($Body,0,$Body.Length)}; $script:TcpStream.Flush() }
function Recv-FramedMsg { param([int]$TimeoutMs=10000); if(-not $script:TcpStream){return $null}; $old=$script:TcpStream.ReadTimeout; $script:TcpStream.ReadTimeout=$TimeoutMs; try{ $hdr=New-Object byte[] 4; $rd=0; while($rd -lt 4){$n=$script:TcpStream.Read($hdr,$rd,4-$rd); if($n -le 0){return $null}; $rd+=$n}; $ml=[BitConverter]::ToInt32($hdr,0); if($ml -lt 1 -or $ml -gt 1048576){return $null}; $pl=New-Object byte[] $ml; $rd=0; while($rd -lt $ml){$n=$script:TcpStream.Read($pl,$rd,$ml-$rd); if($n -le 0){return $null}; $rd+=$n}; $b=if($ml -gt 1){$pl[1..($ml-1)]}else{@()}; return [System.Text.Encoding]::UTF8.GetString($b) }catch{return $null}finally{$script:TcpStream.ReadTimeout=$old} }
function Send-GameRequest { param([string]$RequestLine); if(-not $script:TcpStream){return $null}; try{Send-FramedMsg -Body ([System.Text.Encoding]::UTF8.GetBytes($RequestLine)); return Recv-FramedMsg -TimeoutMs 10000}catch{$script:TcpStream=$null;return $null} }

$MimeTypes = @{'.html'='text/html';'.css'='text/css';'.js'='application/javascript';'.json'='application/json';'.png'='image/png';'.jpg'='image/jpeg';'.svg'='image/svg+xml';'.wav'='audio/wav';'.mp3'='audio/mpeg'}
function Send-StaticFile { param($Response,[string]$FilePath); if(Test-Path -PathType Leaf $FilePath){$ext=[System.IO.Path]::GetExtension($FilePath).ToLower();$mime=if($MimeTypes.ContainsKey($ext)){$MimeTypes[$ext]}else{'application/octet-stream'};$buf=[System.IO.File]::ReadAllBytes($FilePath);$Response.ContentType="$mime; charset=utf-8";$Response.ContentLength64=$buf.Length;$Response.StatusCode=200;$Response.OutputStream.Write($buf,0,$buf.Length)}else{$Response.StatusCode=404;$buf=[System.Text.Encoding]::UTF8.GetBytes('Not found');$Response.ContentType='text/plain';$Response.ContentLength64=$buf.Length;$Response.OutputStream.Write($buf,0,$buf.Length)} }

$listener = [System.Net.HttpListener]::new(); $listener.Prefixes.Add("http://localhost:$Port/"); $listener.Start()
Write-Host "`n  CodexMagic at http://localhost:$Port/ | Admin: /admin.html | Market: /marketplace.html`n" -ForegroundColor Green
function Cleanup-Resources { if($script:TcpStream){try{$script:TcpStream.Close()}catch{};$script:TcpStream=$null}; if($script:TcpClient){try{$script:TcpClient.Close()}catch{};$script:TcpClient=$null}; if($script:TcpListener){try{$script:TcpListener.Stop()}catch{};$script:TcpListener=$null}; if($script:GameVm -and -not $script:GameVm.HasExited){try{Stop-Process -Id $script:GameVm.Id -Force}catch{};$script:GameVm=$null}; if($listener){try{$listener.Stop();$listener.Close()}catch{}} }
try { [Console]::CancelKeyPress.Add({ Cleanup-Resources }) } catch {}

try {
    try { Start-TcpBridge; Start-GameVm } catch { Write-Host "  VM unavailable: $_ — pages still work" -ForegroundColor Yellow }
    while ($listener.IsListening) {
        $ctx = $listener.GetContext(); $resp = $ctx.Response; $path = $ctx.Request.Url.AbsolutePath
        try {
            if ($path -eq '/' -or $path -eq '/index') { $resp.Redirect('/game.html'); $resp.StatusCode = 302 }
            elseif ($path -match '^\/([\w.-]+\.(html|css|js|png|jpg|svg|wav|mp3|codex))$') { Send-StaticFile -Response $resp -FilePath (Join-Path $WebDir $matches[1]) }
            elseif ($path -like '/api/auth/*') { Handle-Auth -Context $ctx -Response $resp; if ($path -in @('/api/auth/register','/api/auth/crack-pack','/api/auth/subscribe','/api/auth/buy-store-item','/api/auth/buy-coin','/api/auth/change-password')) { Save-State } }
            elseif ($path -like '/api/admin/*') { Handle-Admin -Context $ctx -Response $resp; if ($path -ne '/api/admin/check' -and $path -ne '/api/admin/stats' -and $path -ne '/api/admin/users' -and $path -ne '/api/admin/store-items' -and $path -ne '/api/admin/clans' -and $path -ne '/api/admin/blockchain' -and $path -ne '/api/admin/mint-log') { Save-State } }
            elseif ($path -like '/api/market/*') { Handle-Market -Context $ctx -Response $resp; if ($path -ne '/api/market/balance' -and $path -ne '/api/market/listings' -and $path -ne '/api/market/my-listings' -and $path -ne '/api/market/my-cards' -and $path -ne '/api/market/history') { Save-State } }
            elseif ($path -like '/api/magic/*') {
                if (-not $script:TcpStream) { if ($script:GameVm -and -not $script:GameVm.HasExited) { try{Stop-Process -Id $script:GameVm.Id -Force}catch{} }; Start-GameVm }
                if (-not $script:TcpStream) { Send-Json $resp '{"error":"game server not running"}' 503 }
                else { $rl = "$($ctx.Request.HttpMethod) $($ctx.Request.Url.PathAndQuery)"; $rr = Send-GameRequest -RequestLine $rl; if($rr){ $pf='200 application/json '; $jb=if($rr.StartsWith($pf)){$rr.Substring($pf.Length)}else{$rr}; $buf=[System.Text.Encoding]::UTF8.GetBytes($jb); $resp.StatusCode=200;$resp.ContentType='application/json; charset=utf-8';$resp.Headers.Add('Access-Control-Allow-Origin','*');$resp.ContentLength64=$buf.Length;$resp.OutputStream.Write($buf,0,$buf.Length) }else{ if($script:GameVm -and -not $script:GameVm.HasExited){try{Stop-Process -Id $script:GameVm.Id -Force}catch{}}; $script:GameVm=$null;$script:TcpStream=$null;Start-GameVm; Send-Json $resp '{"error":"restarting"}' 504 } }
            }
            else { $resp.StatusCode=404;$buf=[System.Text.Encoding]::UTF8.GetBytes('<html><body style="background:#0d1117;color:#8b949e;font-family:monospace;padding:48px;text-align:center"><h1>404</h1><a href="/" style="color:#58a6ff">Back</a></body></html>');$resp.ContentType='text/html; charset=utf-8';$resp.ContentLength64=$buf.Length;$resp.OutputStream.Write($buf,0,$buf.Length) }
        } catch { Write-Host "  ERROR: $_ at $($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red; $resp.StatusCode = 500 } finally { $resp.Close() }
    }
} finally { Save-State; Cleanup-Resources; Write-Host "Server stopped. State saved." -ForegroundColor Yellow }
