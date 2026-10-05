[CmdletBinding()]
param([string]$Kernel='',[string]$OutDir='',
    [Parameter(Mandatory)][string]$Nasm,[string]$Qemu='D:/Program Files/qemu/qemu-system-x86_64.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path $Kernel).Path;$Nasm=(Resolve-Path $Nasm).Path;$Qemu=(Resolve-Path $Qemu).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/shard-restart-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$vm=Join-Path $repo 'tools/codex-vm.exe'
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;vmHash=(Get-FileHash $vm).Hash;guestMemMB=1024;accelerator='tcg';runs=@();passed=$false}
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw 'RAM admission'}
    return $free
}
function Fnv([byte[]]$Bytes,[int]$Skip=-1){
    [long]$hash=2166136261
    for($i=0;$i -lt $Bytes.Length;$i++){
        if($Skip -ge 0 -and $i -ge $Skip -and $i -lt $Skip+8){continue}
        $hash=(($hash -bxor $Bytes[$i])*16777619)-band 4294967295
    }
    return $hash
}
function Read-Live([string]$Path){
    if(-not (Test-Path $Path)){return ''}
    $f=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader=[IO.StreamReader]::new($f)
    try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
function Compile([string]$Source,[string]$Name,[bool]$Pet){
    $free=Admit
    $argv=@('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',$Source,'-Out',(Join-Path $OutDir "$Name.cdx"),'-Log',(Join-Path $OutDir "$Name.compile.log"),'-Kernel',$Kernel)
    if($Pet){$argv+='-Pet'}
    & pwsh @argv
    if($LASTEXITCODE -ne 0){throw "$Name compile failed"}
}
function Boot([string]$Mode,[string]$Phase,[string]$Disk){
    $free=Admit;$name="$Mode-$Phase";$serial=Join-Path $OutDir "$name.serial";$err=Join-Path $OutDir "$name.stderr"
    $backend=if($Mode -eq 'native'){'ide'}else{'virtio'}
    $exe=$Qemu
    if($Mode -eq 'native'){
        $exe=$vm
        $argv=@('-kernel',('"'+$pristine+'"'),'-disk',('"'+$Disk+'"'),'-output',('"'+$serial+'"'),'-mem','1024','-headless','-uefi')
    }else{
        $argv=@('-accel','tcg','-cpu','max','-machine','pc','-m','1024',
            '-drive',('"if=none,id=vblk,format=raw,file='+$Disk+'"'),'-device','virtio-blk-pci,disable-legacy=on,drive=vblk',
            '-serial',('"file:'+$serial+'"'),'-serial','null','-device','isa-debug-exit,iobase=0xf4,iosize=0x04','-display','none','-no-reboot','-net','none')
        if($Mode -ne 'uefi-disk'){$argv+=@('-cdrom',('"'+$iso+'"'),'-boot','order=d,strict=on')}
        if($Mode -ne 'bios-iso'){
            $vars=Join-Path $OutDir "$name.vars.fd"
            [IO.File]::Copy((Join-Path $firmware 'edk2-i386-vars.fd'),$vars)
            $argv+=@('-drive',('"if=pflash,format=raw,unit=0,readonly=on,file='+(Join-Path $firmware 'edk2-x86_64-code.fd')+'"'),'-drive',('"if=pflash,format=raw,unit=1,file='+$vars+'"'))
        }
    }
    $p=Start-Process $exe -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError $err
    [IO.File]::WriteAllText((Join-Path $OutDir 'run.json'),(@{pid=$p.Id;guests=1;log=$serial;owner=$env:CODEX_SESSION_ID;running=$true}|ConvertTo-Json))
    Write-Output "$name PID=$($p.Id) log=$serial"
    $killed=$false
    try{
        if($Phase -eq 'write'){
            $deadline=[datetime]::UtcNow.AddSeconds(45)
            do{
                $raw=Read-Live $serial
                if($raw -match 'FAIL|!EXC|OOM'){throw "$name guest failure: $raw"}
                if($raw -match 'SHARD STORE SAVED\r?\n'){break}
                if($p.HasExited){throw "$name exited before commit marker: $raw"}
                Start-Sleep -Milliseconds 100
            }while([datetime]::UtcNow -lt $deadline)
            if($raw -notmatch 'SHARD STORE SAVED\r?\n' -or $p.HasExited){throw "$name did not remain live after commit"}
            Stop-Process -Id $p.Id -Force
            if(-not $p.WaitForExit(5000)){throw "$name kill did not complete"}
            $killed=$true
        }else{
            if(-not $p.WaitForExit(45000)){throw "$name timeout"}
            if($p.ExitCode -ne 1){throw "$name abnormal exit: $([IO.File]::ReadAllText($err))"}
        }
        $raw=[IO.File]::ReadAllText($serial)-replace "`r",''
        $stderr=[IO.File]::ReadAllText($err)
        if($stderr -match 'DROPPED|cannot be opened for write|every write.*LOST|(?:nopath|nodata|oob|openfail)=[1-9]'){throw "$name lost output/writes: $stderr"}
        if($Phase -ne 'write' -and $Mode -eq 'native' -and $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1'){throw 'Native recovery lacked normal exit'}
        if($raw -match 'FAIL|!EXC|OOM' -or [regex]::Matches($raw,'SHARD STORE START').Count -ne 1){throw "$name invalid output: $raw"}
        if($Mode -eq 'uefi-iso' -and $raw -notmatch 'starting Boot[0-9A-F]+ "UEFI QEMU DVD-ROM'){throw 'UEFI optical boot not established'}
        $body=$raw.Substring($raw.IndexOf('SHARD STORE START')).TrimEnd([char]10)+"`n"
        $expected="SHARD STORE START`nSHARD STORE DEVICE $backend`n"
        if($Phase -eq 'write'){$expected+="PASS owner mismatch refuses before disk mutation`nPASS blank composite recovery refused`nSHARD STORE SAVED`n"}
        elseif($Phase -eq 'suffix'){$expected+="PASS unsupported committed suffix refused`nSHARD STORE END`n"}
        else{$expected+=([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ShardRestartBoot.expected'))-replace "`r",'')+"SHARD STORE END`n"}
        [IO.File]::WriteAllText((Join-Path $OutDir "$name.actual"),$body)
        if($body -cne $expected){throw "$name oracle mismatch: $body"}
        $receipt.runs+=@{name=$name;pid=$p.Id;freeKiB=$free;exit=$p.ExitCode;killedAfterCommit=$killed;diskHash=(Get-FileHash $Disk).Hash}
        Write-Output "PASS $name"
    }finally{
        if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}
        [IO.File]::WriteAllText((Join-Path $OutDir 'run.json'),(@{pid=$p.Id;guests=0;log=$serial;owner=$env:CODEX_SESSION_ID;running=$false}|ConvertTo-Json))
        $p.Dispose()
    }
}
try{
    . (Join-Path $repo 'build/quire-map.ps1')
    $body=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ShardRestartBoot.codex'))
    $ordered=Resolve-CiteOrder -RootLines ($body -split '\r?\n') -Repo $repo
    $unit=((Format-CiteChapters -Ordered $ordered)-join "`r`n")+"`r`n"+$body
    $source=Join-Path $OutDir 'shard-uefi.codex';[IO.File]::WriteAllText($source,$unit)
    $needle='    initialized <- runtime-init 0'
    if([regex]::Matches($unit,[regex]::Escape($needle)).Count -ne 1){throw 'Startup transformation ambiguous'}
    $biosSource=Join-Path $OutDir 'shard-bios.codex';[IO.File]::WriteAllText($biosSource,$unit.Replace($needle,''))
    $receipt.uefiSourceHash=(Get-FileHash $source).Hash;$receipt.biosSourceHash=(Get-FileHash $biosSource).Hash
    Compile $source 'shard-uefi' $true
    Compile $biosSource 'shard-bios' $false
    $pe=Join-Path $OutDir 'BOOTX64.EFI';$pristine=Join-Path $OutDir 'pristine.img';$iso=Join-Path $OutDir 'shard.iso'
    & pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput (Join-Path $OutDir 'shard-uefi.cdx') -Out $pe -HeapPages 16384 -ExitBootServices -OwnedProcessPool
    if($LASTEXITCODE -ne 0){throw 'PE build failed'}
    & pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput $pe -Out $pristine -TotalSectors 65536
    if($LASTEXITCODE -ne 0){throw 'GPT build failed'}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot '../build-iso.ps1') -BiosCdx (Join-Path $OutDir 'shard-bios.cdx') -UefiImage $pristine -Nasm $Nasm -Out $iso
    if($LASTEXITCODE -ne 0){throw 'ISO build failed'}
    $receipt.imageHash=(Get-FileHash $pristine).Hash;$receipt.isoHash=(Get-FileHash $iso).Hash
    $initial=[IO.File]::ReadAllBytes($pristine)
    $array=[BitConverter]::ToInt64($initial,584)*512
    $espStart=[BitConverter]::ToInt64($initial,$array+32)*512;$espStop=([BitConverter]::ToInt64($initial,$array+40)+1)*512
    $dataStart=[BitConverter]::ToInt64($initial,$array+160)*512;$dataStop=([BitConverter]::ToInt64($initial,$array+168)+1)*512
    if($espStart -lt 17408 -or $espStop -gt $dataStart -or $dataStart -ge $dataStop -or $dataStop -gt $initial.Length-16896){throw 'Fixture partition bounds'}
    $firmware=Join-Path (Split-Path $Qemu) 'share';$dataHashes=@()
    foreach($mode in @('native','uefi-disk','bios-iso','uefi-iso')){
        $baseline=[byte[]]$initial.Clone()
        if($mode -like '*-iso'){[Array]::Clear($baseline,$espStart,$espStop-$espStart)}
        $disk=Join-Path $OutDir "$mode.img";[IO.File]::WriteAllBytes($disk,$baseline)
        $before=(Get-FileHash $disk).Hash
        Boot $mode 'write' $disk
        $saved=(Get-FileHash $disk).Hash
        if($saved -eq $before){throw 'Killed writer persisted no data'}
        Boot $mode 'read' $disk
        if((Get-FileHash $disk).Hash -ne $saved){throw 'Recovery changed disk'}
        $after=[IO.File]::ReadAllBytes($disk)
        if($after.Length -ne $baseline.Length){throw 'Disk size changed'}
        for($i=0;$i -lt $after.Length;$i++){if(($i -lt $dataStart -or $i -ge $dataStop) -and $after[$i] -ne $baseline[$i]){throw "Write outside data partition at $i"}}
        $sha=[Security.Cryptography.SHA256]::Create();try{$dataHashes+=[Convert]::ToHexString($sha.ComputeHash($after,$dataStart,$dataStop-$dataStart))}finally{$sha.Dispose()}
    }
    if(@($dataHashes|Select-Object -Unique).Count -ne 1){throw 'Backends emitted different committed state bytes'}
    $suffix=Join-Path $OutDir 'unsupported-suffix.img'
    $bytes=[IO.File]::ReadAllBytes((Join-Path $OutDir 'native.img'))
    $length=[BitConverter]::ToInt64($bytes,$dataStart+40)
    $at=$dataStart+(1+[long][Math]::Ceiling($length/512.0))*512
    if($at+1024 -gt $dataStop){throw 'Suffix fixture outside data partition'}
    $header=[byte[]]::new(512)
    $cells=@(0x31534f55,1,2,1,0,1,0,(Fnv ([byte[]]@(42))))
    for($i=0;$i -lt $cells.Length;$i++){[BitConverter]::GetBytes([long]$cells[$i]).CopyTo($header,$i*8)}
    $prefix=[byte[]]$header[0..63]
    [BitConverter]::GetBytes([long](Fnv $prefix 48)).CopyTo($header,48)
    [Array]::Copy($header,0,$bytes,$at,512);$bytes[$at+512]=42
    [IO.File]::WriteAllBytes($suffix,$bytes)
    $suffixHash=(Get-FileHash $suffix).Hash
    Boot 'native' 'suffix' $suffix
    if((Get-FileHash $suffix).Hash -ne $suffixHash){throw 'Refused recovery wrote to disk'}
    if($receipt.runs.Count -ne 9 -or (Get-FileHash $iso).Hash -ne $receipt.isoHash){throw 'Incomplete proof or changed ISO'}
    $receipt.dataHash=$dataHashes[0];$receipt.passed=$true
}finally{[IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6));Write-Output "Evidence: $OutDir"}
