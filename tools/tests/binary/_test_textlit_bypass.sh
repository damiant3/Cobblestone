#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/tb-pipe-$$; RAW=/tmp/tb-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\n  main : [Console] Nothing\n  main = do\n   print-line "hello"\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then
    echo "OK: SIZE found (2+ char strings bypass → int 0)"
    grep 'SIZE:' "$RAW" | head -1
else
    echo "FAIL: still crashes"
    echo "Output: $(wc -c < "$RAW") bytes"
fi
rm -f "$RAW"
