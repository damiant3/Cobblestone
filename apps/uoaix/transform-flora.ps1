[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$OutDir,[switch]$Restore,[string]$Reference=(Join-Path $PSScriptRoot 'flora-reference.json'),[switch]$WriteReference)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ClientRoot).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($OutDir.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $OutDir -eq $repo){throw 'Client files and the flora table stay outside tracked source, shipped images and published artifacts (ruling R1)'}
function Get-Sha([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash}
if(-not (Test-Path -LiteralPath $Reference)){throw "No reference hashes at $Reference"}
$ref=Get-Content -LiteralPath $Reference -Raw|ConvertFrom-Json
# CompositeProduction.codex cpr-flora (which includes cpr-tree) is the one copy of the flora art list.
$flora=[bool[]]::new(16384);$tree=[bool[]]::new(16384);$vine=[bool[]]::new(16384)
$text=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CompositeProduction.codex'))
function Read-Ranges([string]$Name,[bool[]]$Into){
    $body=[regex]::Match($text,"(?s)  $Name \(g\) =(.*?)\r?\n {1,2}\S").Groups[1].Value
    $n=0
    foreach($m in [regex]::Matches($body,'g >= #([0-9A-F]+) & g <= #([0-9A-F]+)|g == #([0-9A-F]+)')){
        $a=if($m.Groups[3].Success){[Convert]::ToInt32($m.Groups[3].Value,16)}else{[Convert]::ToInt32($m.Groups[1].Value,16)}
        $b=if($m.Groups[3].Success){$a}else{[Convert]::ToInt32($m.Groups[2].Value,16)}
        for($g=$a;$g -le $b;$g++){if(-not $Into[$g]){$Into[$g]=$true;$n++}}
    }
    $n
}
$trees=Read-Ranges 'cpr-tree' $tree
if($trees -ne 149){throw "cpr-tree lists $trees tree art IDs, not 149"}
for($g=0;$g -lt 16384;$g++){$flora[$g]=$tree[$g]}
[void](Read-Ranges 'cpr-flora' $flora)
$floraCount=($flora|Where-Object {$_}).Count
if($floraCount -ne 209){throw "cpr-flora and cpr-tree list $floraCount flora art IDs, not 209"}; $vines=Read-Ranges 'cpr-tree-vine' $vine; if($vines -ne 12){throw "cpr-tree-vine lists $vines vine art IDs, not 12"}
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class UoaixFlora {
 public const int Blocks=393216,Row=12,Header=64;
 static uint U32(byte[] b,long o){return BitConverter.ToUInt32(b,(int)o);}
 static void W32(byte[] b,long o,uint v){BitConverter.TryWriteBytes(b.AsSpan((int)o,4),v);}
 static void W16(byte[] b,long o,int v){b[o]=(byte)v;b[o+1]=(byte)(v>>8);}
 static void W64(byte[] b,long o,long v){BitConverter.TryWriteBytes(b.AsSpan((int)o,8),v);}
 static long R64(byte[] b,long o){return BitConverter.ToInt64(b,(int)o);}
 // Rebuilds the original STAIDX0/STATICS0 from stripped files and the table; the reference client's statics are in block order with no gaps.
 public static byte[][] Merge(byte[] idx,byte[] st,byte[] table) {
  if(idx.Length!=Blocks*12||table.Length<Header||R64(table,0)!=0x31524C46||R64(table,8)!=1||R64(table,24)!=Blocks||R64(table,48)!=Row)throw new Exception("Not a FLR1 flora table for a stripped STAIDX0");
  long rows=R64(table,16),dir=R64(table,32),data=R64(table,40);
  if(dir!=Header||data!=Header+(Blocks+1)*4L||table.Length!=data+rows*Row)throw new Exception("FLR1 extents invalid");
  byte[] outIdx=new byte[idx.Length];var outSt=new MemoryStream();
  for(int id=0;id<Blocks;id++) {
   uint first=U32(table,dir+id*4L),last=U32(table,dir+id*4L+4);
   uint koff=U32(idx,id*12),klen=U32(idx,id*12+4);W32(outIdx,id*12+8,U32(idx,id*12+8));
   long kept=koff==0xFFFFFFFF?0:klen/7;long count=kept+(last-first);
   if(last<first||last>rows||(koff!=0xFFFFFFFF&&((long)koff+klen>st.Length||klen%7!=0)))throw new Exception("Block "+id+" extents invalid");
   if(count==0){W32(outIdx,id*12,0xFFFFFFFF);W32(outIdx,id*12+4,0xFFFFFFFF);continue;}
   byte[] block=new byte[count*7];var taken=new bool[count];int x0=(id/512)*8,y0=(id%512)*8;
   for(uint r=first;r<last;r++){long a=data+r*Row;int n=BitConverter.ToUInt16(table,(int)a+10);if(n>=count||taken[n])throw new Exception("Row ordinal invalid in block "+id);taken[n]=true;long o=n*7L;
    W16(block,o,BitConverter.ToUInt16(table,(int)a+4));block[o+2]=(byte)(BitConverter.ToUInt16(table,(int)a)-x0);block[o+3]=(byte)(BitConverter.ToUInt16(table,(int)a+2)-y0);block[o+4]=table[a+8];block[o+5]=table[a+6];block[o+6]=table[a+7];}
   long k=koff;for(long n=0;n<count;n++){if(taken[n])continue;Buffer.BlockCopy(st,(int)k,block,(int)(n*7),7);k+=7;}
   if(koff!=0xFFFFFFFF&&k!=(long)koff+klen)throw new Exception("Kept records do not fill block "+id);
   W32(outIdx,id*12,(uint)outSt.Length);W32(outIdx,id*12+4,(uint)block.Length);outSt.Write(block,0,block.Length);
  }
  return new byte[][]{outIdx,outSt.ToArray()};
 }
 public static long[] Strip(byte[] idx,byte[] st,bool[] flora,bool[] tree,bool[] vine,out byte[] outIdx,out byte[] outSt,out byte[] table) {
  if(idx.Length!=Blocks*12)throw new Exception("Legacy STAIDX0 extent required");
  outIdx=new byte[idx.Length];var kept=new MemoryStream();var rows=new MemoryStream();byte[] directory=new byte[(Blocks+1)*4];
  long trees=0,plants=0,keptRecords=0;bool[] cellTree=new bool[64];
  for(int id=0;id<Blocks;id++) {
   W32(directory,id*4,(uint)(rows.Length/Row));
   uint off=U32(idx,id*12),len=U32(idx,id*12+4);W32(outIdx,id*12+8,U32(idx,id*12+8));
   if(off==0xFFFFFFFF){W32(outIdx,id*12,0xFFFFFFFF);W32(outIdx,id*12+4,0xFFFFFFFF);continue;}
   if(len%7!=0||(long)off+len>st.Length||len/7>65535)throw new Exception("Invalid static range in block "+id);
   long start=kept.Length;int x0=(id/512)*8,y0=(id%512)*8;Array.Clear(cellTree);
   for(int n=0;n<len/7;n++){long at=off+n*7L;int g=BitConverter.ToUInt16(st,(int)at),dx=st[at+2],dy=st[at+3];if(g>=16384||dx>=8||dy>=8)throw new Exception("Invalid static record in block "+id);if(tree[g])cellTree[dy*8+dx]=true;}
   for(int n=0;n<len/7;n++) {
    long at=off+n*7L;int g=BitConverter.ToUInt16(st,(int)at),dx=st[at+2],dy=st[at+3];
    bool isVine=!flora[g]&&vine[g]&&cellTree[dy*8+dx];
    if(!flora[g]&&!isVine){kept.Write(st,(int)at,7);keptRecords++;continue;}
    byte[] r=new byte[Row];W16(r,0,x0+dx);W16(r,2,y0+dy);W16(r,4,g);r[6]=st[at+5];r[7]=st[at+6];r[8]=st[at+4];r[9]=(byte)(tree[g]?0:isVine?1:2);W16(r,10,n);
    rows.Write(r,0,Row);if(tree[g])trees++;else plants++;
   }
   long kbytes=kept.Length-start;
   if(kbytes==0){W32(outIdx,id*12,0xFFFFFFFF);W32(outIdx,id*12+4,0xFFFFFFFF);}
   else{W32(outIdx,id*12,(uint)start);W32(outIdx,id*12+4,(uint)kbytes);}
  }
  W32(directory,Blocks*4,(uint)(rows.Length/Row));
  outSt=kept.ToArray();byte[] rowBytes=rows.ToArray();long rowCount=rowBytes.Length/Row;
  table=new byte[Header+directory.Length+rowBytes.Length];
  W64(table,0,0x31524C46);W64(table,8,1);W64(table,16,rowCount);W64(table,24,Blocks);W64(table,32,Header);W64(table,40,Header+directory.Length);W64(table,48,Row);
  Buffer.BlockCopy(directory,0,table,Header,directory.Length);Buffer.BlockCopy(rowBytes,0,table,Header+directory.Length,rowBytes.Length);
  return new long[]{trees,plants,keptRecords,rowCount,st.Length/7};
 }
}
'@
$names='STAIDX0.MUL','STATICS0.MUL'
foreach($n in $names){$path=Join-Path $root $n;if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Client files must not be reparse points'}}
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
if($Restore){
    foreach($n in $names){if((Get-Sha (Join-Path $root $n)) -ne $ref.stripped.$n){throw "$n is not the reference stripped file; restore refused"}}
    if((Get-Sha (Join-Path $root 'flora.flr')) -ne $ref.stripped.'flora.flr'){throw 'flora.flr is not the reference flora table; restore refused'}
    $back=[UoaixFlora]::Merge([IO.File]::ReadAllBytes((Join-Path $root 'STAIDX0.MUL')),[IO.File]::ReadAllBytes((Join-Path $root 'STATICS0.MUL')),[IO.File]::ReadAllBytes((Join-Path $root 'flora.flr')))
    $sha=[Security.Cryptography.SHA256]::Create()
    for($i=0;$i -lt 2;$i++){if([Convert]::ToHexString($sha.ComputeHash($back[$i])) -ne $ref.original.($names[$i])){throw "Restored $($names[$i]) does not hash to the original; nothing written"}}
    for($i=0;$i -lt 2;$i++){[IO.File]::WriteAllBytes((Join-Path $OutDir $names[$i]),$back[$i])}
    Write-Output "RESTORED original STAIDX0/STATICS0 (SHA-256 verified) $OutDir"
    return
}
$sources=[ordered]@{};foreach($n in $names){$sources[$n]=Get-Sha (Join-Path $root $n)}
if(-not $WriteReference){foreach($n in $names){if($ref.original.$n -ne $sources[$n]){throw "$n is not the reference client file (SHA-256 $($sources[$n])); the transform runs only on the reference client"}}}
$idx=[IO.File]::ReadAllBytes((Join-Path $root 'STAIDX0.MUL'));$st=[IO.File]::ReadAllBytes((Join-Path $root 'STATICS0.MUL'))
$outIdx=$null;$outSt=$null;$table=$null
$watch=[Diagnostics.Stopwatch]::StartNew()
$result=[UoaixFlora]::Strip($idx,$st,$flora,$tree,$vine,[ref]$outIdx,[ref]$outSt,[ref]$table)
$back=[UoaixFlora]::Merge($outIdx,$outSt,$table)
$check=[Security.Cryptography.SHA256]::Create();if([Convert]::ToHexString($check.ComputeHash($back[0])) -ne $sources['STAIDX0.MUL'] -or [Convert]::ToHexString($check.ComputeHash($back[1])) -ne $sources['STATICS0.MUL']){throw 'Restoring the stripped files does not give the original bytes; nothing written'}
$watch.Stop()
$sha=[Security.Cryptography.SHA256]::Create()
$outputs=[ordered]@{'STAIDX0.MUL'=[Convert]::ToHexString($sha.ComputeHash($outIdx));'STATICS0.MUL'=[Convert]::ToHexString($sha.ComputeHash($outSt));'flora.flr'=[Convert]::ToHexString($sha.ComputeHash($table))}
if($WriteReference){
    [ordered]@{original=$sources;stripped=$outputs}|ConvertTo-Json|Set-Content -LiteralPath $Reference
} else {
    foreach($k in $outputs.Keys){if($ref.stripped.$k -ne $outputs[$k]){throw "$k differs from the reference output (SHA-256 $($outputs[$k])); nothing written"}}
}
[IO.File]::WriteAllBytes((Join-Path $OutDir 'flora.flr'),$table)
[IO.File]::WriteAllBytes((Join-Path $OutDir 'STATICS0.MUL'),$outSt)
[IO.File]::WriteAllBytes((Join-Path $OutDir 'STAIDX0.MUL'),$outIdx)
foreach($k in $outputs.Keys){if((Get-Sha (Join-Path $OutDir $k)) -ne $outputs[$k]){throw "$k readback differs"}}
$receipt=[ordered]@{clientRoot=$root;outDir=$OutDir;originalSha256=$sources;strippedSha256=$outputs;treeStatics=$result[0];otherFlora=$result[1];keptStatics=$result[2];rows=$result[3];sourceRecords=$result[4];elapsedSeconds=$watch.Elapsed.TotalSeconds;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutDir 'flora.json')
Write-Output "STRIPPED rows=$($result[3]) trees=$($result[0]) other-flora=$($result[1]) kept=$($result[2]) $OutDir"
