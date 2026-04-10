#!/bin/bash
STAGE1=/tmp/stage1.elf

test_src() {
    local name="$1"
    local src="$2"
    PIPE=/tmp/tg-p-$$; RAW=/tmp/tg-r-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & H=$!
    timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
    for i in $(seq 1 10); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'TEXT\n'; printf '%s' "$src"; printf '\x04') > "$PIPE" &
    sleep 5
    kill $Q $H 2>/dev/null; wait 2>/dev/null
    local sz=$(wc -c < "$RAW")
    if grep -qa 'HEAP:' "$RAW" 2>/dev/null; then echo "PASS: $name"
    elif [ "$sz" -gt 10 ]; then echo "PARTIAL: $name ($sz bytes)"; tail -c 100 "$RAW" | cat -v
    else echo "FAIL: $name"; fi
    rm -f "$PIPE" "$RAW"
}

test_src "no-annotation" 'Chapter: T
identity (x) = x
main = identity 42'

test_src "int-annotation" 'Chapter: T
identity : Integer -> Integer
identity (x) = x
main = identity 42'

test_src "type-var-annotation" 'Chapter: T
identity : a -> a
identity (x) = x
main = identity 42'

test_src "two-type-vars" 'Chapter: T
first : a -> b -> a
first (x) (y) = x
main = first 42 "hello"'

test_src "just-annotation-no-call" 'Chapter: T
identity : a -> a
identity (x) = x
main = 42'
