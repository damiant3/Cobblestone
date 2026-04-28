#!/bin/bash
# pingpong-self.sh: Bootstrap-2 acceptance test driven entirely by the
# self-built bare-metal compiler. No .NET, no REF.
#
# Layout:
#   Phase 1  Clean intermediates.
#   Phase 2  Stage the canonical seed ELF (seed/Codex.Codex.elf) and
#            dump Codex.Codex source via tools/concat-codex-self.sh.
#   Phase 3  Self-build: run the seed ELF in QEMU with BINARY mode on
#            source.codex to produce sut.elf. This ELF — emitted by
#            the selfhost from selfhost source — is the SUT.
#   Phase 4  Pingpong: run sut.elf in QEMU twice (TEXT mode) to produce
#            stage1.codex and stage2.codex. Verify byte-identity.
#            Because both compilations run on the same self-built SUT,
#            stage1 === stage2 already proves SUT(source) == SUT(stage1)
#            — semantic equivalence under the SUT, no separate ref
#            sem-equiv step required.
#
# QEMU is driven via two -chardev sockets: COM1 (ch0, FD 3) data, COM2
# (ch1, FD 4) control. The current depot seed is single-chardev — it
# writes READY (and everything else) on COM1; the new source compiles
# READY onto COM2. qemu_read_ready accepts READY on EITHER FD so the
# same harness drives both seeds during the transition. Once the depot
# seed is rebuilt with the dual-chardev startup, this same harness
# automatically reads READY from FD 4 instead of FD 3 — no script change
# required. See tools/qemu-config.sh.
#
# Set QEMU_ACCEL=whpx to use WHPX so peers using KVM aren't blocked
# behind the single shared WSL utility VM.
#
# The seed ELF is checked in at //Codex/main/seed/Codex.Codex.elf.
# Refresh it in the same CL as any compiler change that selfhost
# source uses, otherwise Phase 3 fails on the new construct.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
OUTDIR="$REPO/build-output/bare-metal"
SEED_ELF="$OUTDIR/seed.elf"
SUT_ELF="$OUTDIR/Codex.Codex.elf"
SOURCE="$OUTDIR/source.codex"
SEED_DEPOT="$REPO/seed/Codex.Codex.elf"
CONCAT="$SCRIPT_DIR/concat-codex-self.sh"
. "$SCRIPT_DIR/qemu-config.sh"
TEXT_TIMEOUT=${TIMEOUT:-1200}
BINARY_TIMEOUT=${BINARY_TIMEOUT:-360}
echo "╔═══════════════════════════════════════════════════╗"
echo "║  Ping-Pong (selfhost): BS2 via checked-in seed    ║"
echo "║  accel=$QEMU_ACCEL                                       "
echo "╚═══════════════════════════════════════════════════╝"
date
echo ""
echo "Phase 1: Cleaning intermediates..."
rm -rf "$REPO/build-output"
mkdir -p "$REPO/build-output/bare-metal"
find "$REPO" -maxdepth 3 -type f \( -name '*.bak' -o -name '*.tmp' -o -name '*.snap' \) -not -path '*/.git/*' -delete 2>/dev/null || true
echo "  done"
echo ""
echo "Phase 2: Staging seed ELF and dumping source..."
[ -f "$SEED_DEPOT" ] || { echo "FAIL: $SEED_DEPOT missing — sync //Codex/main/seed/"; exit 1; }
[ -x "$CONCAT" ]     || { echo "FAIL: $CONCAT missing or not executable"; exit 1; }
[ -x "$QEMU" ]       || { echo "FAIL: qemu missing at $QEMU"; exit 1; }
cp "$SEED_DEPOT" "$SEED_ELF"
"$CONCAT" "$REPO/Codex.Codex" > "$SOURCE"
echo "Seed ELF: $(wc -c < "$SEED_ELF") bytes"
echo "Source:   $(wc -c < "$SOURCE") bytes"
echo ""
declare -a STAGE_ELAPSED STAGE_BYTES STAGE_STACK STAGE_HEAP

