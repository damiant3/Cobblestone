#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Stage 1: $(wc -c < "$STAGE1") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"

PIPE=/tmp/s1b-p-$$; RAW=/tmp/s1b-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW"; then
    echo "FAIL: Stage 1 did not boot"
    echo "Output so far:"
    cat -v "$RAW"
    kill $Q $H 2>/dev/null; wait 2>/dev/null
    rm -f "$PIPE" "$RAW"; exit 1
fi
echo "Stage 1 booted! Sending BINARY command + source..."
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Monitor progress every 10s
while true; do
    sleep 10
    kill -0 $Q 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW" && break
    RAWSIZE=$(wc -c < "$RAW")
    LAST=$(tail -c 200 "$RAW" 2>/dev/null | tr '\0' ' ' | strings | tail -3)
    ELAPSED=$((SECONDS - START))
    echo "  ${ELAPSED}s (${RAWSIZE}b): $LAST"
done

ELAPSED=$((SECONDS - START))
if grep -qa 'SIZE:' "$RAW"; then
    ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    echo "SUCCESS: SIZE:$ELF_SIZE in ${ELAPSED}s"
else
    echo "FAIL after ${ELAPSED}s"
    echo "Total output: $(wc -c < "$RAW") bytes"
    echo ""
    echo "=== Last 1000 bytes ==="
    tail -c 1000 "$RAW" | cat -v
    echo ""
    echo "=== Searching for error markers ==="
    grep -a 'ERROR\|CRASH\|PANIC\|FAULT\|error\|crash\|fault' "$RAW" | head -10
    echo ""
    echo "=== Searching for stage markers ==="
    grep -a 'S:\|TOKENS\|PARSE\|DESUGAR\|SCOPE\|CHECK\|LOWER\|EMIT' "$RAW" | head -20
fi
kill $Q $H 2>/dev/null; wait 2>/dev/null
rm -f "$PIPE" "$RAW"
