[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-queue-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-queue: tinygrad's own NVRpcQueue methods
# _checksum, _send_rpc_record, send_rpc and read_resp
# (tinygrad/runtime/support/nv/ip.py at c3aec477), taken from the source
# verbatim and run by Python against a stub queue whose views are a Python
# list of header words and one bytearray ring. Only helpers.py,
# runtime/support/c.py and autogen/nv.py are loaded, beside empty package
# files, so tinygrad's top level is never imported.
#
#   pwsh build/nv-gsp-queue-oracle.ps1 > codex/test/nv-gsp-queue.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'c3aec477b99d9bb87c54d91897cf60acd3f17441'
if (-not (Test-Path -PathType Leaf $Python)) { Write-Host "MISSING: $Python"; exit 2 }
if (-not (Test-Path -PathType Container (Join-Path $TinygradDir '.git'))) {
    & git clone --quiet --filter=blob:none --sparse https://github.com/tinygrad/tinygrad.git $TinygradDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'tinygrad clone failed'; exit 2 }
    & git -C $TinygradDir sparse-checkout set tinygrad/runtime/autogen tinygrad/runtime/support 2>&1 | Out-Null
}
& git -C $TinygradDir checkout --quiet $commit 2>&1 | Out-Null
& git -C $TinygradDir checkout $commit -- tinygrad/helpers.py 2>&1 | Out-Null
$head = (& git -C $TinygradDir rev-parse HEAD).Trim()
if ($head -ne $commit) { Write-Host "tinygrad is at $head, not $commit"; exit 2 }

$src = Join-Path $TinygradDir 'tinygrad'
$pkg = Join-Path $env:TEMP 'nv-gsp-queue-oracle\pkg'
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

$py = Join-Path $env:TEMP 'nv-gsp-queue-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, struct, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import lo32, hi32, ceildiv, round_up

source = open(sys.argv[2], encoding='utf-8').read()
def method(name):
    m = re.search(r'^(  def ' + name + r'\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
    if not m: raise SystemExit('ip.py has no method ' + name)
    return textwrap.dedent(m.group(1))

class System:
    @staticmethod
    def memory_barrier(): pass

ns = {'nv': nv, 'ctypes': ctypes, 'struct': struct, 'lo32': lo32, 'hi32': hi32, 'ceildiv': ceildiv,
      'round_up': round_up, 'System': System, 'DEBUG': 0}
for name in ('_checksum', '_send_rpc_record', 'send_rpc', 'read_resp'): exec(method(name), ns)

SLOT, COUNT = 64, 48
WP = getattr(nv.msgqTxHeader, 'writePtr').offset // 4

class Tx: pass
class Absorb:
    def __getattr__(self, name): return self
    def __call__(self, *a, **k): return self
    def __getitem__(self, i): return self
class Dev(Absorb):
    def __init__(self): self.is_err_state, self.devfmt = False, 'stub'
class Gsp(Absorb):
    def __init__(self): self.seqs, self.nvdev = [], Dev()
    def run_cpu_seq(self, msg): self.seqs.append(msg)
class Queue:
    _checksum = ns['_checksum']; _send_rpc_record = ns['_send_rpc_record']
    send_rpc = ns['send_rpc']; read_resp = ns['read_resp']
    def __init__(self, ring, wp, seq, rp=0):
        self.seq = seq
        self.tx = Tx(); self.tx.msgSize = SLOT; self.tx.msgCount = COUNT
        self.tx_view = [0] * (ctypes.sizeof(nv.msgqTxHeader) // 4)
        self.tx_view[WP] = wp
        self.rx_view = [rp]
        self.queue_mv = ring
        self.gsp = Gsp()

def send(k, wp, seq, func, payload):
    q = Queue(bytearray(SLOT * COUNT), wp, seq)
    q.send_rpc(func, bytes(payload))
    print('S %d %d %d %s' % (k, q.tx_view[WP], q.seq, bytes(q.queue_mv).hex()))

def status(ring, wp, func, payload, result):
    q = Queue(ring, wp, 0)
    q._send_rpc_record(func, bytes(payload))
    off = wp * SLOT
    struct.pack_into('<I', ring, (off + 0x30 + 8) % len(ring), 0x50 + len(payload))
    struct.pack_into('<I', ring, (off + 0x30 + 16) % len(ring), result)
    return q.tx_view[WP]

def read(k, rp, records):
    ring, wp = bytearray(SLOT * COUNT), rp
    for func, payload, result in records: wp = status(ring, wp, func, payload, result)
    q = Queue(ring, wp, 0, rp)
    got, failed = [], 'none'
    try:
        for func, msg in q.read_resp(): got.append((func, msg))
    except RuntimeError as e:
        failed = 'failed'
    for i, (func, msg) in enumerate(got): print('R %d %d %d %d %s' % (k, i, func, len(msg), msg.hex()))
    print('D %d %d %d %s' % (k, q.rx_view[0], wp, failed))

def pl(n, mul, add): return [(i * mul + add) % 256 for i in range(n)]

send(0, 0, 0, 0x49, pl(10, 7, 1))
send(1, 46, 3, 0x41, pl(100, 13, 5))
send(2, 10, 7, 0x67, pl(2000, 3, 11))
send(3, 40, 1, 0x48, pl(1000, 29, 2))
read(0, 0, [(0x41, pl(20, 3, 1), 0), (0x1002, pl(200, 7, 2), 0)])
read(1, 46, [(0x41, pl(100, 5, 9), 0), (0x67, pl(60, 11, 4), 0)])
read(2, 5, [(0x49, pl(30, 2, 7), 0), (0x48, pl(40, 9, 1), 0x51), (0x41, pl(10, 1, 1), 0)])
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
