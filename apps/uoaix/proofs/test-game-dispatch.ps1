[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$CompressionSource,
    [string]$Kernel='',[string]$OutDir='',[switch]$Poison,[switch]$Shared,[string]$Vm='',
    [ValidateRange(30,600)][int]$StartupSeconds=300)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$ClientRoot=(Resolve-Path $ClientRoot).Path;$CompressionSource=(Resolve-Path $CompressionSource).Path
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path $Kernel).Path
if(-not $Vm){$Vm=Join-Path $repo 'tools/codex-vm.exe'}
$Vm=(Resolve-Path $Vm).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/dispatch-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;vmHash=(Get-FileHash $Vm).Hash;poison=[bool]$Poison;shared=[bool]$Shared;passed=$false}
$guest=$null;$adapter=$null;$admin=$null
$config=Join-Path $OutDir 'fixture-input.txt';$stopFile=Join-Path $OutDir 'admin.stop'
function Admit {$free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory;if($free -lt 1572864){throw 'RAM admission'};return $free}
function Port {
    $reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop();return $port
}
function Read-Live([string]$Path){
    if(-not (Test-Path $Path)){return ''}
    $f=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $r=[IO.StreamReader]::new($f);try{return $r.ReadToEnd()}finally{$r.Dispose()}
}
function Compile([string]$Name){
    $free=Admit
    $compileArgs=@('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',(Join-Path $PSScriptRoot "$Name.codex"),'-Out',(Join-Path $OutDir "$Name.cdx"),'-Log',(Join-Path $OutDir "$Name.compile.log"),'-Kernel',$Kernel)
    if($Poison){$compileArgs+='-Poison'}
    & pwsh @compileArgs
    if($LASTEXITCODE -ne 0){throw "$Name compile failed"}
}
function Replay([int]$ReplayPort,[string]$ReplayEvidence,[string]$ReplayLog){
    try{
        . (Join-Path $PSScriptRoot 'test-game-server.ps1') -Port $ReplayPort -CompressionSource $CompressionSource -Evidence $ReplayEvidence -ServerLog $ReplayLog -IdleLoginSeconds 2
    }finally{
        if(Get-Variable -Name packets -Scope Script -ErrorAction SilentlyContinue){
            [IO.File]::WriteAllText((Join-Path $ReplayEvidence 'packets.json'),($script:packets|ConvertTo-Json -Depth 4))
        }
    }
}
try{
    $coreName=if($Shared){'ShardServerProof'}else{'GameDispatchProof'}
    $serverName=if($Shared){'SharedServeProof'}else{'GameDispatchServer'}
    Compile $coreName
    $fixture=Join-Path $OutDir 'core.disk';$bytes=[byte[]]::new(512);$bytes[0]=17;[IO.File]::WriteAllBytes($fixture,$bytes)
    $free=Admit
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel (Join-Path $OutDir "$coreName.cdx") -OutFile (Join-Path $OutDir 'core.actual') -DiskFile $fixture
    if($LASTEXITCODE -ne 0){throw 'Core guest failed'}
    $expected=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "$coreName.expected"))-replace "`r",''
    # gn-log stamps every PACKET/REFUSE/SEND line with HPET milliseconds; the oracle holds the text without them.
    $actual=([IO.File]::ReadAllText((Join-Path $OutDir 'core.actual'))-replace "`r",'')-replace '(?m) ms=[0-9]+$',''
    if($actual -cne $expected){throw 'Core exact oracle mismatch'}
    Write-Output 'PASS dispatch core'
    Compile $serverName
    $port=Port;$mulPort=Port
    if($port -eq $mulPort){throw 'Port allocation collision'}
    $receipt.port=$port;$receipt.mulPort=$mulPort
    $adapter=Start-Process pwsh -ArgumentList @('-NoProfile','-File',('"'+(Join-Path $PSScriptRoot '../serve-mul.ps1')+'"'),'-ClientRoot',('"'+$ClientRoot+'"'),'-Port',"$mulPort",'-RequestTimeoutMs','30000') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutDir 'mul.stdout') -RedirectStandardError (Join-Path $OutDir 'mul.stderr')
    $ready=$false
    for($i=0;$i -lt 50;$i++){
        if($adapter.HasExited){throw 'MUL adapter exited'}
        $listener=Get-NetTCPConnection -State Listen -LocalPort $mulPort -ErrorAction SilentlyContinue
        if($listener -and $listener.OwningProcess -eq $adapter.Id){$ready=$true;break}
        Start-Sleep -Milliseconds 100
    }
    if(-not $ready){throw 'Private MUL adapter not ready'}
    $disk=Join-Path $OutDir 'marker.disk';$bytes=[byte[]]::new(4096);[BitConverter]::GetBytes([long]$port).CopyTo($bytes,8);[IO.File]::WriteAllBytes($disk,$bytes)
    $log=Join-Path $OutDir 'server.log'
    $vmArgs=@('-kernel',('"'+(Join-Path $OutDir "$serverName.cdx")+'"'),'-disk',('"'+$disk+'"'),'-headless','-mem','3072','-portfwd',"${port}:2593",'-natmap',"2595:$mulPort",'-output',('"'+$log+'"'))
    if($Shared){
        $adminPort=Port
        if($adminPort -eq $port -or $adminPort -eq $mulPort){throw 'Admin port allocation collision'}
        $receipt.adminPort=$adminPort
        $keys=@(1..5|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()})
        [IO.File]::WriteAllText($config,($keys -join "`n")+"`n")
        $vmArgs+=@('-portfwd',"${adminPort}:2594",'-input',('"'+$config+'"'))
    }
    $free=Admit
    $guest=Start-Process $Vm -ArgumentList $vmArgs -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $OutDir 'guest.stderr')
    $receipt.pid=$guest.Id;$receipt.adapterPid=$adapter.Id;$receipt.freeKiB=$free
    [IO.File]::WriteAllText((Join-Path $OutDir 'run.json'),(@{pid=$guest.Id;adapterPid=$adapter.Id;guests=1;log=$log;owner=$env:CODEX_SESSION_ID}|ConvertTo-Json))
    Write-Output "dispatch guest PID=$($guest.Id), adapter PID=$($adapter.Id), log=$log"
    $ready=$false
    $deadline=[datetime]::UtcNow.AddSeconds($StartupSeconds)
    $progress=[datetime]::UtcNow.AddSeconds(30)
    while([datetime]::UtcNow -lt $deadline){
        if($guest.HasExited){throw 'Dispatch server exited before listen'}
        $raw=Read-Live $log
        if($raw -match 'FAIL|REFUSE preload|!EXC|OUT OF MEMORY'){throw "Server startup failed: $raw"}
        if($raw -match 'LISTEN game-server 0'){$ready=$true;break}
        if([datetime]::UtcNow -ge $progress){
            $network=Read-Live (Join-Path $OutDir 'guest.stderr')
            Write-Output ("preload connections="+[regex]::Matches($network,'NAT: SYN from guest').Count)
            $progress=[datetime]::UtcNow.AddSeconds(30)
        }
        Start-Sleep -Milliseconds 100
    }
    if(-not $ready){throw 'Dispatch server did not listen'}
    $listener=Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
    if(-not $listener -or $listener.OwningProcess -ne $guest.Id){throw 'Forwarded port not owned by proof guest'}
    if($Shared){
        $adminOut=Join-Path $OutDir 'admin'
        $admin=Start-Process pwsh -ArgumentList @('-NoProfile','-File',('"'+(Join-Path $PSScriptRoot 'test-shared-admin.ps1')+'"'),'-Port',"$adminPort",'-Config',('"'+$config+'"'),'-OutDir',('"'+$adminOut+'"'),'-StopFile',('"'+$stopFile+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutDir 'admin.stdout') -RedirectStandardError (Join-Path $OutDir 'admin.stderr')
        $receipt.adminPid=$admin.Id
        [IO.File]::WriteAllText((Join-Path $OutDir 'run.json'),(@{pid=$guest.Id;adapterPid=$adapter.Id;adminPid=$admin.Id;guests=1;log=$log;owner=$env:CODEX_SESSION_ID}|ConvertTo-Json))
        $deadline=[datetime]::UtcNow.AddSeconds(30)
        while(-not (Test-Path (Join-Path $adminOut 'ready'))){
            if($admin.HasExited -or [datetime]::UtcNow -gt $deadline){throw 'Authenticated admin did not become ready'}
            Start-Sleep -Milliseconds 100
        }
    }
    $replayStart=[datetimeoffset]::UtcNow
    Replay $port (Join-Path $OutDir 'replay') $log
    $replayEnd=[datetimeoffset]::UtcNow
    if($Shared){
        [IO.File]::WriteAllText($stopFile,'stop')
        if(-not $admin.WaitForExit(15000) -or $admin.ExitCode -ne 0){throw 'Concurrent admin proof failed'}
        $adminResult=Get-Content (Join-Path $adminOut 'result.json') -Raw|ConvertFrom-Json
        if(-not $adminResult.passed){throw 'Concurrent admin receipt failed'}
        $during=@($adminResult.healthTimes|Where-Object{[datetimeoffset]$_ -ge $replayStart -and [datetimeoffset]$_ -le $replayEnd})
        if($during.Count -lt 1){throw 'No authenticated health completed during game replay'}
        $receipt.concurrentHealth=$during.Count
    }
    if($guest.HasExited){throw 'Server exited during replay'}
    $shutdown=[Threading.EventWaitHandle]::OpenExisting("Global\CodexVmShutdown_$($guest.Id)")
    try{[void]$shutdown.Set()}finally{$shutdown.Dispose()}
    if(-not $guest.WaitForExit(5000)){throw 'Proof guest did not stop gracefully'}
    $receipt.exitCode=$guest.ExitCode
    $final=[IO.File]::ReadAllText((Join-Path $OutDir 'guest.stderr'))
    if($guest.ExitCode -ne 1 -or $final -notmatch '(?m)^FINAL: debug_exit_code=0 process_exit=1\r?$') {throw 'Proof guest shutdown failed'}
    if($final -cmatch '(?m)^(SERIAL:|OUTPUT:)'){throw 'Proof output capture failed'}
    $captured=[regex]::Matches($final,'(?m)^Output: ([0-9]+) bytes -> .*\r?$')
    if($captured.Count -ne 1 -or [long]$captured[0].Groups[1].Value -ne (Get-Item $log).Length){throw 'Proof output extent mismatch'}
    $receipt.outputBytes=(Get-Item $log).Length
    $saved=[IO.File]::ReadAllBytes($disk);$count=[BitConverter]::ToInt64($saved,0)
    $packets=[regex]::Matches([IO.File]::ReadAllText($log),'(?m)^PACKET ').Count
    if($count -lt 1 -or $count -ne $packets -or [BitConverter]::ToInt64($saved,8) -ne $port){throw 'Dispatch marker/count absent or configuration damaged'}
    if($Shared){
        $adminCount=[BitConverter]::ToInt64($saved,16)
        $adminLogs=[regex]::Matches([IO.File]::ReadAllText($log),'(?m)^ADMINREPLY ').Count
        if($adminCount -lt 1 -or $adminCount -ne $adminLogs){throw 'Admin marker/count mismatch'}
        $receipt.adminCallbacks=$adminCount
    }
    $receipt.callbacks=$count;$receipt.markerHash=(Get-FileHash $disk).Hash;$receipt.passed=$true
    Write-Output "PASS effectful wire replay, callbacks=$count"
}finally{
    if($admin -and -not $admin.HasExited){[IO.File]::WriteAllText($stopFile,'stop');if(-not $admin.WaitForExit(5000)){Stop-Process -Id $admin.Id -Force;$admin.WaitForExit(5000)|Out-Null}}
    if($guest -and -not $guest.HasExited){
        try{$shutdown=[Threading.EventWaitHandle]::OpenExisting("Global\CodexVmShutdown_$($guest.Id)");try{[void]$shutdown.Set()}finally{$shutdown.Dispose()};[void]$guest.WaitForExit(5000)}catch{}
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force;$guest.WaitForExit(5000)|Out-Null}
    }
    if($adapter -and -not $adapter.HasExited){Stop-Process -Id $adapter.Id -Force;$adapter.WaitForExit(5000)|Out-Null}
    if(Test-Path $config){Remove-Item -LiteralPath $config}
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir"
}
