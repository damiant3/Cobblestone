#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "=== Stage 0 compiling source → Stage 1 ==="

PIPE=/tmp/rt-p-$$; RAW=/tmp/rt-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

while true; do
    sleep 10
    kill -0 $Q 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW" && break
    echo "  $((SECONDS - START))s..."
done

ELAPSED=$((SECONDS - START))
if ! grep -qa 'SIZE:' "$RAW"; then
    echo "Stage 0 failed"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; exit 1
fi
ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
NEEDED=$((BSTART + ELF_SIZE))
while true; do CUR=$(wc -c < "$RAW"); [ "$CUR" -ge "$NEEDED" ] && break; kill -0 $Q 2>/dev/null || break; sleep 1; done
dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of=/tmp/stage1.elf 2>/dev/null
echo "Stage 1: $(wc -c < /tmp/stage1.elf) bytes in ${ELAPSED}s"
cp /tmp/stage1.elf /mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"

echo ""
echo "=== Testing Stage 1 with tiny TEXT input ==="
STAGE1=/tmp/stage1.elf
PIPE2=/tmp/rt2-p-$$; RAW2=/tmp/rt2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW2"; then
    echo "Stage 1 did not boot"; kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2"; exit 1
fi
echo "Stage 1 booted"
(printf 'TEXT\n'; printf 'Chapter: T\nmain = 42\n'; printf '\x04') > "$PIPE2" &

for i in $(seq 1 15); do
    sleep 1
    if ! kill -0 $Q2 2>/dev/null; then echo "QEMU exited at ${i}s"; break; fi
done
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Full Stage 1 output ($(wc -c < "$RAW2") bytes) ==="
cat -v "$RAW2"
echo ""
echo "=== S: markers ==="
grep -a 'S:' "$RAW2" | head -30
rm -f "$PIPE2" "$RAW2"
