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
# "STACK:nnnnn" after each compilation is the stack high-water mark.
# "HEAP:nnnnn" (emitted by allocator) is the heap high-water mark.
# Both are parsed and reported in the summary table.
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
TIMEOUT=120

# ── Per-stage stats (indexed 1, 2) ───────────────────────────
declare -a STAGE_ELAPSED STAGE_BYTES STAGE_STACK STAGE_HEAP STAGE_RSS

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

# Stage0 = source as-is. The compiler must handle any whitespace internally.
# No external normalization — if the compiler needs it, it should do it itself.
STAGE0="$SOURCE"

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
    local time_log="/tmp/pingpong-time-${stage}-$$"

    # Handshake: kernel prints "READY\n" after COM1 init. We hold the pipe
    # open with a background sleep, wait for READY, then send source.
    local pipe="/tmp/pingpong-$$"
    rm -f "$pipe"
    mkfifo "$pipe"
    sleep 999 > "$pipe" &
    local holder=$!

    # Wrap QEMU in /usr/bin/time -v to capture peak RSS of the guest process.
    timeout "$TIMEOUT" /usr/bin/time -v -o "$time_log" \
        "$QEMU" \
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
        if [[ "$line" == HEAP:* ]]; then
            echo "$line"
            continue
        fi
        echo "$line"
    done > "$output_file" || true

    kill "$holder" 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"

    local elapsed=$(( SECONDS - start_time ))

    local size
    size=$(wc -c < "$output_file" 2>/dev/null || echo 0)

    # Parse guest-emitted diagnostics from output
    local stack_hwm heap_hwm
    stack_hwm=$(grep -oP '^STACK:\K[0-9]+' "$output_file" | tail -1)
    heap_hwm=$(grep -oP '^HEAP:\K[0-9]+' "$output_file" | tail -1)

    # Parse QEMU process peak RSS from /usr/bin/time output (kB)
    local rss_kb
    rss_kb=$(grep -oP 'Maximum resident set size.*:\s*\K[0-9]+' "$time_log" 2>/dev/null || true)
    rm -f "$time_log"

    # Store stats
    STAGE_ELAPSED[$stage]=$elapsed
    STAGE_BYTES[$stage]=$size
    STAGE_STACK[$stage]=${stack_hwm:-"—"}
    STAGE_HEAP[$stage]=${heap_hwm:-"—"}
    STAGE_RSS[$stage]=${rss_kb:-"—"}

    echo "  Stage $stage: $size bytes (${elapsed}s)"
    [ -n "${stack_hwm:-}" ] && echo "    stack hwm: ${stack_hwm} bytes"
    [ -n "${heap_hwm:-}" ]  && echo "    heap hwm:  ${heap_hwm} bytes"
    [ -n "${rss_kb:-}" ]    && echo "    QEMU RSS:  ${rss_kb} kB"

    if [ "$size" -lt 100 ]; then
        echo "FAIL: Stage $stage output too small ($size bytes)"
        cat "$output_file" 2>/dev/null || true
        exit 1
    fi
}

# ── Stage 1: Codex source → bare-metal compiler → Codex output ──

echo ""
echo "[1/2] Stage 1: compile(stage0)..."
run_stage 1 "$STAGE0" "$OUTDIR/stage1.codex"

# ── Stage 2: feed Stage 1 output back in ──

# Strip STACK:/HEAP: diagnostics (not compiler output)
grep -v '^STACK:\|^HEAP:' "$OUTDIR/stage1.codex" > "$OUTDIR/stage1.clean.codex"

echo "[2/2] Stage 2: compile(stage1)..."
run_stage 2 "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.codex"

# ── Compare ──────────────────────────────────────────────────

grep -v '^STACK:\|^HEAP:' "$OUTDIR/stage2.codex" > "$OUTDIR/stage2.clean.codex"

STAGE1_SIZE=$(wc -c < "$OUTDIR/stage1.clean.codex")
STAGE2_SIZE=$(wc -c < "$OUTDIR/stage2.clean.codex")

RESULT="PASS"
echo ""

# a == b: source and stage1 must have the same definitions
SOURCE_DEFS=$(grep -cP '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$SOURCE" || echo 0)
STAGE1_DEFS=$(grep -cP '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$OUTDIR/stage1.clean.codex" || echo 0)

echo "Definitions: source=$SOURCE_DEFS  stage1=$STAGE1_DEFS"

if [ "$SOURCE_DEFS" != "$STAGE1_DEFS" ]; then
    RESULT="FAIL"
    echo "FAIL: Definition count mismatch (source=$SOURCE_DEFS, stage1=$STAGE1_DEFS)"
    diff <(grep -P '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$SOURCE" | sed 's/ :.*//') \
         <(grep -P '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$OUTDIR/stage1.clean.codex" | sed 's/ :.*//') | head -30
else
    # Verify definition names match (ignoring type signatures since type vars differ)
    SOURCE_NAMES=$(grep -P '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$SOURCE" | sed 's/ :.*//')
    STAGE1_NAMES=$(grep -P '^[a-zA-Z_][a-zA-Z0-9_-]* :' "$OUTDIR/stage1.clean.codex" | sed 's/ :.*//')
    if [ "$SOURCE_NAMES" != "$STAGE1_NAMES" ]; then
        RESULT="FAIL"
        echo "FAIL: Definition names differ between source and stage1"
        diff <(echo "$SOURCE_NAMES") <(echo "$STAGE1_NAMES") | head -30
    else
        echo "PASS: source == stage1 (same definitions, same order)"
    fi
fi

# b === c: stage1 and stage2 must be byte-identical
if diff -q "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" > /dev/null 2>&1; then
    echo "PASS: stage1 === stage2 (byte-identical)"
else
    RESULT="FAIL"
    echo "FAIL: stage1 !== stage2"
    diff "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" | head -30
fi

# ── Summary table ─────────────────────────────────────────────
#
# Columns: stage, output bytes, wall-clock, stack HWM, heap HWM, QEMU RSS.
# Heap HWM requires allocator emission (HEAP:nnnnn on serial).
# QEMU RSS is the host-side peak resident size of the qemu-system process.

echo ""
echo "═══ Performance Summary ═══"
printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
       "Stage" "Output" "Time" "Stack HWM" "Heap HWM" "QEMU RSS"
printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
       "──────" "──────────" "──────" "────────────" "────────────" "──────────"
for s in 1 2; do
    local_bytes=${STAGE_BYTES[$s]:-"—"}
    local_time="${STAGE_ELAPSED[$s]:-"—"}s"
    local_stack=${STAGE_STACK[$s]:-"—"}
    local_heap=${STAGE_HEAP[$s]:-"—"}
    local_rss=${STAGE_RSS[$s]:-"—"}
    [ "$local_stack" != "—" ] && local_stack="${local_stack} B"
    [ "$local_heap"  != "—" ] && local_heap="${local_heap} B"
    [ "$local_rss"   != "—" ] && local_rss="${local_rss} kB"
    printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
           "Stage $s" "$local_bytes" "$local_time" "$local_stack" "$local_heap" "$local_rss"
done
echo ""

rm -f "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex"
date

if [ "$RESULT" = "PASS" ]; then
    exit 0
else
    exit 1
fi
