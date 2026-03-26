#!/bin/bash
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
INPUT="/mnt/d/Projects/NewRepository-cam/tools/mm3-minimal-sum.txt"
PIPE="/tmp/qemu-serial-pipe"
rm -f "$PIPE"
mkfifo "$PIPE"
(sleep 3; cat "$INPUT"; printf '\x04'; sleep 30) > "$PIPE" &
SENDER=$!
timeout 40 qemu-system-x86_64 \
  -kernel "$KERNEL" -serial stdio -display none -no-reboot -m 512 \
  < "$PIPE" 2>/dev/null
kill $SENDER 2>/dev/null
rm -f "$PIPE"
