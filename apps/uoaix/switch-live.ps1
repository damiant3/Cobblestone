[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$World,[Parameter(Mandatory)][string]$OutDir,
    [string]$Running='',[string]$BackupDir='',[switch]$Fresh,[string]$ClientRoot='',[string]$DecorationFile='',[string]$FloraFile='',
    [ValidateRange(1024,65535)][int]$Port=2593,[ValidateRange(1024,65535)][int]$AdminPort=2594,
    [ValidateRange(10,600)][int]$StopSeconds=120,[ValidateRange(30,900)][int]$ServeSeconds=300,[ValidateRange(10,120)][int]$StartupSeconds=120,[ValidateRange(1,100)][int]$ClockScale=1,
    [switch]$Bake,[string]$KeyFile='',[string[]]$PlayerClients=@('D:\Projects\uoaix-client-flora','D:\Projects\uoaix-client2'))
# Switches the live testing shard to a new server artifact: stops the supervisor at -Running (its OutDir) through its
# stop file, backs up every world disk being left or reused, then launches supervise-composite-game.ps1 -Testing -Admin
# detached on -World into the new -OutDir. -Fresh installs the map cache on -World, which must not exist yet.
# A disk a codex-vm process names is never copied (L-HOTCOPY): the switch refuses before it touches anything.
# -Bake (UoaixDecorator.md) adds the decorator bake: before the stop, bake-decor.ps1 exports the marked items through
# the running shard's admin port; after the world backup, install-map-cache.ps1 folds decor.cfg and land.cfg into
# -World, and every -PlayerClients directory gets the same STAIDX0, STATICS0 and MAP0 (decor-statics.ps1 over a fresh
# transform-flora.ps1 output, decor-land.ps1 over -ClientRoot), its own three files backed up first. A failure after
# the stop leaves the shard down with its world and clients backed up.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Artifact=(Resolve-Path -LiteralPath $Artifact).Path
$World=[IO.Path]::GetFullPath($World)
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
if($Fresh){
    if(Test-Path -LiteralPath $World){throw '-Fresh installs a new world disk; -World already exists'}
    if(-not ($ClientRoot -and $DecorationFile -and $FloraFile)){throw '-Fresh needs -ClientRoot, -DecorationFile and -FloraFile'}
    foreach($p in @($ClientRoot,$DecorationFile,$FloraFile)){if(-not (Test-Path -LiteralPath $p)){throw "Missing: $p"}}
}elseif(-not (Test-Path -LiteralPath $World)){throw "No world disk at $World; pass -Fresh to install one"}
if($Bake){
    if($Fresh -or -not $Running){throw '-Bake bakes a running shard into its own world: it needs -Running and no -Fresh'}
    if(-not ($KeyFile -and $ClientRoot -and $DecorationFile -and $FloraFile)){throw '-Bake needs -KeyFile, -ClientRoot, -DecorationFile and -FloraFile'}
    foreach($p in @($KeyFile,$ClientRoot,$DecorationFile,$FloraFile)+$PlayerClients){if(-not (Test-Path -LiteralPath $p)){throw "Missing: $p"}}
    if($PlayerClients.Count -lt 1){throw '-Bake needs at least one player client'}
}
if(-not $BackupDir){$BackupDir=Join-Path (Split-Path $World) 'world-backups'}
$BackupDir=[IO.Path]::GetFullPath($BackupDir)
# transform-flora.ps1 refuses an OutDir inside the repo (ruling R1), so the bake's client files never land there.
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bakeRoot=$BackupDir
if($bakeRoot.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $bakeRoot -eq $repo){$bakeRoot=Join-Path $env:LOCALAPPDATA 'uoaix-bake'}
function Get-Holders([string]$Disk){
    $needle=$Disk.Replace('\','/')
    @(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('codex-vm.exe','candidate.exe') -and $_.CommandLine -and
        $_.CommandLine.Replace('\','/').Contains($needle,[StringComparison]::OrdinalIgnoreCase)})
}
function Read-State([string]$Dir){
    $path=Join-Path $Dir 'supervisor.json'
    if(-not (Test-Path -LiteralPath $path)){return $null}
    for($i=0;$i -lt 10;$i++){try{return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)}catch{Start-Sleep -Milliseconds 200}}
    throw "Unreadable $path"
}
# The disks this switch leaves or reuses: the running shard's own (from its latest run.json) and -World.
$leaving=[Collections.Generic.List[string]]::new()
if(-not $Fresh){$leaving.Add($World)}
if($Running){
    $Running=(Resolve-Path -LiteralPath $Running).Path
    $s=Read-State $Running;if(-not $s){throw "No supervisor.json in $Running"}
    $runs=@(Get-ChildItem -LiteralPath $Running -Directory -Filter 'run-*' | Sort-Object {[int]($_.Name.Substring(4))})
    if($runs.Count -gt 0){
        $rj=Join-Path $runs[-1].FullName 'run.json'
        if(Test-Path -LiteralPath $rj){$d=[IO.Path]::GetFullPath((Get-Content -LiteralPath $rj -Raw | ConvertFrom-Json).stateFile);if(-not $leaving.Contains($d)){$leaving.Add($d)}}
    }
    if($Bake){
        if($s.state -ne 'serving'){throw "-Bake exports from a serving shard; $Running is $($s.state)"}
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'bake-decor.ps1') -Port $AdminPort -KeyFile $KeyFile -ClientRoot $ClientRoot
        if($LASTEXITCODE -ne 0){throw 'Decorator export failed; nothing was stopped'}
    }
    if($s.state -ne 'stopped'){
        "STOP supervisor PID=$($s.supervisor) guest=$($s.guest) state=$($s.state)"
        [IO.File]::WriteAllText((Join-Path $Running 'stop'),'')
        $deadline=[datetime]::UtcNow.AddSeconds($StopSeconds)
        while($true){
            $s=Read-State $Running
            $alive=@(@($s.supervisor,$s.guest) | Where-Object {$_ -and (Get-Process -Id $_ -ErrorAction SilentlyContinue)})
            if($s.state -eq 'stopped' -and $alive.Count -eq 0){break}
            if([datetime]::UtcNow -gt $deadline){throw "Supervisor at $Running did not stop within $StopSeconds s (state=$($s.state)); nothing was copied or launched"}
            Start-Sleep -Milliseconds 500
        }
        "STOPPED supervisor at $Running"
    }
}
foreach($d in $leaving){
    $h=@(Get-Holders $d)
    if($h.Count -gt 0){throw "A VM holds $d (PID $(($h|ForEach-Object ProcessId) -join ',')); nothing was copied or launched"}
}
if($leaving.Count -gt 0){[void](New-Item -ItemType Directory -Force $BackupDir)}
$stamp=[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')
foreach($d in $leaving){
    if(-not (Test-Path -LiteralPath $d)){continue}
    $copy=Join-Path $BackupDir ("{0}-{1}{2}" -f [IO.Path]::GetFileNameWithoutExtension($d),$stamp,[IO.Path]::GetExtension($d))
    if(Test-Path -LiteralPath $copy){throw "Backup exists: $copy"}
    $before=(Get-FileHash -LiteralPath $d).Hash
    Copy-Item -LiteralPath $d -Destination $copy
    if(@(Get-Holders $d).Count -gt 0 -or (Get-FileHash -LiteralPath $d).Hash -ne $before -or (Get-FileHash -LiteralPath $copy).Hash -ne $before){
        Remove-Item -LiteralPath $copy -Force;throw "World $d changed during its backup; nothing was launched"}
    "BACKUP $d -> $copy sha256=$before"
}
if($Fresh){
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $World -DecorationFile $DecorationFile -FloraFile $FloraFile
    if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $World)){throw 'Map cache install failed; nothing was launched'}
}
if($Bake){
    $bakeDir=Join-Path $bakeRoot "bake-$stamp"
    if(@(Get-Holders $World).Count -gt 0){throw "A VM holds $World; the bake did not fold"}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $World -DecorationFile $DecorationFile -FloraFile $FloraFile
    if($LASTEXITCODE -ne 0){throw "Bake fold failed; the shard is stopped, world backed up in $BackupDir"}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'transform-flora.ps1') -ClientRoot $ClientRoot -OutDir (Join-Path $bakeDir 'stripped')
    if($LASTEXITCODE -ne 0){throw 'Bake flora strip failed; the shard is stopped, world folded, clients untouched'}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'decor-statics.ps1') -StrippedDir (Join-Path $bakeDir 'stripped') -OutDir (Join-Path $bakeDir 'client')
    if($LASTEXITCODE -ne 0){throw 'Bake statics patch failed; the shard is stopped, world folded, clients untouched'}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'decor-land.ps1') -ClientRoot $ClientRoot -OutDir (Join-Path $bakeDir 'client')
    if($LASTEXITCODE -ne 0){throw 'Bake land patch failed; the shard is stopped, world folded, clients untouched'}
    $files='STAIDX0.MUL','STATICS0.MUL','MAP0.MUL'
    $want=@{};foreach($n in $files){$want[$n]=(Get-FileHash -LiteralPath (Join-Path $bakeDir "client\$n")).Hash}
    foreach($c in $PlayerClients){
        $keep=Join-Path $bakeRoot ("clients-{0}\{1}" -f $stamp,(Split-Path $c -Leaf))
        [void](New-Item -ItemType Directory -Force $keep)
        foreach($n in $files){$from=Join-Path $c $n;$copy=Join-Path $keep $n;Copy-Item -LiteralPath $from -Destination $copy
            if((Get-FileHash -LiteralPath $copy).Hash -ne (Get-FileHash -LiteralPath $from).Hash){throw "Client backup of $from differs; that client is untouched"}}
        foreach($n in $files){Copy-Item -LiteralPath (Join-Path $bakeDir "client\$n") -Destination (Join-Path $c $n) -Force
            if((Get-FileHash -LiteralPath (Join-Path $c $n)).Hash -ne $want[$n]){throw "$c\$n differs from the baked file; restore from $keep"}}
        "BAKE client $c (backup $keep)"
    }
}
if(@(Get-Holders $World).Count -gt 0){throw "A VM holds $World; nothing was launched"}
$out=$OutDir+'.supervisor.out';$err=$OutDir+'.supervisor.err'
# Win32_Process.Create, not Start-Process: a redirected Start-Process child inherits the caller's stdout handle, so a
# caller capturing this script's output would wait on the supervisor for the life of the shard.
$q={param($t) '"'+$t+'"'}
$line=@((& $q (Get-Process -Id $PID).Path),'-NoProfile','-File',(& $q (Join-Path $PSScriptRoot 'supervise-composite-game.ps1')),'-Artifact',(& $q $Artifact),
    '-StateFile',(& $q $World),'-OutDir',(& $q $OutDir),'-Testing','-Admin','-StartupSeconds',"$StartupSeconds",'-Port',"$Port",'-AdminPort',"$AdminPort",'-ClockScale',"$ClockScale",'>',(& $q $out),'2>',(& $q $err)) -join ' '
