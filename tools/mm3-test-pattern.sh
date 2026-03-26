#!/bin/bash
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
PIPE="/tmp/qemu-serial-pipe"
rm -f "$PIPE"
mkfifo "$PIPE"

SOURCE='Color = Red | Green | Blue

to-int : Color -> Integer
to-int (c) =
  when c
    if Red -> 1
    if Green -> 2
    if Blue -> 3

main : Integer
main = to-int Green'

(sleep 3; printf '%s' "$SOURCE"; printf '\x04'; sleep 60) > "$PIPE" &
SENDER=$!

timeout 70 qemu-system-x86_64 \
  -kernel "$KERNEL" \
  -serial stdio \
  -display none \
  -no-reboot \
  -m 512 \
  < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"
