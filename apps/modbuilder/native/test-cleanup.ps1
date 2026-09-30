[CmdletBinding()]
param([string]$Exe='build-output/native-cleanup/ModBuilder.exe',[string]$LegacyExe='build-output/native-cors/before.exe',[string]$LegacyUninstaller='build-output/native-cleanup/legacy-uninstaller.exe')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$candidate=(Resolve-Path -LiteralPath $Exe).Path
$legacy=(Resolve-Path -LiteralPath $LegacyExe).Path
$previousUninstaller=(Resolve-Path -LiteralPath $LegacyUninstaller).Path
$probe=[Net.Sockets.TcpClient]::new()
try{$probe.Connect('127.0.0.1',18789);throw 'Diagnostic port 18789 is occupied'}catch [Net.Sockets.SocketException]{if($_.Exception.SocketErrorCode -ne 'ConnectionRefused'){throw}}finally{$probe.Dispose()}
$root=Join-Path $repo ('build-output/native-cleanup/fixture-'+[guid]::NewGuid().ToString('N'))
$desktop=Join-Path $root 'Desktop é';$downloads=Join-Path $root 'Downloads';$elsewhere=Join-Path $root 'Elsewhere';$tools=Join-Path $root 'Tools';$local=Join-Path $root 'local';$steam=Join-Path $root 'Steam'
foreach($dir in @($desktop,$downloads,$elsewhere,$tools,$local,$steam)){[void](New-Item -ItemType Directory -Force -Path $dir)}
$game=Join-Path $steam 'steamapps/common/Valheim'
foreach($name in @('valheim.exe','UnityPlayer.dll','valheim_Data/Managed/assembly_valheim.dll','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')){$path=Join-Path $game $name;[void](New-Item -ItemType Directory -Force -Path (Split-Path $path));[IO.File]::WriteAllText($path,'preserved game fixture')}
$keys=@('LOCALAPPDATA','MODBUILDER_TEST_STEAM','MODBUILDER_TEST_DESKTOP','MODBUILDER_TEST_DOWNLOADS');$saved=@{};foreach($key in $keys){$saved[$key]=[Environment]::GetEnvironmentVariable($key)}
$env:LOCALAPPDATA=$local;$env:MODBUILDER_TEST_STEAM=$steam;$env:MODBUILDER_TEST_DESKTOP=$desktop;$env:MODBUILDER_TEST_DOWNLOADS=$downloads
$current=Join-Path $elsewhere 'renamed-current.exe';$old=Join-Path $elsewhere 'renamed-previous.exe';$uninstaller=Join-Path $tools 'Uninstall-ModBuilder-v5.exe'
Copy-Item $candidate $current;Copy-Item $legacy $old;Copy-Item $candidate $uninstaller
$desktopOld=Join-Path $desktop 'ModBuilder-Setup-v3.exe';$duplicate=Join-Path $downloads 'ModBuilder-Setup-v3 (1).exe';$oldUninstaller=Join-Path $tools 'Uninstall-ModBuilder-old.exe'
foreach($path in @($desktopOld,$duplicate)){Copy-Item $legacy $path};Copy-Item $previousUninstaller $oldUninstaller
$unrelated=Join-Path $downloads 'ModBuilder-unrelated.exe';Copy-Item 'C:/Windows/System32/notepad.exe' $unrelated;$unrelatedHash=(Get-FileHash $unrelated).Hash
$note=Join-Path $desktop 'ModBuilder-not-a-program.exe';[IO.File]::WriteAllText($note,'keep this file')
$child=$null;$locked=$null
function Assert($pass,$message){if(-not $pass){throw $message};Write-Host "PASS $message"}
function Start-Helper($path){
 $log=Join-Path $root ([guid]::NewGuid().ToString('N')+'.log')
 $script:child=Start-Process -FilePath $path -ArgumentList '--test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError ($log+'.err')
 for($i=0;$i -lt 80;$i++){Start-Sleep -Milliseconds 100;$stream=[IO.File]::Open($log,'Open','Read','ReadWrite');$reader=[IO.StreamReader]::new($stream);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()};if($text.EndsWith("`n")){return ($text|ConvertFrom-Json)};if($child.HasExited){throw "Helper exited: $text"}}
 throw 'Helper startup timed out'
}
try{
 $null=Start-Helper $current
 $settings=Join-Path $local 'Cobblestone/ModBuilder';$record=Get-Content (Join-Path $settings 'helpers.json') -Raw|ConvertFrom-Json
 Assert ($record -contains $current) 'setup records a renamed helper outside common folders'
 $child.Kill();$child.WaitForExit();$null=Start-Helper $old
 $locked=[IO.File]::Open($duplicate,'Open','Read','Read')
 $result=& $uninstaller --test --uninstall
 Assert ($result -like '*UNINSTALL INCOMPLETE*') 'locked previous version is reported as incomplete'
 Assert ($child.WaitForExit(3000) -and $child.ExitCode -eq 0) 'uninstaller stops the actual previous helper version'
 Assert (-not(Test-Path $old) -and -not(Test-Path $current)) 'running and recorded renamed helpers are removed'
 Assert ((Test-Path $duplicate) -and (Test-Path (Join-Path $settings 'owner.txt'))) 'blocked cleanup preserves remaining files and retry metadata'
 $locked.Dispose();$locked=$null
 $result=& $uninstaller --test --uninstall
 Assert ($result -like '*UNINSTALL COMPLETE*') 'retry completes after the file lock is released'
 Assert (-not(Test-Path $desktopOld) -and -not(Test-Path $duplicate) -and -not(Test-Path $oldUninstaller)) 'legacy copies and earlier uninstallers are removed from discovery folders'
 Assert ((Get-FileHash $unrelated).Hash -eq $unrelatedHash -and [IO.File]::ReadAllText($note) -eq 'keep this file') 'unrelated executables and same-name nonprogram files are preserved'
 Assert ((Test-Path $uninstaller) -and -not(Test-Path $settings)) 'current uninstaller remains and owned setup is removed'
 Assert ([IO.File]::ReadAllText((Join-Path $game 'valheim.exe')) -eq 'preserved game fixture') 'game files are unchanged'
 Copy-Item $legacy $desktopOld
 $result=& $uninstaller --test --uninstall
 Assert ($result -like '*UNINSTALL COMPLETE*' -and -not(Test-Path $desktopOld)) 'orphaned earlier versions can be removed after setup was already uninstalled'
}finally{if($locked){$locked.Dispose()};if($child -and -not $child.HasExited){$child.Kill();$child.WaitForExit()};foreach($key in $keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}}
