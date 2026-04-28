#!/bin/bash
# Quick smoke test: run a Codex.Cli-Codex-built ELF in QEMU and verify it
# prints READY. Used to validate the seed compile before pingpong.
set -u
ELF="${1:-/mnt/c/Users/Damian/AppData/Local/Temp/cli-build-out/Codex.Codex.elf}"
PIPE=/tmp/cli-test-pipe-$$
RAW=/tmp/cli-test-raw-$$
mkfifo "$PIPE"
sleep 999 > "$PIPE" &
HOLDER=$!
timeout 90 /usr/bin/qemu-system-x86_64 -enable-kvm -kernel "$ELF" -serial stdio -device isa-debug-exit,iobase=0xf4,iosize=0x04 -display none -no-reboot -m 1024 < "$PIPE" > "$RAW" 2>/dev/null &
QPID=$!
for i in $(seq 1 60); do
  sleep 0.5
  grep -qa READY "$RAW" 2>/dev/null && break
done
if grep -qa READY "$RAW" 2>/dev/null; then echo "READY: yes"; else echo "READY: no"; fi
echo "first 50 bytes:"
head -c 50 "$RAW"
echo ""
kill $QPID $HOLDER 2>/dev/null
wait 2>/dev/null
rm -f "$PIPE" "$RAW"