# BINARY-mode QEMU run: capture an ELF emitted on serial via socket.
build_sut() {
    local input_file=$1
    local kernel_elf=$2
    local elf_output=$3
    local data_port ctrl_port qemu_pid attempt
    local start_time=$SECONDS
    qemu_pid=
    for attempt in 0 1 2 3; do
        data_port=$(qemu_alloc_port $attempt)
        ctrl_port=$((data_port + 1))
        timeout "$BINARY_TIMEOUT" "$QEMU" \
            "${QEMU_ACCEL_FLAGS[@]}" \
            -kernel "$(qemu_path "$kernel_elf")" \
            -chardev "$(qemu_chardev $data_port)" \
            -chardev "$(qemu_chardev_ctrl $ctrl_port)" \
            -serial chardev:ch0 \
            -serial chardev:ch1 \
            -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
            -display none \
            -no-reboot \
            -m 1024 \
            > /dev/null 2>&1 &
        qemu_pid=$!
        sleep 0.5
        if kill -0 $qemu_pid 2>/dev/null && qemu_wait_listen "$data_port" "$ctrl_port"; then
            break
        fi
        kill $qemu_pid 2>/dev/null || true
        wait 2>/dev/null || true
        qemu_pid=
    done
    if [ -z "$qemu_pid" ]; then
        echo "FAIL: QEMU did not listen after 4 attempts (build_sut)"
        exit 1
    fi
    if ! qemu_read_ready 60; then
        echo "FAIL: READY not received within 60s (build_sut)"
        exec 3>&- 4>&- 2>/dev/null || true
        kill $qemu_pid 2>/dev/null || true
        exit 1
    fi
    { printf 'BINARY\n'; cat "$input_file"; printf '\x04'; } >&3
    local elf_size="" status=""
    while IFS= read -r -t "$BINARY_TIMEOUT" line <&3; do
        if [[ "$line" == SIZE:* ]]; then
            elf_size=${line#SIZE:}
            elf_size=${elf_size%%[!0-9]*}
            status=size
            break
        fi
        if [[ "$line" == CODEGEN-HALTED* ]] || [[ "$line" == CODEGEN-ERRORS* ]]; then
            echo "FAIL: $line (build_sut)"
            status=halted
            break
        fi
    done
    if [ "$status" != "size" ]; then
        echo "FAIL: SIZE: marker not found (build_sut)"
        exec 3>&- 4>&- 2>/dev/null || true
        kill $qemu_pid 2>/dev/null || true
        exit 1
    fi
    head -c "$elf_size" <&3 > "$elf_output"
    local got_size
    got_size=$(wc -c < "$elf_output")
    # Drain post-ELF output for HEAP:/STACK: markers — the seed's emit-start
    # prints these after opening returns, so they come out after the ELF
    # bytes finish on COM1. Capturing them lets us track binary-mode HWM,
    # which is the actual cost of seed→SUT and where memory savings show
    # up (text-mode pingpong stages don't exercise x86-64 emit paths).
    local build_heap_hwm="" build_stack_hwm=""
    while IFS= read -r -t 5 line <&3; do
        if [[ "$line" == HEAP:* ]]; then
            build_heap_hwm=${line#HEAP:}
        elif [[ "$line" == STACK:* ]]; then
            build_stack_hwm=${line#STACK:}
            break
        fi
    done
    exec 3>&- 4>&- 2>/dev/null || true
    kill $qemu_pid 2>/dev/null || true
    wait 2>/dev/null || true
    if [ "$got_size" -ne "$elf_size" ]; then
        echo "FAIL: expected $elf_size bytes, got $got_size (build_sut)"
        exit 1
    fi
    local elapsed=$(( SECONDS - start_time ))
    echo "  Self-built SUT: $elf_size bytes (${elapsed}s)"
    [ -n "$build_heap_hwm" ] && echo "    build heap hwm: ${build_heap_hwm} bytes"
    [ -n "$build_stack_hwm" ] && echo "    build stack hwm: ${build_stack_hwm} bytes"
}

# TEXT-mode QEMU run: SUT compiles input → codex text on serial.
run_text_stage() {
    local stage=$1
    local input_file=$2
    local output_file=$3
    local data_port ctrl_port qemu_pid attempt
    local start_time=$SECONDS
    qemu_pid=
    for attempt in 0 1 2 3; do
        data_port=$(qemu_alloc_port $attempt)
        ctrl_port=$((data_port + 1))
        timeout "$TEXT_TIMEOUT" "$QEMU" \
            "${QEMU_ACCEL_FLAGS[@]}" \
            -kernel "$(qemu_path "$SUT_ELF")" \
            -chardev "$(qemu_chardev $data_port)" \
            -chardev "$(qemu_chardev_ctrl $ctrl_port)" \
            -serial chardev:ch0 \
            -serial chardev:ch1 \
            -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
            -display none \
            -no-reboot \
            -m 1024 \
            > /dev/null 2>&1 &
        qemu_pid=$!
        sleep 0.5
        if kill -0 $qemu_pid 2>/dev/null && qemu_wait_listen "$data_port" "$ctrl_port"; then
            break
        fi
        kill $qemu_pid 2>/dev/null || true
        wait 2>/dev/null || true
        qemu_pid=
    done
    if [ -z "$qemu_pid" ]; then
        echo "FAIL: QEMU did not listen after 4 attempts (run_text_stage $stage)"
        exit 1
    fi
    if ! qemu_read_ready 60; then
        echo "FAIL: READY not received within 60s (run_text_stage $stage)"
        exec 3>&- 4>&- 2>/dev/null || true
        kill $qemu_pid 2>/dev/null || true
        exit 1
    fi
    { printf 'TEXT\n'; cat "$input_file"; printf '\x04'; } >&3
    : > "$output_file"
    while IFS= read -r line <&3; do
        echo "$line" >> "$output_file"
        # Surface watchdog warnings to the operator live — they're
        # otherwise buried in stage1.codex and stripped before review.
        [[ "$line" == WD:* ]] && echo ">>> $line" >&2
        [[ "$line" == STACK:* ]] && break
    done
    exec 3>&- 4>&- 2>/dev/null || true
    kill $qemu_pid 2>/dev/null || true
    wait 2>/dev/null || true
    local elapsed=$(( SECONDS - start_time ))
    local size
    size=$(wc -c < "$output_file" 2>/dev/null || echo 0)
    local stack_hwm heap_hwm
    stack_hwm=$(awk -F: '/^STACK:/ {print $2; exit}' "$output_file")
    heap_hwm=$(awk -F: '/^HEAP:/ {print $2; exit}' "$output_file")
    STAGE_ELAPSED[$stage]=$elapsed
    STAGE_BYTES[$stage]=$size
    STAGE_STACK[$stage]=${stack_hwm:-"—"}
    STAGE_HEAP[$stage]=${heap_hwm:-"—"}
    echo "  Stage $stage: $size bytes (${elapsed}s)"
    [ -n "${stack_hwm:-}" ] && echo "    stack hwm: ${stack_hwm} bytes"
    [ -n "${heap_hwm:-}" ]  && echo "    heap hwm:  ${heap_hwm} bytes"
    if [ "$size" -lt 100 ]; then
        echo "FAIL: Stage $stage output too small ($size bytes)"
        cat "$output_file" 2>/dev/null || true
        exit 1
    fi
}
echo "Phase 3: Self-build (seed → SUT)..."
build_sut "$SOURCE" "$SEED_ELF" "$SUT_ELF"
echo ""

# Canary: catch SUT bugs before Phase 4 burns 5 minutes on stage outputs.
# A SUT that crashes mid-emit on its own source can still look "built"
# (Phase 3 captured an ELF), but trying to run it on anything reveals it.
# A 25-byte hello compile-and-run takes ~10s and is a strong fast-fail.
echo "Phase 3.5: Canary (SUT compiles + runs samples/hello.codex)..."
canary_src="$REPO/samples/hello.codex"
canary_expected="$REPO/samples/hello.expected"
canary_elf="$OUTDIR/canary-hello.elf"
canary_compile_log="$OUTDIR/canary-compile.log"
canary_run_out="$OUTDIR/canary-run.out"
[ -f "$canary_src" ]      || { echo "FAIL: $canary_src missing (canary)"; exit 1; }
[ -f "$canary_expected" ] || { echo "FAIL: $canary_expected missing (canary)"; exit 1; }
canary_start=$SECONDS
canary_compile_rc=0
( cd "$REPO" && "$SCRIPT_DIR/sample-compile-selfhost.sh" "$canary_src" "$canary_elf" "$canary_compile_log" ) || canary_compile_rc=$?
if [ $canary_compile_rc -ne 0 ]; then
    echo "FAIL: canary compile (rc=$canary_compile_rc) — SUT cannot compile samples/hello.codex"
    echo "      log: $canary_compile_log"
    sed -n '1,30p' "$canary_compile_log" 2>/dev/null | sed 's/^/      /'
    exit 1
fi
"$SCRIPT_DIR/run-for-sweep.sh" "$canary_elf" "$canary_run_out" || true
if ! diff -q "$canary_run_out" "$canary_expected" > /dev/null 2>&1; then
    echo "FAIL: canary runtime output mismatch — SUT compiled hello.codex but result is wrong"
    echo "      expected:"
    sed 's/^/        /' "$canary_expected"
    echo "      got:"
    sed 's/^/        /' "$canary_run_out"
    exit 1
fi
canary_elapsed=$(( SECONDS - canary_start ))
echo "  canary: OK (${canary_elapsed}s)"
echo ""

RESULT="PASS"
echo "Phase 4: Pingpong on self-built SUT..."
echo "[1/2] Stage 1: SUT(source)..."
run_text_stage 1 "$SOURCE" "$OUTDIR/stage1.codex"
grep -v '^STACK:\|^HEAP:\|^RESULT:\|^R=\|^WD:' "$OUTDIR/stage1.codex" > "$OUTDIR/stage1.clean.codex"
echo ""
echo "[2/2] Stage 2: SUT(stage1)..."
run_text_stage 2 "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.codex"
grep -v '^STACK:\|^HEAP:\|^RESULT:\|^R=\|^WD:' "$OUTDIR/stage2.codex" > "$OUTDIR/stage2.clean.codex"
echo ""
if diff -q "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" > /dev/null 2>&1; then
    echo "PASS: stage1 === stage2 (byte-identical, $(wc -c < "$OUTDIR/stage1.clean.codex") bytes)"
    echo "      SUT(source) == SUT(stage1) — semantic equivalence under SUT."
else
    RESULT="FAIL"
    echo "FAIL: stage1 !== stage2"
    diff "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex" | head -30
fi
echo ""
echo "═══ Performance Summary ═══"
printf "%-8s  %10s  %6s  %12s  %12s\n" \
       "Stage" "Output" "Time" "Stack HWM" "Heap HWM"
printf "%-8s  %10s  %6s  %12s  %12s\n" \
       "──────" "──────────" "──────" "────────────" "────────────"
for s in 1 2; do
    sb=${STAGE_BYTES[$s]:-"—"}
    st="${STAGE_ELAPSED[$s]:-"—"}s"
    sk=${STAGE_STACK[$s]:-"—"}
    sh=${STAGE_HEAP[$s]:-"—"}
    [ "$sk" != "—" ] && sk="${sk} B"
    [ "$sh" != "—" ] && sh="${sh} B"
    printf "%-8s  %10s  %6s  %12s  %12s\n" \
           "Stage $s" "$sb" "$st" "$sk" "$sh"
done
echo ""
rm -f "$OUTDIR/stage1.clean.codex" "$OUTDIR/stage2.clean.codex"
date
[ "$RESULT" = "PASS" ] && exit 0 || exit 1
