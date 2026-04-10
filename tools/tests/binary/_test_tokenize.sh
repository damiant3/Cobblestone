#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/tk-p-$$; local RAW=/tmp/tk-r-$$; local ELF=/tmp/tk-$$.elf
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
    local PIPE2=/tmp/tk2-p-$$; local RAW2=/tmp/tk2-r-$$
    rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
    sleep 999 > "$PIPE2" & local H2=$!
    timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE2" > "$RAW2" 2>/dev/null & local Q2=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
    if [ -n "$EXPECT" ]; then printf '%s' "$EXPECT" > "$PIPE2" & fi
    sleep 3
    kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$ELF"
    local LINE2=$(sed -n '2p' "$RAW2")
    echo "  $LABEL: $LINE2"
    rm -f "$RAW2"
}

echo "=== Tokenizer tests ==="

# Does tokenize work at all on a trivial string?
test_src "tokenize-trivial" \
    'Chapter: T
Section: Main
  cites Lexer (tokenize)
  main : [Console, FileSystem] Nothing
  main = do
   source <- read-file "x"
   let tokens = tokenize source
   in print-line (integer-to-text (list-length tokens))' \
    "$(printf '42\x04')"

# Even simpler: tokenize a hardcoded string
test_src "tokenize-literal" \
    'Chapter: T
Section: Main
  cites Lexer (tokenize)
  main : Integer
  main = list-length (tokenize "x = 1")'
