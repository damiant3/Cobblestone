#!/bin/bash
# Test if Stage 0 boots and responds with KVM
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/kvm-s0-pipe-$$
RAW=/tmp/kvm-s0-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!
sleep 3
echo "Stage 0 KVM output after 3s: $(wc -c < "$RAW") bytes"
head -c 20 "$RAW"
echo ""
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"

# Now test Stage 1 with KVM
STAGE1=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/stage1.elf
PIPE2=/tmp/kvm-s1-pipe-$$
RAW2=/tmp/kvm-s1-raw-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" &
HOLDER2=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null &
QEMU2=$!
sleep 3
echo "Stage 1 KVM output after 3s: $(wc -c < "$RAW2") bytes"
head -c 20 "$RAW2"
echo ""
kill $QEMU2 $HOLDER2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2"

# Now test Stage 1 with TCG
PIPE3=/tmp/tcg-s1-pipe-$$
RAW3=/tmp/tcg-s1-raw-$$
rm -f "$PIPE3" "$RAW3"; mkfifo "$PIPE3"
sleep 999 > "$PIPE3" &
HOLDER3=$!
timeout 10 qemu-system-x86_64 -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE3" > "$RAW3" 2>/dev/null &
QEMU3=$!
sleep 5
echo "Stage 1 TCG output after 5s: $(wc -c < "$RAW3") bytes"
head -c 20 "$RAW3"
echo ""
kill $QEMU3 $HOLDER3 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE3" "$RAW3"
