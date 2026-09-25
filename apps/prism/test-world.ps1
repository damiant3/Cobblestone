[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UnitySource,
    [Parameter(Mandatory)][string]$ProfilePath,
    [string]$Root='',
    [string]$Kernel='',
    [ValidateSet('create','reload','vanilla')][string]$Mode='create',
    [string]$Saves='',
    [switch]$Render,
    [switch]$ShowWindow,
    [switch]$Play
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($Play){if($Mode -ne 'reload'){throw 'Play requires a saved fixture in reload mode'};$ShowWindow=$true;$Render=$true}
if($ShowWindow -and -not $Render){throw 'ShowWindow requires Render'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Root){$Root=Join-Path $repo 'build-output/prism-targets'}
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Root=[IO.Path]::GetFullPath($Root)
$profile=Get-Content -LiteralPath $ProfilePath -Raw|ConvertFrom-Json
. (Join-Path $PSScriptRoot 'target-toolchain.ps1')
$identity=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='inspect';profile=$profile})
$game=(Resolve-Path -LiteralPath $profile.gamePath).Path
$out=Join-Path (Join-Path $Root 'prism-world-tests') ([guid]::NewGuid().ToString('N'))
if($Mode -eq 'create'){
    if($Saves){throw 'Create requires fresh saves; use reload for existing test saves'}
    $Saves=Join-Path $out 'saves'
}else{
    if(-not [IO.Path]::IsPathFullyQualified($Saves)){throw 'Reload requires an absolute test save directory'}
    $Saves=(Resolve-Path -LiteralPath $Saves).Path
    if(-not(Test-Path -LiteralPath (Join-Path $Saves 'prism-world-probe.marker')) -or -not(Test-Path -LiteralPath (Join-Path $Saves 'probe-world.uid'))){throw 'Reload requires a completed isolated world fixture'}
}
if($Saves.Equals($game,[StringComparison]::OrdinalIgnoreCase) -or $Saves.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test saves must be outside the installed game'}
[void](New-Item -ItemType Directory -Path $out -Force)
[void](New-Item -ItemType Directory -Path $Saves -Force)
$building=[ordered]@{mode=$Mode;render=[bool]$Render;outDirectory=$out;saves=$Saves;log=(Join-Path $out 'player.log');phase='building';passed=$false}
[IO.File]::WriteAllText((Join-Path $out 'run.json'),($building|ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $Root 'latest-world-test.json'),($building|ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $Saves 'prism-world-probe.marker'),'Isolated automated test saves')
[IO.File]::WriteAllText((Join-Path $Saves 'prism-isolated-test.marker'),'Prism isolated game test')
$emitter=Join-Path $out 'world-probe.cdx';$probeSource=Join-Path $out 'WorldProbe.cs'
$probeUnit=Join-Path $repo 'apps/prism/mods/valheim/WorldProbe.codex'
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $probeUnit -Out $emitter -Log (Join-Path $out 'world-probe-compile.log') -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'World acceptance emitter failed'}
& pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $emitter -OutFile $probeSource
if($LASTEXITCODE -ne 0){throw 'World acceptance source emission failed'}
$base=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $UnitySource).Path)
$entry='PrismGenerated.PrismUnityEntry.Initialize();'
if(-not $base.Contains('class PrismStorage') -or $base.IndexOf($entry) -lt 0 -or $base.IndexOf($entry) -ne $base.LastIndexOf($entry)){throw 'Expected the emitted linked-storage Unity library'}
$replacement=$entry+' PrismGenerated.PrismWorldProbe.Begin();'
if($Mode -eq 'vanilla'){$replacement='PrismGenerated.PrismWorldProbe.Begin();'}
$code=$base.Replace($entry,$replacement)+"`n"+[IO.File]::ReadAllText($probeSource)
$build=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='package';profile=$profile;code=$code})
[IO.File]::WriteAllText((Join-Path $out 'build.json'),($build|ConvertTo-Json -Depth 10))
if(-not $build.ok){$build|ConvertTo-Json -Depth 4;throw 'World acceptance package failed'}
foreach($name in @('valheim.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')){Copy-Item -LiteralPath (Join-Path $game $name) -Destination (Join-Path $out $name)}
foreach($name in @('valheim_Data','MonoBleedingEdge','D3D12')){[void](New-Item -ItemType Junction -Path (Join-Path $out $name) -Target (Join-Path $game $name))}
Copy-Item -LiteralPath $build.artifact -Destination (Join-Path $out 'winhttp.dll')
$log=Join-Path $out 'player.log'
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $out 'valheim.exe'))
$start.UseShellExecute=$false;$start.WorkingDirectory=$out;$start.WindowStyle='Hidden'
if($ShowWindow){$start.WindowStyle='Normal'}
foreach($arg in @('-savedir',$Saves,'-logFile',$log,'-prism-world-probe','-prism-isolated-test')){$start.ArgumentList.Add($arg)}
if($Render){foreach($arg in @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-prism-world-render')){$start.ArgumentList.Add($arg)}}else{$start.ArgumentList.Add('-batchmode');$start.ArgumentList.Add('-nographics')}
if($Mode -ne 'create'){$start.ArgumentList.Add('-prism-world-reload')}
if($Mode -eq 'vanilla'){$start.ArgumentList.Add('-prism-world-vanilla')}
if($Play){$start.ArgumentList.Add('-prism-world-play')}
$receipt=[ordered]@{mode=$Mode;render=[bool]$Render;outDirectory=$out;saves=$Saves;log=$log;artifact=$build.artifact;packageHash=$build.artifactHash;gameAssembly=$identity.gameAssembly;unityPlayer=$identity.unityPlayer;probeSourceHash=(Get-FileHash -LiteralPath $probeSource).Hash;baseSourceHash=(Get-FileHash -LiteralPath $UnitySource).Hash;passed=$false}
$proc=$null
$leaveRunning=$false
try{
    $proc=[Diagnostics.Process]::Start($start);$receipt.pid=$proc.Id
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $Root 'latest-world-test.json'),($receipt|ConvertTo-Json))
    if($Play){
        $deadline=[datetime]::UtcNow.AddSeconds(210)
        do {
            if(Test-Path -LiteralPath $log){$stream=[IO.File]::Open($log,'Open','Read','ReadWrite');$reader=[IO.StreamReader]::new($stream);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()};if($text.Contains('PRISM WORLD READY FOR PLAY')){$leaveRunning=$true;break}}
            if($proc.HasExited){break};Start-Sleep -Milliseconds 500
        }while([datetime]::UtcNow -lt $deadline)
        if(-not $leaveRunning){throw "Play fixture did not become ready; see $log"}
        $receipt.readyForPlay=$true;$receipt.leftRunning=$true
        [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
        [IO.File]::WriteAllText((Join-Path $Root 'latest-world-test.json'),($receipt|ConvertTo-Json))
        $receipt|ConvertTo-Json;return
    }
    if(-not $proc.WaitForExit(240000)){$proc.Kill();$proc.WaitForExit();$receipt.stoppedByHarness=$true}
    $receipt.exitCode=$proc.ExitCode
    $text=[IO.File]::ReadAllText($log)
    $imageLine=@($text -split "`n" | Where-Object {$_.StartsWith('PRISM WORLD IMAGE: ')}) | Select-Object -Last 1
    if($imageLine){
        $imagePath=$imageLine.Substring('PRISM WORLD IMAGE: '.Length).Trim()
        if(-not [IO.Path]::GetFullPath($imagePath).StartsWith($Saves+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Capture outside test saves'}
        $receipt.imagePath=$imagePath;$receipt.imageHash=(Get-FileHash -LiteralPath $imagePath).Hash
    }
    $receipt.passed=$proc.ExitCode -eq 0 -and $text.Contains('PRISM WORLD TEST PASS') -and $text.Contains('PRISM WORLD PASS: effective local save directory isolated') -and -not $receipt.Contains('stoppedByHarness')
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $Root 'latest-world-test.json'),($receipt|ConvertTo-Json))
    $receipt|ConvertTo-Json
    if(-not $receipt.passed){throw "World acceptance $Mode failed; see $log"}
}finally{if($proc){if(-not $leaveRunning -and -not $proc.HasExited){$proc.Kill();$proc.WaitForExit()};$proc.Dispose()}}
