#!/bin/bash
# Run BINARY-DIAG on a single sample through the bare-metal Codex.Codex.elf.
# Captures serial output, prints last PH:* marker (and any error context).
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-hex"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
QEMU="/usr/bin/qemu-system-x86_64"
TIMEOUT=${TIMEOUT:-180}

if [ $# -ne 1 ]; then
    echo "usage: $0 <sample.codex>" >&2
    exit 2
fi
SAMPLE="$1"
[ -f "$SAMPLE" ] || { echo "no such file: $SAMPLE" >&2; exit 2; }
[ -x "$QEMU" ]   || { echo "no qemu: $QEMU" >&2; exit 2; }
[ -f "$ELF" ]    || { echo "no elf: $ELF" >&2; exit 2; }

RAW="/tmp/bindiag-raw-$$"
PIPE="/tmp/bindiag-pipe-$$"
rm -f "$RAW" "$PIPE"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout "$TIMEOUT" "$QEMU" \
    -enable-kvm \
    -kernel "$ELF" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 1024 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QPID=$!

# Wait for READY
WAIT=0
while ! grep -qa 'READY' "$RAW" 2>/dev/null; do
    sleep 0.2
    WAIT=$((WAIT + 1))
    [ "$WAIT" -gt 100 ] && { echo "FAIL: no READY in 20s"; kill $QPID $HOLDER 2>/dev/null; exit 1; }
    kill -0 $QPID 2>/dev/null || break
done

(printf 'BINARY-DIAG\n'; cat "$SAMPLE"; printf '\x04') > "$PIPE" &

# Wait for output to settle (no growth for ~6s) or qemu exits
PREV=0
STABLE=0
while true; do
    sleep 3
    CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    if [ "$CUR" -gt 100 ] && [ "$CUR" -eq "$PREV" ]; then
        STABLE=$((STABLE + 1))
        [ "$STABLE" -ge 10 ] && break
    else
        STABLE=0
    fi
    PREV=$CUR
    kill -0 $QPID 2>/dev/null || break
    # Stop early if a SIZE: marker has been printed (success) or a CPU exception has been dumped
    if grep -qa 'PH:wrote\|SIZE:\|EXCEPTION\|#PF\|#GP\|#UD\|#DF' "$RAW" 2>/dev/null; then
        sleep 2
        break
    fi
done

kill $QPID $HOLDER 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$PIPE"

echo "=== Sample: $SAMPLE ($(wc -c < "$SAMPLE") bytes) ==="
echo "--- Raw serial (printable, last 60 lines) ---"
strings -n 1 "$RAW" | tail -60 || tail -c 4000 "$RAW"
echo
echo "--- PH:* markers seen ---"
grep -ao 'PH:[A-Za-z0-9:_-]*' "$RAW" | nl
echo
LAST_PH=$(grep -ao 'PH:[A-Za-z0-9:_-]*' "$RAW" | tail -1)
echo "LAST PH: ${LAST_PH:-<none>}"
SAW_SIZE=$(grep -aoc 'SIZE:' "$RAW" || true)
echo "Saw SIZE: marker: $SAW_SIZE"
echo
rm -f "$RAW"
