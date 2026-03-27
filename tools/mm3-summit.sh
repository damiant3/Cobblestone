#!/bin/bash
# MM3 Summit Attempt: Self-hosted compiler compiling itself on bare metal
# The 274KB kernel receives 180KB of its own source over serial,
# compiles it, and emits C# output back over serial.

KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
SOURCE="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.codex"
PIPE="/tmp/qemu-serial-pipe"
OUTPUT="/tmp/mm3-output.txt"

rm -f "$PIPE" "$OUTPUT"
mkfifo "$PIPE"

echo "MM3 Summit Attempt"
echo "Kernel: $KERNEL ($(stat -c%s "$KERNEL" 2>/dev/null || echo '?') bytes)"
echo "Source: $SOURCE ($(wc -c < "$SOURCE" 2>/dev/null || echo '?') bytes)"
echo "Timeout: 30 minutes"
echo ""
echo "Sending source over serial in 3 seconds..."

# Background: wait for boot, send source with pacing, then wait for compilation
(
    sleep 3
    # Paced send: 256-byte chunks with tiny delays to avoid FIFO overflow
    dd if="$SOURCE" bs=256 2>/dev/null | while IFS= read -r -N 256 chunk; do
        printf '%s' "$chunk"
        sleep 0.01
    done
    # Send EOT terminator
    printf '\x04'
    # Wait up to 30 minutes for compilation
    sleep 1800
) > "$PIPE" &
SENDER=$!

# Run QEMU, capture output
timeout 1830 qemu-system-x86_64 \
    -kernel "$KERNEL" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 512 \
    < "$PIPE" 2>/dev/null | tee "$OUTPUT"

kill $SENDER 2>/dev/null
rm -f "$PIPE"

echo ""
echo "=== MM3 Result ==="
BYTES=$(wc -c < "$OUTPUT" 2>/dev/null || echo 0)
echo "Output: $BYTES bytes"

if grep -q "public static class" "$OUTPUT" 2>/dev/null; then
    echo "STATUS: C# class definition found — MM3 MAY BE PROVEN"
    if grep -q "Codex_Codex_Codex" "$OUTPUT" 2>/dev/null; then
        echo "STATUS: Self-hosted compiler class found — MM3 IS PROVEN"
    fi
else
    echo "STATUS: No C# output detected"
fi
