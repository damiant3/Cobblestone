#!/bin/bash
# Run BINARY-DIAG on a sample for SECS seconds and capture S: profile samples.
set -uo pipefail
SAMPLE="${1:?usage: $0 <sample.codex> [seconds]}"
SECS="${2:-70}"
RAW=/tmp/wd-prof-raw
PIPE=/tmp/wd-prof-pipe
rm -f "$RAW" "$PIPE"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

timeout $((SECS + 30)) /usr/bin/qemu-system-x86_64 \
    -enable-kvm \
    -kernel /mnt/d/Projects/NewRepository-hex/build-output/bare-metal/Codex.Codex.elf \
    -serial stdio -display none -m 1024 -no-reboot \
    < "$PIPE" > "$RAW" 2>&1 &
QPID=$!

while ! grep -qa READY "$RAW" 2>/dev/null; do
    sleep 0.5
    kill -0 $QPID 2>/dev/null || break
done
START=$SECONDS
(printf 'BINARY-DIAG\n'; cat "$SAMPLE"; printf '\x04') > "$PIPE" &

while kill -0 $QPID 2>/dev/null; do
    sleep 2
    [ $((SECONDS - START)) -ge "$SECS" ] && break
done
TOTAL=$((SECONDS - START))
echo "ran for ${TOTAL}s after input"
echo "=== S: profile samples ==="
grep -aE '^S:' "$RAW" || echo "(none)"
echo "=== last 10 lines of printable serial ==="
strings -n 1 "$RAW" | tail -10
echo "=== WD! present? ==="
grep -aE 'WD!' "$RAW" || echo "(no)"
echo "=== SIZE: present? ==="
grep -aE 'SIZE:[0-9]+' "$RAW" || echo "(no)"

kill $QPID $HOLDER 2>/dev/null
wait 2>/dev/null
rm -f "$PIPE" "$RAW"
