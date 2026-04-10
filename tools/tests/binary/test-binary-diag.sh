#!/bin/bash
set -uo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
RAW="/tmp/binary-diag-raw"
PIPE="/tmp/binary-diag-pipe"
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 60 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
QPID=$!
for i in $(seq 1 50); do
    grep -qa 'READY' "$RAW" 2>/dev/null && break
    sleep 0.2
done
echo "READY detected, sending BINARY + source..."
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
PREV=0
STABLE=0
for i in $(seq 1 25); do
    sleep 2
    CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    echo "  t=$((i*2))s: output=$CUR bytes"
    if [ "$CUR" -gt 100 ] && [ "$CUR" -eq "$PREV" ]; then
        STABLE=$((STABLE + 1))
        [ "$STABLE" -ge 2 ] && break
    else
        STABLE=0
    fi
    PREV=$CUR
    kill -0 $QPID 2>/dev/null || break
done
kill $QPID 2>/dev/null || true
kill $HOLDER 2>/dev/null || true
wait 2>/dev/null
echo ""
echo "=== Raw output first 200 bytes (hex) ==="
xxd "$RAW" 2>/dev/null | head -15
echo ""
echo "=== Raw output first 500 chars (text) ==="
head -c 500 "$RAW" 2>/dev/null
echo ""
echo "=== Total output size ==="
wc -c < "$RAW"
echo ""
echo "=== Check for SIZE: marker ==="
grep -a 'SIZE:' "$RAW" 2>/dev/null || echo "SIZE: NOT FOUND"
echo ""
echo "=== Check for Section: (text mode indicator) ==="
grep -ac 'Section:' "$RAW" 2>/dev/null || echo "No Section: found"
rm -f "$PIPE" "$RAW"
