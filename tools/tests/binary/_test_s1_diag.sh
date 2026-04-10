#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
echo "Testing Stage 1 with diagnostics..."

PIPE=/tmp/s1d-p-$$; RAW=/tmp/s1d-r-$$; ERR=/tmp/s1d-e-$$
rm -f "$PIPE" "$RAW" "$ERR"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>"$ERR" & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done

echo "Stage 1 booted, sending TEXT + tiny source"
(printf 'TEXT\n'; printf 'Chapter: T\nmain = 42\n'; printf '\x04') > "$PIPE" &

# Check every 2 seconds if QEMU is still alive
for i in $(seq 1 8); do
    sleep 2
    if ! kill -0 $Q 2>/dev/null; then
        echo "QEMU exited at ${i}x2 = $((i*2))s"
        break
    fi
    echo "  $((i*2))s: QEMU alive, output=$(wc -c < "$RAW")b"
done

kill $Q $H 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Serial output ==="
cat -v "$RAW"
echo ""
echo "=== QEMU stderr ==="
cat "$ERR"
rm -f "$PIPE" "$RAW" "$ERR"
