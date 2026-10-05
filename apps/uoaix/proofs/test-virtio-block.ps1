[CmdletBinding()]
param(
    [string]$Kernel = '', [string]$OutDir = '',
    [string]$Qemu = 'D:/Program Files/qemu/qemu-system-x86_64.exe',
    [ValidateSet('tcg','whpx')][string]$Accel = 'tcg'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
$Qemu = (Resolve-Path -LiteralPath $Qemu).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/uoaix/virtio-' + [guid]::NewGuid().ToString('N')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new' }
[void](New-Item -ItemType Directory -Path $OutDir)
$receipt = [ordered]@{ kernelHash=(Get-FileHash -LiteralPath $Kernel).Hash; qemu=$Qemu; accelerator=$Accel; runs=@(); passed=$false }
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($free -lt 1572864) { throw 'Less than 1.5 GiB free before guest' }
    return $free
}
function Compile([string]$Name) {
    $free=Admit
    $cdx=Join-Path $OutDir "$Name.cdx"
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot "$Name.codex") -Out $cdx -Log (Join-Path $OutDir "$Name.compile.log") -Kernel $Kernel | Out-Host
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutDir "$Name.compile.log") | Out-Host; throw "$Name compile failed" }
    return $cdx
}
function Run-Qemu([string]$Name,[string]$Cdx,[string]$Disk,[string]$Device,[bool]$ReadOnly,[string]$Expected) {
    $free=Admit
    $serial=Join-Path $OutDir "$Name.serial"
    $err=Join-Path $OutDir "$Name.stderr"
    $argv=@('-accel',$Accel,'-machine','pc,kernel-irqchip=off','-m','3072',
        '-kernel',('"'+$Cdx+'"'),'-device','loader,addr=0xfe8,data=0xc0000000,data-len=4',
        '-serial',('"file:'+$serial+'"'),'-serial','null',
        '-device','isa-debug-exit,iobase=0xf4,iosize=0x04','-display','none','-no-reboot','-net','none')
    if ($Accel -eq 'tcg') { $argv+=@('-cpu','max') }
    if ($Device) {
        $drive="if=none,id=vblk,format=raw,file=$Disk"
        if ($ReadOnly) { $drive+=',readonly=on' }
        $argv+=@('-drive',('"'+$drive+'"'),'-device',$Device)
    }
    $p=Start-Process $Qemu -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError $err
    Write-Output "$Name QEMU PID=$($p.Id) log=$serial"
    try {
        if (-not $p.WaitForExit(30000)) { throw "$Name QEMU timeout" }
        $receipt.runs+=@{ name=$Name; pid=$p.Id; freeKiB=$free; exit=$p.ExitCode; artifactHash=(Get-FileHash -LiteralPath $Cdx).Hash; diskHash=(Get-FileHash -LiteralPath $Disk).Hash }
        if ($p.ExitCode -ne 1) { Get-Content $err | Out-Host; throw "$Name QEMU did not exit through debug-exit zero" }
        $raw=[IO.File]::ReadAllText($serial) -replace "`r",''
        $lines=@($raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP:|WD:|STACK:)' })
        $body=(($lines -join "`n").TrimEnd([char]10))+"`n"
        [IO.File]::WriteAllText((Join-Path $OutDir "$Name.actual"),$body)
        if ($body -cne $Expected) { Write-Output $body; throw "$Name exact oracle mismatch" }
        Write-Output "PASS $Name"
    } finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        $p.Dispose()
    }
}
try {
    $queue=Compile 'VirtioQueueProof'
    $free=Admit
    $actual=Join-Path $OutDir 'queue.actual'
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $queue -OutFile $actual
    if ($LASTEXITCODE -ne 0) { throw 'Queue proof run failed' }
    $expected=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'VirtioQueueProof.expected')) -replace "`r",''
    if ([IO.File]::ReadAllText($actual) -cne $expected) { throw 'Queue exact oracle mismatch' }
    $cdx=Compile 'VirtioBlockProof'
    $disk=Join-Path $OutDir 'block.disk'
    $bytes=[byte[]]::new(32768)
    $bytes[0]=161; $bytes[511]=90; $bytes[63*512]=195
    [IO.File]::WriteAllBytes($disk,$bytes)
    $prefix="VBX sectors=64 readonly=0 flush=1`nPASS sector zero read`nPASS last sector read`nPASS sector bounds refuse before publish`n"
    Run-Qemu 'write' $cdx $disk 'virtio-blk-pci,disable-legacy=on,drive=vblk' $false ($prefix+"VBX prior-write=0`nPASS write flush readback`n")
    $written=[IO.File]::ReadAllBytes($disk)
    for($i=0;$i -lt 512;$i++){if($written[10*512+$i] -ne (($i*37+11) -band 255)){throw "Host sector mismatch at byte $i"}}
    Run-Qemu 'reboot' $cdx $disk 'virtio-blk-pci,disable-legacy=on,drive=vblk' $false ($prefix+"VBX prior-write=1`nPASS write flush readback`n")
    $before=(Get-FileHash -LiteralPath $disk).Hash
    $ro="VBX sectors=64 readonly=1 flush=1`nPASS sector zero read`nPASS last sector read`nPASS sector bounds refuse before publish`nVBX prior-write=1`nPASS read-only write refused`n"
    Run-Qemu 'readonly' $cdx $disk 'virtio-blk-pci,disable-legacy=on,drive=vblk' $true $ro
    if ((Get-FileHash -LiteralPath $disk).Hash -ne $before) { throw 'Read-only device changed the image' }
    Run-Qemu 'absent' $cdx $disk '' $false "VBX REFUSED virtio block device absent`n"
    Run-Qemu 'legacy' $cdx $disk 'virtio-blk-pci,disable-modern=on,drive=vblk' $false "VBX REFUSED virtio notification capability missing`n"
    if ($receipt.runs.Count -ne 5) { throw 'Incomplete virtio proof' }
    $receipt.passed=$true
} finally {
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir"
}
