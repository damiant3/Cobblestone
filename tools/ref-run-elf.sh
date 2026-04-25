#!/bin/bash
ELF=$1
[ -f "$ELF" ] || { echo "MISSING: $ELF"; exit 1; }
RAW=/tmp/ref-run-raw-$$
PIPE=/tmp/ref-run-pipe-$$
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 30 > "$PIPE" &
HOLDER=$!
timeout 8 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel "$ELF" -serial stdio -device isa-debug-exit,iobase=0xf4,iosize=0x04 -display none -no-reboot -m 1024 < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!
for i in $(seq 1 40); do grep -qa HEAP "$RAW" 2>/dev/null && break; sleep 0.2; kill -0 $QEMU 2>/dev/null || break; done
sleep 1
kill $QEMU $HOLDER 2>/dev/null
wait 2>/dev/null
awk '/READY/,/STACK/' "$RAW" | head -8
rm -f "$RAW" "$PIPE"
