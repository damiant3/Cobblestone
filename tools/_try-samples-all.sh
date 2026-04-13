#!/bin/bash
# Loop _try-sample-bin.sh over every samples/*.codex and summarize.
set -uo pipefail
REPO="/mnt/d/Projects/NewRepository-cam"
SCRIPT="$REPO/tools/_try-sample-bin.sh"
RESULTS="/tmp/try-samples-$$.log"
SUMMARY="/tmp/try-samples-$$.summary"

: > "$RESULTS"
: > "$SUMMARY"

for src in "$REPO"/samples/*.codex; do
    name=$(basename "$src" .codex)
    echo "=== $name ===" >> "$RESULTS"
    out=$(bash "$SCRIPT" "$src" 2>&1)
    echo "$out" >> "$RESULTS"
    if echo "$out" | grep -q 'SIZE:[0-9]'; then
        size=$(echo "$out" | grep -oa 'SIZE:[0-9]*' | head -1 | sed 's/SIZE://')
        printf 'PASS  %-32s SIZE=%s\n' "$name" "$size" | tee -a "$SUMMARY"
    elif echo "$out" | grep -q 'READY not received'; then
        printf 'BOOT  %-32s (READY failed)\n' "$name" | tee -a "$SUMMARY"
    else
        printf 'FAIL  %-32s (no SIZE marker)\n' "$name" | tee -a "$SUMMARY"
    fi
done

echo ""
echo "=== Summary ==="
echo "PASS: $(grep -c '^PASS' "$SUMMARY")"
echo "FAIL: $(grep -c '^FAIL' "$SUMMARY")"
echo "BOOT: $(grep -c '^BOOT' "$SUMMARY")"
echo "Log:  $RESULTS"
