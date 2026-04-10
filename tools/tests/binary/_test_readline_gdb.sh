#!/bin/bash
# GDB: set breakpoint on __read_line, trace execution
BM=/tmp/bm-small-msr2.elf
# Use the small ELF (main=42) where __read_line is at 0x100b72

echo "=== Checking __read_line execution path ==="
timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$BM" \
    -serial file:/tmp/rl-gdb-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

# Break at __read_line (0x100b72) and check if main even calls it
# The main function for main=42 is just "return 42" — no read-line.
# For the read-line test ELF, the __read_line is different.

# Actually, let's use the Stage 1 ELF (full compiler)
kill $QEMU 2>/dev/null; wait 2>/dev/null

STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
echo "Using Stage 1 ELF (full compiler)"

# Find __read_line in Stage 1
READ_LINE_ADDR=$(python3 -c "
data = open('$STAGE1','rb').read()
# __read_line starts with push rbx; push r12 = 53 41 54
# and is preceded by ret (C3) from __read_file
# Actually, let's find the pattern: 53 41 54 49 BC (push rbx; push r12; movabs r12)
pat = bytes([0x53, 0x41, 0x54, 0x49, 0xBC])
for i in range(0x90, len(data)-5):
    if data[i:i+5] == pat and i > 0 and data[i-1] == 0xC3:
        # This is a function start after a ret
        va = 0x100000 + (i - 0x90)
        print(hex(va))
        break
")
echo "  __read_line at: $READ_LINE_ADDR"

timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial file:/tmp/rl-gdb-serial2.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *$READ_LINE_ADDR" \
    -ex "continue" \
    -ex "echo === Hit __read_line ===\n" \
    -ex "info registers rip r10 rbx" \
    -ex "x/20i \$rip" \
    -ex "si 10" \
    -ex "echo === 10 steps in ===\n" \
    -ex "info registers rip rax rsi r11 rdi" \
    -ex "x/3i \$rip" \
    -ex "si 5" \
    -ex "echo === 15 steps in ===\n" \
    -ex "info registers rip rax rsi r11" \
    -ex "x/3i \$rip" \
    -ex "si 5" \
    -ex "echo === 20 steps in ===\n" \
    -ex "info registers rip rax rsi r11" \
    -ex "x/3i \$rip" \
    2>&1 | grep -E "^(=|rip|rax|rsi|r11|r10|rbx|rdi|=>)" | head -40

kill $QEMU 2>/dev/null; wait 2>/dev/null
