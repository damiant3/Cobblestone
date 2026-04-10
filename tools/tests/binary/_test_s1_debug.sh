#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Stage 1: $(wc -c < "$STAGE1") bytes"

PIPE=/tmp/s1d-p-$$; RAW=/tmp/s1d-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW"; then
    echo "Stage 1 did not boot"
    kill $Q $H 2>/dev/null; wait 2>/dev/null
    rm -f "$PIPE" "$RAW"; exit 1
fi
echo "Stage 1 booted"

# Send tiny TEXT input
(printf 'TEXT\n'; printf 'Chapter: T\nmain = 42\n'; printf '\x04') > "$PIPE" &

# Check every second
for i in $(seq 1 15); do
    sleep 1
    if ! kill -0 $Q 2>/dev/null; then
        echo "QEMU exited at ${i}s"
        break
    fi
done
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Full output ($(wc -c < "$RAW") bytes) ==="
cat -v "$RAW"
echo ""
echo ""
echo "=== S: markers ==="
grep -a 'S:' "$RAW" | head -20
rm -f "$PIPE" "$RAW"
