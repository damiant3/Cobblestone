#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex

# Generate Stage 1
echo "=== Generating Stage 1 ==="
PIPE=/tmp/bs-p-$$; RAW=/tmp/bs-r-$$
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

STAGE1=/tmp/stage1.elf

# Test function
test_source() {
    local name="$1"
    local src="$2"
    local len=${#src}

    PIPE=/tmp/sz-p-$$; RAW=/tmp/sz-r-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & H=$!
    timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'TEXT\n'; printf '%s' "$src"; printf '\x04') > "$PIPE" &
    # Wait up to 10s, checking if QEMU exits
    for i in $(seq 1 10); do
        sleep 1
        kill -0 $Q 2>/dev/null || break
        grep -qa 'HEAP:' "$RAW" && break
    done
    kill $Q $H 2>/dev/null; wait 2>/dev/null

    local sz=$(wc -c < "$RAW")
    if grep -qa 'HEAP:' "$RAW"; then
        echo "PASS: $name (${len}c → ${sz}b)"
    elif [ "$sz" -gt 10 ]; then
        echo "PARTIAL: $name (${len}c → ${sz}b)"
        tail -c 100 "$RAW" | cat -v
    else
        echo "HANG/CRASH: $name (${len}c → ${sz}b)"
    fi
    rm -f "$PIPE" "$RAW"
}

echo ""
echo "=== Size threshold tests ==="
test_source "mini-boot" "$(cat /mnt/d/Projects/NewRepository-cam/samples/mini-bootstrap.codex)"

# Name.codex (277 bytes)
test_source "Name.codex" "$(cat /mnt/d/Projects/NewRepository-cam/Codex.Codex/Core/Name.codex)"

# TokenKind.codex (1234 bytes) - lots of sum type constructors
test_source "TokenKind" "$(cat /mnt/d/Projects/NewRepository-cam/Codex.Codex/Syntax/TokenKind.codex)"

# Collections.codex (2523 bytes) - functions with multiple params
test_source "Collections" "$(cat /mnt/d/Projects/NewRepository-cam/Codex.Codex/Core/Collections.codex)"

# SyntaxNodes.codex (3035 bytes) - lots of records and sum types
test_source "SyntaxNodes" "$(cat /mnt/d/Projects/NewRepository-cam/Codex.Codex/Syntax/SyntaxNodes.codex)"

echo ""
echo "=== Done ==="
