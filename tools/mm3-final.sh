#!/bin/bash
# MM3 Final Attempt: 30-minute timeout, full source, no truncation
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
SOURCE="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.codex"
PIPE="/tmp/qemu-serial-pipe"

SIZE=$(wc -c < "$SOURCE")
echo "MM3 Final: $SIZE bytes, 30 min timeout"
date

rm -f "$PIPE"
mkfifo "$PIPE"

(sleep 3; cat "$SOURCE"; printf '\x04'; sleep 1800) > "$PIPE" &
SENDER=$!

timeout 1810 qemu-system-x86_64 \
    -kernel "$KERNEL" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 512 \
    < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"

echo ""
echo "=== Done ==="
date
