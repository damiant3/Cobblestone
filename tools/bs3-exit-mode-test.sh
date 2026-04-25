#!/bin/bash
set -u
cd "$(dirname "$0")/.."
QEMU=/usr/bin/qemu-system-x86_64
QEMU_ARGS=(-enable-kvm -serial stdio -display none -no-reboot -m 1024 -device isa-debug-exit,iobase=0xf4,iosize=0x04)

echo "=== repl ELF (expect halt loop, wall-killed at 5s) ==="
start=$SECONDS
timeout 5 "$QEMU" "${QEMU_ARGS[@]}" -kernel build-output/exit-test/hello-repl.elf < /dev/null > /tmp/exit-test-repl-out 2>/dev/null
qe=$?
echo "qemu exit=$qe after $((SECONDS-start))s"
echo "bytes=$(wc -c < /tmp/exit-test-repl-out)"
head -10 /tmp/exit-test-repl-out

echo ""
echo "=== qemu-exit ELF (expect clean exit <5s, exit code 1) ==="
start=$SECONDS
timeout 5 "$QEMU" "${QEMU_ARGS[@]}" -kernel build-output/exit-test/hello-qemuexit.elf < /dev/null > /tmp/exit-test-qe-out 2>/dev/null
qe=$?
echo "qemu exit=$qe after $((SECONDS-start))s"
echo "bytes=$(wc -c < /tmp/exit-test-qe-out)"
head -10 /tmp/exit-test-qe-out
