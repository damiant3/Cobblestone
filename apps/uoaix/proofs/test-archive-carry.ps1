[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[string]$Kernel='',[string]$OutDir='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/archive-carry-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
function Admit {if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 3670016){throw 'RAM admission: a 3 GiB guest needs 3.5 GiB free'}}
function Compile([string]$Name){
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot "$Name.codex") -Out (Join-Path $OutDir "$Name.cdx") -Log (Join-Path $OutDir "$Name.compile.log") -Kernel $Kernel | Out-Null
    if(-not (Test-Path -LiteralPath (Join-Path $OutDir "$Name.cdx"))){throw "$Name compile failed"}
}
function Boot([string]$Name,[string]$Disk,[string]$Tag){
    Admit
    $log=Join-Path $OutDir "$Tag.log";$err=Join-Path $OutDir "$Tag.stderr"
    $p=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -PassThru -WindowStyle Hidden -RedirectStandardError $err -ArgumentList @('-kernel',('"'+(Join-Path $OutDir "$Name.cdx")+'"'),'-disk',('"'+$Disk+'"'),'-output',('"'+$log+'"'),'-mem','3072','-headless')
    try{ if(-not $p.WaitForExit(1800000)){throw "$Tag timed out"} } finally { if(-not $p.HasExited){Stop-Process -Id $p.Id -Force} }
    if([IO.File]::ReadAllText($err) -notmatch 'FINAL: debug_exit_code=0 process_exit=1'){throw "$Tag guest did not exit normally"}
    $text=Get-Content -LiteralPath $log
    if(@($text | Where-Object {$_ -cmatch '^FAIL' -or $_ -match '!EXC|OUT OF MEMORY'}).Count -gt 0){throw "$Tag failed: $(@($text | Where-Object {$_ -cmatch '^FAIL'})[0])"}
    return $text
}
$disk=Join-Path $OutDir 'world.disk'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot '../install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $disk | Out-Host
Compile 'CompositeVeinTripProof'
Compile 'EconomyArchiveRead'
$empty=Boot 'EconomyArchiveRead' $disk 'read-fresh'
if(-not ($empty -match 'segments=0 next=1')){throw 'A fresh region does not read as empty'}
for($i=1;$i -le 4;$i++){ $null=Boot 'CompositeVeinTripProof' $disk "world-$i" }
$sealed=Get-Content -LiteralPath (Join-Path $OutDir 'world-4.log') | Where-Object {$_ -match '^ARCHIVE segment'}
if(@($sealed).Count -lt 1){throw 'No economy segment was archived'}
$before=@(Boot 'EconomyArchiveRead' $disk 'read-before' | Where-Object {$_ -match '^SEGMENT|^PASS'})
$compacted=Join-Path $OutDir 'compacted.disk'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot '../compact-world.ps1') -Source $disk -Target $compacted -Kernel $Kernel -OutDir (Join-Path $OutDir 'compact') | Out-Host
& pwsh -NoProfile -File (Join-Path $PSScriptRoot '../install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $compacted | Out-Host
$after=@(Boot 'EconomyArchiveRead' $compacted 'read-after' | Where-Object {$_ -match '^SEGMENT|^PASS'})
if(($before -join "`n") -cne ($after -join "`n")){throw 'The archive changed across compact-world and install-map-cache'}
Write-Output ($before + "PASS the archive region survives compact-world.ps1 then install-map-cache.ps1 unchanged")
