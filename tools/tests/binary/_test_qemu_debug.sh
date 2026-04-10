#!/bin/bash
# Run self-hosted ELF with QEMU debug logging
BM=/tmp/bm-small-fixed.elf
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf
LOG_BM=/tmp/qemu-bm-debug.log
LOG_REF=/tmp/qemu-ref-debug.log

echo "=== QEMU debug: self-hosted ELF ==="
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial stdio -display none -no-reboot -m 512 \
    -d int,cpu_reset -D "$LOG_BM" \
    > /tmp/qemu-bm-serial.txt 2>/dev/null < /dev/null
echo "  Serial output: $(wc -c < /tmp/qemu-bm-serial.txt) bytes"
echo "  Debug log: $(wc -l < "$LOG_BM" 2>/dev/null || echo 0) lines"
head -50 "$LOG_BM" 2>/dev/null

echo ""
echo "=== QEMU debug: reference ELF ==="
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$REF" \
    -serial stdio -display none -no-reboot -m 512 \
    -d int,cpu_reset -D "$LOG_REF" \
    > /tmp/qemu-ref-serial.txt 2>/dev/null < /dev/null
echo "  Serial output: $(wc -c < /tmp/qemu-ref-serial.txt) bytes"
echo "  Debug log: $(wc -l < "$LOG_REF" 2>/dev/null || echo 0) lines"
head -50 "$LOG_REF" 2>/dev/null
