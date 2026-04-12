#!/bin/bash
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
PIPE="/tmp/dbg-pipe-$$"
RAW="/tmp/dbg-raw-$$"

rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

echo "Starting QEMU binary test (600s timeout)..."
timeout 600 "$QEMU" \
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
    sleep 0.5
    WAIT=$((WAIT + 1))
    if [ "$WAIT" -gt 40 ]; then
        echo "FAIL: no READY after 20s"
        kill $QPID 2>/dev/null || true
        kill $HOLDER 2>/dev/null || true
        rm -f "$PIPE" "$RAW"
        exit 1
    fi
done
echo "Got READY, sending BINARY mode + source..."
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Wait for completion or timeout
wait $QPID 2>/dev/null || true
kill $HOLDER 2>/dev/null || true
wait 2>/dev/null || true

echo ""
echo "=== Raw output size: $(wc -c < "$RAW") bytes ==="
echo ""
echo "=== Searching for markers ==="
strings "$RAW" | grep -E 'SIZE:|HEAP:|STACK:|ERROR|OOM|error|panic|fault' | head -20
echo ""
echo "=== First 500 bytes as strings ==="
strings "$RAW" | head -20

rm -f "$PIPE" "$RAW"
