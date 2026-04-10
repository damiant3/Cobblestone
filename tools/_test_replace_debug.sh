#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/rd-p-$$; local RAW=/tmp/rd-r-$$; local ELF=/tmp/rd-$$.elf
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

echo "=== text-replace debug ==="

# Does text-contains work?
test_src "contains-yes" \
    'Chapter: T
Section: Main
  main : Integer
  main = if text-contains "hello world" "world" then 1 else 0' "1"

test_src "contains-no" \
    'Chapter: T
Section: Main
  main : Integer
  main = if text-contains "hello world" "xyz" then 1 else 0' "0"

# Does text-starts-with work?
test_src "starts-with" \
    'Chapter: T
Section: Main
  main : Integer
  main = if text-starts-with "hello" "hel" then 1 else 0' "1"

# Simple replace
test_src "replace-simple" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   print-line (text-replace "abc" "b" "X")' "aXc"

# Replace no match
test_src "replace-no-match" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length (text-replace "abc" "z" "X")' "3"
