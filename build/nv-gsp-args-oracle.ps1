[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-args: tinygrad's own init_rm_args and
# init_libos_args (tinygrad/runtime/support/nv/ip.py), taken from the source
# verbatim and run by Python over its r570 structures, against a stub device
# whose boot-memory allocator places allocation n at 0x1000000000 + n *
# 0x1000000 with its pages 0x2000 apart and records every byte written. Only
# helpers.py, runtime/support/c.py and autogen/nv.py are loaded, beside empty
# package files.
#
#   pwsh build/nv-gsp-args-oracle.ps1 > codex/test/nv-gsp-args.expected

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

$src = Join-Path $TinygradDir 'tinygrad'
$pkg = Join-Path $env:TEMP 'nv-gsp-args-oracle\pkg'
foreach ($d in 'tinygrad', 'tinygrad\runtime', 'tinygrad\runtime\support', 'tinygrad\runtime\autogen') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $d) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$d\__init__.py"), '')
}
foreach ($f in 'helpers.py', 'runtime\support\c.py', 'runtime\autogen\nv.py') {
    $from = Join-Path $src $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "MISSING: $from"; exit 2 }
    Copy-Item -Force $from (Join-Path $pkg "tinygrad\$f")
}
$ip = Join-Path $src 'runtime\support\nv\ip.py'
if (-not (Test-Path -PathType Leaf $ip)) { Write-Host "MISSING: $ip"; exit 2 }

$py = Join-Path $env:TEMP 'nv-gsp-args-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, struct, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import round_up, round_down, ceildiv

source = open(sys.argv[2], encoding='utf-8').read()
def method(name):
    m = re.search(r'^(  def ' + name + r'\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
    if not m: raise SystemExit('ip.py has no method ' + name)
    return textwrap.dedent(m.group(1))

ns = {'nv': nv, 'ctypes': ctypes, 'struct': struct, 'round_up': round_up, 'round_down': round_down, 'ceildiv': ceildiv,
      'NVRpcQueue': lambda *a, **k: None}
exec(method('init_rm_args'), ns)
exec(method('init_libos_args'), ns)

class QView:
    def __init__(self, buf, off): self.buf, self.off = buf, off
    def __setitem__(self, i, v): struct.pack_into('<Q', self.buf, self.off + 8 * i, v)
class View:
    def __init__(self, buf, off): self.buf, self.off = buf, off
    def view(self, off, size=None, fmt=None):
        return QView(self.buf, self.off + off) if fmt == 'Q' else View(self.buf, self.off + off)
    def __setitem__(self, s, data):
        start = self.off + (s.start or 0)
        self.buf[start:start + len(data)] = data
class Obj: pass

def run(k, queue_size):
    allocs = []
    def alloc(size, data=None, sysmem=False):
        n = len(allocs)
        buf = bytearray(round_up(size, 0x1000))
        if data is not None: buf[:len(data)] = data
        allocs.append(buf)
        base = 0x1000000000 + n * 0x1000000
        return (View(buf, 0), None, [base + 0x2000 * i for i in range(ceildiv(size, 0x1000))])
    g = Obj(); g.nvdev = Obj(); g.nvdev._alloc_boot_mem = alloc
    ns['init_rm_args'](g, queue_size)
    ns['init_libos_args'](g)
    queues, rm, libos = allocs[0], allocs[1][:ctypes.sizeof(nv.GSP_ARGUMENTS_CACHED)], allocs[3]
    table = struct.unpack_from('<Q', rm, 16)[0]
    pages = (table + 2 * queue_size) // 0x1000
    print('PT %d %s' % (k, bytes(queues[:8 * pages]).hex()))
    print('RM %d %s' % (k, bytes(rm).hex()))
    print('TX %d %s' % (k, bytes(queues[table:table + ctypes.sizeof(nv.msgqTxHeader)]).hex()))
    print('LB %d %s' % (k, bytes(libos[:6 * ctypes.sizeof(nv.LibosMemoryRegionInitArgument)]).hex()))

run(0, 0x40000)
run(1, 0x80000)
run(2, 0x200000)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
