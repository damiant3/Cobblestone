[CmdletBinding()]
param([string]$Kernel='', [string]$OutDir='', [string]$Qemu='D:/Program Files/qemu/qemu-system-x86_64.exe', [switch]$CombinedTown,
    [ValidateRange(1024,8192)][int]$GuestMemMB=1024)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
$Qemu=(Resolve-Path -LiteralPath $Qemu).Path
$firmware=Join-Path (Split-Path $Qemu) 'share'
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/uefi-storage-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;accelerator='tcg';combinedTown=[bool]$CombinedTown;guestMemMB=$GuestMemMB;runs=@();passed=$false}
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw 'Less than 1.5 GiB free before guest'}
    return $free
}
function Run([string]$Name,[string]$Backend,[string]$Disk,[string]$Action){
    $free=Admit
    $serial=Join-Path $OutDir "$Name.serial"
    $err=Join-Path $OutDir "$Name.stderr"
    if($Backend -eq 'virtio'){
        $vars=Join-Path $OutDir "$Name.vars.fd"
        [IO.File]::Copy((Join-Path $firmware 'edk2-i386-vars.fd'),$vars)
        $exe=$Qemu
        $argv=@('-accel','tcg','-cpu','max','-machine','pc','-m',"$GuestMemMB",
            '-drive',('"if=pflash,format=raw,unit=0,readonly=on,file='+(Join-Path $firmware 'edk2-x86_64-code.fd')+'"'),
            '-drive',('"if=pflash,format=raw,unit=1,file='+$vars+'"'),
            '-drive',('"if=none,id=vblk,format=raw,file='+$Disk+'"'),
            '-device','virtio-blk-pci,disable-legacy=on,drive=vblk,bootindex=1',
            '-serial',('"file:'+$serial+'"'),'-serial','null','-device','isa-debug-exit,iobase=0xf4,iosize=0x04',
            '-display','none','-no-reboot','-net','none')
    }else{
        $exe=Join-Path $repo 'tools/codex-vm.exe'
        $argv=@('-kernel',('"'+$pristine+'"'),'-disk',('"'+$Disk+'"'),'-output',('"'+$serial+'"'),'-mem',"$GuestMemMB",'-headless','-uefi')
    }
    $p=Start-Process $exe -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError $err
    Write-Output "$Name PID=$($p.Id) log=$serial"
    try{
        if(-not $p.WaitForExit(45000)){throw "$Name timed out"}
        $receipt.runs+=@{name=$Name;pid=$p.Id;freeKiB=$free;exit=$p.ExitCode;diskHash=(Get-FileHash $Disk).Hash}
        $stderr=[IO.File]::ReadAllText($err)
        if($p.ExitCode -ne 1){throw "$Name failed: $stderr"}
        if($Backend -eq 'ide' -and $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1'){throw "$Name missing normal exit: $stderr"}
        if($stderr -match 'DROPPED|cannot be opened for write|every write.*LOST|(?:nopath|nodata|oob|openfail)=[1-9]'){throw "$Name lost writes: $stderr"}
        $raw=[IO.File]::ReadAllText($serial)-replace "`r",''
        if($raw -match 'FAIL|!EXC|OOM'){throw "$Name guest failure: $raw"}
        if([regex]::Matches($raw,'UEFI STORE START').Count -ne 1){throw "$Name missing or repeated start: $raw"}
        $body=$raw.Substring($raw.IndexOf('UEFI STORE START'))
        $body=(@($body -split "`n"|Where-Object{$_ -notmatch '^(HEAP:|WD:|STACK:)'} ) -join "`n").TrimEnd([char]10)+"`n"
        $mmio=''
        if($Backend -eq 'virtio'){
            $m=[regex]::Match($body,'(?m)^UEFI STORE MMIO=([0-9]+)$')
            if(-not $m.Success -or [long]$m.Groups[1].Value -le 4294967295){throw "$Name did not exercise high MMIO: $body"}
            $mmio=$m.Value+"`n"
        }
        $oracle="UEFI STORE $Action count=1 tick=720`n"
        if($CombinedTown){
            if($Action -eq 'SAVED'){$oracle="PASS blank store recovery refused`n"+([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownRestartWrite.expected'))-replace "`r",'')}
            else{
                $lines=([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownRestartRead.expected'))-replace "`r",'') -split "`n"
                $oracle=($lines[2..($lines.Length-1)] -join "`n")+"PASS recovered head mismatch refused`n"
            }
        }
        $expected="UEFI STORE START`nUEFI STORE DEVICE $Backend`n"+$mmio+$oracle+"UEFI STORE END`n"
        [IO.File]::WriteAllText((Join-Path $OutDir "$Name.actual"),$body)
        if($body -cne $expected){throw "$Name oracle mismatch: $body"}
        Write-Output "PASS $Name"
    }finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force};$p.Dispose()}
}
try{
    . (Join-Path $repo 'build/quire-map.ps1')
    $body=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'UefiStorageBoot.codex'))
    $seen=@{}
    if($CombinedTown){
        $parts=[Collections.Generic.List[string]]::new()
        foreach($name in @('TownRestartWrite','TownRestartRead')){
            $part=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "$name.codex"))
            $parts.Add($part.Replace("Chapter: $name","Chapter: Uoaix--$name").Replace('  opening :',"  $($name.ToLower())-unused-opening :"))
            $seen["Uoaix::$name"]=$true
        }
        $parts.Add([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'UefiTownBoot.codex')))
        $body=$parts -join "`r`n"
    }
    $ordered=Resolve-CiteOrder -RootLines ($body -split '\r?\n') -Repo $repo -SeedSeen $seen
    $unit=Join-Path $OutDir 'uefi-storage.codex'
    [IO.File]::WriteAllText($unit,((Format-CiteChapters -Ordered $ordered) -join "`r`n")+"`r`n"+$body)
    $receipt.unitHash=(Get-FileHash $unit).Hash
    $cdx=Join-Path $OutDir 'uefi-storage.cdx'
    $pe=Join-Path $OutDir 'BOOTX64.EFI'
    $pristine=Join-Path $OutDir 'pristine.img'
    $free=Admit
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $unit -Out $cdx -Log (Join-Path $OutDir 'compile.log') -Kernel $Kernel -Pet
    if($LASTEXITCODE -ne 0){throw 'UEFI storage compile failed'}
    & pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput $cdx -Out $pe -HeapPages 16384 -ExitBootServices -OwnedProcessPool
    if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
    & pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput $pe -Out $pristine -TotalSectors 65536
    if($LASTEXITCODE -ne 0){throw 'Image build failed'}
    $receipt.imageHash=(Get-FileHash $pristine).Hash
    $initial=[IO.File]::ReadAllBytes($pristine)
    $array=[BitConverter]::ToInt64($initial,512+72)*512
    $start=[BitConverter]::ToInt64($initial,$array+128+32)*512
    $stop=([BitConverter]::ToInt64($initial,$array+128+40)+1)*512
    if($start -lt 17408 -or $stop -gt $initial.Length-16896 -or $stop -le $start){throw 'Invalid data partition fixture'}
    foreach($backend in @('virtio','ide')){
        $disk=Join-Path $OutDir "$backend.img"
        [IO.File]::Copy($pristine,$disk)
        Run "$backend-save" $backend $disk 'SAVED'
        $savedHash=(Get-FileHash $disk).Hash
        Run "$backend-reboot" $backend $disk 'RESTORED'
        if((Get-FileHash $disk).Hash -ne $savedHash){throw 'Recovery changed the disk image'}
        $after=[IO.File]::ReadAllBytes($disk)
        if($after.Length -ne $initial.Length){throw 'Disk size changed'}
        $changed=$false
        for($i=0;$i -lt $after.Length;$i++){
            if($after[$i] -ne $initial[$i]){
                if($i -lt $start -or $i -ge $stop){throw "Write outside data partition at $i"}
                $changed=$true
            }
        }
        if(-not $changed){throw 'Data partition remained blank'}
    }
    if($receipt.runs.Count -ne 4){throw 'Incomplete image proof'}
    $receipt.passed=$true
}finally{
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir"
}
