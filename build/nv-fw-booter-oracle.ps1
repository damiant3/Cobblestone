[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle/tinygrad'),
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)

# Original tinygrad r570 bootloader/booter methods, synthetic file provider
# and capture-only allocator. No firmware downloads or real firmware copies.
# Refusal lines grade local bounds contracts.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
if (-not (Test-Path $Python)) { throw 'Oracle Python is unavailable' }
if (-not (Test-Path $TinygradDir)) { throw "Supply a tinygrad checkout at $commit" }
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'tinygrad commit differs from oracle pin' }
$files = @('helpers.py','runtime/support/c.py','runtime/autogen/nv.py','runtime/support/nv/ip.py')
& git -C $TinygradDir diff --exit-code $commit -- ($files | ForEach-Object {"tinygrad/$_"})
if ($LASTEXITCODE -ne 0) { throw 'tinygrad oracle inputs have local edits' }
$work = Join-Path $env:TEMP ('nv-fw-booter-oracle-' + (Split-Path (Split-Path $PSScriptRoot) -Leaf))
$pkg = Join-Path $work 'pkg'
foreach ($dir in 'tinygrad','tinygrad/runtime','tinygrad/runtime/support','tinygrad/runtime/autogen') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $dir) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$dir/__init__.py"), '')
}
foreach ($file in $files | Where-Object {$_ -ne 'runtime/support/nv/ip.py'}) {
    Copy-Item -LiteralPath (Join-Path $TinygradDir "tinygrad/$file") -Destination (Join-Path $pkg "tinygrad/$file") -Force
}
$ip = Join-Path $TinygradDir 'tinygrad/runtime/support/nv/ip.py'
$script = Join-Path $work 'oracle.py'
[IO.File]::WriteAllText($script, @'
import sys, re, textwrap, struct, ctypes
sys.path.insert(0,sys.argv[1])
from tinygrad.runtime.autogen import nv
source=open(sys.argv[2],encoding='utf-8').read()
ns={'nv':nv,'ctypes':ctypes,'struct':struct}
for name in ('init_boot_binary_image','prep_booter'):
    m=re.search(r'^(  def '+name+r'\(.*?)(?=^  def |^class |\Z)',source,re.M|re.S)
    if not m: raise SystemExit(name+' missing')
    exec(textwrap.dedent(m.group(1)),ns)
class Obj: pass
def header(b,desc,image,size): struct.pack_into('<6I',b,0,0x10de,1,len(b),desc,image,size)
def pattern(b,off,size,base): b[off:off+size]=bytes((base+i)&255 for i in range(size))
def riscv(n):
    b=bytearray(768);desc=48+n*48;image=256+n*128;size=128+n*64
    header(b,desc,image,size)
    d=nv.RM_RISCV_UCODE_DESC(version=1,monitorCodeOffset=8+n*8,monitorCodeSize=24+n*8,
        monitorDataOffset=40+n*24,monitorDataSize=16+n*8,manifestOffset=80+n*48,manifestSize=32+n*16,bIsMonitorEnabled=1)
    b[desc:desc+ctypes.sizeof(d)]=bytes(d);pattern(b,image,size,20+n)
    return bytes(b)
def booter(n):
    b=bytearray(768);header(b,32,256+n*64,96+n*32)
    hs=nv.struct_nvfw_hs_header_v2(sig_prod_offset=160+n*32,sig_prod_size=32+n*16,patch_loc=80,patch_sig=84,
        num_sig=88,header_offset=112,header_size=36)
    b[32:32+ctypes.sizeof(hs)]=bytes(hs)
    struct.pack_into('<3I',b,80,24+n*56,16+n*8,2+n*2)
    lh=nv.struct_nvfw_hs_load_header_v2(os_data_offset=64+n*32,os_data_size=16,num_apps=1)
    app=nv.struct_nvfw_hs_load_header_v2_app(offset=4+n*12,size=24+n*16)
    b[112:132]=bytes(lh);b[132:148]=bytes(app)
    pattern(b,160+n*32,32+n*16,128+n);pattern(b,256+n*64,96+n*32,1+n)
    return bytes(b)
def run(name,blob,filename):
    captured=[];local={}
    def fetch(path,nm,sha):
        assert nm==filename
        return blob
    def alloc(size,data=None,**kw):
        assert data is not None and len(data)==size
        captured.append(bytes(data));return None,0x100000,[0x100000]
    ns['fetch_fw']=fetch
    g=Obj();g.nvdev=Obj();g.nvdev.fw_name='ad102';g.nvdev._alloc_boot_mem=alloc
    fn=ns[name]
    def trace(frame,event,arg):
        if event=='return' and frame.f_code is fn.__code__: local.update(frame.f_locals)
    sys.setprofile(trace)
    try: fn(g)
    finally: sys.setprofile(None)
    return g,local,captured[0]
for n in (0,1):
    g,v,image=run('init_boot_binary_image',riscv(n),'bootloader-570.144.bin')
    d=g.booter_desc;h=v['h']
    print('RISCV',n,h.data_offset,len(image),h.header_offset,d.monitorCodeOffset,d.monitorCodeSize,
          d.monitorDataOffset,d.monitorDataSize,d.manifestOffset,d.manifestSize,sum(image))
for n in (0,1):
    g,v,image=run('prep_booter',booter(n),'booter_load-570.144.bin')
    print('BOOTER',n,v['h'].data_offset,len(image),v['sig_off'],v['sig_len'],v['patch_loc'],
          g.booter_code_off,g.booter_code_sz,g.booter_data_off,g.booter_data_sz)
    print('PATCH',n,len(image),image.hex())
    h=v['h'];print('SOURCE',n,sum(v['b'][h.data_offset:h.data_offset+h.data_size]))
for n in range(2,9): print('BOOTER',n,'REFUSED')
for n in range(2,5): print('RISCV',n,'REFUSED')
'@.Replace("`r",''), [Text.UTF8Encoding]::new($false))
$out = & $Python $script $pkg $ip
if ($LASTEXITCODE -ne 0) { throw 'Booter oracle execution failed' }
[Console]::Out.Write(($out -join "`n") + "`n")
