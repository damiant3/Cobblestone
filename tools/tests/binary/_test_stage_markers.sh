#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
STAGE1=/tmp/stage1-markers.elf

# Generate Stage 1
echo "=== Generating Stage 1 ==="
PIPE=/tmp/sm-p-$$; RAW=/tmp/sm-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do
    sleep 1
    grep -qa 'SIZE:' "$RAW" && break
    kill -0 $Q 2>/dev/null || break
done
# Wait for full binary
if grep -qa 'SIZE:' "$RAW"; then
    SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    NEEDED=$((SOFF + 5 + ${#SZ} + 1 + SZ))
    while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do
        kill -0 $Q 2>/dev/null || break
        sleep 1
    done
fi
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

# Show Stage 0 markers
echo "Stage 0 markers:"
grep -a 'STAGE:' "$RAW"

BSTART=$((SOFF + 5 + ${#SZ} + 1))
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$STAGE1" 2>/dev/null
echo "Stage 1: $(wc -c < "$STAGE1") bytes ($((SECONDS - START))s)"
rm -f "$RAW"

# Boot Stage 1, send small test
echo ""
echo "=== Stage 1 small test ==="
PIPE2=/tmp/sm2-p-$$; RAW2=/tmp/sm2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\nSection: Main\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE2" &
sleep 10
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"
echo "Stage 1 small test output:"
head -c 300 "$RAW2"
echo ""
rm -f "$RAW2"

# Boot Stage 1, send full source
echo ""
echo "=== Stage 1 full source ==="
PIPE3=/tmp/sm3-p-$$; RAW3=/tmp/sm3-r-$$
rm -f "$PIPE3" "$RAW3"; mkfifo "$PIPE3"
sleep 999 > "$PIPE3" & H3=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE3" > "$RAW3" 2>/dev/null & Q3=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW3" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE3" &
# Wait up to 120s, check for stages
for i in $(seq 1 120); do
    sleep 1
    kill -0 $Q3 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW3" && break
done
kill $Q3 $H3 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE3"
echo "Stage 1 full source output:"
grep -a 'STAGE:\|SIZE:\|READY' "$RAW3"
echo "Total: $(wc -c < "$RAW3") bytes"
rm -f "$RAW3" "$STAGE1"
