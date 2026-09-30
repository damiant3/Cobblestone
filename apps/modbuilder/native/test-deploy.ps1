[CmdletBinding()]
param([string]$Exe='build-output/native-deploy/ModBuilder.exe',[int]$Port=18789)
$ErrorActionPreference='Stop'
$exePath=(Resolve-Path $Exe).Path
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$root=Join-Path $repo ('build-output/native-deploy/fixture-'+[guid]::NewGuid().ToString('N'))
$steam=Join-Path $root 'Steam é'
$game=Join-Path $steam 'steamapps/common/Valheim'
$dest=Join-Path $root 'Play é'
$local=Join-Path $root 'local'
foreach($path in @($local,$game,$dest)){[void](New-Item -ItemType Directory -Force -Path $path)}
foreach($base in @($game,$dest)){
    foreach($path in @('valheim_Data/Managed','MonoBleedingEdge/EmbedRuntime')){[void](New-Item -ItemType Directory -Force -Path (Join-Path $base $path))}
    foreach($path in @('valheim.exe','UnityPlayer.dll','valheim_Data/Managed/assembly_valheim.dll','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')){[IO.File]::WriteAllText((Join-Path $base $path),'untouched fixture game')}
}
$captureSource=Join-Path $root 'LaunchCapture.codex'
$capture=@'
Chapter: Capture Game Launch
  opening : [Console] Nothing =
   let h = mb-host 0
   in let command = mb-from-wide (mb-call (h.command) 0 0 0 0 0 0 0 0) 32768
   in let buffer = mb-alloc 8192
   in let count = mb-call (mb-proc "kernel32.dll" "GetCurrentDirectoryW") 4096 buffer 0 0 0 0 0 0
   in let saved = mb-write h (mb-env h "MODBUILDER_TEST_LAUNCH_LOG") (command & "\n" & mb-from-wide buffer count)
   in print-line-uni (if saved then "captured" else "capture failed")
