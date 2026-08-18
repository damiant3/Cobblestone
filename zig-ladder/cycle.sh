#!/bin/bash
# One warmup iteration: rebundle the zig plug (compile step stubbed),
# ring-compile it, run each named program's IR through it, zig-run the
# results. Usage: cycle.sh hello recurse fib
set -e
T="$(cd "$(dirname "$0")" && pwd)"
REPO="$(python3 "$T/ladder_root.py" codex)"
# Warmup blobs and IRs live in the repo, not /tmp: a reboot wiped the
# original scratchpad location and every warmup IR with it.
S=$T/warmups
mkdir -p "$S"

~/.local/pwsh/pwsh -NoProfile -Command "
. $REPO/codex/plugs/common/plug-build-lib.ps1
function Build-PlugCdx { Write-Host '[bundle only, compile skipped]' }
Build-TranspilerPlug -PlugDir $REPO/codex/plugs/zig -PlugName zig -Chapters @('ZigEmitter', 'ZigPlug')
"

python3 - <<PY
src = open('$REPO/codex/plugs/zig/build-output/plug-source.codex','rb').read()
open('$S/plug-unit.blob','wb').write(b"CDX map\n" + src + b"\x04")
PY

cd "$T"
# NOT `ring_compile.py ... | grep ... | tail`: under a pipe the status is
# tail's, and ring_compile exits 1 on a halted compile. It halted on a
# ZigEmitter edit, this line reported nothing, plugcycle printed REBUILT, and
# the arm reran against the plug already sitting in build-output -- so a plug
# that had not been rebuilt produced the same failure as before the fix, which
# reads exactly like a fix that did not work. oracle_lib.sh carries the same
# warning about the same trap.
#
# The stale binary is the other half: a compile that refuses leaves the old
# zig-plug.cdx in place, so no downstream -s check can tell a rebuild from a
# refusal. Remove it first and a refusal has nothing to hide behind.
PLUG_CDX="$REPO/codex/plugs/zig/build-output/zig-plug.cdx"
rm -f "$PLUG_CDX"
if ! cout=$(python3 -u ring_compile.py "$S/plug-unit.blob" "$PLUG_CDX" 2>&1); then
    printf '%s\n' "$cout" | grep -vE "^(WD|HEAP|STACK):" | tail -25
    echo "PLUG COMPILE FAILED -- see the diagnostics above"
    exit 1
fi
printf '%s\n' "$cout" | grep -vE "^(WD|HEAP|STACK):" | tail -25
[ -s "$PLUG_CDX" ] || { echo "PLUG COMPILE FAILED: no zig-plug.cdx"; exit 1; }

# Each warmup diffs against its banked bare-metal truth (see
# warmups/regen.sh), so a pass is an oracle match, not an eyeball.
for prog in "$@"; do
    echo "=== $prog ==="
    python3 -u plug_run.py "$REPO/codex/plugs/zig/build-output/zig-plug.cdx" "$S/$prog.ir" "$S/$prog.zig" 2>&1 | tail -2
    (cd "$S" && timeout 90 zig run "$prog.zig" 2> "$prog.zigout") || true
    if diff "$S/$prog.truth" "$S/$prog.zigout" > /dev/null 2>&1; then
        echo "WARMUP PASS: $prog matches bare-metal truth"
    else
        echo "WARMUP DIFF for $prog:"
        diff "$S/$prog.truth" "$S/$prog.zigout" | head -10 || true
    fi
done
