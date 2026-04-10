#!/bin/bash
# Quick test: generate new small ELF and boot in TCG mode
REPO=/mnt/d/Projects/NewRepository-cam
BM_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
BM_SMALL=/tmp/bm-small-v2.elf

echo "=== Rebuilding Stage 0 ELF ==="
cd "$REPO"
# Use Windows dotnet via path
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" run --project tools/Codex.Cli -- build tools/_test_small_input.codex --target x86-64-bare --output build-output/ref-small-v2.elf 2>&1 | tail -2
echo "  Ref ELF: $(wc -c < "$REPO/build-output/ref-small-v2.elf" 2>/dev/null) bytes"

echo ""
echo "=== Building bare-metal compiler ==="
"$DOTNET" run --project tools/Codex.Cli -- build Codex.Codex/main.codex --target x86-64-bare --output build-output/bare-metal/Codex.Codex.elf 2>&1 | tail -3
echo "  Stage 0 ELF: $(wc -c < "$BM_ELF" 2>/dev/null) bytes"

echo ""
echo "=== Generating self-hosted ELF for main=42 ==="
PIPE=/tmp/qb-pipe-$$
RAW=/tmp/qb-raw-$$
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
if idx < 0: print('FAIL: no SIZE marker'); exit(1)
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$BM_SMALL','wb').write(elf)
print(f'  Self-hosted ELF: {size} bytes')
"
rm -f "$RAW"

echo ""
echo "=== TCG boot test: self-hosted ELF ==="
SERIAL=/tmp/qb-serial-$$
timeout 5 qemu-system-x86_64 -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$SERIAL" 2>/dev/null < /dev/null
echo "  Serial output (first 500 bytes):"
head -c 500 "$SERIAL" 2>/dev/null
echo ""

echo ""
echo "=== TCG boot test: reference ELF ==="
SERIAL2=/tmp/qb-serial2-$$
timeout 5 qemu-system-x86_64 -kernel "$REPO/build-output/ref-small-v2.elf" \
    -serial stdio -display none -no-reboot -m 512 \
    > "$SERIAL2" 2>/dev/null < /dev/null
echo "  Serial output (first 500 bytes):"
head -c 500 "$SERIAL2" 2>/dev/null
echo ""

rm -f "$SERIAL" "$SERIAL2"
