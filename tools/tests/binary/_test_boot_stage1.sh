#!/bin/bash
# Quick test: boot stage1.elf, check for any serial output
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
RAW=/tmp/boot-stage1-raw-$$

echo "Booting stage1.elf ($( wc -c < "$ELF") bytes)..."
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW" 2>/dev/null < /dev/null &
QEMU=$!

sleep 5
kill $QEMU 2>/dev/null; wait 2>/dev/null

SIZE=$(wc -c < "$RAW" 2>/dev/null || echo 0)
echo "Output: $SIZE bytes"
if [ "$SIZE" -gt 0 ]; then
    echo "Content:"
    cat "$RAW" | tr '\0' '.'
    echo ""
    if grep -qa 'READY' "$RAW" 2>/dev/null; then
        echo "READY found!"
    fi
else
    echo "(no output — ELF didn't boot)"
fi

# Also check: does the reference ELF still boot?
REF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REF_RAW=/tmp/boot-ref-raw-$$
echo ""
echo "Control test: booting Codex.Codex.elf (Stage 0, reference-compiled)..."
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$REF" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$REF_RAW" 2>/dev/null < /dev/null &
QEMU2=$!
sleep 5
kill $QEMU2 2>/dev/null; wait 2>/dev/null
REF_SIZE=$(wc -c < "$REF_RAW" 2>/dev/null || echo 0)
echo "Output: $REF_SIZE bytes"
if [ "$REF_SIZE" -gt 0 ]; then
    head -c 200 "$REF_RAW"
    echo ""
fi

rm -f "$RAW" "$REF_RAW"
