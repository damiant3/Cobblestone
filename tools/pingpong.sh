#!/bin/bash
# Ping-Pong: Bare-metal self-compile fixed-point proof.
#
# Codex code (a) → Codex compiler on bare metal → Codex code (b)
#                → Codex compiler on bare metal → Codex code (c)
#                → b == c? PASS. (fixed point)
#
# The bare-metal ELF IS the Codex compiler. It boots on QEMU with KVM
# (hardware virtualization — near-native speed), reads source via serial,
# compiles using the Codex emitter, writes Codex code back via serial.
# "STACK:nnnnn" after each compilation is the completion marker.
#
# Prerequisites (built on Windows before calling this script):
#   dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare
#   dotnet run --project tools/Codex.Bootstrap -- --dump-source Codex.Codex/out/source.codex
#
# Usage: wsl bash tools/pingpong.sh
# Exit 0 = fixed point holds. Exit 1 = broken.

set -euo pipefail

REPO="/mnt/d/Projects/NewRepository-cam"
OUTDIR="$REPO/Codex.Codex/out"
ELF="$OUTDIR/Codex.Codex.elf"
SOURCE="$OUTDIR/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
TIMEOUT=60

echo "=== Ping-Pong: Bare-Metal Self-Compile ==="
date

# ── Verify prerequisites ─────────────────────────────────────

if [ ! -f "$ELF" ]; then
    echo "FAIL: $ELF not found."
    echo "Run: dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare"
    exit 1
fi
if [ ! -f "$SOURCE" ]; then
    echo "FAIL: $SOURCE not found."
    echo "Run: dotnet run --project tools/Codex.Bootstrap -- --dump-source Codex.Codex/out/source.codex"
    exit 1
fi
if [ ! -x "$QEMU" ]; then
    echo "FAIL: $QEMU not found or not executable."
    exit 1
fi

echo "ELF:    $(wc -c < "$ELF") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"

# ── Run one compilation stage ─────────────────────────────────
#
# Serial protocol (bare-metal main.codex):
#   read-line → returns hardcoded "test.codex" (path, ignored)
#   read-file → reads serial until \x04 EOT, Unicode→CCE at boundary
#   compile-streaming-v2 → prints Codex code via serial, CCE→Unicode at boundary
#   REPL loop prints "STACK:nnnnn\n" after each compilation

run_stage() {
    local stage=$1
    local input_file=$2
    local output_file=$3

    local start_time=$SECONDS

    # Handshake: kernel prints "READY\n" after COM1 init. We hold the pipe
    # open with a background sleep, wait for READY, then send source.
    local pipe="/tmp/pingpong-$$"
    rm -f "$pipe"
    mkfifo "$pipe"
    sleep 999 > "$pipe" &
    local holder=$!

    timeout "$TIMEOUT" "$QEMU" \
        -enable-kvm \
        -kernel "$ELF" \
        -serial stdio \
        -display none \
        -no-reboot \
        -m 512 \
        < "$pipe" 2>/dev/null \
    | while IFS= read -r line; do
        if [[ "$line" == READY* ]]; then
            (cat "$input_file"; printf '\x04') > "$pipe" &
            continue
        fi
        if [[ "$line" == STACK:* ]]; then
            echo "$line"
            break
        fi
        echo "$line"
    done > "$output_file" || true

    kill "$holder" 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"

    local elapsed=$(( SECONDS - start_time ))

    local size
    size=$(wc -c < "$output_file" 2>/dev/null || echo 0)
    echo "  Stage $stage: $size bytes (${elapsed}s)"

    if [ "$size" -lt 100 ]; then
        echo "FAIL: Stage $stage output too small ($size bytes)"
        cat "$output_file" 2>/dev/null || true
        exit 1
    fi
}

# ── Stage 1: Codex source → bare-metal compiler → Codex output ──

echo ""
echo "[1/2] Stage 1..."
run_stage 1 "$SOURCE" "$OUTDIR/stage1.codex"

# ── Stage 2: feed Stage 1 output back in ──

grep -v '^STACK:' "$OUTDIR/stage1.codex" | cat -s > "$OUTDIR/stage1.clean.codex"

echo "[2/2] Stage 2..."
run_stage 2 "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.codex"

# ── Compare ──────────────────────────────────────────────────

grep -v '^STACK:' "$OUTDIR/stage2.codex" | cat -s > "$OUTDIR/stage2.clean.codex"

STAGE1_SIZE=$(wc -c < "$OUTDIR/stage1.clean.codex")
STAGE2_SIZE=$(wc -c < "$OUTDIR/stage2.clean.codex")

echo ""
if diff -q "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" > /dev/null 2>&1; then
    echo "PASS: Fixed point verified."
    echo "  Stage 1: $STAGE1_SIZE bytes"
    echo "  Stage 2: $STAGE2_SIZE bytes"
    rm -f "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex"
    date
    exit 0
else
    echo "FAIL: Stage 1 != Stage 2"
    echo "  Stage 1: $STAGE1_SIZE bytes"
    echo "  Stage 2: $STAGE2_SIZE bytes"
    diff "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" | head -30
    rm -f "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex"
    date
    exit 1
fi
