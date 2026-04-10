#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial file:/tmp/rl-gdb3-serial.txt -display none -no-reboot -m 512 \
    -S -gdb tcp::1234 &
QEMU=$!
sleep 1

gdb -batch -nx \
    -ex "set architecture i386:x86-64" \
    -ex "target remote localhost:1234" \
    -ex "set pagination off" \
    -ex "hbreak *0x100b72" \
    -ex "continue" \
    -ex "echo === At __read_line entry ===\n" \
    -ex "info registers rip r10" \
    -ex "si 20" \
    -ex "echo === After 20 steps ===\n" \
    -ex "info registers rip rax rsi r11" \
    -ex "x/5i \$rip" \
    -ex "si 10" \
    -ex "echo === After 30 steps ===\n" \
    -ex "info registers rip rax rsi r11" \
    -ex "x/5i \$rip" \
    2>&1 | head -60

kill $QEMU 2>/dev/null; wait 2>/dev/null
