#!/bin/bash
STAGE0=/mnt/d/Projects/NewRepository-cam/build-output/bare-metal/Codex.Codex.elf

test_src() {
    local LABEL="$1"; local SRC="$2"; local EXPECT="$3"
    local PIPE=/tmp/ml-p-$$; local RAW=/tmp/ml-r-$$; local ELF=/tmp/ml-$$.elf
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local H=$!
    timeout 30 qemu-system-x86_64 -enable-kvm -kernel "$STAGE0" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local Q=$!
    for i in $(seq 1 20); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    printf "BINARY\n%s\x04" "$SRC" > "$PIPE" &
    while true; do sleep 1; grep -qa 'SIZE:' "$RAW" && break; kill -0 $Q 2>/dev/null || break; done
    if ! grep -qa 'SIZE:' "$RAW"; then
        echo "FAIL  $LABEL (compile fail)"; kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return; fi
    local SZ=$(grep -a 'SIZE:' "$RAW" | head -1 | sed 's/.*SIZE://' | tr -dc '0-9')
    local SOFF=$(grep -boa 'SIZE:' "$RAW" | head -1 | cut -d: -f1)
    local BSTART=$((SOFF + 5 + ${#SZ} + 1))
    local NEEDED=$((BSTART + SZ))
    while [ "$(wc -c < "$RAW")" -lt "$NEEDED" ]; do kill -0 $Q 2>/dev/null || break; sleep 1; done
    kill $Q $H 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    dd if="$RAW" bs=1 skip="$BSTART" count="$SZ" of="$ELF" 2>/dev/null
    rm -f "$RAW"
    local OUT=$(timeout 5 qemu-system-x86_64 -enable-kvm -kernel "$ELF" \
        -serial stdio -display none -no-reboot -m 512 < /dev/null 2>/dev/null | head -c 200)
    local LINE2=$(echo "$OUT" | sed -n '2p')
    if [ "$LINE2" = "$EXPECT" ]; then echo "PASS  $LABEL"
    else echo "FAIL  $LABEL (expected '$EXPECT', got '$LINE2')"
    fi
    rm -f "$ELF"
}

echo "=== Mini lexer tests ==="

# Recursive scan with record state — like the real lexer
test_src "rec-scan-simple" \
    'Chapter: T
Section: Types
  Tok = record { kind : Integer, pos : Integer }
  State = record { src : Text, pos : Integer, len : Integer }
Section: Funcs
  make-state : Text -> State
  make-state (s) = State { src = s, pos = 0, len = text-length s }

  scan-one : State -> Tok
  scan-one (st) =
    if st.pos >= st.len then Tok { kind = 0, pos = st.pos }
    else let c = char-code-at (st.src) (st.pos)
    in Tok { kind = c, pos = st.pos }

  scan-all : State -> List Tok -> List Tok
  scan-all (st) (acc) =
    let tok = scan-one st
    in if tok.kind == 0 then acc
    else scan-all (State { src = st.src, pos = st.pos + 1, len = st.len }) (list-snoc acc tok)
Section: Main
  main : Integer
  main = list-length (scan-all (make-state "abc") [])' "3"

# Multiple record-returning functions chained (like lex-state threading)
test_src "state-chain" \
    'Chapter: T
Section: Types
  S = record { val : Integer, count : Integer }
Section: Funcs
  step : S -> S
  step (s) = S { val = s.val + 1, count = s.count + 1 }

  run : S -> Integer -> S
  run (s) (n) = if n == 0 then s else run (step s) (n - 1)
Section: Main
  main : Integer
  main = let s = run (S { val = 0, count = 0 }) 100
    in s.val + s.count' "200"

# if-else chain with string comparison (like classify-word)
test_src "long-if-else-chain" \
    'Chapter: T
Section: Types
  K = | Ka | Kb | Kc | Kd | Ke | Kf | Kg | Kh | Ki | Kj | Kk | Kl | Km | Kn | Ko
Section: Funcs
  classify : Text -> K
  classify (s) =
    if s == "a" then Ka
    else if s == "b" then Kb
    else if s == "c" then Kc
    else if s == "d" then Kd
    else if s == "e" then Ke
    else if s == "f" then Kf
    else if s == "g" then Kg
    else if s == "h" then Kh
    else if s == "i" then Ki
    else if s == "j" then Kj
    else if s == "k" then Kk
    else if s == "l" then Kl
    else if s == "m" then Km
    else if s == "n" then Kn
    else Ko
  tag : K -> Integer
  tag (k) = when k
    if Ka -> 0
    if Kb -> 1
    if Kc -> 2
    if Kd -> 3
    if Ke -> 4
    if Kf -> 5
    if Kg -> 6
    if Kh -> 7
    if Ki -> 8
    if Kj -> 9
    if Kk -> 10
    if Kl -> 11
    if Km -> 12
    if Kn -> 13
    if Ko -> 14
Section: Main
  main : Integer
  main = tag (classify "g")' "6"
