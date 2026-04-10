#!/bin/bash
# Bisect: which language feature crashes Stage 0?
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"
    local PIPE=/tmp/bi-pipe-$$; local RAW=/tmp/bi-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    sleep 15; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    if grep -qa 'SIZE:' "$RAW"; then echo "  $LABEL: OK"
    else echo "  $LABEL: CRASH"
    fi
    rm -f "$RAW"
}

echo "=== Feature bisect ==="

test_src "negate" 'Chapter: T
  main : Integer
  main = 0 - 42'

test_src "char-lit" "Chapter: T
  main : Integer
  main = char-code 'a'"

test_src "long-string" 'Chapter: T
  main : Integer
  main = text-length "abcdefghijklmnopqrstuvwxyz0123456789"'

test_src "pattern-match" 'Chapter: T
  X = | A | B (Integer)
  f : X -> Integer
  f (x) = when x
    if A -> 1
    if B (n) -> n
  main : Integer
  main = f (B 42)'

test_src "do-bind" 'Chapter: T
  main : [Console] Nothing
  main = do
   x <- read-line
   print-line x'

test_src "list-snoc-loop" 'Chapter: T
  build : Integer -> List Integer -> List Integer
  build (n) (acc) =
    if n == 0 then acc
    else build (n - 1) (list-snoc acc n)
  main : Integer
  main = list-length (build 100 [])'

test_src "linked-list" 'Chapter: T
  main : Integer
  main = let ll = linked-list-push (linked-list-push (linked-list-empty 0) 10) 20
    in list-length (linked-list-to-list ll)'

test_src "record-set" 'Chapter: T
  P = record { x : Integer, y : Integer }
  main : Integer
  main = let p = P { x = 1, y = 2 }
    in let p2 = record-set p "x" 99
    in p2.x'

test_src "text-concat" 'Chapter: T
  main : Integer
  main = text-length ("hello" ++ " " ++ "world")'

test_src "20-strings" 'Chapter: T
  s1 : Text
  s1 = "the quick brown fox jumps over the lazy dog"
  s2 : Text
  s2 = "pack my box with five dozen liquor jugs"
  s3 : Text
  s3 = "how vexingly quick daft zebras jump"
  s4 : Text
  s4 = "the five boxing wizards jump quickly"
  s5 : Text
  s5 = "abcdefghijklmnopqrstuvwxyz 0123456789"
  main : Integer
  main = text-length s1 + text-length s2 + text-length s3 + text-length s4 + text-length s5'
