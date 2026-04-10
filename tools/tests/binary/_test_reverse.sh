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
TOTAL=${#FILES[@]}

test_without() {
    local SKIP=$1
    local COMBINED=/tmp/rv-combined-$$
    > "$COMBINED"
    local INCLUDED=0
    for i in $(seq 0 $((TOTAL - 1))); do
        [ "$i" -lt "$SKIP" ] && continue
        cat "$REPO/Codex.Codex/${FILES[$i]}" >> "$COMBINED"
        printf '\n' >> "$COMBINED"
        INCLUDED=$((INCLUDED + 1))
    done
    local SZ=$(wc -c < "$COMBINED")
    local SKIPPED_NAME="${FILES[$((SKIP - 1))]}"

    local PIPE=/tmp/rv-pipe-$$; local RAW=/tmp/rv-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 120 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    (printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
    local prev=0 stable=0
    for i in $(seq 1 60); do
        sleep 2; local cur=$(wc -c < "$RAW" 2>/dev/null)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
        else stable=0; fi
        prev=$cur
        kill -0 $Q 2>/dev/null || break
    done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$COMBINED"
    if grep -qa 'SIZE:' "$RAW"; then
        echo "OK     ${INCLUDED} files  ${SZ}b  (skipped first $SKIP: ...$SKIPPED_NAME)"
    else
        echo "CRASH  ${INCLUDED} files  ${SZ}b  (skipped first $SKIP: ...$SKIPPED_NAME)"
    fi
    rm -f "$RAW"
}

echo "=== Reverse: all 31 minus smallest files from front ==="
echo "All 31 first:"
test_without 0

echo "Drop 1 smallest:"
test_without 1

echo "Drop 2 smallest:"
test_without 2

echo "Drop 5 smallest:"
test_without 5

echo "Drop 10 smallest:"
test_without 10

echo "Drop 15 smallest:"
test_without 15

echo "Drop 20 smallest:"
test_without 20

echo "Drop 25 smallest:"
test_without 25

echo "Drop 28 (just 3 biggest):"
test_without 28
