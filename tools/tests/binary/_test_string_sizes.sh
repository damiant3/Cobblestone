#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

compile_test() {
    local LABEL="$1"
    local SOURCE="$2"
    local PIPE=/tmp/ss-pipe-$$
    local RAW=/tmp/ss-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local HOLDER=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local QEMU=$!
    for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SOURCE" > "$PIPE" &
    sleep 15
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    if grep -qa 'SIZE:' "$RAW"; then
        local SZ=$(grep -oP 'SIZE:\K[0-9]+' "$RAW" | head -1)
        echo "  $LABEL: OK ($SZ bytes)"
    else
        echo "  $LABEL: FAIL"
    fi
    rm -f "$RAW"
}

echo "=== String literal size ladder ==="
compile_test "1-char" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "a"'

compile_test "2-char" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "ab"'

compile_test "3-char" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "abc"'

compile_test "5-char" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "hello"'
