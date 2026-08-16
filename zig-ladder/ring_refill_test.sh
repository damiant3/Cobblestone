#!/bin/bash
# Oracle for the ring refill path: the arith subject padded past the 1 MB
# ring with inert prose must compile through the refill and run with output
# identical to the unpadded subject's banked truth (ast/arith.truth, from
# arithcycle.sh). Same executable content, oversize source -- any
# divergence convicts the transport, not the subject.
set -e
T="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$T/.." && pwd)"
S="${TMPDIR:-/tmp}/ring-refill-test"
mkdir -p "$S"

[ -s $T/ast/arith.truth ] || { echo "no ast/arith.truth -- run ast/arithcycle.sh first"; exit 1; }

python3 - "$S" "$REPO" <<'PY'
import sys
s, repo = sys.argv[1], sys.argv[2]
src = open(f'{repo}/codex/test/plug-oracle-arith.codex', 'rb').read()
# Prose lines are one-space indented in a chapter body; these say nothing
# and change nothing, they only make the unit larger than the ring.
pad = []
i = 0
while sum(len(p) for p in pad) < 1_600_000:
    pad.append(f" Padding paragraph {i} exists to carry this unit past the serial ring.\n\n".encode())
    i += 1
head, sep, tail = src.partition(b"Section: Operations")
padded = head + b"".join(pad) + sep + tail
open(f'{s}/padded.codex', 'wb').write(padded)
open(f'{s}/padded-cdx.blob', 'wb').write(b"CDX map\n" + padded + b"\x04")
print(f"padded subject: {len(padded)} bytes ({len(src)} of real source)")
PY

cd $T
rm -f "$S/padded.cdx"
python3 -u ring_compile.py "$S/padded-cdx.blob" "$S/padded.cdx" 2>&1 | grep -vE "^  \| " | tail -12
[ -s "$S/padded.cdx" ] || { echo "COMPILE FAILED: no padded.cdx"; exit 1; }

python3 - "$S" <<'PY'
import sys
import codex_vm
s = sys.argv[1]
out = codex_vm.run_cdx(f'{s}/padded.cdx', timeout=600, idle_timeout=120)
lines = [l for l in out.decode(errors='replace').splitlines()
         if not l.startswith(("WD:", "HEAP:", "STACK:"))]
open(f'{s}/padded.out', 'w').write("\n".join(lines) + "\n")
print(f"padded subject ran: {len(lines)} lines")
PY

if diff $T/ast/arith.truth "$S/padded.out"; then
    echo "RING REFILL PASS: oversize compile matches the unpadded truth"
else
    echo "RING REFILL FAIL: output diverged"; exit 1
fi
