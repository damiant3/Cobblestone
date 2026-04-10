#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam/Codex.Codex

COMBINED=/tmp/st-src-$$
cat "$REPO/Core/Name.codex" > "$COMBINED"
cat "$REPO/Types/CodexType.codex" >> "$COMBINED"
cat "$REPO/Syntax/TokenKind.codex" >> "$COMBINED"
cat "$REPO/Syntax/Token.codex" >> "$COMBINED"
cat "$REPO/Syntax/Lexer.codex" >> "$COMBINED"
# Override scan-token with a stripped version
cat >> "$COMBINED" << 'EOF'

Chapter: Test

Section: Main

  cites Lexer (make-lex-state, is-at-end, peek-code, skip-spaces, advance-char, is-letter-code, scan-ident-rest, extract-text, classify-word, make-token)

  my-scan : LexState -> LexResult
  my-scan (st) =
   let s = skip-spaces st
   in if is-at-end s then LexEnd
   else let c = peek-code s
   in if is-letter-code c then let start = s.offset
     in let after = scan-ident-rest (advance-char s)
     in let word = extract-text s start after
     in LexToken (make-token (classify-word word) word s) after
   else LexToken (make-token ErrorToken "?" s) (advance-char s)

  main : Integer
  main = when my-scan (make-lex-state "x")
    if LexToken (tok) (next) -> text-length (tok.text)
    if LexEnd -> 0
EOF

PIPE=/tmp/st-p-$$; RAW=/tmp/st-r-$$; ELF=/tmp/st-$$.elf
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#SZ} + 1))
NEEDED=$((BSTART + SZ))
while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
echo "ELF: $(wc -c < "$ELF") bytes"
rm -f "$RAW" "$COMBINED"
timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -3
rm -f "$ELF"
