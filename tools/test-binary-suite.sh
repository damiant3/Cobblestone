#!/bin/bash
# Binary backend test suite — compiles programs through Stage 0,
# extracts ELFs, boots them, checks output.
# Usage: bash tools/test-binary-suite.sh [test-name]
set -o pipefail
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
PASS=0; FAIL=0; SKIP=0; ERRORS=""

compile_and_boot() {
    local NAME="$1"; local SRC="$2"; local INPUT="$3"; local EXPECT="$4"
    local PIPE=/tmp/tbs-p-$$; local RAW=/tmp/tbs-r-$$; local ELF=/tmp/tbs-$$.elf
    rm -f "$PIPE" "$RAW" "$ELF"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if grep -qa 'SIZE:' "$RAW"; then
        local SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
        local SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
        local BSTART=$((SOFF + 5 + ${#SZ} + 1))
        local NEEDED=$((BSTART + SZ))
        while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
        kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
        dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
        rm -f "$RAW"
    else
        kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"
        echo "FAIL  $NAME  (compile failed — no SIZE:)"
        FAIL=$((FAIL + 1)); ERRORS="$ERRORS  $NAME: compile failed\n"; return
    fi

    # Boot
    local PIPE2=/tmp/tbs2-p-$$; local RAW2=/tmp/tbs2-r-$$
    rm -f "$PIPE2" "$RAW2"; mkfifo "$PIPE2"
    sleep 999 > "$PIPE2" & local H2=$!
    timeout 10 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE2" > "$RAW2" 2>/dev/null & local Q2=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW2" 2>/dev/null && break; sleep 0.5; done
    if [ -n "$INPUT" ]; then
        printf '%s' "$INPUT" > "$PIPE2" &
    fi
    sleep 3
    kill $Q2 $H2 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE2" "$ELF"

    if ! grep -qa 'READY' "$RAW2" 2>/dev/null; then
        echo "FAIL  $NAME  (no boot — no READY)"
        FAIL=$((FAIL + 1)); ERRORS="$ERRORS  $NAME: no boot\n"; rm -f "$RAW2"; return
    fi

    # Check output — extract first result line after READY
    local GOT=$(sed -n '2p' "$RAW2")
    rm -f "$RAW2"
    if [ "$GOT" = "$EXPECT" ]; then
        echo "PASS  $NAME"
        PASS=$((PASS + 1))
    else
        echo "FAIL  $NAME  (expected '$EXPECT', got '$GOT')"
        FAIL=$((FAIL + 1)); ERRORS="$ERRORS  $NAME: expected '$EXPECT', got '$GOT'\n"
    fi
}

if [ ! -f "$STAGE0" ]; then
    echo "Stage 0 not found at $STAGE0 — run: dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare --output-dir build-output/bare-metal"
    exit 1
fi

echo "=== Binary Backend Test Suite ==="
echo ""

# --- Literals ---
compile_and_boot "int-lit" \
    'Chapter: T
Section: Main
  main : Integer
  main = 42' "" "42"

compile_and_boot "bool-true" \
    'Chapter: T
Section: Main
  main : Integer
  main = if True then 1 else 0' "" "1"

compile_and_boot "text-lit" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length "hello"' "" "5"

compile_and_boot "text-lit-long" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length "the quick brown fox jumps over the lazy dog"' "" "43"

compile_and_boot "char-lit" \
    "Chapter: T
Section: Main
  main : Integer
  main = char-code 'a'" "" "15"

compile_and_boot "negate" \
    'Chapter: T
Section: Main
  main : Integer
  main = 0 - 42' "" "-42"

# --- Arithmetic ---
compile_and_boot "arith" \
    'Chapter: T
Section: Main
  main : Integer
  main = (10 + 20) * 3 - 5' "" "85"

# --- Functions ---
compile_and_boot "func-call" \
    'Chapter: T
Section: Funcs
  double : Integer -> Integer
  double (x) = x * 2
Section: Main
  main : Integer
  main = double 21' "" "42"

compile_and_boot "recursion" \
    'Chapter: T
Section: Funcs
  fib : Integer -> Integer
  fib (n) = if n <= 1 then n else fib (n - 1) + fib (n - 2)
Section: Main
  main : Integer
  main = fib 10' "" "55"

# --- Pattern matching ---
compile_and_boot "sum-type" \
    'Chapter: T
Section: Types
  Shape = | Circle (Integer) | Rect (Integer) (Integer)
Section: Funcs
  area : Shape -> Integer
  area (s) = when s
    if Circle (r) -> r * r
    if Rect (w) (h) -> w * h
Section: Main
  main : Integer
  main = area (Rect 6 7)' "" "42"

# --- Records ---
compile_and_boot "record" \
    'Chapter: T
Section: Types
  Point = record { x : Integer, y : Integer }
Section: Main
  main : Integer
  main = let p = Point { x = 40, y = 2 } in p.x + p.y' "" "42"

# --- Lists ---
compile_and_boot "list-ops" \
    'Chapter: T
Section: Main
  main : Integer
  main = list-length [10, 20, 30] + list-at [10, 20, 30] 1' "" "23"

# --- String ops ---
compile_and_boot "string-concat" \
    'Chapter: T
Section: Main
  main : Integer
  main = text-length ("hello" ++ " " ++ "world")' "" "11"

compile_and_boot "string-eq" \
    'Chapter: T
Section: Main
  main : Integer
  main = if "abc" == "abc" then 1 else 0' "" "1"

# --- Effectful (do blocks) ---
compile_and_boot "do-print" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   print-line "hello"' "" "hello"

compile_and_boot "do-two-prints" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   print-line "a"
   print-line "b"' "" "a"

compile_and_boot "do-bind-let" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   let x = 42
   in print-line (integer-to-text x)' "" "42"

compile_and_boot "do-call-effectful" \
    'Chapter: T
Section: Funcs
  greet : [Console] Nothing
  greet = do
   print-line "hi"
Section: Main
  main : [Console] Nothing
  main = do
   greet' "" "hi"

# --- I/O ---
compile_and_boot "readline-readfile" \
    'Chapter: T
Section: Main
  main : [Console, FileSystem] Nothing
  main = do
   mode <- read-line
   source <- read-file mode
   print-line source' \
    "$(printf 'go\nhello world\x04')" "hello world"

# --- Higher-order ---
compile_and_boot "higher-order" \
    'Chapter: T
Section: Funcs
  apply : (Integer -> Integer) -> Integer -> Integer
  apply (f) (x) = f x
  inc : Integer -> Integer
  inc (n) = n + 1
Section: Main
  main : Integer
  main = apply inc 41' "" "42"

# --- LinkedList ---
compile_and_boot "linked-list" \
    'Chapter: T
Section: Main
  main : Integer
  main = let ll = linked-list-push (linked-list-push (linked-list-empty 0) 10) 20
   in list-length (linked-list-to-list ll)' "" "2"

# --- TCO ---
compile_and_boot "tco" \
    'Chapter: T
Section: Funcs
  sum-to : Integer -> Integer -> Integer
  sum-to (n) (acc) = if n == 0 then acc else sum-to (n - 1) (acc + n)
Section: Main
  main : Integer
  main = sum-to 10000 0' "" "50005000"

# --- Many string literals ---
compile_and_boot "many-strings" \
    'Chapter: T
Section: Data
  s1 : Text
  s1 = "alpha bravo charlie delta echo foxtrot golf"
  s2 : Text
  s2 = "hotel india juliet kilo lima mike november"
  s3 : Text
  s3 = "oscar papa quebec romeo sierra tango uniform"
  s4 : Text
  s4 = "victor whiskey xray yankee zulu"
Section: Main
  main : Integer
  main = text-length s1 + text-length s2 + text-length s3 + text-length s4' "" "165"

# --- Nested do ---
compile_and_boot "nested-do" \
    'Chapter: T
Section: Main
  main : [Console] Nothing
  main = do
   let x = 10
   in do
    let y = 20
    in do
     print-line (integer-to-text (x + y))' "" "30"

# --- Deep do chain ---
compile_and_boot "do-chain" \
    'Chapter: T
Section: Funcs
  step : Integer -> [Console] Integer
  step (n) = do
   print-line (integer-to-text n)
   let r = n + 1
   in r
Section: Main
  main : [Console] Nothing
  main = do
   a <- step 1
   b <- step a
   c <- step b
   print-line (integer-to-text c)' "" "1"

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
if [ "$FAIL" -gt 0 ]; then
    echo ""
    printf "$ERRORS"
    exit 1
fi
