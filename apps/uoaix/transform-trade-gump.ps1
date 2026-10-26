[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientRoot,[Parameter(Mandatory)][string]$OutDir,[int]$AdminPort=0,[string]$KeyFile='',[string[]]$PlayerClients=@())
# Makes the 1.25 client's buy and sell windows coin aware (UOAIX-169). The server writes each line's price into its name
# ("<item> at 6s 2c", gvp-priced), so the client's own copper number on a line is dropped; the Bill of Sale and Offer
# totals are drawn in gold, silver and copper by a formatter added to CLIENT.EXE at the royal rates read from the running
# shard (-AdminPort, -KeyFile) or, with no shard, the founding rates (bb-coin-copper, bb-coin-silver). A rate edit needs a
# fresh run of this transform. -PlayerClients installs the files into each client (CLIENT2.EXE where the client has
# one), its own files backed up first.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# pwsh -File hands "-PlayerClients a,b" over as one string.
$PlayerClients=@($PlayerClients|ForEach-Object{$_ -split ','}|Where-Object{$_})
$root=(Resolve-Path -LiteralPath $ClientRoot).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($OutDir.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $OutDir -eq $repo){throw 'Client files stay outside tracked source, shipped images and published artifacts (ruling R1)'}
function Get-Sha([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash}
$names='CLIENT.EXE','GUMPART.MUL','GUMPIDX.MUL'
# The 1998 client (1.25.32) every patch site below was read from.
$original=[ordered]@{'CLIENT.EXE'='F78A864042E3762B896CCDF1423E96D3023A969270C4EB6ACBD207CC71D80D07';'GUMPART.MUL'='32E634D518A127E97BD5BE15FA493D658C95B864086D7D55E1EADF49567029D5';'GUMPIDX.MUL'='41BFA0E97491ACC28F5E08A1C59960F9CE5F8FBF2DED438A0E0498F8DEA29BFA'}
foreach($n in $names){$p=Join-Path $root $n;if((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Client files must not be reparse points'}
    $h=Get-Sha $p;if($h -ne $original[$n]){throw "$n is not the reference client file (SHA-256 $h); the transform runs only on the reference client"}}

$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'BritainProduction.codex'))
$copper=[int][regex]::Match($source,'(?m)^  bb-coin-copper : Integer = (\d+)').Groups[1].Value
$silver=[int][regex]::Match($source,'(?m)^  bb-coin-silver : Integer = (\d+)').Groups[1].Value
$rateSource='founding (BritainProduction.codex)'
if($AdminPort -gt 0){
    if(-not $KeyFile){throw '-AdminPort needs -KeyFile'}
    $view=(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'admin-read.ps1') -Port $AdminPort -KeyFile $KeyFile -Command treasury-view) -join "`n"
    if($LASTEXITCODE -ne 0){throw "Reading the shard's rates failed: $view"}
    $t=$view|ConvertFrom-Json
    if(-not ($t.PSObject.Properties.Name -contains 'copper_per_gold')){throw "The shard reports no copper rates: $view"}
    $copper=[int]$t.copper_per_gold;$silver=[int]$t.silver_per_gold;$rateSource="shard admin port $AdminPort"
}
if($copper -lt 1 -or $silver -lt 0){throw "Rates out of range: $copper copper and $silver silver to the gold"}
# gvp-per-silver; a gold-only world (copper-rate 1 or less) counts every total in gold, as gvp-coins does.
$perSilver=if($silver -lt 1 -or $silver -gt $copper){$copper}else{[Math]::Floor($copper/$silver)}
$goldOnly=$copper -le 1
if($goldOnly){$copper=1;$perSilver=1}

