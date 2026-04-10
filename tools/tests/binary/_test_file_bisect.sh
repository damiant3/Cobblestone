#!/bin/bash
# Feed individual .codex files to Stage 0 to find which one crashes
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam

test_file() {
    local FILE="$1"
    local PIPE=/tmp/fb-pipe-$$; local RAW=/tmp/fb-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$REPO/$FILE"; printf '\x04') > "$PIPE" &
    sleep 20; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    local SZ=$(wc -c < "$RAW")
    if grep -qa 'SIZE:' "$RAW"; then
        local ELF_SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
        echo "  OK  $(wc -c < "$REPO/$FILE" | tr -d ' ')b → ${ELF_SZ}b  $FILE"
    else
        echo "  CRASH  $(wc -c < "$REPO/$FILE" | tr -d ' ')b  $FILE  (output: ${SZ}b)"
    fi
    rm -f "$RAW"
}

echo "=== File bisect ==="
test_file "Codex.Codex/Core/Collections.codex"
test_file "Codex.Codex/Syntax/Lexer.codex"
test_file "Codex.Codex/Syntax/Parser.codex"
test_file "Codex.Codex/Ast/AstNodes.codex"
test_file "Codex.Codex/Types/TypeChecker.codex"
test_file "Codex.Codex/Emit/CSharpEmitter.codex"
test_file "Codex.Codex/Emit/X86_64.codex"
test_file "Codex.Codex/Emit/X86_64Helpers.codex"
test_file "Codex.Codex/Emit/X86_64Boot.codex"
test_file "Codex.Codex/main.codex"
