[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='', [switch]$Poison)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repo
if (-not $OutDir) {$OutDir=Join-Path $repo ('build-output/uoaix/panel-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
$Kernel=(Resolve-Path $Kernel).Path
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;passed=$false;poison=[bool]$Poison}
$guest=$null
$liveError=[IO.Path]::GetTempFileName()
function Memory-Bar { $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory;if($free -lt 1572864){throw 'RAM admission'};return $free }
try {
    & pwsh -NoProfile -File apps/uoaix/build-admin-page.ps1 -Check
    if($LASTEXITCODE -ne 0){throw 'Page source parity failed'}
    foreach($unit in @('AdminPanelProof','GmProof','GameClockProof','ReportSubjectProof','TreasuryProof','PanelLogLevelsProof','AdminPanelServeProof')){
        $receipt["freeBefore$unit"]=Memory-Bar
        $arguments=@('-NoProfile','-File','build/compile.ps1','-Src',"apps/uoaix/proofs/$unit.codex",'-Out',"$OutDir/$unit.cdx",'-Log',"$OutDir/$unit.log",'-Kernel',$Kernel)
        if($Poison){$arguments+='-Poison'}
        & pwsh @arguments
        $receipt["compile$unit"]=$LASTEXITCODE
        if($LASTEXITCODE -ne 0){Get-Content "$OutDir/$unit.log";throw "Compilation failed: $unit"}
        if($unit -ne 'AdminPanelServeProof'){
            $receipt.freeBeforeCore=Memory-Bar
            & pwsh -NoProfile -File build/test-run.ps1 -Kernel "$OutDir/$unit.cdx" -OutFile "$OutDir/$unit.out"
            $receipt.coreExit=$LASTEXITCODE
            if($LASTEXITCODE -ne 0){throw 'Core guest failed'}
            $text=[IO.File]::ReadAllText("$OutDir/$unit.out")
            Write-Output $text
            $verdict=if($unit -eq 'GmProof'){'UOAIX GM STAND-IN failures=0'}elseif($unit -eq 'GameClockProof'){'UOAIX GAME CLOCK failures=0'}elseif($unit -eq 'ReportSubjectProof'){'UOAIX REPORT SUBJECT failures=0'}elseif($unit -eq 'TreasuryProof'){'UOAIX TREASURY failures=0'}elseif($unit -eq 'PanelLogLevelsProof'){'UOAIX PANEL LOG LEVELS failures=0'}else{'UOAIX PANEL CORE failures=0'}
            if(-not $text.Contains($verdict) -or $text -match '\bFAIL\b|!EXC|OUT OF MEMORY'){throw "Core assertion failed: $unit"}
        }
    }
    $config=Join-Path $OutDir 'fixture-input.txt'
    $keys=@(1..5|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()})
    [IO.File]::WriteAllText($config,($keys -join "`n")+"`n")
    $reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop()
    $receipt.freeBeforeServer=Memory-Bar
    $guest=Start-Process tools/codex-vm.exe -WindowStyle Hidden -PassThru -ArgumentList @('-kernel',"$OutDir/AdminPanelServeProof.cdx",'-input',$config,'-output',"$OutDir/guest.out",'-portfwd',"${port}:2594",'-mem','3072','-headless') -RedirectStandardError $liveError
    $receipt.guestPid=$guest.Id
    [IO.File]::WriteAllText("$OutDir/run.json",(@{pid=$guest.Id;guests=1;log="$OutDir/guest.out";owner=$env:CODEX_SESSION_ID;port=$port}|ConvertTo-Json))
    Write-Output "Guest PID=$($guest.Id) port=$port"
    $ready=$false
    for($attempt=0;$attempt -lt 100;$attempt++){
        $client=[Net.Sockets.TcpClient]::new()
        try{$client.Connect('127.0.0.1',$port);$ready=$true;break}catch{Start-Sleep -Milliseconds 100}finally{$client.Dispose()}
    }
    if(-not $ready){throw 'Guest port did not open'}
    & node apps/uoaix/test-admin-panel.mjs --url "http://127.0.0.1:$port/" --input $config --out "$OutDir/browser"
    $receipt.browserExit=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw 'Browser acceptance failed'}
    $receipt.passed=$true
    Write-Output 'UOAIX PANEL PASS'
} finally {
    if($guest -and -not $guest.HasExited){Stop-Process -Id $guest.Id -Force;$guest.WaitForExit()}
    Copy-Item -LiteralPath $liveError -Destination "$OutDir/guest.err"
    Remove-Item -LiteralPath $liveError
    if(Test-Path "$OutDir/fixture-input.txt"){Remove-Item -LiteralPath "$OutDir/fixture-input.txt"}
    [IO.File]::WriteAllText("$OutDir/result.json",($receipt|ConvertTo-Json -Depth 5))
    Write-Output "Evidence: $OutDir/result.json"
}
