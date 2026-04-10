#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/ms-pipe-$$; RAW=/tmp/ms-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 60 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\n  s1 : Text\n  s1 = "hello world"\n\n  s2 : Text\n  s2 = "the quick brown fox"\n\n  s3 : Text\n  s3 = "jumps over the lazy dog"\n\n  s4 : Text\n  s4 = "abcdefghijklmnopqrstuvwxyz"\n\n  main : Integer\n  main = text-length s1 + text-length s2 + text-length s3 + text-length s4\n\x04' > "$PIPE" &
sleep 20
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
echo "Output: $(wc -c < "$RAW") bytes"
if grep -qa 'SIZE:' "$RAW"; then
    echo "SIZE: found"
    grep -a 'SIZE:' "$RAW" | head -1
else
    echo "No SIZE: — crash"
fi
rm -f "$RAW"
