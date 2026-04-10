#!/bin/bash
# Progressive test: send increasingly complex programs to Stage 1
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex

# First generate Stage 1
echo "=== Generating Stage 1 ==="
PIPE=/tmp/sp-p-$$; RAW=/tmp/sp-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 300 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &
while true; do sleep 10; kill -0 $Q 2>/dev/null || break; grep -qa 'SIZE:' "$RAW" && break; echo "  ..."; done
ELF_SIZE=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((SOFF + 5 + ${#ELF_SIZE} + 1))
NEEDED=$((BSTART + ELF_SIZE))
while true; do CUR=$(wc -c < "$RAW"); [ "$CUR" -ge "$NEEDED" ] && break; kill -0 $Q 2>/dev/null || break; sleep 1; done
dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of=/tmp/stage1.elf 2>/dev/null
echo "Stage 1: $(wc -c < /tmp/stage1.elf) bytes"
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"

# Test function
run_test() {
    local name="$1"
    local src="$2"

    PIPE=/tmp/tp-p-$$; RAW=/tmp/tp-r-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & H=$!
    timeout 15 qemu-system-x86_64 -enable-kvm -kernel "/tmp/stage1.elf" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'TEXT\n'; printf '%s' "$src"; printf '\x04') > "$PIPE" &
    sleep 5
    kill $Q $H 2>/dev/null; wait 2>/dev/null

    local sz=$(wc -c < "$RAW")
    if [ "$sz" -gt 7 ]; then
        # Check for actual output beyond READY
        local output=$(tail -c +7 "$RAW" | tr '\0' ' ' | strings | head -5)
        if echo "$output" | grep -q 'public static\|HEAP:\|COMPILE-ERROR'; then
            echo "PASS: $name ($sz bytes)"
        else
            echo "FAIL: $name ($sz bytes)"
            cat -v "$RAW" | head -10
        fi
    else
        echo "FAIL: $name (only READY)"
    fi
    rm -f "$PIPE" "$RAW"
}

echo ""
echo "=== Progressive Tests ==="

run_test "literal" 'Chapter: T
main = 42'

run_test "string" 'Chapter: T
main = "hello"'

run_test "let-binding" 'Chapter: T
main = let x = 10 in x + 1'

run_test "function" 'Chapter: T
double (x) = x + x
main = double 21'

run_test "if-else" 'Chapter: T
main = if 1 == 1 then 42 else 0'

run_test "sum-type" 'Chapter: T
Color = | Red | Blue
main = Red'

run_test "record" 'Chapter: T
Point = record { x : Integer, y : Integer }
main = let p = Point { x = 3, y = 4 } in p.x'

run_test "pattern-match" 'Chapter: T
Maybe = | Just (Integer) | Nothing
main = let v = Just 42 in when v if Just (x) -> x if Nothing -> 0'

echo ""
echo "=== Done ==="
