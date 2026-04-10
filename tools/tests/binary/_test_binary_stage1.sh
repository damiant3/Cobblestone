#!/bin/bash
# Build Stage 1 ELF, then test it with a small program in binary mode
REPO=/mnt/d/Projects/NewRepository-cam
REF_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
STAGE1_ELF=/tmp/stage1-elf-$$

if [ ! -f "$REF_ELF" ] || [ ! -f "$SOURCE" ]; then
    echo "Missing ELF or source — run pingpong first"
    exit 1
fi

echo "=== Building Stage 1 ELF ==="
PIPE=/tmp/s1pipe-$$
RAW=/tmp/s1raw-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!

for i in $(seq 1 40); do
    grep -qa 'READY' "$RAW" 2>/dev/null && break
    sleep 0.5
done
echo "  READY received"

# Send BINARY + source + EOF all in one write (like pingpong.sh)
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

prev=0; stable=0
for i in $(seq 1 90); do
    sleep 2
    cur=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
        stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
    else stable=0; fi
    prev=$cur
    kill -0 $QEMU 2>/dev/null || break
done

kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

python3 -c "
import sys
data = open('$RAW', 'rb').read()
idx = data.find(b'SIZE:')
if idx < 0:
    print('FAIL: no SIZE marker'); sys.exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
binary = data[nl+1:nl+1+size]
if len(binary) != size:
    print(f'FAIL: expected {size}, got {len(binary)}'); sys.exit(1)
open('$STAGE1_ELF', 'wb').write(binary)
rest = data[nl+1+size:]
for line in rest.split(b'\x0a'):
    line = line.strip()
    if line: print('  ' + line.decode('ascii', errors='replace'))
print(f'  Stage 1 ELF: {len(binary)} bytes')
"
rm -f "$RAW"

[ ! -f "$STAGE1_ELF" ] && { echo "Failed to build Stage 1 ELF"; exit 1; }

echo ""
echo "=== Testing Stage 1 ELF: does it boot? ==="
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1_ELF" \
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -3
echo ""

echo "=== Testing Stage 1 ELF: small program in binary mode ==="
PIPE2=/tmp/s1test-pipe-$$
RAW2=/tmp/s1test-raw-$$
rm -f "$PIPE2" "$RAW2"
mkfifo "$PIPE2"
sleep 999 > "$PIPE2" &
HOLDER2=$!

timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null &
QEMU2=$!

ready=0
for i in $(seq 1 40); do
    grep -qa 'READY' "$RAW2" 2>/dev/null && { ready=1; break; }
    sleep 0.5
    kill -0 $QEMU2 2>/dev/null || break
done

if [ "$ready" -eq 0 ]; then
    echo "FAIL: Stage 1 ELF did not print READY"
    echo "Raw output:"
    head -c 500 "$RAW2" 2>/dev/null | xxd | head -15
    kill $QEMU2 2>/dev/null; kill $HOLDER2 2>/dev/null; wait 2>/dev/null
    rm -f "$PIPE2" "$RAW2" "$STAGE1_ELF"
    exit 1
fi
echo "  Stage 1 ELF booted OK"

(printf 'BINARY\nChapter: Test\n\n  main : Integer\n  main = 42\n\x04') > "$PIPE2" &

sleep 15
if grep -qa 'SIZE:' "$RAW2"; then
    echo "  Binary mode works! $(grep 'SIZE:' "$RAW2")"
else
    echo "  FAIL: no SIZE marker — binary compilation failed on Stage 1 ELF"
    echo "  Raw output (text):"
    strings "$RAW2" | head -20
fi

kill $QEMU2 2>/dev/null; kill $HOLDER2 2>/dev/null; wait 2>/dev/null
rm -f "$PIPE2" "$RAW2" "$STAGE1_ELF"
