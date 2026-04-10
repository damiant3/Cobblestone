#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/lp-p-$$; local RAW=/tmp/lp-r-$$; local ELF=/tmp/lp-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "FAIL  $LABEL (compile fail)"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return; fi
    local SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    local SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    local BSTART=$((SOFF + 5 + ${#SZ} + 1))
    local NEEDED=$((BSTART + SZ))
    while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
    rm -f "$RAW"
    local OUT=$(timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -c 200)
    local LINE2=$(echo "$OUT" | sed -n '2p')
    if [ "$LINE2" = "$EXPECT" ]; then echo "PASS  $LABEL"
    else echo "FAIL  $LABEL (expected '$EXPECT', got '$LINE2')"
    fi
    rm -f "$ELF"
}

echo "=== Lexer parts ==="

# Can we even call char-code-at on a string?
test_src "char-code-at-runtime" \
    'Chapter: T
Section: Main
  main : Integer
  main = char-code-at "hello" 0' "20"

# Can we call text-length then use it in a loop?
test_src "scan-loop" \
    'Chapter: T
Section: Funcs
  count-a : Text -> Integer -> Integer -> Integer -> Integer
  count-a (s) (i) (len) (acc) =
    if i == len then acc
    else if char-code-at s i == 15 then count-a s (i + 1) len (acc + 1)
    else count-a s (i + 1) len acc
Section: Main
  main : Integer
  main = count-a "abcabc" 0 (text-length "abcabc") 0' "2"

# LexState-like record creation and field access
test_src "lex-state-record" \
    'Chapter: T
Section: Types
  St = record { pos : Integer, line : Integer }
Section: Funcs
  advance : St -> St
  advance (s) = St { pos = s.pos + 1, line = s.line }
Section: Main
  main : Integer
  main = let s = St { pos = 0, line = 1 }
    in let s2 = advance (advance (advance s))
    in s2.pos' "3"

# Keyword classification (if-else chain returning sum type constructors)
test_src "keyword-classify" \
    'Chapter: T
Section: Types
  TK = | KwLet | KwIf | KwElse | Ident | EOF
Section: Funcs
  classify : Text -> TK
  classify (w) =
    if w == "let" then KwLet
    else if w == "if" then KwIf
    else if w == "else" then KwElse
    else Ident
  tag-of : TK -> Integer
  tag-of (k) = when k
    if KwLet -> 0
    if KwIf -> 1
    if KwElse -> 2
    if Ident -> 3
    if EOF -> 4
Section: Main
  main : Integer
  main = tag-of (classify "let") * 100 + tag-of (classify "if") * 10 + tag-of (classify "xyz")' "13"

# Substring (used by lexer to extract token text)
test_src "lex-substring" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   print-line (substring "hello world" 6 5)' "world"
