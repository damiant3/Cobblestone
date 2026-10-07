[CmdletBinding()]
param([string]$Kernel = '', [string]$OutDir = '', [string]$Qemu = 'D:/Program Files/qemu/qemu-system-x86_64.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
$Qemu=(Resolve-Path -LiteralPath $Qemu).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/world-virtio-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
$parts=[Collections.Generic.List[string]]::new()
foreach($name in @('WorldDiskWrite','WorldDiskRead')){
    $text=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "$name.codex"))
    $text=$text.Replace("Chapter: $name","Chapter: Uoaix--$name").Replace('  opening :',"  $($name.ToLower())-unused-opening :")
    $parts.Add($text)
}
$parts.Add([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorldVirtioBoot.codex')))
$unit=Join-Path $OutDir 'world-virtio.codex'
$body=$parts -join "`r`n"
. (Join-Path $repo 'build/quire-map.ps1')
$seen=@{'Uoaix::WorldDiskWrite'=$true;'Uoaix::WorldDiskRead'=$true}
$ordered=Resolve-CiteOrder -RootLines ($body -split '\r?\n') -Repo $repo -SeedSeen $seen
$closure=(Format-CiteChapters -Ordered $ordered) -join "`r`n"
[IO.File]::WriteAllText($unit,$closure+"`r`n"+$body)
$cdx=Join-Path $OutDir 'world-virtio.cdx'
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;unitHash=(Get-FileHash $unit).Hash;accelerator='tcg';runs=@();passed=$false}
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw 'Less than 1.5 GiB free before guest'}
    return $free
}
function Run([string]$Name,[string]$Disk,[bool]$ReadOnly,[string]$Expected){
    $free=Admit
    $serial=Join-Path $OutDir "$Name.serial"
    $err=Join-Path $OutDir "$Name.stderr"
    $drive="if=none,id=vblk,format=raw,file=$Disk"
    if($ReadOnly){$drive+=',readonly=on'}
    $vmArgs=@('-accel','tcg','-cpu','max','-machine','pc,kernel-irqchip=off','-m','3072',
        '-kernel',('"'+$cdx+'"'),'-device','loader,addr=0xfe8,data=0xc0000000,data-len=4',
        '-serial',('"file:'+$serial+'"'),'-serial','null','-device','isa-debug-exit,iobase=0xf4,iosize=0x04',
        '-display','none','-no-reboot','-net','none','-drive',('"'+$drive+'"'),'-device','virtio-blk-pci,disable-legacy=on,drive=vblk')
    $p=Start-Process $Qemu -ArgumentList $vmArgs -WindowStyle Hidden -PassThru -RedirectStandardError $err
    Write-Output "$Name QEMU PID=$($p.Id) log=$serial"
    try{
        if(-not $p.WaitForExit(30000)){throw "$Name timed out"}
        $receipt.runs+=@{name=$Name;pid=$p.Id;freeKiB=$free;exit=$p.ExitCode;diskHash=(Get-FileHash $Disk).Hash}
        if($p.ExitCode -ne 1){Get-Content $err | Out-Host;throw "$Name did not exit normally"}
        $raw=[IO.File]::ReadAllText($serial)-replace "`r",''
        $lines=@($raw -split "`n"|Where-Object{$_ -notmatch '^(HEAP:|WD:|STACK:)'})
        $body=(($lines -join "`n").TrimEnd([char]10))+"`n"
        [IO.File]::WriteAllText((Join-Path $OutDir "$Name.actual"),$body)
        if($body -cne $Expected){Write-Output $body;throw "$Name exact oracle mismatch"}
        Write-Output "PASS $Name"
    }finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force};$p.Dispose()}
}
try{
    $free=Admit
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $unit -Out $cdx -Log (Join-Path $OutDir 'compile.log') -Kernel $Kernel
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $OutDir 'compile.log');throw 'World virtio compile failed'}
    $receipt.artifactHash=(Get-FileHash $cdx).Hash
    $disk=Join-Path $OutDir 'world.disk'
    [IO.File]::WriteAllBytes($disk,[byte[]]::new(32768))
    $write=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorldDiskWrite.expected')) -replace "`r",''
    $read=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorldDiskRead.expected')) -replace "`r",''
    Run 'write' $disk $false ($write+"PASS read fault propagated`n")
    $fixture=[IO.File]::ReadAllBytes($disk)
    if($fixture[11*512] -ne 99){throw 'Pending payload fixture absent'}
    for($i=10*512;$i -lt 11*512;$i++){if($fixture[$i] -ne 0){throw 'Pending header not empty'}}
    Run 'reboot' $disk $false ($read+"PASS read fault propagated`n")
    $readonly=Join-Path $OutDir 'readonly.disk'
    [IO.File]::WriteAllBytes($readonly,[byte[]]::new(32768))
    $before=(Get-FileHash $readonly).Hash
    Run 'readonly' $readonly $true "PASS readonly store refuses commit`nPASS read fault propagated`n"
    if((Get-FileHash $readonly).Hash -ne $before){throw 'Read-only store image changed'}
    if($receipt.runs.Count -ne 3){throw 'Incomplete store proof'}
    $receipt.passed=$true
}finally{
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir"
}
