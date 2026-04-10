#!/bin/bash
# Test KVM with fixed MSR addresses — Stage 0 rebuilt
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
BM_SMALL=/tmp/bm-small-msr2.elf

echo "Stage 0 ELF: $(wc -c < "$BM_ELF") bytes"
echo ""
echo "=== Generating self-hosted small ELF ==="
PIPE=/tmp/msr2-pipe-$$
RAW=/tmp/msr2-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
if ! grep -qa 'READY' "$RAW" 2>/dev/null; then
    echo "FAIL: no READY from Stage 0"
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
    exit 1
fi
echo "  READY from Stage 0"
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0: print('FAIL: no SIZE marker'); exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

# Verify MSR addresses in the new ELF
echo ""
echo "=== Checking MSR addresses in generated ELF ==="
python3 -c "
import struct
data = open('$BM_SMALL','rb').read()
# Look for movabs rcx, imm64 patterns (48 B9 xx xx xx xx xx xx xx xx) with MSR-like values
# Or mov rcx, imm32 (48 C7 C1 xx xx xx xx)
i = 0
while i < len(data) - 10:
    if data[i] == 0x48 and data[i+1] == 0xB9:
        val = struct.unpack_from('<Q', data, i+2)[0]
        if 0xC0000000 <= val <= 0xC00000FF:
            print(f'  offset 0x{i:x}: movabs rcx, 0x{val:x}')
        i += 10; continue
    if data[i] == 0x48 and data[i+1] == 0xC7 and data[i+2] == 0xC1:
        val = struct.unpack_from('<i', data, i+3)[0] & 0xFFFFFFFF
        if 0xC0000000 <= val <= 0xC00000FF:
            print(f'  offset 0x{i:x}: mov rcx, 0x{val:x}')
        i += 7; continue
    i += 1
"

echo ""
echo "=== KVM boot test ==="
RAW2=/tmp/msr2-kvm-$$
timeout 8 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW2" 2>/dev/null < /dev/null
SIZE=$(wc -c < "$RAW2" 2>/dev/null)
echo "  KVM output: $SIZE bytes"
if [ "$SIZE" -gt 0 ]; then
    head -c 200 "$RAW2" 2>/dev/null
    echo ""
else
    echo "  (no output)"
fi
rm -f "$RAW2"
