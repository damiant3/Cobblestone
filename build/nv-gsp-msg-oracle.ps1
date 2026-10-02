[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-msg: tinygrad's own _checksum and
# _send_rpc_record (tinygrad/runtime/support/nv/ip.py), taken from the source
# verbatim and run over its r570 structures (tinygrad/runtime/autogen/nv.py)
# by Python. ip.py itself cannot be imported without a GPU and Linux, so the
# two methods are compiled on their own against a stub object whose command
# queue is a bytearray and whose doorbell absorbs every call, and the bytes
# written to the queue are printed. Only
# helpers.py, runtime/support/c.py and autogen/nv.py are loaded, beside empty
# package files, so tinygrad's top level is never imported.
#
#   pwsh build/nv-gsp-msg-oracle.ps1 > codex/test/nv-gsp-msg.expected

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
$pkg = Join-Path $env:TEMP 'nv-gsp-msg-oracle\pkg'
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

$py = Join-Path $env:TEMP 'nv-gsp-msg-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, struct, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import lo32, hi32, ceildiv

source = open(sys.argv[2], encoding='utf-8').read()
def method(name):
    m = re.search(r'^(  def ' + name + r'\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
    if not m: raise SystemExit('ip.py has no method ' + name)
    return textwrap.dedent(m.group(1))

class System:
    @staticmethod
    def memory_barrier(): pass

ns = {'nv': nv, 'ctypes': ctypes, 'struct': struct, 'lo32': lo32, 'hi32': hi32, 'ceildiv': ceildiv, 'System': System}
exec(method('_checksum'), ns)
exec(method('_send_rpc_record'), ns)

class Tx: pass
class Absorb:
    def __getattr__(self, name): return self
    def __call__(self, *a, **k): return self
    def __getitem__(self, i): return self
class Queue:
    _checksum = ns['_checksum']
    _send_rpc_record = ns['_send_rpc_record']
    def __init__(self, seq, slot, slots):
        self.seq = seq
        self.tx = Tx(); self.tx.msgSize = slot; self.tx.msgCount = slots
        self.tx_view = [0] * (ctypes.sizeof(nv.msgqTxHeader) // 4)
        self.queue_mv = bytearray(slot * slots)
        self.gsp = Absorb()

def record(k, func, payload, seq, slot):
    q = Queue(seq, slot, 64)
    q._send_rpc_record(func, bytes(payload))
    used = q.tx_view[getattr(nv.msgqTxHeader, 'writePtr').offset // 4] * slot
    print('R %d %d %s' % (k, used, bytes(q.queue_mv[:used]).hex()))

record(0, 0x10, [], 0, 128)
record(1, 0x49, [1, 2, 3, 4, 5], 7, 128)
record(2, 0x2C, [(i * 37) % 256 for i in range(100)], 123456, 64)
record(3, 1, [(i * 13 + 7) % 256 for i in range(300)], 2, 128)
record(4, 0x41, [255] * 16, 1, 4096)
record(5, 7, [(i * 5 + 3) % 256 for i in range(48)], 9, 128)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
