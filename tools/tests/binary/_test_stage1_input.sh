#!/bin/bash
# Test: can Stage 1 ELF read serial input? Send a simple text program.
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE=/tmp/s1-pipe-$$
RAW=/tmp/s1-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!

echo "Booting Stage 1 ELF..."
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!

for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done

if grep -qa 'READY' "$RAW"; then
    echo "  READY received!"
    # Send a simple TEXT mode program (not BINARY)
    # read-line reads the first line (mode), read-file reads the rest
    # For TEXT mode, mode is a filename, and read-file reads until EOT
    # Actually, let's send "TEXT\n" as mode, which read-file would try to read
    # But on bare metal, read-file calls __bare_metal_read_serial
    # Actually main reads: mode <- read-line; source <- read-file mode
    # So let's just send text mode:
    printf 'Chapter: Test\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
    sleep 5
    echo "  Output after sending text source:"
    SIZE=$(wc -c < "$RAW")
    echo "  $SIZE bytes"
    head -c 500 "$RAW"
    echo ""
else
    echo "  No READY"
fi

kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
