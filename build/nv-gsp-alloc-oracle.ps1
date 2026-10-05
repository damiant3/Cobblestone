[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-alloc: tinygrad's own rpc_rm_alloc
# (tinygrad/runtime/support/nv/ip.py), taken from the source verbatim and run
# by Python over its r570 structures, against a stub device whose handle
# generator yields the subject's object handle and whose command queue captures
# the function number and payload. init_golden_image supplies the root,
# device, subdevice and VA-space parameters. Channel cases continue through
# its channel allocation using a stub allocator, stopping before GR context setup.
# Only helpers.py, runtime/support/c.py, autogen/nv.py and autogen/nv_570.py are
# loaded, beside empty package files.
#
#   pwsh build/nv-gsp-alloc-oracle.ps1 > codex/test/nv-gsp-alloc.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
if (-not (Test-Path -PathType Leaf $Python)) { Write-Host "MISSING: $Python"; exit 2 }
if (-not (Test-Path -PathType Container $TinygradDir)) {
    & git clone --quiet --filter=blob:none --sparse https://github.com/tinygrad/tinygrad.git $TinygradDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'tinygrad clone failed'; exit 2 }
    & git -C $TinygradDir sparse-checkout set tinygrad/runtime/autogen tinygrad/runtime/support 2>&1 | Out-Null
}
& git -C $TinygradDir checkout --quiet $commit 2>&1 | Out-Null
& git -C $TinygradDir checkout $commit -- tinygrad/helpers.py 2>&1 | Out-Null
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($head -ne $commit) { Write-Host "tinygrad is at $head, not $commit"; exit 2 }
& git -C $TinygradDir diff --exit-code $commit -- tinygrad/helpers.py tinygrad/runtime/support/c.py tinygrad/runtime/autogen/nv.py tinygrad/runtime/autogen/nv_570.py tinygrad/runtime/support/nv/ip.py
if ($LASTEXITCODE -ne 0) { Write-Host 'tinygrad oracle inputs differ from the pinned commit'; exit 2 }

$src = Join-Path $TinygradDir 'tinygrad'
$pkg = Join-Path $env:TEMP 'nv-gsp-alloc-oracle\pkg'
foreach ($d in 'tinygrad', 'tinygrad\runtime', 'tinygrad\runtime\support', 'tinygrad\runtime\autogen') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $d) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$d\__init__.py"), '')
}
foreach ($f in 'helpers.py', 'runtime\support\c.py', 'runtime\autogen\nv.py', 'runtime\autogen\nv_570.py') {
    $from = Join-Path $src $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "MISSING: $from"; exit 2 }
    Copy-Item -Force $from (Join-Path $pkg "tinygrad\$f")
}
$ip = Join-Path $src 'runtime\support\nv\ip.py'
if (-not (Test-Path -PathType Leaf $ip)) { Write-Host "MISSING: $ip"; exit 2 }

