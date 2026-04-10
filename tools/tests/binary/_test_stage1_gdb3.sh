#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

# Find 64-bit entry point
ENTRY=$(python3 -c "
import struct
data = open('$STAGE1','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        print(hex(struct.unpack_from('<I', data, i+1)[0]))
        break
")
echo "Entry: $ENTRY"

timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial file:/tmp/gdb3-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *$ENTRY" \
    -ex "continue" \
    -ex "echo === At entry ===\n" \
    -ex "info registers rip rsp cr3" \
    -ex "si 20" \
    -ex "echo === 20 steps ===\n" \
    -ex "info registers rip rsp" \
    -ex "x/3i \$rip" \
    -ex "continue &" \
    -ex "shell sleep 3" \
    -ex "interrupt" \
    -ex "echo === After 3s run ===\n" \
    -ex "info registers rip rsp rax" \
    -ex "x/5i \$rip" \
    2>&1 | head -40

echo ""
echo "Serial: $(wc -c < /tmp/gdb3-serial.txt 2>/dev/null) bytes"
head -c 50 /tmp/gdb3-serial.txt 2>/dev/null
echo ""
kill $QEMU 2>/dev/null; wait 2>/dev/null
