[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorldDisk,[Parameter(Mandatory)][string]$OutDir,[switch]$Clear)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# UOAIX-50: copies every SEG1 segment of a stopped world's economy archive region to its own file, verified by its FNV32
# and its chain to the one before; -Clear then empties the region (next free sector 1) keeping the segment count and the
# last segment's FNV32, so the server numbers and chains the next segment on from the export.
$WorldDisk=(Resolve-Path -LiteralPath $WorldDisk).Path
$needle=$WorldDisk.Replace('\','/')
if(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'codex-vm.exe' -and $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($needle,[StringComparison]::OrdinalIgnoreCase)}){throw 'A VM has the world disk open; export from a stopped world'}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(-not (Test-Path -LiteralPath $OutDir)){[void](New-Item -ItemType Directory -Path $OutDir)}
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Text; using System.Collections.Generic;
public static class ArchiveExport {
 // MapCache wmc-start (131072) * 512; header cells 304 (region start sector) and 312 (capacity), relative to it.
 public const long Base=67108864;
 public static long R(byte[] b,int o) {return BitConverter.ToInt64(b,o);}
 public static void Q(byte[] b,int o,long v) {BitConverter.GetBytes(v).CopyTo(b,o);}
 // eu-seg-hash and ea-hash: FNV-1a 32 over bytes, skipping the digest at 48..55.
 public static long Hash(byte[] b,long count) {uint h=2166136261;for(long i=0;i<count;i++)if(i<48||i>=56)h=unchecked((h^b[i])*16777619);return h;}
 static byte[] Read(FileStream f,long at,long n) {var b=new byte[n];f.Position=at;f.ReadExactly(b);return b;}
 public static string Run(string disk,string outDir,bool clear) {
  var log=new StringBuilder();
  using var f=new FileStream(disk,FileMode.Open,clear?FileAccess.ReadWrite:FileAccess.Read,FileShare.None);
  if(f.Length<Base+512) return "ARCHIVE none (no map cache)";
  var header=Read(f,Base,512);
  if(R(header,0)!=0x32434D55||R(header,304)<=0) return "ARCHIVE none (a disk installed before the region)";
  long region=R(header,304),capacity=R(header,312);
  if(capacity<2||Base+(region+capacity)*512>f.Length) throw new Exception("Archive region outside the disk; refused");
  long arcAt=Base+region*512;var arc=Read(f,arcAt,512);
  if(R(arc,0)==0&&R(arc,48)==0) return "ARCHIVE region="+region+" capacity="+capacity+" segments=0 next=1: nothing to export";
  if(R(arc,0)!=0x31435241||R(arc,8)!=1||R(arc,48)!=Hash(arc,512)) throw new Exception("Archive header invalid; refused");
  long segments=R(arc,16),next=R(arc,24),last=R(arc,32);
  if(next<1||next>capacity) throw new Exception("Archive header extent invalid; refused");
  if(next==1) return "ARCHIVE region="+region+" capacity="+capacity+" segments="+segments+" next=1: nothing to export";
  log.AppendLine("ARCHIVE region="+region+" capacity="+capacity+" segments="+segments+" next="+next);
  var written=new List<(string path,byte[] bytes)>();long at=1,number=-1,previous=0;
  while(at<next) {
   var head=Read(f,arcAt+at*512,512);long bytes=R(head,16);
   if(R(head,0)!=0x31474553||bytes<128||at+(bytes+511)/512>next) throw new Exception("Archive segment header at sector "+at+" invalid; refused");
   var seg=Read(f,arcAt+at*512,bytes);long n=R(seg,24),hash=R(seg,48),prev=R(seg,88);
   if(hash!=Hash(seg,bytes)) throw new Exception("Archive segment "+n+" checksum invalid; refused");
   if(number>=0&&(n!=number+1||prev!=previous)) throw new Exception("Archive segment "+n+" chain broken; refused");
   string path=Path.Combine(outDir,"segment-"+n.ToString("D6")+".seg");
   if(File.Exists(path)) throw new Exception(path+" exists; export into a new directory");
   written.Add((path,seg));number=n;previous=hash;at+=(bytes+511)/512;
   log.AppendLine("SEGMENT "+n+" bytes="+bytes+" fnv="+hash+" previous="+prev);
  }
  if(number!=segments||previous!=last) throw new Exception("Archive walk ends at segment "+number+", the header names "+segments+"; refused");
  foreach(var w in written){File.WriteAllBytes(w.path,w.bytes);var back=File.ReadAllBytes(w.path);if(back.Length!=w.bytes.Length||Hash(back,back.Length)!=R(back,48))throw new Exception(w.path+" did not read back");}
  var index=new StringBuilder();foreach(var w in written)index.AppendLine(Path.GetFileName(w.path)+" "+R(w.bytes,24)+" "+w.bytes.Length+" "+R(w.bytes,48)+" "+R(w.bytes,88));
  File.AppendAllText(Path.Combine(outDir,"index.txt"),index.ToString());
  log.AppendLine("EXPORTED "+written.Count+" segments to "+outDir);
  if(clear) {
   var fresh=new byte[512];Q(fresh,0,0x31435241);Q(fresh,8,1);Q(fresh,16,segments);Q(fresh,24,1);Q(fresh,32,last);Q(fresh,48,Hash(fresh,512));
   f.Position=arcAt;f.Write(fresh);f.Flush(true);
   var check=Read(f,arcAt,512);if(R(check,24)!=1||R(check,48)!=Hash(check,512))throw new Exception("Archive header rewrite did not read back");
   log.AppendLine("CLEARED region: next=1, segments="+segments+", last="+last);
  }
  return log.ToString().TrimEnd();
 }
}
'@
[ArchiveExport]::Run($WorldDisk,$OutDir,$Clear.IsPresent)
