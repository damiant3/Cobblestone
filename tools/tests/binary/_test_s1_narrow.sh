#!/bin/bash
STAGE1=/tmp/stage1.elf

test_src() {
    local name="$1"
    local src="$2"
    PIPE=/tmp/tn-p-$$; RAW=/tmp/tn-r-$$
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
    elif [ "$sz" -gt 10 ]; then echo "PARTIAL: $name ($sz bytes)"
    else echo "FAIL: $name"; fi
    rm -f "$PIPE" "$RAW"
}

test_src "higher-order-apply" 'Chapter: T
double (x) = x + x
apply-fn (f) (x) = f x
main = apply-fn double 21'

test_src "map-list-inline" 'Chapter: T
double (x) = x + x
main = let xs = [1, 2, 3]
 in let ys = map-list double xs
 in list-length ys'

test_src "fold-simple" 'Chapter: T
add (a) (b) = a + b
main = let xs = [1, 2, 3]
 in fold-list add 0 xs'

test_src "generic-identity" 'Chapter: T
identity : a -> a
identity (x) = x
main = identity 42'

test_src "5-param-function" 'Chapter: T
f (a) (b) (c) (d) (e) = a + b + c + d + e
main = f 1 2 3 4 5'

test_src "tco-loop" 'Chapter: T
count-down (n) (acc) = if n == 0 then acc else count-down (n - 1) (acc + 1)
main = count-down 100 0'
