#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam/Codex.Codex

test_lex() {
    local LABEL="$1"; local MAIN_CODE="$2"; local EXPECT="$3"
    local COMBINED=/tmp/lb-src-$$
    cat "$REPO/Core/Name.codex" > "$COMBINED"
    cat "$REPO/Types/CodexType.codex" >> "$COMBINED"
    cat "$REPO/Syntax/TokenKind.codex" >> "$COMBINED"
    cat "$REPO/Syntax/Token.codex" >> "$COMBINED"
    cat "$REPO/Syntax/Lexer.codex" >> "$COMBINED"
    cat >> "$COMBINED" << EOF

Chapter: Test

Section: Main

  cites Lexer (tokenize, make-lex-state, scan-token, is-at-end, peek-code, skip-spaces, is-letter-code, scan-ident-rest, advance-char, extract-text, classify-word, make-token, scan-ident-end)

$MAIN_CODE
EOF

    local PIPE=/tmp/lb-p-$$; local RAW=/tmp/lb-r-$$; local ELF=/tmp/lb-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "FAIL  $LABEL (compile fail)"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW" "$COMBINED"; return; fi
    local SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    local SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    local BSTART=$((SOFF + 5 + ${#SZ} + 1))
    local NEEDED=$((BSTART + SZ))
    while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
    rm -f "$RAW" "$COMBINED"
    local OUT=$(timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -c 100)
    local LINE2=$(echo "$OUT" | sed -n '2p')
    if [ "$LINE2" = "$EXPECT" ]; then echo "PASS  $LABEL"
    else echo "FAIL  $LABEL (expected '$EXPECT', got '$LINE2') full: $(echo "$OUT" | head -3 | tr '\n' '|')"
    fi
    rm -f "$ELF"
}

echo "=== Lexer bisect ==="

test_lex "make-lex-state" \
    '  main : Integer
  main = let s = make-lex-state "x"
    in s.offset' "0"

test_lex "is-at-end" \
    '  main : Integer
  main = if is-at-end (make-lex-state "") then 1 else 0' "1"

test_lex "peek-code-x" \
    '  main : Integer
  main = peek-code (make-lex-state "x")' "36"

test_lex "skip-spaces" \
    '  main : Integer
  main = let s = skip-spaces (make-lex-state "  x")
    in s.offset' "2"

test_lex "is-letter-code-x" \
    '  main : Integer
  main = if is-letter-code 36 then 1 else 0' "1"

test_lex "advance-char" \
    '  main : Integer
  main = let s = advance-char (make-lex-state "xy")
    in s.offset' "1"

test_lex "scan-ident-end" \
    '  main : Integer
  main = scan-ident-end "abc " 0 4' "3"

test_lex "scan-ident-rest" \
    '  main : Integer
  main = let s = scan-ident-rest (advance-char (make-lex-state "abc "))
    in s.offset' "3"

test_lex "extract-text" \
    '  main : [Console] Nothing
  main = do
   let s = make-lex-state "abc def"
   in print-line (extract-text s 0 (advance-char (advance-char (advance-char s))))' "abc"

test_lex "classify-word" \
    '  main : Integer
  main = let k = classify-word "let"
    in when k
      if LetKeyword -> 1
      if _ -> 0' "1"

test_lex "scan-token-one" \
    '  main : Integer
  main = when scan-token (make-lex-state "x")
    if LexToken (tok) (next) -> text-length (tok.text)
    if LexEnd -> 0' "1"
