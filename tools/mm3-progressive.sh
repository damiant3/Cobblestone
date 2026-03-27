#!/bin/bash
# MM3 Progressive Test: feed the compiler its own source, ring by ring
KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
CODEX="/mnt/d/Projects/NewRepository-cam/Codex.Codex"
PIPE="/tmp/qemu-serial-pipe"

# Extract code blocks from a prose (.codex) file
extract() {
    local file="$1"
    # Check if prose document
    if head -1 "$file" | grep -q "^Chapter:"; then
        # Keep indented lines (code) and blank lines
        awk '/^  / || /^$/' "$file"
    else
        cat "$file"
    fi
}

# Build cumulative source for each ring
build_ring() {
    local ring="$1"
    local output="/tmp/mm3-ring${ring}.codex"
    local dirs=""

    case $ring in
        0) dirs="Core" ;;
        1) dirs="Core Syntax" ;;
        2) dirs="Core Syntax Ast" ;;
        3) dirs="Core Syntax Ast Semantics" ;;
        4) dirs="Core Syntax Ast Semantics Types" ;;
        5) dirs="Core Syntax Ast Semantics Types IR" ;;
        6) dirs="Core Syntax Ast Semantics Types IR Emit" ;;
        7) dirs="Core Syntax Ast Semantics Types IR Emit" ;;  # + main
    esac

    > "$output"
    for dir in $dirs; do
        for f in $(find "$CODEX/$dir" -name '*.codex' | sort); do
            extract "$f" >> "$output"
            echo "" >> "$output"
            echo "" >> "$output"
        done
    done

    if [ "$ring" = "7" ]; then
        extract "$CODEX/main.codex" >> "$output"
    fi

    echo "$output"
}

# Test a ring
test_ring() {
    local ring="$1"
    local timeout_sec="$2"
    local source=$(build_ring "$ring")
    local size=$(wc -c < "$source")

    echo -n "Ring $ring ($size bytes, ${timeout_sec}s timeout): "

    rm -f "$PIPE"
    mkfifo "$PIPE"

    (sleep 2; cat "$source"; printf '\x04'; sleep "$timeout_sec") > "$PIPE" &
    local sender=$!

    local output
    output=$(timeout $((timeout_sec + 5)) qemu-system-x86_64 \
        -kernel "$KERNEL" \
        -serial stdio \
        -display none \
        -no-reboot \
        -m 512 \
        < "$PIPE" 2>/dev/null)

    kill $sender 2>/dev/null
    rm -f "$PIPE"

    local outsize=${#output}
    if [ $outsize -gt 100 ]; then
        echo "PASS ($outsize bytes output)"
    elif [ $outsize -gt 0 ]; then
        echo "PARTIAL ($outsize bytes: $(echo "$output" | head -1))"
    else
        echo "FAIL (no output)"
    fi
}

echo "=== MM3 Progressive Ring Test ==="
echo ""

test_ring 0 30
test_ring 1 120
test_ring 2 180
test_ring 3 240
test_ring 4 360
test_ring 5 420
test_ring 6 480
test_ring 7 600

echo ""
echo "=== Done ==="
