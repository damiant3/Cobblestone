#!/bin/bash
# Run a bare-metal Codex ELF in QEMU (no input required), capture serial output.
set -euo pipefail
ELF="${1:?usage: run-bare-elf.sh <elf>}"
QEMU="/usr/bin/qemu-system-x86_64"
TIMEOUT=${TIMEOUT:-15}
RAW="/tmp/run-bare-raw-$$"
rm -f "$RAW"
timeout "$TIMEOUT" "$QEMU" \
    -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 256 \
    > "$RAW" 2>/dev/null || true
strings -n 1 "$RAW" | head -50
rm -f "$RAW"
