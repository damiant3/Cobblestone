#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PIPE=/tmp/rrf-p-$$; RAW=/tmp/rrf-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
printf 'BINARY\nChapter: T\n\nSection: Main\n\n  main : [Console, FileSystem] Nothing\n  main = do\n   mode <- read-line\n   source <- read-file mode\n   print-line source\n\x04' > "$PIPE" &
while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then
    echo "Compiled OK"
    SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    BSTART=$((SOFF + 5 + ${#SZ} + 1))
    NEEDED=$((BSTART + SZ))
    # Wait for full binary in case
    CUR=$(wc -c < "$RAW")
    if [ "$CUR" -lt "$NEEDED" ]; then sleep 2; fi
    ELF=/tmp/rrf-test.elf
    dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
    echo "ELF: $(wc -c < "$ELF") bytes"
    rm -f "$RAW"
    # Boot it and send "hello\nworld\x04"
    PIPE2=/tmp/rrf2-p-$$; RAW2=/tmp/rrf2-r-$$
    rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
    sleep 999 > "$PIPE2" & H2=$!
    timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE2" > "$RAW2" 2>/dev/null & Q2=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
    echo "READY: $(grep -c 'READY' "$RAW2")"
    printf 'hello\nworld\x04' > "$PIPE2" &
    sleep 3
    kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2"
    echo "Output:"
    head -c 200 "$RAW2"
    echo ""
    rm -f "$RAW2" "$ELF"
else
    echo "COMPILE FAIL"
    rm -f "$RAW"
fi
