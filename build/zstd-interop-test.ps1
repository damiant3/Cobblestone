# BACKLOG 5.13 - real zstd interop.
#
# Our Zstd chapter round-trips through its OWN decoder, and that cannot see a
# frame-header or block-header bug both halves share. It missed exactly one: the
# frame wrote a two-byte Frame_Content_Size, which RFC 8878 3.1.1.1.4 reads with
# a +256 offset, so a real zstd decoder rejected every frame we ever emitted as
# corrupt while our own tests stayed green. Nothing in the tree drove a real zstd
# decoder.
#
# This does. It compiles codex/test/zstd-interop.codex (which prints inputs and
# their zstd-compressed bytes as decimals), then hands each compressed blob to
# python's zstandard -- an independent RFC 8878 decoder -- and requires it to
# reproduce the original bytes. If our framing or blocks drift off-spec, this
# fails where the battery cannot.
#
# Requires python with the `zstandard` module. If either is absent the test
# SKIPS (exit 0) rather than failing, the same way the Renode harnesses skip
# when Renode is not installed.
#
#   pwsh build/zstd-interop-test.ps1                            # against seed/Codex.cdx
#   pwsh build/zstd-interop-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT

param(
  [string]$Kernel = "seed/Codex.cdx"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/zstd-interop"
New-Item -ItemType Directory -Force $out | Out-Null

# Skip cleanly if the oracle is not available.
$py = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $py) { Write-Host "[zstd-interop] SKIP: python not found" -ForegroundColor Yellow; exit 0 }
& $py -c "import zstandard" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host "[zstd-interop] SKIP: python 'zstandard' module not installed (pip install zstandard)" -ForegroundColor Yellow; exit 0 }

$probeCdx   = Join-Path $out "probe.cdx"
$probeOut   = Join-Path $out "probe.out"
$compile    = Join-Path $root "build/compile.ps1"
$vm         = Join-Path $root "tools/codex-vm.exe"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

Write-Host "[zstd-interop] compiling probe with $Kernel ..."
pwsh $compile -Src (Join-Path $root "codex/test/zstd-interop.codex") -Out $probeCdx -Log (Join-Path $out "probe.log") -Kernel $kernelPath | Out-Null
if (-not (Test-Path $probeCdx)) { Write-Host "[zstd-interop] FAIL: probe did not compile" -ForegroundColor Red; exit 1 }

Write-Host "[zstd-interop] running probe ..."
& $vm -kernel $probeCdx -headless -output $probeOut -mem 3072 | Out-Null
if (-not (Test-Path $probeOut)) { Write-Host "[zstd-interop] FAIL: probe produced no output" -ForegroundColor Red; exit 1 }

$decoder = @'
import sys, re
import zstandard
lines = open(sys.argv[1], "r", errors="replace").read().splitlines()
def parse(tag):
    for ln in lines:
        i = ln.find(tag + ":")
        if i >= 0:
            return bytes(int(x) for x in re.findall(r"\d+", ln[i+len(tag)+1:]))
    return None
d = zstandard.ZstdDecompressor()
ok = True
for tag in ("huf", "huf2", "rle", "mixed"):
    o = parse(tag + "-orig"); c = parse(tag + "-comp")
    if o is None or c is None:
        print(f"{tag}: MISSING (orig={o is not None}, comp={c is not None})"); ok = False; continue
    try:
        g = d.decompress(c, max_output_size=1 << 20)
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
$rc = $LASTEXITCODE
if ($rc -eq 0) {
  Write-Host "[zstd-interop] PASS: real zstd decodes every block type" -ForegroundColor Green
  exit 0
} else {
  Write-Host "[zstd-interop] FAIL: real zstd rejected our output" -ForegroundColor Red
  exit 1
}
