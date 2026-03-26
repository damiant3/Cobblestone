#!/bin/bash
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
INPUT="/mnt/c/Users/Damian/AppData/Local/Temp/mm3-test-input.txt"
PIPE="/tmp/qemu-serial-pipe"

rm -f "$PIPE"
mkfifo "$PIPE"

# Background: wait for boot, send input + EOT, then wait for compilation
(sleep 3; cat "$INPUT"; printf "\x04"; sleep 120) > "$PIPE" &
SENDER=$!

# Run QEMU with 512MB RAM, serial on stdio
timeout 130 qemu-system-x86_64 \
  -kernel "$KERNEL" \
  -serial stdio \
  -display none \
  -no-reboot \
  -m 512 \
  < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"
