[CmdletBinding()]
param([Parameter(Mandatory)][string]$BiosCdx,[Parameter(Mandatory)][string]$UefiImage,
    [Parameter(Mandatory)][string]$Nasm,[Parameter(Mandatory)][string]$Out)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$BiosCdx=(Resolve-Path -LiteralPath $BiosCdx).Path
$UefiImage=(Resolve-Path -LiteralPath $UefiImage).Path
$Nasm=(Resolve-Path -LiteralPath $Nasm).Path
$Out=[IO.Path]::GetFullPath($Out)
foreach($path in @($Out,($Out+'.bios.bin'),($Out+'.json'))){if(Test-Path -LiteralPath $path){throw 'Output and sidecars must be new'}}
$cdx=[IO.File]::ReadAllBytes($BiosCdx)
if($cdx.Length -lt 512 -or [Text.Encoding]::ASCII.GetString($cdx,0,4) -cne 'CDX1'){throw 'Expected CDX1'}
$text=[BitConverter]::ToInt64($cdx,168)
$textSize=[BitConverter]::ToInt64($cdx,176)
$rodata=[BitConverter]::ToInt64($cdx,184)
$rodataSize=[BitConverter]::ToInt64($cdx,192)
if($text -ne 224 -or $rodata -lt $text -or $rodataSize -lt 0 -or $rodataSize -gt $cdx.Length-$rodata){throw 'Invalid CDX sections'}
if($textSize -lt 32 -or $textSize -gt $cdx.Length-$text -or $rodata -ne $text+(($textSize+7)-band -8)){throw 'CDX sections do not tile'}
$debug=[BitConverter]::ToInt32($cdx,220)
$contentEnd=if($debug -eq 0){$cdx.Length}else{$debug}
if($contentEnd -lt $rodata+$rodataSize -or $contentEnd -gt $cdx.Length){throw 'CDX content extent invalid'}
$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=[Convert]::ToHexString($sha.ComputeHash($cdx,224,$contentEnd-224))}finally{$sha.Dispose()}
if($hash -cne [Convert]::ToHexString($cdx,8,32)){throw 'CDX content hash mismatch'}
if([BitConverter]::ToUInt32($cdx,$text) -ne 0x1badb002 -or [BitConverter]::ToUInt32($cdx,$text+12) -ne 0x100000 -or [BitConverter]::ToUInt32($cdx,$text+28) -ne 0x100020){throw 'Unsupported BIOS trampoline'}
$payloadSize=$rodata+$rodataSize-$text
if($payloadSize -le 0 -or $payloadSize -gt 16777216){throw 'BIOS payload outside 16 MiB budget'}
$payloadBlocks=[int][Math]::Ceiling($payloadSize/2048.0)
$disk=[IO.File]::ReadAllBytes($UefiImage)
if($disk.Length -lt 17408 -or [Text.Encoding]::ASCII.GetString($disk,512,8) -cne 'EFI PART'){throw 'Expected GPT UEFI image'}
$entries=[BitConverter]::ToInt64($disk,584)*512
if($entries -lt 1024 -or $entries -gt $disk.Length-128){throw 'Invalid GPT entry location'}
if([Convert]::ToHexString($disk,$entries,16) -cne '28732AC11FF8D211BA4B00A0C93EC93B'){throw 'First GPT partition must be ESP'}
$first=[BitConverter]::ToInt64($disk,$entries+32)
$last=[BitConverter]::ToInt64($disk,$entries+40)
if($first -lt 34 -or $last -lt $first -or $last -ge $disk.Length/512){throw 'ESP bounds invalid'}
$espSectors=$last-$first+1
if($espSectors -gt 65535){throw 'ESP exceeds El Torito sector-count budget'}
$espSize=$espSectors*512
$espBlocks=[int][Math]::Ceiling($espSize/2048.0)
$espLba=24+$payloadBlocks
$total=$espLba+$espBlocks
$iso=[byte[]]::new($total*2048)
function Bytes([int]$At,[byte[]]$Value){[Array]::Copy($Value,0,$iso,$At,$Value.Length)}
function Ascii([int]$At,[string]$Value){Bytes $At ([Text.Encoding]::ASCII.GetBytes($Value))}
function U16([int]$At,[int]$Value){Bytes $At ([BitConverter]::GetBytes([uint16]$Value))}
function U32([int]$At,[long]$Value){Bytes $At ([BitConverter]::GetBytes([uint32]$Value))}
function Both16([int]$At,[int]$Value){U16 $At $Value;$b=[BitConverter]::GetBytes([uint16]$Value);[Array]::Reverse($b);Bytes ($At+2) $b}
function Both32([int]$At,[long]$Value){U32 $At $Value;$b=[BitConverter]::GetBytes([uint32]$Value);[Array]::Reverse($b);Bytes ($At+4) $b}
function Record([int]$At,[int]$Lba,[int]$Size,[byte[]]$Name,[bool]$Directory){
    $length=33+$Name.Length;if($length%2){$length++}
    $iso[$At]=[byte]$length
    Both32 ($At+2) $Lba;Both32 ($At+10) $Size
    Bytes ($At+18) ([byte[]]@(126,1,1,0,0,0,0))
    if($Directory){$iso[$At+25]=2}
    Both16 ($At+28) 1;$iso[$At+32]=[byte]$Name.Length;Bytes ($At+33) $Name
    return $length
}
$pvd=16*2048;$iso[$pvd]=1;Ascii ($pvd+1) 'CD001';$iso[$pvd+6]=1
Ascii ($pvd+8) ('CODEX'.PadRight(32));Ascii ($pvd+40) ('UOAIX'.PadRight(32))
Both32 ($pvd+80) $total;Both16 ($pvd+120) 1;Both16 ($pvd+124) 1;Both16 ($pvd+128) 2048
Both32 ($pvd+132) 10;U32 ($pvd+140) 19
$big=[BitConverter]::GetBytes([uint32]20);[Array]::Reverse($big);Bytes ($pvd+148) $big
[void](Record ($pvd+156) 21 2048 ([byte[]]@(0)) $true)
Ascii ($pvd+190) ('UOAIX'.PadRight(128));Ascii ($pvd+318) ('CODEX'.PadRight(128));Ascii ($pvd+446) ('CODEX'.PadRight(128));Ascii ($pvd+574) ('UOAIX'.PadRight(128))
foreach($at in @(813,830,847,864)){Ascii ($pvd+$at) '0000000000000000'};$iso[$pvd+881]=1
$boot=17*2048;Ascii ($boot+1) 'CD001';$iso[$boot+6]=1;Ascii ($boot+7) 'EL TORITO SPECIFICATION';U32 ($boot+71) 22
$term=18*2048;$iso[$term]=255;Ascii ($term+1) 'CD001';$iso[$term+6]=1
$iso[19*2048]=1;U32 (19*2048+2) 21;U16 (19*2048+6) 1
$iso[20*2048]=1;$b=[BitConverter]::GetBytes([uint32]21);[Array]::Reverse($b);Bytes (20*2048+2) $b;$iso[20*2048+7]=1
$at=21*2048;$at+=Record $at 21 2048 ([byte[]]@(0)) $true;$at+=Record $at 21 2048 ([byte[]]@(1)) $true
foreach($row in @(@('BOOT.CAT;1',22,2048),@('BIOS.BIN;1',23,2048),@('KERNEL.BIN;1',24,$payloadSize),@('EFI.IMG;1',$espLba,$espSize))){$at+=Record $at $row[1] $row[2] ([Text.Encoding]::ASCII.GetBytes($row[0])) $false}
$cat=22*2048;$iso[$cat]=1;Ascii ($cat+4) 'CODEX UOAIX';$iso[$cat+30]=0x55;$iso[$cat+31]=0xaa
$sum=0;for($i=0;$i -lt 32;$i+=2){$sum=($sum+[BitConverter]::ToUInt16($iso,$cat+$i))-band 65535};U16 ($cat+28) ((65536-$sum)-band 65535)
$iso[$cat+32]=0x88;U16 ($cat+38) 4;U32 ($cat+40) 23
$iso[$cat+64]=0x91;$iso[$cat+65]=0xef;U16 ($cat+66) 1
$iso[$cat+96]=0x88;U16 ($cat+102) $espSectors;U32 ($cat+104) $espLba
$bootFile=$Out+'.bios.bin'
if(Test-Path -LiteralPath $bootFile){throw 'BIOS scratch output exists'}
& $Nasm -f bin "-DPAYLOAD_LBA=24" "-DPAYLOAD_BLOCKS=$payloadBlocks" (Join-Path $PSScriptRoot 'iso-boot.asm') -o $bootFile
if($LASTEXITCODE -ne 0){throw 'BIOS assembly failed'}
$loader=[IO.File]::ReadAllBytes($bootFile);if($loader.Length -ne 2048){throw 'Unexpected loader extent'}
Bytes (23*2048) $loader
[Array]::Copy($cdx,$text,$iso,24*2048,$payloadSize)
[Array]::Copy($disk,$first*512,$iso,$espLba*2048,$espSize)
[IO.File]::WriteAllBytes($Out,$iso)
$receipt=@{biosCdxHash=(Get-FileHash $BiosCdx).Hash;uefiImageHash=(Get-FileHash $UefiImage).Hash;nasmHash=(Get-FileHash $Nasm).Hash;loaderSourceHash=(Get-FileHash (Join-Path $PSScriptRoot 'iso-boot.asm')).Hash;loaderHash=(Get-FileHash $bootFile).Hash;isoHash=(Get-FileHash $Out).Hash;payloadLba=24;payloadBlocks=$payloadBlocks;espLba=$espLba;espSectors=$espSectors}
[IO.File]::WriteAllText($Out+'.json',($receipt|ConvertTo-Json))
Write-Output "ISO: $Out"
