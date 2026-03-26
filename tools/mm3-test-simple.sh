#!/bin/bash
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
PIPE="/tmp/qemu-serial-pipe"
rm -f "$PIPE"
mkfifo "$PIPE"

(sleep 3; printf 'main : Integer\nmain = 42'; printf '\x04'; sleep 15) > "$PIPE" &
SENDER=$!

timeout 25 qemu-system-x86_64 \
  -kernel "$KERNEL" \
  -serial stdio \
  -display none \
  -no-reboot \
  -m 512 \
  < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"
