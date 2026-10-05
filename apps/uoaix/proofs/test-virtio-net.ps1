[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='',
    [string]$Qemu='D:/Program Files/qemu/qemu-system-x86_64.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
Set-Location $repo
$Kernel=(Resolve-Path $Kernel).Path
$Qemu=(Resolve-Path $Qemu).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/virtio-net-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
$receipt=[ordered]@{kernelHash=(Get-FileHash $Kernel).Hash;passed=$false;runs=@()}
function Admit { $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory;if($free -lt 1572864){throw 'RAM admission'};return $free }
function Compile([string]$Name){
    $free=Admit
    & pwsh -NoProfile -File build/compile.ps1 -Src "apps/uoaix/proofs/$Name.codex" -Out "$OutDir/$Name.cdx" -Log "$OutDir/$Name.log" -Kernel $Kernel | Out-Host
    if($LASTEXITCODE -ne 0){Get-Content "$OutDir/$Name.log"|Out-Host;throw "Compile failed $Name"}
}
function Init-Arm([string]$Name,[string]$Device,[string]$Expected){
    $free=Admit
    $serial="$OutDir/$Name.serial"
    $argv=@('-accel','tcg','-cpu','max','-machine','pc,kernel-irqchip=off','-m','3072','-kernel',('"'+$OutDir+'/VirtioNetInitProof.cdx"'),
        '-device','loader,addr=0xfe8,data=0xc0000000,data-len=4','-serial',('"file:'+$serial+'"'),'-serial','null',
        '-device','isa-debug-exit,iobase=0xf4,iosize=0x04','-display','none','-no-reboot')
    if($Device){$argv+=@('-netdev','user,id=n0','-device',($Device+',netdev=n0,mac=52:54:00:12:34:56'))}else{$argv+=@('-net','none')}
    $p=Start-Process $Qemu -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError "$OutDir/$Name.stderr"
    Write-Output "$Name QEMU PID=$($p.Id) log=$serial"
    try{
        if(-not $p.WaitForExit(30000)){throw "$Name timed out"}
        $receipt.runs+=@{name=$Name;pid=$p.Id;exit=$p.ExitCode;freeKiB=$free}
        if($p.ExitCode -ne 1){Get-Content "$OutDir/$Name.stderr";throw "$Name bad QEMU exit"}
        $raw=[IO.File]::ReadAllText($serial)-replace "`r",''
        $lines=@($raw -split "`n"|Where-Object{$_ -notmatch '^(HEAP:|WD:|STACK:)'})
        $body=($lines -join "`n").TrimEnd([char]10)+"`n"
        if($body -cne $Expected){Write-Output $body;throw "$Name oracle mismatch"}
        Write-Output "PASS $Name"
    }finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force};$p.Dispose()}
}
try{
    Compile 'VirtioNetGuardProof'
    $free=Admit
    & pwsh -NoProfile -File build/test-run.ps1 -Kernel "$OutDir/VirtioNetGuardProof.cdx" -OutFile "$OutDir/guards.out"
    if($LASTEXITCODE -ne 0){throw 'Guard guest failed'}
    $guards=[IO.File]::ReadAllText("$OutDir/guards.out")
    Write-Output $guards
    if(-not $guards.Contains('VIRTIO NET GUARDS failures=0') -or $guards -match '\bFAIL\b|!EXC|OUT OF MEMORY'){throw 'Guard assertions failed'}
    Compile 'VirtioNetInitProof'
    $ready="VNX READY rx=32 tx=8`nVNX MAC 82 86`nVNX BIND virtio-net`n"
    Init-Arm 'modern' 'virtio-net-pci,disable-legacy=on' ("VNX PCI 4161`n"+$ready)
    Init-Arm 'transitional' 'virtio-net-pci,disable-legacy=off,disable-modern=off' ("VNX PCI 4096`n"+$ready)
    Init-Arm 'legacy' 'virtio-net-pci,disable-modern=on' "VNX REFUSED virtio notification capability missing`nVNX BIND unavailable`n"
    Init-Arm 'absent' '' "VNX REFUSED absent`n"
    & pwsh -NoProfile -File apps/uoaix/test-admin.ps1 -Kernel $Kernel -Qemu $Qemu -OutDir "$OutDir/admin"
    $receipt.adminExit=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw 'Admin conversation over virtio failed'}
    $receipt.passed=$true
    Write-Output 'UOAIX VIRTIO NET PASS'
}finally{
    [IO.File]::WriteAllText("$OutDir/result.json",($receipt|ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir/result.json"
}
