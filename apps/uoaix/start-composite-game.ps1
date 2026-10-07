[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$StateFile,
    [string]$OutDir='',[ValidateRange(10,120)][int]$StartupSeconds=60,[switch]$Testing,[switch]$Dev,[switch]$Hosted,[switch]$Admin,[string]$Vm='',[ValidateRange(1024,65535)][int]$Port=2593,[ValidateRange(1024,65535)][int]$AdminPort=2594,[switch]$NoPrompt)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($Testing -and $Hosted){throw 'Testing mode is forbidden on a public or hosted shard'}
if($Dev -and $Hosted){throw 'Development account creation is forbidden on a public or hosted shard'}
$public=-not ($Testing -or $Dev)
$british=''
if($public -and -not $NoPrompt){
    $first=[Net.NetworkCredential]::new('',(Read-Host -AsSecureString 'New password for account British (empty keeps the current one)')).Password
    if($first){
        $again=[Net.NetworkCredential]::new('',(Read-Host -AsSecureString 'Repeat the new password')).Password
        if($first -cne $again){throw 'The two passwords differ'}
        if($first -notmatch '^[\x20-\x7E]{1,30}$'){throw 'The password must be 1..30 printable ASCII characters'}
        $british=$first
    }
}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Artifact=(Resolve-Path -LiteralPath $Artifact).Path
$StateFile=(Resolve-Path -LiteralPath $StateFile).Path
if((Get-Item -LiteralPath $StateFile).Length -le 73400832){throw 'Run install-map-cache.ps1 on the local world disk before boot'}
if(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue){throw "Port $Port is owned; no listener was stopped"}
if($Admin -and (Get-NetTCPConnection -State Listen -LocalPort $AdminPort -ErrorAction SilentlyContinue)){throw "Port $AdminPort is owned; no listener was stopped"}
$needle=$StateFile.Replace('\','/')
if(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('codex-vm.exe','candidate.exe') -and $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($needle,[StringComparison]::OrdinalIgnoreCase)}){throw 'Another VM names this world disk; no second writer was started'}
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/composite-run-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
if($free -lt 1572864){throw 'RAM admission'}
$log=Join-Path $OutDir 'server.log';$err=Join-Path $OutDir 'guest.stderr'
$vm=if($Vm){(Resolve-Path -LiteralPath $Vm).Path}else{Join-Path $repo 'tools/codex-vm.exe'}
$vmArgs=@('-kernel',('"'+$Artifact+'"'),'-disk',('"'+$StateFile+'"'),'-output',('"'+$log+'"'),'-headless','-mem','3072','-e1000-nat','-portfwd',"${Port}:2593")
$option=Join-Path $OutDir 'launch.input'
$record=if($Testing){"UOAIX TESTING`n"}elseif($Dev){"UOAIX DEV`n"}elseif($british){"UOAIX PUBLIC $british`n"}else{''}
if($Admin){
    $keys=@(1..5|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()})
    $record="ADMIN $($keys -join ' ')`n"+$record
    $vmArgs+=@('-portfwd',"${AdminPort}:2594")
    $ownerKey=Join-Path $OutDir 'admin-owner.key'
    [IO.File]::WriteAllText($ownerKey,$keys[3]+"`n",[Text.Encoding]::ASCII)
}
if($record){
    [IO.File]::WriteAllText($option,$record,[Text.Encoding]::ASCII)
    $vmArgs+=@('-input',('"'+$option+'"'))
}
$guest=Start-Process $vm -ArgumentList $vmArgs -WindowStyle Hidden -PassThru -RedirectStandardError $err
$run=[ordered]@{owner=$env:CODEX_SESSION_ID;pid=$guest.Id;guests=1;log=$log;artifact=$Artifact;artifactHash=(Get-FileHash $Artifact).Hash;vm=$vm;vmHash=(Get-FileHash $vm).Hash;stateFile=$StateFile;port=$Port;cacheOnly=$true;testing=[bool]$Testing;dev=[bool]$Dev;hosted=[bool]$Hosted;freeKiB=$free;running=$false}
$run|ConvertTo-Json|Set-Content (Join-Path $OutDir 'run.json')
$ready=$false
try{
    $deadline=[datetime]::UtcNow.AddSeconds($StartupSeconds)
    do{
        $text=''
        if(Test-Path $log){$f=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$r=[IO.StreamReader]::new($f);try{$text=$r.ReadToEnd()}finally{$r.Dispose()}}
        if($text -match '(?m)^FAIL|!EXC|OUT OF MEMORY'){throw "Composite startup failed: $text"}
        if($guest.HasExited){throw 'Composite exited before listen'}
        if($text -match '(?m)^LISTEN game-server 0(?: level=\S+ sys=\S+)?(?: ms=\d+)?\r?$'){
            $mode=if($Testing){'MODE composite TESTING local-only'}elseif($Dev){'MODE composite DEV'}else{'MODE composite PUBLIC'}
            if(-not $text.Contains($mode)){throw 'Guest did not confirm the requested launch mode'}
            $ready=$true;break
        }
        if([datetime]::UtcNow -gt $deadline){throw 'Composite startup deadline'}
        Start-Sleep -Milliseconds 100
    }while($true)
    $listener=Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop
    if($listener.OwningProcess -ne $guest.Id){$ready=$false;throw 'Game port owner differs'}
    $run.running=$true
    Write-Output "READY localhost:$Port PID=$($guest.Id); installed cache only; $log"
    if($Admin){Write-Output "ADMIN http://localhost:$AdminPort/ owner key in $ownerKey (new every launch)"}
}finally{
    if(Test-Path -LiteralPath $option){Remove-Item -LiteralPath $option -Force}
    if(-not $ready -and -not $guest.HasExited){Stop-Process -Id $guest.Id -Force;[void]$guest.WaitForExit(5000)}
    $run|ConvertTo-Json|Set-Content (Join-Path $OutDir 'run.json')
}
