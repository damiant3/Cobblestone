# Shared machinery for the up-to-AST milestone oracles. Each milestone is a
# subject bundled from real compiler chapters plus a generated harness, run
# two ways -- seed-compiled bare metal as truth, and through the zig plug --
# and required to agree byte for byte.
#
# Milestone <m> owns: gen_<m>_harness.py, bundle_<m>.ps1, and the artifacts
# <m>-subject.codex, <m>.ir, <m>.truth, <m>.zig, <m>.zigout, <m>.diff.
T="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO="$(cd "$T/.." && pwd)"

# Extra mode flags appended to the command line of BOTH blobs, per milestone.
# Empty wherever the seed's derived deck scale is enough, so a milestone that
# already passes keeps compiling byte for byte as it did.
#
# derive-deck-scale is unit-len * 200 / 2993576 clamped to [64, 100]: the
# check subject lands at 64, and lower -- the same subject plus 2,581 lines
# of Lowering -- lands at 69 and still overflows the seed's CHECK deck
# (CDX9002). An explicit decks= skips the clamp, which is the only headroom
# available; raising QEMU's memory is not an option on this machine.
mode_flags() {
    case "$1" in
        lower) echo " decks=100" ;;
        fib)   echo " decks=100" ;;
        fibx)  echo " decks=160" ;;
        # passes=text-plug drops the inline passes. Passes.codex says why in
        # so many words: "A plug that emits SOURCE resolves a call by its
        # name, so a pass that substitutes a body and deletes the call
        # deletes the plug's only handle on it." Emitting codex text from
        # inlined IR would produce source with the calls gone -- still a
        # fixed point, but a worthless round trip.
        text)  echo " decks=100 passes=text-plug" ;;
        pingpong) echo " decks=100 passes=text-plug" ;;
        *)     echo "" ;;
    esac
}

# Generate the harness, bundle the subject, compile it both ways, run the
# bare-metal binary, bank its output as the truth side.
truth_arm() {
    local m=$1
    cd $T/ast
    python3 gen_${m}_harness.py

    # NOT `pwsh ... | tail -1`: under a pipe the status is tail's, and
    # plug-build-lib exits 3 on an unresolvable cite. Update 42 added a
    # BootPaint cite to PhaseAllocator; the bundler refused correctly, this
    # line swallowed it, and four rungs went on to compile the PREVIOUS
    # subject still sitting on disk -- two of them banked truth from it.
    # The subject is removed first so a refusal cannot leave a stale one
    # that looks like a fresh one.
    rm -f ${m}-subject.codex
    local bout
    if ! bout=$(~/.local/pwsh/pwsh -NoProfile -File ./bundle_${m}.ps1 2>&1); then
        printf '%s\n' "$bout" | tail -5
        echo "BUNDLE FAILED for $m"; return 1
    fi
    printf '%s\n' "$bout" | tail -1
    [ -s ${m}-subject.codex ] || { echo "BUNDLE FAILED: no ${m}-subject.codex"; return 1; }

    python3 - "$m" "$(mode_flags $m)" <<'PY'
import sys
m, flags = sys.argv[1], sys.argv[2]
src = open(f'{m}-subject.codex', 'rb').read()
open(f'{m}-cdx.blob', 'wb').write(b"CDX map" + flags.encode() + b"\n" + src + b"\x04")
open(f'{m}-ir-cce.blob', 'wb').write(b"IR-CCE" + flags.encode() + b"\n" + src + b"\x04")
print(f"blobs written ({len(src)} bytes of source), mode flags:{flags or ' none'}")
PY

    cd $T
    # A failed compile must stop the run. Banking a truth file from a stale
    # or missing binary is how a broken subject looks like a passing one.
    echo "--- compiling subject to a bare-metal binary"
    rm -f ast/${m}-subject.cdx ast/${m}.ir
    if ! python3 -u ring_compile.py ast/${m}-cdx.blob ast/${m}-subject.cdx 2>&1 | tail -20; then
        echo "COMPILE FAILED (bare metal) -- see the diagnostics above"; return 1
    fi
    [ -s ast/${m}-subject.cdx ] || { echo "COMPILE FAILED: no ${m}-subject.cdx"; return 1; }

    echo "--- compiling subject to IR-CCE for the plug"
    if ! python3 -u ring_compile.py ast/${m}-ir-cce.blob ast/${m}.ir 2>&1 | tail -20; then
        echo "COMPILE FAILED (IR-CCE) -- see the diagnostics above"; return 1
    fi
    [ -s ast/${m}.ir ] || { echo "COMPILE FAILED: no ${m}.ir"; return 1; }

    echo "--- running the subject on bare metal"
    python3 - "$m" <<'PY'
import sys
import codex_vm
m = sys.argv[1]
# idle_timeout is silence tolerance, not total runtime: these subjects
# compute for a long stretch before their first print.
out = codex_vm.run_cdx(f'ast/{m}-subject.cdx', timeout=5400, idle_timeout=600)
lines = [l for l in out.decode(errors='replace').splitlines()
         if not l.startswith(("WD:", "HEAP:", "STACK:"))]
open(f'ast/{m}.truth', 'w').write("\n".join(lines) + "\n")
print(f"banked ast/{m}.truth: {len(lines)} lines")
PY
}

# Push the IR through the plug (verified transfer -- see
# TRANSPORT_CORRUPTION.md), run the emitted zig, diff against the truth.
zig_arm() {
    local m=$1
    cd $T
    rm -f ast/${m}.zig
    python3 -u plug_run_checked.py \
        $REPO/codex/plugs/zig/build-output/zig-plug.cdx \
        ast/${m}.ir ast/${m}.zig
    cd ast
    # program output goes to stderr (std.debug.print); truth was serial bytes
    if timeout 600 zig run ${m}.zig 2> ${m}.zigout; then
        if diff <(tr -d '\r' < ${m}.truth) ${m}.zigout > ${m}.diff 2>&1; then
            echo "ORACLE PASS: zig $m output byte-identical to bare-metal truth"
        else
            echo "ORACLE DIFF (first 15 lines):"
            head -15 ${m}.diff
            return 1
        fi
    else
        echo "--- zig compile/run errors:"
        head -40 ${m}.zigout
        return 1
    fi
}
