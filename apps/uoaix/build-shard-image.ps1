[CmdletBinding()]
param([string]$Kernel='',[string]$OutDir='',
    # The UEFI heap's floor, in 4 KiB pages; the stub sizes the heap at boot from the memory map and refuses below this.
    # A fresh world's server peaks at 944 MiB by game hour 81 (WATERMARK heap-peak, trial 44695) and held 978 MiB live at
    # hour 335; 262144 pages is 1 GiB. The BIOS loader takes the contiguous RAM above 1 MiB instead, capped at 3 GiB.
    [ValidateRange(16384,786432)][int]$HeapFloorPages=262144)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# UOAIX stage D4 (UOAIX.md section 9): the composite server CDX as a BIOS payload and as a UEFI application on a GPT
# image, both packed into one El Torito ISO (build-iso.ps1, IsoBoot.md). The world lives on a separate virtio data disk
# (stage D3, install-map-cache.ps1 -WorldDisk); nothing of the user's client goes into the image.
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/uoaix/shard-image-'+[guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory -Path $OutDir)
function Compile([string]$Source,[string]$Name,[bool]$Pet,[string]$Flags=''){
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw "RAM admission: $free KiB free"}
    $argv=@('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',$Source,'-Out',(Join-Path $OutDir "$Name.cdx"),'-Log',(Join-Path $OutDir "$Name.compile.log"),'-Kernel',$Kernel)
    if($Pet){$argv+='-Pet'}
    if($Flags){$argv+=@('-RawFlags',$Flags)}
    & pwsh @argv
    if($LASTEXITCODE -ne 0){throw "$Name compile failed"}
}
. (Join-Path $repo 'build/quire-map.ps1')
$server=Join-Path $PSScriptRoot 'CompositeGameServer.codex'
$text=[IO.File]::ReadAllText($server)
# The UEFI entry initialises the runtime after ExitBootServices; the BIOS payload's normal CDX startup already has.
$needle=[regex]::Match($text,'(?m)^  opening : \[[^\]]*\] Integer = act\r?\n')
if(-not $needle.Success -or [regex]::Matches($text,'(?m)^  opening : ').Count -ne 1){throw 'Server opening not found exactly once'}
$nl=if($needle.Value.EndsWith("`r`n")){"`r`n"}else{"`n"}
$uefiBody=$text.Insert($needle.Index+$needle.Length,'   initialized <- runtime-init 0'+$nl)
$ordered=Resolve-CiteOrder -RootLines ($uefiBody -split '\r?\n') -Repo $repo -SeedSeen @{'Uoaix::CompositeGameServer'=$true}
$uefiSource=Join-Path $OutDir 'shard-uefi.codex'
[IO.File]::WriteAllText($uefiSource,((Format-CiteChapters -Ordered $ordered)-join "`r`n")+"`r`n"+$uefiBody)
Compile $server 'shard-bios' $false
Compile $uefiSource 'shard-uefi' $true 'pe-base'
$pe=Join-Path $OutDir 'BOOTX64.EFI';$image=Join-Path $OutDir 'shard-uefi.img';$iso=Join-Path $OutDir 'shard.iso'
& pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput (Join-Path $OutDir 'shard-uefi.cdx') -Out $pe -HeapFloorPages $HeapFloorPages -ExitBootServices -OwnedProcessPool
if($LASTEXITCODE -ne 0){throw 'PE build failed'}
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput $pe -Out $image -TotalSectors 65536
if($LASTEXITCODE -ne 0){throw 'GPT build failed'}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-iso.ps1') -BiosCdx (Join-Path $OutDir 'shard-bios.cdx') -UefiImage $image -Out $iso -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'ISO build failed'}
$receipt=[ordered]@{kernel=(Get-FileHash $Kernel).Hash;uefiSource=(Get-FileHash $uefiSource).Hash;bios=(Get-FileHash (Join-Path $OutDir 'shard-bios.cdx')).Hash;
    uefi=(Get-FileHash (Join-Path $OutDir 'shard-uefi.cdx')).Hash;heapFloorPages=$HeapFloorPages;image=(Get-FileHash $image).Hash;iso=(Get-FileHash $iso).Hash}
[IO.File]::WriteAllText((Join-Path $OutDir 'shard-image.json'),($receipt|ConvertTo-Json))
"SHARD IMAGE iso=$iso image=$image heap-floor-pages=$HeapFloorPages"
