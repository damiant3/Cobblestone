#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
echo "Source: $(wc -c < "$SOURCE") bytes"
PIPE=/tmp/s0f-pipe-$$; RAW=/tmp/s0f-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 360 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "READY received"
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
echo "Source sent. Waiting for SIZE:..."
for i in $(seq 1 180); do
    sleep 2
    if grep -qa 'SIZE:' "$RAW"; then
        echo "SIZE: found at ${i}x2s"
        grep -a 'SIZE:' "$RAW" | head -1
        break
    fi
    kill -0 $QEMU 2>/dev/null || { echo "QEMU exited at ${i}x2s"; break; }
done
echo "Raw output: $(wc -c < "$RAW") bytes"
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
