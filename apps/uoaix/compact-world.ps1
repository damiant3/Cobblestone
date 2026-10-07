[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Target,[string]$Kernel='',[string]$OutDir='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
$Source=(Resolve-Path -LiteralPath $Source).Path
$Target=[IO.Path]::GetFullPath($Target)
if(Test-Path -LiteralPath $Target){throw 'Target must be a new file'}
$needle=$Source.Replace('\','/')
if(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'codex-vm.exe' -and $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($needle,[StringComparison]::OrdinalIgnoreCase)}){throw 'A VM has the source disk open; compact a stopped world or a copy'}
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/compact-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 4194304){throw 'RAM admission: the compactor holds two 13 MB snapshots and a 3 MB patch in a 3 GiB guest'}
$before=(Get-FileHash -LiteralPath $Source).Hash
$cdx=Join-Path $OutDir 'compact.cdx'
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'CompositeCompact.codex') -Out $cdx -Log (Join-Path $OutDir 'compact.compile.log') -Kernel $Kernel | Out-Host
if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cdx)){throw 'Compactor compile failed'}
Copy-Item -LiteralPath $Source -Destination $Target
$log=Join-Path $OutDir 'compact.out'
$p=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -PassThru -WindowStyle Hidden -ArgumentList @('-kernel',('"'+$cdx+'"'),'-disk',('"'+$Source+'"'),'-disk2',('"'+$Target+'"'),'-output',('"'+$log+'"'),'-mem','3072','-headless') -RedirectStandardError (Join-Path $OutDir 'compact.stderr')
try{ if(-not $p.WaitForExit(900000)){throw 'Compactor timed out'} } finally { if(-not $p.HasExited){Stop-Process -Id $p.Id -Force} }
$text=if(Test-Path -LiteralPath $log){[IO.File]::ReadAllText($log)}else{''}
Write-Output $text
if((Get-FileHash -LiteralPath $Source).Hash -ne $before){throw 'Source disk changed during compaction'}
if($text -notmatch 'COMPACT OK'){Remove-Item -LiteralPath $Target -Force;throw 'Compaction failed; target removed'}
$fs=[IO.File]::OpenRead($Target);$br=[IO.BinaryReader]::new($fs);$s=0;$records=0
try{ while($s -lt 65536){ $fs.Position=[int64]$s*512; $b=$br.ReadBytes(512); if(-not ($b | Where-Object {$_ -ne 0} | Select-Object -First 1)){break}; $s+=1+[Math]::Floor(([BitConverter]::ToInt64($b,40)+511)/512); $records++ } } finally { $br.Dispose() }
if($records -ne 1){throw "Target journal holds $records records, expected 1"}
Write-Output ("COMPACTED {0}: 1 record, {1:N2} MiB of 32 free in the first half" -f $Target, ((65536-$s)*512/1MB))
