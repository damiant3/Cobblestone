#!/bin/bash
# sample-smoke.sh — feed every samples/*.codex to the bare-metal self-host ELF
# and report per-sample TEXT (sem-equiv) and BINARY (ELF magic+size) outcomes.
#
# Prereq: build-output/bare-metal/Codex.Codex.elf (run tools/pingpong-self.sh first).
#
# Usage:
#   tools/sample-smoke.sh                       # all samples, default 60s each
#   TIMEOUT=30 tools/sample-smoke.sh            # override per-stage timeout
#   tools/sample-smoke.sh --only 'hello*'       # glob filter (matched against basename)
#   tools/sample-smoke.sh --keep-artifacts      # retain text/elf outputs under build-output/sample-smoke/
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
WINREPO="$(wslpath -m "$REPO" 2>/dev/null || echo "$REPO")"
KERNEL="$REPO/build-output/bare-metal/Codex.Codex.elf"
SAMPLES="$REPO/samples"
OUTROOT="$REPO/build-output/sample-smoke"
QEMU="/usr/bin/qemu-system-x86_64"
DOTNET="${DOTNET:-/mnt/c/Program Files/dotnet/dotnet.exe}"
CLI_DLL_WIN="$WINREPO/tools/Codex.Cli/bin/Debug/net8.0/Codex.Cli.dll"
CLI_DLL="$REPO/tools/Codex.Cli/bin/Debug/net8.0/Codex.Cli.dll"
TIMEOUT=${TIMEOUT:-60}
FILTER='*'
KEEP=0
while [ $# -gt 0 ]; do
    case "$1" in
        --only)            FILTER="$2"; shift 2 ;;
        --keep-artifacts)  KEEP=1; shift ;;
        --help|-h)         sed -n '2,12p' "$0"; exit 0 ;;
        *)                 echo "unknown arg: $1" >&2; exit 2 ;;
    esac
done

[ -f "$KERNEL" ]   || { echo "FAIL: $KERNEL missing — run tools/pingpong-self.sh first"; exit 1; }
[ -x "$QEMU" ]     || { echo "FAIL: $QEMU missing"; exit 1; }
[ -f "$CLI_DLL" ]  || { echo "FAIL: $CLI_DLL missing — run 'dotnet build Codex.sln'"; exit 1; }
[ -d "$SAMPLES" ]  || { echo "FAIL: $SAMPLES missing"; exit 1; }

mkdir -p "$OUTROOT"
SUMMARY="$OUTROOT/summary.tsv"
: > "$SUMMARY"

# Run the kernel once: feed it the chosen command + source, capture serial output.
#
# Args: <mode=TEXT|BINARY> <input.codex> <raw-out-path>
# Prints elapsed seconds and exit reason on success.
# Returns 0 if QEMU exited cleanly with expected terminator, 1 otherwise.
run_kernel() {
    local mode="$1" input="$2" raw="$3"
    local pipe="/tmp/smoke-pipe-$$-$RANDOM"
    rm -f "$raw" "$pipe"
    mkfifo "$pipe"
    sleep 999 > "$pipe" &
    local holder=$!

    local start=$SECONDS
    timeout "$TIMEOUT" "$QEMU" \
        -enable-kvm -kernel "$KERNEL" -serial stdio \
        -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
        -display none -no-reboot -m 1024 \
        < "$pipe" > "$raw" 2>/dev/null &
    local qpid=$!

    local wait_cnt=0
    while ! grep -qa 'READY' "$raw" 2>/dev/null; do
        sleep 0.2
        wait_cnt=$((wait_cnt + 1))
        if [ "$wait_cnt" -gt 100 ]; then
            kill "$qpid" 2>/dev/null || true
            kill "$holder" 2>/dev/null || true
            wait 2>/dev/null || true
            rm -f "$pipe"
            echo "$((SECONDS - start))"
            return 1
        fi
        kill -0 "$qpid" 2>/dev/null || break
    done

    (printf '%s\n' "$mode"; cat "$input"; printf '\x04') > "$pipe" &

    # Terminator heuristic:
    #   TEXT   → STACK: line always appears after HEAP:
    #   BINARY → STACK: appears after the ELF body and HEAP:
    # If we also see a fault line (EXC=) the run has crashed.
    local prev=0 stable=0
    while true; do
        sleep 1
        if grep -qa '^STACK:' "$raw" 2>/dev/null; then break; fi
        if grep -qa '!EXC=' "$raw" 2>/dev/null; then break; fi
        local cur
        cur=$(wc -c < "$raw" 2>/dev/null || echo 0)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1))
            [ "$stable" -ge 3 ] && break
        else
            stable=0
        fi
        prev=$cur
        kill -0 "$qpid" 2>/dev/null || break
    done
    local elapsed=$((SECONDS - start))

    kill "$qpid" 2>/dev/null || true
    kill "$holder" 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"

    echo "$elapsed"
    if grep -qa '!EXC=' "$raw" 2>/dev/null; then return 2; fi
    if ! grep -qa '^STACK:' "$raw" 2>/dev/null; then return 3; fi
    return 0
}

