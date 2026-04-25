#!/bin/bash
cd "$(dirname "$0")/.."
name=$1
ELF=build-output/probes/samples-ref/$name/$name.elf
[ -f "$ELF" ] || { echo "MISSING_ELF"; exit 1; }
RAW=/tmp/ref-run-$name-raw
PIPE=/tmp/ref-run-$name-pipe
rm -f "$PIPE" "$RAW"
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 12 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel "$ELF" -serial stdio -device isa-debug-exit,iobase=0xf4,iosize=0x04 -display none -no-reboot -m 1024 < "$PIPE" > "$RAW" 2>/dev/null &
QEMU=$!
for i in $(seq 1 60); do
  grep -qa 'HEAP' "$RAW" 2>/dev/null && break
  sleep 0.2
  kill -0 $QEMU 2>/dev/null || break
done
sleep 1
kill $QEMU $HOLDER 2>/dev/null
wait 2>/dev/null
# extract first iteration only: READY + one result block up through HEAP/STACK
awk '/READY/,/STACK/' "$RAW" | head -20
