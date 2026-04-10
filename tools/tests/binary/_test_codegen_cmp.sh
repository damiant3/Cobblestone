#!/bin/bash
# Compare codegen: reference compiler vs bare-metal self-hosted compiler
# Both compile the same small program to x86-64-bare ELF
REPO=/mnt/d/Projects/NewRepository-cam
REF_ELF="$REPO/build-output/bare-metal/Codex.Codex.elf"
WINREPO="D:/Projects/NewRepository-cam"

SMALL_SOURCE=$(mktemp)
cat > "$SMALL_SOURCE" << 'ENDCODEX'
Chapter: SmallTest

  main : Integer
  main = 42
ENDCODEX

echo "=== Reference compiler output ==="
REF_OUT=$(mktemp -d)
dotnet run --project "$WINREPO/tools/Codex.Cli" -- build "$SMALL_SOURCE" --target x86-64-bare --output-dir "$REF_OUT" 2>&1 | tail -3
REF_SMALL=$(find "$REF_OUT" -name "*.elf" | head -1)
echo "Reference ELF: $(wc -c < "$REF_SMALL") bytes"

echo ""
echo "=== Bare-metal self-hosted compiler output ==="
PIPE=/tmp/cg-pipe-$$
RAW=/tmp/cg-raw-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & HOLDER=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$REF_ELF" \
    -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null & QEMU=$!
for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SMALL_SOURCE"; printf '\x04') > "$PIPE" &
sleep 15
kill $QEMU 2>/dev/null; kill $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

BM_SMALL=/tmp/cg-bm-elf-$$
python3 -c "
data = open('$RAW','rb').read(); idx = data.find(b'SIZE:')
if idx < 0: print('FAIL: no SIZE'); exit(1)
nl = data.index(b'\x0a', idx); size = int(data[idx+5:nl])
open('$BM_SMALL','wb').write(data[nl+1:nl+1+size])
print(f'Bare-metal ELF: {size} bytes')
"
rm -f "$RAW"

echo ""
echo "=== Size comparison ==="
echo "Reference:   $(wc -c < "$REF_SMALL") bytes"
echo "Bare-metal:  $(wc -c < "$BM_SMALL") bytes"

echo ""
echo "=== ELF header comparison ==="
echo "-- Ref --"
readelf -h "$REF_SMALL" 2>/dev/null | grep -E "Entry|Type|Machine"
echo "-- BM --"
readelf -h "$BM_SMALL" 2>/dev/null | grep -E "Entry|Type|Machine"

echo ""
echo "=== Text section comparison (skip first 0x90 header, show 256 bytes after trampoline at ~0x200) ==="
echo "-- Ref (offset 0x290) --"
dd if="$REF_SMALL" bs=1 skip=$((0x290)) count=128 2>/dev/null | xxd
echo "-- BM (offset 0x290) --"
dd if="$BM_SMALL" bs=1 skip=$((0x290)) count=128 2>/dev/null | xxd

echo ""
echo "=== Byte diff ==="
cmp -l "$REF_SMALL" "$BM_SMALL" 2>/dev/null | head -30
if cmp -s "$REF_SMALL" "$BM_SMALL"; then
    echo "IDENTICAL"
else
    echo "DIFFERENT ($(cmp -l "$REF_SMALL" "$BM_SMALL" 2>/dev/null | wc -l) byte differences)"
fi

echo ""
echo "=== Does bare-metal ELF boot? ==="
timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$BM_SMALL" \
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -5

rm -f "$SMALL_SOURCE" "$BM_SMALL"
rm -rf "$REF_OUT"
