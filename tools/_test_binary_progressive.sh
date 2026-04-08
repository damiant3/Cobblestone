#!/bin/bash
set -u
ELF=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
SOURCE=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/source.codex
QEMU=/usr/bin/qemu-system-x86_64
CUTPOINTS=(153 373 451 503 527 571 946 1594 1699 2301 2609 4785 5225 5653 7389 7692 7810 8252 8457 8974 9266 9747 10382 10727 11144 11301 11324 11407 11445 11864 12313 12417 12835 13113)
NAMES=("AST" "Desugar" "Collections" "Diagnostics" "Name" "SourceText" "CSharpEmit1" "CSharpEmit2" "CDXBinary" "CodexEmit" "ELFWriter" "X86Gen1" "X86Boot" "X86Gen2" "X86Gen3" "StringEsc" "IR" "Lower1" "Lower2" "Scoper" "NameRes" "Lexer" "Parser1" "Parser2" "Parser3" "SyntaxNodes" "Tokens" "TokenKinds" "Types" "TypeCheck1" "TypeCheck2" "TypeEnv" "Unify" "Full")

for i in "${!CUTPOINTS[@]}"; do
    CUT=${CUTPOINTS[$i]}
    NAME=${NAMES[$i]}
    TMP=/tmp/progressive-test.codex
    if [ "$CUT" -eq 13113 ]; then
        cp "$SOURCE" "$TMP"
    else
        head -n "$CUT" "$SOURCE" > "$TMP"
        printf '\nSection: Entry Point\n\n  main = 42\n\nPage 1\n' >> "$TMP"
    fi
    SIZE=$(wc -c < "$TMP")
    PIPE=/tmp/prog-pipe
    RAW=/tmp/prog-raw
    rm -f "$PIPE" "$RAW"
    mkfifo "$PIPE"
    sleep 999 > "$PIPE" &
    HOLDER=$!
    timeout 600 "$QEMU" -enable-kvm -kernel "$ELF" -serial stdio -display none -no-reboot -m 512 < "$PIPE" > "$RAW" 2>/dev/null &
    PID=$!
    for w in $(seq 1 50); do
        grep -qa READY "$RAW" 2>/dev/null && break
        sleep 0.2
    done
    (printf 'BINARY\n'; cat "$TMP"; printf '\x04') > "$PIPE" &
    START=$SECONDS
    PREV=0
    STABLE=0
    while true; do
        sleep 3
        CUR=$(wc -c < "$RAW" 2>/dev/null || echo 0)
        if [ "$CUR" -gt 100 ] && [ "$CUR" -eq "$PREV" ]; then
            STABLE=$((STABLE + 1))
            [ "$STABLE" -ge 2 ] && break
        else
            STABLE=0
        fi
        PREV=$CUR
        kill -0 $PID 2>/dev/null || break
    done
    ELAPSED=$(( SECONDS - START ))
    kill $PID 2>/dev/null || true
    kill $HOLDER 2>/dev/null || true
    wait 2>/dev/null || true
    rm -f "$PIPE"
    RAWSIZE=$(wc -c < "$RAW" 2>/dev/null || echo 0)
    if grep -qa 'SIZE:' "$RAW" 2>/dev/null; then
        ELFSIZE=$(grep -aoP 'SIZE:\K[0-9]+' "$RAW" | head -1)
        echo "PASS  $NAME  src=${SIZE}B  elf=${ELFSIZE}B  ${ELAPSED}s"
    else
        echo "FAIL  $NAME  src=${SIZE}B  raw=${RAWSIZE}B  ${ELAPSED}s"
        rm -f "$RAW" "$TMP"
        exit 1
    fi
    rm -f "$RAW" "$TMP"
done
echo ""
echo "All stages passed."
