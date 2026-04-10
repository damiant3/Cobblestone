#!/bin/bash
set -u
QEMU="/usr/bin/qemu-system-x86_64"
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
SOURCE="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex"
RAW="/tmp/text-test-raw"
PIPE="/tmp/text-test-pipe"
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 180 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
PID=$!
for i in $(seq 1 50); do
  grep -qa READY "$RAW" 2>/dev/null && break
  sleep 0.2
done
echo "READY detected, sending TEXT mode + source..."
(printf 'TEXT\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
PREV=0
STABLE=0
for i in $(seq 1 90); do
    sleep 2
    CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    echo "  t=$((i*2))s: output=$CUR bytes"
    if [ "$CUR" -gt 1000 ] && [ "$CUR" -eq "$PREV" ]; then
        STABLE=$((STABLE + 1))
        [ "$STABLE" -ge 2 ] && break
    else
        STABLE=0
    fi
    PREV=$CUR
    kill -0 $PID 2>/dev/null || break
done
echo ""
echo "=== Total output size ==="
wc -c < "$RAW"
echo "=== First 200 chars ==="
head -c 200 "$RAW"
echo ""
echo "=== Check for Section: (text mode) ==="
grep -ac 'Section:' "$RAW" 2>/dev/null || echo "0"
echo "=== Check for STACK: ==="
grep -a 'STACK:' "$RAW" 2>/dev/null || echo "STACK: NOT FOUND"
kill $PID 2>/dev/null
kill $HOLDER 2>/dev/null
rm -f "$PIPE" "$RAW"
wait 2>/dev/null
