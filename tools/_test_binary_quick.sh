#!/bin/bash
set -u
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/test-pipe
RAW=/tmp/test-raw
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
PID=$!
sleep 3
(printf 'BINARY\n'; echo 'main = 42'; printf '\x04') > "$PIPE" &
sleep 12
echo "=== SIZE marker ==="
grep -a 'SIZE:' "$RAW" || echo "SIZE: NOT FOUND"
echo "=== Total output ==="
wc -c < "$RAW"
kill $PID 2>/dev/null
kill $HOLDER 2>/dev/null
rm -f "$PIPE" "$RAW"
wait 2>/dev/null
