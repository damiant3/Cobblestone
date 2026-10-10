[CmdletBinding()]
param([Parameter(Mandatory)][string]$StrippedDir,[Parameter(Mandatory)][string]$OutDir,[string]$Reference=(Join-Path $PSScriptRoot 'flora-reference.json'))
# The decorator bake's client half (UoaixDecorator.md): adds decor.cfg's baked items to the player's statics and drops
# every client static a Remove line names (art, x, y, z all matching). The input
# is transform-flora.ps1's output, checked against its reference hashes, so the result depends only on decor.cfg;
# taking an item out is a rerun after decor.cfg drops it. A legacy static record is 7 bytes: art, x and y within its
# 8 by 8 block, z (signed), hue; STAIDX0 holds 12 bytes a block: offset, length, extra.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$in=(Resolve-Path -LiteralPath $StrippedDir).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($OutDir.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $OutDir -eq $repo){throw 'Client files stay outside tracked source, shipped images and published artifacts (ruling R1)'}
$ref=Get-Content -LiteralPath $Reference -Raw|ConvertFrom-Json
foreach($n in 'STAIDX0.MUL','STATICS0.MUL'){if((Get-FileHash -LiteralPath (Join-Path $in $n) -Algorithm SHA256).Hash -ne $ref.stripped.$n){throw "$n in $in is not transform-flora.ps1's reference output"}}
$baked=[Collections.Generic.List[int]]::new();$removes=[Collections.Generic.List[int]]::new();$art=-1;$hue=0;$lineNo=0
foreach($line in [IO.File]::ReadLines((Join-Path $PSScriptRoot 'decor.cfg'))){
    $lineNo++;$text=$line.Trim()
    if($text.Length -eq 0 -or $text.StartsWith('#')){continue}
    if($text -match '^Remove\s+0x([0-9A-Fa-f]+)\s+(\d+)\s+(\d+)\s+(-?\d+)(?:\s|$)'){
        $ra=[Convert]::ToInt32($Matches[1],16);$rx=[int]$Matches[2];$ry=[int]$Matches[3];$rz=[int]$Matches[4]
        if($rx -ge 6144 -or $ry -ge 4096 -or $rz -lt -128 -or $rz -gt 127 -or $ra -lt 1 -or $ra -ge 16384){throw "decor.cfg:$lineNo Remove outside the legacy map or record"}
        $removes.AddRange([int[]]@($rx,$ry,$ra,$rz));continue
    }
    if($text -match '^Static\s+0x([0-9A-Fa-f]+)(?:\s*\(Hue=0x([0-9A-Fa-f]+)\))?\s*$'){$art=[Convert]::ToInt32($Matches[1],16);$hue=if($Matches[2]){[Convert]::ToInt32($Matches[2],16)}else{0};continue}
    if($text -match '^(\d+)\s+(\d+)\s+(-?\d+)(?:\s|$)'){
        if($art -lt 0){throw "decor.cfg:$lineNo placement without a Static definition"}
        $x=[int]$Matches[1];$y=[int]$Matches[2];$z=[int]$Matches[3]
        if($x -ge 6144 -or $y -ge 4096 -or $z -lt -128 -or $z -gt 127 -or $art -ge 16384 -or $hue -gt 65535){throw "decor.cfg:$lineNo outside the legacy map or record"}
        $baked.AddRange([int[]]@($x,$y,$art,$z,$hue));continue
    }
    throw "decor.cfg:$lineNo unparsed"
}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
public static class UoaixDecorStatics {
 public const int Blocks=393216;
 // A Remove names art, x, y and z; a record matching all four is dropped. Returns the records dropped in Removed.
 public static long Removed;
 static long Key(int x,int y,int art,int z){return (((long)x*4096+y)*16384+art)*256+(z&255);}
 public static byte[][] Apply(byte[] idx,byte[] st,int[] baked,int[] removes) {
  if(idx.Length!=Blocks*12)throw new Exception("Legacy STAIDX0 extent required");
  var at=new Dictionary<int,List<int>>();
  for(int i=0;i+4<baked.Length;i+=5){int block=(baked[i]/8)*512+baked[i+1]/8;if(!at.ContainsKey(block))at[block]=new List<int>();at[block].Add(i);}
  var gone=new HashSet<long>();var goneBlocks=new HashSet<int>();
  for(int i=0;i+3<removes.Length;i+=4){gone.Add(Key(removes[i],removes[i+1],removes[i+2],removes[i+3]));goneBlocks.Add((removes[i]/8)*512+removes[i+1]/8);}
  Removed=0;
  byte[] outIdx=new byte[idx.Length];var outSt=new MemoryStream();byte[] rec=new byte[7];
  for(int id=0;id<Blocks;id++) {
   uint off=BitConverter.ToUInt32(idx,id*12),len=BitConverter.ToUInt32(idx,id*12+4);
   Buffer.BlockCopy(idx,id*12+8,outIdx,id*12+8,4);
   bool empty=off==0xFFFFFFFF;if(!empty&&(len%7!=0||(long)off+len>st.Length))throw new Exception("Invalid static range in block "+id);
   bool added=at.ContainsKey(id);
   if(empty&&!added){BitConverter.TryWriteBytes(outIdx.AsSpan(id*12,4),0xFFFFFFFFu);BitConverter.TryWriteBytes(outIdx.AsSpan(id*12+4,4),0xFFFFFFFFu);continue;}
   long start=outSt.Length;
   if(!empty&&!goneBlocks.Contains(id))outSt.Write(st,(int)off,(int)len);
   else if(!empty)for(long n=off;n<off+len;n+=7){int x0=(id/512)*8,y0=(id%512)*8;
    if(gone.Contains(Key(x0+st[n+2],y0+st[n+3],BitConverter.ToUInt16(st,(int)n),(sbyte)st[n+4]))){Removed++;continue;}
    outSt.Write(st,(int)n,7);}
   if(outSt.Length==start&&!added){BitConverter.TryWriteBytes(outIdx.AsSpan(id*12,4),0xFFFFFFFFu);BitConverter.TryWriteBytes(outIdx.AsSpan(id*12+4,4),0xFFFFFFFFu);continue;}
   if(added)foreach(int i in at[id]){BitConverter.TryWriteBytes(rec.AsSpan(0,2),(ushort)baked[i+2]);rec[2]=(byte)(baked[i]%8);rec[3]=(byte)(baked[i+1]%8);rec[4]=(byte)(sbyte)baked[i+3];BitConverter.TryWriteBytes(rec.AsSpan(5,2),(ushort)baked[i+4]);outSt.Write(rec,0,7);}
   BitConverter.TryWriteBytes(outIdx.AsSpan(id*12,4),(uint)start);BitConverter.TryWriteBytes(outIdx.AsSpan(id*12+4,4),(uint)(outSt.Length-start));
  }
  return new byte[][]{outIdx,outSt.ToArray()};
 }
}
'@
$result=[UoaixDecorStatics]::Apply([IO.File]::ReadAllBytes((Join-Path $in 'STAIDX0.MUL')),[IO.File]::ReadAllBytes((Join-Path $in 'STATICS0.MUL')),$baked.ToArray(),$removes.ToArray())
$removed=[UoaixDecorStatics]::Removed
if($removed -ne $removes.Count/4){Write-Warning "decor.cfg names $($removes.Count/4) static removals; $removed client statics matched (a Remove naming no static removes nothing)"}
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
[IO.File]::WriteAllBytes((Join-Path $OutDir 'STAIDX0.MUL'),$result[0])
[IO.File]::WriteAllBytes((Join-Path $OutDir 'STATICS0.MUL'),$result[1])
$receipt=[ordered]@{strippedDir=$in;outDir=$OutDir;bakedStatics=$baked.Count/5;removedStatics=$removed;decorCfgSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'decor.cfg') -Algorithm SHA256).Hash;staidxSha256=(Get-FileHash -LiteralPath (Join-Path $OutDir 'STAIDX0.MUL') -Algorithm SHA256).Hash;staticsSha256=(Get-FileHash -LiteralPath (Join-Path $OutDir 'STATICS0.MUL') -Algorithm SHA256).Hash;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutDir 'decor-statics.json')
Write-Output "DECOR statics=$($baked.Count/5) removed=$removed $OutDir"
