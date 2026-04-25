#!/bin/bash
# End-to-end verify the tier-3 watchdog panic dump fires with the expected
# richer-dump strings. Compiles samples/watchdog-panic-probe.codex (TCO'd
# infinite spin) on both REF and self-host (via Bootstrap --binary-sample)
# with --watchdog=pet, runs each under QEMU with a ~5s timeout, asserts
# the serial output contains the expected markers.
#
# Markers expected:
#   both:      WD!  HWM=
#   self-host: STK=   R=   (R= = ring-slot RIP label from dump-slots)
#
# Run from Git Bash (invokes WSL QEMU) or WSL directly.
# Exit 0 iff all assertions pass.
set -u
cd "$(dirname "$0")/.."

SAMPLE=samples/watchdog-panic-probe.codex
if [ -d /mnt/c ]; then
    DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
else
    DOTNET="/c/Program Files/dotnet/dotnet.exe"
fi
CLI="tools/Codex.Cli/bin/Debug/net8.0/Codex.Cli.dll"
BOOTSTRAP="tools/Codex.Bootstrap/bin/Debug/net8.0/Codex.Bootstrap.dll"
QEMU="/usr/bin/qemu-system-x86_64"
OUT=build-output/probes/watchdog-panic
mkdir -p "$OUT"

[ -f "$SAMPLE" ] || { echo "FAIL: $SAMPLE missing"; exit 1; }

PASS=0
FAIL=0

run_qemu() {
    local elf=$1 raw=$2
    local pipe=/tmp/wdpanic-pipe-$$
    rm -f "$pipe"
    mkfifo "$pipe"
    sleep 30 > "$pipe" &
    local holder=$!
    timeout 6 "$QEMU" -enable-kvm -kernel "$elf" -serial stdio \
        -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
        -display none -no-reboot -m 1024 < "$pipe" > "$raw" 2>/dev/null &
    local qpid=$!
    for _ in $(seq 1 30); do
        grep -qa 'WD!' "$raw" 2>/dev/null && break
        sleep 0.2
        kill -0 $qpid 2>/dev/null || break
    done
    sleep 0.5
    kill $qpid $holder 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$pipe"
}

assert_contains() {
    local raw=$1 needle=$2 label=$3
    if grep -qa "$needle" "$raw"; then
        echo "  OK    $label  '$needle'"
        PASS=$((PASS + 1))
    else
        echo "  FAIL  $label  '$needle' not found"
        FAIL=$((FAIL + 1))
    fi
}

assert_absent() {
    local raw=$1 needle=$2 label=$3
    if grep -qa "$needle" "$raw"; then
        echo "  FAIL  $label  '$needle' unexpectedly present"
        FAIL=$((FAIL + 1))
    else
        echo "  OK    $label  '$needle' absent"
        PASS=$((PASS + 1))
    fi
}

echo "=== REF ==="
ELF_REF=$OUT/ref/watchdog-panic-probe.elf
RAW_REF=$OUT/ref.raw
mkdir -p "$OUT/ref"
if "$DOTNET" "$CLI" build "$SAMPLE" --target x86-64-bare \
        --watchdog=pet --exit-mode=qemu-exit \
        --output-dir "$OUT/ref" \
        > "$OUT/ref-build.log" 2>&1; then
    echo "  build OK ($(wc -c < "$ELF_REF") bytes)"
else
    echo "  FAIL: REF build did not complete"
    tail -20 "$OUT/ref-build.log"
    exit 1
fi
run_qemu "$ELF_REF" "$RAW_REF"
assert_contains "$RAW_REF" 'WD!' 'REF panic marker'
assert_contains "$RAW_REF" 'HWM=' 'REF heap-hwm label'
assert_absent   "$RAW_REF" 'STK=' 'REF has no stack walk'

echo
echo "=== self-host (BS1-hosted) ==="
ELF_SH=$OUT/selfhost.elf
RAW_SH=$OUT/selfhost.raw
if "$DOTNET" "$BOOTSTRAP" --binary-sample "$SAMPLE" "$ELF_SH" \
        --watchdog=pet --exit-mode=qemu-exit \
        > "$OUT/selfhost-build.log" 2>&1; then
    echo "  build OK ($(wc -c < "$ELF_SH") bytes)"
else
    echo "  FAIL: self-host build did not complete"
    tail -20 "$OUT/selfhost-build.log"
    exit 1
fi
run_qemu "$ELF_SH" "$RAW_SH"
assert_contains "$RAW_SH" 'WD!' 'self-host panic marker'
assert_contains "$RAW_SH" 'HWM=' 'self-host heap-hwm label'
assert_contains "$RAW_SH" 'STK=' 'self-host stack-walk label'
assert_contains "$RAW_SH" 'R='  'self-host ring-slot RIP label'

echo
echo "=== $PASS passed / $FAIL failed ==="
[ "$FAIL" -eq 0 ]
