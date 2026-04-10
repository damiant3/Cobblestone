#!/bin/bash
# Narrow down the KVM crash: step from 0x102567 to find exact crash point
BM=/tmp/bm-small-v2.elf

echo "=== Disassembly from 0x102567 to 0x1025c5 ==="
python3 -c "
import struct, subprocess
data = open('$BM','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff + 4)[0]
p_va = struct.unpack_from('<I', data, e_phoff + 8)[0]
# Convert 0x102567 to file offset
off = p_off + (0x102534 - p_va)
code = data[off:off + 200]
tmpf = '/tmp/narrow.bin'
open(tmpf, 'wb').write(code)
result = subprocess.run(
    ['objdump', '-D', '-b', 'binary', '-m', 'i386:x86-64',
     '--adjust-vma=0x102534', tmpf],
    capture_output=True, text=True
)
for line in result.stdout.split('\n'):
    if ':\t' in line:
        print(line.strip())
"

echo ""
echo "=== Stepping from CR3 write instruction by instruction ==="
timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial file:/tmp/narrow-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *0x102534" \
    -ex "continue" \
    -ex "echo === At CR3 write ===\n" \
    -ex "si" \
    -ex "echo Step 1:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 2:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 3:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 4:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 5:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 6:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 7:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 8:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 9:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 10:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 11:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 12:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 13:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 14:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 15:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 16:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 17:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 18:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 19:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    -ex "si" \
    -ex "echo Step 20:\n" \
    -ex "info registers rip" \
    -ex "x/1i \$rip" \
    2>&1 | grep -E "^(=|Step|rip|=>|0x|\[)" | head -80

kill $QEMU 2>/dev/null; wait 2>/dev/null
