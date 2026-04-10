#!/bin/bash
# Run mini-bootstrap.codex through the binary pipeline
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE=$(cat "$REPO/samples/mini-bootstrap.codex")

echo "=== Compiling mini-bootstrap via bare-metal BINARY mode ==="
PIPE=/tmp/mb-pipe-$$
RAW=/tmp/mb-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf "BINARY\n%s\x04" "$SOURCE" > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

if grep -qa 'SIZE:' "$RAW"; then
    ELF=/tmp/mb-test.elf
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$ELF','wb').write(elf)
print(f'  ELF: {size} bytes')
"
    echo ""
    echo "=== Booting mini-bootstrap ELF ==="
    RAW2=/tmp/mb-boot-$$
    timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        > "$RAW2" 2>/dev/null < /dev/null
    echo "  Output: $(wc -c < "$RAW2") bytes"
    head -c 300 "$RAW2"
    echo ""
    rm -f "$RAW2" "$ELF"
else
    echo "  FAIL: no SIZE marker"
    echo "  First 300 bytes:"
    head -c 300 "$RAW" | cat -v
fi
rm -f "$RAW"

echo ""
echo "=== Reference comparison ==="
REF_ELF=/tmp/mb-ref.elf
cd "$REPO"
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project tools/Codex.Cli -- build samples/mini-bootstrap.codex --target x86-64-bare --output "$REF_ELF" 2>&1 | tail -2
echo "  Ref ELF: $(wc -c < "$REF_ELF" 2>/dev/null) bytes"
RAW3=/tmp/mb-ref-boot-$$
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW3" 2>/dev/null < /dev/null
echo "  Ref output: $(wc -c < "$RAW3") bytes"
head -c 300 "$RAW3"
echo ""
rm -f "$RAW3" "$REF_ELF"
