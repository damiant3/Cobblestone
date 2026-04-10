#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_depth() {
    local N=$1
    local SRC="Chapter: T

Section: Main

  main : Integer
  main = let x = 42
    in "
    for i in $(seq 1 $N); do
        SRC="${SRC}if x == ${i} then ${i}
    else "
    done
    SRC="${SRC}0"

    local PIPE=/tmp/id-p-$$; local RAW=/tmp/id-r-$$; local ELF=/tmp/id-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "depth $N: COMPILE FAIL"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return; fi
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
    if [ "$LINE2" = "0" ]; then echo "depth $N: PASS (got 0)"
    else echo "depth $N: FAIL (got '$LINE2')"
    fi
    rm -f "$ELF"
}

echo "=== If-else nesting depth ==="
test_depth 2
test_depth 4
test_depth 6
test_depth 8
test_depth 10
test_depth 12
test_depth 15
test_depth 20
