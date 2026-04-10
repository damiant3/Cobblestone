#!/bin/bash
# GDB: Trace Stage 1 ELF from READY through first main call
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE=/tmp/gdb-pipe-$$

rm -f "$PIPE"; mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 \
    < "$PIPE" > /tmp/gdb-serial-out.txt 2>/dev/null &
QEMU=$!
sleep 1

# Find the CALL to main in emit-start — it's the only CALL in the 64-bit entry code
# that calls into the helpers region
MAIN_CALL_ADDR=$(python3 -c "
import struct
data = open('$STAGE1','rb').read()
# Find far jump target
for i in range(0x90, 0x200):
    if data[i] == 0xEA and data[i+5] == 0x08 and data[i+6] == 0x00:
        entry_va = struct.unpack_from('<I', data, i+1)[0]
        break
e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
p_off = struct.unpack_from('<I', data, e_phoff+4)[0]
p_va = struct.unpack_from('<I', data, e_phoff+8)[0]
entry_off = p_off + (entry_va - p_va)
end_off = p_off + struct.unpack_from('<I', data, e_phoff+16)[0]
# Search for CALL instructions (E8 xx xx xx xx) in the 64-bit code
code = data[entry_off:end_off]
calls = []
for i in range(len(code) - 5):
    if code[i] == 0xE8:
        rel = struct.unpack_from('<i', code, i+1)[0]
        target = entry_va + i + 5 + rel
        calls.append((entry_va + i, target))
# Find the call that goes to the user function area (after helpers)
# Helpers are in the first ~6KB, user functions after
for addr, target in calls:
    if target > entry_va:  # forward call (to main)
        print(hex(addr))
        break
")

echo "Main CALL at: $MAIN_CALL_ADDR"

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *$MAIN_CALL_ADDR" \
    -ex "continue" \
    -ex "echo === At CALL main ===\n" \
    -ex "info registers rip rsp r10 r15" \
    -ex "x/3i \$rip" \
    -ex "si" \
    -ex "echo === Inside main ===\n" \
    -ex "info registers rip" \
    -ex "x/20i \$rip" \
    2>&1 | head -40

# Send BINARY data while in main
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 5

echo ""
echo "Serial output: $(wc -c < /tmp/gdb-serial-out.txt) bytes"
head -c 300 /tmp/gdb-serial-out.txt

kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
