#!/bin/bash
# Test: compile the FULL lexer + a main that calls tokenize
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam/Codex.Codex

COMBINED=/tmp/lf-src-$$
# Include type deps + lexer + test main
cat "$REPO/Core/Name.codex" > "$COMBINED"
cat "$REPO/Types/CodexType.codex" >> "$COMBINED"
cat "$REPO/Syntax/TokenKind.codex" >> "$COMBINED"
cat "$REPO/Syntax/Token.codex" >> "$COMBINED"
cat "$REPO/Syntax/Lexer.codex" >> "$COMBINED"
# Add a main that calls tokenize
cat >> "$COMBINED" << 'EOF'

Chapter: Test

Section: Main

  cites Lexer (tokenize)

  main : Integer
  main = list-length (tokenize "x = 1")
EOF

echo "Source: $(wc -c < "$COMBINED") bytes"

PIPE=/tmp/lf-p-$$; RAW=/tmp/lf-r-$$; ELF=/tmp/lf-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 60 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
if ! grep -qa 'SIZE:' "$RAW"; then
    echo "COMPILE FAIL"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW" "$COMBINED"; exit 1; fi
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
echo "ELF: $(wc -c < "$ELF") bytes"
rm -f "$RAW" "$COMBINED"

OUT=$(timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -c 100)
echo "Output: $(echo "$OUT" | head -3 | tr '\n' '|')"
rm -f "$ELF"
