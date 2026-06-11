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
$script:TotalMinted = 0; $script:TotalBurned = 0; $script:CoinForSale = 0; $script:CoinPrice = 1; $script:Clans = @{}; $script:ClanNextId = 1
$script:Listings = @{}; $script:ListingNextId = 1
$script:TradeHistory = [System.Collections.Generic.List[hashtable]]::new()
$script:TotalVolume = 0; $script:NextTokenId = 1000
$script:CurrentSeason = @{ name='Age of Embers'; year=2026; quarter=2; banned=@(); restricted=@(); active=$true }

# ── Card Pool (Prismatic System) ─────────────────────────────────
# Colors: R=Red Y=Yellow B=Blue O=Orange G=Green P=Purple W=White(generic/unfiltered)
# Spell speeds: summoning, cantrip, incantation, disruption
# baseFocus: used in Focus vs Disruption contest
function New-Card { param($id,$name,$type,$rarity,$r,$y,$b,$o,$g,$p,$w,$pow,$tou,$def,$kw,$basic,$spellSpeed,$baseFocus,$fluorClause)
    @{id=$id;name=$name;type=$type;rarity=$rarity;cost=@{red=$r;yellow=$y;blue=$b;orange=$o;green=$g;purple=$p;white=$w};color=@{red=$r-gt0;yellow=$y-gt0;blue=$b-gt0;orange=$o-gt0;green=$g-gt0;purple=$p-gt0};power=$pow;toughness=$tou;defense=$def;keywords=$kw;isBasic=[bool]$basic;spellSpeed=$spellSpeed;baseFocus=[int]$baseFocus;fluorClause=$fluorClause}
}
function New-Gem { param($id,$name,$gemColor,$variety,$hardness,$isBasic,$ability)
    @{id=$id;name=$name;type='Gemstone';rarity=$(if($isBasic){'Common'}else{'Rare'});cost=@{red=0;yellow=0;blue=0;orange=0;green=0;purple=0;white=0};color=@{red=$gemColor-eq'Red';yellow=$gemColor-eq'Yellow';blue=$gemColor-eq'Blue';orange=$gemColor-eq'Orange';green=$gemColor-eq'Green';purple=$false};power=0;toughness=0;defense=0;keywords='';isBasic=[bool]$isBasic;gemColor=$gemColor;variety=$variety;hardness=[int]$hardness;gemAbility=$ability;spellSpeed=$null;baseFocus=0;fluorClause=$null}
}
function New-Equipment { param($id,$name,$rarity,$r,$y,$b,$o,$g,$p,$w,$slot,$pow,$tou,$def,$kw,$socketEmpty)
    @{id=$id;name=$name;type='Equipment';rarity=$rarity;cost=@{red=$r;yellow=$y;blue=$b;orange=$o;green=$g;purple=$p;white=$w};color=@{red=$r-gt0;yellow=$y-gt0;blue=$b-gt0;orange=$o-gt0;green=$g-gt0;purple=$p-gt0};power=$pow;toughness=$tou;defense=$def;keywords=$kw;isBasic=$false;slot=$slot;socketEmpty=[bool]$socketEmpty;spellSpeed=$null;baseFocus=0;fluorClause=$null}
}
$script:CardPool = @(
    # ── Gemstones 0-6 (basic, unlimited in deck) ──
    (New-Gem 0  'Ruby'      'Red'    'Ruby'      9  $true  $null)
    (New-Gem 1  'Topaz'     'Yellow' 'Topaz'     8  $true  $null)
    (New-Gem 2  'Sapphire'  'Blue'   'Sapphire'  9  $true  $null)
    (New-Gem 3  'Carnelian' 'Orange' 'Carnelian'  7  $true  $null)
    (New-Gem 4  'Emerald'   'Green'  'Emerald'   7.5 $true  $null)
    (New-Gem 5  'Diamond'   'White'  'Diamond'   10 $true  $null)
    (New-Gem 6  'Obsidian'  'Black'  'Obsidian'  5  $true  $null)
    # ── Red Creatures 7-11 ──
    (New-Card 7  'Ruby Golem'        'Creature' 'Common'   1 0 0 0 0 0 0 2 1 0 'Alacrity' $false 'summoning' 5 $null)
    (New-Card 8  'Flame Sentinel'    'Creature' 'Uncommon' 1 0 0 0 0 0 2 3 2 0 'Initiative' $false 'summoning' 6 $null)
    (New-Card 9  'Molten Wurm'       'Creature' 'Rare'     2 0 0 0 0 0 3 5 4 1 'Splash' $false 'summoning' 7 'Deal 2 damage to all')
    (New-Card 10 'Cinder Sprite'     'Creature' 'Common'   1 0 0 0 0 0 1 2 2 0 'Flying' $false 'summoning' 5 $null)
    (New-Card 11 'Pyroclasm Giant'   'Creature' 'Mythic'   2 0 0 0 0 0 4 6 5 2 '' $false 'summoning' 8 'Deal 3 damage to all')
    # ── Yellow Creatures 12-16 ──
    (New-Card 12 'Topaz Guardian'    'Creature' 'Common'   0 1 0 0 0 0 1 1 4 2 '' $false 'summoning' 5 $null)
    (New-Card 13 'Radiant Scout'     'Creature' 'Common'   0 1 0 0 0 0 0 2 1 0 '' $false 'summoning' 4 $null)
    (New-Card 14 'Aureate Seraph'    'Creature' 'Rare'     0 2 0 0 0 0 2 3 4 1 'Flying, Vigilance' $false 'summoning' 7 'Gain 4 life')
    (New-Card 15 'Topaz Bulwark'     'Creature' 'Common'   0 1 0 0 0 0 2 1 5 3 'Stalwart' $false 'summoning' 5 $null)
    (New-Card 16 'Gilded Champion'   'Creature' 'Rare'     0 2 0 0 0 0 3 4 5 1 'Drain Life' $false 'summoning' 7 'Gain 3 life')
    # ── Blue Creatures 17-21 ──
    (New-Card 17 'Sapphire Scholar'  'Creature' 'Common'   0 0 1 0 0 0 1 1 3 0 '' $false 'summoning' 5 $null)
    (New-Card 18 'Cerulean Drake'    'Creature' 'Common'   0 0 1 0 0 0 2 2 2 0 'Flying' $false 'summoning' 5 $null)
    (New-Card 19 'Tidal Leviathan'   'Creature' 'Common'   0 0 2 0 0 0 4 5 5 0 '' $false 'summoning' 7 $null)
    (New-Card 20 'Glacial Sentinel'  'Creature' 'Common'   0 0 1 0 0 0 3 2 5 2 '' $false 'summoning' 6 $null)
    (New-Card 21 'Mirage Weaver'     'Creature' 'Rare'     0 0 2 0 0 0 1 3 1 0 'Hexproof' $false 'summoning' 7 'Draw 2 cards')
    # ── Orange Creatures 22-26 ──
    (New-Card 22 'Carnelian Striker'  'Creature' 'Common'   0 0 0 1 0 0 1 2 2 0 'Alacrity' $false 'summoning' 5 $null)
    (New-Card 23 'Sunforge Knight'    'Creature' 'Uncommon' 0 0 0 1 0 0 2 3 2 0 'Initiative' $false 'summoning' 6 $null)
    (New-Card 24 'Amber Behemoth'     'Creature' 'Rare'     0 0 0 2 0 0 3 5 5 1 'Splash' $false 'summoning' 7 '+2/+2/+0')
    (New-Card 25 'Dawnfire Archer'    'Creature' 'Common'   0 0 0 1 0 0 1 2 1 0 'Ranged' $false 'summoning' 5 $null)
    (New-Card 26 'Magma Phoenix'      'Creature' 'Mythic'   0 0 0 2 0 0 4 4 3 0 'Flying, Alacrity' $false 'summoning' 8 'Deal 3 on death')
    # ── Green Creatures 27-31 ──
    (New-Card 27 'Emerald Wurm'      'Creature' 'Rare'     0 0 0 0 2 0 3 5 5 1 'Splash' $false 'summoning' 7 '+3/+3/+0')
    (New-Card 28 'Jade Bear'         'Creature' 'Common'   0 0 0 0 1 0 1 2 2 0 '' $false 'summoning' 5 $null)
    (New-Card 29 'Verdant Spider'    'Creature' 'Common'   0 0 0 0 1 0 2 2 4 0 'Ranged' $false 'summoning' 5 $null)
    (New-Card 30 'Ancient Treant'    'Creature' 'Rare'     0 0 0 0 2 0 4 4 7 3 '' $false 'summoning' 7 $null)
    (New-Card 31 'Thornbow Ranger'   'Creature' 'Common'   0 0 0 0 1 0 1 2 1 0 'Ranged' $false 'summoning' 5 $null)
    # ── Purple Creatures 32-35 (cost Purple = needs Ruby + Sapphire) ──
    (New-Card 32 'Prismatic Assassin' 'Creature' 'Uncommon' 0 0 0 0 0 1 1 3 2 0 'Exsanguinate' $false 'summoning' 6 $null)
    (New-Card 33 'Twilight Specter'   'Creature' 'Rare'     0 0 0 0 0 1 2 3 3 0 'Flying' $false 'summoning' 7 'Draw 1 card')
    (New-Card 34 'Void Colossus'      'Creature' 'Mythic'   0 0 0 0 0 2 4 6 6 2 '' $false 'summoning' 9 'Destroy target creature')
    (New-Card 35 'Spectral Shade'     'Creature' 'Common'   0 0 0 0 0 1 0 2 1 0 '' $false 'summoning' 5 $null)
    # ── Colorless Creatures 36-37 ──
    (New-Card 36 'Quartz Bastion'    'Creature' 'Common'   0 0 0 0 0 0 3 1 5 3 'Stalwart' $false 'summoning' 4 $null)
    (New-Card 37 'Iron Golem'        'Creature' 'Uncommon' 0 0 0 0 0 0 5 4 4 2 '' $false 'summoning' 5 $null)
    # ── Red Spells 38-40 ──
    (New-Card 38 'Spark Bolt'        'Cantrip'     'Common'   1 0 0 0 0 0 1 0 0 0 'Deal 3 damage' $false 'cantrip' 0 $null)
    (New-Card 39 'Conflagration'     'Incantation' 'Uncommon' 1 0 0 0 0 0 3 0 0 0 'Deal 5 damage' $false 'incantation' 6 'Deal 7 damage')
    (New-Card 40 'Inferno Wave'      'Incantation' 'Uncommon' 2 0 0 0 0 0 3 0 0 0 'Deal 4 damage to all' $false 'incantation' 7 'Deal 6 to all')
    # ── Yellow Spells 41-43 ──
    (New-Card 41 'Mending Light'     'Cantrip'     'Common'   0 1 0 0 0 0 0 0 0 0 'Gain 4 life' $false 'cantrip' 0 $null)
    (New-Card 42 'Radiant Shield'    'Enchantment' 'Uncommon' 0 1 0 0 0 0 1 0 0 0 'Prevent 2 damage' $false 'incantation' 5 'Prevent 4 damage')
    (New-Card 43 'Judgement Ray'     'Incantation' 'Uncommon' 0 2 0 0 0 0 2 0 0 0 'Destroy target' $false 'incantation' 6 'Destroy + gain 3 life')
    # ── Blue Spells 44-46 ──
    (New-Card 44 'Insight'           'Cantrip'     'Common'   0 0 1 0 0 0 0 0 0 0 'Draw 1 card' $false 'cantrip' 0 $null)
    (New-Card 45 'Deep Scrying'      'Incantation' 'Uncommon' 0 0 1 0 0 0 2 0 0 0 'Draw 2 cards' $false 'incantation' 6 'Draw 3 cards')
    (New-Card 46 'Nullify'           'Disruption'  'Rare'     0 0 2 0 0 0 0 0 0 0 'Disrupt caster' $false 'disruption' 8 $null)
    # ── Orange Spells 47-48 ──
    (New-Card 47 'Searing Touch'     'Cantrip'     'Common'   0 0 0 1 0 0 0 0 0 0 'Deal 2, gain 1 life' $false 'cantrip' 0 $null)
    (New-Card 48 'Sunrise Blast'     'Incantation' 'Uncommon' 0 0 0 1 0 0 2 0 0 0 'Deal 4 damage' $false 'incantation' 6 'Deal 4 + Alacrity to ally')
    # ── Green Spells 49-51 ──
    (New-Card 49 'Stonehide'         'Cantrip'     'Common'   0 0 0 0 1 0 0 0 0 0 '+3/+3/+0' $false 'cantrip' 0 $null)
    (New-Card 50 'Shatter'           'Cantrip'     'Uncommon' 0 0 0 0 1 0 1 0 0 0 'Destroy equipment' $false 'cantrip' 0 $null)
    (New-Card 51 'Reclaim'           'Incantation' 'Uncommon' 0 0 0 0 1 0 1 0 0 0 'Return from graveyard' $false 'incantation' 5 'Return 2 from graveyard')
    # ── Purple Spells 52-53 ──
    (New-Card 52 'Void Bolt'         'Cantrip'     'Uncommon' 0 0 0 0 0 1 0 0 0 0 'Deal 3 + exile' $false 'cantrip' 0 $null)
    (New-Card 53 'Prismatic Doom'    'Incantation' 'Rare'     0 0 0 0 0 1 0 2 0 0 'Destroy target, exile' $false 'incantation' 7 'Destroy + draw 2')
    # ── Disruption Spells 54-56 ──
    (New-Card 54 'Mosquito Plague'   'Disruption' 'Common'   0 0 0 0 0 0 1 0 0 0 'Disrupt caster' $false 'disruption' 3 $null)
    (New-Card 55 'Blinding Flash'    'Disruption' 'Uncommon' 0 1 0 0 0 0 1 0 0 0 'Disrupt caster' $false 'disruption' 5 $null)
    (New-Card 56 'Thunder Clap'      'Disruption' 'Rare'     1 0 0 0 0 0 2 0 0 0 'Disrupt caster' $false 'disruption' 8 $null)
    # ── Equipment 57-63 ──
    (New-Equipment 57 'Iron Sword'       'Common'   0 0 0 0 0 0 2 'Weapon'  2 0 0 '' $true)
    (New-Equipment 58 'Oak Shield'       'Common'   0 0 0 0 1 0 1 'Shield'  0 0 2 '' $true)
    (New-Equipment 59 'Leather Helm'     'Common'   0 0 0 0 0 0 1 'Helmet'  0 1 0 '' $true)
    (New-Equipment 60 'Chain Hauberk'    'Uncommon' 0 0 0 0 0 0 3 'Armor'   0 2 1 '' $true)
    (New-Equipment 61 'Signet Ring'      'Uncommon' 0 0 0 0 0 0 2 'Ring'    0 0 0 '' $true)
    (New-Equipment 62 'Amber Amulet'     'Uncommon' 0 0 0 1 0 0 1 'Amulet'  0 0 0 'Drain Life' $true)
    (New-Equipment 63 'Runed Greatsword' 'Rare'     1 0 0 0 0 0 3 'Weapon'  4 0 0 '' $true)
    # ── Triggered Creatures 64-69 ──
    (New-Card 64 'War Drummer'       'Creature' 'Uncommon' 1 0 0 0 0 0 1 2 2 0 'On attack: +1/+0/+0' $false 'summoning' 5 $null)
    (New-Card 65 'Cleric of Light'   'Creature' 'Uncommon' 0 1 0 0 0 0 1 1 3 1 'ETB: gain 3 life' $false 'summoning' 5 'ETB: gain 5 life')
    (New-Card 66 'Thought Wisp'      'Creature' 'Uncommon' 0 0 1 0 0 0 2 1 1 0 'Flying, ETB: draw 1' $false 'summoning' 6 'ETB: draw 2')
    (New-Card 67 'Carnelian Stalker' 'Creature' 'Uncommon' 0 0 0 1 0 0 1 2 2 0 'Combat dmg: gain 2 life' $false 'summoning' 5 $null)
    (New-Card 68 'Vine Caller'       'Creature' 'Uncommon' 0 0 0 0 1 0 2 2 3 0 'On cast: +0/+1/+1' $false 'summoning' 5 $null)
    (New-Card 69 'Prism Dragon'      'Creature' 'Mythic'   0 0 0 0 0 1 3 5 5 1 'Flying, Splash' $false 'summoning' 9 'Deal 3 to all on ETB')
)
$script:CardIndex = @{}; foreach ($c in $script:CardPool) { $script:CardIndex[$c.id] = $c }
$script:CurrentSeason.poolSize = $script:CardPool.Count

