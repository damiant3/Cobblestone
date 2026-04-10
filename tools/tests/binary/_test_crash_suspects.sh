#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/cs-p-$$; local RAW=/tmp/cs-r-$$; local ELF=/tmp/cs-$$.elf
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
    else echo "FAIL  $LABEL (expected '$EXPECT', got '$LINE2') full: $(echo "$OUT" | head -4 | tr '\n' '|')"
    fi
    rm -f "$ELF"
}

echo "=== Crash suspects ==="

# Suspect 1: Bool equality (sum type pointer comparison)
test_src "bool-eq-true" \
    'Chapter: T
Section: Main
  main : Integer
  main = if True == True then 1 else 0' "1"

test_src "bool-eq-false" \
    'Chapter: T
Section: Main
  main : Integer
  main = if False == False then 1 else 0' "1"

test_src "bool-neq" \
    'Chapter: T
Section: Main
  main : Integer
  main = if True == False then 0 else 1' "1"

# Suspect 2: text-replace (used by normalize-whitespace)
test_src "text-replace" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length (text-replace "aXXXb" "XXX" "Y")' "3"

test_src "text-replace-newlines" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length (text-replace "a\n\n\nb" "\n\n\n" "\n\n")' "4"

# Suspect 3: zero-arg function returning constant
test_src "zero-arg-func" \
    'Chapter: T
Section: Funcs
  my-const : Integer
  my-const = 42
Section: Main
  main : Integer
  main = my-const' "42"

test_src "zero-arg-used-in-compare" \
    "Chapter: T
Section: Data
  threshold : Integer
  threshold = 10
Section: Funcs
  check : Integer -> Integer
  check (n) = if n >= threshold then 1 else 0
Section: Main
  main : Integer
  main = check 5 + check 15 * 10" "10"

# Suspect 2b: normalize-whitespace itself
test_src "normalize-ws" \
    'Chapter: T
Section: Funcs
  normalize : Text -> Text
  normalize (s) =
   let r = text-replace s "\n\n\n" "\n\n"
   in if text-length r == text-length s then s
      else normalize r
Section: Main
  main : Integer
  main = text-length (normalize "a\n\n\nb")' "4"
