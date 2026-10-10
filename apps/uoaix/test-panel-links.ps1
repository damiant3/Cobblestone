[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/panel-links-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
$Kernel=(Resolve-Path $Kernel).Path
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;passed=$false}
$guest=$null
$liveError=[IO.Path]::GetTempFileName()
try{
    & pwsh -NoProfile -File build/compile.ps1 -Src apps/uoaix/proofs/GameLinksPanelProof.codex -Out "$OutDir/GameLinksPanelProof.cdx" -Log "$OutDir/GameLinksPanelProof.log" -Kernel $Kernel
    if($LASTEXITCODE -ne 0){throw 'Compilation failed: GameLinksPanelProof'}
    if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864){throw 'RAM admission'}
    $config=Join-Path $OutDir 'fixture-input.txt'
    $keys=@(1..5|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()})
    [IO.File]::WriteAllText($config,($keys -join "`n")+"`n")
    $ports=foreach($n in 1,2){$r=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$r.Start();$r.LocalEndpoint.Port;$r.Stop()}
    $panelPort=$ports[0];$gamePort=$ports[1]
    $guest=Start-Process tools/codex-vm.exe -WindowStyle Hidden -PassThru -ArgumentList @('-kernel',"$OutDir/GameLinksPanelProof.cdx",'-input',$config,'-output',"$OutDir/guest.out",'-e1000-nat','-portfwd',"${panelPort}:2594",'-portfwd',"${gamePort}:2593",'-mem','3072','-headless') -RedirectStandardError $liveError
    $receipt.guestPid=$guest.Id
    [IO.File]::WriteAllText("$OutDir/run.json",(@{pid=$guest.Id;guests=1;log="$OutDir/guest.out";owner=$env:CODEX_SESSION_ID;panelPort=$panelPort;gamePort=$gamePort}|ConvertTo-Json))
    Write-Output "Guest PID=$($guest.Id) panel=$panelPort game=$gamePort"
    $ready=$false
    for($attempt=0;$attempt -lt 100;$attempt++){
        $client=[Net.Sockets.TcpClient]::new()
        try{$client.Connect('127.0.0.1',$panelPort);$ready=$true;break}catch{Start-Sleep -Milliseconds 100}finally{$client.Dispose()}
    }
    if(-not $ready){throw 'Panel port did not open'}
    $game=[Net.Sockets.TcpClient]::new()
    try{$game.Connect('127.0.0.1',$gamePort);$receipt.gameConnected=$game.Connected}finally{$game.Dispose()}
    if(-not $receipt.gameConnected){throw 'Game port refused while the panel is bound'}
    & node apps/uoaix/test-admin-panel.mjs --url "http://127.0.0.1:$panelPort/" --input $config --out "$OutDir/browser"
    $receipt.browserExit=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw 'Browser acceptance failed'}
    $receipt.passed=$true
    Write-Output 'UOAIX PANEL LINKS PASS'
}finally{
    if($guest -and -not $guest.HasExited){Stop-Process -Id $guest.Id -Force;$guest.WaitForExit()}
    Copy-Item -LiteralPath $liveError -Destination "$OutDir/guest.err"
    Remove-Item -LiteralPath $liveError
    if(Test-Path "$OutDir/fixture-input.txt"){Remove-Item -LiteralPath "$OutDir/fixture-input.txt"}
    [IO.File]::WriteAllText("$OutDir/result.json",($receipt|ConvertTo-Json -Depth 5))
    Write-Output "Evidence: $OutDir/result.json"
}
