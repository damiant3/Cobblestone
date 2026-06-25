<#
.SYNOPSIS
    Generate TrueType fonts from a trained FontAI model.
    Loads weights, runs MLP inference for all 95 ASCII glyphs,
    builds polygon-contour glyphs, writes valid .ttf files.

.PARAMETER WeightsFile
    Path to FontAiWeights.codex. Default: apps/fontai/build-output/FontAiWeights.codex

.PARAMETER OutDir
    Output directory for .ttf files. Default: apps/fontai/build-output/generated

.PARAMETER Count
    Number of fonts to generate (varying style). Default: 5

.PARAMETER Upem
    Units-per-em. Default: 1024
#>
[CmdletBinding()]
param(
    [string]$WeightsFile = "apps/fontai/build-output/FontAiWeights.codex",
    [string]$OutDir = "apps/fontai/build-output/generated",
    [int]$Count = 5,
    [int]$Upem = 1024
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $WeightsFile)) {
    Write-Error "Weights file not found: $WeightsFile`nRun 'pwsh apps/fontai/train.ps1' first."
    exit 1
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Parse-WeightArray($content, $name) {
    $pattern = "$name = \[([\s\S]*?)\]"
    if ($content -match $pattern) {
        $nums = $Matches[1] -replace '\s+', ' ' -replace '^\s+', '' -replace '\s+$', ''
        return @($nums -split '[,\s]+' | Where-Object { $_ -match '^-?\d+$' } | ForEach-Object { [int]$_ })
    }
    Write-Error "Could not find $name in weights file"; exit 1
}

function Parse-ConfigInt($content, $name, $default) {
    if ($content -match "$name.*?=\s*(\d+)") { return [int]$Matches[1] }
    return $default
}

Write-Host "[fontai-gen] Loading weights from $WeightsFile..."
$wContent = Get-Content -Raw $WeightsFile
$w1 = Parse-WeightArray $wContent "fai-w1-data"
$b1 = Parse-WeightArray $wContent "fai-b1-data"
$w2 = Parse-WeightArray $wContent "fai-w2-data"
$b2 = Parse-WeightArray $wContent "fai-b2-data"
$w3 = Parse-WeightArray $wContent "fai-w3-data"
$b3 = Parse-WeightArray $wContent "fai-b3-data"

$inputDim = Parse-ConfigInt $wContent "fai-input-dim" 101
$hidden1 = Parse-ConfigInt $wContent "fai-hidden1-dim" 128
$hidden2 = Parse-ConfigInt $wContent "fai-hidden2-dim" 64
$outputDim = Parse-ConfigInt $wContent "fai-output-dim" 53
$nPoints = Parse-ConfigInt $wContent "fai-n-points" 24
$nOuterPts = Parse-ConfigInt $wContent "fai-n-outer-pts" 32
$nInner1Pts = Parse-ConfigInt $wContent "fai-n-inner1-pts" 16
$nInner2Pts = Parse-ConfigInt $wContent "fai-n-inner2-pts" 8
$nInnerPts = $nInner1Pts  # backward compat
$hasBbox = Parse-ConfigInt $wContent "fai-has-bbox" 0

Write-Host "[fontai-gen] Model: $inputDim -> $hidden1 -> $hidden2 -> $outputDim (outer=$nOuterPts + inner1=$nInner1Pts + inner2=$nInner2Pts)"
Write-Host "[fontai-gen] W1=$($w1.Count) b1=$($b1.Count) W2=$($w2.Count) b2=$($b2.Count) W3=$($w3.Count) b3=$($b3.Count)"

$csharpCode = @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public class FontGenerator {
    int InputDim, H1, H2, OutputDim, NPoints, NOuterPts, NInnerPts, NInner1Pts, NInner2Pts;
    double[] W1f, b1f, W2f, b2f, W3f, b3f;

    [DllImport("gdi32.dll", CharSet=CharSet.Unicode)]
    static extern int AddFontResourceEx(string name, int fl, IntPtr res);
    [DllImport("gdi32.dll", CharSet=CharSet.Unicode)]
    static extern bool RemoveFontResourceEx(string name, int fl, IntPtr res);

    bool HasBbox;
    public FontGenerator(int[] w1, int[] b1, int[] w2, int[] b2, int[] w3, int[] b3,
                         int inputDim, int hidden1, int hidden2, int outputDim, int nPoints, int nOuterPts, int nInner1Pts, int nInner2Pts) {
        InputDim=inputDim; H1=hidden1; H2=hidden2; OutputDim=outputDim; NPoints=nPoints;
        NOuterPts=nOuterPts; NInner1Pts=nInner1Pts; NInner2Pts=nInner2Pts; NInnerPts=nInner1Pts; HasBbox=true;
        W1f=new double[w1.Length]; for(int i=0;i<w1.Length;i++) W1f[i]=w1[i]/1000.0;
        b1f=new double[b1.Length]; for(int i=0;i<b1.Length;i++) b1f[i]=b1[i]/1000.0;
        W2f=new double[w2.Length]; for(int i=0;i<w2.Length;i++) W2f[i]=w2[i]/1000.0;
        b2f=new double[b2.Length]; for(int i=0;i<b2.Length;i++) b2f[i]=b2[i]/1000.0;
        W3f=new double[w3.Length]; for(int i=0;i<w3.Length;i++) W3f[i]=w3[i]/1000.0;
        b3f=new double[b3.Length]; for(int i=0;i<b3.Length;i++) b3f[i]=b3[i]/1000.0;
    }

    double[] Forward(int glyphIdx, double weight, double italic, int cp) {
        double fU=(cp>=65&&cp<=90)?1:0, fL=(cp>=97&&cp<=122)?1:0;
        double fD=(cp>=48&&cp<=57)?1:0, fP=(fU==0&&fL==0&&fD==0&&cp!=32)?1:0;
        double fDesc=(cp==103||cp==106||cp==112||cp==113||cp==121)?1:0;
        double[] h1=new double[H1], h2=new double[H2], o=new double[OutputDim];
        for(int r=0;r<H1;r++){
            double v=b1f[r];
            if(glyphIdx>=0&&glyphIdx<95) v+=W1f[r*InputDim+glyphIdx];
            v+=W1f[r*InputDim+95]*weight+W1f[r*InputDim+96]*italic;
            v+=W1f[r*InputDim+97]*fU+W1f[r*InputDim+98]*fL+W1f[r*InputDim+99]*fD+W1f[r*InputDim+100]*fP+W1f[r*InputDim+101]*fDesc;
            h1[r]=v>0?v:0;
        }
        for(int r=0;r<H2;r++){
            double v=b2f[r]; for(int c=0;c<H1;c++) v+=W2f[r*H1+c]*h1[c]; h2[r]=v>0?v:0;
        }
        for(int r=0;r<OutputDim;r++){
            double s=b3f[r]; for(int c=0;c<H2;c++) s+=W3f[r*H2+c]*h2[c]; o[r]=s;
        }
        return o;
    }

    static int Clamp(int v,int lo,int hi){return v<lo?lo:v>hi?hi:v;}

    public bool Validate(string path) {
        int ret=AddFontResourceEx(path,0x10,IntPtr.Zero);
        if(ret>0){RemoveFontResourceEx(path,0x10,IntPtr.Zero);return true;}
        return false;
    }

    public void Generate(string path, string fontName, double weight, double italic, int upem) {
        int asc=upem*13/16, dsc=-(upem*3/16), gap=upem/8;
        int nGlyphs=97; // .notdef + space + 94 printable (33-126)

        // Infer all glyphs: each glyph has outer xs/ys + optional inner xs/ys
        short[][] glyphXs = new short[nGlyphs][];
        short[][] glyphYs = new short[nGlyphs][];
        int[] glyphOuterN = new int[nGlyphs]; // how many outer points (for contour split)
        ushort[] advances = new ushort[nGlyphs];

        // glyph 0: .notdef (rectangle with hole = 2 contours)
        int nd=upem/10;
        glyphXs[0]=new short[]{(short)nd,0,(short)(upem/2-nd),(short)(upem/2-nd),(short)nd,(short)nd,(short)(nd*2),(short)(upem/2-nd*2),(short)(upem/2-nd*2),(short)(nd*2)};
        glyphYs[0]=new short[]{0,(short)(asc*9/10),(short)(asc*9/10),0,0,(short)(nd),(short)(nd),(short)(asc*9/10-nd),(short)(nd),(short)(nd)};
        glyphOuterN[0]=5; advances[0]=(ushort)(upem/2);

        // glyph 1: space (empty)
        glyphXs[1]=null; glyphYs[1]=null;
        glyphOuterN[1]=0; advances[1]=(ushort)(upem/4);

        // glyphs 2-96: ASCII 33-126
        for(int cp=33;cp<=126;cp++){
            int gi=cp-33+2;
            double[] pred=Forward(cp-32, weight, italic, cp);
            int advRaw=(int)Math.Round(pred[0]*upem);
            advances[gi]=(ushort)Clamp(advRaw, upem/8, upem);
            if(!HasBbox){glyphXs[gi]=null;glyphYs[gi]=null;glyphOuterN[gi]=0;continue;}
            double bx=pred[1], by=pred[2], bw=pred[3], bh=pred[4];
            if(bw<0.01) bw=0.3; if(bh<0.01) bh=0.5;
            // Decode outer contour
            var oxs=new short[NOuterPts]; var oys=new short[NOuterPts];
            bool outerOk=false;
            for(int pi=0;pi<NOuterPts;pi++){
                double nx=pred[5+pi*2], ny=pred[5+pi*2+1];
                int px=Clamp((int)Math.Round((bx+nx*bw)*upem), 0, upem);
                int py=Clamp((int)Math.Round((by+ny*bh)*upem), dsc, asc);
                oxs[pi]=(short)px; oys[pi]=(short)py;
                if(px!=0||py!=0) outerOk=true;
            }
            if(!outerOk){glyphXs[gi]=null;glyphYs[gi]=null;glyphOuterN[gi]=0;continue;}
            // Check has_hole flag and decode inner contour
            int hBase=5+NOuterPts*2;
            double hasHoleVal=hBase<pred.Length?pred[hBase]:0;
            bool hasHole=hasHoleVal>0.5 && NInnerPts>0;
            if(hasHole){
                // Decode inner contour points (same bbox normalization as outer)
                var ixs=new short[NInnerPts]; var iys=new short[NInnerPts];
                bool innerOk=false;
                for(int pi=0;pi<NInnerPts;pi++){
                    double nx=pred[hBase+1+pi*2], ny=pred[hBase+1+pi*2+1];
                    int px=Clamp((int)Math.Round((bx+nx*bw)*upem),0,upem);
                    int py=Clamp((int)Math.Round((by+ny*bh)*upem),dsc,asc);
                    ixs[pi]=(short)px; iys[pi]=(short)py;
                    if(px!=0||py!=0) innerOk=true;
                }
                if(innerOk){
                    // Check for second hole (inner2)
                    int h2Base=hBase+1+NInner1Pts*2;
                    double hasHole2Val=h2Base<pred.Length?pred[h2Base]:0;
                    bool hasHole2=hasHole2Val>0.5 && NInner2Pts>0;
                    short[] i2xs=null, i2ys=null; bool inner2Ok=false;
                    if(hasHole2){
                        i2xs=new short[NInner2Pts]; i2ys=new short[NInner2Pts];
                        for(int pi=0;pi<NInner2Pts;pi++){
                            double nx=pred[h2Base+1+pi*2], ny=pred[h2Base+1+pi*2+1];
                            int px=Clamp((int)Math.Round((bx+nx*bw)*upem),0,upem);
                            int py=Clamp((int)Math.Round((by+ny*bh)*upem),dsc,asc);
                            i2xs[pi]=(short)px; i2ys[pi]=(short)py;
                            if(px!=0||py!=0) inner2Ok=true;
                        }
                    }
                    // Force outer CW
                    double outerArea=0;
                    for(int i=0;i<NOuterPts;i++){int ni=(i+1)%NOuterPts;outerArea+=(double)oxs[i]*oys[ni]-(double)oxs[ni]*oys[i];}
                    if(outerArea>0){Array.Reverse(oxs);Array.Reverse(oys);}
                    // Force inner1 CCW
                    double innerArea=0;
                    for(int i=0;i<NInner1Pts;i++){int ni=(i+1)%NInner1Pts;innerArea+=(double)ixs[i]*iys[ni]-(double)ixs[ni]*iys[i];}
                    if(innerArea<0){Array.Reverse(ixs);Array.Reverse(iys);}
                    int totalPts=NOuterPts+NInner1Pts+(inner2Ok?NInner2Pts:0);
                    short[] allX=new short[totalPts], allY=new short[totalPts];
                    for(int i=0;i<NOuterPts;i++){allX[i]=oxs[i];allY[i]=oys[i];}
                    for(int i=0;i<NInner1Pts;i++){allX[NOuterPts+i]=ixs[i];allY[NOuterPts+i]=iys[i];}
                    if(inner2Ok){
                        // Force inner2 CCW
                        double i2Area=0;
                        for(int i=0;i<NInner2Pts;i++){int ni=(i+1)%NInner2Pts;i2Area+=(double)i2xs[i]*i2ys[ni]-(double)i2xs[ni]*i2ys[i];}
                        if(i2Area<0){Array.Reverse(i2xs);Array.Reverse(i2ys);}
                        for(int i=0;i<NInner2Pts;i++){allX[NOuterPts+NInner1Pts+i]=i2xs[i];allY[NOuterPts+NInner1Pts+i]=i2ys[i];}
                    }
                    glyphXs[gi]=allX;glyphYs[gi]=allY;glyphOuterN[gi]=NOuterPts;
                } else {
                    glyphXs[gi]=oxs;glyphYs[gi]=oys;glyphOuterN[gi]=NOuterPts;
                }
            } else {
                glyphXs[gi]=oxs; glyphYs[gi]=oys; glyphOuterN[gi]=NOuterPts;
            }
        }

        // === Build tables ===
        // glyf + loca
        var glyfW=new MemoryStream(65536);
        uint[] locaOff=new uint[nGlyphs+1];
        for(int i=0;i<nGlyphs;i++){
            locaOff[i]=(uint)glyfW.Position;
            if(glyphXs[i]!=null) WriteGlyph(glyfW, glyphXs[i], glyphYs[i], glyphOuterN[i]);
            if(glyfW.Position%2!=0) glyfW.WriteByte(0);
        }
        locaOff[nGlyphs]=(uint)glyfW.Position;
        byte[] glyfData=glyfW.ToArray();

        var locaW=new MemoryStream();
        for(int i=0;i<=nGlyphs;i++) WU16(locaW,(ushort)(locaOff[i]/2));
        byte[] locaData=locaW.ToArray();

        // hmtx
        var hmtxW=new MemoryStream();
        for(int i=0;i<nGlyphs;i++){WU16(hmtxW,advances[i]);WI16(hmtxW,0);}
        byte[] hmtxData=hmtxW.ToArray();

        // cmap: map 32-126 to glyphs 1-96 (space=1, '!'=2, ..., '~'=96)
        int segCount=2;
        int f4len=14+2+segCount*8;
        var cmapW=new MemoryStream();
        WU16(cmapW,0);WU16(cmapW,1); // version, numTables
        WU16(cmapW,3);WU16(cmapW,1);WU32(cmapW,12); // platform 3 enc 1
        WU16(cmapW,4);WU16(cmapW,(ushort)f4len);WU16(cmapW,0); // format, length, language
        WU16(cmapW,(ushort)(segCount*2));WU16(cmapW,4);WU16(cmapW,1);WU16(cmapW,0); // search params
        WU16(cmapW,126);WU16(cmapW,0xFFFF); // endCode
        WU16(cmapW,0); // reservedPad
        WU16(cmapW,32);WU16(cmapW,0xFFFF); // startCode
        WI16(cmapW,(short)(1-32));WI16(cmapW,1); // idDelta (glyph = cp-32+1)
        WU16(cmapW,0);WU16(cmapW,0); // idRangeOffset
        byte[] cmapData=cmapW.ToArray();

        // head (54 bytes)
        byte[] headData=new byte[54]; int hp=0;
        P32(headData,ref hp,0x00010000); P32(headData,ref hp,0x00010000);
        P32(headData,ref hp,0); P32(headData,ref hp,0x5F0F3CF5);
        P16(headData,ref hp,0x000B); P16(headData,ref hp,(ushort)upem);
        hp+=16; // created+modified
        PI16(headData,ref hp,0);PI16(headData,ref hp,(short)dsc);PI16(headData,ref hp,(short)upem);PI16(headData,ref hp,(short)asc);
        P16(headData,ref hp,0);P16(headData,ref hp,8);PI16(headData,ref hp,2);PI16(headData,ref hp,0);PI16(headData,ref hp,0);

        // hhea (36 bytes)
        byte[] hheaData=new byte[36]; hp=0;
        P32(hheaData,ref hp,0x00010000);
        PI16(hheaData,ref hp,(short)asc);PI16(hheaData,ref hp,(short)dsc);PI16(hheaData,ref hp,(short)gap);
        P16(hheaData,ref hp,(ushort)upem);PI16(hheaData,ref hp,0);PI16(hheaData,ref hp,0);
        PI16(hheaData,ref hp,(short)upem);PI16(hheaData,ref hp,1);PI16(hheaData,ref hp,0);
        hp+=10; PI16(hheaData,ref hp,0); P16(hheaData,ref hp,(ushort)nGlyphs);

        // maxp (32 bytes)
        byte[] maxpData=new byte[32]; hp=0;
        P32(maxpData,ref hp,0x00010000); P16(maxpData,ref hp,(ushort)nGlyphs);
        P16(maxpData,ref hp,(ushort)(NPoints>10?NPoints:10)); P16(maxpData,ref hp,3);

        // post (32 bytes, format 3)
        byte[] postData=new byte[32]; hp=0; P32(postData,ref hp,0x00030000);

        // name
        string[] strs={fontName,fontName,"Regular",fontName+";1.0",fontName+" Regular","Version 1.0",fontName+"-Regular"};
        int[] nids={0,1,2,3,4,5,6};
        var nameW=new MemoryStream();
        int nRecs=strs.Length; int storOff=6+nRecs*12;
        WU16(nameW,0);WU16(nameW,(ushort)nRecs);WU16(nameW,(ushort)storOff);
        byte[][] sBytes=new byte[nRecs][];
        for(int i=0;i<nRecs;i++) sBytes[i]=Encoding.BigEndianUnicode.GetBytes(strs[i]);
        int sOff=0;
        for(int i=0;i<nRecs;i++){
            WU16(nameW,3);WU16(nameW,1);WU16(nameW,0x0409);WU16(nameW,(ushort)nids[i]);
            WU16(nameW,(ushort)sBytes[i].Length);WU16(nameW,(ushort)sOff);
            sOff+=sBytes[i].Length;
        }
        for(int i=0;i<nRecs;i++) nameW.Write(sBytes[i],0,sBytes[i].Length);
        byte[] nameData=nameW.ToArray();

        // OS/2 (96 bytes, v4)
        byte[] os2Data=new byte[96]; hp=0;
        P16(os2Data,ref hp,4);PI16(os2Data,ref hp,(short)(upem/2));P16(os2Data,ref hp,400);P16(os2Data,ref hp,5);
        P16(os2Data,ref hp,0); hp+=16; // fsType + sub/superscript
        hp+=4; // strikeout
        hp+=2; // sFamilyClass
        hp+=10; // panose
        hp+=16; // unicodeRange
        os2Data[hp++]=(byte)'C';os2Data[hp++]=(byte)'D';os2Data[hp++]=(byte)'X';os2Data[hp++]=(byte)' ';
        P16(os2Data,ref hp,0x0040);P16(os2Data,ref hp,32);P16(os2Data,ref hp,126);
        PI16(os2Data,ref hp,(short)asc);PI16(os2Data,ref hp,(short)dsc);PI16(os2Data,ref hp,(short)gap);
        P16(os2Data,ref hp,(ushort)asc);P16(os2Data,ref hp,(ushort)(-dsc));
        P32(os2Data,ref hp,1);P32(os2Data,ref hp,0);
        PI16(os2Data,ref hp,(short)(asc*7/10));PI16(os2Data,ref hp,(short)asc);
        P16(os2Data,ref hp,0);P16(os2Data,ref hp,32);P16(os2Data,ref hp,1);

        // === Assemble font ===
        string[] tags={"OS/2","cmap","glyf","head","hhea","hmtx","loca","maxp","name","post"};
        byte[][] tdata={os2Data,cmapData,glyfData,headData,hheaData,hmtxData,locaData,maxpData,nameData,postData};
        int nTables=tags.Length;
        int dirSize=12+nTables*16;
        int dataStart=(dirSize+3)&~3;
        int[] offsets=new int[nTables];
        int pos=dataStart;
        for(int i=0;i<nTables;i++){offsets[i]=pos;pos+=tdata[i].Length;pos=(pos+3)&~3;}
        byte[] file=new byte[pos]; int fp=0;
        P32(file,ref fp,0x00010000);P16(file,ref fp,(ushort)nTables);
        P16(file,ref fp,128);P16(file,ref fp,3);P16(file,ref fp,32);
        for(int i=0;i<nTables;i++){
            byte[] tag=Encoding.ASCII.GetBytes(tags[i].PadRight(4));
            Array.Copy(tag,0,file,fp,4);fp+=4;
            P32(file,ref fp,CalcChecksum(tdata[i]));
            P32(file,ref fp,(uint)offsets[i]);P32(file,ref fp,(uint)tdata[i].Length);
        }
        for(int i=0;i<nTables;i++) Array.Copy(tdata[i],0,file,offsets[i],tdata[i].Length);
        int headIdx=3;
        uint fsum=CalcChecksum(file); uint adj=0xB1B0AFBA-fsum;
        file[offsets[headIdx]+8]=(byte)(adj>>24);file[offsets[headIdx]+9]=(byte)(adj>>16);
        file[offsets[headIdx]+10]=(byte)(adj>>8);file[offsets[headIdx]+11]=(byte)adj;
        File.WriteAllBytes(path,file);
    }

    void WriteGlyph(MemoryStream s, short[] xs, short[] ys, int outerN) {
        int nPts=xs.Length;
        int nContours; ushort[] endPts;
        if(outerN>0 && outerN<nPts){
            int inner1End=outerN+NInner1Pts-1;
            if(nPts>outerN+NInner1Pts){
                nContours=3; endPts=new ushort[]{(ushort)(outerN-1),(ushort)inner1End,(ushort)(nPts-1)};
            } else {
                nContours=2; endPts=new ushort[]{(ushort)(outerN-1),(ushort)(nPts-1)};
            }
        } else {
            nContours=1; endPts=new ushort[]{(ushort)(nPts-1)};
        }

        short xmin=short.MaxValue,ymin=short.MaxValue,xmax=short.MinValue,ymax=short.MinValue;
        for(int i=0;i<nPts;i++){
            if(xs[i]<xmin)xmin=xs[i];if(xs[i]>xmax)xmax=xs[i];
            if(ys[i]<ymin)ymin=ys[i];if(ys[i]>ymax)ymax=ys[i];
        }
        WI16(s,(short)nContours);WI16(s,xmin);WI16(s,ymin);WI16(s,xmax);WI16(s,ymax);
        foreach(var ep in endPts) WU16(s,ep);
        WU16(s,0); // instructionLength
        short px=0,py=0;
        var flags=new MemoryStream();var xd=new MemoryStream();var yd=new MemoryStream();
        for(int i=0;i<nPts;i++){
            short dx=(short)(xs[i]-px),dy=(short)(ys[i]-py);px=xs[i];py=ys[i];
            byte f=1;
            if(dx==0){f|=0x10;}
            else if(dx>0&&dx<=255){f|=0x12;xd.WriteByte((byte)dx);}
            else if(dx<0&&dx>=-255){f|=0x02;xd.WriteByte((byte)(-dx));}
            else{byte[] db=new byte[2];int dp=0;PI16(db,ref dp,dx);xd.Write(db,0,2);}
            if(dy==0){f|=0x20;}
            else if(dy>0&&dy<=255){f|=0x24;yd.WriteByte((byte)dy);}
            else if(dy<0&&dy>=-255){f|=0x04;yd.WriteByte((byte)(-dy));}
            else{byte[] db=new byte[2];int dp=0;PI16(db,ref dp,dy);yd.Write(db,0,2);}
            flags.WriteByte(f);
        }
        byte[] fb=flags.ToArray(),xb=xd.ToArray(),yb=yd.ToArray();
        s.Write(fb,0,fb.Length);s.Write(xb,0,xb.Length);s.Write(yb,0,yb.Length);
    }

    static void WU16(MemoryStream s,ushort v){s.WriteByte((byte)(v>>8));s.WriteByte((byte)v);}
    static void WI16(MemoryStream s,short v){WU16(s,(ushort)v);}
    static void WU32(MemoryStream s,uint v){s.WriteByte((byte)(v>>24));s.WriteByte((byte)(v>>16));s.WriteByte((byte)(v>>8));s.WriteByte((byte)v);}
    static void P16(byte[] b,ref int p,ushort v){b[p++]=(byte)(v>>8);b[p++]=(byte)v;}
    static void PI16(byte[] b,ref int p,short v){P16(b,ref p,(ushort)v);}
    static void P32(byte[] b,ref int p,uint v){b[p++]=(byte)(v>>24);b[p++]=(byte)(v>>16);b[p++]=(byte)(v>>8);b[p++]=(byte)v;}
    static uint CalcChecksum(byte[] data){
        uint sum=0;int len=(data.Length+3)/4*4;
        for(int i=0;i<len;i+=4){uint v=0;for(int j=0;j<4;j++){v<<=8;if(i+j<data.Length)v|=data[i+j];}sum+=v;}
        return sum;
    }
}
"@

Add-Type -TypeDefinition $csharpCode -Language CSharp
$gen = [FontGenerator]::new($w1, $b1, $w2, $b2, $w3, $b3, $inputDim, $hidden1, $hidden2, $outputDim, $nPoints, $nOuterPts, $nInner1Pts, $nInner2Pts)

$variants = @(
    @{name="CodexAI-Regular"; weight=0.375; italic=0.0; style="Regular"},
    @{name="CodexAI-Bold"; weight=0.75; italic=0.0; style="Bold"},
    @{name="CodexAI-Italic"; weight=0.375; italic=1.0; style="Italic"},
    @{name="CodexAI-BoldItalic"; weight=0.75; italic=1.0; style="BoldItalic"},
    @{name="CodexAI-Light"; weight=0.125; italic=0.0; style="Light"}
)

Write-Host "[fontai-gen] Generating $($variants.Count) font family variants (UPM=$Upem)..."
foreach ($v in $variants) {
    $ttfPath = Join-Path $OutDir "$($v.name).ttf"
    $gen.Generate($ttfPath, $v.name, $v.weight, $v.italic, $Upem)
    $sz = (Get-Item $ttfPath).Length
    $valid = $gen.Validate($ttfPath)
    $tag = if ($valid) { "OK" } else { "INVALID" }
    Write-Host "  $($v.name).ttf ($sz bytes, $($v.style)) [$tag]"
}

Write-Host ""
Write-Host "[fontai-gen] Done. Generated $($variants.Count) fonts in $OutDir"
