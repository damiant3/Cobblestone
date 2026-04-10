#!/bin/bash
REPO=/mnt/d/Projects/NewRepository-cam
REF_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
STAGE1_ELF=/tmp/tramp-cmp-elf

# Build Stage 1 ELF
PIPE=/tmp/tcmp-pipe-$$
RAW=/tmp/tcmp-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
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
data = open('$RAW','rb').read(); idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx); size = int(data[idx+5:nl])
open('$STAGE1_ELF','wb').write(data[nl+1:nl+1+size])
print(f'Stage 1 ELF: {size} bytes')
"
rm -f "$RAW"

echo ""
echo "=== Trampoline comparison (first 512 bytes of text @ offset 0x90) ==="
echo "--- Reference ---"
dd if="$REF_ELF" bs=1 skip=144 count=512 2>/dev/null | xxd | head -32
echo "--- Stage 1 ---"
dd if="$STAGE1_ELF" bs=1 skip=144 count=512 2>/dev/null | xxd | head -32

echo ""
echo "=== Trampoline byte diff ==="
dd if="$REF_ELF" bs=1 skip=144 count=512 2>/dev/null > /tmp/ref-tramp
dd if="$STAGE1_ELF" bs=1 skip=144 count=512 2>/dev/null > /tmp/s1-tramp
cmp -l /tmp/ref-tramp /tmp/s1-tramp 2>/dev/null | head -20
if cmp -s /tmp/ref-tramp /tmp/s1-tramp; then
    echo "IDENTICAL — trampoline matches"
else
    echo "DIFFERENT — trampoline diverges"
fi

rm -f /tmp/ref-tramp /tmp/s1-tramp "$STAGE1_ELF"
