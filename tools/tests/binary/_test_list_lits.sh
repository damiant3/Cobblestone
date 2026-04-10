#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf
COMBINED=/tmp/ll-$$
cat > "$COMBINED" << 'EOF'
Chapter: ListTest

Section: Data

  d1 : List Text
  d1 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d2 : List Text
  d2 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d3 : List Text
  d3 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d4 : List Text
  d4 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d5 : List Text
  d5 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d6 : List Text
  d6 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d7 : List Text
  d7 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d8 : List Text
  d8 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d9 : List Text
  d9 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d10 : List Text
  d10 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d11 : List Text
  d11 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d12 : List Text
  d12 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d13 : List Text
  d13 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d14 : List Text
  d14 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d15 : List Text
  d15 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d16 : List Text
  d16 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d17 : List Text
  d17 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d18 : List Text
  d18 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d19 : List Text
  d19 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

  d20 : List Text
  d20 = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"]

Section: Main

  main : Integer
  main = list-length d1 + list-length d2 + list-length d3 + list-length d4 + list-length d5
EOF
echo "Source: $(wc -c < "$COMBINED") bytes (200 string lits in 20 lists)"
PIPE=/tmp/ll-p-$$; RAW=/tmp/ll-r-$$
rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
sleep 999 > "$PIPE" & H=$!
timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
    -serial stdio -display none -no-reboot -m 512 \
    < "$PIPE" > "$RAW" 2>/dev/null & Q=$!
for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
(printf 'BINARY\n'; cat "$COMBINED"; printf '\x04') > "$PIPE" &
sleep 20; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$COMBINED"
if grep -qa 'SIZE:' "$RAW"; then echo "OK"
else echo "CRASH (output: $(wc -c < "$RAW") bytes)"
fi
rm -f "$RAW"
