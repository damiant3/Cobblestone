#!/bin/bash
# Combine files incrementally to find the tipping point
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam

test_combined() {
    local LABEL="$1"; shift
    local COMBINED=/tmp/cb-combined-$$
    > "$COMBINED"
    local TOTAL=0
    for f in "$@"; do
        cat "$REPO/$f" >> "$COMBINED"
        printf '\n' >> "$COMBINED"
    done
    TOTAL=$(wc -c < "$COMBINED")

    local PIPE=/tmp/cb-pipe-$$; local RAW=/tmp/cb-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
    local prev=0 stable=0
    for i in $(seq 1 60); do
        sleep 2
        local cur=$(wc -c < "$RAW" 2>/dev/null)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
        else stable=0; fi
        prev=$cur
        kill -0 $Q 2>/dev/null || break
    done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$COMBINED"
    if grep -qa 'SIZE:' "$RAW"; then
        echo "  OK  ${TOTAL}b  $LABEL"
    else
        echo "  CRASH  ${TOTAL}b  $LABEL  (output: $(wc -c < "$RAW")b)"
    fi
    rm -f "$RAW"
}

echo "=== Combined file bisect ==="

test_combined "2-big" \
    Codex.Codex/Emit/X86_64.codex \
    Codex.Codex/Emit/X86_64Helpers.codex

test_combined "3-big" \
    Codex.Codex/Emit/X86_64.codex \
    Codex.Codex/Emit/X86_64Helpers.codex \
    Codex.Codex/Emit/CSharpEmitter.codex

test_combined "4-big" \
    Codex.Codex/Emit/X86_64.codex \
    Codex.Codex/Emit/X86_64Helpers.codex \
    Codex.Codex/Emit/CSharpEmitter.codex \
    Codex.Codex/Syntax/Parser.codex

test_combined "5-big" \
    Codex.Codex/Emit/X86_64.codex \
    Codex.Codex/Emit/X86_64Helpers.codex \
    Codex.Codex/Emit/CSharpEmitter.codex \
    Codex.Codex/Syntax/Parser.codex \
    Codex.Codex/Emit/X86_64Boot.codex
