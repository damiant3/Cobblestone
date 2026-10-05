[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle/tinygrad'),
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)

# Original NVDev init order, NV_FLCN hardware methods, NVReg encodings and
# GSP boot RPC methods. PCI discovery and boot-memory preparation are supplied
# inputs. Queue transport logs complete RPC payloads and supplies INIT_DONE.
# Stop at the call to golden-context creation. No hardware or firmware access.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'tinygrad commit differs from pin' }
$regs = @('dev_gsp','dev_sec_pri','dev_falcon_v4','dev_falcon_second_pri','dev_fbif_v4','dev_riscv_pri','dev_fb','dev_bus')
$files = @('helpers.py','runtime/support/c.py','runtime/autogen/nv.py','runtime/autogen/nv_570.py','runtime/autogen/pci.py','runtime/support/nv/ip.py','runtime/support/nv/nvdev.py') + @($regs | ForEach-Object {"runtime/autogen/nv_regs/$_.py"})
& git -C $TinygradDir diff --exit-code $commit -- ($files | ForEach-Object {"tinygrad/$_"})
if ($LASTEXITCODE -ne 0) { throw 'tinygrad oracle inputs have local edits' }
$work = Join-Path $env:TEMP ('nv-gsp-boot-oracle-' + (Split-Path (Split-Path $PSScriptRoot) -Leaf))
$pkg = Join-Path $work 'pkg'
foreach ($dir in 'tinygrad','tinygrad/runtime','tinygrad/runtime/support','tinygrad/runtime/autogen','tinygrad/runtime/autogen/nv_regs') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $dir) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$dir/__init__.py"), '')
}
foreach ($file in $files | Where-Object {$_ -notlike 'runtime/support/nv/*'}) {
    Copy-Item -LiteralPath (Join-Path $TinygradDir "tinygrad/$file") -Destination (Join-Path $pkg "tinygrad/$file") -Force
}
$script = Join-Path $work 'oracle.py'
[IO.File]::WriteAllText($script,@'
from __future__ import annotations
import sys,re,ctypes,textwrap,functools,itertools,importlib
sys.path.insert(0,sys.argv[1])
import tinygrad
from tinygrad.runtime.autogen import nv,nv_570 as nv_gpu,pci
from tinygrad.helpers import getbits,lo32,hi32
for name in ('dev_gsp','dev_sec_pri','dev_falcon_v4','dev_falcon_second_pri','dev_fbif_v4','dev_riscv_pri','dev_fb','dev_bus'):
    importlib.import_module('tinygrad.runtime.autogen.nv_regs.'+name)
ip=open(sys.argv[2],encoding='utf-8').read()
devsrc=open(sys.argv[3],encoding='utf-8').read()
helpers=open(sys.argv[4],encoding='utf-8').read()
log=[]
class Obj: pass
class Ready(Exception): pass
class Clock:
    tick=0
    case=-1
    @staticmethod
    def sleep(seconds):
        log.append('D %d'%round(seconds*1000000))
        if Clock.case==13: raise RuntimeError('backend failure')
    @classmethod
    def perf_counter(cls): v=cls.tick;cls.tick+=1;return float(v)
class View:
    def view(self,*args,**kwargs): return self
class Queue:
    def __init__(self,g,*args): self.g=g;self.tx=Obj();self.tx.rxHdrOff=ctypes.sizeof(nv.msgqTxHeader)
    def send_rpc(self,func,data):
        log.append('RPC %d %s'%(func,bytes(data).hex()))
        if self.g.nvdev.case==12: raise RuntimeError('backend failure')
    def wait_resp(self,event):
        log.append('WAIT %d'%event)
        if self.g.nvdev.case==7: raise TimeoutError('init event')
        return b''
ns={'tinygrad':tinygrad,'ctypes':ctypes,'functools':functools,'itertools':itertools,'getbits':getbits,'lo32':lo32,'hi32':hi32,
    'nv':nv,'nv_gpu':nv_gpu,'pci':pci,'time':Clock,'NVRpcQueue':Queue}
def extract(source,pattern):
    m=re.search(pattern,source,re.M|re.S)
    if not m: raise RuntimeError('missing original '+pattern)
    return m.group(0)
exec('from __future__ import annotations\n'+extract(helpers,r'^def wait_cond\(.*?(?=^def |^# \*\*\*)'),ns)
for name in ('NV_IP','NV_FLCN','NV_GSP'):
    exec('from __future__ import annotations\n'+extract(ip,r'^class '+name+r'\b.*?(?=^class |\Z)'),ns)
for name in ('NVReg','NVDev'):
    exec('from __future__ import annotations\n'+extract(devsrc,r'^class '+name+r'\b.*?(?=^class |\Z)'),ns)
class PCI:
    def __init__(self,case): self.case=case;self.pcibus='0000:02:03.1' if case==1 else '0000:01:00.0'
    def map_bar(self,*args,**kw): return None
    def bar_info(self,index):
        n=int(self.case==1)
        return {0:(0xC0000000+n*0x1000000,0),1:(0x1000000000+n*0x2000000000,0),3:(0xF0000000+n*0x100000,0)}[index]
    def read_config(self,off,size):
        n=int(self.case==1)
        return {0:0x28A010DE+n,0x2C:0x12341043+n,8:1+n}[off]
class Device(ns['NVDev']):
    def _early_ip_init(self):
        self.case=self.pci_dev.case;self.reads={};self.chip_id=0x190600A1+int(self.case==1)
        self.chip_name='AD106';self.fw_name='ad102';self.fmc_boot=False
        for name,arch in (('dev_fb','tu102'),('dev_bus','tu102')): self.include(name,arch)
        self.flcn=ns['NV_FLCN'](self);self.gsp=ns['NV_GSP'](self)
        n=int(self.case==1);d=Obj()
        d.IMEMLoadSize=512+n*256;d.IMEMPhysBase=0x1000+n*0x1000;d.IMEMVirtBase=0x2000+n*0x1000
        d.DMEMPhysBase=0x200+n*0x200;d.DMEMLoadSize=768+n*256;d.PKCDataOffset=384+n*128
        d.EngineIdMask=0x1234+n;d.UcodeId=7+n
        self.flcn.desc_v3=d;self.flcn.frts_image_paddr=0x1000000+n*0x2EF000000
        self.flcn.booter_image_paddr=0x2000000+n*0x1000000
        self.flcn.booter_code_off=0x100+n*0x100;self.flcn.booter_code_sz=512+n
        self.flcn.booter_data_off=0x500+n*0x100;self.flcn.booter_data_sz=256+n
        self.flcn.prep_ucode=lambda:None;self.flcn.prep_booter=lambda:None
        self.gsp.libos_args_sysmem=0x123456789000+n*0x111000
        self.gsp.wpr_meta_sysmem=0x23456789A000+n*0x222000
        def queues():
            self.gsp.cmd_q_view=View();self.gsp.stat_q_view=View();self.gsp.cmd_q=Queue(self.gsp)
        self.gsp.init_rm_args=queues;self.gsp.init_libos_args=lambda:None;self.gsp.init_wpr_meta=lambda:None
        def ready(): raise Ready()
        self.gsp.init_golden_image=ready
    def _early_mmu_init(self): pass
    def wreg(self,addr,value):
        assert 0<=value<=0xffffffff
        log.append('W %08x %08x'%(addr,value))
        if self.case==11 and addr==0x1103c0 and value==1: raise RuntimeError('backend failure')
    def rreg(self,addr):
        n=int(self.case==1)
        if addr in (0x1100f4,0x8400f4):
            value=0x80000400
            if n and addr==0x8400f4: value=0x80000000
            if self.case==6 and addr==0x1100f4: value|=0x1000
        elif addr in (0x111668,0x841668): value=0 if self.case==9 and addr==0x111668 else 1
        elif addr in (0x110624,0x840624): value=0xA5000055+n*0x1000
        elif addr in (0x110600,0x840600): value=0x12340003+n*0x1000
        elif addr in (0x110118,0x840118):
            value=2
            if self.case==5 and addr==0x110118: value=3
            if self.case==8 and addr==0x110118 and self.reads.get(addr,0)==0: value=3
        elif addr in (0x110100,0x840100):
            value=0x2010 if n else 0x2050
            if self.case==10 and addr==0x110100: value &= ~0x10
        elif addr==0x1FA828: value=0 if self.case==2 else 0x40000
        elif addr==0x840040: value=7 if self.case==3 else 0
        elif addr==0x840044: value=0xA5
        elif addr==0x111388: value=0 if self.case==4 else 0x80
        else: raise RuntimeError('unexpected read %x'%addr)
        self.reads[addr]=self.reads.get(addr,0)+1
        log.append('R %08x %08x'%(addr,value));return value
for case in range(14):
    log.clear();Clock.tick=0;Clock.case=case;result=0
    try: Device(PCI(case))
    except Ready: pass
    except TimeoutError as e: result=5 if str(e)=='init event' else 1
    except RuntimeError as e:
        if str(e)=='backend failure': result=1
        else: raise
    except AssertionError as e:
        message=str(e)
        if message=='WPR2 is not initialized': result=2
        elif message.startswith('Booter failed to execute'): result=3
        elif message=='GSP Core is not active': result=4
        else: raise
    else: raise RuntimeError('golden-context boundary was not reached')
    print('CASE',case)
    for line in log: print(line)
    print('RESULT',case,result)
'@.Replace("`r",''),[Text.UTF8Encoding]::new($false))
$src=Join-Path $TinygradDir 'tinygrad'
$out=& $Python $script $pkg (Join-Path $src 'runtime/support/nv/ip.py') (Join-Path $src 'runtime/support/nv/nvdev.py') (Join-Path $src 'helpers.py')
if($LASTEXITCODE -ne 0){throw 'Boot-chain oracle failed'}
[Console]::Out.Write(($out -join "`n")+"`n")
