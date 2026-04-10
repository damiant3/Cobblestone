#!/bin/bash
# Compare self-hosted ELF vs reference ELF for main=42
REPO=/mnt/d/Projects/NewRepository-cam
REF_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
REF_SMALL=/tmp/codegen-test/small.elf
BM_SMALL=/tmp/cg-bm-small

# Build self-hosted ELF for main=42
PIPE=/tmp/cg2-pipe-$$
RAW=/tmp/cg2-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04') > "$PIPE" &
sleep 15
kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data = open('$RAW','rb').read(); idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx); size = int(data[idx+5:nl])
open('$BM_SMALL','wb').write(data[nl+1:nl+1+size])
print(f'Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

echo "Reference ELF:   $(wc -c < "$REF_SMALL") bytes"
echo ""

# Compare program headers
echo "=== Program Headers ==="
echo "-- Reference --"
readelf -l "$REF_SMALL" 2>/dev/null | grep -A1 LOAD
echo "-- Self-hosted --"
readelf -l "$BM_SMALL" 2>/dev/null | grep -A1 LOAD

# Compare function layout by looking for known patterns
echo ""
echo "=== Looking for serial init (0xe9 0xfa = out dx,al pattern) ==="
echo "Reference:"
xxd "$REF_SMALL" | grep -i "e6 f9\|e6 fa\|e6 fb\|e6 fc\|e6 fd" | head -5
echo "Self-hosted:"
xxd "$BM_SMALL" | grep -i "e6 f9\|e6 fa\|e6 fb\|e6 fc\|e6 fd" | head -5

# Compare the code right after the trampoline (runtime helpers start)
echo ""
echo "=== Code after trampoline (offset 0x1a0 from text start) ==="
echo "-- Reference --"
dd if="$REF_SMALL" bs=1 skip=$((0x90 + 0x1a0)) count=64 2>/dev/null | xxd
echo "-- Self-hosted --"
dd if="$BM_SMALL" bs=1 skip=$((0x90 + 0x1a0)) count=64 2>/dev/null | xxd

# Find "READY" string in rodata
echo ""
echo "=== READY string search ==="
echo "Reference:"
strings "$REF_SMALL" | grep -i "READY"
echo "Self-hosted:"
strings "$BM_SMALL" | grep -i "READY"

# Disassemble first few functions after trampoline
echo ""
echo "=== Disassembly after trampoline (first 200 bytes of 64-bit code) ==="
# Find the far jump target to know where 64-bit code starts
REF_ENTRY=$(python3 -c "
data = open('$REF_SMALL','rb').read()
# Far jump at trampoline offset 0xa4: EA xx xx xx xx 08 00
import struct
off = 0x90 + 0xa4
target = struct.unpack_from('<I', data, off+1)[0]
print(hex(target))
")
BM_ENTRY=$(python3 -c "
data = open('$BM_SMALL','rb').read()
off = 0x90 + 0xa4
target = struct.unpack_from('<I', data, off+1)[0]
print(hex(target))
")
echo "Reference 64-bit entry: $REF_ENTRY"
echo "Self-hosted 64-bit entry: $BM_ENTRY"

REF_FILE_OFF=$(python3 -c "print(hex($REF_ENTRY - 0x100000 + 0x90))")
BM_FILE_OFF=$(python3 -c "print(hex($BM_ENTRY - 0x100000 + 0x90))")
echo "Reference file offset: $REF_FILE_OFF"
echo "Self-hosted file offset: $BM_FILE_OFF"

echo ""
echo "-- Reference 64-bit code --"
objdump -D -b binary -m i386:x86-64 --start-address=0 --stop-address=200 \
    <(dd if="$REF_SMALL" bs=1 skip=$((REF_FILE_OFF)) count=200 2>/dev/null) 2>/dev/null | head -30

echo "-- Self-hosted 64-bit code --"
objdump -D -b binary -m i386:x86-64 --start-address=0 --stop-address=200 \
    <(dd if="$BM_SMALL" bs=1 skip=$((BM_FILE_OFF)) count=200 2>/dev/null) 2>/dev/null | head -30

rm -f "$BM_SMALL"
