#!/bin/bash
# Test Stage 1 compiling the full compiler source in TEXT mode
STAGE1=/tmp/stage1.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "=== Stage 1 compiling full source (TEXT mode) ==="
echo "Source: $(wc -c < "$SOURCE") bytes"

PIPE=/tmp/sf-p-$$; RAW=/tmp/sf-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 600 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 1 booted"
START=$SECONDS
(printf 'TEXT\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Monitor
while true; do
    sleep 15
    kill -0 $Q 2>/dev/null || break
    RAWSIZE=$(wc -c < "$RAW")
    ELAPSED=$((SECONDS - START))
    echo "  ${ELAPSED}s: ${RAWSIZE}b"
    # Check for completion markers
    grep -qa 'HEAP:' "$RAW" && break
done

ELAPSED=$((SECONDS - START))
RAWSIZE=$(wc -c < "$RAW")
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo ""
echo "=== Result: ${RAWSIZE} bytes in ${ELAPSED}s ==="
if grep -qa 'HEAP:' "$RAW"; then
    echo "COMPLETED"
    echo "First 200 chars:"
    head -c 200 "$RAW" | cat -v
    echo ""
    echo "..."
    echo "Last 200 chars:"
    tail -c 200 "$RAW" | cat -v
else
    echo "INCOMPLETE/CRASH"
    echo "Last 500 chars:"
    tail -c 500 "$RAW" | cat -v
fi
rm -f "$PIPE" "$RAW"