# ── Pack Cracking (spec-compliant) ───────────────────────────────
$script:PoolByRarity = @{ Common=@(); Uncommon=@(); Rare=@(); Mythic=@() }
foreach ($c in $script:CardPool) { if ($c.type -ne 'Gemstone') { $script:PoolByRarity[$c.rarity] += @($c) } }

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
        Clans = @{}; ClanNextId = $script:ClanNextId
        Listings = @{}; ListingNextId = $script:ListingNextId; NextTokenId = $script:NextTokenId
        TradeHistory = @($script:TradeHistory); TotalVolume = $script:TotalVolume
        PoolVersion = 2
    }
    foreach ($k in $script:AuthAccounts.Keys) {
        $a = $script:AuthAccounts[$k]
        $owned = if ($a.OwnedCards) { @($a.OwnedCards) } else { @() }
        $roles = if ($a.Roles) { @($a.Roles) } else { @() }
        $decks = if ($a.Decks) { $a.Decks } else { @{} }
        $state.AuthAccounts[$k] = @{ Id=$a.Id; Handle=$a.Handle; Display=$a.Display; Password=$a.Password; PwScore=$a.PwScore; Tfa=$a.Tfa; Admin=$a.Admin; Banned=$a.Banned; Balance=$a.Balance; OwnedCards=$owned; Wins=$a.Wins; Losses=$a.Losses; Rating=$a.Rating; Subscription=$a.Subscription; ClanId=$(if($a.ClanId){$a.ClanId}else{0}); Roles=$roles; Decks=$decks }
    }
    foreach ($k in $script:StoreItems.Keys) { $state.StoreItems["$k"] = $script:StoreItems[$k] }
    foreach ($k in $script:Clans.Keys) { $state.Clans["$k"] = $script:Clans[$k] }
    foreach ($k in $script:Listings.Keys) {
        $l = $script:Listings[$k].Clone()
        if ($l.createdAt -is [datetime]) { $l.createdAt = $l.createdAt.ToString('o') }
        if ($l.endsAt -is [datetime]) { $l.endsAt = $l.endsAt.ToString('o') }
        $state.Listings["$k"] = $l
    }
    $json = $state | ConvertTo-Json -Depth 7 -Compress
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
        try { $script:ClanNextId = [int]$state.ClanNextId } catch { $script:ClanNextId = 1 }
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
                ClanId = $(try { [int]$v.ClanId } catch { 0 })
                Roles = $(try { if ($v.Roles) { @($v.Roles) } else { @() } } catch { @() })
                Decks = $(try { if ($v.Decks -and $v.Decks.PSObject) { $dk = @{}; foreach ($dp in $v.Decks.PSObject.Properties) { $dk[$dp.Name] = @{ cards = @($dp.Value.cards | ForEach-Object { [int]$_ }) } }; $dk } else { @{} } } catch { @{} })
            }
        }
        foreach ($prop in $state.StoreItems.PSObject.Properties) {
            $v = $prop.Value
            $script:StoreItems[[int]$prop.Name] = @{ id=[int]$v.id; name=$v.name; type=$v.type; price=[int]$v.price; qty=[int]$v.qty; desc=$v.desc; discount=$(try{[int]$v.discount}catch{0}); saleLabel=$(if($v.saleLabel){$v.saleLabel}else{''}); sold=$(try{[int]$v.sold}catch{0}); cardId=$(try{[int]$v.cardId}catch{-1}); availableFrom=$(if($v.availableFrom){$v.availableFrom}else{''}); availableTo=$(if($v.availableTo){$v.availableTo}else{''}); discountFrom=$(if($v.discountFrom){$v.discountFrom}else{''}); discountTo=$(if($v.discountTo){$v.discountTo}else{''}) }
        }
        foreach ($prop in $state.Clans.PSObject.Properties) {
            $v = $prop.Value
            $mems = [System.Collections.ArrayList]::new()
            try { if ($v.members -is [array] -or ($v.members -is [object[]])) { foreach ($m in $v.members) { [void]$mems.Add(@{handle=$m.handle;role=$m.role;joinedAt=$(if($m.joinedAt){$m.joinedAt}else{Now-Stamp});clanCoins=$(try{[int]$m.clanCoins}catch{0})}) } }
            } catch { if ($v.members -is [int]) { if ($v.founder) { [void]$mems.Add(@{handle=$v.founder;role='Leader';joinedAt=Now-Stamp;clanCoins=0}) } } }
            $apps = [System.Collections.ArrayList]::new()
            try { if ($v.applications) { foreach ($a2 in $v.applications) { [void]$apps.Add(@{handle=$a2.handle;appliedAt=$(if($a2.appliedAt){$a2.appliedAt}else{Now-Stamp})}) } } } catch {}
            $trades = @{}; try { if ($v.trades -and $v.trades.PSObject) { foreach ($tp in $v.trades.PSObject.Properties) { $tv=$tp.Value; $trades[[int]$tp.Name]=@{id=[int]$tv.id;seller=$tv.seller;tokenId=[int]$tv.tokenId;cardId=[int]$tv.cardId;cardName=$tv.cardName;cardType=$(if($tv.cardType){$tv.cardType}else{'?'});rarity=$tv.rarity;priceClanCoins=[int]$tv.priceClanCoins;status=$tv.status;escrowCard=$(if($tv.escrowCard){@{cardId=[int]$tv.escrowCard.cardId;rarity=$tv.escrowCard.rarity;mintSource=$tv.escrowCard.mintSource;tokenId=[int]$tv.escrowCard.tokenId}}else{$null});createdAt=$(if($tv.createdAt){$tv.createdAt}else{Now-Stamp})} } } } catch {}
            $loans = @{}; try { if ($v.loans -and $v.loans.PSObject) { foreach ($lp in $v.loans.PSObject.Properties) { $lv=$lp.Value; $loans[[int]$lp.Name]=@{id=[int]$lv.id;lender=$lv.lender;borrower=$lv.borrower;tokenId=[int]$lv.tokenId;cardId=[int]$lv.cardId;cardName=$lv.cardName;rarity=$lv.rarity;lentAt=$lv.lentAt;expiresAt=$lv.expiresAt;status=$lv.status;originalCard=$(if($lv.originalCard){@{cardId=[int]$lv.originalCard.cardId;rarity=$lv.originalCard.rarity;mintSource=$lv.originalCard.mintSource;tokenId=[int]$lv.originalCard.tokenId}}else{$null})} } } } catch {}
            $th = [System.Collections.ArrayList]::new(); try { if ($v.tradeHistory) { foreach ($e in $v.tradeHistory) { [void]$th.Add(@{cardName=$e.cardName;price=[int]$e.price;buyer=$e.buyer;seller=$e.seller;time=$e.time}) } } } catch {}
            $script:Clans[[int]$prop.Name] = @{id=[int]$v.id;name=$v.name;tag=$v.tag;founder=$v.founder;leader=$(if($v.leader){$v.leader}else{$v.founder});createdAt=$(if($v.createdAt){$v.createdAt}else{Now-Stamp});members=$mems;applications=$apps;treasury=$(try{[int]$v.treasury}catch{0});isPaid=$(try{[bool]$v.isPaid}catch{$false});trades=$trades;tradeNextId=$(try{[int]$v.tradeNextId}catch{1});tradeHistory=$th;loans=$loans;loanNextId=$(try{[int]$v.loanNextId}catch{1});rank=$(if($v.rank){$v.rank}else{'Unranked'})}
        }
        foreach ($prop in $state.Listings.PSObject.Properties) {
            $v = $prop.Value
            $endsAt = if ($v.endsAt) { try { [datetime]::Parse($v.endsAt) } catch { $null } } else { $null }
            $createdAt = if ($v.createdAt) { try { [datetime]::Parse($v.createdAt) } catch { Get-Date } } else { Get-Date }
            $script:Listings[[int]$prop.Name] = @{ id=[int]$v.id; type=$v.type; tokenId=$v.tokenId; seller=$v.seller; cardName=$v.cardName; cardType=$v.cardType; rarity=$v.rarity; status=$v.status; createdAt=$createdAt; price=[int]$v.price; minBid=[int]$v.minBid; currentBid=[int]$v.currentBid; buyout=[int]$v.buyout; bidCount=[int]$v.bidCount; highBidder=$v.highBidder; endsAt=$endsAt }
        }
        foreach ($e in $state.MintLog) { $script:MintLog.Add(@{ type=$e.type; handle=$e.handle; amount=[int]$e.amount; reason=$e.reason; time=$e.time }) }
        foreach ($e in $state.TradeHistory) { $script:TradeHistory.Add(@{ type=$e.type; cardName=$e.cardName; price=[int]$e.price; buyer=$e.buyer; seller=$e.seller; time=$e.time }) }
        Write-Host "  Loaded $($script:AuthAccounts.Count) accounts, $($script:StoreItems.Count) store items, $($script:Listings.Count) listings, $($script:Clans.Count) clans from disk" -ForegroundColor Green
        # Migrate old WUBRG cards to prismatic system
        $poolVersion = if ($state.PSObject.Properties['PoolVersion']) { $state.PoolVersion } else { 1 }
        if ($poolVersion -lt 2) {
            Write-Host "  Migrating cards from WUBRG (v1) to Prismatic (v2)..." -ForegroundColor Yellow
            $maxOldId = 59; $rng = [System.Random]::new(42)
            foreach ($k in @($script:AuthAccounts.Keys)) {
                $a = $script:AuthAccounts[$k]
                $oldCards = @($a.OwnedCards | Where-Object { $_.cardId -le $maxOldId })
                if ($oldCards.Count -eq 0) { continue }
                $newCards = [System.Collections.ArrayList]::new()
                foreach ($c in $a.OwnedCards) {
                    if ($c.cardId -le $maxOldId) {
                        $newId = $script:CardPool[$rng.Next(7, $script:CardPool.Count)].id
                        [void]$newCards.Add(@{cardId=$newId;rarity=$c.rarity;mintSource='Migrated';tokenId=$c.tokenId})
                    } else {
                        [void]$newCards.Add($c)
                    }
                }
                $a.OwnedCards = $newCards
                Write-Host "    Migrated $($oldCards.Count) cards for $k" -ForegroundColor Cyan
            }
            # Clear old marketplace listings with old card IDs
            $oldListings = @($script:Listings.Keys | Where-Object { $script:Listings[$_].cardId -le $maxOldId })
            foreach ($lid in $oldListings) {
                $listing = $script:Listings[$lid]
                if ($listing.escrowCard) {
                    $seller = $script:AuthAccounts[$listing.seller]
                    if ($seller) {
                        $newId = $script:CardPool[$rng.Next(7, $script:CardPool.Count)].id
                        [void]$seller.OwnedCards.Add(@{cardId=$newId;rarity=$listing.rarity;mintSource='Migrated';tokenId=$listing.escrowCard.tokenId})
                    }
                }
                $script:Listings.Remove($lid)
            }
            if ($oldListings.Count -gt 0) { Write-Host "    Cleared $($oldListings.Count) old marketplace listings" -ForegroundColor Cyan }
            Write-Host "  Migration complete. Saving..." -ForegroundColor Green
            Save-State
        }
    } catch {
        Write-Host "  Failed to load state: $_ — starting fresh" -ForegroundColor Yellow
    }
}

