#!/bin/bash
# Compile and run the small DiagnosticBag-shaped probes, in order of how much
# of the real thing they carry. Each is a ~1 minute cycle instead of the 13 the
# clamp rung costs, which is the whole point: the fault we are chasing needs a
# small reproducer before it needs another theory.
set -u
T="$(cd "$(dirname "$0")/.." && pwd)"
cd "$T"
for p in "$@"; do
    echo "############ $p"
    rm -f "ast/$p.cdx"
    if ! python3 -u ring_compile.py "ast/$p.blob" "ast/$p.cdx" 2>&1 | grep -E "SIZE:|error CDX|CODEGEN-HALTED" | head -4; then
        echo "  COMPILE FAILED"; continue
    fi
    [ -s "ast/$p.cdx" ] || { echo "  no binary"; continue; }
    python3 -c "
import codex_vm
out = codex_vm.run_cdx('ast/$p.cdx', timeout=120, idle_timeout=60)
lines = [l for l in out.decode(errors='replace').splitlines()
         if not l.startswith(('WD:','HEAP:','STACK:'))]
if any('!EXC' in l for l in lines):
    print('  FAULTED:', next(l for l in lines if '!EXC' in l)[:110])
else:
    print('  output:', ' | '.join(lines[:3]))
" 2>&1 | grep -E "FAULTED|output:"
done
