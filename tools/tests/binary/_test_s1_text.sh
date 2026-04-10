#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
echo "Testing Stage 1 with simple TEXT input..."

# Simple test: just compile a tiny program
TINY='Chapter: Test
main = 42'

PIPE=/tmp/s1t-p-$$; RAW=/tmp/s1t-r-$$
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

# Send TEXT mode with tiny source
(printf 'TEXT\n'; printf '%s' "$TINY"; printf '\x04') > "$PIPE" &
sleep 15
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo "Output ($(wc -c < "$RAW") bytes):"
cat -v "$RAW"
rm -f "$PIPE" "$RAW"
