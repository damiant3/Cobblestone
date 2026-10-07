[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UnitySource,
    [Parameter(Mandatory)][string]$ProfilePath,
    [string]$Root='',
    [string]$Kernel='',
    [int]$Days=14,
    [switch]$Kill,
    [switch]$Armour,
    [switch]$Ui,
    [string]$Saves='',
    [switch]$Reload,
    [switch]$Food,
    [switch]$Dig
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Root){$Root=Join-Path $repo 'build-output/prism-targets'}
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Root=[IO.Path]::GetFullPath($Root)
$profile=Get-Content -LiteralPath $ProfilePath -Raw|ConvertFrom-Json
. (Join-Path $PSScriptRoot 'target-toolchain.ps1')
$identity=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='inspect';profile=$profile})
$game=(Resolve-Path -LiteralPath $profile.gamePath).Path
$out=Join-Path (Join-Path $Root 'prism-play-tests') ([guid]::NewGuid().ToString('N'))
if($Reload){
    if(-not $Saves -or -not [IO.Path]::IsPathFullyQualified($Saves)){throw 'Reload requires the absolute saves directory of an earlier run'}
    $saves=(Resolve-Path -LiteralPath $Saves).Path
    if(-not(Test-Path -LiteralPath (Join-Path $saves 'prism-world-probe.marker'))){throw 'Reload requires marked isolated test saves'}
    if($saves.StartsWith($game,[StringComparison]::OrdinalIgnoreCase)){throw 'Test saves must be outside the installed game'}
    [void](New-Item -ItemType Directory -Path $out -Force)
}else{
    if($Saves){throw 'A fresh run creates its own saves; pass -Saves only with -Reload'}
    $saves=Join-Path $out 'saves'
    [void](New-Item -ItemType Directory -Path $saves -Force)
    [IO.File]::WriteAllText((Join-Path $saves 'prism-world-probe.marker'),'Isolated automated test saves')
    [IO.File]::WriteAllText((Join-Path $saves 'prism-isolated-test.marker'),'Prism isolated game test')
}
$emitter=Join-Path $out 'play-probe.cdx';$probeSource=Join-Path $out 'PlayProbe.cs'
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'mods/valheim/PlayProbe.codex') -Out $emitter -Log (Join-Path $out 'play-probe-compile.log') -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'Play probe emitter failed'}
& pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $emitter -OutFile $probeSource
if($LASTEXITCODE -ne 0){throw 'Play probe source emission failed'}
$base=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $UnitySource).Path)
$entry='PrismGenerated.PrismUnityEntry.Initialize();'
if($base.IndexOf($entry) -lt 0 -or $base.IndexOf($entry) -ne $base.LastIndexOf($entry)){throw 'Expected one emitted Unity library entry'}
$code=$base.Replace($entry,$entry+' PrismGenerated.PrismPlayProbe.Begin();')+"`n"+[IO.File]::ReadAllText($probeSource)
$build=Invoke-PrismTarget -Root $Root -Request ([pscustomobject]@{operation='package';profile=$profile;code=$code})
[IO.File]::WriteAllText((Join-Path $out 'build.json'),($build|ConvertTo-Json -Depth 10))
if(-not $build.ok){$build|ConvertTo-Json -Depth 4;throw 'Play probe package failed'}
foreach($name in @('valheim.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt')){Copy-Item -LiteralPath (Join-Path $game $name) -Destination (Join-Path $out $name)}
foreach($name in @('valheim_Data','MonoBleedingEdge','D3D12')){[void](New-Item -ItemType Junction -Path (Join-Path $out $name) -Target (Join-Path $game $name))}
Copy-Item -LiteralPath $build.artifact -Destination (Join-Path $out 'winhttp.dll')
$log=Join-Path $out 'player.log'
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $out 'valheim.exe'))
$start.UseShellExecute=$false;$start.WorkingDirectory=$out;$start.WindowStyle='Hidden'
foreach($arg in @('-savedir',$saves,'-logFile',$log,'-prism-play-probe','-prism-isolated-test','-prism-play-days',[string]$Days,'-batchmode','-nographics')){$start.ArgumentList.Add($arg)}
if($Kill){$start.ArgumentList.Add('-prism-play-kill')}
if($Armour){$start.ArgumentList.Add('-prism-play-armour')}
if($Ui){$start.ArgumentList.Add('-prism-play-ui')}
if($Reload){$start.ArgumentList.Add('-prism-play-reload')}
if($Food){$start.ArgumentList.Add('-prism-play-food')}
if($Dig){$start.ArgumentList.Add('-prism-play-dig')}
$receipt=[ordered]@{outDirectory=$out;saves=$saves;log=$log;artifact=$build.artifact;packageHash=$build.artifactHash;gameAssembly=$identity.gameAssembly;unityPlayer=$identity.unityPlayer;probeSourceHash=(Get-FileHash -LiteralPath $probeSource).Hash;baseSourceHash=(Get-FileHash -LiteralPath $UnitySource).Hash;meadows=$base.Contains('class PrismMeadowsSpawns');kill=[bool]$Kill;armour=[bool]$Armour;reload=[bool]$Reload;days=$Days;passed=$false}
$proc=$null
try{
    $proc=[Diagnostics.Process]::Start($start);$receipt.pid=$proc.Id
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    if(-not $proc.WaitForExit(1800000)){$proc.Kill();$proc.WaitForExit();$receipt.stoppedByHarness=$true}
    $receipt.exitCode=$proc.ExitCode
    $text=[IO.File]::ReadAllText($log)
    $receipt.passed=$proc.ExitCode -eq 0 -and $text.Contains('PRISM WORLD TEST PASS') -and $text.Contains('PRISM WORLD PASS: effective local save directory isolated') -and -not $receipt.Contains('stoppedByHarness')
    [IO.File]::WriteAllText((Join-Path $out 'run.json'),($receipt|ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $Root 'latest-play-test.json'),($receipt|ConvertTo-Json))
    $text -split "`n" | Where-Object { $_.StartsWith('PRISM PLAY') -or $_.StartsWith('PRISM LOADOUT') -or $_.StartsWith('PRISM NECK') -or $_.StartsWith('PRISM WORLD') }
    $receipt|ConvertTo-Json
    if(-not $receipt.passed){throw "Play probe failed; see $log"}
}finally{if($proc){if(-not $proc.HasExited){$proc.Kill();$proc.WaitForExit()};$proc.Dispose()}}
