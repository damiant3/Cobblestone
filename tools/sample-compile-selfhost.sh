#!/bin/bash
# Compile a single .codex sample by booting the bare-metal self-host
# compiler (Codex.Codex.elf) in QEMU and piping the source over serial in
# BINARY mode. Extracts the emitted ELF from the serial stream and writes
# it to the output path.
#
# Usage: sample-compile-selfhost.sh <source.codex> <out.elf> <log.out>
# Exit 0 = compile succeeded, out.elf is a valid ELF.
# Exit non-zero = compile failed; log.out contains the serial output for
# diagnostic-code matching.
set -u

SRC=$1
OUT=$2
LOG=$3

STAGE0="build-output/bare-metal/Codex.Codex.elf"
QEMU=/usr/bin/qemu-system-x86_64

if [ ! -f "$STAGE0" ]; then
    echo "MISSING: $STAGE0 — run pingpong.sh first to build the self-host ELF" >&2
    exit 2
fi

raw="/tmp/sh-compile-$$"
pipe="/tmp/sh-compile-pipe-$$"
rm -f "$raw" "$pipe"
mkfifo "$pipe"

# Holder keeps the pipe open while we drip source into it.
sleep 999 > "$pipe" &
holder=$!

timeout 45 "$QEMU" -enable-kvm -kernel "$STAGE0" -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 < "$pipe" > "$raw" 2>/dev/null &
qemu=$!

# Wait for READY before feeding source.
ready_wait=0
while ! grep -qa 'READY' "$raw" 2>/dev/null; do
    sleep 0.2
    ready_wait=$((ready_wait + 1))
    if [ "$ready_wait" -gt 100 ]; then
        kill $qemu $holder 2>/dev/null
        wait 2>/dev/null
        cp "$raw" "$LOG"
        rm -f "$raw" "$pipe"
        echo "READY not received within 20s" >> "$LOG"
        exit 3
    fi
    kill -0 $qemu 2>/dev/null || break
done

# Foreword preload — bare-metal has no filesystem, so prepend cited Foreword
# chapters to the serial feed. Mirrors tools/Codex.Bootstrap/Program.cs's
# LoadCitedForewordChapters. Chapter headers are renamed "Chapter: X" →
# "Chapter: Foreword--X" so they coexist with the citing chapter in one source.
fwcites=$(grep -E '^[[:space:]]*cites[[:space:]]+Foreword[[:space:]]+chapter[[:space:]]+[A-Za-z_][A-Za-z0-9_-]*' "$SRC" \
          | sed -E 's/^[[:space:]]*cites[[:space:]]+Foreword[[:space:]]+chapter[[:space:]]+([A-Za-z_][A-Za-z0-9_-]*).*/\1/' \
          | sort -u)
fwtmp="/tmp/sh-compile-fw-$$"
: > "$fwtmp"
if [ -n "$fwcites" ]; then
    for name in $fwcites; do
        fwpath="foreword/$name.codex"
        if [ ! -f "$fwpath" ]; then
            cp "$raw" "$LOG"
            kill $qemu $holder 2>/dev/null
            wait 2>/dev/null
            rm -f "$raw" "$pipe" "$fwtmp"
            echo "error 3010: Cited foreword chapter '$name' not found (expected $fwpath)" >> "$LOG"
            exit 8
        fi
        awk '!done && /^Chapter:[[:space:]]*/ {
                sub(/^Chapter:[[:space:]]*/, "");
                sub(/[[:space:]]*$/, "");
                print "Chapter: Foreword--" $0;
                done=1; next
             }
             { print }' "$fwpath" >> "$fwtmp"
        printf '\n\n' >> "$fwtmp"
    done
fi

# Feed source in BINARY mode, terminated by EOT (0x04).
(printf 'BINARY\n'; cat "$fwtmp" "$SRC"; printf '\x04') > "$pipe" &

# Wait for compile to finish: either SIZE: marker (success) or
# "CODEGEN-HALTED"/"error" strings (failure). Cap total time.
prev_size=0
stable=0
for i in $(seq 1 120); do
    sleep 1
    cur_size=$(wc -c < "$raw" 2>/dev/null || echo 0)
    if grep -qa 'SIZE:\|CODEGEN-HALTED\|CODEGEN-ERRORS' "$raw" 2>/dev/null; then
        if [ "$cur_size" -gt 100 ] && [ "$cur_size" -eq "$prev_size" ]; then
            stable=$((stable + 1))
            [ "$stable" -ge 2 ] && break
        else
            stable=0
        fi
    fi
    prev_size=$cur_size
    kill -0 $qemu 2>/dev/null || break
done

kill $qemu $holder 2>/dev/null
wait 2>/dev/null

# Extract the stage-1 ELF from serial stream. Format:
#   ... diagnostic text ...
#   SIZE:<n>\n
#   <n bytes of ELF>
#   HEAP:<h>\n
#   STACK:<s>\n
if ! grep -qa 'SIZE:' "$raw"; then
    cp "$raw" "$LOG"
    rm -f "$raw" "$pipe"
    exit 4
fi

size_line=$(grep -a 'SIZE:' "$raw" | head -1)
elf_size=${size_line#*SIZE:}
elf_size=${elf_size%%[!0-9]*}
size_byte_off=$(grep -boa 'SIZE:' "$raw" | head -1 | cut -d: -f1)
binary_start=$((size_byte_off + 5 + ${#elf_size} + 1))

dd if="$raw" bs=1 skip="$binary_start" count="$elf_size" of="$OUT" 2>/dev/null
got_size=$(wc -c < "$OUT")

# Trim the raw to just the text portion (before SIZE:) for the log.
head -c "$size_byte_off" "$raw" > "$LOG"

rm -f "$raw" "$pipe" "$fwtmp"

if [ "$got_size" -ne "$elf_size" ]; then
    echo "ELF size mismatch: expected $elf_size got $got_size" >> "$LOG"
    exit 5
fi

[ "$elf_size" -lt 100 ] && { echo "ELF too small ($elf_size)" >> "$LOG"; exit 6; }
exit 0
