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
$book=[regex]::Match([IO.File]::ReadAllText((Join-Path $uoaix 'GameHuffman.codex')),'(?s)gh-codebook : List Integer = \[(.*?)\]').Groups[1].Value
$pairs=[int[]]@([regex]::Matches($book,'#([0-9A-Fa-f]+)')|ForEach-Object{[Convert]::ToInt32($_.Groups[1].Value,16)})
$table=[regex]::Match([IO.File]::ReadAllText((Join-Path $uoaix 'GameOpcode.codex')),'(?s)go-lengths : List Integer = \[(.*?)\]').Groups[1].Value
$lengths=[int[]]@([regex]::Matches($table,'\d+')|ForEach-Object{[int]$_.Value})
if($lengths.Count -ne 180){throw "go-lengths has $($lengths.Count) entries, not 180 (opcodes 00 to B3)"}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (@('Assist.cs','GameLink.cs','AssistFeatures.cs','AssistState.cs','AssistWindow.cs','AssistAtlas.cs','AssistTrips.cs','UoWire.cs','UoWeapons.cs','UoBestiary.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}) -ReferencedAssemblies System.Runtime,System.Collections,System.Collections.Concurrent,System.Collections.NonGeneric,System.Console,System.Net.Primitives,System.Net.Sockets,System.Threading,System.Threading.Thread,System.Threading.ThreadPool,System.Text.Encoding.Extensions,System.Runtime.InteropServices,System.ComponentModel,System.ComponentModel.Primitives,System.Drawing.Primitives,System.Drawing,System.Drawing.Common,System.Private.Windows.GdiPlus,System.Private.Windows.Core,System.Windows.Forms,System.Windows.Forms.Primitives
# A relay on another port than 2630 keeps its own settings file, so two relays never rewrite each other's: the first
# start copies the 2630 relay's choices (window, keys, lists), less its fail-safe state.
if(-not $Settings -and $ListenPort -ne 2630){
    $base=[AssistRelay]::settingsPath;$Settings=Join-Path (Split-Path $base) "settings-$ListenPort.txt"
    if(-not (Test-Path -LiteralPath $Settings) -and (Test-Path -LiteralPath $base)){Get-Content -LiteralPath $base | Where-Object {$_ -notmatch '^guard\.disabled='} | Set-Content -LiteralPath $Settings}
}
if($Settings){[AssistRelay]::settingsPath=[IO.Path]::GetFullPath($Settings)}
$relay=[AssistRelay]::new($ListenPort,$loginHost,$loginPort,$gameHost,$gamePort,$lengths,$pairs,$LogDir)
if(-not $NoWindow){[AssistWindow]::Start($relay)}
Write-Output "ASSIST listening on 127.0.0.1:$ListenPort -> ${loginHost}:$loginPort log=$($relay.LogPath)"
$relay.Run()
