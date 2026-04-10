#!/bin/bash
# Control: can Stage 0 compile main=42?
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/s0q-pipe-$$
RAW=/tmp/s0q-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 0 READY: $(grep -c 'READY' "$RAW")"
printf 'BINARY\nChapter: Test\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
for i in $(seq 1 15); do
    sleep 2
    SIZE=$(wc -c < "$RAW")
    echo "  ${i}x2s: $SIZE bytes"
    grep -qa 'SIZE:' "$RAW" && echo "  SIZE: found!" && break
done
echo "Output:"
head -c 200 "$RAW" | cat -v
echo ""
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
