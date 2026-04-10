#!/bin/bash
# Debug with GDB: set breakpoint at 64-bit entry, let VM boot to it
BM=/tmp/bm-small-v2.elf
REF=/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf

# Find the 64-bit entry point for each ELF
BM_ENTRY=$(python3 -c "
import struct
data = open('$BM','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
        print(hex(struct.unpack_from('<I', data, i+1)[0]))
        break
")
REF_ENTRY=$(python3 -c "
import struct
data = open('$REF','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
        print(hex(struct.unpack_from('<I', data, i+1)[0]))
        break
")
echo "Self-hosted 64-bit entry: $BM_ENTRY"
echo "Reference 64-bit entry: $REF_ENTRY"

echo ""
echo "=== Self-hosted ELF with KVM — GDB breakpoint at entry ==="
timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial file:/tmp/gdb2-bm-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *$BM_ENTRY" \
    -ex "continue" \
    -ex "info registers rip rsp rbp cr0 cr3 cr4 efer cs ss" \
    -ex "x/20i \$rip" \
    -ex "si 5" \
    -ex "info registers rip rsp rbp r10 r15" \
    -ex "x/10i \$rip" \
    -ex "continue &" \
    2>&1 | head -80

sleep 3
echo ""
echo "  Serial output: $(wc -c < /tmp/gdb2-bm-serial.txt 2>/dev/null) bytes"
head -c 50 /tmp/gdb2-bm-serial.txt 2>/dev/null
echo ""
kill $QEMU 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Reference ELF with KVM — GDB breakpoint at entry ==="
timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$REF" \
    -serial file:/tmp/gdb2-ref-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU2=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *$REF_ENTRY" \
    -ex "continue" \
    -ex "info registers rip rsp rbp cr0 cr3 cr4 efer cs ss" \
    -ex "x/20i \$rip" \
    -ex "si 5" \
    -ex "info registers rip rsp rbp r10 r15" \
    -ex "x/10i \$rip" \
    -ex "continue &" \
    2>&1 | head -80

sleep 3
echo ""
echo "  Serial output: $(wc -c < /tmp/gdb2-ref-serial.txt 2>/dev/null) bytes"
head -c 50 /tmp/gdb2-ref-serial.txt 2>/dev/null
echo ""
kill $QEMU2 2>/dev/null; wait 2>/dev/null
