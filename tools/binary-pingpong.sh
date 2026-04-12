#!/bin/bash
# Binary (MM4) pingpong. Assumes tools/pingpong.sh has already run
# successfully and left $OUTDIR populated with the reference ELF and
# source dump. Runs the bare-metal binary self-compilation fixed-point
# check — Stage 0 compiles source to ELF, Stage 1 ELF compiles source
# to ELF, both ELFs must be byte-identical.
set -euo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
OUTDIR="$REPO/build-output/bare-metal"
ELF="$OUTDIR/Codex.Codex.elf"
SOURCE="$OUTDIR/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
BINARY_TIMEOUT=${BINARY_TIMEOUT:-360}

if [ ! -f "$ELF" ] || [ ! -f "$SOURCE" ]; then
    echo "FAIL: $ELF or $SOURCE missing. Run tools/pingpong.sh first."
    exit 1
fi
if [ ! -x "$QEMU" ]; then
    echo "FAIL: $QEMU not found or not executable."
    exit 1
fi

echo "╔═══════════════════════════════════════════════════╗"
echo "║  Binary Pingpong (MM4)                            ║"
echo "╚═══════════════════════════════════════════════════╝"
date
echo ""
echo "ELF:    $(wc -c < "$ELF") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"
echo ""

run_binary_stage() {
    local stage=$1
    local input_file=$2
    local kernel_elf=$3
    local elf_output=$4

    local raw_output="/tmp/pingpong-binary-raw-${stage}-$$"
    local pipe="/tmp/pingpong-binary-pipe-$$"
    local parse_out="/tmp/pingpong-binary-parse-${stage}-$$"
    rm -f "$pipe" "$raw_output" "$parse_out"
    mkfifo "$pipe"
    sleep 999 > "$pipe" &
    local holder=$!
    local start_time=$SECONDS

    timeout "$BINARY_TIMEOUT" "$QEMU" \
        -enable-kvm \
        -kernel "$kernel_elf" \
        -serial stdio \
        -display none \
        -no-reboot \
        -m 1024 \
        < "$pipe" > "$raw_output" 2>/dev/null &
    local qemu_pid=$!
    local ready_wait=0
    while ! grep -qa 'READY' "$raw_output" 2>/dev/null; do
        sleep 0.2
        ready_wait=$((ready_wait + 1))
        if [ "$ready_wait" -gt 100 ]; then
            echo "FAIL: READY not received within 20s"
            kill $qemu_pid 2>/dev/null || true
            kill $holder 2>/dev/null || true
            wait 2>/dev/null || true
            rm -f "$pipe" "$raw_output"
            exit 1
        fi
        kill -0 $qemu_pid 2>/dev/null || break
    done
    (printf 'BINARY\n'; cat "$input_file"; printf '\x04') > "$pipe" &
    local prev_size=0
    local stable_count=0
    while true; do
        sleep 2
        local cur_size
        cur_size=$(wc -c < "$raw_output" 2>/dev/null || echo 0)
        if [ "$cur_size" -gt 100 ] && [ "$cur_size" -eq "$prev_size" ]; then
            stable_count=$((stable_count + 1))
            if [ "$stable_count" -ge 2 ]; then
                break
            fi
        else
            stable_count=0
        fi
        prev_size=$cur_size
        kill -0 $qemu_pid 2>/dev/null || break
    done

    local elapsed=$(( SECONDS - start_time ))

    kill $qemu_pid 2>/dev/null || true
    kill $holder 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"
    # Extract ELF binary from raw serial output
    local parse_exit=0
    if ! grep -qa 'SIZE:' "$raw_output"; then
        echo "FAIL: SIZE: marker not found" > "$parse_out"
        parse_exit=1
    else
        local size_line elf_size size_byte_off binary_start
        size_line=$(grep -a 'SIZE:' "$raw_output" | head -1)
        elf_size=${size_line#*SIZE:}
        elf_size=${elf_size%%[!0-9]*}
        size_byte_off=$(grep -boa 'SIZE:' "$raw_output" | head -1 | cut -d: -f1)
        binary_start=$((size_byte_off + 5 + ${#elf_size} + 1))
        dd if="$raw_output" bs=1 skip="$binary_start" count="$elf_size" of="$elf_output" 2>/dev/null
        local got_size
        got_size=$(wc -c < "$elf_output")
        if [ "$got_size" -ne "$elf_size" ]; then
            echo "FAIL: expected $elf_size bytes, got $got_size" > "$parse_out"
            parse_exit=1
        else
            {
                echo "ELF_SIZE:$elf_size"
                dd if="$raw_output" bs=1 skip=$((binary_start + elf_size)) 2>/dev/null | \
                    grep -a '^HEAP:\|^STACK:' | head -2
            } > "$parse_out"
        fi
    fi
    rm -f "$raw_output"

    if [ "$parse_exit" -ne 0 ]; then
        echo "FAIL: Binary parse failed for stage $stage"
        cat "$parse_out"
        rm -f "$parse_out"
        exit 1
    fi
    local elf_size heap_hwm stack_hwm
    elf_size=$(grep -oP '^ELF_SIZE:\K[0-9]+' "$parse_out" || echo 0)
    heap_hwm=$(grep -oP '^HEAP:\K[0-9]+' "$parse_out" || true)
    stack_hwm=$(grep -oP '^STACK:\K[0-9]+' "$parse_out" || true)
    rm -f "$parse_out"
    echo "  Stage $stage: $elf_size bytes ELF (${elapsed}s)"
    [ -n "${heap_hwm:-}" ]  && echo "    heap hwm:  ${heap_hwm} bytes"
    [ -n "${stack_hwm:-}" ] && echo "    stack hwm: ${stack_hwm} bytes"

    if [ "$elf_size" -lt 100 ]; then
        echo "FAIL: Stage $stage ELF too small ($elf_size bytes)"
        exit 1
    fi
}

echo "[1/2] Binary Stage 1: Stage 0 compiles source → ELF..."
run_binary_stage 1 "$SOURCE" "$ELF" "$OUTDIR/stage1.elf"
echo ""
echo "[2/2] Binary Stage 2: Stage 1 ELF compiles source → ELF..."
run_binary_stage 2 "$SOURCE" "$OUTDIR/stage1.elf" "$OUTDIR/stage2.elf"
echo ""
if cmp -s "$OUTDIR/stage1.elf" "$OUTDIR/stage2.elf"; then
    S1_SIZE=$(wc -c < "$OUTDIR/stage1.elf")
    echo "═══════════════════════════════════════════════════"
    echo "  MM4 PROVEN: Stage 1 ELF === Stage 2 ELF"
    echo "  ($S1_SIZE bytes, byte-identical)"
    echo "  The cord is cut."
    echo "═══════════════════════════════════════════════════"
    RESULT="PASS"
else
    echo "FAIL: Stage 1 ELF !== Stage 2 ELF"
    echo "  Stage 1: $(wc -c < "$OUTDIR/stage1.elf") bytes"
    echo "  Stage 2: $(wc -c < "$OUTDIR/stage2.elf") bytes"
    RESULT="FAIL"
fi
echo ""
date
if [ "$RESULT" = "PASS" ]; then
    exit 0
else
    exit 1
fi
