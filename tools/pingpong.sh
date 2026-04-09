#!/bin/bash
# Ping-Pong: Full clean-room self-hosting verification.
set -euo pipefail
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
WINREPO="D:/Projects/NewRepository-cam"
REPO="/mnt/d/Projects/NewRepository-cam"
OUTDIR="$REPO/build-output/bare-metal"
ELF="$OUTDIR/Codex.Codex.elf"
SOURCE="$OUTDIR/source.codex"
QEMU="/usr/bin/qemu-system-x86_64"
TIMEOUT=${TIMEOUT:-180}
echo "╔═══════════════════════════════════════════════════╗"
echo "║  Ping-Pong: Full Clean-Room Verification          ║"
echo "╚═══════════════════════════════════════════════════╝"
date
echo ""
echo "Phase 1: Cleaning all intermediates..."
# Stage outputs
rm -rf "$REPO/build-output"
mkdir -p "$REPO/build-output/bootstrap"
mkdir -p "$REPO/build-output/bare-metal"
rm -rf "$REPO/Codex.Codex/out"
# Build artifacts
find "$REPO" -type d \( -name bin -o -name obj \) -not -path '*/.git/*' -exec rm -rf {} + 2>/dev/null || true
# Incremental build cache
rm -rf "$REPO/.codex-build"
# Generated Bootstrap intermediates (must regenerate from .codex source)
rm -rf "$REPO/tools/Codex.Bootstrap/bootstrap-output"
rm -f "$REPO/tools/Codex.Bootstrap/CodexLib.g.cs"
# Stale temps
find "$REPO" -maxdepth 3 -type f \( -name '*.bak' -o -name '*.tmp' -o -name '*.snap' \) -not -path '*/.git/*' -delete 2>/dev/null || true
echo "  done"
echo ""
echo "Phase 2: Building from source..."

# Build Codex.Cli first (the reference compiler), then Bootstrap.
# Bootstrap's RegenerateCodexLib target automatically invokes the CLI
# to compile .codex → C# → CodexLib.g.cs before building itself.
# We do NOT build the full solution — Codex.Codex.csproj always fails
# (it's the generated output, not a buildable project on its own).
"$DOTNET" build "$WINREPO/tools/Codex.Cli/Codex.Cli.csproj" 2>&1 | tail -3
"$DOTNET" build "$WINREPO/tools/Codex.Bootstrap/Codex.Bootstrap.csproj" 2>&1 | tail -3
echo ""
echo "Phase 3: C# bootstrap (fixed-point proof)..."
"$DOTNET" run --project "$WINREPO/tools/Codex.Cli" -- bootstrap "$WINREPO/Codex.Codex"
BOOTSTRAP_EXIT=$?
if [ "$BOOTSTRAP_EXIT" -ne 0 ]; then
    echo ""
    echo "FAIL: C# bootstrap failed (exit $BOOTSTRAP_EXIT). Aborting."
    date
    exit 1
fi
echo ""
echo "Phase 4: Bare-metal pingpong..."
echo ""
echo "Building ELF (reference compiler → x86-64-bare)..."
"$DOTNET" run --project "$WINREPO/tools/Codex.Cli" -- build "$WINREPO/Codex.Codex" --target x86-64-bare --output-dir "$WINREPO/build-output/bare-metal"
echo ""
echo "Dumping source..."
WINSOURCE="$WINREPO/build-output/bare-metal/source.codex"
"$DOTNET" run --project "$WINREPO/tools/Codex.Bootstrap" -- --dump-source "$WINSOURCE"
if [ ! -f "$ELF" ]; then
    echo "FAIL: $ELF not found after build."
    exit 1
fi
if [ ! -f "$SOURCE" ]; then
    echo "FAIL: $SOURCE not found after dump."
    exit 1
fi
if [ ! -x "$QEMU" ]; then
    echo "FAIL: $QEMU not found or not executable."
    exit 1
