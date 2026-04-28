#!/bin/bash
# bs3-gdb-probe.sh — drive gdb+QEMU against stage1.elf, hbreak at an address,
# read registers at hit, exit. Pair with bs3-qemu-trace.sh to find live
# addresses before setting a breakpoint.
#
# Usage:
#   tools/bs3-gdb-probe.sh <hex-address> [sample-file]
# Example:
#   tools/bs3-gdb-probe.sh 0x1e0534
#   tools/bs3-gdb-probe.sh 0x1e0534 samples/foo.codex
#
# Defaults: sample-file = build-output/bare-metal/bs3-mini.codex.
# See docs/Test/gdb-on-qemu.md for the full workflow.
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO"

if [ $# -lt 1 ]; then
    echo "usage: $0 <hex-address> [sample-file]" >&2
    exit 1
fi

ADDR=$1
SAMPLE=${2:-build-output/bare-metal/bs3-mini.codex}
ELF=build-output/bare-metal/stage1.elf
SERIAL=build-output/bare-metal/bs3-gdb-serial.raw
PIPE=/tmp/bs3gdb-$$
rm -f "$PIPE" "$SERIAL"
mkfifo "$PIPE"

if [ ! -f "$ELF" ]; then
    echo "ERROR: $ELF not found. Run tools/pingpong-self.sh first." >&2
    exit 1
fi
if [ ! -f "$SAMPLE" ]; then
    echo "ERROR: $SAMPLE not found." >&2
    exit 1
fi

(
    while ! grep -qa READY "$SERIAL" 2>/dev/null; do sleep 0.3; done
    printf 'BINARY\n'
    cat "$SAMPLE"
    printf '\x04'
) > "$PIPE" &
FEEDER=$!

/usr/bin/qemu-system-x86_64 \
    -enable-kvm -kernel "$ELF" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -gdb tcp::1234 -S \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!

sleep 1

cat > /tmp/bs3gdb.gdb << GEOF
set architecture i386:x86-64
file $ELF
target remote :1234
set pagination off
set confirm off

hbreak *$ADDR
continue
printf "HIT $ADDR: rip=%#lx rdi=%#lx rsi=%#lx rdx=%#lx rcx=%#lx r8=%#lx r9=%#lx\n", \$rip, \$rdi, \$rsi, \$rdx, \$rcx, \$r8, \$r9

kill
quit
GEOF

timeout 30 /usr/bin/gdb -batch -nx -x /tmp/bs3gdb.gdb 2>&1

kill $QEMU $FEEDER 2>/dev/null
wait 2>/dev/null
rm -f "$PIPE" /tmp/bs3gdb.gdb

echo "=== SERIAL (first 500 printable) ==="
head -c 500 "$SERIAL" | tr -cd '[:print:]\n\t'
