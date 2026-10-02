[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle/tinygrad'),
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)

# Original tinygrad elf_loader and init_gsp_image run with synthetic ELF input.
# fetch_fw is replaced by a fixture-only function; no firmware is downloaded.
# Malformed-input refusals are the Codex selector's local contract.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
if (-not (Test-Path $Python)) { throw 'Oracle Python is unavailable' }
if (-not (Test-Path $TinygradDir)) { throw "Supply a tinygrad checkout at $commit" }
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'tinygrad commit differs from oracle pin' }
$files = @('helpers.py','runtime/support/c.py','runtime/support/elf.py','runtime/autogen/libc.py','runtime/autogen/nv.py','runtime/support/nv/ip.py')
& git -C $TinygradDir diff --exit-code $commit -- ($files | ForEach-Object {"tinygrad/$_"})
if ($LASTEXITCODE -ne 0) { throw 'tinygrad oracle inputs have local edits' }
$work = Join-Path $env:TEMP ('nv-fw-elf-oracle-' + (Split-Path (Split-Path $PSScriptRoot) -Leaf))
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
import sys, re, textwrap, struct, ctypes, array
sys.path.insert(0,sys.argv[1])
from tinygrad.runtime.support.elf import elf_loader
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import round_up

source=open(sys.argv[2],encoding='utf-8').read()
m=re.search(r'^(  def init_gsp_image\(.*?)(?=^  def |^class |\Z)',source,re.M|re.S)
if not m: raise SystemExit('init_gsp_image missing')
ns={'elf_loader':elf_loader,'nv':nv,'ctypes':ctypes,'array':array,'round_up':round_up}
exec(textwrap.dedent(m.group(1)),ns)
class Obj: pass
class View:
    def __init__(self,size,data=None): self.data=bytearray(size if data is None else data)
    def view(self,off,size=None,fmt='B'): return memoryview(self.data)[off:off+size if size is not None else None].cast(fmt)

def fixture(mode):
    b=bytearray(1536)
    ident=bytes([0x7f,69,76,70,2,1,1])+bytes(9)
    struct.pack_into('<16sHHIQQQIHHHHHH',b,0,ident,0,243,1,0,0,512,0,64,0,0,64,5,1)
    names=b'\0.shstrtab\0.fwimage\0.fwsignature_ga10x\0.fwsignature_ad10x\0'
    b[256:256+len(names)]=names
    def sh(i,name,kind,off,size):
        struct.pack_into('<IIQQQQIIQQ',b,512+i*64,names.index(name),kind,0,0,off,size,0,0,1,0)
    sh(1,b'.shstrtab',3,256,len(names))
    sh(2 if mode==0 else 4,b'.fwimage',1,1024+mode*128,13+mode*4)
    sh(3,b'.fwsignature_ga10x',1,1080,4)
    sh(4 if mode==0 else 2,b'.fwsignature_ad10x',1,1100+mode*180,7+mode*2)
    for i in range(13+mode*4): b[1024+mode*128+i]=10+mode+i
    for i in range(7+mode*2): b[1100+mode*180+i]=100+mode+i
    return bytes(b)

for mode in (0,1):
    blob=fixture(mode)
    _,sections,_=elf_loader(blob)
    image=next(s for s in sections if s.name=='.fwimage')
    sig=next(s for s in sections if s.name=='.fwsignature_ad10x')
    def fetch(path,name,sha):
        assert name=='gsp-570.144.bin'
        return blob
    ns['fetch_fw']=fetch
    captured=[]
    def alloc(size,data=None):
        v=View(size,data);captured.append(v)
        return v,0,[0x100000000+i*4096 for i in range((size+4095)//4096)]
    g=Obj();g.nvdev=Obj();g.nvdev.chip_name='AD106';g.nvdev._alloc_boot_mem=alloc
    ns['init_gsp_image'](g)
    assert g.gsp_image==image.content and captured[-1].data==sig.content
    print('ELF',mode,image.header.sh_offset,len(image.content),sig.header.sh_offset,len(sig.content),sum(image.content),sum(sig.content))
for case in range(2,9): print('ELF',case,'REFUSED')
'@.Replace("`r",''), [Text.UTF8Encoding]::new($false))
$out = & $Python $script $pkg $ip
if ($LASTEXITCODE -ne 0) { throw 'ELF oracle execution failed' }
[Console]::Out.Write(($out -join "`n") + "`n")
