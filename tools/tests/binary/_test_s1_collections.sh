#!/bin/bash
# Test Collections-like patterns that are self-contained
STAGE1=/tmp/stage1.elf

PIPE=/tmp/tc-p-$$; RAW=/tmp/tc-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 20 qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done

SRC='Chapter: Test

  map-list : (a -> b) -> List a -> List b
  map-list (f) (xs) =
   map-list-loop f xs 0 (list-length xs) []

  map-list-loop : (a -> b) -> List a -> Integer -> Integer -> List b -> List b
  map-list-loop (f) (xs) (i) (len) (acc) =
   if i == len then acc
   else map-list-loop f xs (i + 1) len (list-snoc acc (f (list-at xs i)))

  fold-list : (b -> a -> b) -> b -> List a -> b
  fold-list (f) (z) (xs) =
   fold-list-loop f z xs 0 (list-length xs)

  fold-list-loop : (b -> a -> b) -> b -> List a -> Integer -> Integer -> b
  fold-list-loop (f) (z) (xs) (i) (len) =
   if i == len then z
   else fold-list-loop f (f z (list-at xs i)) xs (i + 1) len

  sorted-insert : List Text -> Text -> List Text
  sorted-insert (xs) (name) =
   list-snoc xs name

  sort-text-list : List Text -> List Text
  sort-text-list (xs) = fold-list sorted-insert [] xs

  double (x) = x + x

  main = list-length (map-list double [1, 2, 3])
'

(printf 'TEXT\n'; printf '%s' "$SRC"; printf '\x04') > "$PIPE" &
sleep 8
kill $Q $H 2>/dev/null; wait 2>/dev/null

sz=$(wc -c < "$RAW")
if grep -qa 'HEAP:' "$RAW"; then
    echo "PASS: collections-style ($sz bytes)"
    grep -a 'main' "$RAW" | head -3
else
    echo "FAIL ($sz bytes)"
    cat -v "$RAW" | head -10
fi
rm -f "$PIPE" "$RAW"
