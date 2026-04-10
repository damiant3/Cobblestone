#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam/Codex.Codex

test_pair() {
    local LABEL="$1"; shift
    local COMBINED=/tmp/np-$$
    > "$COMBINED"
    for f in "$@"; do cat "$REPO/$f" >> "$COMBINED"; printf '\n' >> "$COMBINED"; done
    local SZ=$(wc -c < "$COMBINED")
    local PIPE=/tmp/np-p-$$; local RAW=/tmp/np-r-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 60 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
    local prev=0 stable=0
    for i in $(seq 1 30); do
        sleep 2; local cur=$(wc -c < "$RAW" 2>/dev/null)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
        else stable=0; fi
        prev=$cur; kill -0 $Q 2>/dev/null || break
    done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$COMBINED"
    if grep -qa 'SIZE:' "$RAW"; then echo "OK     ${SZ}b  $LABEL"
    else echo "CRASH  ${SZ}b  $LABEL"
    fi
    rm -f "$RAW"
}

test_pair "Name+X86_64" Core/Name.codex Emit/X86_64.codex
