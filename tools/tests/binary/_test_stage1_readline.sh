#!/bin/bash
# Test: boot Stage 1 ELF (which has effectful main with read-line)
# Send simple input, see if it crashes
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE=/tmp/rl1-pipe-$$
RAW=/tmp/rl1-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "READY received"
# Just send a mode line — main reads: mode <- read-line
# If main crashes during read-line, we'll see QEMU exit
printf 'TEXT\n' > "$PIPE" &
sleep 3
echo "After sending TEXT: $(wc -c < "$RAW") bytes"
kill -0 $QEMU 2>/dev/null && echo "QEMU alive" || echo "QEMU DEAD"
head -c 200 "$RAW" | cat -v
echo ""
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
