#!/bin/bash
# Build Stage 1 ELF and compare with reference ELF
REPO=/mnt/d/Projects/NewRepository-cam
REF_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
STAGE1_ELF=/tmp/stage1-compare-elf

echo "=== Building Stage 1 ELF ==="
PIPE=/tmp/cmp-pipe-$$
RAW=/tmp/cmp-raw-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
prev=0; stable=0
for i in $(seq 1 90); do
    sleep 2; cur=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ] && { stable=$((stable+1)); [ "$stable" -ge 2 ] && break; } || stable=0
    prev=$cur; kill -0 $QEMU 2>/dev/null || break
done
kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
python3 -c "
data = open('$RAW', 'rb').read()
idx = data.find(b'SIZE:')
if idx < 0: print('FAIL: no SIZE'); exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
binary = data[nl+1:nl+1+size]
open('$STAGE1_ELF', 'wb').write(binary)
print(f'Stage 1 ELF: {len(binary)} bytes')
"
rm -f "$RAW"

echo ""
echo "=== Comparing ELF headers ==="
echo "Reference ELF: $(wc -c < "$REF_ELF") bytes"
echo "Stage 1 ELF:   $(wc -c < "$STAGE1_ELF") bytes"

echo ""
echo "--- Reference ELF header ---"
xxd "$REF_ELF" | head -6
echo ""
echo "--- Stage 1 ELF header ---"
xxd "$STAGE1_ELF" | head -6

echo ""
echo "=== Comparing entry points ==="
readelf -h "$REF_ELF" 2>/dev/null | grep -E "Entry|Type|Machine"
echo "---"
readelf -h "$STAGE1_ELF" 2>/dev/null | grep -E "Entry|Type|Machine"

echo ""
echo "=== Comparing program headers ==="
readelf -l "$REF_ELF" 2>/dev/null | head -15
echo "---"
readelf -l "$STAGE1_ELF" 2>/dev/null | head -15

echo ""
echo "=== First divergence ==="
cmp -l "$REF_ELF" "$STAGE1_ELF" 2>/dev/null | head -20

rm -f "$STAGE1_ELF"
