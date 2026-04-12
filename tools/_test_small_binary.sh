#!/bin/bash
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SRC="${1:-$REPO/samples/arithmetic.codex}"
QEMU="/usr/bin/qemu-system-x86_64"
PIPE="/tmp/sb-pipe-$$"
RAW="/tmp/sb-raw-$$"

rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

echo "Input: $SRC ($(wc -l < "$SRC") lines, $(wc -c < "$SRC") bytes)"
echo "Starting QEMU (60s timeout)..."

timeout 60 "$QEMU" \
    -enable-kvm \
    -kernel "$ELF" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 1024 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QPID=$!

WAIT=0
while ! grep -qa 'READY' "$RAW" 2>/dev/null; do
    sleep 0.5
    WAIT=$((WAIT + 1))
    if [ "$WAIT" -gt 40 ]; then
        echo "FAIL: no READY"
        kill $QPID 2>/dev/null || true
        kill $HOLDER 2>/dev/null || true
        rm -f "$PIPE" "$RAW"
        exit 1
    fi
done

echo "READY received, sending BINARY..."
(printf 'BINARY\n'; cat "$SRC"; printf '\x04') > "$PIPE" &

wait $QPID 2>/dev/null || true
kill $HOLDER 2>/dev/null || true
wait 2>/dev/null || true

echo ""
echo "=== Raw output: $(wc -c < "$RAW") bytes ==="
echo ""
echo "=== All strings in output ==="
strings "$RAW"
echo ""

rm -f "$PIPE" "$RAW"
