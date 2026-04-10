#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Source: $(wc -c < "$SOURCE") bytes"
echo "Stage 0: $(wc -c < "$STAGE0") bytes"

PIPE=/tmp/s1-p-$$; RAW=/tmp/s1-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW"; then
    echo "FAIL: Stage 0 did not boot"
    kill $Q $H 2>/dev/null; wait 2>/dev/null
    rm -f "$PIPE" "$RAW"; exit 1
fi
echo "Stage 0 booted, sending BINARY command + source..."
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Monitor progress - print last line of output every 10s
while true; do
    sleep 10
    kill -0 $Q 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW" && break
    LAST=$(tail -c 500 "$RAW" 2>/dev/null | tr '\0' ' ' | strings | tail -1)
    ELAPSED=$((SECONDS - START))
    echo "  ${ELAPSED}s: $LAST"
done

ELAPSED=$((SECONDS - START))
if grep -qa 'SIZE:' "$RAW"; then
    ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    echo "SIZE:$ELF_SIZE in ${ELAPSED}s"
    SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    NEEDED=$((SOFF + 5 + ${#ELF_SIZE} + 1 + ELF_SIZE))
    while true; do
        CUR=$(wc -c < "$RAW")
        [ "$CUR" -ge "$NEEDED" ] && break
        kill -0 $Q 2>/dev/null || break
        sleep 1
    done
    BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
    dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of=/tmp/stage1.elf 2>/dev/null
    GOT=$(wc -c < /tmp/stage1.elf)
    echo "Stage 1 ELF: $GOT bytes"
    cp /tmp/stage1.elf /mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
else
    echo "FAIL: no SIZE: marker after ${ELAPSED}s"
    echo "Last 500 bytes of output:"
    tail -c 500 "$RAW" | cat -v
    echo ""
    echo "Total output: $(wc -c < "$RAW") bytes"
fi
kill $Q $H 2>/dev/null; wait 2>/dev/null
rm -f "$PIPE" "$RAW"