Load-State

# CDX VM proxy: forward a request path to the CDX via the framed TCP bridge
function Invoke-CdxApi {
    param([string]$Path)
    if (-not $script:TcpStream) { return $null }
    try { return Send-GameRequest "GET $Path" } catch { return $null }
}

# Save CDX state to disk by requesting a state dump from the CDX VM
$script:LastCdxSave = [DateTime]::MinValue
function Save-CdxState {
    if (-not $script:TcpStream) { return }
    if (([DateTime]::UtcNow - $script:LastCdxSave).TotalSeconds -lt 5) { return }
    try {
        $dump = Invoke-CdxApi '/api/magic/state-dump'
        if ($dump -and $dump.Length -gt 20) {
            [System.IO.File]::WriteAllText($script:DbFile, $dump, [System.Text.UTF8Encoding]::new($false))
            $script:LastCdxSave = [DateTime]::UtcNow
        }
    } catch {}
}

function Get-DeckThumbprint {
    param([int[]]$Cards)
    $sorted = $Cards | Sort-Object
    $canonical = ($sorted | ForEach-Object { $_.ToString() }) -join ','
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($canonical))
    $sha.Dispose()
    return ([BitConverter]::ToString($hash).Replace('-','').Substring(0,16)).ToLower()
}

function Get-AuthUser { param([string]$Token); if (-not $Token -or -not $script:AuthSessions.ContainsKey($Token)) { return $null }; $h = $script:AuthSessions[$Token]; if (-not $script:AuthAccounts.ContainsKey($h)) { return $null }; return $script:AuthAccounts[$h] }
function Is-Admin { param([string]$Token); $u = Get-AuthUser $Token; if (-not $u) { return $false }; return ($u.Admin -eq $true) }
function Now-Stamp { return (Get-Date).ToString('HH:mm:ss') }
function Score-Password { param([string]$P); if ($P.Length -lt 5) { return 0 }; if ($P -notmatch '^[a-zA-Z0-9]+$') { return 0 }; $s = 1; if ($P.Length -ge 8) { $s++ }; if ($P -cmatch '[a-z]' -and $P -cmatch '[A-Z]') { $s++ }; if ($P -match '[0-9]') { $s++ }; return $s }
function Get-ClanTitle {
    param($Account)
    if (-not $Account.ClanId -or $Account.ClanId -eq 0 -or -not $script:Clans.ContainsKey($Account.ClanId)) { return '' }
    $cl = $script:Clans[$Account.ClanId]
    $mem = $null; foreach ($m in $cl.members) { if ($m.handle -eq $Account.Handle) { $mem = $m; break } }
    if (-not $mem) { return '' }
    $mc = $cl.members.Count; $paid = $cl.isPaid; $treas = $cl.treasury
    $title = switch ($mem.role) {
        'Leader' {
            if ($paid -and $mc -ge 10 -and $treas -ge 10000) { 'Overlord' }
            elseif ($paid -and $mc -ge 5) { 'Warlord' }
            elseif ($paid) { 'Commander' }
            else { 'Chieftain' }
        }
        'Officer' {
            if ($paid -and $mc -ge 10) { 'Marshal' }
            elseif ($paid) { 'Captain' }
            else { 'Lieutenant' }
        }
        default {
            if ($paid -and $mc -ge 10) { 'Veteran' }
            elseif ($paid) { 'Warrior' }
            else { 'Initiate' }
        }
    }
    return "[$($cl.tag) $title]"
}

