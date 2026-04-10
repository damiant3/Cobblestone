#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Source: $(wc -c < "$SOURCE") bytes"
PIPE=/tmp/f31-p-$$; RAW=/tmp/f31-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "READY received"
START=$SECONDS
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do
    sleep 1
    grep -qa 'SIZE:' "$RAW" && break
    kill -0 $Q 2>/dev/null || break
done
ELAPSED=$((SECONDS - START))
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then
    SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    echo "OK in ${ELAPSED}s — SIZE:$SZ"
else
    echo "CRASH in ${ELAPSED}s (output: $(wc -c < "$RAW") bytes)"
fi
rm -f "$RAW"
