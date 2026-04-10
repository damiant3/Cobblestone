#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
REPO=/mnt/d/Projects/NewRepository-cam

FILES=(
    Core/Name.codex
    Syntax/Token.codex
    Types/CodexType.codex
    Core/SourceText.codex
    Core/Diagnostic.codex
    Syntax/TokenKind.codex
    Ast/AstNodes.codex
    Core/Collections.codex
    IR/IRChapter.codex
    Syntax/SyntaxNodes.codex
    Types/TypeEnv.codex
    IR/LoweringTypes.codex
    Syntax/ParserCore.codex
    Ast/Desugarer.codex
    Emit/ElfWriter.codex
    Emit/X86_64Encoder.codex
    Semantics/NameResolver.codex
    Types/Unifier.codex
    Types/TypeCheckerInference.codex
    Syntax/Lexer.codex
    Types/TypeChecker.codex
    Syntax/ParserExpressions.codex
    IR/Lowering.codex
    Emit/CSharpEmitter.codex
    Emit/X86_64Boot.codex
    Emit/CodexEmitter.codex
    Semantics/ChapterScoper.codex
    Syntax/Parser.codex
    Emit/CSharpEmitterExpressions.codex
    Emit/X86_64Helpers.codex
    Emit/X86_64.codex
)

# Start from index 15 (drop 15 worked = files 15..30)
# Add back 14, 13, 12... one at a time
START=15

for ADDBACK in 14 13 12 11 10 9 8 7 6 5 4 3 2 1 0; do
    COMBINED=/tmp/ab-combined-$$
    > "$COMBINED"
    # Add the one we're testing
    cat "$REPO/Codex.Codex/${FILES[$ADDBACK]}" >> "$COMBINED"
    printf '\n' >> "$COMBINED"
    # Add all from 15..30
    for i in $(seq $START 30); do
        cat "$REPO/Codex.Codex/${FILES[$i]}" >> "$COMBINED"
        printf '\n' >> "$COMBINED"
    done
    SZ=$(wc -c < "$COMBINED")

    PIPE=/tmp/ab-pipe-$$; RAW=/tmp/ab-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & H=$!
    timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
    prev=0; stable=0
    for i in $(seq 1 60); do
        sleep 2; cur=$(wc -c < "$RAW" 2>/dev/null)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
        else stable=0; fi
        prev=$cur
        kill -0 $Q 2>/dev/null || break
    done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$COMBINED"
    if grep -qa 'SIZE:' "$RAW"; then
        echo "OK     +${FILES[$ADDBACK]}  ${SZ}b"
    else
        echo "CRASH  +${FILES[$ADDBACK]}  ${SZ}b"
    fi
    rm -f "$RAW"
done