function Get-AdornedName {
    param($Account)
    $name = $Account.Display
    $badges = @()
    if ($Account.Admin) { $badges += '[Admin]' }
    if ($Account.Roles) { foreach ($r in $Account.Roles) { $badges += "[$r]" } }
    $clanTag = Get-ClanTitle $Account
    if ($clanTag) { $badges += $clanTag }
    if ($badges.Count -gt 0) { return "$name " + ($badges -join ' ') }
    return $name
}

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
        $isFirst = $script:AuthAccounts.Count -eq 0
        if ($pwScore -eq 0 -and -not $isFirst) { Send-Json $Response '{"error":"password too weak"}' 400; return }
        if ($pwScore -eq 0) { $pwScore = 1 }
        if ($script:AuthAccounts.ContainsKey($ul)) { Send-Json $Response '{"error":"handle already taken"}' 400; return }
        $id = $script:AuthNextId++; $display = if ($d) { $d } else { $u }
        $starterCards = [System.Collections.ArrayList]::new()
        $starterPack = Crack-PackCards -PackType 'standard' -Sub 'Free'
        foreach ($c in $starterPack) { [void]$starterCards.Add($c) }
        $script:AuthAccounts[$ul] = @{ Id=$id; Handle=$ul; Display=$display; Password=$p; PwScore=$pwScore; Tfa=$false; Admin=$isFirst; Banned=$false; Balance=500; OwnedCards=$starterCards; Wins=0; Losses=0; Rating=1000; Subscription='Free'; ClanId=0; Roles=@() }
        $tok = [guid]::NewGuid().ToString('N'); $script:AuthSessions[$tok] = $ul; $labels = @('rejected','thin','fair','good','strong')
        Send-Json $Response "{`"token`":`"$tok`",`"handle`":`"$ul`",`"display`":`"$display`",`"id`":$id,`"pw-score`":$pwScore,`"pw-label`":`"$($labels[$pwScore])`",`"tfa`":false,`"admin`":$($isFirst.ToString().ToLower()),`"starterGrant`":true,`"starterBalance`":500,`"starterCards`":$($starterPack.Count)}"
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
        $adorned = Get-AdornedName $acct
        Send-Json $Response "{`"handle`":`"$u`",`"display`":`"$($acct.Display)`",`"adorned`":`"$adorned`",`"id`":$($acct.Id),`"pw-score`":$($acct.PwScore),`"pw-label`":`"$($labels[$acct.PwScore])`",`"tfa`":$($acct.Tfa.ToString().ToLower()),`"admin`":$($acct.Admin.ToString().ToLower())}"
    }
    elseif ($path -eq '/api/auth/logout') { $t = $qs['t']; if ($t) { $script:AuthSessions.Remove($t) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/auth/profile') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]; $labels = @('rejected','thin','fair','good','strong')
        $ownedCount = if ($a.OwnedCards) { $a.OwnedCards.Count } else { 0 }
        $clanId = if ($a.ClanId) { $a.ClanId } else { 0 }
        $adorned = Get-AdornedName $a
        $rolesJson = '[' + (($a.Roles | ForEach-Object { "`"$_`"" }) -join ',') + ']'
        Send-Json $Response "{`"handle`":`"$u`",`"display`":`"$($a.Display)`",`"adorned`":`"$adorned`",`"id`":$($a.Id),`"admin`":$($a.Admin.ToString().ToLower()),`"roles`":$rolesJson,`"pw-score`":$($a.PwScore),`"pw-label`":`"$($labels[$a.PwScore])`",`"balance`":$($a.Balance),`"tokens`":$ownedCount,`"wins`":$($a.Wins),`"losses`":$($a.Losses),`"rating`":$($a.Rating),`"subscription`":`"$($a.Subscription)`",`"clanId`":$clanId,`"rank`":`"$(if ($a.Rating -ge 1800) {'Diamond'} elseif ($a.Rating -ge 1500) {'Platinum'} elseif ($a.Rating -ge 1200) {'Gold'} elseif ($a.Rating -ge 900) {'Silver'} else {'Bronze'})`"}"
    }
    elseif ($path -eq '/api/auth/pool') {
        $cdxPool = Invoke-CdxApi '/api/magic/pool'
        if ($cdxPool) { Send-Json $Response $cdxPool }
        else {
            $json = ($script:CardPool | ForEach-Object { $c = $_
                $cj = $c.cost | ConvertTo-Json -Compress
                $clj = $c.color | ConvertTo-Json -Compress
                $q = '"'
                $ss = if ($c.spellSpeed) { $q + $c.spellSpeed + $q } else { 'null' }
                $fc = if ($c.fluorClause) { $q + $c.fluorClause + $q } else { 'null' }
                $extra = ''
                if ($c.type -eq 'Gemstone') { $extra = ',"gemColor":' + $q + $c.gemColor + $q + ',"variety":' + $q + $c.variety + $q + ',"hardness":' + $c.hardness }
                if ($c.type -eq 'Equipment') { $extra = ',"slot":' + $q + $c.slot + $q + ',"socketEmpty":' + $c.socketEmpty.ToString().ToLower() }
                '{' + '"id":' + $c.id + ',"name":' + $q + $c.name + $q + ',"type":' + $q + $c.type + $q + ',"rarity":' + $q + $c.rarity + $q + ',"cost":' + $cj + ',"color":' + $clj + ',"power":' + $c.power + ',"toughness":' + $c.toughness + ',"defense":' + $c.defense + ',"keywords":' + $q + $c.keywords + $q + ',"isBasic":' + $c.isBasic.ToString().ToLower() + ',"spellSpeed":' + $ss + ',"baseFocus":' + $c.baseFocus + ',"fluorClause":' + $fc + $extra + '}'
            }) -join ','
            Send-Json $Response ('{"cards":[' + $json + ']}')
        }
    }
    elseif ($path -eq '/api/auth/rules') {
        $cdxRules = Invoke-CdxApi '/api/magic/rules'
        if ($cdxRules) { Send-Json $Response $cdxRules }
        else {
            $rules = '{"deck":{"minSize":60,"maxCopies":4,"minGemsRecommended":15,"gemTarget":24,"spellTarget":36},"pack":{"commons":10,"uncommons":3,"rareOrMythic":1,"wildcards":1,"mythicChance":"1/8"},"packCost":{"standard":100,"premium":300,"draft":250},"tierBonus":{"Free":0,"Bronze":1,"Silver":2,"Gold":3,"Platinum":5},"starterBalance":500,"colors":["Red","Yellow","Blue","Orange","Green","Purple"],"gemstones":["Ruby","Topaz","Sapphire","Carnelian","Emerald","Diamond","Obsidian"],"spellSpeeds":["cantrip","incantation","summoning","disruption"],"cardTypes":["Gemstone","Creature","Equipment","Cantrip","Incantation","Disruption","Enchantment"],"equipSlots":["Weapon","Shield","Helmet","Armor","Gloves","Boots","Ring","Amulet"],"phases":[{"step":0,"name":"Untap"},{"step":1,"name":"Upkeep"},{"step":2,"name":"Draw"},{"step":3,"name":"Pre-combat Main"},{"step":4,"name":"Declare Attackers"},{"step":5,"name":"Declare Blockers"},{"step":6,"name":"Combat Damage"},{"step":7,"name":"End Combat"},{"step":8,"name":"Post-combat Main"},{"step":9,"name":"Cleanup"}]}'
            Send-Json $Response $rules
        }
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
    elseif ($path -eq '/api/auth/decks') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"decks":[]}'; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $decks = if ($a.Decks) { $a.Decks } else { @{} }
        $q = '"'
        $list = @($decks.Keys | ForEach-Object { $d = $decks[$_]; $vCount = if($d.versions){$d.versions.Count}else{0}; '{' + $q + 'name' + $q + ':' + $q + $_ + $q + ',' + $q + 'cards' + $q + ':' + $d.cards.Count + ',' + $q + 'thumbprint' + $q + ':' + $q + $d.thumbprint + $q + ',' + $q + 'created' + $q + ':' + $q + $d.created + $q + ',' + $q + 'modified' + $q + ':' + $q + $d.modified + $q + ',' + $q + 'versions' + $q + ':' + $vCount + '}' })
        Send-Json $Response ('{"decks":[' + ($list -join ',') + ']}')
    }
    elseif ($path -eq '/api/auth/save-deck') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $name = $qs['name']; $cards = $qs['cards']
        if (-not $name -or $name.Length -lt 1) { Send-Json $Response '{"error":"name required"}' 400; return }
        if (-not $cards) { Send-Json $Response '{"error":"cards required"}' 400; return }
        $cardList = @($cards -split ',' | ForEach-Object { [int]$_ })
        $thumbprint = Get-DeckThumbprint $cardList
        $now = (Get-Date).ToString('o')
        if (-not $a.Decks) { $a.Decks = @{} }
        if ($a.Decks.ContainsKey($name)) {
            $d = $a.Decks[$name]; $d.cards = $cardList; $d.thumbprint = $thumbprint; $d.modified = $now
        } else {
            $a.Decks[$name] = @{ cards = $cardList; thumbprint = $thumbprint; created = $now; modified = $now; versions = @() }
        }
        Save-State
        Send-Json $Response ('{"ok":true,"name":"' + $name + '","cards":' + $cardList.Count + ',"thumbprint":"' + $thumbprint + '"}')
    }
    elseif ($path -eq '/api/auth/mark-deck-version') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $name = $qs['name']; $label = $qs['label']
        if (-not $a.Decks -or -not $a.Decks.ContainsKey($name)) { Send-Json $Response '{"error":"deck not found"}' 404; return }
        $d = $a.Decks[$name]
        if (-not $label) { $label = 'v' + ($d.versions.Count + 1) }
        $ver = @{ label = $label; cards = @($d.cards); thumbprint = $d.thumbprint; markedAt = (Get-Date).ToString('o') }
        $d.versions = @($d.versions) + @($ver)
        Save-State
        Send-Json $Response ('{"ok":true,"label":"' + $label + '","thumbprint":"' + $d.thumbprint + '","version":' + $d.versions.Count + '}')
    }
    elseif ($path -eq '/api/auth/load-deck') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $name = $qs['name']; $ver = $qs['version']
        if (-not $a.Decks -or -not $a.Decks.ContainsKey($name)) { Send-Json $Response '{"error":"deck not found"}' 404; return }
        $d = $a.Decks[$name]
        if ($ver) {
            $vi = [int]$ver - 1
            if ($vi -lt 0 -or $vi -ge $d.versions.Count) { Send-Json $Response '{"error":"version not found"}' 404; return }
            $v = $d.versions[$vi]
            $cardsJson = ($v.cards | ForEach-Object { $_ }) -join ','
            Send-Json $Response ('{"name":"' + $name + '","label":"' + $v.label + '","thumbprint":"' + $v.thumbprint + '","cards":[' + $cardsJson + ']}')
        } else {
            $cardsJson = ($d.cards | ForEach-Object { $_ }) -join ','
            $q = '"'
            $versJson = @($d.versions | ForEach-Object { '{' + $q + 'label' + $q + ':' + $q + $_.label + $q + ',' + $q + 'thumbprint' + $q + ':' + $q + $_.thumbprint + $q + ',' + $q + 'markedAt' + $q + ':' + $q + $_.markedAt + $q + ',' + $q + 'cards' + $q + ':' + $_.cards.Count + '}' }) -join ','
            Send-Json $Response ('{"name":"' + $name + '","thumbprint":"' + $d.thumbprint + '","created":"' + $d.created + '","modified":"' + $d.modified + '","cards":[' + $cardsJson + '],"versions":[' + $versJson + ']}')
        }
    }
    elseif ($path -eq '/api/auth/delete-deck') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $name = $qs['name']
        if ($a.Decks -and $a.Decks.ContainsKey($name)) { $a.Decks.Remove($name); Save-State }
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/auth/crack-pack') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $packType = $qs['type']; $cost = switch ($packType) { 'premium'{300} 'draft'{250} default{100} }
        if ($a.Balance -lt $cost) { Send-Json $Response "{`"error`":`"Not enough MC. Need $cost, have $($a.Balance).`"}" 400; return }
        if (-not $a.OwnedCards) { $a.OwnedCards = [System.Collections.ArrayList]::new() }
        $pulled = Crack-PackCards -PackType $packType -Sub $a.Subscription
        $a.Balance -= $cost
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
        $discActive = $item.discount -gt 0
        if ($discActive -and $item.discountFrom) { try { if ([datetime]::Parse($item.discountFrom) -gt (Get-Date)) { $discActive = $false } } catch {} }
        if ($discActive -and $item.discountTo) { try { if ([datetime]::Parse($item.discountTo) -lt (Get-Date)) { $discActive = $false } } catch {} }
        $effectivePrice = if ($discActive) { [Math]::Floor($item.price * (1 - $item.discount / 100)) } else { $item.price }
        if ($a.Balance -lt $effectivePrice) { Send-Json $Response "{`"error`":`"Not enough MC. Need $effectivePrice, have $($a.Balance).`"}" 400; return }
        if (-not $a.OwnedCards) { $a.OwnedCards = [System.Collections.ArrayList]::new() }
        $pulled = @()
        if ($item.type -in @('pack','bundle','starter')) {
            $packCount = switch ($item.type) { 'bundle'{3} 'starter'{1} default{1} }
            for ($pi = 0; $pi -lt $packCount; $pi++) {
                $cards = Crack-PackCards -PackType 'standard' -Sub $a.Subscription
                foreach ($c in $cards) { [void]$a.OwnedCards.Add($c); $pulled += $c }
            }
        } elseif ($item.type -eq 'card' -and $item.cardId -ge 0) {
            $tmpl = $script:CardIndex[$item.cardId]
            $rar = if ($tmpl) { $tmpl.rarity } else { 'Common' }
            $nc = @{cardId=$item.cardId;rarity=$rar;mintSource='Store';tokenId=$script:NextTokenId++}
            [void]$a.OwnedCards.Add($nc); $pulled += $nc
        }
        $a.Balance -= $effectivePrice
        if ($item.qty -gt 0) { $item.qty-- }
        $item.sold++
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
        $adorned = Get-AdornedName $a
        Send-Json $Response "{`"ok`":true,`"subscription`":`"$tier`",`"adorned`":`"$adorned`"}"
    }
    elseif ($path -eq '/api/auth/wipe-collection') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        if (-not $a.Admin) { Send-Json $Response '{"error":"admin only"}' 403; return }
        $count = if ($a.OwnedCards) { $a.OwnedCards.Count } else { 0 }
        $a.OwnedCards = [System.Collections.ArrayList]::new()
        Save-State
        Send-Json $Response ('{"ok":true,"wiped":' + $count + '}')
    }
    elseif ($path -eq '/api/auth/forget-me') {
        $t = $qs['t']; if (-not $t -or -not $script:AuthSessions.ContainsKey($t)) { Send-Json $Response '{"error":"not authenticated"}' 401; return }
        $u = $script:AuthSessions[$t]; $a = $script:AuthAccounts[$u]
        $confirm = $qs['confirm']
        if ($confirm -ne 'DELETE') { Send-Json $Response '{"error":"pass confirm=DELETE to confirm"}' 400; return }
        if ($a.ClanId -and $a.ClanId -ne 0 -and $script:Clans.ContainsKey($a.ClanId)) {
            $cl = $script:Clans[$a.ClanId]; $me2 = $null; foreach ($m in $cl.members) { if ($m.handle -eq $u) { $me2 = $m; break } }
            $cl.members = [System.Collections.ArrayList]@($cl.members | Where-Object { $_.handle -ne $u })
            if ($me2 -and $me2.role -eq 'Leader') {
                $off = @($cl.members | Where-Object { $_.role -eq 'Officer' } | Sort-Object { $_.joinedAt })
                $reg = @($cl.members | Where-Object { $_.role -eq 'Member' } | Sort-Object { $_.joinedAt })
                if ($off.Count -gt 0) { $off[0].role = 'Leader'; $cl.leader = $off[0].handle }
                elseif ($reg.Count -gt 0) { $reg[0].role = 'Leader'; $cl.leader = $reg[0].handle }
                else { $script:Clans.Remove($a.ClanId) }
            }
        }
        foreach ($lk in @($script:Listings.Keys)) { $l = $script:Listings[$lk]; if ($l.seller -eq $u -and $l.status -eq 'active') { $l.status = 'cancelled' } }
        $script:AuthSessions.Remove($t)
        $script:AuthAccounts.Remove($u)
        Send-Json $Response '{"ok":true,"forgotten":true}'
    }
    elseif ($path -eq '/api/auth/store-items') {
        $now = Get-Date
        $visible = @($script:StoreItems.Values | Where-Object {
            $show = $true
            if ($_.availableFrom) { try { if ([datetime]::Parse($_.availableFrom) -gt $now) { $show = $false } } catch {} }
            if ($_.availableTo) { try { if ([datetime]::Parse($_.availableTo) -lt $now) { $show = $false } } catch {} }
            $show
        } | Sort-Object { $_.id })
        $it = $visible | ForEach-Object {
            $discActive = $_.discount -gt 0; $discPct = $_.discount; $sl = $_.saleLabel
            if ($discActive -and $_.discountFrom) { try { if ([datetime]::Parse($_.discountFrom) -gt $now) { $discActive = $false } } catch {} }
            if ($discActive -and $_.discountTo) { try { if ([datetime]::Parse($_.discountTo) -lt $now) { $discActive = $false } } catch {} }
            $dv = if ($discActive) { $discPct } else { 0 }; $slv = if ($discActive) { $sl } else { '' }
            "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"type`":`"$($_.type)`",`"price`":$($_.price),`"qty`":$($_.qty),`"discount`":$dv,`"saleLabel`":`"$slv`",`"sold`":$($_.sold)}"
        }
        Send-Json $Response "{`"items`":[$($it -join ',')]}"
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
    elseif ($path -eq '/api/admin/users') { $u = $script:AuthAccounts.Values | ForEach-Object { $rj='[' + (($_.Roles | ForEach-Object { "`"$_`"" }) -join ',') + ']'; "{`"id`":$($_.Id),`"handle`":`"$($_.Handle)`",`"display`":`"$($_.Display)`",`"admin`":$($_.Admin.ToString().ToLower()),`"banned`":$($_.Banned.ToString().ToLower()),`"pwScore`":$($_.PwScore),`"balance`":$($_.Balance),`"roles`":$rj}" }; Send-Json $Response "{`"users`":[$($u -join ',')]}" }
    elseif ($path -eq '/api/admin/set-role') { $hl = ($qs['handle']).ToLower(); if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown"}' 400; return }; $script:AuthAccounts[$hl].Admin = ($qs['admin'] -eq '1'); Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/ban') { $hl = ($qs['handle']).ToLower(); if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown"}' 400; return }; $script:AuthAccounts[$hl].Banned = ($qs['banned'] -eq '1'); Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/set-roles') { $hl = ($qs['handle']).ToLower(); if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"unknown"}' 400; return }; $roleStr = $qs['roles']; $script:AuthAccounts[$hl].Roles = if ($roleStr) { @($roleStr -split ',') } else { @() }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/add-item') { $name = $qs['name']; if (-not $name) { Send-Json $Response '{"error":"name required"}' 400; return }; $id = $script:StoreNextId++; $avFrom = if($qs['availableFrom']){$qs['availableFrom']}else{''}; $avTo = if($qs['availableTo']){$qs['availableTo']}else{''}; $cid = if($qs['cardId']){[int]$qs['cardId']}else{-1}; $script:StoreItems[$id] = @{id=$id;name=$name;type=$qs['type'];price=[int]$qs['price'];qty=[int]$qs['qty'];desc=$qs['desc'];discount=0;saleLabel='';sold=0;cardId=$cid;availableFrom=$avFrom;availableTo=$avTo;discountFrom='';discountTo=''}; Send-Json $Response "{`"ok`":true,`"id`":$id}" }
    elseif ($path -eq '/api/admin/store-items') { $it = $script:StoreItems.Values | Sort-Object {$_.id} | ForEach-Object { $af=if($_.availableFrom){$_.availableFrom}else{''}; $at2=if($_.availableTo){$_.availableTo}else{''}; $df=if($_.discountFrom){$_.discountFrom}else{''}; $dt=if($_.discountTo){$_.discountTo}else{''}; $cid=if($_.cardId){$_.cardId}else{-1}; "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"type`":`"$($_.type)`",`"price`":$($_.price),`"qty`":$($_.qty),`"discount`":$($_.discount),`"saleLabel`":`"$($_.saleLabel)`",`"sold`":$($_.sold),`"cardId`":$cid,`"availableFrom`":`"$af`",`"availableTo`":`"$at2`",`"discountFrom`":`"$df`",`"discountTo`":`"$dt`"}" }; Send-Json $Response "{`"items`":[$($it -join ',')]}" }
    elseif ($path -eq '/api/admin/set-discount') { $id = [int]$qs['id']; if (-not $script:StoreItems.ContainsKey($id)) { Send-Json $Response '{"error":"not found"}' 400; return }; $script:StoreItems[$id].discount = [int]$qs['pct']; $script:StoreItems[$id].saleLabel = if ($qs['label']) { $qs['label'] } else { '' }; $script:StoreItems[$id].discountFrom = if($qs['discountFrom']){$qs['discountFrom']}else{''}; $script:StoreItems[$id].discountTo = if($qs['discountTo']){$qs['discountTo']}else{''}; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/remove-item') { $id = [int]$qs['id']; if ($script:StoreItems.ContainsKey($id)) { $script:StoreItems.Remove($id) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/clans') { $c = $script:Clans.Values | ForEach-Object { $mc=$_.members.Count; $tc=@($_.trades.Values|Where-Object{$_.status -eq 'active'}).Count; $lc=@($_.loans.Values|Where-Object{$_.status -eq 'active'}).Count; "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"tag`":`"$($_.tag)`",`"leader`":`"$($_.leader)`",`"members`":$mc,`"isPaid`":$($_.isPaid.ToString().ToLower()),`"treasury`":$($_.treasury),`"trades`":$tc,`"loans`":$lc}" }; Send-Json $Response "{`"clans`":[$($c -join ',')]}" }
    elseif ($path -eq '/api/admin/disband-clan') { $id = [int]$qs['id']; if ($script:Clans.ContainsKey($id)) { $cl=$script:Clans[$id]; foreach($m in $cl.members){if($script:AuthAccounts.ContainsKey($m.handle)){$script:AuthAccounts[$m.handle].ClanId=0}}; foreach($tk in @($cl.trades.Keys)){$tr=$cl.trades[$tk];if($tr.status -eq 'active' -and $tr.escrowCard -and $script:AuthAccounts.ContainsKey($tr.seller)){$sa=$script:AuthAccounts[$tr.seller];if(-not $sa.OwnedCards){$sa.OwnedCards=[System.Collections.ArrayList]::new()};[void]$sa.OwnedCards.Add($tr.escrowCard)}}; foreach($lk in @($cl.loans.Keys)){$ln=$cl.loans[$lk];if($ln.status -eq 'active'){if($ln.originalCard -and $script:AuthAccounts.ContainsKey($ln.lender)){$la=$script:AuthAccounts[$ln.lender];if(-not $la.OwnedCards){$la.OwnedCards=[System.Collections.ArrayList]::new()};[void]$la.OwnedCards.Add($ln.originalCard)};if($script:AuthAccounts.ContainsKey($ln.borrower)){$ba=$script:AuthAccounts[$ln.borrower];for($i=0;$i -lt $ba.OwnedCards.Count;$i++){if($ba.OwnedCards[$i] -is [hashtable] -and $ba.OwnedCards[$i].tokenId -eq $ln.tokenId){$ba.OwnedCards.RemoveAt($i);break}}}}}; $script:Clans.Remove($id) }; Send-Json $Response '{"ok":true}' }
    elseif ($path -eq '/api/admin/blockchain') { $supply = 0; foreach ($a in $script:AuthAccounts.Values) { $supply += $a.Balance }; $txC = $script:MintLog.Count + $script:TradeHistory.Count; $bc = [Math]::Max(1,[Math]::Floor($txC/3)+1); $hash = '{0:x16}' -f ([Math]::Abs($bc.GetHashCode())*7919); $rt = @(); foreach ($e in ($script:MintLog | Select-Object -Last 5)) { $rt += "{`"time`":`"$($e.time)`",`"type`":`"$($e.type)`",`"detail`":`"$($e.amount) MC $($e.handle)`"}" }; foreach ($e in ($script:TradeHistory | Select-Object -Last 5)) { $rt += "{`"time`":`"$($e.time)`",`"type`":`"trade`",`"detail`":`"$($e.cardName) $($e.price) MC`"}" }; Send-Json $Response "{`"blocks`":$bc,`"transactions`":$txC,`"tokens`":$supply,`"supply`":$supply,`"height`":$bc,`"lastHash`":`"0x$hash`",`"avgBlockTime`":`"12`",`"pendingTx`":0,`"nodeStatus`":`"Online`",`"recentTx`":[$($rt -join ',')]}" }
    else { Send-Json $Response '{"error":"unknown admin endpoint"}' 404 }
}

