[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LoginServer,
    [ValidateRange(1024,65535)][int]$ListenPort=2630,
    [string]$GameServer='',
    [string]$LogDir='',
    [string]$Settings='',
    [switch]$NoWindow
)
# UOAIX Assist (assist-plan.md): the relay between a 1.25.32 client and its server. The client's
# LOGIN.CFG names 127.0.0.1,<ListenPort>; -LoginServer is the real login server as LOGIN.CFG writes it
# ("login.uo1998.com,5000", or "127.0.0.1,2593" for the UOAIX shard).
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$uoaix=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $uoaix '../..'))
$parts=$LoginServer -split ','
if($parts.Count -ne 2 -or -not $parts[0] -or $parts[1] -notmatch '^\d+$'){throw "LoginServer must be host,port: $LoginServer"}
$loginHost=$parts[0].Trim();$loginPort=[int]$parts[1]
if($loginPort -eq $ListenPort -and $loginHost -in @('127.0.0.1','localhost')){throw 'The relay cannot listen on the login server''s own port'}
# -GameServer host,port replaces the address every 0x8C names: a UOAIX shard copy on a forwarded port names its guest
# port (2593, where the live shard's proxy listens), so a test shard is run as -LoginServer and -GameServer 127.0.0.1,<its port>.
$gameHost=$null;$gamePort=0
if($GameServer){$g=$GameServer -split ',';if($g.Count -ne 2 -or -not $g[0] -or $g[1] -notmatch '^\d+$'){throw "GameServer must be host,port: $GameServer"};$gameHost=$g[0].Trim();$gamePort=[int]$g[1]}
foreach($p in $ListenPort,($ListenPort+1)){if(Get-NetTCPConnection -State Listen -LocalPort $p -ErrorAction SilentlyContinue){throw "Port $p is owned (the relay and its control port); nothing was stopped"}}
if(-not $LogDir){$LogDir=Join-Path $repo 'build-output/uoaix/assist'}
$LogDir=[IO.Path]::GetFullPath($LogDir)
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (@('Assist.cs','GameLink.cs','AssistSettings.cs','AssistGuard.cs','LoopKind.cs','AdviceRow.cs','AssistFeatures.cs','AssistFeatures.Walking.cs','AssistFeatures.TailoringCircuit.cs','AssistFeatures.CircuitTrips.cs','AssistFeatures.BankApproach.cs','AssistFeatures.CircuitShops.cs','AssistFeatures.TourAndBuy.cs','AssistFeatures.Advertiser.cs','AssistFeatures.SpellbookAndRunes.cs','AssistFeatures.MiningCircuit.cs','AssistFeatures.Tools.cs','AssistFeatures.Circuit.cs','AssistFeatures.LumberjackCircuit.cs','AssistFeatures.FeatherCircuit.cs','AssistFeatures.ReagentCircuit.cs','AssistFeatures.VendorTab.cs','AssistFeatures.Houses.cs','AssistFeatures.SmithingCircuit.cs','AssistFeatures.CircuitBrain.cs','AssistFeatures.TailoringShops.cs','AssistFeatures.Bank.cs','AssistFeatures.CircuitSteps.cs','AssistFeatures.Loops.cs','AssistFeatures.Crafts.cs','AssistFeatures.ArmAndLadder.cs','AssistFeatures.HousePlacement.cs','AssistFeatures.Loadout.cs','AssistFeatures.Harvest.cs','AssistFeatures.Trips.cs','AssistFeatures.People.cs','AssistTimings.cs','Collections.cs','Outcome.cs','Wait.cs','Heard.cs','ActionScope.cs','Hands.cs','TaskContract.cs','Clock.cs','BytePipe.cs','RelayLog.cs','WireTables.cs','KeepRules.cs','MinePlanner.cs','Collection.cs','RuneChoice.cs','VendorWares.cs','VendorBook.cs','Gather.cs','LumberGather.cs','FeatherGather.cs','MineGather.cs','GatherOrder.cs','Inventory.cs','Tools.cs','Stash.cs','AssistState.cs','AssistWindow.cs','AssistWindow.Info.cs','AssistWindow.Actions.cs','AssistWindow.General.cs','AssistWindow.Skills.cs','AssistWindow.Map.cs','AssistWindow.Shops.cs','AssistWindow.Vendors.cs','AssistWindow.Maker.cs','AssistWindow.Bags.cs','AssistWindow.Houses.cs','AssistWindow.Circuits.cs','AssistNative.cs','UoSounds.cs','ItemPicker.cs','UoFoliage.cs','UoTex.cs','TitleBars.cs','UoMap.cs','UoRegions.cs','FieldGrid.cs','SortList.cs','MapView.cs','Cached.cs','UoArt.cs','UoGump.cs','UoAnim.cs','UoTiles.cs','UoRadar.cs','AssistAtlas.cs','AssistTrips.cs','UoWire.cs','UoWeapons.cs','UoBestiary.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}) -ReferencedAssemblies System.Runtime,System.Collections,System.Collections.Immutable,System.Collections.Concurrent,System.Collections.NonGeneric,System.Console,System.Net.Primitives,System.Net.Sockets,System.Threading,System.Threading.Thread,System.Threading.ThreadPool,System.Text.Encoding.Extensions,System.Runtime.InteropServices,System.ComponentModel,System.ComponentModel.Primitives,System.Drawing.Primitives,System.Drawing,System.Drawing.Common,System.Private.Windows.GdiPlus,System.Private.Windows.Core,System.Windows.Forms,System.Windows.Forms.Primitives
# A relay on another port than 2630 keeps its own settings file, so two relays never rewrite each other's: the first
# start copies the 2630 relay's choices (window, keys, lists), less its fail-safe state.
if(-not $Settings -and $ListenPort -ne 2630){
    $base=[Uoaix.Assist.AssistRelay]::settingsPath;$Settings=Join-Path (Split-Path $base) "settings-$ListenPort.txt"
    if(-not (Test-Path -LiteralPath $Settings) -and (Test-Path -LiteralPath $base)){Get-Content -LiteralPath $base | Where-Object {$_ -notmatch '^guard\.disabled='} | Set-Content -LiteralPath $Settings}
}
if($Settings){[Uoaix.Assist.AssistRelay]::settingsPath=[IO.Path]::GetFullPath($Settings)}
[Uoaix.Assist.AssistRelay]::SourceDir=$PSScriptRoot
$wire=[Uoaix.Assist.WireTables]::Load($uoaix)
$relay=[Uoaix.Assist.AssistRelay]::new($ListenPort,$loginHost,$loginPort,$gameHost,$gamePort,[int[]]@($wire.Lengths),[int[]]@($wire.Book),$LogDir)
if(-not $NoWindow){[Uoaix.Assist.AssistWindow]::Start($relay)}
Write-Output "ASSIST listening on 127.0.0.1:$ListenPort -> ${loginHost}:$loginPort log=$($relay.LogPath)"
$relay.Run()
