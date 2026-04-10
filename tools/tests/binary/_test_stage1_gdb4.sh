#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

# Try TCG first — does it boot at all?
echo "=== TCG boot test ==="
timeout 5 qemu-system-x86_64 -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < /dev/null 2>/dev/null | head -c 100
echo ""
echo "---"

# KVM boot test
echo "=== KVM boot test ==="
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < /dev/null 2>/dev/null | head -c 100
echo ""
echo "---"

# GDB with TCG (no KVM) - more debuggable
echo "=== GDB with TCG ==="
timeout 15 qemu-system-x86_64 -kernel "$STAGE1" \
    -serial file:/tmp/gdb4-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 2

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "continue &" \
    -ex "shell sleep 5" \
    -ex "interrupt" \
    -ex "echo === Interrupted ===\n" \
    -ex "info registers rip rsp rax" \
    -ex "x/5i \$rip" \
    2>&1 | head -20

echo ""
echo "Serial: $(wc -c < /tmp/gdb4-serial.txt 2>/dev/null) bytes"
head -c 100 /tmp/gdb4-serial.txt 2>/dev/null
echo ""
kill $QEMU 2>/dev/null; wait 2>/dev/null
