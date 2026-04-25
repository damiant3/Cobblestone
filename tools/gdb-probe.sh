#!/bin/bash
# Quick probe: boot the given bare-metal ELF under QEMU with the GDB stub
# on :1234, attach gdb-multiarch with the same ELF as symbol source, dump
# `info functions` and the first 10 frames from a `bt`, then quit.
#
# Usage: tools/gdb-probe.sh <elf-path> [break-at-function]
set -u
ELF="${1:?usage: gdb-probe.sh <elf> [break-at-function]}"
BREAK="${2:-}"

[ -f "$ELF" ] || { echo "FAIL: $ELF missing"; exit 1; }

QEMU_LOG="/tmp/gdb-probe-qemu-$$.log"
GDB_CMD="/tmp/gdb-probe-cmds-$$.txt"

{
  echo "set arch i386:x86-64"
  echo "target remote :1234"
  [ -n "$BREAK" ] && echo "break $BREAK"
  echo "info functions"
  echo "continue &"
  echo "shell sleep 2"
  echo "interrupt"
  echo "bt 10"
  echo "quit"
} > "$GDB_CMD"

timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
    -display none -no-reboot -m 512 -s -S \
    -serial null > "$QEMU_LOG" 2>&1 &
QPID=$!
sleep 1

gdb -nx -batch -x "$GDB_CMD" "$ELF" 2>&1 | \
    grep -v "^(gdb) " | head -80

kill $QPID 2>/dev/null || true
wait 2>/dev/null || true
rm -f "$QEMU_LOG" "$GDB_CMD"
