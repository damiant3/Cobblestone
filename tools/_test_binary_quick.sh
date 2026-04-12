#!/bin/bash
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
PIPE="/tmp/bq-pipe-$$"
OUTPUT="/tmp/bq-out-$$"

rm -f "$PIPE" "$OUTPUT"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 360 "$QEMU" \
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
        echo "[input sent]" >&2
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

echo "=== Binary Stage 1 Results ===" >&2
cat "$OUTPUT" >&2
SIZE=$(grep -oP '^SIZE:\K[0-9]+' "$OUTPUT" 2>/dev/null || echo "MISSING")
echo "SIZE=$SIZE" >&2
rm -f "$OUTPUT"
