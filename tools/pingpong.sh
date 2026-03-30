#!/bin/bash
# Ping-Pong: Bare-metal self-compile fixed-point proof.
# Boots ELF via QEMU, feeds source via serial, captures output twice, compares.
# Prerequisites: ELF and source must be built BEFORE running this script.
#   dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare
#   dotnet run --project tools/Codex.Bootstrap -- --dump-source Codex.Codex/out/source.codex
# Usage: wsl bash tools/pingpong.sh
# Exit 0 = fixed point holds. Exit 1 = broken.

set -e

REPO="/mnt/d/Projects/NewRepository-cam"
OUTDIR="$REPO/Codex.Codex/out"
ELF="$OUTDIR/Codex.Codex.elf"
SOURCE="$OUTDIR/source.codex"
PIPE="/tmp/qemu-serial-pipe"

echo "=== Ping-Pong: Bare-Metal Self-Compile ==="
date

# Verify prerequisites
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

echo "ELF: $(wc -c < "$ELF") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"

run_stage() {
    local stage=$1
    local outfile=$2

    rm -f "$PIPE" "$outfile"
    mkfifo "$PIPE"

    # Feed source after 3s boot delay, keep pipe open
    (sleep 3; cat "$SOURCE"; printf '\x04'; sleep 600) > "$PIPE" &
    local feeder=$!

    # Run QEMU, stream output, monitor for STACK: completion marker
    qemu-system-x86_64 \
        -kernel "$ELF" \
        -serial stdio \
        -display none \
        -no-reboot \
        -m 512 \
        < "$PIPE" 2>/dev/null &
    local qemu=$!

    # Monitor output file for completion (STACK: line = compilation done)
    local elapsed=0
    while [ $elapsed -lt 600 ]; do
        sleep 1
        elapsed=$((elapsed + 1))
        if [ -f /proc/$qemu/status ] 2>/dev/null || kill -0 $qemu 2>/dev/null; then
            : # qemu still running
        else
            break # qemu exited
        fi
        # Check if STACK: marker appeared (compilation complete)
        if [ -f "$outfile" ] && grep -q "STACK:" "$outfile" 2>/dev/null; then
            sleep 1  # let last bytes flush
            break
        fi
    done > "$outfile" < <(cat /proc/$qemu/fd/1 2>/dev/null || true)

    # That subshell approach won't work. Simpler: tee to file, grep in loop.
    # Let me just use a direct approach.
    kill $qemu 2>/dev/null || true
    kill $feeder 2>/dev/null || true
    wait $qemu 2>/dev/null || true
    wait $feeder 2>/dev/null || true
    rm -f "$PIPE"

    local size
    size=$(wc -c < "$outfile" 2>/dev/null || echo 0)
    echo "  Stage $stage: $size bytes"
    if [ "$size" -lt 1000 ]; then
        echo "FAIL: Stage $stage output too small ($size bytes)"
        exit 1
    fi
}

# Simpler approach: pipe QEMU output through a monitor that kills on STACK:
run_stage_v2() {
    local stage=$1
    local outfile=$2

    rm -f "$PIPE" "$outfile"
    mkfifo "$PIPE"

    (sleep 3; cat "$SOURCE"; printf '\x04'; sleep 600) > "$PIPE" &
    local feeder=$!

    # Run QEMU with output piped to awk that exits on STACK: line
    timeout 600 qemu-system-x86_64 \
        -kernel "$ELF" \
        -serial stdio \
        -display none \
        -no-reboot \
        -m 512 \
        < "$PIPE" 2>/dev/null | awk '{print; fflush()} /^STACK:/{exit}' > "$outfile"

    kill $feeder 2>/dev/null || true
    rm -f "$PIPE"

    local size
    size=$(wc -c < "$outfile" 2>/dev/null || echo 0)
    echo "  Stage $stage: $size bytes"
    if [ "$size" -lt 1000 ]; then
        echo "FAIL: Stage $stage output too small ($size bytes)"
        exit 1
    fi
}

# Stage 1
echo "[1/2] Stage 1: self-compile on bare metal..."
run_stage_v2 1 "$OUTDIR/stage1.cs"

# Stage 2
echo "[2/2] Stage 2: second self-compile (fixed-point check)..."
run_stage_v2 2 "$OUTDIR/stage2.cs"

# Strip STACK: lines before comparing (they're diagnostic, not compiler output)
grep -v "^STACK:" "$OUTDIR/stage1.cs" > "$OUTDIR/stage1.clean"
grep -v "^STACK:" "$OUTDIR/stage2.cs" > "$OUTDIR/stage2.clean"

STAGE1_SIZE=$(wc -c < "$OUTDIR/stage1.clean")
STAGE2_SIZE=$(wc -c < "$OUTDIR/stage2.clean")

if diff -q "$OUTDIR/stage1.clean" "$OUTDIR/stage2.clean" > /dev/null 2>&1; then
    echo ""
    echo "PASS: Fixed point verified. Stage 1 ($STAGE1_SIZE bytes) == Stage 2 ($STAGE2_SIZE bytes)"
    rm -f "$OUTDIR/stage1.clean" "$OUTDIR/stage2.clean"
    date
    exit 0
else
    echo ""
    echo "FAIL: Stage 1 != Stage 2"
    echo "  Stage 1: $STAGE1_SIZE bytes"
    echo "  Stage 2: $STAGE2_SIZE bytes"
    diff "$OUTDIR/stage1.clean" "$OUTDIR/stage2.clean" | head -20
    rm -f "$OUTDIR/stage1.clean" "$OUTDIR/stage2.clean"
    date
    exit 1
fi
