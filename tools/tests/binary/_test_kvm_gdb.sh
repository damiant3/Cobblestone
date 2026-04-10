#!/bin/bash
# Use QEMU GDB stub to find exactly where the self-hosted ELF hangs/crashes under KVM
BM=/tmp/bm-small-v2.elf
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf

echo "=== GDB debug: self-hosted ELF with KVM ==="
# Start QEMU with GDB stub, wait 3s, connect GDB, dump state
timeout 8 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial stdio -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 \
    > /tmp/gdb-serial.txt 2>/dev/null < /dev/null &
QEMU=$!
sleep 1

# Connect GDB and single-step through the boot
gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "info registers" \
    -ex "si 10" \
    -ex "info registers" \
    -ex "c &" \
    -ex "shell sleep 2" \
    -ex "interrupt" \
    -ex "info registers" \
    2>&1 | head -100

kill $QEMU 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Alternative: use QEMU monitor to check CPU state ==="
# Start QEMU with monitor, let it run briefly, then dump CPU state
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial file:/tmp/kvm-serial-mon.txt -display none -no-reboot -m 512 \
    -monitor stdio 2>/dev/null << 'MONITOR_CMDS' | head -100
info cpus
info registers
quit
MONITOR_CMDS

echo ""
echo "  Serial output: $(wc -c < /tmp/kvm-serial-mon.txt 2>/dev/null) bytes"
