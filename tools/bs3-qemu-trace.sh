#!/bin/bash
# bs3-qemu-trace.sh — run stage1.elf under QEMU TCG (NO KVM) with -d in_asm,
# capture every translated block address to a trace log. Use this BEFORE
# setting a gdb hbreak to confirm the target address is actually executed.
#
# Usage:
#   tools/bs3-qemu-trace.sh [sample-file]
# Example:
#   tools/bs3-qemu-trace.sh samples/foo.codex
#
# Defaults: sample-file = build-output/bare-metal/bs3-mini.codex.
# Output: /tmp/bs3-qemu-trace.log (full trace) plus summary.
# See docs/Test/gdb-on-qemu.md for the full workflow.
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO"

SAMPLE=${1:-build-output/bare-metal/bs3-mini.codex}
ELF=build-output/bare-metal/stage1.elf
SERIAL=build-output/bare-metal/bs3-qemu-trace-serial.raw
TRACE=/tmp/bs3-qemu-trace.log
PIPE=/tmp/bs3trace-$$
rm -f "$PIPE" "$SERIAL" "$TRACE"
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

# NO -enable-kvm — TCG needed for -d in_asm
/usr/bin/qemu-system-x86_64 \
    -kernel "$ELF" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -d in_asm -D "$TRACE" \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!

# Wait for compile+emit to complete (up to 120s under TCG — slower than KVM)
for i in $(seq 120); do
    if ! kill -0 $QEMU 2>/dev/null; then break; fi
    if grep -qa 'CODEGEN-HALTED\|CODEGEN-ERRORS\|CODEGEN-EMITTED' "$SERIAL" 2>/dev/null; then
        sleep 1  # drain
        kill $QEMU 2>/dev/null
        break
    fi
    sleep 1
done

kill $QEMU $FEEDER 2>/dev/null
wait 2>/dev/null
rm -f "$PIPE"

echo "=== Trace written: $TRACE ==="
ls -la "$TRACE"

echo "=== Unique executed address count ==="
grep -oE '0x00[0-9a-f]+:' "$TRACE" | sort -u | wc -l

echo "=== Executed address range ==="
grep -oE '0x00[0-9a-f]+:' "$TRACE" | sort -u | head -1
grep -oE '0x00[0-9a-f]+:' "$TRACE" | sort -u | tail -1

echo "=== SERIAL (first 500 printable) ==="
head -c 500 "$SERIAL" | tr -cd '[:print:]\n\t'

echo ""
echo "To check whether address 0x<ADDR> was executed:"
echo "  grep -c '^0x00<ADDR>:' $TRACE"
echo "To list all addresses in function range [lo, hi]:"
echo "  grep -oE '0x00[0-9a-f]+:' $TRACE | sort -u | awk '{v=strtonum(\$0); if (v>=<lo> && v<=<hi>) print}'"
