#!/bin/bash
# Test: does a program with just Red (no match) compile and boot?
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/tc-p-$$; RAW=/tmp/tc-r-$$; ELF=/tmp/tc-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
# Test: main just returns 42 but we define a sum type
printf 'BINARY\nChapter: T\n\nSection: Types\n\n  Color = | Red | Green | Blue\n\nSection: Main\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
rm -f "$RAW"
echo "Test 1 (sum type defined, main=42):"
timeout 3 qemu-system-x86_64 -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -2
rm -f "$ELF"
