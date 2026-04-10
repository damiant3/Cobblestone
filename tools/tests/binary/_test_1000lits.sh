#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/ll1k-p-$$; RAW=/tmp/ll1k-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 60 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat /tmp/ll1000.codex; printf '\x04') > "$PIPE" &
sleep 30; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then echo "OK"
else echo "CRASH ($(wc -c < "$RAW") bytes)"
fi
rm -f "$RAW"
