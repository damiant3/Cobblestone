[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientRoot,
    [Parameter(Mandatory)][string]$Kernel,
    [string]$OutDirectory='build-output/uoaix/server',
    [switch]$Replay
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$client=(Resolve-Path -LiteralPath $ClientRoot).Path
$compiler=(Resolve-Path -LiteralPath $Kernel).Path
$out=if([IO.Path]::IsPathRooted($OutDirectory)){$OutDirectory}else{Join-Path $repo $OutDirectory}
New-Item -ItemType Directory -Force $out|Out-Null
$gamePort=if($Replay){2594}else{2593}
$entry=if($Replay){'proofs/GameServerReplay.codex'}else{'GameServer.codex'}
foreach($port in @($gamePort,2595)){
    if(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue){throw "Port $port is already owned"}
}
function Admit-Guest {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB
    "Free RAM GiB: $free"
    if($free -le 1.5){throw 'Single guest RAM admission refused'}
}
Admit-Guest
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot $entry) -Out (Join-Path $out 'GameServer.cdx') -Log (Join-Path $out 'compile.log') -Kernel $compiler
if($LASTEXITCODE -ne 0){throw 'GameServer compilation failed; see compile.log'}
$adapter=$null;$guest=$null
try{
    $adapterArgs=@('-NoProfile','-File',('"'+(Join-Path $PSScriptRoot 'serve-mul.ps1')+'"'),'-ClientRoot',('"'+$client+'"'),'-Port','2595')
    $adapter=Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList $adapterArgs -RedirectStandardOutput (Join-Path $out 'mul.stdout') -RedirectStandardError (Join-Path $out 'mul.stderr')
    $ready=$false
    for($i=0;$i -lt 50;$i++){
        if($adapter.HasExited){throw 'MUL adapter exited; see mul.stderr'}
        $listener=Get-NetTCPConnection -State Listen -LocalPort 2595 -ErrorAction SilentlyContinue
        if($listener -and $listener.OwningProcess -eq $adapter.Id){$ready=$true;break}
        Start-Sleep -Milliseconds 100
    }
    if(-not $ready){throw 'MUL adapter did not bind'}
    Admit-Guest
    $stderr=Join-Path $env:TEMP ('uoaix-'+[Guid]::NewGuid().ToString('N')+'.stderr')
    $vmArguments=@('-kernel',('"'+(Join-Path $out 'GameServer.cdx')+'"'),'-headless','-mem','3072','-portfwd',"${gamePort}:2593",'-natmap','2595:2595','-output',('"'+(Join-Path $out 'server.log')+'"'))
    $guest=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -WindowStyle Hidden -PassThru -ArgumentList $vmArguments -RedirectStandardError $stderr -RedirectStandardOutput (Join-Path $out 'vm.stdout')
    @{guest=$guest.Id;adapter=$adapter.Id;log=(Join-Path $out 'server.log');stderr=$stderr;kernel=$compiler;started=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $out 'run.json') -Encoding utf8
    "UOAIX guest PID $($guest.Id), MUL PID $($adapter.Id); test account uoaixtest; wait for LISTEN in server.log"
    while(-not $guest.WaitForExit(1000)){
        if($adapter.HasExited){throw 'MUL adapter exited'}
    }
    throw "Guest exited unexpectedly with code $($guest.ExitCode); inspect server.log and $stderr"
}finally{
    if($guest -and -not $guest.HasExited){Stop-Process -Id $guest.Id}
    if($adapter -and -not $adapter.HasExited){Stop-Process -Id $adapter.Id}
}