Add-Type -TypeDefinition @'
using System;
public static class UoaixGump {
 // A gump entry: one u32 per row, the row's offset in u32 units from the entry start; each row is (u16 colour, u16 run)
 // pairs covering the width, colour 0 transparent.
 public static ushort[] Decode(byte[] d,int w,int h){
  var px=new ushort[w*h];
  for(int y=0;y<h;y++){int p=BitConverter.ToInt32(d,y*4)*4,end=y<h-1?BitConverter.ToInt32(d,(y+1)*4)*4:d.Length,x=0;
   while(p<end){ushort c=BitConverter.ToUInt16(d,p);int n=BitConverter.ToUInt16(d,p+2);p+=4;
    if(x+n>w)throw new Exception("Row "+y+" overruns the width");for(int k=0;k<n;k++)px[y*w+x+k]=c;x+=n;}
   if(x!=w)throw new Exception("Row "+y+" covers "+x+" of "+w);}
  return px;
 }
 public static byte[] Encode(ushort[] px,int w,int h){
  var rows=new System.Collections.Generic.List<byte>();var head=new byte[h*4];
  for(int y=0;y<h;y++){BitConverter.TryWriteBytes(head.AsSpan(y*4,4),(h*4+rows.Count)/4);
   int x=0;while(x<w){ushort c=px[y*w+x];int n=1;while(x+n<w&&px[y*w+x+n]==c)n++;
    rows.Add((byte)c);rows.Add((byte)(c>>8));rows.Add((byte)n);rows.Add((byte)(n>>8));x+=n;}}
  var o=new byte[head.Length+rows.Count];Buffer.BlockCopy(head,0,o,0,head.Length);rows.CopyTo(o,head.Length);return o;
 }
}
'@
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
$exe=[IO.File]::ReadAllBytes((Join-Path $root 'CLIENT.EXE'))
$idx=[IO.File]::ReadAllBytes((Join-Path $root 'GUMPIDX.MUL'))
$art=[IO.File]::ReadAllBytes((Join-Path $root 'GUMPART.MUL'))
function Expect([byte[]]$B,[int]$At,[string]$Hex,[string]$What){$want=[Convert]::FromHexString($Hex);for($i=0;$i -lt $want.Length;$i++){if($B[$At+$i] -ne $want[$i]){throw "CLIENT.EXE at 0x$('{0:X}' -f $At) is not $What"}}}
function Put([byte[]]$B,[int]$At,[byte[]]$Bytes){[Array]::Copy($Bytes,0,$B,$At,$Bytes.Length)}
function Le([long]$V){[BitConverter]::GetBytes([int32]$V)}
# File offsets of the client's .text (raw 0x400, VA 0x401000) and .data (raw 0xA4600, VA 0x4A6000).
function Raw([long]$Va){if($Va -ge 0x4A6000){$Va-0x4A6000+0xA4600}else{$Va-0x401000+0x400}}
$sprintf=0x48B1D0;$cave=0x49BAD0
# CShopGump line "%s at %dgp" (name, copper price): the name carries the coin price, so the line draws the name alone.
Expect $exe (Raw 0x4AE5C4) ([Convert]::ToHexString([Text.Encoding]::ASCII.GetBytes("%s at %dgp`0"))) 'the shop line format'
Put $exe (Raw 0x4AE5C4) ([byte[]](0x25,0x73,0,0,0,0,0,0,0,0,0))
# CBillGump second line "at %d g.p." under each bill line's name: dropped for the same reason.
Expect $exe (Raw 0x4AE64C) ([Convert]::ToHexString([Text.Encoding]::ASCII.GetBytes("at %d g.p.`0"))) 'the bill line format'
Put $exe (Raw 0x4AE64C) ([byte[]]::new(11))
# CBillGump total: sprintf(buf, "%d", copper total) at 0x46E86B now calls the coin formatter (same cdecl arguments).
Expect $exe (Raw 0x46E86B) ('E8'+[Convert]::ToHexString((Le ($sprintf-0x46E870)))) 'the total sprintf call'
Put $exe (Raw 0x46E86B) ([byte[]](@(0xE8)+(Le ($cave-0x46E870))))
# Bill of Sale "Gold Avail" number: 15 pixels down (y 0xAB to 0xBA) with its label, freeing the total's row.
# The server sends the purse and pack coin as copper in the status gold (UOAIX-279; it arrives as the dword 0x64CD74,
# copied whole into the status gump at +0xD0 and as a word into the Bill's). Both draws call the coin formatter:
# the status gump's sprintf(buf, fmt, [ebx+0xD0]) at 0x4692A9, and the Bill's Coin Available, whose word read
# (mov dx,[ecx+0xD0]; push edx; push fmt; push eax; call sprintf, 0x46E8F3..0x46E905) becomes
# push dword [0x64CD74]; push fmt; push eax; call formatter; nop; nop.
Expect $exe (Raw 0x469299) ('8B83D0000000'+'8D4DB850'+'6830E44A00'+'51'+'E8'+[Convert]::ToHexString((Le ($sprintf-0x4692AE)))) 'the status gump gold draw'
Put $exe (Raw 0x4692A9) ([byte[]](@(0xE8)+(Le ($cave-0x4692AE))))
Expect $exe (Raw 0x46E8F3) ('668B91D0000000'+'52'+'681C644A00'+'50'+'E8'+[Convert]::ToHexString((Le ($sprintf-0x46E906)))) 'the Gold Avail draw'
Put $exe (Raw 0x46E8F3) ([byte[]](@(0xFF,0x35,0x74,0xCD,0x64,0x00,0x68,0x1C,0x64,0x4A,0x00,0x50,0xE8)+(Le ($cave-0x46E904))+@(0x90,0x90)))
Expect $exe (Raw 0x46E943) '81C1AB000000' 'the Gold Avail row'
$exe[(Raw 0x46E945)]=0xBA
# Its x (push 0xBC): moved past the wider "Coin Available:" label once the label's end is known, below.
Expect $exe (Raw 0x46E94C) '68BC000000' 'the Gold Avail x'
# The coin formatter, in .text slack past its virtual size (0x9AAC6, raw 0x9AC00, all zero in the reference client):
# n = [esp+12]; writes "<g>g <s>s <c>c" into [esp+4], omitting zero coins and printing "0c" for zero, through the
# client's own sprintf; returns the length.
$code=('558BEC5356578B7D088BDFC607008B451033D2B9{C}F7F18BF285C0740ABA{SG}E838000000'+
    '8BC633D2B9{P}F7F18BF285C0740ABA{SS}E81D00000085F675043BDF750C8BC6BA{SC}E809000000'+
    '8BC32BC75F5E5B5DC33BDF7501425052 53E8{SP}83C40C803B00740343EBF8C3').Replace(' ','')
