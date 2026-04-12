#!/bin/bash
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
PIPE="/tmp/bl-pipe-$$"
OUTPUT="/tmp/bl-out-$$"
TIMEOUT=86400  # 24 hours — effectively no timeout

rm -f "$PIPE" "$OUTPUT"
mkfifo "$PIPE"
sleep 99999 > "$PIPE" &
HOLDER=$!

echo "Binary stage 1 — long run (timeout ${TIMEOUT}s)"
echo "  ELF: $(wc -c < "$ELF") bytes"
echo "  Source: $(wc -c < "$SOURCE") bytes"
echo "  Started: $(date)"

timeout "$TIMEOUT" "$QEMU" \
    -enable-kvm \
    -kernel "$ELF" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 1024 \
    < "$PIPE" 2>/dev/null \
| while IFS= read -r line; do
    if [[ "$line" == READY* ]]; then
        (printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
        echo "  [input sent at $(date)]"
        continue
    fi
    if [[ "$line" == STACK:* ]]; then
        echo "$line"
        break
    fi
    if [[ "$line" == HEAP:* || "$line" == SIZE:* ]]; then
        echo "$line"
        continue
    fi
done > "$OUTPUT" || true

kill "$HOLDER" 2>/dev/null || true
pkill -f "qemu-system-x86_64" 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$PIPE"

echo ""
echo "=== Results ($(date)) ==="
cat "$OUTPUT"
echo ""
SIZE=$(grep -oP '^SIZE:\K[0-9]+' "$OUTPUT" 2>/dev/null || echo "MISSING")
HEAP=$(grep -oP '^HEAP:\K[0-9]+' "$OUTPUT" 2>/dev/null || echo "MISSING")
STACK=$(grep -oP '^STACK:\K[0-9]+' "$OUTPUT" 2>/dev/null || echo "MISSING")
echo "SIZE=$SIZE  HEAP=$HEAP  STACK=$STACK"
rm -f "$OUTPUT"
