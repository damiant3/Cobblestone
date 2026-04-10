#!/bin/bash
# Test: compile main=42 via bare-metal compiler, extract ELF, boot it
set -e
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
OUT_ELF=/tmp/bm-small-test.elf

if [ ! -f "$BM_ELF" ]; then
    echo "ERROR: bare-metal compiler ELF not found at $BM_ELF"
    exit 1
fi

echo "=== Step 1: Compile main=42 via bare-metal compiler ==="
PIPE=/tmp/bboot-pipe-$$
RAW=/tmp/bboot-raw-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!

# Wait for READY
for i in $(seq 1 40); do
    grep -qa 'READY' "$RAW" 2>/dev/null && break
    sleep 0.5
done

if ! grep -qa 'READY' "$RAW" 2>/dev/null; then
    echo "ERROR: bare-metal compiler did not produce READY"
    kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null
    rm -f "$PIPE" "$RAW"
    exit 1
fi
echo "  READY received from bare-metal compiler"

# Send BINARY mode + small program
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &

# Wait for compilation to complete
sleep 15
kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

# Extract ELF from output
python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0:
    print('ERROR: No SIZE: marker in output')
    # Show what we got
    print('Raw output (first 500 bytes as text):')
    print(data[:500])
    exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$OUT_ELF','wb').write(elf)
print(f'  Extracted ELF: {size} bytes -> $OUT_ELF')
"
rm -f "$RAW"

if [ ! -f "$OUT_ELF" ]; then
    echo "ERROR: ELF extraction failed"
    exit 1
fi

echo ""
echo "=== Step 2: Boot the compiled ELF ==="
PIPE2=/tmp/bboot2-pipe-$$
RAW2=/tmp/bboot2-raw-$$
rm -f "$PIPE2" "$RAW2"
mkfifo "$PIPE2"
sleep 999 > "$PIPE2" &
HOLDER2=$!

timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$OUT_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null &
QEMU2=$!

sleep 5
kill $QEMU2 2>/dev/null; kill $HOLDER2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"

echo "  Output from booted ELF:"
if [ -f "$RAW2" ]; then
    SIZE=$(wc -c < "$RAW2")
    echo "  Raw size: $SIZE bytes"
    if [ "$SIZE" -gt 0 ]; then
        cat "$RAW2" | tr '\0' '.' | head -c 500
        echo ""
        if grep -qa 'READY' "$RAW2" 2>/dev/null; then
            echo "  READY found!"
        fi
        if grep -qa '42' "$RAW2" 2>/dev/null; then
            echo "  Exit code 42 found!"
        fi
    else
        echo "  (empty — no serial output)"
    fi
else
    echo "  (no output file)"
fi
rm -f "$RAW2" "$OUT_ELF"

echo ""
echo "=== Done ==="
