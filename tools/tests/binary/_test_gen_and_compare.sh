#!/bin/bash
# Generate reference small ELF and new self-hosted small ELF, compare and boot
REPO=/mnt/d/Projects/NewRepository-cam
DOTNET=$(command -v dotnet || echo "/mnt/c/Program Files/dotnet/dotnet")

echo "=== Step 1: Generate reference ELF for main=42 ==="
# Write small test program
TESTFILE=/tmp/small-test.codex
cat > "$TESTFILE" << 'CODE'
Chapter: SmallTest

  main : Integer
  main = 42
CODE

REF_SMALL=/tmp/ref-small-new.elf
cd "$REPO"
"$DOTNET" run --project tools/Codex.Cli -- build "$TESTFILE" --target x86-64-bare --output "$REF_SMALL" 2>&1 | tail -5
echo "  Reference ELF: $(wc -c < "$REF_SMALL" 2>/dev/null || echo 'FAILED') bytes"

echo ""
echo "=== Step 2: Generate self-hosted ELF for main=42 ==="
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
BM_SMALL=/tmp/bm-small-new.elf
PIPE=/tmp/gen-pipe-$$
RAW=/tmp/gen-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
if idx < 0:
    print('FAIL: no SIZE marker'); exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

echo ""
echo "=== Step 3: Page table entry comparison ==="
python3 << 'PYEOF'
import struct

def count_page_entries(path, label):
    data = open(path, 'rb').read()
    # Count instructions that load rax with values ending in 0x83 (page table entries)
    count = 0
    i = 0
    while i < len(data) - 10:
        # mov rax, imm32: 48 C7 C0 xx xx xx xx
        if data[i] == 0x48 and data[i+1] == 0xC7 and data[i+2] == 0xC0:
            val = struct.unpack_from('<i', data, i+3)[0]
            if val >= 0 and val & 0xFF == 0x83:
                count += 1
            i += 7
            continue
        # movabs rax, imm64: 48 B8 xx xx xx xx xx xx xx xx
        if data[i] == 0x48 and data[i+1] == 0xB8:
            val = struct.unpack_from('<Q', data, i+2)[0]
            if val & 0xFF == 0x83 and val < 0x40000000:
                count += 1
            i += 10
            continue
        i += 1
    print(f"  {label}: {count} page table entries (0xXX83), file={len(data)} bytes")

count_page_entries('/tmp/ref-small-new.elf', 'Reference')
count_page_entries('/tmp/bm-small-new.elf', 'Self-hosted')
count_page_entries('/tmp/bm-small.elf', 'Old self-hosted (before fix)')
PYEOF

echo ""
echo "=== Step 4: Boot reference ELF ==="
RAW2=/tmp/boot-ref-raw-$$
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$REF_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW2" 2>/dev/null < /dev/null &
QEMU2=$!; sleep 5; kill $QEMU2 2>/dev/null; wait 2>/dev/null
echo "  Reference output: $(wc -c < "$RAW2") bytes"
[ "$(wc -c < "$RAW2")" -gt 0 ] && cat "$RAW2" | tr '\0' '.'
echo ""

echo ""
echo "=== Step 5: Boot self-hosted ELF ==="
RAW3=/tmp/boot-bm-raw-$$
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW3" 2>/dev/null < /dev/null &
QEMU3=$!; sleep 5; kill $QEMU3 2>/dev/null; wait 2>/dev/null
echo "  Self-hosted output: $(wc -c < "$RAW3") bytes"
[ "$(wc -c < "$RAW3")" -gt 0 ] && cat "$RAW3" | tr '\0' '.'
echo ""

rm -f "$RAW2" "$RAW3"
echo ""
echo "=== Done ==="
