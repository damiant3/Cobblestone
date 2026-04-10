#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
{
echo 'Chapter: ListTest'
echo ''
echo 'Section: Data'
echo ''
for i in $(seq 1 100); do
echo "  d${i} : List Text"
echo "  d${i} = [\"a\", \"b\", \"c\", \"d\", \"e\", \"f\", \"g\", \"h\", \"i\", \"j\"]"
echo ""
done
echo 'Section: Main'
echo ''
echo '  main : Integer'
echo '  main = list-length d1'
} > /tmp/ll1000b.codex
PIPE=/tmp/ll2-p-$$; RAW=/tmp/ll2-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 60 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
START=$SECONDS
(printf 'BINARY\n'; cat /tmp/ll1000b.codex; printf '\x04') > "$PIPE" &
for i in $(seq 1 60); do
    sleep 1
    grep -qa 'SIZE:' "$RAW" && break
    kill -0 $Q 2>/dev/null || break
done
ELAPSED=$((SECONDS - START))
kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
if grep -qa 'SIZE:' "$RAW"; then echo "OK in ${ELAPSED}s"
else echo "CRASH in ${ELAPSED}s ($(wc -c < "$RAW") bytes)"
fi
rm -f "$RAW" /tmp/ll1000b.codex
