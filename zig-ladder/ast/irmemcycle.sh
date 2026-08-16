#!/bin/bash
# The IRTextParser memory-probe loop: generate, bundle, one bare-metal
# compile, run, print the heap numbers. No zig arm and no diff -- the
# numbers ARE the result, and a diet iteration is this script end to end.
set -e
T="$(cd "$(dirname "$0")/.." && pwd)"
cd "$T/ast"
python3 gen_irmem_harness.py
rm -f irmem-subject.codex
out=$(~/.local/pwsh/pwsh -NoProfile -File ./bundle_irmem.ps1 2>&1) || { printf '%s\n' "$out" | tail -5; exit 1; }
printf '%s\n' "$out" | tail -1
python3 - <<'PY'
src = open('irmem-subject.codex', 'rb').read()
open('irmem-cdx.blob', 'wb').write(b"CDX map\n" + src + b"\x04")
print(f"blob: {len(src)} bytes")
PY
cd "$T"
rm -f ast/irmem-subject.cdx
python3 -u ring_compile.py ast/irmem-cdx.blob ast/irmem-subject.cdx 2>&1 | grep -E "error|SIZE" | head -8
[ -s ast/irmem-subject.cdx ] || { echo "COMPILE FAILED"; exit 1; }
python3 - <<'PY'
import codex_vm
out = codex_vm.run_cdx('ast/irmem-subject.cdx', timeout=1200, idle_timeout=300)
for l in out.decode(errors='replace').splitlines():
    if not l.startswith(("WD:", "HEAP:", "STACK:")):
        print(l)
PY
