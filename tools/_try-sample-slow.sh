#!/bin/bash
# Feed a single source file to the bare-metal self-host ELF using
# BINARY-DIAG mode. Reports the last PH: marker before crash/hang.
set -uo pipefail
ELF="/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf"
SOURCE="${1:-}"
if [ -z "$SOURCE" ] || [ ! -f "$SOURCE" ]; then
    echo "usage: $0 <source.codex>"
    exit 2
fi

OUTDIR="/tmp/codex-sample-diag-$$"
mkdir -p "$OUTDIR"
RAW="$OUTDIR/raw"
PIPE="$OUTDIR/pipe"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout 600 /usr/bin/qemu-system-x86_64 \
    -enable-kvm \
    -kernel "$ELF" \
    -serial stdio \
    -display none \
    -no-reboot \
    -m 1024 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QEMU_PID=$!

for i in $(seq 1 150); do
    grep -qa 'READY' "$RAW" 2>/dev/null && break
    sleep 0.2
    kill -0 $QEMU_PID 2>/dev/null || break
done
if ! grep -qa 'READY' "$RAW" 2>/dev/null; then
    echo "FAIL: READY not received"
    kill $QEMU_PID $HOLDER 2>/dev/null
    wait 2>/dev/null
    rm -rf "$OUTDIR"
    exit 1
fi

(printf 'BINARY-DIAG\n'; cat "$SOURCE"; printf '\x04') > "$PIPE" &

prev_size=0
stable=0
for i in $(seq 1 290); do
    sleep 2
    cur=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev_size" ]; then
        stable=$((stable+1))
        [ "$stable" -ge 2 ] && break
    else
        stable=0
    fi
    prev_size=$cur
    kill -0 $QEMU_PID 2>/dev/null || break
done

kill $QEMU_PID $HOLDER 2>/dev/null
wait 2>/dev/null

echo "--- PH: markers ---"
grep -oa 'PH:[a-z-]*' "$RAW" || echo "(none)"
echo "--- SIZE marker? ---"
grep -oa 'SIZE:[0-9]*' "$RAW" | head -3 || echo "(not found)"
echo "--- last 2KB (printable) ---"
tail -c 2048 "$RAW" | tr -dc '\11\12\15\40-\176' | tail -40
echo "--- total raw size: $(wc -c < "$RAW") bytes ---"
rm -rf "$OUTDIR"
