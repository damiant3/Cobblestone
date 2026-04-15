#!/bin/bash
# Compile a single .codex sample through the bare-metal stage1 self-host ELF
# and extract the resulting ELF bytes.
#
# Usage: tools/compile-sample-bare.sh <input.codex> <output.elf>
set -euo pipefail
INPUT="${1:?usage: compile-sample-bare.sh <input.codex> <output.elf>}"
OUTPUT="${2:?usage: compile-sample-bare.sh <input.codex> <output.elf>}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
KERNEL="$REPO/build-output/bare-metal/Codex.Codex.elf"
QEMU="/usr/bin/qemu-system-x86_64"
TIMEOUT=${TIMEOUT:-120}

[ -f "$KERNEL" ] || { echo "FAIL: $KERNEL missing — run tools/pingpong.sh first"; exit 1; }
[ -f "$INPUT" ] || { echo "FAIL: $INPUT missing"; exit 1; }
[ -x "$QEMU" ] || { echo "FAIL: $QEMU missing"; exit 1; }

RAW="/tmp/csb-raw-$$"
PIPE="/tmp/csb-pipe-$$"
rm -f "$RAW" "$PIPE"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!

START=$SECONDS
timeout "$TIMEOUT" "$QEMU" \
    -enable-kvm -kernel "$KERNEL" -serial stdio -display none -no-reboot -m 1024 \
    < "$PIPE" > "$RAW" 2>/dev/null &
QPID=$!

WAIT=0
while ! grep -qa 'READY' "$RAW" 2>/dev/null; do
    sleep 0.2
    WAIT=$((WAIT + 1))
    [ "$WAIT" -gt 100 ] && { echo "FAIL: no READY"; kill $QPID $HOLDER 2>/dev/null; exit 1; }
    kill -0 $QPID 2>/dev/null || break
done

(printf 'BINARY\n'; cat "$INPUT"; printf '\x04') > "$PIPE" &
PREV=0; STABLE=0
while true; do
    sleep 1
    CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    if [ "$CUR" -gt 100 ] && [ "$CUR" -eq "$PREV" ]; then
        STABLE=$((STABLE + 1))
        [ "$STABLE" -ge 2 ] && break
    else
        STABLE=0
    fi
    PREV=$CUR
    kill -0 $QPID 2>/dev/null || break
done
ELAPSED=$(( SECONDS - START ))
kill $QPID $HOLDER 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$PIPE"

if ! grep -qa 'SIZE:' "$RAW"; then
    echo "FAIL: no SIZE: marker (${ELAPSED}s)"
    echo "--- tail of raw output ---"
    tail -c 400 "$RAW" | strings
    rm -f "$RAW"
    exit 1
fi

SIZE_LINE=$(grep -a 'SIZE:' "$RAW" | head -1)
ELF_SIZE=${SIZE_LINE#*SIZE:}
ELF_SIZE=${ELF_SIZE%%[!0-9]*}
OFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
BSTART=$((OFF + 5 + ${#ELF_SIZE} + 1))
dd if="$RAW" bs=1 skip="$BSTART" count="$ELF_SIZE" of="$OUTPUT" 2>/dev/null
GOT=$(wc -c < "$OUTPUT")

echo "Input:  $INPUT ($(wc -c < "$INPUT") bytes)"
echo "Output: $OUTPUT (expected $ELF_SIZE, got $GOT, ${ELAPSED}s)"
if [ "$GOT" -ne "$ELF_SIZE" ]; then
    echo "FAIL: size mismatch"
    rm -f "$RAW"
    exit 1
fi

HEAD=$(xxd -l 4 -p "$OUTPUT")
echo "ELF magic (first 4 bytes): $HEAD (expected 7f454c46)"
grep -a '^HEAP:\|^STACK:' "$RAW" | head -2 || true
rm -f "$RAW"
[ "$HEAD" = "7f454c46" ] || { echo "WARN: ELF magic wrong — CDX-C6 corruption suspected"; exit 2; }
echo "OK"
