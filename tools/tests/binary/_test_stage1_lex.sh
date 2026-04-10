#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE=/tmp/s1l-p-$$; RAW=/tmp/s1l-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 1 READY"
# Send small source
printf 'BINARY\nChapter: T\n\nSection: Main\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:\|STAGE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
sleep 2
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
echo "Output:"
grep -a 'STAGE:\|SIZE:\|READY' "$RAW"
echo "Total: $(wc -c < "$RAW") bytes"
rm -f "$RAW"