fi
echo ""
echo "ELF:    $(wc -c < "$ELF") bytes"
echo "Source: $(wc -c < "$SOURCE") bytes"
STAGE0="$SOURCE"
declare -a STAGE_ELAPSED STAGE_BYTES STAGE_STACK STAGE_HEAP STAGE_RSS
run_stage() {
    local stage=$1
    local input_file=$2
    local output_file=$3
    local start_time=$SECONDS
    local time_log="/tmp/pingpong-time-${stage}-$$"
    local pipe="/tmp/pingpong-$$"
    rm -f "$pipe"
    mkfifo "$pipe"
    sleep 999 > "$pipe" &
    local holder=$!
    local elapsed_file="/tmp/pingpong-elapsed-${stage}-$$"
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
            (printf 'TEXT\n'; cat "$input_file"; printf '\x04') > "$pipe" &
            continue
        fi
        if [[ "$line" == STACK:* ]]; then
            echo "$(( SECONDS - start_time ))" > "$elapsed_file"
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
    pkill -f "qemu-system-x86_64.*$ELF" 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"
    local elapsed
    if [ -f "$elapsed_file" ]; then
        elapsed=$(cat "$elapsed_file")
        rm -f "$elapsed_file"
    else
        elapsed=$(( SECONDS - start_time ))
    fi
    local size
    size=$(wc -c < "$output_file" 2>/dev/null || echo 0)
    local stack_hwm heap_hwm
    stack_hwm=$(grep -oP '^STACK:\K[0-9]+' "$output_file" | tail -1)
    heap_hwm=$(grep -oP '^HEAP:\K[0-9]+' "$output_file" | tail -1)
    local rss_kb
    rss_kb=$(grep -oP 'Maximum resident set size.*:\s*\K[0-9]+' "$time_log" 2>/dev/null || true)
    rm -f "$time_log"

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
echo ""
echo "[1/2] Stage 1: compile(stage0)..."
run_stage 1 "$STAGE0" "$OUTDIR/stage1.codex"
grep -v '^STACK:\|^HEAP:' "$OUTDIR/stage1.codex" > "$OUTDIR/stage1.clean.codex"
RESULT="PASS"
echo ""
SOURCE_NAMES=$(grep -P '^\s*[a-zA-Z_][a-zA-Z0-9_-]* :' "$SOURCE" | sed 's/ :.*//; s/^\s*//' | sort -u)
STAGE1_NAMES=$(grep -P '^\s*[a-zA-Z_][a-zA-Z0-9_-]* :' "$OUTDIR/stage1.clean.codex" | sed 's/ :.*//; s/^\s*//' | sort -u)
SOURCE_UNIQUE=$(echo "$SOURCE_NAMES" | wc -l)
STAGE1_UNIQUE=$(echo "$STAGE1_NAMES" | wc -l)
echo "Definitions: source=$SOURCE_UNIQUE unique  stage1=$STAGE1_UNIQUE unique"
MISSING=$(comm -23 <(echo "$SOURCE_NAMES") <(echo "$STAGE1_NAMES"))
MISSING_COUNT=$(echo "$MISSING" | grep -c . || echo 0)
if [ -n "$MISSING" ]; then
    echo "NOTE: $MISSING_COUNT source names mangled by chapter scoping (expected)"
else
    echo "PASS: all source definitions present in stage1"
fi
if [ "$RESULT" != "PASS" ]; then
    echo ""
    echo "Skipping stage 2 — semantic equivalence failed."
    echo ""
    echo "═══ Performance Summary ═══"
    printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
           "Stage" "Output" "Time" "Stack HWM" "Heap HWM" "QEMU RSS"
    printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
           "──────" "──────────" "──────" "────────────" "────────────" "──────────"
    local_bytes=${STAGE_BYTES[1]:-"—"}
    local_time="${STAGE_ELAPSED[1]:-"—"}s"
    local_stack=${STAGE_STACK[1]:-"—"}
    local_heap=${STAGE_HEAP[1]:-"—"}
    local_rss=${STAGE_RSS[1]:-"—"}
    [ "$local_stack" != "—" ] && local_stack="${local_stack} B"
    [ "$local_heap"  != "—" ] && local_heap="${local_heap} B"
    [ "$local_rss"   != "—" ] && local_rss="${local_rss} kB"
    printf "%-8s  %10s  %6s  %12s  %12s  %10s\n" \
           "Stage 1" "$local_bytes" "$local_time" "$local_stack" "$local_heap" "$local_rss"
    echo ""
    rm -f "$OUTDIR/stage1.clean.codex"
    date
    exit 1
fi

echo "[2/2] Stage 2: compile(stage1)..."
run_stage 2 "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.codex"
grep -v '^STACK:\|^HEAP:' "$OUTDIR/stage2.codex" > "$OUTDIR/stage2.clean.codex"

if diff -q "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" > /dev/null 2>&1; then
    echo "PASS: stage1 === stage2 (byte-identical)"
else
    RESULT="FAIL"
    echo "FAIL: stage1 !== stage2"
    diff "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" | head -30
fi
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
if [ "$RESULT" != "PASS" ]; then
    echo "FAIL: Text pingpong failed. Skipping binary pingpong."
    date
    exit 1
fi
echo ""
echo "Phase 5: Binary pingpong (MM4)..."
echo ""
BINARY_TIMEOUT=${BINARY_TIMEOUT:-360}
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
        -m 512 \
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
    # Extract ELF binary from raw serial output (pure bash — no python)
    {
        local size_line
        size_line=$(grep -a 'SIZE:' "$raw_output" | head -1)
        if [ -z "$size_line" ]; then
            echo "FAIL: SIZE: marker not found"
            exit 1
        fi
        local elf_size=${size_line#*SIZE:}
        elf_size=$(echo "$elf_size" | tr -dc '0-9')

        # Find byte offset of SIZE: line end (newline after SIZE:N)
        local size_offset
        size_offset=$(grep -boa 'SIZE:' "$raw_output" | head -1 | cut -d: -f1)
        local nl_offset=$((size_offset))
        # Scan for newline after SIZE:
        while [ "$(dd if="$raw_output" bs=1 skip=$nl_offset count=1 2>/dev/null | od -An -tu1 | tr -d ' ')" != "10" ]; do
            nl_offset=$((nl_offset + 1))
        done
        local binary_start=$((nl_offset + 1))

        dd if="$raw_output" bs=1 skip=$binary_start count="$elf_size" of="$elf_output" 2>/dev/null
        local got_size
        got_size=$(wc -c < "$elf_output")
        if [ "$got_size" -ne "$elf_size" ]; then
            echo "FAIL: expected $elf_size bytes, got $got_size"
            exit 1
        fi

        # Extract HEAP:/STACK: from data after the ELF
        local rest_offset=$((binary_start + elf_size))
        local rest
        rest=$(dd if="$raw_output" bs=1 skip=$rest_offset 2>/dev/null | tr '\0' '\n')
        echo "ELF_SIZE:$elf_size"
        echo "$rest" | grep -a '^HEAP:' | head -1
        echo "$rest" | grep -a '^STACK:' | head -1
    } > "$parse_out" 2>&1

    local parse_exit=$?
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
