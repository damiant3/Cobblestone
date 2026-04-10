#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_n_funcs() {
    local N=$1
    local SRC="Chapter: T

Section: Funcs
"
    for i in $(seq 1 $N); do
        SRC="${SRC}
  f${i} : Integer -> Integer
  f${i} (x) = x + ${i}
"
    done
    SRC="${SRC}
Section: Main

  main : Integer
  main = f${N} 0
"
    local PIPE=/tmp/mf-p-$$; local RAW=/tmp/mf-r-$$; local ELF=/tmp/mf-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "$N funcs: COMPILE FAIL"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return; fi
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
    if [ "$LINE2" = "$N" ]; then echo "$N funcs: PASS"
    else echo "$N funcs: FAIL (expected '$N', got '$LINE2')"
    fi
    rm -f "$ELF"
}

echo "=== Function count scaling ==="
test_n_funcs 10
test_n_funcs 50
test_n_funcs 100
test_n_funcs 200
test_n_funcs 500
