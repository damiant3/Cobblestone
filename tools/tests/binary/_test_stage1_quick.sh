#!/bin/bash
# Quick test: can Stage 1 compile main=42?
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE=/tmp/sq-pipe-$$
RAW=/tmp/sq-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "READY: $(grep -c 'READY' "$RAW")"
printf 'BINARY\nChapter: Test\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
echo "Source sent. Waiting..."
for i in $(seq 1 15); do
    sleep 2
    SIZE=$(wc -c < "$RAW")
    echo "  ${i}x2s: $SIZE bytes"
    grep -qa 'SIZE:' "$RAW" && echo "  SIZE: found!" && break
    kill -0 $QEMU 2>/dev/null || { echo "  QEMU exited"; break; }
done
echo "Final output:"
head -c 300 "$RAW" | cat -v
echo ""
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
