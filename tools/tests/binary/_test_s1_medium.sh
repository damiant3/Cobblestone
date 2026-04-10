#!/bin/bash
# Test Stage 1 with the first few files of the compiler source
STAGE1=/tmp/stage1.elf
REPO=/mnt/d/Projects/NewRepository-cam

# Build a medium source: just the core + syntax files
echo "=== Building medium source ==="
MEDIUM=""
for f in $(find "$REPO/Codex.Codex/Core" "$REPO/Codex.Codex/Syntax" -name "*.codex" | sort); do
    MEDIUM="$MEDIUM$(cat "$f")"$'\n\n'
done
echo "Medium source: ${#MEDIUM} chars"

echo "=== Stage 1 compiling medium source ==="
PIPE=/tmp/med-p-$$; RAW=/tmp/med-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 1 booted"
START=$SECONDS
(printf 'TEXT\n'; printf '%s' "$MEDIUM"; printf '\x04') > "$PIPE" &

while true; do
    sleep 10
    kill -0 $Q 2>/dev/null || break
    RAWSIZE=$(wc -c < "$RAW")
    ELAPSED=$((SECONDS - START))
    echo "  ${ELAPSED}s: ${RAWSIZE}b"
    grep -qa 'HEAP:' "$RAW" && break
done

ELAPSED=$((SECONDS - START))
RAWSIZE=$(wc -c < "$RAW")
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo ""
if grep -qa 'HEAP:' "$RAW"; then
    echo "COMPLETED: ${RAWSIZE} bytes in ${ELAPSED}s"
else
    echo "CRASHED after ${ELAPSED}s, ${RAWSIZE} bytes"
    echo "Last 300 chars:"
    tail -c 300 "$RAW" | cat -v
fi
rm -f "$PIPE" "$RAW"
