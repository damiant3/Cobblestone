[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle/tinygrad'),
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)

# Synthetic VBIOS only. Original tinygrad prep_ucode grades lookup and v3.
# For v2, tinygrad stops after common lookup; original nouveau fwsec_v2
# supplies the versioned field interpretation and stops before firmware I/O.
# Refusal fixtures express local bounds/domain contracts.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
if (-not (Test-Path $Python)) { throw 'Oracle Python is unavailable' }
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'tinygrad commit differs from pin' }
$files = @('helpers.py','runtime/support/c.py','runtime/autogen/nv.py','runtime/support/nv/ip.py')
& git -C $TinygradDir diff --exit-code $commit -- ($files | ForEach-Object {"tinygrad/$_"})
if ($LASTEXITCODE -ne 0) { throw 'tinygrad oracle inputs have local edits' }
$work = Join-Path $env:TEMP ('nv-fwsec-oracle-' + (Split-Path (Split-Path $PSScriptRoot) -Leaf))
$pkg = Join-Path $work 'pkg'
foreach ($dir in 'tinygrad','tinygrad/runtime','tinygrad/runtime/support','tinygrad/runtime/autogen') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $dir) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$dir/__init__.py"), '')
}
foreach ($file in $files | Where-Object {$_ -ne 'runtime/support/nv/ip.py'}) {
    Copy-Item -LiteralPath (Join-Path $TinygradDir "tinygrad/$file") -Destination (Join-Path $pkg "tinygrad/$file") -Force
}
$base = 'https://raw.githubusercontent.com/torvalds/linux/v6.12/drivers/gpu/drm/nouveau'
foreach ($ref in @(
    @('fwsec.c','nvkm/subdev/gsp/fwsec.c','982B707407A925B09CECC0BC1FCE71D257078AF8D30952559316EC0669F26420'),
    @('fw.h','include/nvfw/fw.h','29524E3B1586A674924D5019A2F694B5EB47737032E75C91B4BA2C443CF5E27A')
)) {
    $path = Join-Path $work $ref[0]
    if (-not (Test-Path $path)) { Invoke-WebRequest "$base/$($ref[1])" -OutFile $path }
    if ((Get-FileHash $path).Hash -ne $ref[2]) { throw "Reference hash differs: $path" }
}
function Extract([string]$File, [string]$Pattern) {
    $text = [IO.File]::ReadAllText((Join-Path $work $File))
    $m = [regex]::Match($text,$Pattern,'Multiline,Singleline')
    if (-not $m.Success) { throw "Cannot extract $Pattern from $File" }
    return $m.Value
}
$source = [IO.File]::ReadAllText((Join-Path $work 'fwsec.c'))
$license = $source.Substring(0,$source.IndexOf('*/')+2)
$desc = Extract 'fwsec.c' '^union nvfw_falcon_ucode_desc \{.*?^\};'
$v2 = Extract 'fwsec.c' '^static int\r?\nnvkm_gsp_fwsec_v2\(.*?^\}'
$bin = Extract 'fw.h' '^struct nvfw_bin_hdr \{.*?^\};'
$bl = Extract 'fw.h' '^struct nvfw_bl_desc \{.*?^\};'
$c = @'
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdbool.h>
typedef uint8_t u8; typedef uint16_t u16; typedef uint32_t u32; typedef uint64_t u64;
struct nvkm_device { int unused; };
struct nvkm_subdev { struct nvkm_device *device; };
struct nvkm_falcon { int unused; };
struct nvkm_gsp { struct nvkm_subdev subdev; struct { void *fwsec; } *func; struct nvkm_falcon falcon; };
struct nvkm_falcon_fw { const u8 *image; u32 image_size; u32 nmem_base_img,nmem_base,nmem_size,imem_base_img,imem_base,imem_size,dmem_base_img,dmem_base,dmem_size,boot_addr,boot_size; void *boot; };
struct firmware { const u8 *data; };
#define WARN_ON(x) (x)
#define GFP_KERNEL 0
#define ENOMEM 12
static int nvkm_falcon_fw_ctor(void *func,const char *name,struct nvkm_device *dev,bool priv,const u8 *image,u32 size,struct nvkm_falcon *falcon,struct nvkm_falcon_fw *fw) { fw->image=image;fw->image_size=size;return 0; }
static int nvkm_firmware_get(struct nvkm_subdev *s,const char *name,int ver,const struct firmware **fw) { return -123; }
static void nvkm_firmware_put(const struct firmware *fw) { abort(); }
static void *kmemdup(const void *p,size_t size,int flags) { abort();return NULL; }
static int nvkm_gsp_fwsec_patch(struct nvkm_gsp *g,struct nvkm_falcon_fw *fw,u32 off,u32 cmd) { abort();return -1; }
'@
$accessors = @'
static const struct nvfw_bin_hdr *nvfw_bin_hdr(struct nvkm_subdev *s,const void *p) { abort();return p; }
static const struct nvfw_bl_desc *nvfw_bl_desc(struct nvkm_subdev *s,const void *p) { abort();return p; }
'@
$main = @'
int main(int argc,char **argv) {
    if(argc!=3) return 2;
    FILE *f=fopen(argv[1],"rb");if(!f) return 2;
    fseek(f,0,SEEK_END);long bytes=ftell(f);rewind(f);
    if(bytes<0) abort();u8 *b=malloc((size_t)bytes);if(!b) abort();
    if(fread(b,1,(size_t)bytes,f)!=(size_t)bytes) abort();fclose(f);
    unsigned off=(unsigned)strtoul(argv[2],NULL,10);
    if((u64)off+sizeof(struct nvkm_falcon_ucode_desc_v2)>(u64)bytes) abort();
    const struct nvkm_falcon_ucode_desc_v2 *d=(const void *)(b+off);
    u32 size=d->Hdr>>16;if(((d->Hdr>>8)&255)!=2 || (u64)off+size+d->IMEMLoadSize+d->DMEMLoadSize>(u64)bytes) abort();
    struct nvkm_device device={0};struct nvkm_gsp g={0};struct {void *fwsec;} funcs={0};
    g.subdev.device=&device;g.func=(void *)&funcs;
    struct nvkm_falcon_fw fw={0};
    if(nvkm_gsp_fwsec_v2(&g,"synthetic",d,size,21,&fw)!=-123) abort();
    printf("2 %u %u %u %u %u %u %u %u %u %u %u %u %u %u\n",off,size,(u32)(fw.image-b),fw.image_size,
        fw.nmem_base,fw.nmem_size+fw.imem_size,d->IMEMVirtBase,fw.dmem_base_img,fw.dmem_base,fw.dmem_size,
        d->InterfaceOffset,fw.nmem_size,fw.imem_base,fw.imem_size);
    free(b);return 0;
}
'@
[IO.File]::WriteAllText((Join-Path $work 'v2.c'),($license+"`n"+$c+"`n"+$desc+"`n"+$bin+"`n"+$bl+"`n"+$accessors+"`n"+$v2+"`n"+$main).Replace("`r",''),[Text.UTF8Encoding]::new($false))
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$exe=Join-Path $work 'v2.exe'
if(Test-Path $exe){[IO.File]::Delete($exe)}
$log=& cmd /c "call `"$vcvars`" >nul && cd /d `"$work`" && cl /nologo /W3 /O2 v2.c /Fe:v2.exe" 2>&1
if($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)){$log|ForEach-Object{Write-Host $_};throw 'v2 reference compile failed'}
$script=Join-Path $work 'oracle.py'
[IO.File]::WriteAllText($script,@'
import sys,re,textwrap,ctypes,array,struct,pathlib,subprocess
sys.path.insert(0,sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import round_up
source=open(sys.argv[2],encoding='utf-8').read()
m=re.search(r'^(  def prep_ucode\(.*?)(?=^  def |^class |\Z)',source,re.M|re.S)
if not m: raise SystemExit('prep_ucode missing')
ns={'nv':nv,'ctypes':ctypes,'array':array,'round_up':round_up}
exec(textwrap.dedent(m.group(1)),ns)
def put(b,off,value,n=4): b[off:off+n]=value.to_bytes(n,'little')
def pattern(b,off,size,base): b[off:off+size]=bytes((base+i)&255 for i in range(size))
def fixture(version,n):
    b=bytearray(8192)
    def rom(off,blocks,kind):
        put(b,off,0xaa55,2);put(b,off+24,32,2);put(b,off+32,0x52494350);put(b,off+48,blocks,2);put(b,off+52,kind,1)
    if n==0: rom(0,4,0)
    else: rom(0,1,3);rom(512,3,0)
    rom(2048,12 if n==0 else 0,nv.NV_BCRT_HASH_INFO_BASE_CODE_TYPE_VBIOS_EXT)
    stride=8 if n==0 else 6
    hdr=nv.BIT_HEADER_V1_00(Signature=0x00544942,HeaderSize=12,TokenSize=stride,TokenEntries=2)
    b[0x1b0:0x1bc]=bytes(hdr)
    b[444:452]=bytes(nv.BIT_TOKEN_V1_00(TokenId=0x70,DataVersion=1,DataSize=4))
    b[444+stride:452+stride]=bytes(nv.BIT_TOKEN_V1_00(TokenId=0x70,DataVersion=2,DataSize=4,DataPtr=0x12340300))
    put(b,0x300,0x900);base=n*512;table=base+0x900;off=base+0x1000
    th=nv.FALCON_UCODE_TABLE_HDR_V1(Version=1,HeaderSize=6,EntrySize=6,EntryCount=2,DescVersion=version)
    b[table:table+6]=bytes(th)
    b[table+6:table+12]=bytes(nv.FALCON_UCODE_TABLE_ENTRY_V1(ApplicationID=0x84 if n==0 else 0x85,DescPtr=0xfffffff0 if n==0 else 0x700))
    if n==1: put(b,base+0x700,0x40001|(version<<8))
    b[table+12:table+18]=bytes(nv.FALCON_UCODE_TABLE_ENTRY_V1(ApplicationID=0x85,DescPtr=0x1000))
    length=60 if version==2 else 44+384*(n+1);imem=512+n*256;dmem=256 if version==2 else 768
    stored=imem+dmem+(0 if version==2 else 13);h=(length<<16)|(version<<8)|1
    if version==2:
        struct.pack_into('<15I',b,off,h,stored,stored,0,16,0x1000+n*256,imem,0x8000+n*256,0x1100+n*256,128+n*128,imem,0x4000+n*256,dmem,0,0)
    else:
        d=nv.FALCON_UCODE_DESC_V3(Hdr=nv.FALCON_UCODE_DESC_HEADER(vDesc=h),StoredSize=stored,PKCDataOffset=384,InterfaceOffset=16,
            IMEMPhysBase=0x2000+n*256,IMEMLoadSize=imem,IMEMVirtBase=0x9000+n*256,DMEMPhysBase=0x5000+n*256,DMEMLoadSize=dmem,
            EngineIdMask=0x1234+n,UcodeId=7+n,SignatureCount=1+n,SignatureVersions=5+n)
        b[off:off+44]=bytes(d);pattern(b,off+44,length-44,160+n)
    size=stored if version==2 else round_up(stored,256);image=off+length;pattern(b,image,size,20+version+n)
    if version==3:
        b[image+imem+16:image+imem+20]=bytes(nv.FALCON_APPLICATION_INTERFACE_HEADER_V1(version=1,headerSize=4,entrySize=8,entryCount=1))
        b[image+imem+20:image+imem+28]=bytes(nv.FALCON_APPLICATION_INTERFACE_ENTRY_V1(id=4,dmemOffset=32))
        b[image+imem+32:image+imem+96]=bytes(nv.FALCON_APPLICATION_INTERFACE_DMEM_MAPPER_V3(version=3,size=64,cmd_in_buffer_offset=96,cmd_in_buffer_size=48))
    return bytes(b)
class StopV2(Exception): pass
class MMIO:
    def __init__(self,b): self.b=b
    def __getitem__(self,key):
        assert key.start==0x300000//4 and key.stop==0x400000//4
        return memoryview(self.b).cast('I')
class Dev:
    def __init__(self,b,v): self.mmio=MMIO(b);self.version=v;self.captured=[]
    @property
    def vram_size(self):
        if self.version==2: raise StopV2()
        return 0x400000000
    def _alloc_boot_mem(self,size,data,sysmem):
        assert len(data)==size and sysmem is False
        self.captured.append(bytes(data));return None,0x100000,[]
class Obj: pass
for version in (2,3):
    for n in (0,1):
        b=fixture(version,n);g=Obj();g.nvdev=Dev(b,version);local={};fn=ns['prep_ucode']
        def trace(frame,event,arg):
            if event=='return' and frame.f_code is fn.__code__: local.update(frame.f_locals)
        sys.setprofile(trace)
        try: fn(g)
        except StopV2: pass
        finally: sys.setprofile(None)
        off=local['ucode_desc_off'];length=local['ucode_desc_size']
        if version==2:
            path=pathlib.Path(sys.argv[3]).parent/f'synthetic-v2-{n}.bin';path.write_bytes(b)
            vals=list(map(int,subprocess.check_output([sys.argv[3],str(path),str(off)],text=True).split()))+[0]*7
            image=b[vals[3]:vals[3]+vals[4]];sig=b''
        else:
            d=g.desc_v3;image=bytes(local['image']);sig=bytes(local['signature'][-384:])
            assert g.nvdev.captured[0][d.IMEMLoadSize+d.PKCDataOffset:d.IMEMLoadSize+d.PKCDataOffset+384]==sig
            vals=[3,off,length,off+length,len(image),d.IMEMPhysBase,d.IMEMLoadSize,d.IMEMVirtBase,d.IMEMLoadSize,d.DMEMPhysBase,d.DMEMLoadSize,
                d.InterfaceOffset,0,0,0,off+44+len(local['signature'])-384,384,d.PKCDataOffset,d.EngineIdMask,d.UcodeId,d.SignatureCount,d.SignatureVersions]
        print(f'V{version}{"AB"[n]}',*vals,sum(image),sum(sig))
for tag in ('SHORT','ROMZERO','BIT','TOKEN','PTR','VERSION','SECURE','SIGCOUNT','PKC','TOKENS'): print(tag,'REFUSED')
'@.Replace("`r",''),[Text.UTF8Encoding]::new($false))
$ip=Join-Path $TinygradDir 'tinygrad/runtime/support/nv/ip.py'
$out=& $Python $script $pkg $ip $exe
if($LASTEXITCODE -ne 0){throw 'FWSEC oracle execution failed'}
[Console]::Out.Write(($out -join "`n")+"`n")
