#!/bin/bash
# Snapshot the current runtime output of samples/NAME.codex into samples/NAME.expected.
# Use ONLY when you've verified the output is actually correct.
set -u
cd "$(dirname "$0")/.."
QEMU=/usr/bin/qemu-system-x86_64

name=$1
elf="build-output/probes/samples-ref/$name/$name.elf"
if [ ! -f "$elf" ]; then
    echo "MISSING: $elf — build it first" >&2
    exit 1
fi
raw=/tmp/ref-snap-$$
pipe=/tmp/ref-snap-pipe-$$
rm -f "$raw" "$pipe"
mkfifo "$pipe"
sleep 30 > "$pipe" &
holder=$!
timeout 10 "$QEMU" -enable-kvm -kernel "$elf" -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 < "$pipe" > "$raw" 2>/dev/null &
qemu=$!
for _ in $(seq 1 60); do
    grep -qa 'HEAP' "$raw" 2>/dev/null && break
    sleep 0.2
    kill -0 $qemu 2>/dev/null || break
done
sleep 1
kill $qemu $holder 2>/dev/null || true
wait 2>/dev/null || true
awk 'found && /^HEAP:/ { exit } found { print } /^READY/ { found=1 }' "$raw" > "samples/$name.expected"
rm -f "$raw" "$pipe"
echo "wrote samples/$name.expected ($(wc -l < samples/$name.expected) lines)"
