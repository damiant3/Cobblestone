#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Stage 0: $(wc -c < "$STAGE0") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"

PIPE=/tmp/s0d-p-$$; RAW=/tmp/s0d-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 0 booted, sending BINARY + source..."
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Monitor with debug output
while true; do
    sleep 5
    kill -0 $Q 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW" && break
    RAWSIZE=$(wc -c < "$RAW")
    # Show S: markers
    grep -a 'S:' "$RAW" 2>/dev/null | tail -3
    ELAPSED=$((SECONDS - START))
    echo "  --- ${ELAPSED}s, ${RAWSIZE}b ---"
done

ELAPSED=$((SECONDS - START))
echo ""
echo "=== Debug markers from Stage 0 ==="
grep -a '^S:' "$RAW" 2>/dev/null | head -20
echo ""
if grep -qa 'SIZE:' "$RAW"; then
    ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    echo "SIZE:$ELF_SIZE in ${ELAPSED}s"
    SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
    NEEDED=$((BSTART + ELF_SIZE))
    while true; do
        CUR=$(wc -c < "$RAW")
        [ "$CUR" -ge "$NEEDED" ] && break
        kill -0 $Q 2>/dev/null || break
        sleep 1
    done
    dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of=/tmp/stage1-debug.elf 2>/dev/null
    GOT=$(wc -c < /tmp/stage1-debug.elf)
    echo "Stage 1: $GOT bytes"
    cp /tmp/stage1-debug.elf /mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
else
    echo "FAIL: no SIZE: after ${ELAPSED}s"
    echo "Last 500 bytes:"
    tail -c 500 "$RAW" | cat -v
fi
kill $Q $H 2>/dev/null; wait 2>/dev/null
rm -f "$PIPE" "$RAW"