$hidden=New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0}
$r=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine="cmd.exe /d /c `"$line`"";CurrentDirectory=$PSScriptRoot;ProcessStartupInformation=$hidden}
if($r.ReturnValue -ne 0){throw "Win32_Process.Create returned $($r.ReturnValue); nothing was launched"}
$deadline=[datetime]::UtcNow.AddSeconds($ServeSeconds)
while($true){
    $s=if(Test-Path -LiteralPath $OutDir){Read-State $OutDir}else{$null}
    if($s -and $s.state -eq 'serving'){break}
    if(-not (Get-Process -Id $r.ProcessId -ErrorAction SilentlyContinue) -or ($s -and $s.state -in @('gave-up','stopped'))){throw "Supervisor ended before serving; see $out and $err"}
    if([datetime]::UtcNow -gt $deadline){throw "Not serving within $ServeSeconds s; the supervisor is left running: create $OutDir\stop to end it"}
    Start-Sleep -Milliseconds 500
}
"SERVING localhost:$Port admin localhost:$AdminPort supervisor PID=$($s.supervisor) guest=$($s.guest) world=$World; stop with $OutDir\stop"
# A client outside -PlayerClients holds unbaked statics and map, and raised land then renders as a mirror trail.
$known=@($PlayerClients | ForEach-Object {[IO.Path]::GetFullPath($_).TrimEnd('\')})
foreach($p in @(Get-CimInstance Win32_Process -Filter "Name='client.exe'")){
    $dir=if($p.ExecutablePath){Split-Path $p.ExecutablePath}else{''}
    if(-not $dir -or -not ($known | Where-Object {$_ -eq $dir.TrimEnd('\')})){"WARNING: CLIENT.EXE PID=$($p.ProcessId) runs from '$dir', which is not in -PlayerClients; its map and statics are not this shard's"}
}
if($Bake){
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'bake-confirm.ps1') -Port $AdminPort -KeyFile $KeyFile
    if($LASTEXITCODE -ne 0){"BAKE folded but decor-baked failed: the baked items' world copies stand beside their statics; rerun bake-confirm.ps1"}
}
