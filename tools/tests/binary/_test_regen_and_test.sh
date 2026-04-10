#!/bin/bash
# Regenerate Stage 1 from current Stage 0, then test it
REPO=/mnt/d/Projects/NewRepository-cam
STAGE0="$REPO/build-output/bare-metal/Codex.Codex.elf"
SOURCE="$REPO/build-output/bare-metal/source.codex"
STAGE1="$REPO/build-output/bare-metal/stage1.elf"

# Dump source
cd "$REPO"
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare --output-dir build-output/bare-metal 2>&1 | tail -2
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project tools/Codex.Cli -- dump-source Codex.Codex --output "$SOURCE" 2>&1 | tail -2

echo "Stage 0: $(wc -c < "$STAGE0") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"

echo ""
echo "=== Generating Stage 1 ELF ==="
PIPE=/tmp/rg-pipe-$$
RAW=/tmp/rg-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
echo "  READY from Stage 0"
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
# Wait for SIZE marker
for i in $(seq 1 60); do
    sleep 2
    grep -qa 'SIZE:' "$RAW" && break
    kill -0 $QEMU 2>/dev/null || break
done
kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then
    python3 -c "
data = open('$RAW','rb').read()
idx = data.find(b'SIZE:')
nl = data.index(b'\x0a', idx)
size = int(data[idx+5:nl])
elf = data[nl+1:nl+1+size]
open('$STAGE1','wb').write(elf)
print(f'  Stage 1: {size} bytes')
"
else
    echo "  FAIL: no SIZE marker"
    echo "  Output: $(wc -c < "$RAW") bytes"
    head -c 100 "$RAW" | cat -v
    rm -f "$RAW"; exit 1
fi
rm -f "$RAW"

echo ""
echo "=== Testing Stage 1: compile main=42 ==="
PIPE2=/tmp/rg2-pipe-$$
RAW2=/tmp/rg2-raw-$$
rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
sleep 999 > "$PIPE2" & HOLDER2=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE2" > "$RAW2" 2>/dev/null & QEMU2=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
echo "  Stage 1 READY: $(grep -c 'READY' "$RAW2")"
printf 'BINARY\nChapter: Test\n\n  main : Integer\n  main = 42\n\x04' > "$PIPE2" &
for i in $(seq 1 15); do
    sleep 2
    grep -qa 'SIZE:' "$RAW2" && echo "  SIZE: found!" && break
    kill -0 $QEMU2 2>/dev/null || { echo "  QEMU exited at ${i}x2s"; break; }
done
echo "  Output: $(wc -c < "$RAW2") bytes"
head -c 200 "$RAW2" | cat -v
echo ""
kill $QEMU2 $HOLDER2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$RAW2"