$hex=@{C=$copper;P=$perSilver;SG=$cave+0x7B;SS=$cave+0x80;SC=$cave+0x85;SP=$sprintf-($cave+0x6F)}
foreach($k in $hex.Keys){$code=$code.Replace("{$k}",[Convert]::ToHexString((Le $hex[$k])))}
$caveBytes=[byte[]]([Convert]::FromHexString($code)+[Text.Encoding]::ASCII.GetBytes(" %dg`0 %ds`0 $(if($goldOnly){'%dg'}else{'%dc'})`0"))
if($caveBytes.Length -ne 0x8A){throw "Formatter is $($caveBytes.Length) bytes, not 138"}
$caveRaw=Raw $cave
for($i=0;$i -lt $caveBytes.Length;$i++){if($exe[$caveRaw+$i] -ne 0){throw 'CLIENT.EXE .text slack is not empty'}}
Put $exe $caveRaw $caveBytes
$pe=[BitConverter]::ToInt32($exe,0x3C);$text=$pe+0x18+[BitConverter]::ToUInt16($exe,$pe+0x14)
if([Text.Encoding]::ASCII.GetString($exe,$text,5) -ne '.text' -or [BitConverter]::ToUInt32($exe,$text+8) -ne 0x9AAC6){throw 'CLIENT.EXE .text header is not the reference one'}
Put $exe ($text+8) (Le 0x9AC00)

