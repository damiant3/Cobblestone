#!/bin/bash
# Rebuild every warmup's artifacts from its checked-in source: the IR the
# plug consumes, and a bare-metal truth to diff the zig run against. Run
# after a seed change, or whenever the .ir files are missing.
set -e
T="$(cd "$(dirname "$0")/.." && pwd)"
S=$T/warmups

cd $S
for src in hello recurse fib; do
    echo "=== $src ==="
    python3 - "$src" <<'PY'
import sys
p = sys.argv[1]
src = open(f'{p}.codex', 'rb').read()
open(f'{p}-cdx.blob', 'wb').write(b"CDX map\n" + src + b"\x04")
open(f'{p}-ir-cce.blob', 'wb').write(b"IR-CCE\n" + src + b"\x04")
PY
    (cd $T && python3 -u ring_compile.py warmups/${src}-cdx.blob warmups/${src}.cdx 2>&1 | tail -1)
    [ -s ${src}.cdx ] || { echo "COMPILE FAILED: no ${src}.cdx"; exit 1; }
    (cd $T && python3 -u ring_compile.py warmups/${src}-ir-cce.blob warmups/${src}.ir 2>&1 | tail -1)
    [ -s ${src}.ir ] || { echo "COMPILE FAILED: no ${src}.ir"; exit 1; }
    (cd $T && python3 - "$src" <<'PY'
import sys
import codex_vm
p = sys.argv[1]
out = codex_vm.run_cdx(f'warmups/{p}.cdx', timeout=300, idle_timeout=60)
lines = [l for l in out.decode(errors='replace').splitlines()
         if not l.startswith(("WD:", "HEAP:", "STACK:"))]
open(f'warmups/{p}.truth', 'w').write("\n".join(lines) + "\n")
print(f"banked warmups/{p}.truth: {lines}")
PY
    )
done
