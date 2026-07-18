# BACKLOG 5.13 - real gzip/deflate interop.
#
# Our Deflate and Gzip round-trip through their own decoder, and that cannot see
# a bug both halves share. This hands their output to python's zlib -- an
# independent RFC 1951/1952 decoder in the standard library -- and requires it to
# reproduce the original bytes. It checks a compressible input (which takes the
# dynamic-Huffman block) and a full-entropy input (which must fall back to a
# stored block), for both the gzip container and raw Deflate, so the stored
# fallback is proven interoperable too. Sibling of build/zstd-interop-test.ps1.
#
# Requires python (zlib ships with it). If python is absent the test SKIPS
# (exit 0), the way the Renode harnesses skip when Renode is not installed.
#
#   pwsh build/gzip-interop-test.ps1                            # against seed/Codex.cdx
#   pwsh build/gzip-interop-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT

param(
  [string]$Kernel = "seed/Codex.cdx"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/gzip-interop"
New-Item -ItemType Directory -Force $out | Out-Null

$py = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $py) { Write-Host "[gzip-interop] SKIP: python not found" -ForegroundColor Yellow; exit 0 }
& $py -c "import zlib" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host "[gzip-interop] SKIP: python 'zlib' unavailable" -ForegroundColor Yellow; exit 0 }

$probeCdx = Join-Path $out "probe.cdx"
$probeOut = Join-Path $out "probe.out"
$compile  = Join-Path $root "build/compile.ps1"
$vm       = Join-Path $root "tools/codex-vm.exe"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

Write-Host "[gzip-interop] compiling probe with $Kernel ..."
pwsh $compile -Src (Join-Path $root "codex/test/gzip-interop.codex") -Out $probeCdx -Log (Join-Path $out "probe.log") -Kernel $kernelPath | Out-Null
if (-not (Test-Path $probeCdx)) { Write-Host "[gzip-interop] FAIL: probe did not compile" -ForegroundColor Red; exit 1 }

Write-Host "[gzip-interop] running probe ..."
& $vm -kernel $probeCdx -headless -output $probeOut -mem 3072 | Out-Null
if (-not (Test-Path $probeOut)) { Write-Host "[gzip-interop] FAIL: probe produced no output" -ForegroundColor Red; exit 1 }

$decoder = @'
import sys, re, zlib
lines = open(sys.argv[1], "r", errors="replace").read().splitlines()
def parse(tag):
    for ln in lines:
        i = ln.find(tag + ":")
        if i >= 0:
            return bytes(int(x) for x in re.findall(r"\d+", ln[i+len(tag)+1:]))
    return None
# gz* are gzip streams (wbits 31); df* are raw Deflate (wbits -15).
cases = [("gzc", 31), ("gzr", 31), ("dfc", -15), ("dfr", -15)]
ok = True
for tag, wbits in cases:
    o = parse(tag + "-orig"); c = parse(tag + "-comp")
    if o is None or c is None:
        print(f"{tag}: MISSING (orig={o is not None}, comp={c is not None})"); ok = False; continue
    try:
        g = zlib.decompress(c, wbits)
        v = "PASS" if g == o else f"MISMATCH len {len(g)} vs {len(o)}"
    except Exception as e:
        v = f"DECODE-ERROR {type(e).__name__}: {e}"
    if v != "PASS": ok = False
    print(f"{tag}: orig={len(o)}B comp={len(c)}B -> {v}")
sys.exit(0 if ok else 1)
'@

$decoderFile = Join-Path $out "decode.py"
Set-Content -Path $decoderFile -Value $decoder -Encoding UTF8
& $py $decoderFile $probeOut
if ($LASTEXITCODE -eq 0) {
  Write-Host "[gzip-interop] PASS: zlib decodes our Gzip and raw Deflate, compressible and stored" -ForegroundColor Green
  exit 0
} else {
  Write-Host "[gzip-interop] FAIL: zlib rejected our output" -ForegroundColor Red
  exit 1
}
