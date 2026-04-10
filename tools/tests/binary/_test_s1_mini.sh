#!/bin/bash
STAGE1=/tmp/stage1.elf
MINI=/mnt/d/Projects/NewRepository-cam/samples/mini-bootstrap.codex
echo "=== Stage 1 compiling mini-bootstrap ==="

PIPE=/tmp/sm-p-$$; RAW=/tmp/sm-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 1 booted"
(printf 'TEXT\n'; cat "$MINI"; printf '\x04') > "$PIPE" &
sleep 10
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo "Output ($(wc -c < "$RAW") bytes):"
cat -v "$RAW" | head -40
rm -f "$PIPE" "$RAW"