$py = Join-Path $env:TEMP 'nv-gsp-alloc-oracle\oracle.py'
[IO.File]::WriteAllText($py, @"
import sys, re, ctypes, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv, nv_570 as nv_gpu

source = open(sys.argv[2], encoding='utf-8').read()
m = re.search(r'^(  def rpc_rm_alloc\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method rpc_rm_alloc')
ns = {'nv': nv, 'nv_gpu': nv_gpu, 'ctypes': ctypes, 'Any': object}
exec(textwrap.dedent(m.group(1)), ns)

class Obj: pass
def alloc(parent, klass, obj, params, client, priv_root):
    captured = []
    g = Obj(); g.priv_root = priv_root; g.gpfifo_class = -1; g.viddec_class = -2; g.compute_class = -3; g.handle_gen = iter([obj])
    g.cmd_q = Obj(); g.cmd_q.send_rpc = lambda func, data: captured.append((func, bytes(data)))
    g.stat_q = Obj(); g.stat_q.wait_resp = lambda func: None
    p = None if params is None else (ctypes.c_ubyte * len(params)).from_buffer_copy(bytes(params))
    ns['rpc_rm_alloc'](g, parent, klass, p, client=client)
    print('F %d %s' % (captured[0][0], captured[0][1].hex()))

alloc(0xCAF00001, 0x2080, 0xCAF00002, [1, 0, 0, 0], 0xC1E00004, 0xC1D00000)
alloc(0xC1D00000, 0x90F1, 0xCAF00010, None, None, 0xC1D00000)
alloc(0xCAF00002, 0xC5B5, 0xCAF00020, [9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 255, 254, 253], 0xC1E00004, 0xC1D00000)

m = re.search(r'^(  def init_golden_image\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method init_golden_image')
exec(textwrap.dedent(m.group(1)), ns)
class StopBeforeControl(Exception): pass
def stop_control(*args, **kwargs): raise StopBeforeControl()
for client in (0xC1E00004, 0x12345678):
    captured = []
    g = Obj(); g.priv_root = client; g.gpfifo_class = -1; g.viddec_class = -2; g.compute_class = -3
    g.handle_gen = iter(range(0xCAF00100, 0xCAF00104))
    g.cmd_q = Obj(); g.cmd_q.send_rpc = lambda func, data: captured.append((func, bytes(data)))
    g.stat_q = Obj(); g.stat_q.wait_resp = lambda func: None
    g.rpc_rm_alloc = lambda *a, **kw: ns['rpc_rm_alloc'](g, *a, **kw)
    g.rpc_rm_control = stop_control
    try: ns['init_golden_image'](g)
    except StopBeforeControl: pass
    assert len(captured) == 4, 'golden-image allocation prefix changed'
    for func, data in captured: print('F %d %s' % (func, data.hex()))

for client, fifo_va, fifo_pa, instance_pa, method_pa in (
    (0xC1E00004, 0x12345678000, 0x23456780000, 0x34567890000, 0x456789A0000),
    (0x12345678, 0x7654321000, 0x6543210000, 0x5432100000, 0x4321000000)):
    captured = []
    g = Obj(); g.priv_root = client; g.gpfifo_class = nv_gpu.AMPERE_CHANNEL_GPFIFO_A
    g.viddec_class = -2; g.compute_class = -3; g.chan_runlists = {}
    first_handle = 0xCAF00100 if client == 0xC1E00004 else 0xBEE00100
    g.handle_gen = iter(range(first_handle, first_handle + 5))
    g.cmd_q = Obj(); g.cmd_q.send_rpc = lambda func, data: captured.append((func, bytes(data)))
    g.stat_q = Obj(); g.stat_q.wait_resp = lambda func: None
    g.rpc_rm_alloc = lambda *a, **kw: ns['rpc_rm_alloc'](g, *a, **kw)
    def control(hObject, cmd, params):
        if cmd == nv_gpu.NV2080_CTRL_CMD_FIFO_GET_DEVICE_INFO_TABLE:
            di = Obj(); di.numEntries = 0; return di
        if cmd == nv_gpu.NV90F1_CTRL_CMD_VASPACE_COPY_SERVER_RESERVED_PDES: return None
        assert cmd == nv_gpu.NV2080_CTRL_CMD_INTERNAL_STATIC_KGR_GET_CONTEXT_BUFFERS_INFO
        raise StopBeforeControl()
    g.rpc_rm_control = control
    g.nvdev = Obj(); g.nvdev.mm = Obj()
    g.nvdev.mm.alloc_vaddr = lambda size: 0x80000000
    g.nvdev.mm.page_tables = lambda addr, size: []
    allocations = iter((fifo_pa, instance_pa))
    def valloc(size, contiguous):
        assert size == 0x1000 and contiguous
        area = Obj(); area.va_addr = fifo_va; area.paddrs = [(next(allocations), size)]; return area
    def boot_mem(size, sysmem):
        assert size == 0x5000 and sysmem is False
        return None, method_pa, None
    g.nvdev.mm.valloc = valloc; g.nvdev._alloc_boot_mem = boot_mem
    try: ns['init_golden_image'](g)
    except StopBeforeControl: pass
    assert len(captured) == 5, 'golden-image channel allocation changed'
    func, data = captured[-1]
    print('F %d %s' % (func, data.hex()))
"@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