function Handle-Market {
    param($Context, $Response)
    $path = $Context.Request.Url.AbsolutePath; $qs = [System.Web.HttpUtility]::ParseQueryString($Context.Request.Url.Query); $tok = $qs['t']; $user = Get-AuthUser $tok
    if ($path -eq '/api/market/balance') { Send-Json $Response "{`"balance`":$(if ($user) { $user.Balance } else { 0 })}"; return }
    elseif ($path -eq '/api/market/listings') {
        foreach ($expL in @($script:Listings.Values | Where-Object { $_.status -eq 'active' -and $_.type -eq 'auction' -and $_.endsAt -and $_.endsAt -le (Get-Date) })) {
            if ($expL.highBidder -and $script:AuthAccounts.ContainsKey($expL.highBidder)) {
                $winner = $script:AuthAccounts[$expL.highBidder]
                if (-not $winner.OwnedCards) { $winner.OwnedCards = [System.Collections.ArrayList]::new() }
                $wonCard = @{cardId=$expL.cardId;rarity=$expL.rarity;mintSource='Trade';tokenId=$script:NextTokenId++}
                [void]$winner.OwnedCards.Add($wonCard)
                if ($script:AuthAccounts.ContainsKey($expL.seller)) { $script:AuthAccounts[$expL.seller].Balance += $expL.currentBid }
                $script:TotalVolume += $expL.currentBid
                $script:TradeHistory.Add(@{type='auction-won';cardName=$expL.cardName;price=$expL.currentBid;buyer=$expL.highBidder;seller=$expL.seller;time=Now-Stamp})
                $expL.status = 'sold'
            } else {
                if ($expL.escrowCard -and $script:AuthAccounts.ContainsKey($expL.seller)) {
                    $sellerAcct = $script:AuthAccounts[$expL.seller]
                    if (-not $sellerAcct.OwnedCards) { $sellerAcct.OwnedCards = [System.Collections.ArrayList]::new() }
                    [void]$sellerAcct.OwnedCards.Add($expL.escrowCard)
                }
                $expL.status = 'expired'
            }
            Save-State
        }
        $active = @($script:Listings.Values | Where-Object { $_.status -eq 'active' })
        if ($qs['type']) { $active = @($active | Where-Object { $_.type -eq $qs['type'] }) }
        if ($qs['cardType']) { $active = @($active | Where-Object { $_.cardType -eq $qs['cardType'] }) }
        if ($qs['rarity']) { $active = @($active | Where-Object { $_.rarity -eq $qs['rarity'] }) }
        if ($qs['search']) { $active = @($active | Where-Object { $_.cardName -like "*$($qs['search'])*" }) }
        $sort = $qs['sort']; if ($sort -eq 'price-low') { $active = @($active | Sort-Object { if ($_.type -eq 'auction') { $_.currentBid } else { $_.price } }) } elseif ($sort -eq 'price-high') { $active = @($active | Sort-Object { if ($_.type -eq 'auction') { $_.currentBid } else { $_.price } } -Descending) } elseif ($sort -eq 'ending') { $active = @($active | Sort-Object { if ($_.endsAt) { $_.endsAt } else { [datetime]::MaxValue } }) } else { $active = @($active | Sort-Object { $_.id } -Descending) }
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
        else { $l.minBid = [int]$qs['minBid']; if ($l.minBid -lt 1) { Send-Json $Response '{"error":"starting bid must be positive"}' 400; return }; $l.buyout = [int]$qs['buyout']; if ($l.buyout -lt 0) { $l.buyout = 0 }; $dur = [int]$qs['duration']; if ($dur -lt 1) { $dur = 24 }; $l.currentBid = $l.minBid; $l.endsAt = (Get-Date).AddHours($dur) }
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
        if ($l.endsAt -and $l.endsAt -le (Get-Date)) { Send-Json $Response '{"error":"auction has ended"}' 400; return }
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
        if ($l.endsAt -and $l.endsAt -le (Get-Date)) { Send-Json $Response '{"error":"auction has ended"}' 400; return }
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

function Expire-ClanLoans {
    param($Clan)
    $now = Get-Date
    foreach ($lk in @($Clan.loans.Keys)) {
        $loan = $Clan.loans[$lk]
        if ($loan.status -ne 'active') { continue }
        $exp = try { [datetime]::Parse($loan.expiresAt) } catch { $null }
        if (-not $exp -or $exp -gt $now) { continue }
        if ($script:AuthAccounts.ContainsKey($loan.borrower)) {
            $ba = $script:AuthAccounts[$loan.borrower]
            for ($i = 0; $i -lt $ba.OwnedCards.Count; $i++) {
                $c = $ba.OwnedCards[$i]
                if ($c -is [hashtable] -and $c.tokenId -eq $loan.tokenId) { $ba.OwnedCards.RemoveAt($i); break }
            }
        }
        if ($loan.originalCard -and $script:AuthAccounts.ContainsKey($loan.lender)) {
            $la = $script:AuthAccounts[$loan.lender]
            if (-not $la.OwnedCards) { $la.OwnedCards = [System.Collections.ArrayList]::new() }
            [void]$la.OwnedCards.Add($loan.originalCard)
        }
        $loan.status = 'returned'
    }
}

function Get-ClanMember { param($Clan, [string]$Handle); foreach ($m in $Clan.members) { if ($m.handle -eq $Handle) { return $m } }; return $null }

function Handle-Clan {
    param($Context, $Response)
    $path = $Context.Request.Url.AbsolutePath; $qs = [System.Web.HttpUtility]::ParseQueryString($Context.Request.Url.Query); $tok = $qs['t']; $user = Get-AuthUser $tok
    if ($path -eq '/api/clan/info') {
        $cid = if ($qs['id']) { [int]$qs['id'] } elseif ($user -and $user.ClanId) { $user.ClanId } else { 0 }
        if ($cid -eq 0 -or -not $script:Clans.ContainsKey($cid)) { Send-Json $Response '{"id":0}'; return }
        $cl = $script:Clans[$cid]; Expire-ClanLoans $cl
        $mc = $cl.members.Count; $ac = $cl.applications.Count; $tc = @($cl.trades.Values | Where-Object { $_.status -eq 'active' }).Count; $lc = @($cl.loans.Values | Where-Object { $_.status -eq 'active' }).Count
        Send-Json $Response "{`"id`":$($cl.id),`"name`":`"$($cl.name)`",`"tag`":`"$($cl.tag)`",`"leader`":`"$($cl.leader)`",`"founder`":`"$($cl.founder)`",`"members`":$mc,`"applications`":$ac,`"treasury`":$($cl.treasury),`"isPaid`":$($cl.isPaid.ToString().ToLower()),`"activeTrades`":$tc,`"activeLoans`":$lc,`"rank`":`"$($cl.rank)`",`"createdAt`":`"$($cl.createdAt)`"}"
    }
    elseif ($path -eq '/api/clan/search') {
        $q = $qs['q']; if (-not $q) { Send-Json $Response '{"clans":[]}'; return }
        $ql = $q.ToLower()
        $found = @($script:Clans.Values | Where-Object { $_.name -like "*$ql*" -or $_.tag -like "*$ql*" })
        $items = $found | ForEach-Object { "{`"id`":$($_.id),`"name`":`"$($_.name)`",`"tag`":`"$($_.tag)`",`"leader`":`"$($_.leader)`",`"members`":$($_.members.Count)}" }
        Send-Json $Response "{`"clans`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/clan/create') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        if ($user.ClanId -and $user.ClanId -ne 0) { Send-Json $Response '{"error":"already in a clan"}' 400; return }
        $name = $qs['name']; $tag = $qs['tag']
        if (-not $name -or $name.Length -lt 2 -or $name.Length -gt 32) { Send-Json $Response '{"error":"name must be 2-32 characters"}' 400; return }
        if (-not $tag -or $tag.Length -lt 3 -or $tag.Length -gt 5) { Send-Json $Response '{"error":"tag must be 3-5 characters"}' 400; return }
        if ($tag -notmatch '^[A-Za-z0-9]+$') { Send-Json $Response '{"error":"tag must be alphanumeric"}' 400; return }
        $tagU = $tag.ToUpper()
        $dup = $script:Clans.Values | Where-Object { $_.tag -eq $tagU }
        if ($dup) { Send-Json $Response '{"error":"tag already taken"}' 400; return }
        $id = $script:ClanNextId++
        $mems = [System.Collections.ArrayList]::new()
        [void]$mems.Add(@{handle=$user.Handle;role='Leader';joinedAt=Now-Stamp;clanCoins=0})
        $script:Clans[$id] = @{id=$id;name=$name;tag=$tagU;founder=$user.Handle;leader=$user.Handle;createdAt=(Get-Date).ToString('o');members=$mems;applications=[System.Collections.ArrayList]::new();treasury=0;isPaid=$false;trades=@{};tradeNextId=1;tradeHistory=[System.Collections.ArrayList]::new();loans=@{};loanNextId=1;rank='Unranked'}
        $user.ClanId = $id
        Send-Json $Response "{`"ok`":true,`"id`":$id}"
    }
    elseif ($path -eq '/api/clan/apply') {
        if (-not $user) { Send-Json $Response '{"error":"login required"}' 401; return }
        if ($user.ClanId -and $user.ClanId -ne 0) { Send-Json $Response '{"error":"already in a clan"}' 400; return }
        $cid = [int]$qs['id']; if (-not $script:Clans.ContainsKey($cid)) { Send-Json $Response '{"error":"clan not found"}' 400; return }
        $cl = $script:Clans[$cid]
        $existing = $cl.applications | Where-Object { $_.handle -eq $user.Handle }
        if ($existing) { Send-Json $Response '{"error":"already applied"}' 400; return }
        [void]$cl.applications.Add(@{handle=$user.Handle;appliedAt=Now-Stamp})
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/applications') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -eq 'Member') { Send-Json $Response '{"error":"officer or leader required"}' 403; return }
        $items = $cl.applications | ForEach-Object { "{`"handle`":`"$($_.handle)`",`"appliedAt`":`"$($_.appliedAt)`"}" }
        Send-Json $Response "{`"applications`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/clan/approve') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -eq 'Member') { Send-Json $Response '{"error":"officer or leader required"}' 403; return }
        $hl = ($qs['handle']).ToLower()
        $app = $cl.applications | Where-Object { $_.handle -eq $hl }
        if (-not $app) { Send-Json $Response '{"error":"no application from that handle"}' 400; return }
        if (-not $script:AuthAccounts.ContainsKey($hl)) { Send-Json $Response '{"error":"account not found"}' 400; return }
        $cl.applications = [System.Collections.ArrayList]@($cl.applications | Where-Object { $_.handle -ne $hl })
        [void]$cl.members.Add(@{handle=$hl;role='Member';joinedAt=Now-Stamp;clanCoins=0})
        $script:AuthAccounts[$hl].ClanId = $cl.id
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/reject') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -eq 'Member') { Send-Json $Response '{"error":"officer or leader required"}' 403; return }
        $hl = ($qs['handle']).ToLower()
        $cl.applications = [System.Collections.ArrayList]@($cl.applications | Where-Object { $_.handle -ne $hl })
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/kick') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -eq 'Member') { Send-Json $Response '{"error":"officer or leader required"}' 403; return }
        $hl = ($qs['handle']).ToLower()
        $target = Get-ClanMember $cl $hl
        if (-not $target) { Send-Json $Response '{"error":"not a member"}' 400; return }
        if ($target.role -eq 'Leader') { Send-Json $Response '{"error":"cannot kick the leader"}' 400; return }
        if ($target.role -eq 'Officer' -and $me.role -ne 'Leader') { Send-Json $Response '{"error":"only leader can kick officers"}' 400; return }
        $cl.members = [System.Collections.ArrayList]@($cl.members | Where-Object { $_.handle -ne $hl })
        if ($script:AuthAccounts.ContainsKey($hl)) { $script:AuthAccounts[$hl].ClanId = 0 }
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/promote') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -ne 'Leader') { Send-Json $Response '{"error":"leader only"}' 403; return }
        $hl = ($qs['handle']).ToLower(); $target = Get-ClanMember $cl $hl
        if (-not $target -or $target.role -ne 'Member') { Send-Json $Response '{"error":"can only promote members"}' 400; return }
        $target.role = 'Officer'
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/demote') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or $me.role -ne 'Leader') { Send-Json $Response '{"error":"leader only"}' 403; return }
        $hl = ($qs['handle']).ToLower(); $target = Get-ClanMember $cl $hl
        if (-not $target -or $target.role -ne 'Officer') { Send-Json $Response '{"error":"can only demote officers"}' 400; return }
        $target.role = 'Member'
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/leave') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me) { $user.ClanId = 0; Send-Json $Response '{"ok":true}'; return }
        $cl.members = [System.Collections.ArrayList]@($cl.members | Where-Object { $_.handle -ne $user.Handle })
        $user.ClanId = 0
        if ($me.role -eq 'Leader') {
            $officers = @($cl.members | Where-Object { $_.role -eq 'Officer' } | Sort-Object { $_.joinedAt })
            $regulars = @($cl.members | Where-Object { $_.role -eq 'Member' } | Sort-Object { $_.joinedAt })
            if ($officers.Count -gt 0) { $officers[0].role = 'Leader'; $cl.leader = $officers[0].handle }
            elseif ($regulars.Count -gt 0) { $regulars[0].role = 'Leader'; $cl.leader = $regulars[0].handle }
            else {
                foreach ($tk in @($cl.trades.Keys)) { $tr = $cl.trades[$tk]; if ($tr.status -eq 'active' -and $tr.escrowCard -and $script:AuthAccounts.ContainsKey($tr.seller)) { $sa=$script:AuthAccounts[$tr.seller]; if(-not $sa.OwnedCards){$sa.OwnedCards=[System.Collections.ArrayList]::new()}; [void]$sa.OwnedCards.Add($tr.escrowCard) } }
                foreach ($lk in @($cl.loans.Keys)) { $ln=$cl.loans[$lk]; if($ln.status -eq 'active' -and $ln.originalCard -and $script:AuthAccounts.ContainsKey($ln.lender)){$la=$script:AuthAccounts[$ln.lender];if(-not $la.OwnedCards){$la.OwnedCards=[System.Collections.ArrayList]::new()};[void]$la.OwnedCards.Add($ln.originalCard);if($script:AuthAccounts.ContainsKey($ln.borrower)){$ba=$script:AuthAccounts[$ln.borrower];for($i=0;$i -lt $ba.OwnedCards.Count;$i++){if($ba.OwnedCards[$i] -is [hashtable] -and $ba.OwnedCards[$i].tokenId -eq $ln.tokenId){$ba.OwnedCards.RemoveAt($i);break}}}}}
                $script:Clans.Remove($cl.id)
            }
        }
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/members') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        $items = $cl.members | ForEach-Object { "{`"handle`":`"$($_.handle)`",`"role`":`"$($_.role)`",`"joinedAt`":`"$($_.joinedAt)`",`"clanCoins`":$($_.clanCoins)}" }
        Send-Json $Response "{`"members`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/clan/deposit') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me) { Send-Json $Response '{"error":"not a member"}' 400; return }
        $amt = [int]$qs['amount']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        if ($user.Balance -lt $amt) { Send-Json $Response '{"error":"insufficient MC"}' 400; return }
        $user.Balance -= $amt; $me.clanCoins += $amt; $cl.treasury += $amt; $cl.isPaid = $true
        Send-Json $Response "{`"ok`":true,`"balance`":$($user.Balance),`"clanCoins`":$($me.clanCoins),`"treasury`":$($cl.treasury)}"
    }
    elseif ($path -eq '/api/clan/withdraw') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me) { Send-Json $Response '{"error":"not a member"}' 400; return }
        if (-not $cl.isPaid) { Send-Json $Response '{"error":"clan is free tier"}' 400; return }
        $amt = [int]$qs['amount']
        if ($amt -lt 1) { Send-Json $Response '{"error":"amount must be positive"}' 400; return }
        if ($me.clanCoins -lt $amt) { Send-Json $Response '{"error":"insufficient clan coins"}' 400; return }
        $mcReturn = [Math]::Floor($amt * 0.7)
        $me.clanCoins -= $amt; $user.Balance += $mcReturn
        Send-Json $Response "{`"ok`":true,`"balance`":$($user.Balance),`"clanCoins`":$($me.clanCoins),`"taxed`":$($amt - $mcReturn),`"received`":$mcReturn}"
    }
    elseif ($path -eq '/api/clan/balance') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"clanCoins":0,"treasury":0}'; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        $cc = if ($me) { $me.clanCoins } else { 0 }
        Send-Json $Response "{`"clanCoins`":$cc,`"treasury`":$($cl.treasury),`"isPaid`":$($cl.isPaid.ToString().ToLower())}"
    }
    elseif ($path -eq '/api/clan/trade/list') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        if (-not $cl.isPaid) { Send-Json $Response '{"error":"paid tier required"}' 400; return }
        $tid = [int]$qs['tokenId']; $price = [int]$qs['price']
        if ($price -lt 1) { Send-Json $Response '{"error":"price must be positive"}' 400; return }
        $cardEntry = $null; $cardIdx = -1
        for ($i = 0; $i -lt $user.OwnedCards.Count; $i++) {
            $c = $user.OwnedCards[$i]; $ctid = if ($c -is [hashtable]) { $c.tokenId } else { $i }
            if ($ctid -eq $tid) { $cardEntry = $c; $cardIdx = $i; break }
        }
        if (-not $cardEntry) { Send-Json $Response '{"error":"card not found"}' 400; return }
        if ($cardEntry -is [hashtable] -and $cardEntry.mintSource -eq 'Borrowed') { Send-Json $Response '{"error":"cannot trade a borrowed card"}' 400; return }
        $cid2 = if ($cardEntry -is [hashtable]) { $cardEntry.cardId } else { [int]$cardEntry }
        $rar = if ($cardEntry -is [hashtable] -and $cardEntry.rarity) { $cardEntry.rarity } else { 'Common' }
        $tmpl = $script:CardIndex[$cid2]; $cname = if ($tmpl) { $tmpl.name } else { "Card #$cid2" }; $ctype = if ($tmpl) { $tmpl.type } else { '?' }
        $trId = $cl.tradeNextId++
        $cl.trades[$trId] = @{id=$trId;seller=$user.Handle;tokenId=$tid;cardId=$cid2;cardName=$cname;cardType=$ctype;rarity=$rar;priceClanCoins=$price;status='active';escrowCard=$cardEntry;createdAt=Now-Stamp}
        $user.OwnedCards.RemoveAt($cardIdx)
        Send-Json $Response "{`"ok`":true,`"tradeId`":$trId}"
    }
    elseif ($path -eq '/api/clan/trade/buy') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]; $me = Get-ClanMember $cl $user.Handle
        if (-not $me -or -not $cl.isPaid) { Send-Json $Response '{"error":"not available"}' 400; return }
        $trId = [int]$qs['tradeId']; if (-not $cl.trades.ContainsKey($trId)) { Send-Json $Response '{"error":"trade not found"}' 400; return }
        $tr = $cl.trades[$trId]
        if ($tr.status -ne 'active') { Send-Json $Response '{"error":"trade not active"}' 400; return }
        if ($tr.seller -eq $user.Handle) { Send-Json $Response '{"error":"cannot buy your own"}' 400; return }
        if ($me.clanCoins -lt $tr.priceClanCoins) { Send-Json $Response '{"error":"insufficient clan coins"}' 400; return }
        $seller = Get-ClanMember $cl $tr.seller
        $me.clanCoins -= $tr.priceClanCoins
        if ($seller) { $seller.clanCoins += $tr.priceClanCoins }
        $buyerCard = @{cardId=$tr.cardId;rarity=$tr.rarity;mintSource='ClanTrade';tokenId=$script:NextTokenId++}
        if (-not $user.OwnedCards) { $user.OwnedCards = [System.Collections.ArrayList]::new() }
        [void]$user.OwnedCards.Add($buyerCard)
        $tr.status = 'sold'
        if (-not $cl.tradeHistory) { $cl.tradeHistory = [System.Collections.ArrayList]::new() }
        [void]$cl.tradeHistory.Add(@{cardName=$tr.cardName;price=$tr.priceClanCoins;buyer=$user.Handle;seller=$tr.seller;time=Now-Stamp})
        Send-Json $Response "{`"ok`":true,`"clanCoins`":$($me.clanCoins)}"
    }
    elseif ($path -eq '/api/clan/trade/cancel') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        $trId = [int]$qs['tradeId']; if (-not $cl.trades.ContainsKey($trId)) { Send-Json $Response '{"error":"trade not found"}' 400; return }
        $tr = $cl.trades[$trId]
        if ($tr.seller -ne $user.Handle) { Send-Json $Response '{"error":"not your listing"}' 403; return }
        if ($tr.escrowCard) { if (-not $user.OwnedCards) { $user.OwnedCards = [System.Collections.ArrayList]::new() }; [void]$user.OwnedCards.Add($tr.escrowCard) }
        $tr.status = 'cancelled'
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/trade/browse') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        if (-not $cl.isPaid) { Send-Json $Response '{"trades":[]}'; return }
        $active = @($cl.trades.Values | Where-Object { $_.status -eq 'active' })
        $items = $active | ForEach-Object { "{`"id`":$($_.id),`"seller`":`"$($_.seller)`",`"cardName`":`"$($_.cardName)`",`"cardType`":`"$($_.cardType)`",`"rarity`":`"$($_.rarity)`",`"price`":$($_.priceClanCoins)}" }
        Send-Json $Response "{`"trades`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/clan/trade/history') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"history":[]}'; return }
        $cl = $script:Clans[$user.ClanId]
        $items = @(); if ($cl.tradeHistory) { $items = @($cl.tradeHistory | Select-Object -Last 20 | ForEach-Object { "{`"cardName`":`"$($_.cardName)`",`"price`":$($_.price),`"buyer`":`"$($_.buyer)`",`"seller`":`"$($_.seller)`",`"time`":`"$($_.time)`"}" }) }
        Send-Json $Response "{`"history`":[$($items -join ',')]}"
    }
    elseif ($path -eq '/api/clan/loan/lend') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        if (-not $cl.isPaid) { Send-Json $Response '{"error":"paid tier required"}' 400; return }
        $tid = [int]$qs['tokenId']; $borrower = ($qs['borrower']).ToLower(); $hours = [int]$qs['hours']
        if ($hours -lt 1 -or $hours -gt 168) { Send-Json $Response '{"error":"duration must be 1-168 hours"}' 400; return }
        $bm = Get-ClanMember $cl $borrower
        if (-not $bm) { Send-Json $Response '{"error":"borrower not in clan"}' 400; return }
        if ($borrower -eq $user.Handle) { Send-Json $Response '{"error":"cannot lend to yourself"}' 400; return }
        $cardEntry = $null; $cardIdx = -1
        for ($i = 0; $i -lt $user.OwnedCards.Count; $i++) {
            $c = $user.OwnedCards[$i]; $ctid = if ($c -is [hashtable]) { $c.tokenId } else { $i }
            if ($ctid -eq $tid) { $cardEntry = $c; $cardIdx = $i; break }
        }
        if (-not $cardEntry) { Send-Json $Response '{"error":"card not found"}' 400; return }
        if ($cardEntry -is [hashtable] -and $cardEntry.mintSource -eq 'Borrowed') { Send-Json $Response '{"error":"cannot lend a borrowed card"}' 400; return }
        $cid2 = if ($cardEntry -is [hashtable]) { $cardEntry.cardId } else { [int]$cardEntry }
        $rar = if ($cardEntry -is [hashtable] -and $cardEntry.rarity) { $cardEntry.rarity } else { 'Common' }
        $tmpl = $script:CardIndex[$cid2]; $cname = if ($tmpl) { $tmpl.name } else { "Card #$cid2" }
        $borrowedCard = @{cardId=$cid2;rarity=$rar;mintSource='Borrowed';tokenId=$script:NextTokenId++}
        $loanId = $cl.loanNextId++
        $cl.loans[$loanId] = @{id=$loanId;lender=$user.Handle;borrower=$borrower;tokenId=$borrowedCard.tokenId;cardId=$cid2;cardName=$cname;rarity=$rar;lentAt=(Get-Date).ToString('o');expiresAt=(Get-Date).AddHours($hours).ToString('o');status='active';originalCard=$cardEntry}
        $user.OwnedCards.RemoveAt($cardIdx)
        $ba = $script:AuthAccounts[$borrower]
        if (-not $ba.OwnedCards) { $ba.OwnedCards = [System.Collections.ArrayList]::new() }
        [void]$ba.OwnedCards.Add($borrowedCard)
        Send-Json $Response "{`"ok`":true,`"loanId`":$loanId}"
    }
    elseif ($path -eq '/api/clan/loan/return') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"error":"not in a clan"}' 400; return }
        $cl = $script:Clans[$user.ClanId]
        $loanId = [int]$qs['loanId']; if (-not $cl.loans.ContainsKey($loanId)) { Send-Json $Response '{"error":"loan not found"}' 400; return }
        $loan = $cl.loans[$loanId]
        if ($loan.status -ne 'active') { Send-Json $Response '{"error":"loan not active"}' 400; return }
        if ($loan.borrower -ne $user.Handle -and $loan.lender -ne $user.Handle) { Send-Json $Response '{"error":"not your loan"}' 403; return }
        for ($i = 0; $i -lt $user.OwnedCards.Count; $i++) {
            $c = $user.OwnedCards[$i]
            if ($c -is [hashtable] -and $c.tokenId -eq $loan.tokenId) { $user.OwnedCards.RemoveAt($i); break }
        }
        if ($loan.borrower -ne $user.Handle -and $script:AuthAccounts.ContainsKey($loan.borrower)) {
            $ba = $script:AuthAccounts[$loan.borrower]
            for ($i = 0; $i -lt $ba.OwnedCards.Count; $i++) { if ($ba.OwnedCards[$i] -is [hashtable] -and $ba.OwnedCards[$i].tokenId -eq $loan.tokenId) { $ba.OwnedCards.RemoveAt($i); break } }
        }
        if ($loan.originalCard -and $script:AuthAccounts.ContainsKey($loan.lender)) {
            $la = $script:AuthAccounts[$loan.lender]; if (-not $la.OwnedCards) { $la.OwnedCards = [System.Collections.ArrayList]::new() }; [void]$la.OwnedCards.Add($loan.originalCard)
        }
        $loan.status = 'returned'
        Send-Json $Response '{"ok":true}'
    }
    elseif ($path -eq '/api/clan/loan/active') {
        if (-not $user -or -not $user.ClanId -or $user.ClanId -eq 0) { Send-Json $Response '{"loans":[]}'; return }
        $cl = $script:Clans[$user.ClanId]; Expire-ClanLoans $cl
        $mine = @($cl.loans.Values | Where-Object { $_.status -eq 'active' -and ($_.lender -eq $user.Handle -or $_.borrower -eq $user.Handle) })
        $items = $mine | ForEach-Object { $tl = try { $r = ([datetime]::Parse($_.expiresAt) - (Get-Date)); if ($r.TotalSeconds -gt 0) { '{0}h {1}m' -f [int]$r.TotalHours, $r.Minutes } else { 'expired' } } catch { '--' }; "{`"id`":$($_.id),`"lender`":`"$($_.lender)`",`"borrower`":`"$($_.borrower)`",`"cardName`":`"$($_.cardName)`",`"rarity`":`"$($_.rarity)`",`"timeLeft`":`"$tl`"}" }
        Send-Json $Response "{`"loans`":[$($items -join ',')]}"
    }
    else { Send-Json $Response '{"error":"unknown clan endpoint"}' 404 }
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
            if ($path -eq '/' -or $path -eq '/index') { $resp.Redirect('/welcome.html'); $resp.StatusCode = 302 }
            elseif ($path -match '^\/([\w.-]+\.(html|css|js|png|jpg|svg|wav|mp3|codex))$') { Send-StaticFile -Response $resp -FilePath (Join-Path $WebDir $matches[1]) }
            elseif ($path -like '/api/*') {
                # Try CDX first for all API calls
                $cdxPath = $path -replace '^/api/auth/', '/api/magic/auth/' -replace '^/api/admin/', '/api/magic/admin/' -replace '^/api/market/', '/api/magic/market/' -replace '^/api/clan/', '/api/magic/clan/'
                if ($path -like '/api/magic/*') { $cdxPath = $path }
                $cdxQuery = $ctx.Request.Url.Query
                $cdxResult = Invoke-CdxApi ($cdxPath + $cdxQuery)
                if ($cdxResult -and $cdxResult -ne '' -and -not $cdxResult.StartsWith('{"error":"not found"}')) {
                    Send-Json $resp $cdxResult
                    if ($path -match 'register|login|logout|crack|buy|grant|mint|burn|subscribe|wipe|change|forget|create|cancel|bid|buyout|deposit|withdraw|promote|demote|kick|approve|reject|leave|lend|return') { Save-CdxState }
                } else {
                    # Fallback to PS1 handlers
                    if ($path -like '/api/auth/*') { Handle-Auth -Context $ctx -Response $resp; if ($path -in @('/api/auth/register','/api/auth/crack-pack','/api/auth/subscribe','/api/auth/buy-store-item','/api/auth/buy-coin','/api/auth/change-password','/api/auth/forget-me','/api/auth/wipe-collection')) { Save-State } }
                    elseif ($path -like '/api/admin/*') { Handle-Admin -Context $ctx -Response $resp; if ($path -ne '/api/admin/check' -and $path -ne '/api/admin/stats' -and $path -ne '/api/admin/users' -and $path -ne '/api/admin/store-items' -and $path -ne '/api/admin/clans' -and $path -ne '/api/admin/blockchain' -and $path -ne '/api/admin/mint-log') { Save-State } }
                    elseif ($path -like '/api/market/*') { Handle-Market -Context $ctx -Response $resp; if ($path -ne '/api/market/balance' -and $path -ne '/api/market/listings' -and $path -ne '/api/market/my-listings' -and $path -ne '/api/market/my-cards' -and $path -ne '/api/market/history') { Save-State } }
                    elseif ($path -like '/api/clan/*') { Handle-Clan -Context $ctx -Response $resp; if ($path -notin @('/api/clan/info','/api/clan/search','/api/clan/members','/api/clan/balance','/api/clan/applications','/api/clan/trade/browse','/api/clan/trade/history','/api/clan/loan/active')) { Save-State } }
                    else { Send-Json $resp '{"error":"unknown endpoint"}' 404 }
                }
            }
            else { $resp.StatusCode=404;$buf=[System.Text.Encoding]::UTF8.GetBytes('<html><body style="background:#0d1117;color:#8b949e;font-family:monospace;padding:48px;text-align:center"><h1>404</h1><a href="/" style="color:#58a6ff">Back</a></body></html>');$resp.ContentType='text/html; charset=utf-8';$resp.ContentLength64=$buf.Length;$resp.OutputStream.Write($buf,0,$buf.Length) }
        } catch { Write-Host "  ERROR: $_ at $($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red; $resp.StatusCode = 500 } finally { $resp.Close() }
    }
} finally { Save-CdxState; Save-State; Cleanup-Resources; Write-Host "Server stopped. State saved." -ForegroundColor Yellow }
