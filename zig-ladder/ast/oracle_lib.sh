# Shared machinery for the up-to-AST milestone oracles. Each milestone is a
# subject bundled from real compiler chapters plus a generated harness, run
# two ways -- seed-compiled bare metal as truth, and through the zig plug --
# and required to agree byte for byte.
#
# Milestone <m> owns: gen_<m>_harness.py, bundle_<m>.ps1, and the artifacts
# <m>-subject.codex, <m>.ir, <m>.truth, <m>.zig, <m>.zigout, <m>.diff.
T="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"  # ladder-root-bootstrap: reaches the LADDER only; the checkout comes from ladder_root
REPO="$(python3 "$T/ladder_root.py" codex)"

# The ladder, cheapest first. allcycles.sh sweeps it and rebank_all.sh
# re-banks it, and they must not disagree about what the ladder is: a rung
# missing from one is a rung whose truth is stale while its diff still
# reports green.
LADDER_RUNGS="lex parse desugar scope check lower text pingpong lir fib fibx scale whole clamp"

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
        # Same bundle as fibx -- the subject differs only by the 19KB of CCE
        # riding in its Text literal -- so it needs the same deck scale.
        scale) echo " decks=160" ;;
        # 160 scaled by unit length: whole is 2,578,233 bytes against fibx's
        # 2,398,065, so 160 * 2578233/2398065 = 172. Deck scale tracks the
        # unit, and guessing low here costs a ten-minute cycle to find out.
        whole) echo " decks=172" ;;
        clamp) echo " decks=172" ;;
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

# The pingpong rung's real claim, which the arm diff does not make.
#
# pingpong's subject IS text.truth: stage 2 compiles the text stage 1 emitted
# and emits again, so a compiler that round-trips its own output must produce
# the same bytes back. Both arms agreeing says nothing about that -- they would
# agree just as contentedly on a second pass that dropped half the chapter,
# because both would drop the same half.
#
# This was written in truthcycle_pingpong.sh as "the whole claim" and checked
# by nothing. The files matched, by luck rather than by test, for as long as
# the rung has existed. A missing file fails here rather than passing quietly:
# an unrun rung and a green one must not look alike.
pingpong_fixed_point() {
    cd $T/ast
    local f
    for f in text.truth pingpong.truth; do
        [ -s "$f" ] || {
            echo "FIXED POINT UNCHECKED: no $f (run truthcycle_text.sh and truthcycle_pingpong.sh)"
            return 1
        }
    done
    if diff <(tr -d '\r' < text.truth) <(tr -d '\r' < pingpong.truth) \
            > pingpong.fixpoint.diff 2>&1; then
        echo "FIXED POINT: pingpong.truth byte-identical to text.truth"
    else
        echo "FIXED POINT BROKEN (first 15 lines):"
        head -15 pingpong.fixpoint.diff
        return 1
    fi
}

# Run the emitted zig, diff against the truth. Shared by both arms: the
# transport is what differs between them, never the verdict.
zig_verdict() {
    local m=$1
    cd $T/ast
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

# Push the IR through the plug over TCP (verified transfer -- see
# TRANSPORT_CORRUPTION.md). The default arm: it exercises the
# Codex-written network stack on every run, which is itself an oracle
# surface and is where the odd-frame defect was caught.
zig_arm() {
    local m=$1
    cd $T
    rm -f ast/${m}.zig
    # Transport chatter goes to a log, not to the caller: the sweep prints
    # a bounded slice of each rung's output, and a transfer that narrates
    # 25 lines pushed the verdict past the cut -- fibx passed SILENTLY,
    # which reads exactly like a rung that never ran.
    if ! python3 -u plug_run_checked.py \
        $REPO/codex/plugs/zig/build-output/zig-plug.cdx \
        ast/${m}.ir ast/${m}.zig > ast/${m}.transport.log 2>&1; then
        echo "TRANSPORT FAILED for $m (ast/${m}.transport.log):"
        tail -6 ast/${m}.transport.log
        return 1
    fi
    zig_verdict $m
}

# The ring arm, for subjects past the TCP intake ceiling: the receive
# path costs ~130 bytes of guest heap per IR byte and fibx is 12.9 MB,
# where read-serial-cce costs one. Same parser, same emitter, and the
# ring plug is rebuilt from the same ZigEmitter -- only the transport
# and the plug body differ.
ring_arm() {
    local m=$1
    cd $T
    rm -f ast/${m}.zig
    if ! python3 -u plug_run_ring.py ast/${m}.ir ast/${m}.zig \
        > ast/${m}.transport.log 2>&1; then
        echo "TRANSPORT FAILED for $m (ast/${m}.transport.log):"
        tail -6 ast/${m}.transport.log
        return 1
    fi
    zig_verdict $m
}

# Which transport a rung needs is a property of the rung, so the sweep
# asks here rather than carrying a list of its own.
arm_for() {
    case "$1" in
        fibx|scale|whole|clamp) echo ring_arm ;;
        *)                echo zig_arm ;;
    esac
}
