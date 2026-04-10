#!/bin/bash
# Test: generate Stage 1 ELF, boot it, send BINARY source, check for SIZE: marker
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"

echo "Stage 0: $(wc -c < "$BM_ELF") bytes"

# Step 1: Generate Stage 1 ELF (binary compile of small program)
echo ""
echo "=== Step 1: Generate Stage 1 small ELF ==="
PIPE=/tmp/bp-pipe-$$
RAW=/tmp/bp-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

# Check for SIZE: marker
if grep -qa 'SIZE:' "$RAW"; then
    echo "  SIZE: marker found!"
    grep 'SIZE:' "$RAW"
    # Extract ELF
    BM_SMALL=/tmp/bp-small.elf
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Extracted ELF: {size} bytes')
"
    # Boot it
    echo ""
    echo "=== Step 2: Boot extracted ELF with KVM ==="
    RAW2=/tmp/bp-boot-$$
    timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
        -serial stdio -display none -no-reboot -m 512 \
        > "$RAW2" 2>/dev/null < /dev/null
    echo "  Output: $(wc -c < "$RAW2") bytes"
    head -c 200 "$RAW2" 2>/dev/null
    echo ""
    rm -f "$RAW2" "$BM_SMALL"
else
    echo "  No SIZE: marker — checking raw output..."
    echo "  Raw output size: $(wc -c < "$RAW") bytes"
    echo "  First 500 bytes:"
    head -c 500 "$RAW" | cat -v
    echo ""
fi
rm -f "$RAW"
