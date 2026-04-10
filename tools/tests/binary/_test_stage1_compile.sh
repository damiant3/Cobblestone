#!/bin/bash
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex

# First extract Stage 1 from the full source compile
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/s1c-p-$$; RAW=/tmp/s1c-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
ELAPSED=$((SECONDS - START))
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

if ! grep -qa 'SIZE:' "$RAW"; then
    echo "Stage 1 generation FAILED in ${ELAPSED}s"
    rm -f "$RAW"; exit 1
fi

SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$STAGE1" 2>/dev/null
rm -f "$RAW"
echo "Stage 1: $SZ bytes (${ELAPSED}s)"

# Now boot Stage 1 and compile source through it
echo ""
echo "=== Stage 2: Stage 1 compiles source ==="
PIPE2=/tmp/s1c2-p-$$; RAW2=/tmp/s1c2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW2" 2>/dev/null; then
    echo "Stage 1 did not boot (no READY)"
    kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2"; exit 1
fi
echo "Stage 1 READY"
START2=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE2" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW2" && break; kill -0 $Q2 2>/dev/null || break; done
ELAPSED2=$((SECONDS - START2))
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"

if grep -qa 'SIZE:' "$RAW2"; then
    SZ2=$(grep -a 'SIZE:' "$RAW2" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    echo "Stage 2: $SZ2 bytes (${ELAPSED2}s)"
    if [ "$SZ" = "$SZ2" ]; then
        echo ""
        echo "Stage 1 == Stage 2 size. Extracting for byte comparison..."
        SOFF2=$(grep -boa 'SIZE:' "$RAW2" | head -1 | cut -d: -f1)
        BSTART2=$((SOFF2 + 5 + ${#SZ2} + 1))
        dd if="$RAW2" bs=1 skip="$BSTART2" count="$SZ2" of=/tmp/stage2.elf 2>/dev/null
        if cmp -s "$STAGE1" /tmp/stage2.elf; then
            echo "=== MM4: BYTE-IDENTICAL FIXED POINT ==="
        else
            echo "DIFFER — same size but different content"
        fi
        rm -f /tmp/stage2.elf
    else
        echo "Size differs: Stage 1=$SZ Stage 2=$SZ2"
    fi
else
    echo "Stage 2 FAILED in ${ELAPSED2}s (output: $(wc -c < "$RAW2") bytes)"
    head -c 200 "$RAW2" | cat -v
fi
rm -f "$RAW2"
