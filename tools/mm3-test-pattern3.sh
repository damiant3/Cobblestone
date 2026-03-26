#!/bin/bash
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
INPUT="/mnt/d/Projects/NewRepository-cam/tools/mm3-pattern-input.txt"
PIPE="/tmp/qemu-serial-pipe"
rm -f "$PIPE"
mkfifo "$PIPE"

# Much longer timeout — sum types may be slow to compile on bare metal
(sleep 3; cat "$INPUT"; printf '\x04'; sleep 300) > "$PIPE" &
SENDER=$!

timeout 310 qemu-system-x86_64 \
  -kernel "$KERNEL" \
  -serial stdio \
  -display none \
  -no-reboot \
  -m 512 \
  < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"
