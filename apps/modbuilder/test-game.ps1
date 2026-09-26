[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GamePath,
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$OutDirectory,
    [string]$SaveRoot = '',
    [ValidateSet('startup','storage')][string]$Probe = 'storage'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$GamePath=(Resolve-Path -LiteralPath $GamePath).Path
$Package=(Resolve-Path -LiteralPath $Package).Path
$OutDirectory=[IO.Path]::GetFullPath($OutDirectory)
if((Test-Path -LiteralPath $OutDirectory) -and @(Get-ChildItem -LiteralPath $OutDirectory -Force).Count){throw 'Game test directory must be empty'}
foreach($name in @('valheim.exe','UnityPlayer.dll','steam_appid.txt')){if(-not(Test-Path -LiteralPath (Join-Path $GamePath $name))){throw "Missing game file: $name"}}
[void](New-Item -ItemType Directory -Path $OutDirectory -Force)
$saves=Join-Path $OutDirectory 'saves'
if($SaveRoot){
    if(-not [IO.Path]::IsPathFullyQualified($SaveRoot)){throw 'Test save root must be an absolute path'}
    $saveBase=[IO.Path]::GetFullPath($SaveRoot)
    if($saveBase.Equals($GamePath,[StringComparison]::OrdinalIgnoreCase) -or $saveBase.StartsWith($GamePath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test saves must be outside the installed game directory'}
    $saves=Join-Path $saveBase ([guid]::NewGuid().ToString('N'))
}
[void](New-Item -ItemType Directory -Path $saves -Force)
[IO.File]::WriteAllText((Join-Path $saves 'prism-isolated-test.marker'),'Prism isolated game test')
foreach($name in @('valheim.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')){Copy-Item -LiteralPath (Join-Path $GamePath $name) -Destination (Join-Path $OutDirectory $name)}
foreach($name in @('valheim_Data','MonoBleedingEdge','D3D12')){[void](New-Item -ItemType Junction -Path (Join-Path $OutDirectory $name) -Target (Join-Path $GamePath $name))}
Copy-Item -LiteralPath $Package -Destination (Join-Path $OutDirectory 'winhttp.dll')
$log=Join-Path $OutDirectory 'player.log'
$argsList=@('-batchmode','-nographics','-prism-isolated-test','-savedir',('"'+$saves+'"'),'-logFile',('"'+$log+'"'))
if($Probe -eq 'storage'){$argsList+='-prism-storage-test'}
$receipt=[ordered]@{probe=$Probe;game=$GamePath;packageHash=(Get-FileHash -LiteralPath $Package).Hash;outDirectory=$OutDirectory;saves=$saves;started=[DateTime]::UtcNow.ToString('o');passed=$false;log=$log}
$proc=$null
function Read-Shared([string]$Path){
    if(-not(Test-Path -LiteralPath $Path)){return ''}
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=[IO.StreamReader]::new($stream)
    try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
try{
    $proc=Start-Process -FilePath (Join-Path $OutDirectory 'valheim.exe') -WorkingDirectory $OutDirectory -ArgumentList $argsList -WindowStyle Hidden -PassThru
    $receipt.pid=$proc.Id
    [IO.File]::WriteAllText((Join-Path $OutDirectory 'run.json'),($receipt|ConvertTo-Json))
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    $marker=if($Probe -eq 'storage'){'PRISM STORAGE INVENTORY TEST PASS'}else{'Prism Codex entry initialized'}
    do{
        $text=Read-Shared $log
        if($text.Contains($marker) -and $text.Contains('PRISM TEST SAVES VERIFIED: '+$saves)){$receipt.passed=$true;break}
        if($proc.HasExited){break}
        Start-Sleep -Milliseconds 250
    }while([DateTime]::UtcNow -lt $deadline)
    if($receipt.passed){[void]$proc.WaitForExit(10000)}
    if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit();$receipt.stoppedByHarness=$true}
    $receipt.exitCode=$proc.ExitCode
    if($Probe -eq 'storage' -and ($proc.ExitCode -ne 0 -or $receipt.Contains('stoppedByHarness'))){$receipt.passed=$false}
    $receipt.bootstrap=Read-Shared (Join-Path $OutDirectory 'prism-bootstrap.log')
    $receipt.finished=[DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $OutDirectory 'run.json'),($receipt|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    if(-not $receipt.passed){[Console]::Error.WriteLine((Read-Shared $log));throw "Game $Probe probe did not reach its success marker; see $log"}
    "PASS: game $Probe probe; receipt $(Join-Path $OutDirectory 'run.json')"
}finally{
    if($proc){if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit()};$proc.Dispose()}
}
