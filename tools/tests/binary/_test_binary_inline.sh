#!/bin/bash
set -u
QEMU="/usr/bin/qemu-system-x86_64"
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
RAW="/tmp/binary-test-raw"
PIPE="/tmp/binary-test-pipe"
echo "main = 42" > /tmp/tiny.codex
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 30 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
PID=$!
for i in $(seq 1 40); do
  grep -qa READY "$RAW" 2>/dev/null && break
  sleep 0.25
done
echo "READY detected, sending BINARY mode..."
(printf 'BINARY\n'; cat /tmp/tiny.codex; printf '\x04') > "$PIPE" &
sleep 12
echo "=== Raw size: $(wc -c < "$RAW") ==="
echo "=== First 400 bytes as text ==="
head -c 400 "$RAW"
echo ""
echo "=== Check for SIZE: ==="
grep -a 'SIZE:' "$RAW" 2>/dev/null || echo "SIZE: NOT FOUND"
echo "=== Check for Section: (text mode indicator) ==="
grep -ac 'Section:' "$RAW" 2>/dev/null || echo "No Section: lines"
kill $PID 2>/dev/null
kill $HOLDER 2>/dev/null
rm -f "$PIPE" /tmp/tiny.codex "$RAW"
wait 2>/dev/null
