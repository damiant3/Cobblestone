#!/bin/bash
# Build the two native tools that take QEMU out of the pipeline.
#
#   codexir   .codex -> .ir     (the compiler, emitting IR instead of a CDX)
#   zigemit   .ir    -> .zig    (the plug, as a program instead of a kernel)
#
# Each is built the way zigc was: bundle the subject, compile it to IR with the
# seed, push that IR through the ring plug, and build the emitted zig. The last
# step is the only one that is not a VM, which is the whole point of building
# these -- afterwards the chain
#
#   codexir prog.codex 2>prog.ir && zigemit prog.ir 2>prog.zig && zig build-exe prog.zig
#
# is three native processes. Output lands on stderr because print-text is
# cx_print is std.debug.print; that is a wart, not a design, and it is why the
# 2> redirects are there.
#
# No arguments. Both tools, every time, in order, stopping on the first failure
# -- the failure modes are shared and finding them on the smaller subject first
# costs less.
set -e
T="$(cd "$(dirname "$0")" && pwd)"
OUT="$T/native"
mkdir -p "$OUT"

build_one() {
    local name=$1 gen=$2 bundle=$3 subject=$4
    echo "############ $name"
    cd "$T/ast"
    if [ -n "$gen" ]; then python3 "$gen"; fi

    rm -f "$subject"
    ~/.local/pwsh/pwsh -NoProfile -File "./$bundle" | tail -1
    [ -s "$subject" ] || { echo "BUNDLE FAILED: no $subject"; return 1; }

    python3 - "$subject" "$name" <<'PY'
import sys
src = open(sys.argv[1], 'rb').read()
open(f'{sys.argv[2]}-ir.blob', 'wb').write(b"IR-CCE decks=172\n" + src + b"\x04")
print(f"blob: {len(src)} bytes of source")
PY

    cd "$T"
    echo "--- compiling $name to IR (seed, in QEMU)"
    rm -f "ast/$name.ir"
    python3 -u ring_compile.py "ast/$name-ir.blob" "ast/$name.ir" 2>&1 | tail -3
    [ -s "ast/$name.ir" ] || { echo "COMPILE FAILED: no $name.ir"; return 1; }

    echo "--- transpiling $name through the plug (in QEMU, for the last time)"
    rm -f "ast/$name.zig"
    python3 -u plug_run_ring.py "ast/$name.ir" "ast/$name.zig" > "ast/$name.transport.log" 2>&1 \
        || { echo "TRANSPORT FAILED ($name):"; tail -5 "ast/$name.transport.log"; return 1; }

    local markers
    markers=$(grep -c '@compileError' "ast/$name.zig" || true)
    if [ "$markers" != "0" ]; then
        echo "REFUSED: $markers markers in $name.zig"
        grep -o '@compileError("zig plug: [^"]*")' "ast/$name.zig" | sort | uniq -c
        return 1
    fi

    echo "--- building the native binary"
    zig build-exe "ast/$name.zig" -femit-bin="$OUT/$name"
    ls -la "$OUT/$name" | awk '{print "    " $NF, $5, "bytes"}'
    echo "############ $name built"
}

build_one zigemit ""                       bundle_zigemit.ps1  zigemit-source.codex
build_one codexir gen_codexir_harness.py   bundle_codexir.ps1  codexir-subject.codex

echo "############ both built in $OUT"
