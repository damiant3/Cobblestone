[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-pdir: tinygrad's own rpc_set_page_directory
# (tinygrad/runtime/support/nv/ip.py), taken from the source verbatim and run
# by Python over its r570 structures, against a stub device whose memory
# manager reports the page directory's entry count and whose command queue
# captures the function number and payload. Only helpers.py,
# runtime/support/c.py and autogen/nv.py are loaded, beside empty package files.
#
#   pwsh build/nv-gsp-pdir-oracle.ps1 > codex/test/nv-gsp-pdir.expected

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
$pkg = Join-Path $env:TEMP 'nv-gsp-pdir-oracle\pkg'
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

$py = Join-Path $env:TEMP 'nv-gsp-pdir-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv

source = open(sys.argv[2], encoding='utf-8').read()
m = re.search(r'^(  def rpc_set_page_directory\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method rpc_set_page_directory')
ns = {'nv': nv, 'ctypes': ctypes}
exec(textwrap.dedent(m.group(1)), ns)

class Obj: pass
def pdir(device, vaspace, paddr, entries, client, priv_root, pasid=None):
    captured = []
    g = Obj(); g.priv_root = priv_root
    g.nvdev = Obj(); g.nvdev.mm = Obj(); g.nvdev.mm.pte_cnt = [entries]
    g.cmd_q = Obj(); g.cmd_q.send_rpc = lambda func, data: captured.append((func, bytes(data)))
    g.stat_q = Obj(); g.stat_q.wait_resp = lambda func: None
    if pasid is None: ns['rpc_set_page_directory'](g, device, vaspace, paddr, client=client)
    else: ns['rpc_set_page_directory'](g, device, vaspace, paddr, client=client, pasid=pasid)
    print('F %d %s' % (captured[0][0], captured[0][1].hex()))

pdir(0xCAF00001, 0xCAF00003, 0x123456000, 512, 0xC1E00004, 0x1)
pdir(0xCAF00011, 0xCAF00013, 0xFFFFFFF000, 256, None, 0xC1D00000, pasid=5)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
