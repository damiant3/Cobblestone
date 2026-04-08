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
(printf 'TEXT\n'; echo 'main = 42'; printf '\x04') > "$PIPE" &
sleep 15
echo "=== First 200 bytes ==="
head -c 200 "$RAW"
echo ""
echo "=== Total output ==="
wc -c < "$RAW"
kill $PID 2>/dev/null
kill $HOLDER 2>/dev/null
rm -f "$PIPE" "$RAW"
wait 2>/dev/null
