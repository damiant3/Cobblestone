#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

# Build a program that does read-line/read-file 50 times
SRC='Chapter: HeavyIO

Section: Main

  do-n : Integer -> [Console, FileSystem] Nothing
  do-n (n) =
   if n == 0 then print-line "done"
   else do
    line <- read-line
    data <- read-file line
    print-line (integer-to-text (text-length data))
    do-n (n - 1)

  main : [Console, FileSystem] Nothing
  main = do
   do-n 50'

# Compile it
PIPE=/tmp/hio-p-$$; RAW=/tmp/hio-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

if ! grep -qa 'SIZE:' "$RAW"; then
    echo "COMPILE FAIL"; rm -f "$RAW"; exit 1
fi
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
ELF=/tmp/hio-test.elf
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
echo "ELF: $(wc -c < "$ELF") bytes"
rm -f "$RAW"

# Boot it and send 50 pairs of (line + data)
PIPE2=/tmp/hio2-p-$$; RAW2=/tmp/hio2-r-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & H2=$!
timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
echo "READY received"

# Send 50 rounds: "go\n<payload>\x04" each
{
for i in $(seq 1 50); do
    printf 'go\n'
    # Payload: repeat "abcdefghij" 100 times = 1000 chars
    for j in $(seq 1 100); do printf 'abcdefghij'; done
    printf '\x04'
done
} > "$PIPE2" &

sleep 10
kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"
echo "Output (first 500b):"
head -c 500 "$RAW2"
echo ""
echo "Total output: $(wc -c < "$RAW2") bytes"
rm -f "$RAW2" "$ELF"
