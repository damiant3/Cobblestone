[CmdletBinding()]
param([Parameter(Mandatory)][string]$Iso,[Parameter(Mandatory)][string]$DataTemplate,
    [Parameter(Mandatory)][string]$OutDir,[string]$Qemu='D:/Program Files/qemu/qemu-system-x86_64.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Iso=(Resolve-Path $Iso).Path;$DataTemplate=(Resolve-Path $DataTemplate).Path;$Qemu=(Resolve-Path $Qemu).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$initial=[IO.File]::ReadAllBytes($DataTemplate)
if($initial.Length -lt 17408 -or [Text.Encoding]::ASCII.GetString($initial,512,8) -cne 'EFI PART'){throw 'Expected GPT data fixture'}
$array=[BitConverter]::ToInt64($initial,584)*512
if($array -lt 1024 -or $array -gt $initial.Length-256){throw 'GPT fixture entry bounds'}
$espStart=[BitConverter]::ToInt64($initial,$array+32)*512
$espStop=([BitConverter]::ToInt64($initial,$array+40)+1)*512
$dataStart=[BitConverter]::ToInt64($initial,$array+160)*512
$dataStop=([BitConverter]::ToInt64($initial,$array+168)+1)*512
if($espStart -lt 17408 -or $espStop -gt $dataStart -or $dataStart -ge $dataStop -or $dataStop -gt $initial.Length-16896){throw 'Fixture partition bounds'}
[Array]::Clear($initial,$espStart,$espStop-$espStart)
[Array]::Clear($initial,$dataStart,$dataStop-$dataStart)
$receipt=[ordered]@{isoHash=(Get-FileHash $Iso).Hash;templateHash=(Get-FileHash $DataTemplate).Hash;guestMemMB=1024;accelerator='tcg';runs=@();passed=$false}
$firmware=Join-Path (Split-Path $Qemu) 'share'
function Boot([string]$Mode,[string]$Phase,[string]$Disk){
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw 'RAM admission'}
    $name="$Mode-$Phase";$serial=Join-Path $OutDir "$name.serial";$err=Join-Path $OutDir "$name.stderr"
    $argv=@('-accel','tcg','-cpu','max','-machine','pc','-m','1024','-cdrom',('"'+$Iso+'"'),'-boot','order=d,strict=on',
        '-drive',('"if=none,id=vblk,format=raw,file='+$Disk+'"'),'-device','virtio-blk-pci,disable-legacy=on,drive=vblk',
        '-serial',('"file:'+$serial+'"'),'-serial','null','-device','isa-debug-exit,iobase=0xf4,iosize=0x04','-display','none','-no-reboot','-net','none')
    if($Mode -eq 'uefi'){
        $vars=Join-Path $OutDir "$name.vars.fd";[IO.File]::Copy((Join-Path $firmware 'edk2-i386-vars.fd'),$vars)
        $argv+=@('-drive',('"if=pflash,format=raw,unit=0,readonly=on,file='+(Join-Path $firmware 'edk2-x86_64-code.fd')+'"'),'-drive',('"if=pflash,format=raw,unit=1,file='+$vars+'"'))
    }
    $p=Start-Process $Qemu -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError $err
    Write-Output "$name PID=$($p.Id) log=$serial"
    try{
        if(-not $p.WaitForExit(45000)){throw "$name timeout"}
        $receipt.runs+=@{name=$name;pid=$p.Id;exit=$p.ExitCode;freeKiB=$free;diskHash=(Get-FileHash $Disk).Hash}
        if($p.ExitCode -ne 1){throw "$name bad exit: $([IO.File]::ReadAllText($err))"}
        $raw=[IO.File]::ReadAllText($serial)-replace "`r",''
        if($raw -match 'FAIL|!EXC|OOM' -or [regex]::Matches($raw,'UEFI STORE START').Count -ne 1){throw "$name missing completion: $raw"}
        if($Mode -eq 'uefi' -and $raw -notmatch 'starting Boot[0-9A-F]+ "UEFI QEMU DVD-ROM'){throw 'UEFI did not report CD boot'}
        $body=$raw.Substring($raw.IndexOf('UEFI STORE START')).TrimEnd([char]10)+"`n"
        $mmio=[regex]::Match($body,'(?m)^UEFI STORE MMIO=([0-9]+)$')
        if(-not $mmio.Success){throw 'MMIO address absent'}
        $expected="UEFI STORE START`nUEFI STORE DEVICE virtio`n"+$mmio.Value+"`n"
        if($Phase -eq 'save'){$expected+="PASS blank store recovery refused`n"+([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownRestartWrite.expected'))-replace "`r",'')}
        else{$lines=([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownRestartRead.expected'))-replace "`r",'')-split "`n";$expected+=($lines[2..($lines.Length-1)]-join "`n")+"PASS recovered head mismatch refused`n"}
        $expected+="UEFI STORE END`n"
        if($body -cne $expected){throw "$name oracle mismatch: $body"}
        Write-Output "PASS $name"
    }finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force};$p.Dispose()}
}
try{
    foreach($mode in @('bios','uefi')){
        $disk=Join-Path $OutDir "$mode.disk";[IO.File]::WriteAllBytes($disk,$initial)
        Boot $mode 'save' $disk
        $saved=(Get-FileHash $disk).Hash
        Boot $mode 'reboot' $disk
        if((Get-FileHash $disk).Hash -ne $saved){throw 'Recovery wrote to disk'}
        $after=[IO.File]::ReadAllBytes($disk);$changed=$false
        if($after.Length -ne $initial.Length){throw 'Disk size changed'}
        for($i=0;$i -lt $after.Length;$i++){if($after[$i] -ne $initial[$i]){if($i -lt $dataStart -or $i -ge $dataStop){throw "Write outside data partition at $i"};$changed=$true}}
        if(-not $changed){throw 'No disk commit observed'}
    }
    if((Get-FileHash $Iso).Hash -ne $receipt.isoHash -or $receipt.runs.Count -ne 4){throw 'Image changed or incomplete run'}
    $receipt.passed=$true
}finally{[IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6));Write-Output "Evidence: $OutDir"}
