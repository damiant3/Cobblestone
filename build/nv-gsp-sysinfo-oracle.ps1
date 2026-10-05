[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-sysinfo: tinygrad's own
# rpc_set_gsp_system_info (tinygrad/runtime/support/nv/ip.py), taken from the
# source verbatim and run by Python over its r570 GspSystemInfo, against a
# stub device: BAR bases, a PCI address string, configuration reads keyed by
# tinygrad's own PCI offsets (runtime/autogen/pci.py), and a command queue
# that captures the payload. Only helpers.py, runtime/support/c.py,
# autogen/nv.py and autogen/pci.py are loaded, beside empty package files.
#
#   pwsh build/nv-gsp-sysinfo-oracle.ps1 > codex/test/nv-gsp-sysinfo.expected

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
$pkg = Join-Path $env:TEMP 'nv-gsp-sysinfo-oracle\pkg'
foreach ($d in 'tinygrad', 'tinygrad\runtime', 'tinygrad\runtime\support', 'tinygrad\runtime\autogen') {
    New-Item -ItemType Directory -Force (Join-Path $pkg $d) | Out-Null
    [IO.File]::WriteAllText((Join-Path $pkg "$d\__init__.py"), '')
}
foreach ($f in 'helpers.py', 'runtime\support\c.py', 'runtime\autogen\nv.py', 'runtime\autogen\pci.py') {
    $from = Join-Path $src $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "MISSING: $from"; exit 2 }
    Copy-Item -Force $from (Join-Path $pkg "tinygrad\$f")
}
$ip = Join-Path $src 'runtime\support\nv\ip.py'
if (-not (Test-Path -PathType Leaf $ip)) { Write-Host "MISSING: $ip"; exit 2 }

$py = Join-Path $env:TEMP 'nv-gsp-sysinfo-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv, pci

source = open(sys.argv[2], encoding='utf-8').read()
m = re.search(r'^(  def rpc_set_gsp_system_info\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method rpc_set_gsp_system_info')
ns = {'nv': nv, 'pci': pci, 'ctypes': ctypes}
exec(textwrap.dedent(m.group(1)), ns)

class Obj: pass

def info(k, bars, devfmt, config, fmc):
    captured = []
    pcidev = Obj()
    pcidev.bar_info = lambda n: (bars[n], 0x1000)
    pcidev.read_config = lambda off, size: config[(off, size)]
    g = Obj(); g.nvdev = Obj(); g.nvdev.pci_dev = pcidev; g.nvdev.devfmt = devfmt; g.nvdev.fmc_boot = fmc
    g.cmd_q = Obj(); g.cmd_q.send_rpc = lambda func, data: captured.append(bytes(data))
    ns['rpc_set_gsp_system_info'](g)
    print('S %d %s' % (k, captured[0].hex()))

def config(ident, sub, rev):
    return {(pci.PCI_VENDOR_ID, 4): ident, (pci.PCI_SUBSYSTEM_VENDOR_ID, 4): sub, (pci.PCI_REVISION_ID, 1): rev}

info(0, {0: 0xF6000000, 1: 0x6000000000, 3: 0x6800000000}, '0000:01:00.0', config(0x280310DE, 0x88971043, 0xA1), False)
info(1, {0: 0xDE000000, 1: 0xC000000000, 3: 0xC200000000}, '0000:41:1f.7', config(0x13C210DE, 0x3101462, 2), True)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
