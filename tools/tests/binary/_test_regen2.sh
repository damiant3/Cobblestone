#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
echo "Source: $(wc -c < "$SOURCE") bytes"
PIPE=/tmp/rg3-pipe-$$; RAW=/tmp/rg3-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "READY from Stage 0"
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
for i in $(seq 1 60); do
    sleep 2
    grep -qa 'SIZE:' "$RAW" && echo "SIZE found at ${i}x2s" && break
    kill -0 $QEMU 2>/dev/null || { echo "QEMU exited at ${i}x2s"; break; }
done
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
open('$STAGE1','wb').write(data[nl+1:nl+1+size])
print(f'Stage 1: {size} bytes')
"
else
    echo "FAIL: no SIZE"
    echo "Output: $(wc -c < "$RAW") bytes"
fi
rm -f "$RAW"

echo ""
echo "=== Quick test: boot Stage 1 ==="
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < /dev/null 2>/dev/null | head -c 50
echo ""
