#!/bin/bash
# Test KVM with fixed MSR addresses
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"

echo "=== Rebuilding Stage 0 ==="
cd "$REPO"
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project tools/Codex.Cli -- build Codex.Codex/main.codex --target x86-64-bare --output build-output/bare-metal/Codex.Codex.elf 2>&1 | tail -3

echo ""
echo "=== Generating self-hosted small ELF ==="
PIPE=/tmp/msr-pipe-$$
RAW=/tmp/msr-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$BM_ELF" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: SmallTest\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE" &
sleep 15
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
BM_SMALL=/tmp/bm-small-msr.elf
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

echo ""
echo "=== KVM boot test ==="
RAW2=/tmp/msr-kvm-$$
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW2" 2>/dev/null < /dev/null
SIZE=$(wc -c < "$RAW2" 2>/dev/null)
echo "  KVM output: $SIZE bytes"
head -c 200 "$RAW2" 2>/dev/null
echo ""

echo ""
echo "=== TCG boot test (control) ==="
RAW3=/tmp/msr-tcg-$$
timeout 5 qemu-system-x86_64 -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$RAW3" 2>/dev/null < /dev/null
SIZE3=$(wc -c < "$RAW3" 2>/dev/null)
echo "  TCG output: $SIZE3 bytes"
head -c 200 "$RAW3" 2>/dev/null
echo ""

rm -f "$RAW2" "$RAW3"
