#!/bin/bash
# Test: compile progressively larger programs through Stage 1
REPO=/mnt/d/Projects/NewRepository-cam
STAGE1="$REPO/build-output/bare-metal/stage1.elf"

test_compile() {
    local LABEL="$1"
    local SOURCE="$2"
    local TIMEOUT="${3:-60}"
    local PIPE=/tmp/mc-pipe-$$
    local RAW=/tmp/mc-raw-$$
    rm -f "$PIPE" "$RAW"; mkfifo "$PIPE"
    sleep 999 > "$PIPE" & local HOLDER=$!
    timeout "$TIMEOUT" qemu-system-x86_64 -enable-kvm -kernel "$STAGE1" \
        -serial stdio -display none -no-reboot -m 512 \
        < "$PIPE" > "$RAW" 2>/dev/null & local QEMU=$!
    for i in $(seq 1 40); do grep -qa 'READY' "$RAW" 2>/dev/null && break; sleep 0.5; done
    if ! grep -qa 'READY' "$RAW" 2>/dev/null; then
        echo "  $LABEL: NO READY"
        kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" "$RAW"; return
    fi
    printf "BINARY\n%s\x04" "$SOURCE" > "$PIPE" &
    # Wait for output to stabilize
    local prev=0 stable=0
    for i in $(seq 1 30); do
        sleep 2
        local cur=$(wc -c < "$RAW" 2>/dev/null)
        if [ "$cur" -gt 100 ] && [ "$cur" -eq "$prev" ]; then
            stable=$((stable + 1))
            [ "$stable" -ge 2 ] && break
        else
            stable=0
        fi
        prev=$cur
        kill -0 $QEMU 2>/dev/null || break
    done
    kill $QEMU $HOLDER 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"
    if grep -qa 'SIZE:' "$RAW"; then
        local SIZE_LINE=$(grep 'SIZE:' "$RAW" | head -1)
        echo "  $LABEL: OK — $SIZE_LINE"
    else
        local BYTES=$(wc -c < "$RAW")
        echo "  $LABEL: FAIL ($BYTES bytes output, no SIZE:)"
        head -c 100 "$RAW" | tr '\0' '.'
        echo ""
    fi
    rm -f "$RAW"
}

echo "=== Testing Stage 1 with progressively larger programs ==="

test_compile "pure-42" 'Chapter: T1

  main : Integer
  main = 42'

test_compile "effectful-print" 'Chapter: T2

  main : [Console] Nothing
  main = do
   print-line "hello"'

test_compile "mini-bootstrap" "$(cat "$REPO/samples/mini-bootstrap.codex")"

test_compile "let-chain" 'Chapter: T3

  f : Integer -> Integer
  f (x) = x * 2 + 1

  g : Integer -> Integer -> Integer
  g (a) (b) = f a + f b

  main : Integer
  main = g 10 20'

test_compile "match+record" 'Chapter: T4

  Shape =
    | Circle (Integer)
    | Rect (Integer) (Integer)

  area : Shape -> Integer
  area (s) =
    when s
      if Circle (r) -> r * r * 3
      if Rect (w) (h) -> w * h

  main : Integer
  main = area (Circle 5) + area (Rect 3 4)'

echo ""
echo "Done."