# Bill of Sale art (gump 0x871): the "Gold Avail:" label (x 115..190, rows 168..182) trades places with the clean
# parchment under it (rows 183..197), so the total has its row to the right edge.
$id=0x871;$off=[BitConverter]::ToInt32($idx,$id*12);$len=[BitConverter]::ToInt32($idx,$id*12+4);$extra=[BitConverter]::ToInt32($idx,$id*12+8)
$w=($extra -shr 16) -band 0xFFFF;$h=$extra -band 0xFFFF
if($w -ne 283 -or $h -ne 248){throw "Gump 0x871 is ${w}x$h, not 283x248"}
$entry=[byte[]]::new($len);[Array]::Copy($art,$off,$entry,0,$len)
$px=[UoaixGump]::Decode($entry,$w,$h)
# "Gold Avail:" becomes "Coin Available:" (Damian, 2026-10-20), lettered from this gump's own blackletter: C from
# "Clear" (rows 220-231), the n from the m of "Amt." and the e from "More" (rows 43-50), the b the label's d mirrored, the
# rest the label's own glyphs (rows 171-182). A glyph's ink (blue or near black) is drawn over parchment from rows 38 up.
function Ink([int]$c){$r=($c -shr 10) -band 31;$g=($c -shr 5) -band 31;$b=$c -band 31;($b -gt $r+3 -and $r -lt 14) -or ($r+$g+$b -lt 30)}
$glyphs=@(@{x0=170;x1=177;y0=220;y1=231;dy=-50},@{x0=128;x1=132;y0=171;y1=182;dy=0},@{x0=171;x1=174;y0=171;y1=182;dy=0},
    @{x0=44;x1=49;y0=43;y1=50;dy=130},@{space=4},@{x0=148;x1=156;y0=171;y1=182;dy=0},@{x0=157;x1=162;y0=171;y1=182;dy=0},
    @{x0=164;x1=168;y0=171;y1=182;dy=0},@{x0=171;x1=174;y0=171;y1=182;dy=0},@{x0=177;x1=181;y0=171;y1=182;dy=0},
    @{x0=164;x1=168;y0=171;y1=182;dy=0},@{x0=138;x1=143;y0=171;y1=182;dy=0;mirror=$true},@{x0=177;x1=181;y0=171;y1=182;dy=0},
    @{x0=186;x1=190;y0=43;y1=50;dy=130},@{x0=182;x1=183;y0=171;y1=182;dy=0})
$labelEnd=118;foreach($gl in $glyphs){$labelEnd+=if($gl.ContainsKey('space')){$gl.space}else{$gl.x1-$gl.x0+2}}
$swapEnd=[Math]::Max(190,$labelEnd+2)
if($swapEnd -gt 240){throw "Coin Available label ends at x $labelEnd, past the clean parchment"}
$src=[ushort[]]$px.Clone()
for($y=168;$y -le 182;$y++){for($x=115;$x -le $swapEnd;$x++){$px[$y*$w+$x]=$src[($y-38)*$w+$x]}}
$at=118
foreach($gl in $glyphs){if($gl.ContainsKey('space')){$at+=$gl.space;continue}
    $gw=$gl.x1-$gl.x0+1
    for($y=$gl.y0;$y -le $gl.y1;$y++){for($i=0;$i -lt $gw;$i++){$sx=if($gl.ContainsKey('mirror')){$gl.x1-$i}else{$gl.x0+$i}
        $c=$src[$y*$w+$sx];if(Ink $c){$px[($y+$gl.dy)*$w+$at+$i]=$c}}}
    $at+=$gw+1}