# Strip kernel markers (STACK:/HEAP:/RESULT:/READY) from raw TEXT output.
clean_text() {
    local raw="$1" out="$2"
    grep -v '^STACK:\|^HEAP:\|^RESULT:\|^READY' "$raw" > "$out"
}

# Given a raw BINARY output, slice out the ELF body and write to $2.
# Returns 0 on success, 1 if SIZE: marker missing, 2 if size mismatch.
extract_elf() {
    local raw="$1" out="$2"
    grep -qa '^SIZE:' "$raw" || return 1
    local size_line elf_size off bstart got
    size_line=$(grep -a '^SIZE:' "$raw" | head -1)
    elf_size=${size_line#SIZE:}
    elf_size=${elf_size%%[!0-9]*}
    off=$(grep -boa '^SIZE:' "$raw" | head -1 | cut -d: -f1)
    bstart=$((off + 5 + ${#elf_size} + 1))
    dd if="$raw" bs=1 skip="$bstart" count="$elf_size" of="$out" 2>/dev/null
    got=$(wc -c < "$out")
    [ "$got" -eq "$elf_size" ] || return 2
    return 0
}

printf '%-38s  %5s  %-10s  %-10s  %s\n' \
    "SAMPLE" "BYTES" "TEXT" "BINARY" "NOTES"
printf '%-38s  %5s  %-10s  %-10s  %s\n' \
    "--------------------------------------" "-----" "----------" "----------" "-----"

TOTAL=0; TEXT_PASS=0; TEXT_FAIL=0; BIN_PASS=0; BIN_FAIL=0; BOTH_PASS=0; BOTH_FAIL=0

shopt -s nullglob
for sample in "$SAMPLES"/*.codex; do
    base=$(basename "$sample")
    # basename-level filter
    case "$base" in $FILTER) ;; *) continue ;; esac

    TOTAL=$((TOTAL + 1))
    in_bytes=$(wc -c < "$sample")
    sample_out="$OUTROOT/${base%.codex}"
    mkdir -p "$sample_out"

    # TEXT mode
    text_raw="$sample_out/text.raw"
    text_clean="$sample_out/stage1.clean.codex"
    text_verdict="FAIL"
    text_note=""
    set +e
    text_run_info=$(run_kernel TEXT "$sample" "$text_raw")
    text_rc=$?
    set -e
    text_elapsed=${text_run_info##*$'\n'}
    if [ "$text_rc" -eq 0 ]; then
        clean_text "$text_raw" "$text_clean"
        if [ -s "$text_clean" ]; then
            sample_win=$(wslpath -m "$sample")
            clean_win=$(wslpath -m "$text_clean")
            set +e
            "$DOTNET" "$CLI_DLL_WIN" sem-equiv "$sample_win" "$clean_win" > "$sample_out/sem-equiv.log" 2>&1
            sem_rc=$?
            set -e
            if [ "$sem_rc" -eq 0 ]; then
                text_verdict="PASS"
            else
                text_note="sem-equiv-diff"
            fi
        else
            text_note="empty-output"
        fi
    elif [ "$text_rc" -eq 2 ]; then
        text_note="kernel-crashed"
    elif [ "$text_rc" -eq 3 ]; then
        text_note="no-terminator"
    else
        text_note="no-ready"
    fi

    # BINARY mode
    bin_raw="$sample_out/binary.raw"
    bin_elf="$sample_out/stage1.elf"
    bin_verdict="FAIL"
    bin_note=""
    set +e
    bin_run_info=$(run_kernel BINARY "$sample" "$bin_raw")
    bin_rc=$?
    set -e
    bin_elapsed=${bin_run_info##*$'\n'}
    if [ "$bin_rc" -eq 0 ]; then
        set +e
        extract_elf "$bin_raw" "$bin_elf"
        ex_rc=$?
        set -e
        if [ "$ex_rc" -eq 0 ]; then
            magic=$(xxd -l 4 -p "$bin_elf" 2>/dev/null)
            if [ "$magic" = "7f454c46" ]; then
                bin_verdict="PASS"
            else
                bin_note="bad-magic:$magic"
            fi
        elif [ "$ex_rc" -eq 1 ]; then
            bin_note="no-SIZE"
        else
            bin_note="size-mismatch"
        fi
    elif [ "$bin_rc" -eq 2 ]; then
        bin_note="kernel-crashed"
    elif [ "$bin_rc" -eq 3 ]; then
        bin_note="no-terminator"
    else
        bin_note="no-ready"
    fi

    # Merge notes
    notes=""
    [ -n "$text_note" ] && notes="T:$text_note"
    [ -n "$bin_note" ]  && notes="${notes:+$notes }B:$bin_note"
    [ -z "$notes" ] && notes="-"

    printf '%-38s  %5d  %-10s  %-10s  %s\n' \
        "$base" "$in_bytes" "$text_verdict" "$bin_verdict" "$notes"
    printf '%s\t%d\t%s\t%s\t%s\n' \
        "$base" "$in_bytes" "$text_verdict" "$bin_verdict" "$notes" >> "$SUMMARY"

    [ "$text_verdict" = "PASS" ] && TEXT_PASS=$((TEXT_PASS + 1)) || TEXT_FAIL=$((TEXT_FAIL + 1))
    [ "$bin_verdict"  = "PASS" ] && BIN_PASS=$((BIN_PASS + 1))   || BIN_FAIL=$((BIN_FAIL + 1))
    if [ "$text_verdict" = "PASS" ] && [ "$bin_verdict" = "PASS" ]; then
        BOTH_PASS=$((BOTH_PASS + 1))
    fi
    if [ "$text_verdict" = "FAIL" ] && [ "$bin_verdict" = "FAIL" ]; then
        BOTH_FAIL=$((BOTH_FAIL + 1))
    fi

    if [ "$KEEP" -eq 0 ]; then
        rm -f "$text_raw" "$bin_raw"
    fi
done

echo ""
echo "═══ Summary ═══"
printf '  total samples:  %d\n' "$TOTAL"
printf '  TEXT   pass:    %d / %d\n' "$TEXT_PASS" "$TOTAL"
printf '  BINARY pass:    %d / %d\n' "$BIN_PASS"  "$TOTAL"
printf '  both   pass:    %d\n' "$BOTH_PASS"
printf '  both   fail:    %d\n' "$BOTH_FAIL"
echo ""
echo "  per-sample TSV: $SUMMARY"
[ "$KEEP" -eq 1 ] && echo "  artifacts:      $OUTROOT/<sample>/"
