[CmdletBinding()]
param([string]$Server = (Join-Path $PSScriptRoot '../web/server.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Server).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Server source does not parse' }
$definitions = $ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.FunctionDefinitionAst] }
. ([ScriptBlock]::Create(($definitions.Extent.Text -join "`n")))
$wireWriter = (Get-Command Send-Json).ScriptBlock
function Send-Json { param($Response, [string]$Json, [int]$Status = 200); $script:Captured = $Json; $script:CapturedStatus = $Status }
function Save-State {}
function Invoke-CdxApi { param($Path); return $null }

function Check { param([bool]$Condition, [string]$Label); if (-not $Condition) { throw $Label } }
function Check-Text { param($Value, $Expected, [string]$Label); Check ([string]::Equals($Value, $Expected, [StringComparison]::Ordinal)) $Label }
function Check-Array { param($Value, [int]$Count, [string]$Label); Check ($Value -is [array] -and $Value.Count -eq $Count) $Label }
function Check-Number { param($Value, $Expected, [string]$Label); Check (($Value -is [long] -or $Value -is [int] -or $Value -is [double] -or $Value -is [decimal]) -and $Value -eq $Expected) $Label }
function Request-Json {
    param([string]$Handler, [string]$Path, [hashtable]$Query = @{})
    $pairs = foreach ($key in $Query.Keys) { [Uri]::EscapeDataString($key) + '=' + [Uri]::EscapeDataString([string]$Query[$key]) }
    $context = [pscustomobject]@{ Request = [pscustomobject]@{ Url = [uri]('http://localhost' + $Path + '?' + ($pairs -join '&')) } }
    $script:Captured = $null
    & $Handler $context $null
    Check ($null -ne $script:Captured) ("No response: " + $Path)
    try { $doc = [Text.Json.JsonDocument]::Parse($script:Captured); $doc.Dispose() }
    catch { throw ("Invalid JSON response: " + $Path + ': ' + $_.Exception.Message) }
    ConvertFrom-Json -InputObject $script:Captured -AsHashtable -Depth 100
}

$payload = 'quote" slash\ ' + (-join (0..31 | ForEach-Object { [char]$_ })) + ' café À Ω 中'
$script:AuthAccounts = @{}
$script:AuthSessions = @{ session = 'tester' }
$script:Clans = @{}
$script:Listings = @{}
$script:StoreItems = @{}
$script:TotalVolume = 0
$script:MintLog = @()
$script:TradeHistory = @()
$script:AuthNextId = 1
$user = @{ Id=0; Handle='tester'; Display=$payload; Password='DummyPass7'; PwScore=3; Tfa=$false; Admin=$true; Banned=$false; Balance=500; OwnedCards=@(); Wins=0; Losses=0; Rating=1000; Subscription='Free'; ClanId=0; Roles=@(); Decks=@{} }
$script:AuthAccounts.tester = $user
$me = Request-Json Handle-Auth /api/auth/me @{t='session'}
Check-Text $me.display $payload 'Auth display changed text'
Check ($me.admin -is [bool] -and $me.admin) 'Auth admin changed type'
Check-Number $me.id 0 'Auth id changed number'
$login = Request-Json Handle-Auth /api/auth/login @{u='tester';p='DummyPass7'}
Check-Text $login.display $payload 'Login display changed text'
'PASS host auth strings and typed fields'

