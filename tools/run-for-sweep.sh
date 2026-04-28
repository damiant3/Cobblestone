#!/bin/bash
# Helper invoked by sweep.sh. Boots $1 (an ELF) under QEMU,
# captures serial output between READY and HEAP, writes to $2.
#
# Two-channel serial: COM1 (FD 3) data, COM2 (FD 4) reserved for control.
# qemu_read_ready accepts READY on FD 4 (dual-chardev seed).
# See tools/qemu-config.sh.
set -u
elf=$1
outfile=$2
. "$(dirname "$0")/qemu-config.sh"

RAW=/tmp/sweep-raw-$$
qemu=
DATA_PORT=
CTRL_PORT=
for attempt in 0 1 2 3; do
    DATA_PORT=$(qemu_alloc_port $attempt)
    CTRL_PORT=$((DATA_PORT + 1))
    timeout 10 "$QEMU" "${QEMU_ACCEL_FLAGS[@]}" \
        -kernel "$(qemu_path "$elf")" \
        -chardev "$(qemu_chardev $DATA_PORT)" \
        -chardev "$(qemu_chardev_ctrl $CTRL_PORT)" \
        -serial chardev:ch0 \
        -serial chardev:ch1 \
        -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
        -display none -no-reboot -m 1024 > "$RAW" 2>/dev/null &
    qemu=$!
    sleep 0.5
    if kill -0 $qemu 2>/dev/null && qemu_wait_listen "$DATA_PORT" "$CTRL_PORT"; then
        break
    fi
    kill $qemu 2>/dev/null
    wait 2>/dev/null
    qemu=
done

if [ -z "$qemu" ]; then
    : > "$outfile"
    rm -f "$RAW"
    exit 1
fi

: > "$outfile"
if qemu_read_ready 12; then
    while IFS= read -r -t 12 line <&3; do
        if [[ "$line" == HEAP:* ]]; then
            break
        fi
        [[ "$line" == WD:* ]] && echo ">>> $line" >&2
        echo "$line" >> "$outfile"
    done
fi

exec 3>&- 4>&- 2>/dev/null || true
kill $qemu 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$RAW"