for($y=168;$y -le 182;$y++){for($x=115;$x -le $swapEnd;$x++){$a=$y*$w+$x;$b=($y+15)*$w+$x;$t=$px[$a];$px[$a]=$px[$b];$px[$b]=$t}}
Put $exe (Raw 0x46E94D) (Le ($labelEnd+4))
$new=[UoaixGump]::Encode($px,$w,$h)
$back=[UoaixGump]::Decode($new,$w,$h);for($i=0;$i -lt $px.Length;$i++){if($back[$i] -ne $px[$i]){throw 'Re-encoded gump 0x871 does not decode to its pixels'}}
$outArt=[byte[]]::new($art.Length+$new.Length);[Array]::Copy($art,$outArt,$art.Length);[Array]::Copy($new,0,$outArt,$art.Length,$new.Length)
Put $idx ($id*12) (Le $art.Length);Put $idx ($id*12+4) (Le $new.Length)

# CLIENT2.EXE, the second seat's executable (uoaix-client2): CLIENT.EXE with the strings "UoClientAp2" and "UO Playe2"
# and the byte at 0x4A6F2 from 74 (je) to EB (jmp). Its reference hash is those three edits over the reference CLIENT.EXE.
$original['CLIENT2.EXE']='707B10FB323E7950A6CFC220C0C90CC640E1B31F534686F889E97C11A8FA0002'
$exe2=[byte[]]$exe.Clone()
foreach($e in @(@(0x4A6F2,0x74,0xEB),@(0xA8644,0x72,0x32),@(0xAEA36,0x70,0x32))){if($exe2[$e[0]] -ne $e[1]){throw "CLIENT.EXE at 0x$('{0:X}' -f $e[0]) is not the CLIENT2 edit's original byte"};$exe2[$e[0]]=$e[2]}
$names+='CLIENT2.EXE'
$outputs=[ordered]@{}
$bytes=@{'CLIENT.EXE'=$exe;'CLIENT2.EXE'=$exe2;'GUMPART.MUL'=$outArt;'GUMPIDX.MUL'=$idx}
foreach($n in $names){$p=Join-Path $OutDir $n;[IO.File]::WriteAllBytes($p,$bytes[$n]);$outputs[$n]=Get-Sha $p}
$receipt=[ordered]@{clientRoot=$root;outDir=$OutDir;rates=$rateSource;copperPerGold=$copper;copperPerSilver=$perSilver;goldOnly=$goldOnly;originalSha256=$original;outputSha256=$outputs}
$receipt|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutDir 'trade-gump.json')
"TRADE GUMP copper/gold=$copper copper/silver=$perSilver ($rateSource) $OutDir"

# Install: a client's file must be the reference file or this transform's last install there (its uoaix-trade.json).
foreach($c in $PlayerClients){
    $c=(Resolve-Path -LiteralPath $c).Path;$mark=Join-Path $c 'uoaix-trade.json'
    $last=if(Test-Path -LiteralPath $mark){Get-Content -LiteralPath $mark -Raw|ConvertFrom-Json}else{$null}
    $have=@($names|Where-Object{$_ -ne 'CLIENT2.EXE' -or (Test-Path -LiteralPath (Join-Path $c $_))})
    foreach($n in $have){$h=Get-Sha (Join-Path $c $n);if($h -ne $original[$n] -and -not ($last -and $last.outputSha256.$n -eq $h)){throw "$c\$n is neither the reference file nor this transform's last install; that client and the ones after it are untouched"}}
    $keep=Join-Path $OutDir ("backup-{0}" -f (Split-Path $c -Leaf));[void](New-Item -ItemType Directory -Force $keep)
    foreach($n in $have){$from=Join-Path $c $n;$copy=Join-Path $keep $n;Copy-Item -LiteralPath $from -Destination $copy -Force
        if((Get-Sha $copy) -ne (Get-Sha $from)){throw "Client backup of $from differs; that client is untouched"}}
    foreach($n in $have){$to=Join-Path $c $n;Copy-Item -LiteralPath (Join-Path $OutDir $n) -Destination $to -Force
        if((Get-Sha $to) -ne $outputs[$n]){throw "$to differs from the transformed file; restore from $keep"}}
    $receipt|ConvertTo-Json|Set-Content -LiteralPath $mark
    "INSTALLED $c (backup $keep)"
}
