#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/ic-p-$$; local RAW=/tmp/ic-r-$$; local ELF=/tmp/ic-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
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
    local OUT=$(timeout 3 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -2)
    local LINE2=$(echo "$OUT" | sed -n '2p')
    if [ "$LINE2" = "$EXPECT" ]; then echo "PASS  $LABEL"
    else echo "FAIL  $LABEL (expected '$EXPECT', got '$LINE2')"
    fi
    rm -f "$ELF"
}

echo "=== Complex nested if-else ==="

# Nested if with let bindings in branches (like scan-token)
test_src "if-let-3" \
    'Chapter: T
Section: Funcs
  f : Integer -> Integer
  f (c) =
    if c == 1 then let x = c + 10 in x
    else if c == 2 then let y = c + 20 in y
    else if c == 3 then let z = c + 30 in z
    else 0
Section: Main
  main : Integer
  main = f 2' "22"

# Nested if with function calls + let in branches
test_src "if-call-let-5" \
    'Chapter: T
Section: Funcs
  double : Integer -> Integer
  double (x) = x * 2
  f : Integer -> Integer
  f (c) =
    if c == 1 then let x = double c in x + 1
    else if c == 2 then let x = double c in x + 2
    else if c == 3 then let x = double c in x + 3
    else if c == 4 then let x = double c in x + 4
    else if c == 5 then let x = double c in x + 5
    else 0
Section: Main
  main : Integer
  main = f 3' "9"

# Nested if with string comparison (like classify-word + scan-token)
test_src "if-streq-let-8" \
    'Chapter: T
Section: Funcs
  f : Text -> Integer -> Integer
  f (s) (n) =
    if s == "a" then let x = n + 1 in x
    else if s == "b" then let x = n + 2 in x
    else if s == "c" then let x = n + 3 in x
    else if s == "d" then let x = n + 4 in x
    else if s == "e" then let x = n + 5 in x
    else if s == "f" then let x = n + 6 in x
    else if s == "g" then let x = n + 7 in x
    else if s == "h" then let x = n + 8 in x
    else 0
Section: Main
  main : Integer
  main = f "e" 100' "105"

# Record construction in nested if branches (like scan-token building LexToken)
test_src "if-record-5" \
    'Chapter: T
Section: Types
  R = record { a : Integer, b : Integer }
Section: Funcs
  f : Integer -> R
  f (c) =
    if c == 1 then R { a = 10, b = 20 }
    else if c == 2 then R { a = 30, b = 40 }
    else if c == 3 then R { a = 50, b = 60 }
    else if c == 4 then R { a = 70, b = 80 }
    else if c == 5 then R { a = 90, b = 100 }
    else R { a = 0, b = 0 }
Section: Main
  main : Integer
  main = let r = f 3 in r.a + r.b' "110"

# Sum type constructor in nested if branches (like scan-token building LexToken)
test_src "if-ctor-5" \
    'Chapter: T
Section: Types
  Res = | Ok (Integer) | Err (Integer)
Section: Funcs
  f : Integer -> Res
  f (c) =
    if c == 1 then Ok 10
    else if c == 2 then Ok 20
    else if c == 3 then Ok 30
    else if c == 4 then Err 40
    else if c == 5 then Err 50
    else Err 0
Section: Main
  main : Integer
  main = when f 3
    if Ok (n) -> n
    if Err (n) -> 0 - n' "30"
