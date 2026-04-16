#!/bin/bash
# Run bare-metal ELF in MEASURE mode and print the per-phase heap marks.
# Usage: tools/measure-compile.sh [source-file]
set -euo pipefail
ELF="${ELF:-build-output/bare-metal/Codex.Codex.elf}"
SOURCE="${1:-build-output/bare-metal/source.codex}"
QEMU="${QEMU:-/usr/bin/qemu-system-x86_64}"
TIMEOUT="${TIMEOUT:-180}"

if [ ! -f "$ELF" ]; then echo "FAIL: $ELF not found"; exit 1; fi
if [ ! -f "$SOURCE" ]; then echo "FAIL: $SOURCE not found"; exit 1; fi

pipe="/tmp/measure-$$"
rm -f "$pipe"; mkfifo "$pipe"
sleep 999 > "$pipe" &
holder=$!
out="/tmp/measure-out-$$"
timeout "$TIMEOUT" "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 1024 < "$pipe" 2>/dev/null \
| while IFS= read -r line; do
    if [[ "$line" == READY* ]]; then
        (printf 'MEASURE\n'; cat "$SOURCE"; printf '\x04') > "$pipe" &
        continue
    fi
    if [[ "$line" == STACK:* ]]; then
        echo "$line"; break
    fi
    echo "$line"
done > "$out" || true
kill "$holder" 2>/dev/null || true
pkill -f "qemu-system-x86_64.*$ELF" 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$pipe"

echo "=== heap marks (bytes heap-top at each phase boundary) ==="
grep -E '^PHASE-|^EMIT-BYTES|^HEAP:|^STACK:|^RESULT:' "$out" || true
echo ""
echo "=== deltas (bytes allocated in each phase) ==="
awk -F':' '
/^PHASE-/ { name=$1; sub(/^PHASE-/, "", name); val=$2;
            if (prev != "") {
              delta = val - prev_val;
              printf "  %-30s +%12d bytes (%0.1f MB)  cumulative=%d\n", name, delta, delta/1048576.0, val;
            } else {
              printf "  %-30s  (start)  at=%d\n", name, val;
            }
            prev = name; prev_val = val; }
' "$out"
rm -f "$out"
