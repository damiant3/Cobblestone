#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/sd-p-$$; local RAW=/tmp/sd-r-$$; local ELF=/tmp/sd-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "FAIL  $LABEL (compile fail)"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return
    fi
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

echo "=== Sum type debug ==="

test_src "nullary-ctor" \
    'Chapter: T
Section: Types
  Color = | Red | Green | Blue
Section: Main
  main : Integer
  main = when Red
    if Red -> 1
    if Green -> 2
    if Blue -> 3' "1"

test_src "unary-ctor" \
    'Chapter: T
Section: Types
  Box = | Box (Integer)
Section: Main
  main : Integer
  main = when (Box 42)
    if Box (n) -> n' "42"

test_src "binary-ctor" \
    'Chapter: T
Section: Types
  Pair = | Pair (Integer) (Integer)
Section: Main
  main : Integer
  main = when (Pair 6 7)
    if Pair (a) (b) -> a * b' "42"

test_src "two-ctors" \
    'Chapter: T
Section: Types
  X = | A | B (Integer)
Section: Funcs
  f : X -> Integer
  f (x) = when x
    if A -> 0
    if B (n) -> n
Section: Main
  main : Integer
  main = f (B 42)' "42"

test_src "shape-test" \
    'Chapter: T
Section: Types
  Shape = | Circle (Integer) | Rect (Integer) (Integer)
Section: Funcs
  area : Shape -> Integer
  area (s) = when s
    if Circle (r) -> r * r
    if Rect (w) (h) -> w * h
Section: Main
  main : Integer
  main = area (Rect 6 7)' "42"
