# Where the safari application came from

This application is not ours. It is Steve Howell's `safari-codex`, his port of
a Safari driving screensaver to Codex, taken into this tree with his
permission and with his name on it.

    source      github.com/showell/safari-codex
    branch      master
    commit      571d7d0864274b2acfe5871645c8c2b7d3c74a75
    author      Steve Howell
    dated       2026-09-01
    subject     build_wasm: name the artifacts after the entry, and build the
                native leg

**The repository carries no licence file.** Permission to take this code was
given by Steve Howell directly to Damian Tedrow on 2026-09-02, in conversation.
That permission, and not a licence header, is the basis on which these files
are here. Anyone extending this application to new material from the same
author should ask again rather than treat this page as a standing grant.

## What was taken, measured at the commit above

| tree | files | bytes |
|---|---|---|
| `port/` (the Codex chapters) | 27 | 1,028,221 |
| `judge/` (the grading chapters) | 19 | 150,784 |
| `gold/` (the goldens) | 18 | 552,964 |
| `probe/` (differential probes) | 29 | 120,956 |
| `poc/` | 15 | 77,259 |
| `harness/` (python, shell and js) | 32 | 204,834 |

`build/` in the source repository is GENERATED, several of its files past
3 MB, and is deliberately not taken. Of `port/`, `CatStills.codex` at 550 KB
and `EmojiStills.codex` at 232 KB are generated frame data rather than
hand-written chapters.

The documents that came with it are `README.md` (73 KB), `PORTING_NOTES.txt`
(91 KB), `WASM_FINDINGS.md` (50 KB), `FINDINGS.md` (40 KB), `NOTES.txt` (27 KB)
and `PLUG_WORK.md` (22 KB). They are the author's own account of the port and
of the toolchain defects he hit making it.

## THE CHAPTERS ARE BUILT AGAINST A FORK OF COBBLESTONE, NOT AGAINST OUR MAIN

This is the fact to read before compiling a line, because it decides how to
read a failure. The source repository's own `PROVENANCE.md` pins the language
it was built against, and that pin is not our head:

    base        58b08c38   Update 53
    + PR 100    3 commits  real-to-int / real-from-int and real-to-bits /
                           bits-to-real in the ZIG plug. `judge/Grade.codex`
                           calls real-to-bits to reject a non-finite value, so
                           the checks do not build without them.
    + local     20 commits wasm and zig plug fixes, listed in that file
    head        9632bb870cd684efa89b497b563a19a39e939ae4

**So a chapter that will not compile at our head is expected, and the cause is
ours rather than the author's** until it is shown otherwise. PR 100 is the one
this fleet deliberately did not land: its `.expected` encodes x86's NaN and
overflow answers (`plugs-backlog` 2.07). Classifying each failure against that
list of 20, and routing the ones that are real to the wasm and zig plug lanes
as findings, is most of the value of this intake. Treating a red compile here
as a defect in the donated code would be the wrong reading, and fixing one in
place would destroy the evidence.

**Nine of those 20 are already proposed to us as Cobblestone PR 112.** The
author settled this on content rather than on commit ids, because the same nine
changes exist on two branches with two bases and neither sha set is
interchangeable: `wasm-slot-from-type` at 9632bb87 (which is what his arms
actually ran) against `wasm-plug-selfhost-batch` at ccfde8d7, which is PR 112.
`codex/plugs/wasm/WasmEmitter.codex` hashes `feb09250410c13d2` on both, as do
`check-emitted-runtime.ps1` and `wasm-e2e.ps1`. The emitter this port was
verified against is byte-for-byte the emitter PR 112 proposes, which is worth
knowing to whoever reviews that PR: it has seventeen programs and ten
differential probes standing behind it, against an oracle written by someone
who was not trying to make our plug look good.

## Correction to the register

`docs/PM/CurrentPlan.md` described this as "about thirteen local commits". The
count at the pin is 20 beyond PR 100, in two groups of 11 and 9, and the second
group is PR 112. Re-measured here from the source repository's own file
(L-COUNT).
