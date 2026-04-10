#!/bin/bash
# Test increasingly complex effectful programs compiled by Stage 0
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

compile_and_boot() {
    local LABEL="$1"
    local SOURCE="$2"
    local PIPE=/tmp/ec-pipe-$$
    local RAW=/tmp/ec-raw-$$
    local ELF=/tmp/ec-elf-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local HOLDER=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local QEMU=$!
    for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SOURCE" > "$PIPE" &
    sleep 15
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "  $LABEL: compile FAIL"; rm -f "$RAW"; return
    fi
    python3 -c "
data=open('$RAW','rb').read();idx=data.find(b'SIZE:');nl=data.index(b'\x0a',idx);size=int(data[idx+5:nl])
open('$ELF','wb').write(data[nl+1:nl+1+size]);print(f'  $LABEL: {size}b', end=' ')
"
    rm -f "$RAW"
    local BOOT_RAW=/tmp/ec-boot-$$
    timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        > "$BOOT_RAW" 2>/dev/null < /dev/null
    local BOOT_SIZE=$(wc -c < "$BOOT_RAW" 2>/dev/null)
    if [ "$BOOT_SIZE" -gt 0 ]; then
        echo "BOOTS $(head -c 40 "$BOOT_RAW" | tr '\n' '|')"
    else
        echo "NO BOOT"
    fi
    rm -f "$BOOT_RAW" "$ELF"
}

echo "=== Effectful complexity ladder ==="

compile_and_boot "1-pure" 'Chapter: T
  main : Integer
  main = 42'

compile_and_boot "2-print" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "hi"'

compile_and_boot "3-two-prints" 'Chapter: T
  main : [Console] Nothing
  main = do
   print-line "a"
   print-line "b"'

compile_and_boot "4-bind-let" 'Chapter: T
  main : [Console] Nothing
  main = do
   let x = 42
   in print-line (integer-to-text x)'

compile_and_boot "5-bind-readline" 'Chapter: T
  main : [Console] Nothing
  main = do
   x <- read-line
   print-line x'

compile_and_boot "6-two-funcs" 'Chapter: T
  greet : [Console] Nothing
  greet = do
   print-line "hello"

  main : [Console] Nothing
  main = do
   greet'
