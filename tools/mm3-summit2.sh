#!/bin/bash
# MM3 Summit — simplified: just cat the source and wait
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
SOURCE="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.codex"
PIPE="/tmp/qemu-serial-pipe"

rm -f "$PIPE"
mkfifo "$PIPE"

echo "MM3 Summit — sending $(wc -c < "$SOURCE") bytes, waiting 10 minutes"

# Background: boot delay, send source + EOT, wait
(sleep 3; cat "$SOURCE"; printf '\x04'; sleep 600) > "$PIPE" &
SENDER=$!

timeout 610 qemu-system-x86_64 \
    -kernel "$KERNEL" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 512 \
    < "$PIPE" 2>/dev/null

kill $SENDER 2>/dev/null
rm -f "$PIPE"
