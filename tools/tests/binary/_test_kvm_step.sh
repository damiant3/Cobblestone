#!/bin/bash
# Step through self-hosted ELF under KVM to find hang point
BM=/tmp/bm-small-v2.elf

echo "=== Stepping through self-hosted ELF with KVM ==="
timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial file:/tmp/step-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

# Set breakpoints at key milestones
# CR3 write (mov %rax,%cr3) is at some address — let's find it
CR3_ADDR=$(python3 -c "
import struct, subprocess
data = open('$BM','rb').read()
# Find the far jump target
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
# Parse ELF to get file offset mapping
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff + 4)[0]
p_va = struct.unpack_from('<I', data, e_phoff + 8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff + 16)[0]
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off + p_fsz]
# Search for mov rax, cr3 = 0F 22 D8
for i in range(len(code) - 2):
    if code[i] == 0x0F and code[i+1] == 0x22 and code[i+2] == 0xD8:
        print(hex(entry_va + i))
        break
")

# Find STI instruction
STI_ADDR=$(python3 -c "
import struct
data = open('$BM','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff + 4)[0]
p_va = struct.unpack_from('<I', data, e_phoff + 8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff + 16)[0]
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off + p_fsz]
# Find first STI (0xFB) after the entry
for i in range(len(code)):
    if code[i] == 0xFB:
        print(hex(entry_va + i))
        break
")

# Find first OUT instruction (serial output)
OUT_ADDR=$(python3 -c "
import struct
data = open('$BM','rb').read()
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff + 4)[0]
p_va = struct.unpack_from('<I', data, e_phoff + 8)[0]
p_fsz = struct.unpack_from('<I', data, e_phoff + 16)[0]
entry_off = p_off + (entry_va - p_va)
code = data[entry_off:p_off + p_fsz]
# Find first OUT al,(dx) = 0xEE after entry
for i in range(len(code)):
    if code[i] == 0xEE:
        print(hex(entry_va + i))
        break
")

echo "CR3 write at: $CR3_ADDR"
echo "STI at: $STI_ADDR"
echo "First OUT at: $OUT_ADDR"

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *0x101693" \
    -ex "continue" \
    -ex "echo === At 64-bit entry ===\n" \
    -ex "info registers rip cr0 cr3 cr4 efer" \
    -ex "delete" \
    -ex "hbreak *$CR3_ADDR" \
    -ex "continue" \
    -ex "echo === At CR3 write ===\n" \
    -ex "info registers rip rax cr3" \
    -ex "si" \
    -ex "echo === After CR3 write ===\n" \
    -ex "info registers rip cr3" \
    -ex "si 10" \
    -ex "echo === 10 steps after CR3 ===\n" \
    -ex "info registers rip rsp" \
    -ex "delete" \
    -ex "hbreak *$STI_ADDR" \
    -ex "continue" \
    -ex "echo === At STI ===\n" \
    -ex "info registers rip rsp cr3" \
    -ex "si" \
    -ex "echo === After STI ===\n" \
    -ex "info registers rip" \
    -ex "x/5i \$rip" \
    -ex "delete" \
    -ex "hbreak *$OUT_ADDR" \
    -ex "continue" \
    -ex "echo === At first OUT ===\n" \
    -ex "info registers rip rax rdx" \
    -ex "si" \
    -ex "echo === After first OUT ===\n" \
    -ex "info registers rip" \
    -ex "si 20" \
    -ex "echo === 20 steps after OUT ===\n" \
    -ex "info registers rip rax rdx" \
    -ex "x/5i \$rip" \
    2>&1 | grep -v "^xmm" | head -80

sleep 2
echo ""
echo "Serial output after stepping: $(wc -c < /tmp/step-serial.txt 2>/dev/null) bytes"
head -c 50 /tmp/step-serial.txt 2>/dev/null
echo ""

kill $QEMU 2>/dev/null; wait 2>/dev/null
