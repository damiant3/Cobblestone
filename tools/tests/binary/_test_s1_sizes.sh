#!/bin/bash
# Test Stage 1 with increasing source sizes to find the threshold
STAGE1=/tmp/stage1.elf

test_size() {
    local name="$1"
    local src="$2"
    local len=${#src}

    PIPE=/tmp/sz-p-$$; RAW=/tmp/sz-r-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & H=$!
    timeout 15 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'TEXT\n'; printf '%s' "$src"; printf '\x04') > "$PIPE" &
    sleep 8
    kill $Q $H 2>/dev/null; wait 2>/dev/null

    local sz=$(wc -c < "$RAW")
    if grep -qa 'HEAP:' "$RAW"; then
        echo "PASS: $name (${len}c, ${sz}b output)"
    elif [ "$sz" -gt 10 ]; then
        echo "PARTIAL: $name (${len}c, ${sz}b output)"
    else
        echo "FAIL: $name (${len}c, ${sz}b output)"
    fi
    rm -f "$PIPE" "$RAW"
}

# Small: mini-bootstrap (known working)
test_size "mini-bootstrap" "$(cat /mnt/d/Projects/NewRepository-cam/samples/mini-bootstrap.codex)"

# Medium: first 2K of source
SRC=$(cat /mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex)
test_size "2K" "${SRC:0:2000}"
test_size "4K" "${SRC:0:4000}"
test_size "8K" "${SRC:0:8000}"
test_size "16K" "${SRC:0:16000}"
test_size "32K" "${SRC:0:32000}"
