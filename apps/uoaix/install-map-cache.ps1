[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$WorldDisk,[string]$DecorationFile='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ClientRoot).Path
$WorldDisk=[IO.Path]::GetFullPath($WorldDisk)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($WorldDisk.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
   -not $WorldDisk.StartsWith((Join-Path $repo 'build-output')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Operator caches must stay outside tracked source, shipped images and published artifacts'
}
$paths=@('MAP0.MUL','STAIDX0.MUL','STATICS0.MUL','TILEDATA.MUL')|ForEach-Object {Join-Path $root $_}
foreach($path in $paths){if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Client files must not be reparse points'}}
if(Test-Path -LiteralPath $WorldDisk){if((Get-Item -LiteralPath $WorldDisk).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'World disk must not be a reparse point'}}
$fingerprints=@($paths|ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash})
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
public static class UoaixMapInstaller {
 public const long Base=67108864;
 public const int Blocks=393216,DirectoryBytes=Blocks*16,DataSector=1+DirectoryBytes/512;
 public static uint Hash(byte[] b,int count,bool header) {uint h=2166136261;for(int i=0;i<count;i++)if(!header||i<48||i>=56)h=unchecked((h^b[i])*16777619);return h;}
 public static long U32(byte[] b,int o) {return (long)b[o]|((long)b[o+1]<<8)|((long)b[o+2]<<16)|((long)b[o+3]<<24);}
 public static int U16(byte[] b,int o) {return b[o]|(b[o+1]<<8);}
 public static void Q(byte[] b,int o,long v) {System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o,8),v);}
 public static long R(byte[] b,int o) {return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o,8));}
 public static void D(byte[] b,int o,uint v) {System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o,4),v);}
 public static bool Same(byte[] a,byte[] b) {return a.AsSpan().SequenceEqual(b);}
 public static int OverlayMaximum(byte[] data) {
  long count=R(data,8);if(count<0||count>65536||data.Length!=64+count*176)throw new Exception("DWD1 count invalid");
  var cells=new Dictionary<long,int>();int maximum=0;
  for(int i=0;i<count;i++)for(int opened=0;opened<2;opened++) {
   int at=64+i*176;long flags=R(data,at+(opened==0?5:12)*8);if((flags&576)==0)continue;
   long x=R(data,at)+(opened==0?0:R(data,at+72)),y=R(data,at+8)+(opened==0?0:R(data,at+80));
   if(x<0||x>=6144||y<0||y>=4096)throw new Exception("DWD1 collision outside map");
   long key=x*4096+y;cells.TryGetValue(key,out int used);cells[key]=++used;maximum=Math.Max(maximum,used);
  }
  return maximum;
 }
 public static byte[] Prefix(FileStream disk) {
  using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
   byte[] buf=new byte[65536];disk.Position=0;
   for(long at=0;at<Base;at+=buf.Length){disk.ReadExactly(buf);hash.AppendData(buf);}
   return hash.GetHashAndReset();
  }
 }
 public static long[] Build(string root,string path,string[] fingerprints,string decoration) {
  using(var disk=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,65536)) {
   if(disk.Length==0)disk.SetLength(Base);
   if(disk.Length<Base)throw new Exception("World journal prefix must be64 MiB; existing data was not resized");
   byte[] header=new byte[512];
   if(disk.Length>Base){disk.Position=Base;disk.ReadExactly(header);if((R(header,0)!=0x32434D55&&R(header,0)!=0x32494D55)||R(header,8)!=2||R(header,48)!=Hash(header,512,true))throw new Exception("Unrecognized data after world journal; refused");}
   byte[] prefix=Prefix(disk);Array.Clear(header);Q(header,0,0x32494D55);Q(header,8,2);Q(header,48,Hash(header,512,true));disk.Position=Base;disk.Write(header);disk.Flush(true);
   byte[] directory=new byte[DirectoryBytes],tiles=File.ReadAllBytes(Path.Combine(root,"TILEDATA.MUL"));
   byte[] land=new byte[196],idx=new byte[12],item=new byte[7],rows=new byte[64*256*6],block=new byte[99328];
   int[] counts=new int[64];int maximum=1;long records=0;
   using(var map=new FileStream(Path.Combine(root,"MAP0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
   using(var index=new FileStream(Path.Combine(root,"STAIDX0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
   using(var statics=new FileStream(Path.Combine(root,"STATICS0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536)) {
    if(map.Length!=77070336||index.Length!=4718592||tiles.Length!=1036288)throw new Exception("Legacy MUL extents required");
    disk.Position=Base+(long)DataSector*512;
    for(int id=0;id<Blocks;id++) {
     map.ReadExactly(land);index.ReadExactly(idx);Array.Clear(counts);Q(block,0,id);Q(block,8,64);
     for(int cell=0;cell<64;cell++) {
      int at=4+cell*3,graphic=U16(land,at);if(graphic>=16384)throw new Exception("Invalid land graphic");
      int tile=(graphic/32)*836+4+(graphic%32)*26;
      D(block,16+cell*7,(uint)U32(tiles,tile));block[20+cell*7]=land[at+2];
     }
     long offset=U32(idx,0),size=U32(idx,4);
     if(offset!=4294967295) {
      if(size==4294967295||size%7!=0||offset>statics.Length||size>statics.Length-offset)throw new Exception("Invalid static range");
      if(statics.Position!=offset)statics.Position=offset;
      for(long n=0;n<size;n+=7) {
       statics.ReadExactly(item);int graphic=U16(item,0),dx=item[2],dy=item[3];
       if(graphic>=16384||dx>=8||dy>=8)throw new Exception("Invalid static record");
       int tile=428032+(graphic/32)*1188+4+(graphic%32)*37;long flags=U32(tiles,tile);if((flags&576)==0)continue;
       int cell=dy*8+dx,count=counts[cell]++;if(count>=256)throw new Exception("Collider limit exceeded");
       int target=(cell*256+count)*6;D(rows,target,(uint)flags);rows[target+4]=item[4];rows[target+5]=tiles[tile+16];records++;
      }
     }
     int used=464;
     for(int cell=0;cell<64;cell++) {
      int count=counts[cell];maximum=Math.Max(maximum,count);block[21+cell*7]=(byte)count;block[22+cell*7]=(byte)(count>>8);
      Buffer.BlockCopy(rows,cell*256*6,block,used,count*6);used+=count*6;
     }
     int padded=(used+511)/512*512;Array.Clear(block,used,padded-used);
     Q(directory,id*16,(disk.Position-Base)/512);D(directory,id*16+8,(uint)used);D(directory,id*16+12,Hash(block,used,false));disk.Write(block,0,padded);
    }
   }
   long mapEnd=(disk.Position-Base)/512,decorSector=0,decorBytes=0;uint decorHash=0;int overlay=0;
   if(!String.IsNullOrEmpty(decoration)) {
    byte[] data=File.ReadAllBytes(decoration);if(data.Length<64||data.Length>11534400||R(data,0)!=0x31445744)throw new Exception("Invalid DWD1 install data");
    overlay=OverlayMaximum(data);if(maximum+overlay>256)throw new Exception("Combined collision capacity exceeds256");
    decorSector=(disk.Position-Base)/512;decorBytes=data.Length;decorHash=Hash(data,data.Length,false);disk.Write(data);
    int pad=(512-data.Length%512)%512;if(pad>0)disk.Write(new byte[pad]);
   }
   long itemSector=(disk.Position-Base)/512;byte[] itemTable=new byte[608256];Buffer.BlockCopy(tiles,428032,itemTable,0,608256);uint itemHash=Hash(itemTable,itemTable.Length,false);disk.Write(itemTable);
   long length=disk.Position;disk.SetLength(length);disk.Position=Base+512;disk.Write(directory);disk.Flush(true);
   if(!Same(prefix,Prefix(disk)))throw new Exception("Journal prefix changed; cache remains uncommitted");
   for(int i=0;i<4;i++){string name=new[]{"MAP0.MUL","STAIDX0.MUL","STATICS0.MUL","TILEDATA.MUL"}[i];using(var f=File.OpenRead(Path.Combine(root,name))){if(Convert.ToHexString(SHA256.HashData(f))!=fingerprints[i])throw new Exception("Client changed during installation");}Convert.FromHexString(fingerprints[i]).CopyTo(header,128+i*32);}
   Q(header,0,0x32434D55);Q(header,8,2);Q(header,16,6144);Q(header,24,4096);Q(header,32,Blocks);Q(header,40,DirectoryBytes);
   Q(header,56,length-Base);Q(header,64,maximum);Q(header,72,DataSector);Q(header,80,records);
   Q(header,88,decorSector);Q(header,96,decorBytes);Q(header,104,decorHash);Q(header,112,mapEnd);Q(header,120,maximum+overlay);Q(header,256,itemSector);Q(header,264,itemTable.Length);Q(header,272,itemHash);Q(header,48,Hash(header,512,true));
   disk.Position=Base;disk.Write(header);disk.Flush(true);byte[] verify=new byte[512];disk.Position=Base;disk.ReadExactly(verify);if(!Same(header,verify))throw new Exception("Cache header readback failed");
   return new long[]{length,length-Base,maximum,records};
  }
 }
}
'@
$parent=Split-Path $WorldDisk
if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory $parent)}
$watch=[Diagnostics.Stopwatch]::StartNew()
if($DecorationFile){$DecorationFile=(Resolve-Path -LiteralPath $DecorationFile).Path}
$result=[UoaixMapInstaller]::Build($root,$WorldDisk,[string[]]$fingerprints,$DecorationFile)
$watch.Stop()
$receipt=[ordered]@{worldDisk=$WorldDisk;width=6144;height=4096;blocks=393216;sourceSha256=$fingerprints;diskBytes=$result[0];cacheBytes=$result[1];maximumColliders=$result[2];colliders=$result[3];elapsedSeconds=$watch.Elapsed.TotalSeconds;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath ($WorldDisk+'.map-install.json')
Write-Output "INSTALLED whole local map; cache bytes=$($result[1]); seconds=$($watch.Elapsed.TotalSeconds); $WorldDisk"
