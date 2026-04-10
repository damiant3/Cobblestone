#!/bin/bash
# Generate self-hosted ELF for main=42 and compare with reference
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
REF_SMALL="$REPO/build-output/ref-small.elf"
BM_SMALL=/tmp/bm-small-fixed.elf

echo "=== Generating self-hosted ELF for main=42 ==="
PIPE=/tmp/gen2-pipe-$$
RAW=/tmp/gen2-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!

for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done

if ! grep -qa 'READY' "$RAW"; then
    echo "FAIL: no READY"
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; exit 1
fi
echo "  READY received"

printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0:
    print('FAIL: no SIZE marker')
    # Show raw output
    print('Raw:', data[:200])
    exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

echo "  Reference ELF:   $(wc -c < "$REF_SMALL") bytes"
echo "  Old BM ELF:      $(wc -c < /tmp/bm-small.elf) bytes"
echo ""

echo "=== Page table entry counts ==="
python3 << 'PYEOF'
import struct

def count_page_entries(path, label):
    try:
        data = open(path, 'rb').read()
    except:
        print(f"  {label}: FILE NOT FOUND")
        return
    count = 0
    i = 0
    while i < len(data) - 10:
        if data[i] == 0x48 and data[i+1] == 0xC7 and data[i+2] == 0xC0:
            val = struct.unpack_from('<i', data, i+3)[0]
            if val >= 0 and val & 0xFF == 0x83:
                count += 1
            i += 7; continue
        if data[i] == 0x48 and data[i+1] == 0xB8:
            val = struct.unpack_from('<Q', data, i+2)[0]
            if val & 0xFF == 0x83 and val < 0x40000000:
                count += 1
            i += 10; continue
        i += 1
    print(f"  {label}: {count} page table entries, {len(data)} bytes")

count_page_entries('/mnt/d/Projects/NewRepository-cam/build-output/ref-small.elf', 'Reference')
count_page_entries('/tmp/bm-small-fixed.elf', 'Self-hosted (fixed)')
count_page_entries('/tmp/bm-small.elf', 'Self-hosted (old)')
PYEOF

echo ""
echo "=== Boot test: reference ==="
RAW2=/tmp/bt-ref-$$
timeout 8 qemu-system-x86_64 -enable-kvm -kernel "$REF_SMALL" \
    -serial stdio -display none -no-reboot -m 512 > "$RAW2" 2>/dev/null < /dev/null & Q=$!
sleep 5; kill $Q 2>/dev/null; wait 2>/dev/null
echo "  $(wc -c < "$RAW2") bytes: $(cat "$RAW2" | tr '\0' '.' 2>/dev/null)"

echo ""
echo "=== Boot test: self-hosted (fixed) ==="
RAW3=/tmp/bt-bm-$$
timeout 8 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 > "$RAW3" 2>/dev/null < /dev/null & Q=$!
sleep 5; kill $Q 2>/dev/null; wait 2>/dev/null
echo "  $(wc -c < "$RAW3") bytes: $(cat "$RAW3" | tr '\0' '.' 2>/dev/null)"

echo ""
echo "=== Boot test: self-hosted (old, before fix) ==="
RAW4=/tmp/bt-old-$$
timeout 8 qemu-system-x86_64 -enable-kvm -kernel /tmp/bm-small.elf \
    -serial stdio -display none -no-reboot -m 512 > "$RAW4" 2>/dev/null < /dev/null & Q=$!
sleep 5; kill $Q 2>/dev/null; wait 2>/dev/null
echo "  $(wc -c < "$RAW4") bytes: $(cat "$RAW4" | tr '\0' '.' 2>/dev/null)"

rm -f "$RAW2" "$RAW3" "$RAW4"
