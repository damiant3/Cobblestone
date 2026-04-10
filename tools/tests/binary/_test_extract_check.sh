#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
PIPE=/tmp/ec-p-$$; RAW=/tmp/ec-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

echo "Raw output: $(wc -c < "$RAW") bytes"
echo "SIZE line: $(grep -a 'SIZE:' "$RAW" | head -1)"
echo "grep -boa offset: $(grep -boa 'SIZE:' "$RAW" | head -1)"

# Check: how many SIZE: matches are there?
echo "SIZE: match count: $(grep -c 'SIZE:' "$RAW")"
echo "SIZE: byte offsets:"
grep -boa 'SIZE:' "$RAW" | head -5

# The REAL size line should be a text line, not inside binary data
# Let's find it by looking at the text portion before binary data
echo ""
echo "First 20 bytes after READY:"
dd if="$RAW" bs=1 skip=6 count=20 2>/dev/null | xxd

rm -f "$RAW"
