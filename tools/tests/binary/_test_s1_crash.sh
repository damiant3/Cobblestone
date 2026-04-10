#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
echo "Testing Stage 1 crash location..."

PIPE=/tmp/sc-p-$$; RAW=/tmp/sc-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 -d int \
    < "$PIPE" > "$RAW" 2>/tmp/sc-qemu-$$.log & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'TEXT\n'; printf 'Chapter: T\nmain = 42\n'; printf '\x04') > "$PIPE" &
sleep 3
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo "=== Serial output ==="
cat -v "$RAW"
echo ""
echo "=== QEMU log (last 30 lines) ==="
tail -30 /tmp/sc-qemu-$$.log
rm -f "$PIPE" "$RAW" /tmp/sc-qemu-$$.log
