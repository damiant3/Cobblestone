[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-seq: tinygrad's own run_cpu_seq
# (tinygrad/runtime/support/nv/ip.py), taken from the source verbatim and run
# by Python over its r570 rpc_run_cpu_sequencer_v17_00, against a stub device
# that logs every register write, read, poll, delay, falcon action and
# mailbox access instead of touching hardware. A register read answers
# (address * 0x9E3779B1) mod 2^32, as codex/os/kernel/NvGspSeq.codex's
# seq-model does. tinygrad's ValueError on an unknown opcode is logged as
# BAD and the opcode. Only helpers.py, runtime/support/c.py and autogen/nv.py
# are loaded, beside empty package files.
#
#   pwsh build/nv-gsp-seq-oracle.ps1 > codex/test/nv-gsp-seq.expected

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
$pkg = Join-Path $env:TEMP 'nv-gsp-seq-oracle\pkg'
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

$py = Join-Path $env:TEMP 'nv-gsp-seq-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, struct, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import lo32, hi32

source = open(sys.argv[2], encoding='utf-8').read()
m = re.search(r'^(  def run_cpu_seq\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method run_cpu_seq')

log = []
class Time:
    @staticmethod
    def sleep(s): log.append('D %d' % round(s * 1e6))
def wait_cond(fn, *args, value=None, msg=None):
    if args: log.append('P 0x%x 0x%x 0x%x' % (args[0], args[1], value))
    else: log.append('WAIT handoff')
ns = {'nv': nv, 'ctypes': ctypes, 'lo32': lo32, 'hi32': hi32, 'time': Time, 'wait_cond': wait_cond}
exec(textwrap.dedent(m.group(1)), ns)

class Obj: pass
class Reg:
    def __init__(self, name): self.name = name
    def write(self, v): log.append('%s 0x%x' % (self.name, v))
    def with_base(self, base):
        r = Obj(); r.read = lambda: (log.append('MBR %s' % base), 0)[1]; return r
class Flcn:
    falcon, sec2 = 'gsp', 'sec2'
    def reset(self, f, riscv=False): log.append('RESET %s riscv' % f if riscv else 'RESET %s' % f)
    def disable_ctx_req(self, f): log.append('DISCTX %s' % f)
    def start_cpu(self, f): log.append('START %s' % f)
    def wait_cpu_halted(self, f): log.append('HALT %s' % f)

def dev():
    d = Obj()
    d.wreg = lambda a, v: log.append('W 0x%x 0x%x' % (a, v))
    def rreg(a):
        log.append('R 0x%x' % a)
        return (a * 0x9E3779B1) & 0xFFFFFFFF
    d.rreg = rreg
    d.flcn = Flcn()
    d.NV_PGSP_FALCON_MAILBOX0 = Reg('MB0'); d.NV_PGSP_FALCON_MAILBOX1 = Reg('MB1')
    d.NV_PFALCON_FALCON_MAILBOX0 = Reg('MBR')
    return d

def trace(k, words, libos):
    del log[:]
    buf = struct.pack('<II', len(words) + 16, len(words)) + bytes(32) + struct.pack('<%dI' % len(words), *words)
    g = Obj(); g.nvdev = dev(); g.libos_args_sysmem = libos
    try: ns['run_cpu_seq'](g, buf)
    except ValueError as e: log.append('BAD %s' % re.search(r'op code (\d+)', str(e)).group(1))
    for t in log: print('T %d %s' % (k, t))

trace(0, [0, 0x110000, 0xDEADBEEF, 1, 0x110004, 0x12345678, 0xFF00FF, 2, 0x110008, 0xF0, 0x30, 1000, 0, 3, 50, 4, 0x11000C, 5, 5, 6, 7, 8], 0x123456789A)
trace(1, [1, 0x840000, 0xCAFEF00D, 0xFFFFFFFF, 1, 0x840004, 0xCAFEF00D, 0, 3, 0, 4, 0x840008, 0, 2, 0x84000C, 0xFFFFFFFF, 0x80000000, 5, 5, 9, 0, 1], 0xFFFFF000)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
