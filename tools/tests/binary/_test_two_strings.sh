#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/ts-p-$$; RAW=/tmp/ts-r-$$; ELF=/tmp/ts-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\nSection: Main\n\n  main : [Console] Nothing\n  main = do\n   print-line "abc"\n   print-line "xyz"\n\x04' > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
rm -f "$RAW"
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -c 100
echo ""
rm -f "$ELF"
