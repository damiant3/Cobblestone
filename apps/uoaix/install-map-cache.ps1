[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$WorldDisk,[string]$DecorationFile='',[string]$FloraFile='',[ValidateRange(64,1048576)][long]$StoreMiB=1024)
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
# The server's collision comes from these statics, so they must be the unstripped client's.
$flora=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'flora-reference.json') -Raw|ConvertFrom-Json
if($fingerprints[2] -eq $flora.stripped.'STATICS0.MUL'){throw 'STATICS0.MUL is the flora-stripped client file; install the map cache from the original client (transform-flora.ps1 -Restore gives it back)'}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
public static class UoaixMapInstaller {
 public const long Base=67108864;
 // UOAIX-50 economy archive region: 256 MiB after the cache, named by header cells 304 (start sector) and 312 (capacity).
 public const long ArchiveSectors=524288;
 // The world journal (UOS1 pair) after the archive, named by header cells 320 (start sector) and 328 (sectors), sized by -StoreMiB.
 // A reinstall keeps it in place; one that would move it refuses.
 public const int Blocks=393216,DirectoryBytes=Blocks*16,DataSector=1+DirectoryBytes/512;
 public static uint Hash(byte[] b,int count,bool header) {uint h=2166136261;for(int i=0;i<count;i++)if(!header||i<48||i>=56)h=unchecked((h^b[i])*16777619);return h;}
 public static long U32(byte[] b,int o) {return (long)b[o]|((long)b[o+1]<<8)|((long)b[o+2]<<16)|((long)b[o+3]<<24);}
 public static int U16(byte[] b,int o) {return b[o]|(b[o+1]<<8);}
 public static void Q(byte[] b,int o,long v) {System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o,8),v);}
 public static long R(byte[] b,int o) {return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o,8));}
 public static void D(byte[] b,int o,uint v) {System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o,4),v);}
 public static bool Same(byte[] a,byte[] b) {return a.AsSpan().SequenceEqual(b);}
 // Source-X CItemBase::IsID_Chair, as WalkMap.codex walk-chair: seats never collide.
 public static bool Chair(int g) {return (g>=0x0459&&g<=0x045C)||(g>=0x0B2C&&g<=0x0B33)||(g>=0x0B4E&&g<=0x0B6A)||(g>=0x0B91&&g<=0x0B94)||g==0x0C17||g==0x0C18||g==0x1049||g==0x104A||(g>=0x1207&&g<=0x120C)||(g>=0x1218&&g<=0x121B)||g==0x1526||g==0x1527||(g>=0x19F1&&g<=0x19F3)||(g>=0x19F5&&g<=0x19F7)||(g>=0x19F9&&g<=0x19FC)||(g>=0x1DC7&&g<=0x1DD2)||g==0x1E6F||g==0x1E78||g==0x3DFF||g==0x3E00;}
 // As WalkMap.codex walk-brush: brambles, saplings and small rocks never collide (Damian, 2026-10-10).
 public static bool Brush(int g) {return g==0x0D3F||g==0x0D40||g==0x0CE9||g==0x0CEA||(g>=0x1363&&g<=0x136D)||(g>=0x1771&&g<=0x177C);}
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
 // FLR1 kind 3 rows are furnishings premises.cfg strips from the player's statics, so they collide nowhere.
 public static long Key(int x,int y,int graphic,byte z) {return (((long)x*4096+y)*16384+graphic)*256+z;}
 public static HashSet<long> Stripped(string flora) {
  var set=new HashSet<long>();if(String.IsNullOrEmpty(flora))return set;
  byte[] t=File.ReadAllBytes(flora);if(t.Length<64||R(t,0)!=0x31524C46||R(t,48)!=12||t.Length!=R(t,40)+R(t,16)*12)throw new Exception("Invalid FLR1 flora table (transform-flora.ps1 writes flora.flr)");
  for(long i=0,at=R(t,40);i<R(t,16);i++,at+=12)if(t[at+9]==3)set.Add(Key(U16(t,(int)at),U16(t,(int)at+2),U16(t,(int)at+4),t[at+8]));
  return set;
 }
 public static byte[] Prefix(FileStream disk) {
  using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
   byte[] buf=new byte[65536];disk.Position=0;
   for(long at=0;at<Base;at+=buf.Length){disk.ReadExactly(buf);hash.AppendData(buf);}
   return hash.GetHashAndReset();
  }
 }
 public static long[] Build(string root,string path,string[] fingerprints,string decoration,string flora,bool[] mountain,bool[] sand,long storeSectors,int[] baked,int[] landEdits,int[] removes) {
  // decor.cfg's Remove lines (x, y, art, z): the matching client static collides nowhere, as the players' clients no longer draw it.
  var removed=new HashSet<long>();long removedRecords=0;
  for(int i=0;i+3<removes.Length;i+=4)removed.Add(Key(removes[i],removes[i+1],removes[i+2],(byte)(sbyte)removes[i+3]));
  var landAt=new System.Collections.Generic.Dictionary<int,System.Collections.Generic.List<int>>();long landRecords=0;
  for(int i=0;i+3<landEdits.Length;i+=4){
   if(landEdits[i]<0||landEdits[i]>=6144||landEdits[i+1]<0||landEdits[i+1]>=4096||landEdits[i+2]<-1||landEdits[i+2]>=16384||landEdits[i+3]<-128||landEdits[i+3]>127)throw new Exception("Land edit outside the legacy map");
   int block=(landEdits[i]/8)*512+landEdits[i+1]/8;if(!landAt.ContainsKey(block))landAt[block]=new System.Collections.Generic.List<int>();landAt[block].Add(i);
  }
  var bakedAt=new System.Collections.Generic.Dictionary<int,System.Collections.Generic.List<int>>();long bakedRecords=0;
  for(int i=0;i+3<baked.Length;i+=4){
   if(baked[i]<0||baked[i]>=6144||baked[i+1]<0||baked[i+1]>=4096||baked[i+2]<0||baked[i+2]>=16384||baked[i+3]<-128||baked[i+3]>127)throw new Exception("Baked static outside the legacy map");
   int block=(baked[i]/8)*512+baked[i+1]/8;if(!bakedAt.ContainsKey(block))bakedAt[block]=new System.Collections.Generic.List<int>();bakedAt[block].Add(i);
  }
  using(var disk=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,65536)) {
   if(disk.Length==0)disk.SetLength(Base);
   if(disk.Length<Base)throw new Exception("World journal prefix must be64 MiB; existing data was not resized");
   byte[] header=new byte[512];
   if(disk.Length>Base){disk.Position=Base;disk.ReadExactly(header);if((R(header,0)!=0x32434D55&&R(header,0)!=0x32494D55)||(R(header,8)!=2&&R(header,8)!=3)||R(header,48)!=Hash(header,512,true))throw new Exception("Unrecognized data after world journal; refused");}
   byte[] carried=new byte[0];long oldStore=0,oldStoreSectors=0;
   if(disk.Length>Base&&R(header,0)==0x32434D55&&R(header,320)>0){oldStore=R(header,320);oldStoreSectors=R(header,328);if(oldStoreSectors<2||Base+(oldStore+oldStoreSectors)*512>disk.Length)throw new Exception("Store region outside the disk; refused");}
   if(disk.Length>Base&&R(header,0)==0x32434D55&&R(header,304)>0) {
    long region=R(header,304),capacity=R(header,312);if(capacity<2||Base+(region+capacity)*512>disk.Length)throw new Exception("Archive region outside the disk; refused");
    byte[] arc=new byte[512];disk.Position=Base+region*512;disk.ReadExactly(arc);
    long used=R(arc,0)==0x31435241?R(arc,24):1;if(used<1||used>capacity||used>ArchiveSectors)throw new Exception("Archive region header invalid; refused");
    carried=new byte[used*512];disk.Position=Base+region*512;disk.ReadExactly(carried);
   }
   byte[] prefix=Prefix(disk);Array.Clear(header);Q(header,0,0x32494D55);Q(header,8,3);Q(header,48,Hash(header,512,true));disk.Position=Base;disk.Write(header);disk.Flush(true);
   byte[] directory=new byte[DirectoryBytes],tiles=File.ReadAllBytes(Path.Combine(root,"TILEDATA.MUL"));
   var stripped=Stripped(flora);
   byte[] land=new byte[196],idx=new byte[12],item=new byte[7],rows=new byte[64*256*6],block=new byte[99328];
   int[] counts=new int[64];int maximum=1;long records=0;
   using(var map=new FileStream(Path.Combine(root,"MAP0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
   using(var index=new FileStream(Path.Combine(root,"STAIDX0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
   using(var statics=new FileStream(Path.Combine(root,"STATICS0.MUL"),FileMode.Open,FileAccess.Read,FileShare.Read,65536)) {
    if(map.Length!=77070336||index.Length!=4718592||tiles.Length!=1036288)throw new Exception("Legacy MUL extents required");
    disk.Position=Base+(long)DataSector*512;
    for(int id=0;id<Blocks;id++) {
     map.ReadExactly(land);index.ReadExactly(idx);Array.Clear(counts);Q(block,0,id);Q(block,8,64);
     if(landAt.ContainsKey(id))foreach(int i in landAt[id]){int at=4+((landEdits[i+1]%8)*8+landEdits[i]%8)*3;if(landEdits[i+2]>=0){land[at]=(byte)landEdits[i+2];land[at+1]=(byte)(landEdits[i+2]>>8);}land[at+2]=(byte)(sbyte)landEdits[i+3];landRecords++;}
     for(int cell=0;cell<64;cell++) {
      int at=4+cell*3,graphic=U16(land,at);if(graphic>=16384)throw new Exception("Invalid land graphic");
      int tile=(graphic/32)*836+4+(graphic%32)*26;
      uint landFlags=(uint)U32(tiles,tile);if((landFlags&0x80000000u)!=0)throw new Exception("Land tiledata sets the mountain mark bit");if((landFlags&0x40000000u)!=0)throw new Exception("Land tiledata sets the sand mark bit");if(mountain[graphic])landFlags|=0x80000000u;if(sand[graphic])landFlags|=0x40000000u;D(block,16+cell*7,landFlags);block[20+cell*7]=land[at+2];
     }
     long offset=U32(idx,0),size=U32(idx,4);
     if(offset!=4294967295) {
      if(size==4294967295||size%7!=0||offset>statics.Length||size>statics.Length-offset)throw new Exception("Invalid static range");
      if(statics.Position!=offset)statics.Position=offset;
      for(long n=0;n<size;n+=7) {
       statics.ReadExactly(item);int graphic=U16(item,0),dx=item[2],dy=item[3];
       if(graphic>=16384||dx>=8||dy>=8)throw new Exception("Invalid static record");
       int tile=428032+(graphic/32)*1188+4+(graphic%32)*37;long flags=U32(tiles,tile);if((flags&576)==0||Chair(graphic)||Brush(graphic))continue;
       if(stripped.Contains(Key((id/512)*8+dx,(id%512)*8+dy,graphic,item[4])))continue;
       if(removed.Count>0&&removed.Contains(Key((id/512)*8+dx,(id%512)*8+dy,graphic,item[4]))){removedRecords++;continue;}
       int cell=dy*8+dx,count=counts[cell]++;if(count>=256)throw new Exception("Collider limit exceeded");
       int target=(cell*256+count)*6;D(rows,target,(uint)flags);rows[target+4]=item[4];rows[target+5]=tiles[tile+16];records++;
      }
     }
     if(bakedAt.ContainsKey(id))foreach(int i in bakedAt[id]) {
      int graphic=baked[i+2],tile=428032+(graphic/32)*1188+4+(graphic%32)*37;long flags=U32(tiles,tile);if((flags&576)==0||Chair(graphic)||Brush(graphic))continue;
      int cell=(baked[i+1]%8)*8+baked[i]%8,count=counts[cell]++;if(count>=256)throw new Exception("Collider limit exceeded");
      int target=(cell*256+count)*6;D(rows,target,(uint)flags);rows[target+4]=(byte)(sbyte)baked[i+3];rows[target+5]=tiles[tile+16];records++;bakedRecords++;
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
   int itemPad=(512-itemTable.Length%512)%512;if(itemPad>0)disk.Write(new byte[itemPad]);
   long floraSector=0,floraBytes=0;uint floraHash=0;
   if(!String.IsNullOrEmpty(flora)) {
    byte[] data=File.ReadAllBytes(flora);if(data.Length<64||data.Length>16777216||R(data,0)!=0x31524C46||R(data,8)!=1||data.Length!=R(data,40)+R(data,16)*12)throw new Exception("Invalid FLR1 flora table (transform-flora.ps1 writes flora.flr)");
    floraSector=(disk.Position-Base)/512;floraBytes=data.Length;floraHash=Hash(data,data.Length,false);disk.Write(data);
    int pad=(512-data.Length%512)%512;if(pad>0)disk.Write(new byte[pad]);
   }
   long archiveSector=(disk.Position-Base)/512;if(carried.Length>0)disk.Write(carried);else disk.Write(new byte[512]);
   long storeSector=archiveSector+ArchiveSectors;if(oldStore>0&&oldStore!=storeSector)throw new Exception("The world journal's store region would move; install on a fresh disk");
   long stores=Math.Max(storeSectors,oldStoreSectors);long length=Base+(storeSector+stores)*512;if(oldStore==0)disk.SetLength(Base+storeSector*512);disk.SetLength(length);disk.Position=Base+512;disk.Write(directory);disk.Flush(true);
   if(!Same(prefix,Prefix(disk)))throw new Exception("Journal prefix changed; cache remains uncommitted");
   for(int i=0;i<4;i++){string name=new[]{"MAP0.MUL","STAIDX0.MUL","STATICS0.MUL","TILEDATA.MUL"}[i];using(var f=File.OpenRead(Path.Combine(root,name))){if(Convert.ToHexString(SHA256.HashData(f))!=fingerprints[i])throw new Exception("Client changed during installation");}Convert.FromHexString(fingerprints[i]).CopyTo(header,128+i*32);}
   Q(header,0,0x32434D55);Q(header,8,3);Q(header,16,6144);Q(header,24,4096);Q(header,32,Blocks);Q(header,40,DirectoryBytes);
   Q(header,56,length-Base);Q(header,64,maximum);Q(header,72,DataSector);Q(header,80,records);
   Q(header,88,decorSector);Q(header,96,decorBytes);Q(header,104,decorHash);Q(header,112,mapEnd);Q(header,120,maximum+overlay);Q(header,256,itemSector);Q(header,264,itemTable.Length);Q(header,272,itemHash);Q(header,280,floraSector);Q(header,288,floraBytes);Q(header,296,floraHash);Q(header,304,archiveSector);Q(header,312,ArchiveSectors);Q(header,320,storeSector);Q(header,328,stores);Q(header,48,Hash(header,512,true));
   disk.Position=Base;disk.Write(header);disk.Flush(true);byte[] verify=new byte[512];disk.Position=Base;disk.ReadExactly(verify);if(!Same(header,verify))throw new Exception("Cache header readback failed");
   return new long[]{length,length-Base,maximum,records,bakedRecords,landRecords,removedRecords};
  }
 }
}
'@
$parent=Split-Path $WorldDisk
if(-not (Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory $parent)}
# MiningTiles.codex is the one copy of ServUO's mountain and cave land list; bit 31 marks it.
$mountain=[bool[]]::new(16384);$marked=0
$tilesText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'MiningTiles.codex'))
foreach($m in [regex]::Matches($tilesText,'g >= (\d+) & g <= (\d+)|g == (\d+)')){$a=if($m.Groups[3].Success){[int]$m.Groups[3].Value}else{[int]$m.Groups[1].Value};$b=if($m.Groups[3].Success){$a}else{[int]$m.Groups[2].Value};for($g=$a;$g -le $b;$g++){if(-not $mountain[$g]){$mountain[$g]=$true;$marked++}}}
if($marked -ne 294){throw "MiningTiles.codex lists $marked mountain land IDs, not 294"}
# The legacy TILEDATA names 169 land IDs "sand" (26-byte land records, name at byte 6); bit 30 marks them for the sand gatherer.
$tiledata=[IO.File]::ReadAllBytes($paths[3]);$sand=[bool[]]::new(16384);$sandMarked=0
for($g=0;$g -lt 16384;$g++){$at=[math]::Floor($g/32)*836+4+($g%32)*26+6;if([Text.Encoding]::ASCII.GetString($tiledata,$at,20).Trim([char]0).Trim() -ieq 'sand'){$sand[$g]=$true;$sandMarked++}}
if($sandMarked -ne 169){throw "TILEDATA names $sandMarked sand land IDs, not 169"}
$watch=[Diagnostics.Stopwatch]::StartNew()
if($DecorationFile){$DecorationFile=(Resolve-Path -LiteralPath $DecorationFile).Path}
if($FloraFile){$FloraFile=(Resolve-Path -LiteralPath $FloraFile).Path}
# decor.cfg's baked items are statics of the shard's own (UoaixDecorator.md): each collides like a client static.
$baked=[Collections.Generic.List[int]]::new();$removes=[Collections.Generic.List[int]]::new();$bakedArt=-1;$bakedLine=0
foreach($line in [IO.File]::ReadLines((Join-Path $PSScriptRoot 'decor.cfg'))){
    $bakedLine++;$text=$line.Trim()
    if($text.Length -eq 0 -or $text.StartsWith('#')){continue}
    if($text -match '^Remove\s+0x([0-9A-Fa-f]+)\s+(\d+)\s+(\d+)\s+(-?\d+)(?:\s|$)'){
        $ra=[Convert]::ToInt32($Matches[1],16);$rx=[int]$Matches[2];$ry=[int]$Matches[3];$rz=[int]$Matches[4]
        if($rx -ge 6144 -or $ry -ge 4096 -or $rz -lt -128 -or $rz -gt 127 -or $ra -lt 1 -or $ra -ge 16384){throw "decor.cfg:$bakedLine Remove outside the legacy map or record"}
        $removes.AddRange([int[]]@($rx,$ry,$ra,$rz));continue
    }
    if($text -match '^Static\s+0x([0-9A-Fa-f]+)\b'){$bakedArt=[Convert]::ToInt32($Matches[1],16);continue}
    if($text -match '^(\d+)\s+(\d+)\s+(-?\d+)(?:\s|$)'){if($bakedArt -lt 0){throw "decor.cfg:$bakedLine placement without a Static definition"};$baked.Add([int]$Matches[1]);$baked.Add([int]$Matches[2]);$baked.Add($bakedArt);$baked.Add([int]$Matches[3]);continue}
    throw "decor.cfg:$bakedLine unparsed"
}
# land.cfg's baked land edits replace a cell's land art and height (UoaixDecorator.md item 6): x y art z per line.
$landEdits=[Collections.Generic.List[int]]::new();$landLine=0
foreach($line in [IO.File]::ReadLines((Join-Path $PSScriptRoot 'land.cfg'))){
    $landLine++;$text=$line.Trim()
    if($text.Length -eq 0 -or $text.StartsWith('#')){continue}
    if($text -match '^(\d+)\s+(\d+)\s+(?:0x([0-9A-Fa-f]+)|keep)\s+(-?\d+)(?:\s|$)'){$landEdits.AddRange([int[]]@([int]$Matches[1],[int]$Matches[2],$(if($Matches[3]){[Convert]::ToInt32($Matches[3],16)}else{-1}),[int]$Matches[4]));continue}
    throw "land.cfg:$landLine unparsed"
}
$result=[UoaixMapInstaller]::Build($root,$WorldDisk,[string[]]$fingerprints,$DecorationFile,$FloraFile,$mountain,$sand,$StoreMiB*2048,$baked.ToArray(),$landEdits.ToArray(),$removes.ToArray())
$watch.Stop()
if($result[6] -ne $removes.Count/4){Write-Warning "decor.cfg names $($removes.Count/4) static removals; $($result[6]) colliding client statics matched (a Remove naming a non-colliding or absent static drops no collider)"}
$receipt=[ordered]@{worldDisk=$WorldDisk;width=6144;height=4096;blocks=393216;sourceSha256=$fingerprints;diskBytes=$result[0];cacheBytes=$result[1];maximumColliders=$result[2];colliders=$result[3];bakedColliders=$result[4];landEdits=$result[5];removedColliders=$result[6];elapsedSeconds=$watch.Elapsed.TotalSeconds;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath ($WorldDisk+'.map-install.json')
Write-Output "INSTALLED whole local map; cache bytes=$($result[1]); seconds=$($watch.Elapsed.TotalSeconds); $WorldDisk"
