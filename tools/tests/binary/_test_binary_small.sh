#!/bin/bash
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/hex-btest-$$
RAW=/tmp/hex-braw-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!

# Wait for READY
for i in $(seq 1 20); do
    grep -qa 'READY' "$RAW" 2>/dev/null && break
    sleep 0.5
done
echo "READY received"

# Send BINARY mode + small test
printf 'BINARY\nChapter: Test\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &

# Wait for output
sleep 15
SIZE=$(wc -c < "$RAW")
echo "Raw output: $SIZE bytes"
# Show first 200 bytes as hex
head -c 200 "$RAW" | xxd | head -15

# Check for SIZE: marker
if grep -qa 'SIZE:' "$RAW"; then
    echo "SIZE marker found!"
    grep 'SIZE:' "$RAW"
else
    echo "No SIZE marker — binary compilation may have failed"
    # Show as text to see any error messages
    cat "$RAW" | tr '\0' '.' | head -c 500
    echo ""
fi

kill $QEMU 2>/dev/null
kill $HOLDER 2>/dev/null
wait 2>/dev/null
rm -f "$PIPE" "$RAW"
