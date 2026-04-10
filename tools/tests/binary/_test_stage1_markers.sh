#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

# Generate Stage 1
PIPE=/tmp/m1-p-$$; RAW=/tmp/m1-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$STAGE1" 2>/dev/null
echo "Stage 0 markers:"
grep -a 'S:' "$RAW" | head -15
echo "Stage 1: $(wc -c < "$STAGE1") bytes"
rm -f "$RAW"

# Send source to Stage 1
echo ""
PIPE2=/tmp/m2-p-$$; RAW2=/tmp/m2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE2" &
for i in $(seq 1 120); do
    sleep 1
    kill -0 $Q2 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW2" && break
done
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"
echo "Stage 1 markers:"
grep -a 'S:\|SIZE:\|READY' "$RAW2"
echo "Total: $(wc -c < "$RAW2") bytes"
rm -f "$RAW2"
