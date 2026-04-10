#!/bin/bash
# Test: compile a program that just calls read-line and prints the result
# Run it on bare-metal Stage 0 to see what read-line returns
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"

echo "=== Test 1: Generate ELF for read-line test ==="
PIPE=/tmp/rl-pipe-$$
RAW=/tmp/rl-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
# Send a program that reads a line and prints it
printf 'BINARY\nChapter: ReadTest\n\n  main : [Console] Nothing\n  main = do\n   line <- read-line\n   print-line line\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

BM_RL=/tmp/rl-test.elf
if grep -qa 'SIZE:' "$RAW"; then
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_RL','wb').write(elf)
print(f'  ELF: {size} bytes')
"
else
    echo "FAIL: no SIZE marker"
    head -c 200 "$RAW" | cat -v
    rm -f "$RAW"
    exit 1
fi
rm -f "$RAW"

echo ""
echo "=== Test 2: Boot read-line test ELF, send 'hello' ==="
PIPE2=/tmp/rl2-pipe-$$
RAW2=/tmp/rl2-raw-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & HOLDER2=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$BM_RL" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & QEMU2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
echo "  READY: $(grep -c 'READY' "$RAW2" 2>/dev/null)"
# Send "hello\n"
printf 'hello\n' > "$PIPE2" &
sleep 3
echo "  Output after sending 'hello':"
cat "$RAW2" | cat -v
echo ""
kill $QEMU2 $HOLDER2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2" "$BM_RL"
