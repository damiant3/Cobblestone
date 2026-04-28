#!/bin/bash
# Run bare-metal ELF in MEASURE mode and print the per-phase heap marks.
# Usage: tools/measure-compile.sh [source-file]
#
# Two-channel serial via qemu-config.sh (matches pingpong-self.sh).
# Set QEMU_ACCEL=whpx to use WHPX (default).
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/qemu-config.sh"

ELF="${ELF:-build-output/bare-metal/Codex.Codex.elf}"
SOURCE="${1:-build-output/bare-metal/source.codex}"
TIMEOUT="${TIMEOUT:-180}"

if [ ! -f "$ELF" ]; then echo "FAIL: $ELF not found"; exit 1; fi
if [ ! -f "$SOURCE" ]; then echo "FAIL: $SOURCE not found"; exit 1; fi

out="/tmp/measure-out-$$"
qemu_pid=
DATA_PORT=
CTRL_PORT=
for attempt in 0 1 2 3; do
    DATA_PORT=$(qemu_alloc_port $attempt)
    CTRL_PORT=$((DATA_PORT + 1))
    timeout "$TIMEOUT" "$QEMU" "${QEMU_ACCEL_FLAGS[@]}" \
        -kernel "$(qemu_path "$ELF")" \
        -chardev "$(qemu_chardev $DATA_PORT)" \
        -chardev "$(qemu_chardev_ctrl $CTRL_PORT)" \
        -serial chardev:ch0 \
        -serial chardev:ch1 \
        -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
        -display none -no-reboot -m 1024 > /dev/null 2>&1 &
    qemu_pid=$!
    sleep 0.5
    if kill -0 $qemu_pid 2>/dev/null && qemu_wait_listen "$DATA_PORT" "$CTRL_PORT"; then
        break
    fi
    kill $qemu_pid 2>/dev/null
    wait 2>/dev/null
    qemu_pid=
done
[ -z "$qemu_pid" ] && { echo "FAIL: QEMU did not listen"; exit 1; }
qemu_read_ready 30 || { echo "FAIL: READY not received"; exit 1; }
{ printf 'MEASURE\n'; cat "$SOURCE"; printf '\x04'; } >&3
: > "$out"
while IFS= read -r -t "$TIMEOUT" line <&3; do
    echo "$line" >> "$out"
    [[ "$line" == STACK:* ]] && break
done
exec 3>&- 4>&- 2>/dev/null || true
kill $qemu_pid 2>/dev/null || true
wait 2>/dev/null || true

echo "=== heap marks (bytes heap-top at each phase boundary) ==="
grep -E '^PHASE-|^EMIT-BYTES|^HEAP:|^STACK:|^RESULT:' "$out" || true
echo ""
echo "=== deltas (bytes allocated in each phase) ==="
awk -F':' '
/^PHASE-/ { name=$1; sub(/^PHASE-/, "", name); val=$2;
            if (prev != "") {
              delta = val - prev_val;
              printf "  %-30s +%12d bytes (%0.1f MB)  cumulative=%d\n", name, delta, delta/1048576.0, val;
            } else {
              printf "  %-30s  (start)  at=%d\n", name, val;
            }
            prev = name; prev_val = val; }
' "$out"
rm -f "$out"
