#!/bin/bash
# Test record field 0 access by compiling a tiny program with Stage 0
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

# Tiny source that tests record field 0
TINY='Chapter: Test

  Pair = record {
   first : Integer,
   second : Integer
  }

  main = let p = Pair { first = 111, second = 222 }
   in print-line (integer-to-text (p.first))
'

echo "=== Testing field 0 access ==="
PIPE=/tmp/f0-p-$$; RAW=/tmp/f0-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 0 booted"

(printf 'TEXT\n'; printf '%s' "$TINY"; printf '\x04') > "$PIPE" &

sleep 15
kill $Q $H 2>/dev/null; wait 2>/dev/null

echo "Output ($(wc -c < "$RAW") bytes):"
cat -v "$RAW"
rm -f "$PIPE" "$RAW"
