[CmdletBinding()]
param(
    [string]$TinygradDir = (Join-Path $env:TEMP 'nv-gsp-msg-oracle\tinygrad'),
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe'
)

# The oracle for codex/test/nv-gsp-wpr: tinygrad's own init_wpr_meta
# (tinygrad/runtime/support/nv/ip.py), taken from the source verbatim and run
# by Python over its r570 GspFwWprMeta, against a stub device: images of the
# subject's sizes, no FMC, an FRTS offset that agrees with whatever
# init_wpr_meta computes, and a boot-memory allocator that captures the 256
# bytes it is handed. Only helpers.py, runtime/support/c.py and autogen/nv.py
# are loaded, beside empty package files.
#
#   pwsh build/nv-gsp-wpr-oracle.ps1 > codex/test/nv-gsp-wpr.expected

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
$pkg = Join-Path $env:TEMP 'nv-gsp-wpr-oracle\pkg'
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

$py = Join-Path $env:TEMP 'nv-gsp-wpr-oracle\oracle.py'
[IO.File]::WriteAllText($py, @'
import sys, re, ctypes, textwrap
sys.path.insert(0, sys.argv[1])
from tinygrad.runtime.autogen import nv
from tinygrad.helpers import round_down, round_up

source = open(sys.argv[2], encoding='utf-8').read()
m = re.search(r'^(  def init_wpr_meta\(.*?)(?=^  def |^class |\Z)', source, re.S | re.M)
if not m: raise SystemExit('ip.py has no method init_wpr_meta')
ns = {'nv': nv, 'ctypes': ctypes, 'round_down': round_down, 'round_up': round_up}
exec(textwrap.dedent(m.group(1)), ns)

class AnyEq:
    def __eq__(self, other): return True
class Obj: pass

def meta(k, vram, boot_sz, radix3_sz, boot_addr, radix3_addr, sig_addr, code_off, data_off, manifest_off):
    captured = []
    dev = Obj(); dev.vram_size = vram; dev.fmc_boot = False
    dev.flcn = Obj(); dev.flcn.frts_offset = AnyEq()
    def alloc(size, data=None):
        captured.append(bytes(data))
        return (None, None, [0])
    dev._alloc_boot_mem = alloc
    g = Obj(); g.nvdev = dev
    g.init_gsp_image = lambda: None
    g.init_boot_binary_image = lambda: None
    g.booter_image = bytes(boot_sz); g.gsp_image = bytes(radix3_sz)
    g.booter_bar1 = boot_addr; g.gsp_radix3_addrs = [radix3_addr]; g.gsp_signature_bar1 = sig_addr
    g.booter_desc = Obj(); g.booter_desc.monitorCodeOffset = code_off; g.booter_desc.monitorDataOffset = data_off; g.booter_desc.manifestOffset = manifest_off
    ns['init_wpr_meta'](g)
    print('W %d %d %s' % (k, len(captured[0]), captured[0].hex()))

meta(0, 0x400000000, 0x28E00, 0x2B3C123, 0x100000000, 0x200000000, 0x300000000, 0x100, 0x2800, 0x28000)
meta(1, 0x200000000, 0x1234, 0x10, 0x1000, 0x2000, 0x3000, 0, 0, 0)
meta(2, 0x2FFF00000, 0xFFFFF, 0x7FFFFFF, 0xABCDEF000, 0x123456000, 0xFEDCBA000, 0x40, 0x80, 0xC0)
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

$out = & $Python $py $pkg $ip
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
