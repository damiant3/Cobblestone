#!/bin/bash
# Build and boot a small ELF (main=42) from the self-hosted compiler
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
REF_SMALL="$REPO/build-output/ref-small.elf"
BM_SMALL=/tmp/bm-small-fixed.elf

echo "=== Generating self-hosted ELF for main=42 ==="
PIPE=/tmp/small-pipe-$$
RAW=/tmp/small-raw-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!

for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done

if ! grep -qa 'READY' "$RAW" 2>/dev/null; then
    echo "FAIL: no READY from bare-metal compiler"
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
    exit 1
fi
echo "  READY from bare-metal compiler"

printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0:
    print('FAIL: no SIZE: marker')
    import sys; sys.exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

echo "  Reference ELF: $(wc -c < "$REF_SMALL") bytes"

echo ""
echo "=== Comparing page table entries ==="
python3 -c "
import struct

def parse_elf32(path):
    data = open(path, 'rb').read()
    e_phoff = struct.unpack_from('<I', data, 0x1c)[0]
    e_phnum = struct.unpack_from('<H', data, 0x2c)[0]
    e_phentsize = struct.unpack_from('<H', data, 0x2a)[0]
    for j in range(e_phnum):
        ph_off = e_phoff + j * e_phentsize
        p_type = struct.unpack_from('<I', data, ph_off)[0]
        if p_type == 1:
            return data, struct.unpack_from('<I', data, ph_off+4)[0], struct.unpack_from('<I', data, ph_off+8)[0], struct.unpack_from('<I', data, ph_off+16)[0]
    return data, 0, 0, 0

for label, path in [('Reference', '$REF_SMALL'), ('Self-hosted', '$BM_SMALL')]:
    data, p_offset, p_vaddr, p_filesz = parse_elf32(path)
    print(f'\n{label}: {len(data)} bytes')
    print(f'  LOAD: offset=0x{p_offset:x} vaddr=0x{p_vaddr:x} filesz=0x{p_filesz:x}')

    # Find page table entries at PD address (pml4=0x8000, pd=0x8000+0x2000=0xa000)
    # These are written as mov instructions in the code
    # Count mov instructions that write 0xXX83 values (page table entries)
    # Search for the pattern: load rax with value ending in 0x83, then store
    count = 0
    # Look for 'li rax, xxx83' pattern in text
    # li for values > 0x7FFFFFFF uses mov-ri64: REX.W 0xB8+reg [8 bytes imm]
    # li for values 0..0x7FFFFFFF uses mov-ri32: REX.W 0xC7 modrm [4 bytes imm]
    i = p_offset
    end = p_offset + p_filesz
    while i < end - 6:
        # Check for movabs rax, imm64 (48 B8 xx xx xx xx xx xx xx xx)
        if data[i] == 0x48 and i+1 < end and data[i+1] == 0xB8:
            val = struct.unpack_from('<Q', data, i+2)[0]
            if val & 0xFF == 0x83 and val < 0x40000000:
                count += 1
            i += 10
            continue
        # Check for mov rax, imm32 (48 C7 C0 xx xx xx xx)
        if data[i] == 0x48 and i+2 < end and data[i+1] == 0xC7 and data[i+2] == 0xC0:
            val = struct.unpack_from('<i', data, i+3)[0]
            if val & 0xFF == 0x83 and val >= 0:
                count += 1
            i += 7
            continue
        i += 1
    print(f'  Page table entries (0xXX83 values): {count}')
"

echo ""
echo "=== Booting self-hosted ELF ==="
PIPE2=/tmp/small-boot-pipe-$$
RAW2=/tmp/small-boot-raw-$$
rm -f "$PIPE2" "$RAW2"
mkfifo "$PIPE2"
sleep 999 > "$PIPE2" &
HOLDER2=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null &
QEMU2=$!
sleep 5
kill $QEMU2 $HOLDER2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"
SIZE=$(wc -c < "$RAW2" 2>/dev/null || echo 0)
echo "  Output: $SIZE bytes"
if [ "$SIZE" -gt 0 ]; then
    cat "$RAW2" | tr '\0' '.'
    echo ""
else
    echo "  (no output — still broken)"
fi

echo ""
echo "=== Booting reference ELF ==="
PIPE3=/tmp/small-ref-pipe-$$
RAW3=/tmp/small-ref-raw-$$
rm -f "$PIPE3" "$RAW3"
mkfifo "$PIPE3"
sleep 999 > "$PIPE3" &
HOLDER3=$!
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$REF_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE3" > "$RAW3" 2>/dev/null &
QEMU3=$!
sleep 5
kill $QEMU3 $HOLDER3 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE3"
SIZE3=$(wc -c < "$RAW3" 2>/dev/null || echo 0)
echo "  Output: $SIZE3 bytes"
if [ "$SIZE3" -gt 0 ]; then
    cat "$RAW3" | tr '\0' '.'
    echo ""
fi

rm -f "$RAW2" "$RAW3"