foreach ($n in @(0,1,2)) {
    $user.Roles = @(for($i=0;$i -lt $n;$i++){ $payload })
    $profile = Request-Json Handle-Auth /api/auth/profile @{t='session'}
    Check-Array $profile.roles $n 'Profile roles lost array shape'
    Check-Number $profile.balance 500 'Profile balance changed number'
    foreach($role in $profile.roles){ Check-Text $role $payload 'Role changed text' }
    $admin = Request-Json Handle-Admin /api/admin/users @{t='session'}
    Check-Array $admin.users 1 'Admin users lost singleton array'
    Check-Array $admin.users[0].roles $n 'Admin roles lost array shape'
    Check-Text $admin.users[0].display $payload 'Admin display changed text'

    $script:CardPool = @(for($i=0;$i -lt $n;$i++) {
        $card = if($i -eq 0){New-Gem $i $payload 'Red' $payload 9 $true $null}else{New-Equipment $i $payload $payload 0 0 0 0 0 0 0 $payload 0 0 0 $payload $true}
        $card.keywords=$payload
        if($i -gt 0){$card.spellSpeed=$payload;$card.fluorClause=$payload}
        $card
    })
    $pool = Request-Json Handle-Auth /api/auth/pool
    Check-Array $pool.cards $n 'Pool lost array shape'
    foreach($card in $pool.cards) {
        Check-Text $card.name $payload 'Pool name changed text'
        Check-Text $card.keywords $payload 'Pool keywords changed text'
        Check ($card.cost -is [System.Collections.IDictionary] -and $card.color -is [System.Collections.IDictionary]) 'Pool cost/color changed object type'
        Check ($card.ContainsKey('spellSpeed') -and $card.ContainsKey('fluorClause')) 'Pool nullable fields disappeared'
        if($card.id -eq 0){Check ($null -eq $card.spellSpeed -and $null -eq $card.fluorClause) 'Pool null fields changed'}
        else {Check-Text $card.spellSpeed $payload 'Spell speed changed text';Check-Text $card.fluorClause $payload 'Fluor clause changed text'}
        Check-Number $card.power 0 'Pool power changed number'
        Check-Number $card.baseFocus 0 'Pool focus changed number'
        Check ($card.isBasic -is [bool]) 'Pool basic flag changed type'
        if($card.type -eq 'Gemstone'){Check-Text $card.variety $payload 'Gem variety changed text'}
        if($card.type -eq 'Equipment'){Check-Text $card.slot $payload 'Equipment slot changed text';Check ($card.socketEmpty -is [bool]) 'Socket flag changed type'}
    }

    $user.Decks=@{}
    for($i=0;$i -lt $n;$i++) {
        $name=$payload+[string]$i
        $user.Decks[$name]=@{cards=@(0);thumbprint=$payload;created=$payload;modified=$payload;versions=@()}
    }
    $decks=Request-Json Handle-Auth /api/auth/decks @{t='session'}
    Check-Array $decks.decks $n 'Deck list lost array shape'
    foreach($deck in $decks.decks){Check ($user.Decks.ContainsKey($deck.name)) 'Deck name changed text';Check-Text $deck.thumbprint $payload 'Deck thumbprint changed text'}

    $script:Listings=@{}
    $script:StoreItems=@{}
    $script:MintLog=@(for($i=0;$i -lt $n;$i++){@{type=$payload;handle=$payload;amount=1;reason=$payload;time=$payload}})
    for($i=0;$i -lt $n;$i++) {
        $script:Listings[$i]=@{id=$i;status='active';type='sale';cardName=$payload;cardType=$payload;rarity=$payload;price=1;minBid=0;currentBid=0;buyout=0;bidCount=0;seller=$payload;endsAt=$null}
        $script:StoreItems[$i]=@{id=$i;name=$payload;type=$payload;price=1;qty=1;discount=1;saleLabel=$payload;sold=0;cardId=1;availableFrom='';availableTo='';discountFrom='';discountTo=''}
    }
    $market=Request-Json Handle-Market /api/market/listings
    Check-Array $market.listings $n 'Market list lost array shape'
    foreach($listing in $market.listings){Check-Text $listing.cardName $payload 'Market card changed text';Check-Text $listing.seller $payload 'Market seller changed text';Check-Number $listing.price 1 'Market price changed number'}
    foreach($route in @('/api/auth/store-items','/api/admin/store-items')) {
        $handler=if($route.StartsWith('/api/admin')){'Handle-Admin'}else{'Handle-Auth'}
        $store=Request-Json $handler $route @{t='session'}
        Check-Array $store.items $n 'Store list lost array shape'
        foreach($item in $store.items){Check-Text $item.name $payload 'Store name changed text';Check-Text $item.saleLabel $payload 'Sale label changed text'}
    }
    $mint=Request-Json Handle-Admin /api/admin/mint-log @{t='session'}
    Check-Array $mint.entries $n 'Mint log lost array shape'
    foreach($entry in $mint.entries){Check-Text $entry.reason $payload 'Mint reason changed text'}
}
'PASS empty singleton and multiple response arrays'

$user.Decks=@{}
$saved=Request-Json Handle-Auth /api/auth/save-deck @{t='session';name=$payload;cards='0,1'}
Check-Text $saved.name $payload 'Save-deck name changed text'
$marked=Request-Json Handle-Auth /api/auth/mark-deck-version @{t='session';name=$payload;label=$payload}
Check-Text $marked.label $payload 'Version label changed text'
$loaded=Request-Json Handle-Auth /api/auth/load-deck @{t='session';name=$payload}
Check-Text $loaded.name $payload 'Load-deck name changed text'
Check-Array $loaded.cards 2 'Deck cards lost array shape'
Check-Number $loaded.cards[0] 0 'First deck card changed number'
Check-Number $loaded.cards[1] 1 'Second deck card changed number'
Check-Array $loaded.versions 1 'Deck versions lost array shape'
Check-Text $loaded.versions[0].label $payload 'Loaded version changed text'
$version=Request-Json Handle-Auth /api/auth/load-deck @{t='session';name=$payload;version='1'}
Check-Text $version.label $payload 'Version response changed text'
'PASS deck save and version response fragments'

$script:Clans[1]=@{id=1;name=$payload;tag=$payload;leader=$payload;founder=$payload;members=@();applications=@();treasury=0;isPaid=$false;trades=@{};loans=@{};rank=$payload;createdAt=$payload}
$clan=Request-Json Handle-Clan /api/clan/info @{id='1'}
foreach($key in @('name','tag','leader','founder','rank','createdAt')){Check-Text $clan[$key] $payload ('Clan field changed: '+$key)}
$search=Request-Json Handle-Clan /api/clan/search @{q='quote'}
Check-Array $search.clans 1 'Clan search lost array shape'
Check-Text $search.clans[0].name $payload 'Clan search name changed text'
'PASS clan JSON strings'

foreach($value in @('', $payload, 'plain', '"', '\')) {
    $encoded=Json-String $value
    $doc=[Text.Json.JsonDocument]::Parse($encoded)
    Check-Text ($doc.RootElement.GetString()) $value 'Platform quote helper changed text'
    $doc.Dispose()
}
$json='{"text":'+(Json-String $payload)+'}'
$response=[pscustomobject]@{StatusCode=0;ContentType='';Headers=[Collections.Specialized.NameValueCollection]::new();ContentLength64=0L;OutputStream=[IO.MemoryStream]::new()}
& $wireWriter $response $json 200
$bytes=$response.OutputStream.ToArray()
Check ($response.ContentLength64 -eq $bytes.Length) 'UTF-8 response length differs'
Check-Text ([Text.UTF8Encoding]::new($false,$true).GetString($bytes)) $json 'UTF-8 response changed text'
$response.OutputStream.Dispose()
'PASS all 32 control characters quote backslash non-ASCII and UTF-8 wire'
