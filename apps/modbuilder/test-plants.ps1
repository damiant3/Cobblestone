[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UnitySource,
    [Parameter(Mandatory)][string]$ProfilePath,
    [string]$Root='',
    [string]$Kernel='',
    [switch]$Expansion,
    [switch]$Render
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($Render -and -not $Expansion){throw 'Render requires Expansion'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Root){$Root=Join-Path $repo 'build-output/prism-targets'}
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Root=[IO.Path]::GetFullPath($Root)
$profile=Get-Content -LiteralPath $ProfilePath -Raw|ConvertFrom-Json
. (Join-Path $PSScriptRoot 'target-toolchain.ps1')
$identity=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='inspect';profile=$profile})
$game=(Resolve-Path -LiteralPath $profile.gamePath).Path
$out=Join-Path (Join-Path $Root 'prism-plant-tests') ([guid]::NewGuid().ToString('N'))
$saves=Join-Path $out 'saves'
[void](New-Item -ItemType Directory -Path $saves -Force)
[IO.File]::WriteAllText((Join-Path $saves 'prism-world-probe.marker'),'Isolated automated test saves')
[IO.File]::WriteAllText((Join-Path $saves 'prism-isolated-test.marker'),'Prism isolated game test')
$emitter=Join-Path $out 'plant-probe.cdx';$probeSource=Join-Path $out 'PlantProbe.cs'
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'mods/valheim/PlantProbe.codex') -Out $emitter -Log (Join-Path $out 'plant-probe-compile.log') -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'Plant probe emitter failed'}
& pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $emitter -OutFile $probeSource
if($LASTEXITCODE -ne 0){throw 'Plant probe source emission failed'}
$base=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $UnitySource).Path)
$entry='PrismGenerated.PrismUnityEntry.Initialize();'
if($base.IndexOf($entry) -lt 0 -or $base.IndexOf($entry) -ne $base.LastIndexOf($entry)){throw 'Expected one emitted Unity library entry'}
$probeText=[IO.File]::ReadAllText($probeSource)
$extra=''
if($Expansion){
    $extraEmitter=Join-Path $out 'expansion-probe.cdx'
    $extraSource=Join-Path $out 'ExpansionProbe.cs'
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'mods/valheim/ExpansionProbe.codex') -Out $extraEmitter -Log (Join-Path $out 'expansion-probe-compile.log') -Kernel $Kernel
    if($LASTEXITCODE -ne 0){throw 'Expansion probe emitter failed'}
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $extraEmitter -OutFile $extraSource
    if($LASTEXITCODE -ne 0){throw 'Expansion probe source emission failed'}
    $marker='UnityEngine.Debug.Log("PRISM WORLD TEST PASS");'
    if(-not $probeText.Contains($marker)){throw 'Plant completion point unavailable'}
    $finish=if($Render){'PrismExpansionProbe.Run(Player.m_localPlayer); stage = -1; PrismExpansionProbe.BeginPanel(); return;'}else{'PrismExpansionProbe.Run(Player.m_localPlayer); '+$marker}
    $probeText=$probeText.Replace($marker,$finish)
    $extra=[IO.File]::ReadAllText($extraSource)
}
$code=$base.Replace($entry,$entry+' PrismGenerated.PrismPlantProbe.Begin();')+"`n"+$probeText+"`n"+$extra
$build=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='package';profile=$profile;code=$code})
[IO.File]::WriteAllText((Join-Path $out 'build.json'),($build|ConvertTo-Json -Depth 10))
if(-not $build.ok){$build|ConvertTo-Json -Depth 4;throw 'Plant probe package failed'}
foreach($name in @('valheim.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')){Copy-Item -LiteralPath (Join-Path $game $name) -Destination (Join-Path $out $name)}
foreach($name in @('valheim_Data','MonoBleedingEdge','D3D12')){[void](New-Item -ItemType Junction -Path (Join-Path $out $name) -Target (Join-Path $game $name))}
Copy-Item -LiteralPath $build.artifact -Destination (Join-Path $out 'winhttp.dll')
$log=Join-Path $out 'player.log'
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $out 'valheim.exe'))
$start.UseShellExecute=$false;$start.WorkingDirectory=$out;$start.WindowStyle='Hidden'
foreach($arg in @('-savedir',$saves,'-logFile',$log,'-prism-plant-probe','-prism-isolated-test')){$start.ArgumentList.Add($arg)}
if($Render){foreach($arg in @('-screen-width','1920','-screen-height','1080','-screen-fullscreen','0','-prism-loadout-capture',(Join-Path $out 'loadout.png'))){$start.ArgumentList.Add($arg)}}else{$start.ArgumentList.Add('-batchmode');$start.ArgumentList.Add('-nographics')}
$receipt=[ordered]@{outDirectory=$out;saves=$saves;log=$log;artifact=$build.artifact;packageHash=$build.artifactHash;gameAssembly=$identity.gameAssembly;unityPlayer=$identity.unityPlayer;probeSourceHash=(Get-FileHash -LiteralPath $probeSource).Hash;baseSourceHash=(Get-FileHash -LiteralPath $UnitySource).Hash;rows=$base.Contains('class PrismPlantRows');expansion=[bool]$Expansion;passed=$false}
if($Expansion){$receipt.expansionSourceHash=(Get-FileHash -LiteralPath $extraSource).Hash}
$receipt.render=[bool]$Render
$proc=$null
try{
    $proc=[Diagnostics.Process]::Start($start);$receipt.pid=$proc.Id
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    if(-not $proc.WaitForExit(300000)){$proc.Kill();$proc.WaitForExit();$receipt.stoppedByHarness=$true}
    $receipt.exitCode=$proc.ExitCode
    $text=[IO.File]::ReadAllText($log)
    $receipt.passed=$proc.ExitCode -eq 0 -and $text.Contains('PRISM WORLD TEST PASS') -and -not $receipt.Contains('stoppedByHarness')
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $Root 'latest-plant-test.json'),($receipt|ConvertTo-Json))
    $text -split "`n" | Where-Object { $_.StartsWith('PRISM WORLD') -or $_.StartsWith('PRISM PLANT') -or $_.StartsWith('EXPANSION') }
    $receipt|ConvertTo-Json
    if(-not $receipt.passed){throw "Plant probe failed; see $log"}
}finally{if($proc){if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit()};$proc.Dispose()}}
