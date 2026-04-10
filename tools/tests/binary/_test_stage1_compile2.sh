#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf

echo "Source: $(wc -c < "$SOURCE") bytes"

# Stage 1: Stage 0 compiles source
PIPE=/tmp/s1-p-$$; RAW=/tmp/s1-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

# Wait for SIZE: then wait for full binary to arrive
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
if grep -qa 'SIZE:' "$RAW"; then
    ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    NEEDED=$((SOFF + 5 + ${#ELF_SIZE} + 1 + ELF_SIZE))
    echo "SIZE:$ELF_SIZE — need $NEEDED bytes total"
    # Wait for full data
    while true; do
        CUR=$(wc -c < "$RAW")
        [ "$CUR" -ge "$NEEDED" ] && break
        kill -0 $Q 2>/dev/null || break
        sleep 1
    done
fi
ELAPSED=$((SECONDS - START))
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

if ! grep -qa 'SIZE:' "$RAW"; then
    echo "FAIL in ${ELAPSED}s"; rm -f "$RAW"; exit 1
fi
BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of="$STAGE1" 2>/dev/null
GOT=$(wc -c < "$STAGE1")
echo "Stage 1: $GOT bytes (${ELAPSED}s)"
rm -f "$RAW"

if [ "$GOT" -ne "$ELF_SIZE" ]; then
    echo "TRUNCATED: got $GOT, expected $ELF_SIZE"; exit 1
fi

# Stage 2: Stage 1 compiles source
echo ""
echo "=== Stage 2 ==="
PIPE2=/tmp/s2-p-$$; RAW2=/tmp/s2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW2" 2>/dev/null; then
    echo "Stage 1 did not boot"; kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2"; exit 1
fi
echo "Stage 1 booted"
START2=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE2" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW2" && break; kill -0 $Q2 2>/dev/null || break; done
if grep -qa 'SIZE:' "$RAW2"; then
    ELF_SIZE2=$(grep -a 'SIZE:' "$RAW2" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    SOFF2=$(grep -boa 'SIZE:' "$RAW2" | head -1 | cut -d: -f1)
    NEEDED2=$((SOFF2 + 5 + ${#ELF_SIZE2} + 1 + ELF_SIZE2))
    while true; do
        CUR2=$(wc -c < "$RAW2")
        [ "$CUR2" -ge "$NEEDED2" ] && break
        kill -0 $Q2 2>/dev/null || break
        sleep 1
    done
fi
ELAPSED2=$((SECONDS - START2))
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"

if ! grep -qa 'SIZE:' "$RAW2"; then
    echo "Stage 2 FAIL in ${ELAPSED2}s ($(wc -c < "$RAW2") bytes)"
    head -c 200 "$RAW2" | cat -v
    rm -f "$RAW2"; exit 1
fi
BSTART2=$((SOFF2 + 5 + ${#ELF_SIZE2} + 1))
dd if="$RAW2" bs=1 skip="$BSTART2" count="$ELF_SIZE2" of=/tmp/stage2.elf 2>/dev/null
GOT2=$(wc -c < /tmp/stage2.elf)
echo "Stage 2: $GOT2 bytes (${ELAPSED2}s)"
rm -f "$RAW2"

if [ "$GOT" = "$GOT2" ] && cmp -s "$STAGE1" /tmp/stage2.elf; then
    echo ""
    echo "=== MM4: BYTE-IDENTICAL FIXED POINT ==="
else
    echo "Stage 1: $GOT bytes, Stage 2: $GOT2 bytes"
fi
rm -f /tmp/stage2.elf
