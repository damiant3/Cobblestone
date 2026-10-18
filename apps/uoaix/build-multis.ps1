[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$OutDir,[string]$Config=(Join-Path $PSScriptRoot 'multis.cfg'),[string]$DecorationFile='')
# Buildable multis (docs/Designs/Active/Apps/UoaixMultis.md): cuts multis.cfg's pieces out of the unstripped client's
# map and writes MULTI.IDX and MULTI.MUL with every design appended. MULTI.IDX is 4096 entries of offset, length and
# extra (i32); a MULTI.MUL component is art (u16), x, y, z (i16) and flags (u32, 1 drawn). MAP0 is 196 bytes a block
# (a 4-byte header, then 64 cells of art u16 and z i8); STAIDX0 is 12 bytes a block and a static is art, x and y within
# its block, z and hue (7 bytes). VERDATA.MUL file 14 entries patch a multi id the client holds.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ClientRoot).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($OutDir.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $OutDir -eq $repo){throw 'Client files stay outside tracked source, shipped images and published artifacts (ruling R1)'}
$flora=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'flora-reference.json') -Raw|ConvertFrom-Json
foreach($n in 'STAIDX0.MUL','STATICS0.MUL'){if((Get-FileHash -LiteralPath (Join-Path $root $n) -Algorithm SHA256).Hash -ne $flora.original.$n){throw "$n in $root is not the unstripped client's; pieces are cut from the original map"}}
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
public static class UoaixMultis {
 public class Piece { public string Name; public int X0,Y0,X1,Y1,ZLo=-128,ZHi=127,OX,OY,OZ; public bool Origin; public List<int[]> Land=new List<int[]>(); public List<int[]> Except=new List<int[]>(); public List<int[]> Doors=new List<int[]>(); }
 // A piece's front door (a single or a double door that locks with a deed house's key) is a map door: x, y, z and its
 // closed art, open art and open offset, taken from the decoration (DWD1: 176-byte rows of qwords x, y, z, closed art,
 // hue, flags, height, kind (1 door, 4 locked door), open art, dx, dy, ...) when the line gives only the place.
 public static List<string> Doors=new List<string>();
 static int[] DoorAt(byte[] dwd,int x,int y,int z,string where){
  if(dwd==null)throw new Exception(where+" door needs -DecorationFile, or its arts and offset on the line");
  long n=BitConverter.ToInt64(dwd,8);
  for(long i=0;i<n;i++){int a=(int)(64+i*176);long kind=BitConverter.ToInt64(dwd,a+56);
   if((kind==1||kind==4)&&BitConverter.ToInt64(dwd,a)==x&&BitConverter.ToInt64(dwd,a+8)==y&&BitConverter.ToInt64(dwd,a+16)==z)
    return new int[]{x,y,z,(int)BitConverter.ToInt64(dwd,a+24),(int)BitConverter.ToInt64(dwd,a+64),(int)BitConverter.ToInt64(dwd,a+72),(int)BitConverter.ToInt64(dwd,a+80)};}
  throw new Exception(where+" names no decoration door at "+x+" "+y+" "+z);
 }
 public class Design { public int Id; public string Name; public List<int[]> Parts=new List<int[]>(); }
 public static List<string> Report=new List<string>();
 public static List<string> Names=new List<string>();
 static int Num(string t,string where){
  if(t.StartsWith("0x",StringComparison.OrdinalIgnoreCase)&&int.TryParse(t.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int h))return h;
  if(int.TryParse(t,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int d))return d;
  throw new Exception(where+" '"+t+"' is not a number");
 }
 static int LandArt(byte[] map,int x,int y){return BitConverter.ToUInt16(map,((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3);}
 static int LandZ(byte[] map,int x,int y){return (sbyte)map[((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3+2];}
 // A piece's components relative to its origin: dx, dy, dz, art.
 static List<int[]> Cut(Piece p,byte[] map,byte[] idx,byte[] st){
  var o=new List<int[]>();
  for(int bx=p.X0/8;bx<=p.X1/8;bx++)for(int by=p.Y0/8;by<=p.Y1/8;by++){
   int b=bx*512+by;uint off=BitConverter.ToUInt32(idx,b*12),len=BitConverter.ToUInt32(idx,b*12+4);if(off==0xFFFFFFFF)continue;
   if(len%7!=0||(long)off+len>st.Length)throw new Exception("Invalid static range in block "+b);
   for(long n=off;n<off+len;n+=7){int x=bx*8+st[n+2],y=by*8+st[n+3],art=BitConverter.ToUInt16(st,(int)n),z=(sbyte)st[n+4];
    if(x<p.X0||x>p.X1||y<p.Y0||y>p.Y1||z<p.ZLo||z>p.ZHi)continue;
    bool skip=false;foreach(var e in p.Except)if(art>=e[0]&&art<=e[1])skip=true;if(skip)continue;
    o.Add(new int[]{x-p.OX,y-p.OY,z-p.OZ,art});}
  }
  for(int x=p.X0;x<=p.X1;x++)for(int y=p.Y0;y<=p.Y1;y++){int a=LandArt(map,x,y),z=LandZ(map,x,y);if(z<p.ZLo||z>p.ZHi)continue;
   foreach(var l in p.Land)if(a>=l[0]&&a<=l[1]){o.Add(new int[]{x-p.OX,y-p.OY,z-p.OZ,l[2]+(a-l[0])});break;}}
  return o;
 }
 public static byte[][] Build(string root,string config,string decoration){
  Report.Clear();Names.Clear();Doors.Clear();pieceAt.Clear();
  byte[] dwd=String.IsNullOrEmpty(decoration)?null:File.ReadAllBytes(decoration);
  if(dwd!=null&&(dwd.Length<64||BitConverter.ToInt64(dwd,0)!=0x31445744||dwd.Length!=64+BitConverter.ToInt64(dwd,8)*176))throw new Exception("Invalid DWD1 decoration file");
  byte[] map=File.ReadAllBytes(Path.Combine(root,"MAP0.MUL")),idx=File.ReadAllBytes(Path.Combine(root,"STAIDX0.MUL")),st=File.ReadAllBytes(Path.Combine(root,"STATICS0.MUL"));
  byte[] midx=File.ReadAllBytes(Path.Combine(root,"MULTI.IDX")),mmul=File.ReadAllBytes(Path.Combine(root,"MULTI.MUL"));
  if(map.Length!=77070336||idx.Length!=4718592||midx.Length!=4096*12)throw new Exception("Legacy MAP0, STAIDX0 and 4096-entry MULTI.IDX required");
  var taken=new HashSet<int>();
  for(int i=0;i<4096;i++){int off=BitConverter.ToInt32(midx,i*12),len=BitConverter.ToInt32(midx,i*12+4);if(off>=0&&len>0)taken.Add(i);}
  string ver=Path.Combine(root,"VERDATA.MUL");
  if(File.Exists(ver)){byte[] v=File.ReadAllBytes(ver);int n=BitConverter.ToInt32(v,0);for(int i=0;i<n;i++)if(BitConverter.ToInt32(v,4+i*20)==14)taken.Add(BitConverter.ToInt32(v,8+i*20));}
  var pieces=new Dictionary<string,Piece>();var designs=new List<Design>();var names=new HashSet<string>();
  Piece piece=null;Design design=null;int lineNo=0;
  foreach(string raw in File.ReadLines(config)){
   lineNo++;string where="multis.cfg:"+lineNo;string text=raw;int hash=text.IndexOf('#');if(hash>=0)text=text.Substring(0,hash);text=text.Trim();if(text.Length==0)continue;
   string[] w=text.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
   if(w[0]=="Piece"){
    if(w.Length!=6&&w.Length!=8)throw new Exception(where+" Piece takes a name, x0 y0 x1 y1 and an optional z range");
    piece=new Piece{Name=w[1],X0=Num(w[2],where),Y0=Num(w[3],where),X1=Num(w[4],where),Y1=Num(w[5],where)};design=null;
    if(w.Length==8){piece.ZLo=Num(w[6],where);piece.ZHi=Num(w[7],where);}
    if(piece.X0<0||piece.Y0<0||piece.X1>=6144||piece.Y1>=4096||piece.X0>piece.X1||piece.Y0>piece.Y1||piece.ZLo>piece.ZHi)throw new Exception(where+" Piece rectangle outside the legacy map or reversed");
    if(pieces.ContainsKey(piece.Name))throw new Exception(where+" Piece "+piece.Name+" named twice");
    piece.OX=(piece.X0+piece.X1)/2;piece.OY=(piece.Y0+piece.Y1)/2;piece.OZ=LandZ(map,piece.OX,piece.OY);pieces[piece.Name]=piece;continue;
   }
   if(w[0]=="Design"){
    if(w.Length!=3)throw new Exception(where+" Design takes a multi id and a name");
    design=new Design{Id=Num(w[1],where),Name=w[2]};piece=null;
    if(design.Id<0||design.Id>=4096)throw new Exception(where+" Design id outside MULTI.IDX's 4096 entries");
    if(taken.Contains(design.Id))throw new Exception(where+" Design id 0x"+design.Id.ToString("X")+" is a client multi (MULTI.IDX or VERDATA)");
    if(!names.Add(design.Name))throw new Exception(where+" Design "+design.Name+" named twice");
    if(design.Name.Length>28||!System.Text.RegularExpressions.Regex.IsMatch(design.Name,"^[a-z0-9-]+$"))throw new Exception(where+" Design name must be at most 28 of a-z, 0-9 and -");
    taken.Add(design.Id);designs.Add(design);continue;
   }
   if(piece!=null){
    if(w[0]=="origin"&&w.Length==4){piece.OX=Num(w[1],where);piece.OY=Num(w[2],where);piece.OZ=Num(w[3],where);continue;}
    if(w[0]=="land"&&w.Length==4){piece.Land.Add(new int[]{Num(w[1],where),Num(w[2],where),Num(w[3],where)});continue;}
    if(w[0]=="except"&&(w.Length==2||w.Length==3)){int a=Num(w[1],where);piece.Except.Add(new int[]{a,w.Length==3?Num(w[2],where):a});continue;}
    if(w[0]=="door"&&w.Length==4){piece.Doors.Add(DoorAt(dwd,Num(w[1],where),Num(w[2],where),Num(w[3],where),where));continue;}
    if(w[0]=="door"&&w.Length==8){piece.Doors.Add(new int[]{Num(w[1],where),Num(w[2],where),Num(w[3],where),Num(w[4],where),Num(w[5],where),Num(w[6],where),Num(w[7],where)});continue;}
    throw new Exception(where+" unparsed in Piece "+piece.Name);
   }
   if(design!=null){
    if(w[0]=="clear"&&(w.Length==5||w.Length==7)){design.Parts.Add(new int[]{-1,Num(w[1],where),Num(w[2],where),Num(w[3],where),Num(w[4],where),w.Length==7?Num(w[5],where):-32768,w.Length==7?Num(w[6],where):32767});continue;}
    if(w[0]=="multi"&&w.Length==5){design.Parts.Add(new int[]{-2,Num(w[1],where),Num(w[2],where),Num(w[3],where),Num(w[4],where)});continue;}
    if(w[0]=="item"&&w.Length==5){design.Parts.Add(new int[]{-3,Num(w[1],where),Num(w[2],where),Num(w[3],where),Num(w[4],where)});continue;}
    if(w.Length==4&&pieces.ContainsKey(w[0])){design.Parts.Add(new int[]{lineNo,Num(w[1],where),Num(w[2],where),Num(w[3],where)});pieceAt[lineNo]=w[0];continue;}
    throw new Exception(where+" unparsed in Design "+design.Name+" (a piece must be defined above it; multi 0xID and item 0xART take dx dy dz)");
   }
   throw new Exception(where+" unparsed");
  }
  var cut=new Dictionary<string,List<int[]>>();var built=new Dictionary<int,List<int[]>[]>();
  var outMul=new MemoryStream();outMul.Write(mmul,0,mmul.Length);byte[] outIdx=(byte[])midx.Clone();byte[] rec=new byte[12];
  foreach(var d in designs){
   var comps=new List<int[]>();var doors=new List<int[]>();
   foreach(var part in d.Parts){
    // multi: an earlier design (its doors too) or a client multi (its drawn components); item: one static.
    if(part[0]==-2){
     if(built.ContainsKey(part[1])){foreach(var c in built[part[1]][0])comps.Add(new int[]{c[0]+part[2],c[1]+part[3],c[2]+part[4],c[3]});foreach(var o in built[part[1]][1])doors.Add(new int[]{o[0]+part[2],o[1]+part[3],o[2]+part[4],o[3],o[4],o[5],o[6]});continue;}
     if(part[1]<0||part[1]>=4096)throw new Exception("Design "+d.Name+" names multi "+part[1]+" outside MULTI.IDX");
     int off=BitConverter.ToInt32(midx,part[1]*12),len=BitConverter.ToInt32(midx,part[1]*12+4);
     if(off<0||len<=0||(long)off+len>mmul.Length)throw new Exception("Design "+d.Name+" names multi 0x"+part[1].ToString("X")+", which neither the client nor an earlier design holds");
     for(int a=off;a<off+len;a+=12)if(BitConverter.ToInt32(mmul,a+8)!=0)comps.Add(new int[]{BitConverter.ToInt16(mmul,a+2)+part[2],BitConverter.ToInt16(mmul,a+4)+part[3],BitConverter.ToInt16(mmul,a+6)+part[4],BitConverter.ToUInt16(mmul,a)});
     continue;
    }
    if(part[0]==-3){comps.Add(new int[]{part[2],part[3],part[4],part[1]});continue;}
    if(part[0]<0){comps.RemoveAll(c=>c[0]>=part[1]&&c[0]<=part[3]&&c[1]>=part[2]&&c[1]<=part[4]&&c[2]>=part[5]&&c[2]<=part[6]);
     doors.RemoveAll(c=>c[0]>=part[1]&&c[0]<=part[3]&&c[1]>=part[2]&&c[1]<=part[4]&&c[2]>=part[5]&&c[2]<=part[6]);continue;}
    string name=pieceAt[part[0]];if(!cut.ContainsKey(name))cut[name]=Cut(pieces[name],map,idx,st);
    foreach(var c in cut[name])comps.Add(new int[]{c[0]+part[1],c[1]+part[2],c[2]+part[3],c[3]});
    var p=pieces[name];foreach(var o in p.Doors)doors.Add(new int[]{o[0]-p.OX+part[1],o[1]-p.OY+part[2],o[2]-p.OZ+part[3],o[3],o[4],o[5],o[6]});
   }
   // A house locks one door or one double door: the first two a design takes are its front doors.
   string spare=doors.Count>2?" doors="+doors.Count+" (the first 2 lock)":"";
   if(doors.Count>2)doors.RemoveRange(2,doors.Count-2);
   built[d.Id]=new List<int[]>[]{comps,doors};
   foreach(var o in doors)Doors.Add(String.Format("0x{0:X4} {1} {2} {3} 0x{4:X4} 0x{5:X4} {6} {7}",d.Id,o[0],o[1],o[2],o[3],o[4],o[5],o[6]));
   if(comps.Count==0)throw new Exception("Design "+d.Name+" has no components");
   int x0=int.MaxValue,x1=int.MinValue,y0=int.MaxValue,y1=int.MinValue,z0=int.MaxValue,z1=int.MinValue;
   long start=outMul.Length;
   foreach(var c in comps){
    if(c[0]<-32768||c[0]>32767||c[1]<-32768||c[1]>32767||c[2]<-128||c[2]>127||c[3]<1||c[3]>=16384)throw new Exception("Design "+d.Name+" component outside the multi record");
    x0=Math.Min(x0,c[0]);x1=Math.Max(x1,c[0]);y0=Math.Min(y0,c[1]);y1=Math.Max(y1,c[1]);z0=Math.Min(z0,c[2]);z1=Math.Max(z1,c[2]);
    BitConverter.TryWriteBytes(rec.AsSpan(0,2),(ushort)c[3]);BitConverter.TryWriteBytes(rec.AsSpan(2,2),(short)c[0]);BitConverter.TryWriteBytes(rec.AsSpan(4,2),(short)c[1]);BitConverter.TryWriteBytes(rec.AsSpan(6,2),(short)c[2]);BitConverter.TryWriteBytes(rec.AsSpan(8,4),1);outMul.Write(rec,0,12);
   }
   BitConverter.TryWriteBytes(outIdx.AsSpan(d.Id*12,4),(int)start);BitConverter.TryWriteBytes(outIdx.AsSpan(d.Id*12+4,4),comps.Count*12);BitConverter.TryWriteBytes(outIdx.AsSpan(d.Id*12+8,4),0);
   Report.Add("MULTI 0x"+d.Id.ToString("X4")+" "+d.Name+" components="+comps.Count+" x="+x0+".."+x1+" y="+y0+".."+y1+" z="+z0+".."+z1+spare);
   Names.Add("0x"+d.Id.ToString("X4")+" "+d.Name);
  }
  return new byte[][]{outIdx,outMul.ToArray()};
 }
 static Dictionary<int,string> pieceAt=new Dictionary<int,string>();
}
'@
$result=[UoaixMultis]::Build($root,(Resolve-Path -LiteralPath $Config).Path,$(if($DecorationFile){(Resolve-Path -LiteralPath $DecorationFile).Path}else{''}))
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
[IO.File]::WriteAllBytes((Join-Path $OutDir 'MULTI.IDX'),$result[0])
[IO.File]::WriteAllBytes((Join-Path $OutDir 'MULTI.MUL'),$result[1])
# The design names for the server (install-map-cache.ps1 -MultiDir puts them in the map cache): one "0xID name" a line.
[IO.File]::WriteAllLines((Join-Path $OutDir 'MULTI.NAMES'),[string[]][UoaixMultis]::Names)
# Each design's front doors for the server: "0xID dx dy dz 0xCLOSED 0xOPEN odx ody" a line, relative to the design origin.
[IO.File]::WriteAllLines((Join-Path $OutDir 'MULTI.DOORS'),[string[]][UoaixMultis]::Doors)
$receipt=[ordered]@{clientRoot=$root;outDir=$OutDir;designs=@([UoaixMultis]::Report);multisCfgSha256=(Get-FileHash -LiteralPath $Config -Algorithm SHA256).Hash;multiIdxSha256=(Get-FileHash -LiteralPath (Join-Path $OutDir 'MULTI.IDX') -Algorithm SHA256).Hash;multiMulSha256=(Get-FileHash -LiteralPath (Join-Path $OutDir 'MULTI.MUL') -Algorithm SHA256).Hash;localOnly=$true}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutDir 'build-multis.json')
[UoaixMultis]::Report
Write-Output "MULTIS designs=$([UoaixMultis]::Report.Count) $OutDir"
