#!/bin/bash
# Run self-hosted ELF with QEMU TCG (no KVM) for proper debugging
BM=/tmp/bm-small-fixed.elf
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
LOG=/tmp/qemu-tcg-debug.log

echo "=== QEMU TCG debug: self-hosted ELF ==="
timeout 10 qemu-system-x86_64 -kernel "$BM" \
    -serial stdio -display none -no-reboot -m 512 \
    -d int,in_asm -D "$LOG" \
    > /tmp/qemu-tcg-serial.txt 2>/dev/null < /dev/null
echo "  Serial output: $(wc -c < /tmp/qemu-tcg-serial.txt) bytes"
echo "  Debug log size: $(wc -c < "$LOG" 2>/dev/null || echo 0) bytes"
echo ""
echo "  Last 100 lines of debug log:"
tail -100 "$LOG" 2>/dev/null
