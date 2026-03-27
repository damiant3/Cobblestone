#!/bin/bash
# MM3 Builtin Coverage Test Suite
# Sends small programs to the bare metal kernel and verifies output.
# Usage: wsl -e bash /mnt/d/Projects/NewRepository-cam/tools/mm3-test-suite.sh

KERNEL="/mnt/c/Users/Damian/AppData/Local/Temp/codex-all-source.elf"
PIPE="/tmp/qemu-serial-pipe"
PASS=0
FAIL=0
SKIP=0

run_test() {
    local name="$1"
    local source="$2"
    local expect="$3"
    local timeout_sec="${4:-30}"

    rm -f "$PIPE"
    mkfifo "$PIPE"

    # Background: boot delay, send source + EOT, wait
    (sleep 2; printf '%s' "$source"; printf '\x04'; sleep "$timeout_sec") > "$PIPE" &
    local sender=$!

    # Run QEMU, capture output
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

    # Check if expected string is in output
    if echo "$output" | grep -qF "$expect"; then
        echo "PASS: $name"
        PASS=$((PASS + 1))
    else
        echo "FAIL: $name"
        echo "  expected: $expect"
        echo "  got: $(echo "$output" | tail -5)"
        FAIL=$((FAIL + 1))
    fi
}

echo "=== MM3 Builtin Coverage Test Suite ==="
echo "Kernel: $KERNEL"
echo ""

# --- Basic ---
run_test "integer literal" \
    "main : Integer
main = 42" \
    "main() => 42"

run_test "arithmetic" \
    "main : Integer
main = 3 + 4 * 5" \
    "main() => 3 + 4 * 5"

run_test "let binding" \
    "main : Integer
main = let x = 10 in let y = 20 in x + y" \
    "x + y"

run_test "if-else" \
    "main : Integer
main = if 1 == 1 then 42 else 0" \
    "42"

# --- Functions + Recursion ---
run_test "function call" \
    "double : Integer -> Integer
double (x) = x * 2

main : Integer
main = double 21" \
    "double"

run_test "recursion" \
    "factorial : Integer -> Integer
factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

main : Integer
main = factorial 5" \
    "factorial"

# --- Text operations ---
run_test "text-length" \
    "main : Integer
main = text-length \"hello\"" \
    "text-length"

run_test "substring" \
    "main : Text
main = substring \"hello world\" 0 5" \
    "substring"

run_test "integer-to-text" \
    "main : Text
main = integer-to-text 42" \
    "integer-to-text"

run_test "text-to-integer" \
    "main : Integer
main = text-to-integer \"123\"" \
    "text-to-integer"

run_test "text-compare" \
    "main : Integer
main = text-compare \"abc\" \"def\"" \
    "text-compare"

run_test "text-contains" \
    "main : Boolean
main = text-contains \"hello world\" \"world\"" \
    "text-contains"

run_test "text-replace" \
    "main : Text
main = text-replace \"hello world\" \"world\" \"codex\"" \
    "text-replace"

run_test "text-concat-list" \
    "main : Text
main = text-concat-list [\"hello\", \" \", \"world\"]" \
    "text-concat-list"

run_test "text-split" \
    "main : List Text
main = text-split \"a,b,c\" \",\"" \
    "text-split"

run_test "text append" \
    "main : Text
main = \"hello\" ++ \" world\"" \
    "string.Concat"

# --- Character operations ---
run_test "char-at" \
    "main : Char
main = char-at \"hello\" 0" \
    "char-at"

run_test "char-code-at" \
    "main : Integer
main = char-code-at \"hello\" 0" \
    "char-code-at"

run_test "char-to-text" \
    "main : Text
main = char-to-text 'a'" \
    "char-to-text"

run_test "is-letter" \
    "main : Boolean
main = is-letter 'a'" \
    "is-letter"

run_test "is-digit" \
    "main : Boolean
main = is-digit '5'" \
    "is-digit"

run_test "is-whitespace" \
    "main : Boolean
main = is-whitespace ' '" \
    "is-whitespace"

# --- List operations ---
run_test "list-length" \
    "main : Integer
main = list-length [1, 2, 3]" \
    "list-length"

run_test "list-at" \
    "main : Integer
main = list-at [10, 20, 30] 1" \
    "list-at"

run_test "list-snoc" \
    "main : List Integer
main = list-snoc [1, 2] 3" \
    "list-snoc"

run_test "list-insert-at" \
    "main : List Integer
main = list-insert-at [1, 3] 1 2" \
    "list-insert-at"

run_test "list-contains" \
    "main : Boolean
main = list-contains [1, 2, 3] 2" \
    "list-contains"

run_test "list append" \
    "main : List Integer
main = [1, 2] ++ [3, 4]" \
    "AddRange"

# --- Higher-order functions (closures) ---
run_test "higher-order function" \
    "apply : (Integer -> Integer) -> Integer -> Integer
apply (f) (x) = f x

double : Integer -> Integer
double (x) = x * 2

main : Integer
main = apply double 21" \
    "apply"

run_test "map-style loop" \
    "map-it : (Integer -> Integer) -> List Integer -> Integer -> Integer -> List Integer -> List Integer
map-it (f) (xs) (i) (len) (acc) =
  if i == len then acc
  else map-it f xs (i + 1) len (list-snoc acc (f (list-at xs i)))

double : Integer -> Integer
double (x) = x * 2

main : Integer
main = list-at (map-it double [1, 2, 3] 0 3 []) 0" \
    "map-it"

# --- Pattern matching ---
run_test "pattern match" \
    "Color = Red | Green | Blue

to-int : Color -> Integer
to-int (c) =
  when c
    if Red -> 1
    if Green -> 2
    if Blue -> 3

main : Integer
main = to-int Green" \
    "to-int"

# --- Records ---
run_test "record access" \
    "Point = Point { x : Integer, y : Integer }

main : Integer
main = let p = Point { x = 3, y = 4 } in p.x + p.y" \
    "Point"

# --- Power (new __ipow) ---
run_test "power operator" \
    "main : Integer
main = 2 ^ 10" \
    "1024"

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