'@
[IO.File]::WriteAllText($captureSource,[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WindowsHost.codex'))+"`n"+$capture)
& node (Join-Path $PSScriptRoot 'build-helper.mjs') $captureSource (Join-Path $dest 'valheim.exe')
if($LASTEXITCODE -ne 0){throw 'Launch capture build failed'}
$saved=@{};foreach($key in @('LOCALAPPDATA','MODBUILDER_TEST_STEAM','MODBUILDER_TEST_DEST','MODBUILDER_TEST_NEW_DEST','MODBUILDER_TEST_GAME_HASH','MODBUILDER_TEST_PLAYER_HASH','MODBUILDER_TEST_RUNNING','MODBUILDER_TEST_LAUNCH_LOG','MODBUILDER_TEST_STEAM_READY')){$saved[$key]=[Environment]::GetEnvironmentVariable($key)}
$env:MODBUILDER_TEST_LAUNCH_LOG=Join-Path $root 'launch.txt'
$env:LOCALAPPDATA=$local;$env:MODBUILDER_TEST_STEAM=$steam;$env:MODBUILDER_TEST_DEST=$dest
$env:MODBUILDER_TEST_GAME_HASH=(Get-FileHash (Join-Path $game 'valheim_Data/Managed/assembly_valheim.dll')).Hash
$env:MODBUILDER_TEST_PLAYER_HASH=(Get-FileHash (Join-Path $game 'UnityPlayer.dll')).Hash
$env:MODBUILDER_TEST_RUNNING='0'
$env:MODBUILDER_TEST_STEAM_READY='1'
$child=$null
function Assert($value,$name){if(-not $value){throw $name};Write-Host "PASS $name"}
function Start-Helper {
    $log=Join-Path $root ('helper-'+[guid]::NewGuid().ToString('N')+'.log')
    $script:child=Start-Process -FilePath $exePath -ArgumentList '--test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError ($log+'.err')
    for($i=0;$i -lt 100;$i++){Start-Sleep -Milliseconds 100;$file=[IO.File]::Open($log,'Open','Read','ReadWrite');$reader=[IO.StreamReader]::new($file);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()};if($text -and $text.EndsWith("`n")){return ($text|ConvertFrom-Json)};if($child.HasExited){throw "Helper exited: $text"}}
    throw 'Helper startup timeout'
}
function Stop-Helper {if($script:child -and -not $script:child.HasExited){$script:child.Kill();$script:child.WaitForExit()}}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DeployResource {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryExW(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll")] static extern IntPtr FindResourceW(IntPtr module,IntPtr name,IntPtr type);
 [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
 [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module,IntPtr resource);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 public static byte[] Read(string path){var module=LoadLibraryExW(path,IntPtr.Zero,2);if(module==IntPtr.Zero)throw new Exception("Cannot inspect loader");try{var res=FindResourceW(module,new IntPtr(101),new IntPtr(10));if(res==IntPtr.Zero)throw new Exception("Payload missing");var data=new byte[SizeofResource(module,res)];Marshal.Copy(LockResource(LoadResource(module,res)),data,0,data.Length);return data;}finally{FreeLibrary(module);}}
}
'@
try {
    $ready=Start-Helper;$url="http://127.0.0.1:$Port";$headers=@{'X-Bridge-Token'=$ready.token;Origin='null'}
    $initial=Invoke-RestMethod "$url/deployment" -Headers $headers
    Assert (-not $initial.ready -and -not $initial.destination -and $initial.defaultFolder -eq (Join-Path $steam 'ModBuilder')) 'no default copy leaves target creation as the initial action'
    $selected=Invoke-RestMethod "$url/deployment/select" -Method Post -Headers $headers
    Assert ($selected.ok -and $selected.ready -and $selected.destination -eq $dest) 'native destination selection preserves Unicode paths'
    $bad=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body ([byte[]]::new(600))
    Assert (-not $bad.ok -and -not(Test-Path (Join-Path $dest 'winhttp.dll'))) 'invalid DLL refuses before game writes'
    $dll=Join-Path $repo 'codex/plugs/cil/build-output/test/unity-hud.dll'
    $bytes=[IO.File]::ReadAllBytes($dll)
    $build=[Reflection.Assembly]::Load($bytes).ManifestModule.ModuleVersionId.ToString('N')
    $denied=Invoke-WebRequest "$url/deployment" -Method Post -ContentType 'application/octet-stream' -Body '' -SkipHttpErrorCheck
    Assert ($denied.StatusCode -eq 403) 'deployment requires the paired token'
    $client=[Net.Sockets.TcpClient]::new('127.0.0.1',$Port)
    try{
        $network=$client.GetStream();$network.ReadTimeout=5000
        $header=[Text.Encoding]::ASCII.GetBytes("POST /deployment HTTP/1.1`r`nHost: localhost`r`nOrigin: null`r`nX-Bridge-Token: $($ready.token)`r`nContent-Length: $($bytes.Length)`r`n`r`n")
        $network.Write($header);$network.Write($bytes,0,64);$client.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
        $reader=[IO.StreamReader]::new($network);$reply=$reader.ReadToEnd()
        Assert ($reply.StartsWith('HTTP/1.1 400') -and -not(Test-Path (Join-Path $dest 'winhttp.dll'))) 'interrupted binary upload writes no loader'
    }finally{$client.Dispose()}
    $result=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes -TimeoutSec 30
    Assert ($result.ok -and $result.installed) ('native deployment succeeds: '+($result|ConvertTo-Json -Compress))
    Assert ($result.buildNumber -ceq $build) 'native build identifier matches the CLR module ID used in game'
    $launch=Invoke-RestMethod "$url/launch" -Method Post -Headers $headers
    Assert (-not $launch.ok -and $launch.err -like '*isolated Saves*') 'launch refuses missing separate-save configuration'
    [void](New-Item -ItemType Directory -Path (Join-Path $dest 'Saves'))
    [IO.File]::WriteAllText((Join-Path $dest 'prism-local-saves.marker'),'Prism local saves')
    $launch=Invoke-RestMethod "$url/launch" -Method Post -Headers $headers
    Assert ($launch.ok -and $launch.launched -and $launch.buildNumber -ceq $build) 'native launch reports the installed build'
    for($i=0;$i -lt 50 -and -not(Test-Path $env:MODBUILDER_TEST_LAUNCH_LOG);$i++){Start-Sleep -Milliseconds 100}
    $captured=[IO.File]::ReadAllText($env:MODBUILDER_TEST_LAUNCH_LOG)
    Assert ($captured.Contains('"'+(Join-Path $dest 'Saves')+'"') -and $captured.Contains('-screen-fullscreen 1') -and $captured.EndsWith($dest)) 'real native launch preserves save arguments and working directory'
    $loader=Join-Path $dest 'winhttp.dll'
    Assert ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([DeployResource]::Read($loader))) -eq (Get-FileHash $dll).Hash) 'installed loader contains exactly the uploaded managed DLL'
    $first=(Get-FileHash $loader).Hash
    $receiptPath=Join-Path $dest 'Support/deployment.json'
    $record=Get-Content $receiptPath -Raw|ConvertFrom-Json -AsHashtable;$record['shortcut']='keep é.lnk';$record['fullscreenOnLaunch']=$true
    [IO.File]::WriteAllText($receiptPath,($record|ConvertTo-Json))
    $locked=[IO.File]::Open($loader,'Open','Read','Read')
    try{$blocked=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes -TimeoutSec 30}finally{$locked.Dispose()}
    Assert (-not $blocked.ok -and (Get-FileHash $loader).Hash -eq $first) 'locked loader refuses replacement and preserves the previous file'
    $result=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes -TimeoutSec 30
    Assert ($result.ok -and (Get-FileHash (Join-Path $result.backup 'winhttp.dll')).Hash -eq $first) ('redeploy preserves the previous loader and receipt: '+($result|ConvertTo-Json -Compress))
    $record=Get-Content $receiptPath -Raw|ConvertFrom-Json
    Assert ($record.shortcut -ceq 'keep é.lnk' -and $record.fullscreenOnLaunch -and $record.modHash -eq (Get-FileHash $loader).Hash.ToLowerInvariant()) 'receipt preserves play-copy settings and records the committed loader'
    [IO.File]::WriteAllText($loader,'an unrelated mod loader')
    $result=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes
    Assert (-not $result.ok -and [IO.File]::ReadAllText($loader) -eq 'an unrelated mod loader') 'external loader changes refuse overwrite'
    $launch=Invoke-RestMethod "$url/launch" -Method Post -Headers $headers
    Assert (-not $launch.ok) 'launch refuses a modified loader'
    $copy=Invoke-RestMethod "$url/deployment/create" -Method Post -Headers $headers -TimeoutSec 30
    Assert ($copy.ok -and $copy.ready -and (Test-Path (Join-Path $copy.destination 'Saves')) -and (Get-Content (Join-Path $copy.destination 'prism-local-saves.marker') -Raw) -eq 'Prism local saves') ('separate game copy is prepared with isolated saves: '+($copy|ConvertTo-Json -Compress))
    $light=Invoke-RestMethod "$url/deployment/create-light" -Method Post -Headers $headers -TimeoutSec 30
    Assert ($light.ok -and $light.ready -and $light.targetType -eq 'light') 'lightweight target is created and identified'
    foreach($name in @('valheim_Data','MonoBleedingEdge')){Assert ((Get-Item -LiteralPath (Join-Path $light.destination $name)).LinkType -eq 'Junction') ('light target shares '+$name+' through a junction')}
    Assert (-not (Get-Item -LiteralPath (Join-Path $light.destination 'UnityPlayer.dll')).LinkType -and (Test-Path (Join-Path $light.destination 'Saves'))) 'light target keeps private root binaries and saves'
    Assert ((Get-Content (Join-Path $light.destination 'steam_appid.txt') -Raw).Trim() -eq '892970') 'new target includes the Steam identity needed for direct launch'
    $lightDeploy=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes
    Assert ($lightDeploy.ok -and (Test-Path (Join-Path $light.destination 'winhttp.dll')) -and -not(Test-Path (Join-Path $game 'winhttp.dll'))) 'lightweight deployment writes only the private target loader'
    $null=Invoke-RestMethod "$url/deployment/create" -Method Post -Headers $headers
    $fullDeploy=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes
    Assert $fullDeploy.ok 'full target also has a verified deployment for default selection'
    [IO.File]::SetLastWriteTimeUtc((Join-Path $copy.destination 'Support/deployment.json'),[datetime]'2026-09-01T00:00:00Z')
    [IO.File]::SetLastWriteTimeUtc((Join-Path $light.destination 'Support/deployment.json'),[datetime]'2026-09-02T00:00:00Z')
    Stop-Helper
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $latest=Invoke-RestMethod "$url/deployment" -Headers $headers
    Assert ($latest.destination -eq $light.destination -and $latest.buildNumber -eq $build) 'startup selects the most recently deployed valid copy rather than the previously selected target'
    foreach($kind in @('light','full')){
        Stop-Helper
        $newFolder=Join-Path $steam ('ModBuilder/Chosen é '+$kind)
        [void](New-Item -ItemType Directory -Path $newFolder)
        $env:MODBUILDER_TEST_NEW_DEST=$newFolder
        $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
        $route=if($kind -eq 'light'){'create-folder-light'}else{'create-folder'}
        $custom=Invoke-RestMethod "$url/deployment/$route" -Method Post -Headers $headers
        Assert ($custom.ok -and $custom.destination -eq $newFolder -and $custom.targetType -eq $kind) ('new folder picker accepts an empty Unicode folder for '+$kind)
        $customDeploy=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes
        Assert $customDeploy.ok 'chosen folder receives the deployment'
    }
    Stop-Helper
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $latest=Invoke-RestMethod "$url/deployment" -Headers $headers
    Assert ($latest.destination -eq $newFolder -and $latest.ready) 'default discovery includes user-named copies under the default folder'
    [IO.File]::WriteAllText((Join-Path $newFolder 'winhttp.dll'),'changed outside ModBuilder')
    Stop-Helper
    $env:MODBUILDER_TEST_NEW_DEST=$dest
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $latest=Invoke-RestMethod "$url/deployment" -Headers $headers
    Assert ($latest.destination -ne $newFolder -and $latest.ready) 'a modified newest deployment is not selected as the default'
    $refused=Invoke-RestMethod "$url/deployment/create-folder" -Method Post -Headers $headers
    Assert (-not $refused.ok -and $refused.err -like '*empty folder*' -and [IO.File]::ReadAllText($loader) -eq 'an unrelated mod loader') 'new target refuses nonempty unowned folders without overwriting files'
    Stop-Helper
    $env:MODBUILDER_TEST_NEW_DEST=Join-Path $game 'Forbidden'
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $refused=Invoke-RestMethod "$url/deployment/create-folder" -Method Post -Headers $headers
    Assert (-not $refused.ok -and -not(Test-Path $env:MODBUILDER_TEST_NEW_DEST)) 'new target refuses a Steam game descendant before creating a directory'
    Stop-Helper
    $env:MODBUILDER_TEST_NEW_DEST=''
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $cancelled=Invoke-RestMethod "$url/deployment/create-folder-light" -Method Post -Headers $headers
    Assert (-not $cancelled.ok -and $cancelled.err -like '*cancelled*') 'new folder picker cancellation creates no target'
    Stop-Helper
    $env:MODBUILDER_TEST_DEST=$light.destination;$env:MODBUILDER_TEST_STEAM_READY='0';$ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $existingLight=Invoke-RestMethod "$url/deployment/select" -Method Post -Headers $headers
    Assert ($existingLight.ok -and $existingLight.ready -and $existingLight.targetType -eq 'light') 'existing lightweight targets remain selectable'
    $launch=Invoke-RestMethod "$url/launch" -Method Post -Headers $headers
    Assert (-not $launch.ok -and $launch.err -like '*Steam*') 'launch refuses before starting a game when Steam is not ready'
    Stop-Helper
    $env:MODBUILDER_TEST_STEAM_READY='1'
    $env:MODBUILDER_TEST_DEST=$game;$ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $result=Invoke-RestMethod "$url/deployment/select" -Method Post -Headers $headers
    Assert (-not $result.ok -and -not(Test-Path (Join-Path $game 'winhttp.dll'))) 'original Steam installation cannot be selected'
    Stop-Helper
    $env:MODBUILDER_TEST_DEST=$dest;$env:MODBUILDER_TEST_RUNNING='1'
    [IO.File]::Delete($loader)
    $ready=Start-Helper;$headers['X-Bridge-Token']=$ready.token
    $null=Invoke-RestMethod "$url/deployment/select" -Method Post -Headers $headers
    $launch=Invoke-RestMethod "$url/launch" -Method Post -Headers $headers
    Assert (-not $launch.ok -and $launch.err -like '*already running*') 'launch refuses an already running game'
    $result=Invoke-RestMethod "$url/deployment" -Method Post -Headers $headers -ContentType 'application/octet-stream' -Body $bytes
    Assert (-not $result.ok -and $result.err -like '*Close Valheim*' -and -not(Test-Path $loader)) 'running game blocks deployment'
    Assert ([IO.File]::ReadAllText((Join-Path $game 'valheim.exe')) -eq 'untouched fixture game') 'original game remains unchanged'
    $uninstall=& $exePath --test --uninstall
    Assert ($uninstall -match 'UNINSTALL COMPLETE' -and -not(Test-Path (Join-Path $local 'Cobblestone/ModBuilder/destination.txt')) -and (Test-Path (Join-Path $copy.destination 'valheim.exe'))) 'setup uninstall removes destination settings and preserves the game copy'
} finally {Stop-Helper;foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}}
