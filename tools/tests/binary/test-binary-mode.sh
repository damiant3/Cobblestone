#!/bin/bash
set -u
QEMU="/usr/bin/qemu-system-x86_64"
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
RAW="/tmp/binary-test-raw"
PIPE="/tmp/binary-test-pipe"
echo "main = 42" > /tmp/tiny.codex
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
exec 3>"$PIPE"
timeout 30 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
PID=$!
for i in $(seq 1 40); do
  grep -qa READY "$RAW" 2>/dev/null && break
  sleep 0.25
done
echo "Sending BINARY mode..."
printf 'BINARY\n' >&3
cat /tmp/tiny.codex >&3
printf '\x04' >&3
sleep 12
echo "=== Raw size: $(wc -c < "$RAW") ==="
echo "=== First 400 bytes as text ==="
head -c 400 "$RAW"
echo ""
echo "=== Hex first 8 lines ==="
xxd "$RAW" | head -8
kill $PID 2>/dev/null
exec 3>&-
rm -f "$PIPE" /tmp/tiny.codex
wait 2>/dev/null
