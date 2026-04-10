#!/bin/bash
# Test Stage 1 with a single compiler source file
STAGE1=/tmp/stage1.elf
FILE="$1"
echo "=== Stage 1 compiling: $FILE ==="
echo "Size: $(wc -c < "$FILE") bytes"

PIPE=/tmp/ss-p-$$; RAW=/tmp/ss-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'TEXT\n'; cat "$FILE"; printf '\x04') > "$PIPE" &
sleep 10
kill $Q $H 2>/dev/null; wait 2>/dev/null

RAWSIZE=$(wc -c < "$RAW")
if [ "$RAWSIZE" -gt 10 ]; then
    if grep -qa 'HEAP:' "$RAW"; then
        echo "PASS ($RAWSIZE bytes)"
    else
        echo "CRASH ($RAWSIZE bytes)"
        tail -c 300 "$RAW" | cat -v
    fi
else
    echo "CRASH (only $RAWSIZE bytes)"
fi
rm -f "$PIPE" "$RAW"
