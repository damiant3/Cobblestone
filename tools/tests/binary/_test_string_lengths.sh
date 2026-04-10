#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/sl-p-$$; RAW=/tmp/sl-r-$$; ELF=/tmp/sl-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\nSection: Data\n\n  s1 : Text\n  s1 = "the quick brown fox jumps over the lazy dog"\n\n  s2 : Text\n  s2 = "pack my box with five dozen liquor jugs"\n\n  s3 : Text\n  s3 = "how vexingly quick daft zebras jump"\n\n  s4 : Text\n  s4 = "the five boxing wizards jump quickly"\n\nSection: Main\n\n  main : [Console] Nothing\n  main = do\n   print-line (integer-to-text (text-length s1))\n   print-line (integer-to-text (text-length s2))\n   print-line (integer-to-text (text-length s3))\n   print-line (integer-to-text (text-length s4))\n\x04' > "$PIPE" &
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
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -8
rm -f "$ELF"
