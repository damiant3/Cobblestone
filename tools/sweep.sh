#!/bin/bash
# Sample-sweep harness. Runs every samples/*.codex through the bare-metal
# selfhost compiler and verifies the outcome against its sidecar convention.
#
# Usage:
#   tools/sweep.sh [--jobs=N]
#
# Boots build-output/bare-metal/Codex.Codex.elf in QEMU per sample (~2-5s
# per sample). Requires stage-0 ELF present (run pingpong-self.sh first).
# --jobs=N runs up to N samples concurrently (default 1). Per-sample scripts
# already use $$-unique tmp paths, so concurrent QEMU instances don't collide.
#
# Sidecars (all optional, presence-driven — no parsing of the .codex):
#   samples/foo.failing   — compile must FAIL. File contents = expected CDX
#                           error code(s), one per line; each must appear at
#                           least once in the compile log. Empty = any fail OK.
#   samples/foo.expected  — compile must SUCCEED and the ELF's serial output
#                           between READY and HEAP must equal this file.
#   samples/foo.skip      — skipped entirely. File contents = reason (shown).
#
# No sidecar → compile-only check (must succeed; no runtime assertion).
#
# samples/MM4-Deferred/ is intentionally NOT iterated: tests for features
# explicitly scoped out of MM4 (prose type-defs in prose sections, POSIX
# fork/await semantics on a not-POSIX target). Revisit post-MM4.
#
# Exit status: 0 iff every sample ends in its expected bucket.
set -u
cd "$(dirname "$0")/.."

JOBS=1
for arg in "$@"; do
    case "$arg" in
        --jobs=*) JOBS="${arg#--jobs=}" ;;
        *) echo "Unknown arg: $arg" >&2; exit 2 ;;
    esac
done

OUTROOT=build-output/probes/samples-selfhost
RESULTS_DIR="$OUTROOT/_results"
mkdir -p "$OUTROOT"
rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

echo "sample-sweep: compiler=selfhost jobs=$JOBS outdir=$OUTROOT"

compile_sample() {
    local src=$1 outdir=$2
    local name
    name=$(basename "$src" .codex)
    local log="$outdir/build.log"
    local elf="$outdir/$name.elf"

    if wsl bash tools/sample-compile-selfhost.sh "$src" "$elf" "$log"; then
        COMPILE_OK=1
    else
        COMPILE_OK=0
    fi
}

run_sample() {
    local src=$1 outdir=$2 actual=$3
    local name
    name=$(basename "$src" .codex)
    wsl bash tools/run-for-sweep.sh "$outdir/$name.elf" "$actual" 2>/dev/null
}

process_sample() {
    local src=$1
    local name
    name=$(basename "$src" .codex)
    local dir
    dir=$(dirname "$src")
    local out="$OUTROOT/$name"
    local result_file="$RESULTS_DIR/$name"
    mkdir -p "$out"

    local skip_file="$dir/$name.skip"
    local failing_file="$dir/$name.failing"
    local expected_file="$dir/$name.expected"

    if [ -f "$skip_file" ]; then
        local reason
        reason=$(head -1 "$skip_file")
        printf 'SKIPPED\t%s\t%s\n' "$name" "$reason" > "$result_file"
        return 0
    fi

    compile_sample "$src" "$out"
    local log="$out/build.log"

    if [ -f "$failing_file" ]; then
        if [ "$COMPILE_OK" -eq 1 ]; then
            printf 'FAIL_EXPECTED_BUT_COMPILED\t%s\t\n' "$name" > "$result_file"
            return 0
        fi
        local codes_ok=1
        while IFS= read -r expected_code; do
            expected_code=$(printf '%s' "$expected_code" | tr -d '\r')
            [ -z "$expected_code" ] && continue
            if ! grep -Eq "error (CDX)?0*$expected_code\b" "$log" 2>/dev/null; then
                codes_ok=0
                break
            fi
        done < "$failing_file"
        if [ "$codes_ok" -eq 1 ]; then
            printf 'PASS_FAILING\t%s\t\n' "$name" > "$result_file"
        else
            printf 'FAIL_WRONG_DIAGNOSTIC\t%s\t\n' "$name" > "$result_file"
        fi
        return 0
    fi

    if [ "$COMPILE_OK" -eq 0 ]; then
        printf 'FAIL_COMPILE\t%s\t\n' "$name" > "$result_file"
        return 0
    fi

    if [ ! -f "$expected_file" ]; then
        printf 'PASS_UNVERIFIED\t%s\t\n' "$name" > "$result_file"
        return 0
    fi

    local actual="$out/runtime.actual"
    if ! run_sample "$src" "$out" "$actual"; then
        printf 'FAIL_RUNTIME\t%s\trun failed\n' "$name" > "$result_file"
        return 0
    fi

    if diff -q <(tr -d '\r' < "$expected_file") "$actual" > /dev/null 2>&1; then
        printf 'PASS_EXPECTED\t%s\t\n' "$name" > "$result_file"
    else
        printf 'FAIL_OUTPUT\t%s\t\n' "$name" > "$result_file"
    fi
}

