#!/bin/bash
# Compile a single .codex sample by booting the bare-metal self-host
# compiler (Codex.Codex.elf) in QEMU and feeding the source over a
# socket-chardev serial port in BINARY mode. Captures the emitted ELF
# from the same socket and writes it to the output path.
#
# Usage: sample-compile-selfhost.sh <source.codex> <out.elf> <log.out>
# Exit 0 = compile succeeded, out.elf is a valid ELF.
# Exit non-zero = compile failed; log.out contains the serial output for
# diagnostic-code matching.
#
# Two-channel serial: COM1 (FD 3) data, COM2 (FD 4) reserved for control.
# qemu_read_ready accepts READY on either FD so single-chardev (current
# depot) and dual-chardev (future) seeds both work. See tools/qemu-config.sh.
# Set QEMU_ACCEL=whpx to use Windows QEMU + WHPX (frees /dev/kvm for
# concurrent peer agents).
set -u

SRC=$1
OUT=$2
LOG=$3

STAGE0="build-output/bare-metal/Codex.Codex.elf"
. "$(dirname "$0")/qemu-config.sh"

if [ ! -f "$STAGE0" ]; then
    echo "MISSING: $STAGE0 — run pingpong-self.sh first to build the self-host ELF" >&2
    exit 2
fi

DATA_PORT=
CTRL_PORT=
qemu=
fwtmp="/tmp/sh-compile-fw-$$"

cleanup() {
    exec 3>&- 4>&- 2>/dev/null || true
    [ -n "${qemu:-}" ] && kill "$qemu" 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$fwtmp"
}
trap cleanup EXIT

# Foreword preload — bare-metal has no filesystem, so prepend cited Foreword
# chapters to the serial feed. Mirrors tools/Codex.Bootstrap/Program.cs's
# LoadCitedForewordChapters. Chapter headers are renamed "Chapter: X" →
# "Chapter: Foreword--X" so they coexist with the citing chapter in one source.
fwcites=$(grep -E '^[[:space:]]*cites[[:space:]]+Foreword[[:space:]]+chapter[[:space:]]+[A-Za-z_][A-Za-z0-9_-]*' "$SRC" \
          | sed -E 's/^[[:space:]]*cites[[:space:]]+Foreword[[:space:]]+chapter[[:space:]]+([A-Za-z_][A-Za-z0-9_-]*).*/\1/' \
          | sort -u)
: > "$fwtmp"
if [ -n "$fwcites" ]; then
    for name in $fwcites; do
        fwpath="foreword/$name.codex"
        if [ ! -f "$fwpath" ]; then
            echo "error 3010: Cited foreword chapter '$name' not found (expected $fwpath)" > "$LOG"
            exit 8
        fi
        awk '!done && /^Chapter:[[:space:]]*/ {
                sub(/^Chapter:[[:space:]]*/, "");
                sub(/[[:space:]]*$/, "");
                print "Chapter: Foreword--" $0;
                done=1; next
             }
             { print }' "$fwpath" >> "$fwtmp"
        printf '\n\n' >> "$fwtmp"
    done
fi

for attempt in 0 1 2 3; do
    DATA_PORT=$(qemu_alloc_port $attempt)
    CTRL_PORT=$((DATA_PORT + 1))
    timeout 45 "$QEMU" "${QEMU_ACCEL_FLAGS[@]}" \
        -kernel "$(qemu_path "$STAGE0")" \
        -chardev "$(qemu_chardev $DATA_PORT)" \
        -chardev "$(qemu_chardev_ctrl $CTRL_PORT)" \
        -serial chardev:ch0 \
        -serial chardev:ch1 \
        -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
        -display none -no-reboot -m 1024 > /dev/null 2>&1 &
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
    echo "FAIL: QEMU did not listen after 4 attempts (last ports $DATA_PORT/$CTRL_PORT)" > "$LOG"
    exit 3
fi

if ! qemu_read_ready 20; then
    echo "READY not received within 20s" > "$LOG"
    exit 3
fi

# Send foreword + source in BINARY mode, terminated by EOT (0x04).
{ printf 'BINARY\n'; cat "$fwtmp" "$SRC"; printf '\x04'; } >&3

# Read text portion until SIZE:/CODEGEN-HALTED/CODEGEN-ERRORS marker.
: > "$LOG"
elf_size=""
status=""
while IFS= read -r -t 60 line <&3; do
    if [[ "$line" == SIZE:* ]]; then
        elf_size=${line#SIZE:}
        elf_size=${elf_size%%[!0-9]*}
        status=size
        break
    fi
    if [[ "$line" == CODEGEN-HALTED* ]] || [[ "$line" == CODEGEN-ERRORS* ]]; then
        echo "$line" >> "$LOG"
        status=halted
        break
    fi
    [[ "$line" == WD:* ]] && echo ">>> $line" >&2
    echo "$line" >> "$LOG"
done

if [ "$status" != "size" ]; then
    # Drain remaining text into log so diagnostics are visible.
    while IFS= read -r -t 5 line <&3; do
        echo "$line" >> "$LOG"
        [[ "$line" == HEAP:* ]] && break
    done
    exit 4
fi

# Read $elf_size bytes of ELF binary directly from the socket.
head -c "$elf_size" <&3 > "$OUT"
got_size=$(wc -c < "$OUT")

if [ "$got_size" -ne "$elf_size" ]; then
    echo "ELF size mismatch: expected $elf_size got $got_size" >> "$LOG"
    exit 5
fi

[ "$elf_size" -lt 100 ] && { echo "ELF too small ($elf_size)" >> "$LOG"; exit 6; }
exit 0
