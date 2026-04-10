#!/bin/bash
# Compile a tiny program with record field 0 access via Stage 0 BINARY mode
# Then run the resulting binary to check
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

TINY='Chapter: Test

  Pair = record {
   first : Text,
   second : Integer
  }

  main = let p = Pair { first = "hello", second = 42 }
   in do
    print-line ("F0=" ++ p.first)
    print-line ("F1=" ++ integer-to-text (p.second))
'

echo "=== Stage 0: compiling tiny program (BINARY mode) ==="
PIPE=/tmp/fb-p-$$; RAW=/tmp/fb-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "Stage 0 booted, sending BINARY + tiny source..."
(printf 'BINARY\n'; printf '%s' "$TINY"; printf '\x04') > "$PIPE" &

# Wait for SIZE:
for i in $(seq 1 60); do
    sleep 1
    kill -0 $Q 2>/dev/null || break
    grep -qa 'SIZE:' "$RAW" && break
done

if ! grep -qa 'SIZE:' "$RAW"; then
    echo "FAIL: no SIZE:"
    tail -c 500 "$RAW" | cat -v
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; exit 1
fi

ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
echo "SIZE:$ELF_SIZE"
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
NEEDED=$((BSTART + ELF_SIZE))
while true; do CUR=$(wc -c < "$RAW"); [ "$CUR" -ge "$NEEDED" ] && break; kill -0 $Q 2>/dev/null || break; sleep 1; done
dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of=/tmp/tiny.elf 2>/dev/null
echo "Tiny ELF: $(wc -c < /tmp/tiny.elf) bytes"
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"

echo ""
echo "=== Running tiny binary ==="
PIPE2=/tmp/fb2-p-$$; RAW2=/tmp/fb2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "/tmp/tiny.elf" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
sleep 3
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null

echo "Output ($(wc -c < "$RAW2") bytes):"
cat -v "$RAW2"
rm -f "$PIPE2" "$RAW2" /tmp/tiny.elf