export -f compile_sample run_sample process_sample
export OUTROOT RESULTS_DIR

chmod +x tools/run-for-sweep.sh 2>/dev/null || true

samples=()
for src in samples/*.codex samples/errors/*.codex; do
    [ -f "$src" ] || continue
    samples+=("$src")
done

echo "dispatching ${#samples[@]} samples..."
printf '%s\n' "${samples[@]}" | xargs -P "$JOBS" -I {} bash -c 'process_sample "$@"' _ {}

PASS_EXPECTED=()
PASS_UNVERIFIED=()
PASS_FAILING=()
FAIL_COMPILE=()
FAIL_RUNTIME=()
FAIL_OUTPUT=()
SKIPPED=()
FAIL_EXPECTED_BUT_COMPILED=()
FAIL_WRONG_DIAGNOSTIC=()

for f in "$RESULTS_DIR"/*; do
    [ -f "$f" ] || continue
    IFS=$'\t' read -r status name detail < "$f"
    case "$status" in
        PASS_EXPECTED)              PASS_EXPECTED+=("$name") ;;
        PASS_UNVERIFIED)            PASS_UNVERIFIED+=("$name") ;;
        PASS_FAILING)               PASS_FAILING+=("$name") ;;
        SKIPPED)                    SKIPPED+=("$name: $detail") ;;
        FAIL_COMPILE)               FAIL_COMPILE+=("$name") ;;
        FAIL_RUNTIME)               FAIL_RUNTIME+=("$name: $detail") ;;
        FAIL_OUTPUT)                FAIL_OUTPUT+=("$name") ;;
        FAIL_EXPECTED_BUT_COMPILED) FAIL_EXPECTED_BUT_COMPILED+=("$name") ;;
        FAIL_WRONG_DIAGNOSTIC)      FAIL_WRONG_DIAGNOSTIC+=("$name") ;;
        *) echo "unknown result status '$status' for $name" >&2 ;;
    esac
done

section() {
    local label=$1
    shift
    local count=$#
    echo
    echo "=== $label ($count) ==="
    if [ "$count" -gt 0 ]; then
        for i in "$@"; do
            echo "  $i"
        done
    fi
}

section "PASS: verified runtime output"                ${PASS_EXPECTED[@]+"${PASS_EXPECTED[@]}"}
section "PASS: expected-to-fail (diagnostic matches)"  ${PASS_FAILING[@]+"${PASS_FAILING[@]}"}
section "PASS: compiles (unverified runtime)"          ${PASS_UNVERIFIED[@]+"${PASS_UNVERIFIED[@]}"}
section "SKIPPED"                                      ${SKIPPED[@]+"${SKIPPED[@]}"}
section "FAIL: compile failed"                         ${FAIL_COMPILE[@]+"${FAIL_COMPILE[@]}"}
section "FAIL: expected compile error but compiled"    ${FAIL_EXPECTED_BUT_COMPILED[@]+"${FAIL_EXPECTED_BUT_COMPILED[@]}"}
section "FAIL: wrong diagnostic code"                  ${FAIL_WRONG_DIAGNOSTIC[@]+"${FAIL_WRONG_DIAGNOSTIC[@]}"}
section "FAIL: runtime output mismatch"                ${FAIL_OUTPUT[@]+"${FAIL_OUTPUT[@]}"}
section "FAIL: runtime error"                          ${FAIL_RUNTIME[@]+"${FAIL_RUNTIME[@]}"}

echo
total=$(( ${#PASS_EXPECTED[@]} + ${#PASS_FAILING[@]} + ${#PASS_UNVERIFIED[@]} \
         + ${#SKIPPED[@]} + ${#FAIL_COMPILE[@]} + ${#FAIL_EXPECTED_BUT_COMPILED[@]} \
         + ${#FAIL_WRONG_DIAGNOSTIC[@]} + ${#FAIL_OUTPUT[@]} + ${#FAIL_RUNTIME[@]} ))
passed=$(( ${#PASS_EXPECTED[@]} + ${#PASS_FAILING[@]} + ${#PASS_UNVERIFIED[@]} ))
failed=$(( ${#FAIL_COMPILE[@]} + ${#FAIL_EXPECTED_BUT_COMPILED[@]} \
           + ${#FAIL_WRONG_DIAGNOSTIC[@]} + ${#FAIL_OUTPUT[@]} + ${#FAIL_RUNTIME[@]} ))
echo "total=$total  pass=$passed  fail=$failed  skip=${#SKIPPED[@]}"
echo "  verified=${#PASS_EXPECTED[@]}  unverified=${#PASS_UNVERIFIED[@]}"

[ "$failed" -eq 0 ]
