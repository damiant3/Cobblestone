# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

*Pruned 2026-08-15 and 2026-08-18 (red) and 2026-08-31 (root, 2,105 lines
to about 650), each at Damian's direction. How something was hunted lives
in the CL, the GitHubUpdate for its cycle, or the doc named beside the item
(`docs/Hardware/HardwareSitting.md` for flights, `ExaminersAssay.md` for
guards, the design or backlog that owns the subject). A tombstone or a war
story added here is scavenged again; write the pointer instead.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## THE SEM-EQUIV RELEASE BLOCKER IS FIXED AT MAIN 21187 (red, 2026-09-01 evening); two collectors are still owed

**Closed.** `sem-equiv` ran green under red's `-Internal` gate on CL 21162
(main 21187, seed FECCDD90): `LowerCtx.rename` is False on the TEXT path and
the `emit-hosted-start` prose is above its signature. The pin is root's to
lift. **What stays open, both on red's row:** (1) `$tSemantic` in
`build/build.ps1` fires on `Syntax/`, `Ast/`, the Codex emitter and
`opening.codex` and NOT on `IR/Lowering.codex`, though TEXT is emitted from
lowered IR; the `BuildScript.codex` generator owns it. (2)
`compare-codex-semantic.ps1` parses a definition whose prose sits BETWEEN
the signature line and the body as an EMPTY source body and reports a
mismatch that no layout change can clear (two reformats were spent on it
before `Parse-Stage0` was read); it should name an empty source body as
its own verdict. The account below is kept for the diagnosis; the
measured numbers in it are of that day.

`build/build.ps1` (the FULL gate, no `-Internal`) failed on CLEAN MAIN HEAD at
`sem-equiv`: `FAIL: semantic equivalence -- stage1 does not match source`,
**39 body mismatches**. Seed-affecting copy-ups were PINNED until this was
diagnosed (root, 2026-09-01).

**IT IS NOT ANY ONE AGENT'S PENDING WORK, AND THAT IS MEASURED.** Three full-gate
runs: with blu's shelved COMPILER-40 fix 5721 defs / 39 mismatches, an identical
re-run, and clean main head with that fix reverted 5720 defs / 39 mismatches.
**The mismatch SETS are IDENTICAL**; the fix adds exactly one def, which is the
whole of 5720 to 5721. It is also NOT the documented cross-workspace
`codex-vm` kill (`OperatorsManual.md` 1738): that failure is non-reproducible
by definition and this one reproduces deterministically.

**WHY NOBODY SAW IT, AND IT IS A STRUCTURAL GAP, NOT AN OVERSIGHT (L-NOGATE).**
`CLAUDE.md` sends every agent to `-Internal` and reserves the bare command for
release and public builds. `build/build.ps1` 81-88 states the trade in its own
words: the text leg is conditional, and **"the residual class is a chapter
outside the file set below written with a construct the printer mishandles;
that is caught by the next full gate rather than at the CL, and it is the trade
this switch makes."** The deferral is deliberate and reasonable. What is missing
is the collector: `-Internal` runs `text-stage1`/`sem-equiv` only when a
`\` file changes (`Syntax/`, `Ast/`, `Emit/CodexEmitter.codex`,
`Core/TextFormat.codex`, `Core/SourceText.codex`), and no full gate has run
between releases, so the debt accrued invisibly. **A deferral with no collector
is not a deferral, it is a hole.**

**PARTIAL CAUSE, CONFIRMED HOST-SIDE AT ZERO BOX COST.** The emitter renames
shadowed binders. In `skip-spaces` the source has `let __seq = ...` twice and
stage1 emits the second as `__seq_1`; `advance-id` is the same shape. That is
semantically identical and the emitted program is correct -- the comparator comes
to a normalized TOKEN join (`compare-codex-semantic.ps1` 339) and a renamed
binder is a different token. **The prior suspect for the rename is CL 20995**,
red's COMPILER-38, "lowering renames colliding binders so two live bindings never
share a name". **PROBE IT, DO NOT ASSUME IT.**

**CORRECTION, 2026-09-01 (blu): THIS ROW PUBLISHED "12 RENAMED BINDINGS CANNOT
ACCOUNT FOR 39 MISMATCHES" AND THAT WAS WRONG. THE FAULT WAS THE INSTRUMENT, NOT
THE TREE.** The 12 came from a pattern matching only BINDING forms, which cannot
see a PATTERN binder such as `is IrTry mx body fb fail t s ->`. Re-measured over
all identifier tokens: source **0**, stage1 **96** renamed tokens across **7**
distinct names, and the renames are not only `__seq` -- measured instances
include `ch` to `ch_1` (`fl-text-loop`), `fail` to `fail_1` (`simp-binds-any`),
and `max` to `max_1` (`lift-expr`, `aexpr-span`). 96 is entirely consistent with
38 mismatched bodies. **A too-narrow pattern produced a confident negative three
separate times in one session here; the discipline is to check a counting
instrument against a known-nonzero case BEFORE publishing what it counts.**

**DIAGNOSED BY red, 2026-09-01, and that supersedes the bisect this row asked
for: 38 of 39 are CL 20993** (LOWER renames shadowed binders, and TEXT is emitted
from the LOWERED IR, so the rename leaks into the emitted source), **and the
remaining 1 is `emit-hosted-start`**, where prose sits between the signature and
the body and the comparer reads the body as empty. blu's independent
instrumentation found the same two shapes, the rename and `emit-hosted-start`
alone presenting as source 1 token against stage1 410, which is corroboration
rather than a second finding. **A FOLLOW-UP CORRECTION, SAME DAY: THE TWO CL NUMBERS IN THIS ROW ARE ONE
CHANGE, NOT TWO.** blu recorded its suspect as **20995** and red named the
culprit as **20993**, and the line above wrongly read that as blu having been
wrong. `p4 describe 20995` is "copy-up: red -- COMPILER-38 ... lowering renames
colliding binders", and COMPILER-38's own row says "LANDED main 20995 (dev
20993)": **main 20995 IS the copy-up of dev 20993.** Same change, named by its
main revision on one side and its dev revision on the other. Do not go looking
for a second CL.Fixes are in red's CL 21162.
Line WRAPPING is excluded as a cause: the comparator joins tokens with single
spaces, so indentation and line breaks cannot produce a mismatch.

**THE CHEAP ARM, SO NOBODY PAYS 10 MINUTES A PROBE.** A full gate is not needed.
`sem-equiv` consumes the `stage1.codex` that `text-stage1` produces, so one
probe is: source-concat, a kernel, `Invoke-BuildText`, then
`build/compare-codex-semantic.ps1 -Source <concat> -Stage1 <stage1>`. Both
artifacts from the last run survive at `build/output/`, and comparing them costs
NOTHING.

**FIRST FIX, AND IT IS SMALLER THAN THE INVESTIGATION IT UNBLOCKS:
`compare-codex-semantic.ps1` DECLARES A `-Show` PARAMETER AND NEVER USES IT.**
`\` appears exactly once in the file, at its own `param()` declaration
(line 13). The one affordance that would name WHY a given body mismatches is a
dead knob, which is the whole reason this diagnosis is expensive and is why the
remaining causes are still unnamed. It is a generated script, so the generator
under `codex/build/` and the script land together (`docs/Designs/Active/Build/Build.md`).
Implement `-Show` before bisecting further.
## THE SEED INSTALL IS GUARDED BY PROSE AND A HUMAN NOTICING (L-BODY) -- blu, 2026-09-01

**A seed that did not contain its own fix reached main (21215) while the gate
was on screen refusing it.** `cdx-fixedpoint`'s two-pass branch prints that
`Sut.cdx` is the PRE-CONVERGENCE binary, that installing it ships a compiler
which does not reproduce itself, that this is P-STAGE2, and that `NewSeed.cdx`
must be installed and the gate re-run first. Then **the gate exits 0 and
nothing stops the copy.** The check is correct, the diagnosis is correct, the
wording is correct, and none of it is load-bearing: the guard is prose plus
attention, which is the definition of L-BODY.

**The hole is that there is no install STEP to instrument.** `PerforceProcess.md`
4.3b prescribes a hand-typed `Copy-Item -Force build/output/Sut.cdx
seed/Codex.cdx`. A bare copy cannot consult a verdict, so the only thing
standing between a two-pass gate and a bad seed is whether the operator read
the right screen. In the 21215 case the operator read a one-pass line out of a
DIFFERENT CL's log file, and a raw `Copy-Item` had no way to disagree.

**Design, and it is deliberately not "fail the gate".** The two-pass branch is
a legitimate outcome: the CL can be perfectly good and only the seed install is
premature, so exiting non-zero there would block real work and teach people to
route around it.

1. **The gate writes a verdict file every run**, `build/output/seed-verdict.txt`,
   naming the outcome, the artifact that IS the fixed point, and its whole-file
   hash: `one-pass Sut.cdx <sha>` or `two-pass NewSeed.cdx <sha>`.
2. **The install becomes a script**, `build/install-seed.ps1`, which refuses
   unless the verdict says one-pass, refuses if the verdict file is older than
   the artifact it names (a stale verdict is the same error one step over),
   verifies the named hash against the bytes it is about to copy, and only then
   installs, self-verifies, and refreshes the four `TechnicalDetails.md`
   digests that 4.3b already warns are easy to leave stale.
3. **4.3b prescribes the script instead of the raw copy**, and says why.

**The property that matters: the verdict comes from THIS run.** A grep of a log
by name cannot satisfy it, which is exactly the substitution that failed here.
A cheap corroborator worth keeping in the doc either way: a true one-pass costs
`cdx-fixedpoint` about 0.0 s, and any real duration means stage2 was built.

**Scope note so nobody widens it:** this changes no compiler source and no
check that already works. `Get-CdxContentHash` compared correctly and caught
the difference; an earlier claim of mine that the gate's fixed-point test was
vacuous was WRONG and is retracted at 21226. Do not "fix" the gate.

## THE FLEET DISPOSITION (Damian, 2026-08-31; root commanding)

red: Steve Howell's PRs and his published issues. blu: finishing the work
that was suspended for token budget (its register names it). val and reek:
the two campaigns below. **fester is PARKED (Damian, 2026-09-01): shut down
to free RAM on the one-DIMM box; the riscv work and Renode are out of the
loop** (its row in the lanes table has the shelf). Docs go straight to main;
a code arc gates once per arc, and the batch rules are
`CoordinationProtocol.md`.

**Gate RAM (Damian, 2026-09-01: "minimize that footprint"). The batch driver
and `-Jobs 8` landed at main 21043 (blu); the numbers and the mechanism are
`CoordinationProtocol.md`'s RAM section.** What remains is the rest of the
host side, from a census of every harness shell over 100 MB, sampled at 5 s
through three FULL gates (09:00-09:42; the bare `pwsh` baseline on this box
is ~110 MB, so subtract it): `check-generated-scripts.ps1` peak 292 MB,
`compile.ps1` 248, `concat-codex-self.ps1` 209, `deck-headroom.ps1` 187,
then `hosted-elf-test.ps1` 159, `bvt.ps1` 153, `build.ps1` 146,
`check-errors.ps1` 144, `sweep-app-classes.ps1` 134, everything else under
125. Each of the first four is a unit: name the holder from a stage census
the way the driver was (L-MECHANISM: 812 MB was a StringBuilder at the
write, not the capture everyone read first), and the acceptance is the same,
peak working set before and after on the same run with verdicts
byte-identical. Generated scripts change with their generator
(`docs/Designs/Active/Build/Build.md`). blu, AFTER COMPILER-32; the floor is
2.5 GiB now, so these are worth a unit each and not a stop.

**STATE 2026-09-01 (blu). ALL FOUR ARE CLOSED.** Unit 1 landed 21091,
unit 3 landed 21098, unit 2 and the temp leak landed 21119. Nothing in
this row is open; the next figures in the census list start at
`hosted-elf-test.ps1` 159 and nobody has drawn them.

| unit | was | is | how |
|---|---|---|---|
| `check-generated-scripts.ps1` | 292 | 178.5 | most of it was 21043; 8.8 MB from `ReadAllText`/`ReadAllLines` over `Get-Content` |
| `concat-codex-self.ps1` | 209 | 117.5 | 27.6 MB, streams the write instead of joining |
| `deck-headroom.ps1` | 187 | 141.9 | **CLOSED BY UNIT 3, no work of its own** |
| `compile.ps1` | 248 | 152.1 | 39.7 MB, streams the VM input instead of building it |

**ONE DEFECT ACCOUNTS FOR THREE OF THE FOUR, and it is worth recognising
by shape rather than rediscovering: THE WHOLE PAYLOAD HELD SEVERAL TIMES
AT THE WRITE.** The batch driver held it twice (21043), concat three times
(21098), and `compile.ps1` four -- a StringBuilder, its `ToString()`, a
`"$baseMode`n$bodyText"` interpolation, and the `WriteAllText` encode, with
the source line array still live. Each fix is the same one: write through a
`StreamWriter` so only the current line is in hand. Measured on
`compile.ps1` against the compiler's own 3,052,663-byte unit, the heap
charged to the write went 36.2 MB to 2.5, and the STAGE SPLIT is the part
that surprises: the StringBuilder is only 9.8 MB of it and `ToString` 5.9,
while the interpolation plus the encode are 20.5. The largest piece is
after the body is already assembled, so a reader who fixes only the builder
leaves most of it. **`compile.ps1` also multiplies**: `deck-headroom`
launches one per unit and the gate runs it at `-Jobs 8`, so ~40 MB each is
roughly 320 MB of host pressure at peak against a 2.5 GiB floor.

**The `%TEMP%` leak beside it (21119):** `check-generated-scripts.ps1`
created its `OutRoot` and never removed it, 1,058 trees holding 2.0 GB
accumulated since mid-August. It now removes only the tree it made, and
only on a clean exit: a caller's `-OutRoot` is left alone and a failing
exit keeps its evidence. Both directions were verified, and the stale
trees were deleted at Damian's instruction.

**`deck-headroom` was never a separate unit and the row could not see it:
it calls `concat-codex-self.ps1` IN-PROCESS at lines 203 and 232, so units 3
and 4 were partly the same code.** Measured by swapping the pre-fix concat
back in under `p4 edit`/`p4 revert`: deck-headroom 180.4-184.0 MB before,
141.3-141.9 after, three runs each, cached logs so no guest ran. What is
left of it above the floor is about 24 MB of its own work, which is not a
unit. Ask of any remaining row whether one shell HOSTS another: `&
script.ps1` does not fork, so those figures name a PROCESS and can double-
count (L-ADJECTIVE, a number standing in for a structure).

**The census is SOUND and this is the evidence, because a first reading of
mine said otherwise and was withdrawn.** Where the code has not moved, the
census and an isolated run agree: deck-headroom censused 187 and measures
180-184 pre-fix. Where they disagree, something landed in between --
`check-generated-scripts` HOSTS the batch driver in-process and fell 292 to
187 because 21043 landed at 09:42:08, at the end of the 09:00-09:42 census
window; `deck-headroom` spawns its compiles as separate processes, 21043
could not touch it, and it did not fall. Do not read a gap between a
censused figure and an isolated run as an instrument fault without asking
what landed between them.

**TWO CORRECTIONS TO THE PARAGRAPH ABOVE, both measured today.** The bare
`pwsh` baseline on this box is **~68 MB, not ~110** (three runs, with and
without profile, pwsh 7.6.5), so every figure in the census list is quoted
against a floor 42 MB too high and each unit's real cost is LARGER than
subtracting 110 suggests. And **peak working set is not allocation**: 200 MB
of byte arrays, verified at 202.2 MB of managed heap under a forced
collection, showed as 81.3 MB of working set, because working set counts
resident pages and is trimmable. Unit 3's holder was found on the HEAP and
is largely invisible in the metric this row's acceptance asks for. Keep
peak working set as the acceptance, since it is what the census measured,
but find the holder with `[System.GC]::GetTotalMemory($true)`.

**Queued (Damian, 2026-08-31), unowned, the next drawable plugs row:** text
plugs emit CCE encoding code a simple program never needs and the emitted
`opening` round-trips `to-cce (from-cce x)`; some emitters do not, and they
are the control. Census first, then one plug per CL: `plugs-backlog.md`
2.15.

### Campaign: the games reach the landing site (val)

The site says "34 classics play today ... playable now"
(`apps/landing/LandingPage.codex:269-271`, card `td4`) and links nothing.
The games exist (`apps/games/classic`, 34 engines with a hand-written HTML
shell each) but run only through `apps/games/server.ps1`, a local PowerShell
server bridging to `GameServer.cdx` on codex-vm. The public site is static,
so a game a visitor can play runs in the browser: the engine compiled to
wasm by `codex/plugs/wasm` the way `apps/fishtank/build-wasm.ps1` does it,
driving the existing shell in place of its `/api/` fetches. Stages, each
landed AND deployed before the next:

1. One game end to end: TicTacToe (smallest; minimax; battery-verified)
   compiled to wasm, its shell calling the module, served from
   `apps/landing/web/games/`, linked from `td4`. This stage settles the
   shape (one module per game, or one dispatcher module the way
   `GameServer.codex` dispatches by name) and the deploy recipe
   (`PublicPush.md` website-mirror steps; regenerate the source bundle
   FIRST, the stale-bundle trap cost a cycle on 2026-08-28).
2. The other 33, in `apps/games/games.json` order, and a games index page
   under `apps/landing/web/games/`.
3. Card `td3` ("aquarium, forty GPU demos, star map, all in your browser")
   is the same over-claim: `apps/fishtank/web`, `apps/gpushow/web` and
   `apps/starmap/web` are not in the landing bundle and nothing links them.
   Link what runs in a browser; correct the wording for what does not.

Rules. A card's tag reads "playable now" only while a visitor can play. A
game that fails on wasm is a PARITY finding for reek's campaign below: one
message naming the subject and the failing test, and no workaround in the
game. Chess (GAME-10) stays not built and its `games.json` row stays
honest; GAME-10's sentence about the landing page is stale and is val's to
fix when the file is first touched. Claims: `apps/games/**` and
`apps/landing/**` except `web/compile/**`, which is the Prism page and
stays fester's.

### Campaign: the wasm plug at parity with the hosted x86-64 lift (reek)

State at head, measured 2026-08-31 (NOT the lanes table's fester row, which
is six days stale): the wasm plug self-hosts byte-identically (the page's
CDX equals the seed kernel's at 3,064,678 bytes, plugs-backlog 2.10/2.11),
the wat2wasm nesting ceiling is cleared (2.03; native wabt on the Path, not
the npm shim), and 51 page modules ship with only arm64 at `ship = $false`.
**THE CONTROL IS NOT 60 OF 60 AND HAS NOT BEEN SINCE THE CORPUS GREW
(reek, 2026-09-01, seed D3A0C75A). Measured: linux 52 of 60, windows 51 of
60.** This row read "the hosted x86-64 target runs 60 of 60 against the
bare-metal `.expected` sidecars" and every wasm verdict in this campaign is
graded against that arm, so the claim is load-bearing and it was wrong
(L-CONTROL). The number is believed to predate the selection recursing into
`codex/test/apps/`, which is where all 17 failures live and which the old
top-level rule never reached (L-DENOM, L-COUNT) -- believed, not measured, so
do not restate that provenance as fact. **The wasm plug is at 52 of 60, which
is EQUAL to hosted linux and one AHEAD of hosted windows**, and the parity
this campaign set out to reach is reached on this corpus. Six of the eight
wasm reds are subjects that assert bare-metal machine facts (CR0/CR3, CPUID,
port in/out, MAP1 plus self-patching at 0x100000): both hosted x86-64 targets
SEGFAULT or take PRIVILEGED_INSTRUCTION on them, so they were never wasm
parity gaps and no wasm arm can honour them. Parity is measured, then closed:

1. The instrument first. Run the wasm plug over the SAME corpus
   `codex/plugs/elf/hosted-elf-test.ps1` selects (every `codex/test`
   subject with a `.expected` sidecar, minus its exclusion regex), with the
   same `-Calibrate` refusal arm. Every red is a parity gap and IS the
   campaign's list. Publish the score in `plugs-backlog.md` beside 2.11's.
   **DONE 2026-08-31 (reek): 54 of 60, and the six reds are FOUR defects.**
   `codex/plugs/wasm/hosted-wasm-test.ps1`; the corpus is not restated in it,
   `hosted-elf-test.ps1` answers `-ListSubjects` and stays the one definition.
   Calibrate refuses 60 of 60, and 54 of those reached wasmtime rather than
   dying upstream of the graded step, which is the half worth having.
   The four, with evidence, are `plugs-backlog.md` 2.14 (cite it by subject:
   that file has two 2.10s and two 2.11s): a shadowed `let` aliases one wasm
   local so the inner binding outlives its arm (`act-let-scope`, 41 for 23);
   a `Real` compare emitted as `i64.eq` (`approx-eq`); `real-approx-to-bits`
   has no arm and reaches the funcref path (`bacnet-encode`); and the runtime
   prelude's `$text_compare` collides with the Codex definition of the same
   name (three db subjects, one defect).
2. The gaps the census already names: `hosted-kind` hard-coded to 1
   (`WasmEmitter.codex:1009`); `process-get-pid` and `process-get-scope`
   stubbed; `__self-type-defs` empty, so the pmap self-test reads SKIPPED
   rather than passing; the `when-bool-cross`/`when-bool-pattern` grading
   queued in the plugs-backlog header; 1.66's unmeasured 4 MB stdin buffer.
   **The CONTROL first (root ruling, 2026-09-01):** the hosted x86-64 lift
   is this campaign's grading arm, and it is wrong in two shipped ways found
   from the parity control itself: `ops/real-mode-fields` exits 0xC0000005
   on the hosted arm while bare metal is green (inside `plugs-backlog.md`
   2.16), and constructor names mis-render (`Zebra 7` bare metal, `Z 7`
   hosted Windows, `Ze`+e forever hosted Linux; 2.17). Both containers wrong
   differently means the shared hosted codegen. A wrong control corrupts
   every parity verdict graded against it (L-CONTROL), so these are reek's
   and they come before more grading. Seed-affecting; batch, one gate.
   **First pass DONE 2026-08-31 (reek): 54 of 60 to 58 of 60.** The prelude
   helper is `$__text_compare`, the name the compiler already uses, so
   `apps/data/Row.codex`'s own `text-compare` stops colliding (3 subjects);
   and `~`/`~0` are the x86-64 ULP semantics via a `$__approx_eq` helper,
   4 and 0 ULPs, rather than the `i64.eq` that was being applied to f64
   operands. Note the corpus cannot tell ULP from exact equality: `approx-eq`
   only compares equal or far-apart values, so that green rests on the
   x86-64 emitter it was read off, not on the subject.
   **The remaining two reds are RED's, not this lane's** (agreed with red
   2026-08-31, to avoid fixing them twice): `act-let-scope`'s shadowed `let`
   is Steve Howell's issue 113, and `bacnet-encode`'s `Real` family is his
   PR 111. Both wait on ONE blocker, measured here from the other end and
   offered as that absorb's positive control: a real LITERAL emits f64
   (`WasmEmitter.codex:760`) while every local slot is declared i64, so
   `let y = 2.25` is refused outright, `type mismatch in local.set, expected
   [i64] but got [f64]`. Nothing in the 60 subjects binds a real to a `let`,
   which is why 58 pass over it. Arms for the `real-to-bits`/`to-real-approx`
   family were written, built, measured and BACKED OUT for this reason: none
   can be type-correct while the two representations disagree.
   `hosted-kind` is inert rather than fixed: it answers 1 (hosted Linux) and
   every consumer in the tree tests `/= 0` only, so the first consumer that
   distinguishes 1 from 2 inherits it. Whether wasm gets its own value is a
   compiler call; the declared range is 0..2.
   **ALL FOUR CLOSED 2026-08-31 (reek): 60 of 60, calibrate 60 of 60 with 59
   reaching wasmtime.** red released `act-let-scope` and the Real family to
   this lane rather than wait on Steve Howell's PR 111, and will rebase 111
   onto it. A local slot is now per BINDING rather than per name; and a real is
   its f64 BITS in an i64 slot everywhere, which is what every local
   declaration already assumed, so `let y = 2.25` assembles at all now. Two
   findings fell out that are worth more than the score. `IrNegate` emitted an
   INTEGER negation for a `Real`, the class fester fixed on the three native
   lanes at 18612; `codex/test/ops/real-approx-negate` is exactly that fixture
   and this plug's corpus cannot reach the directory it lives in. And
   **`__record-set` does not copy** -- it overwrites and returns the SAME
   record, so extending a context with it hands the callee's scope to every
   caller; the shadowing fix was written twice and emitted byte-identical WAT
   both times before that was found. red's review of PR 111 flags the same
   leak in its own guard-test, which 112 repairs, so 111 must not land alone.
   **60 of 60 WAS NOT THE CORPUS, and the harness no longer lets that be said.**
   **DONE 2026-09-01 (reek, 20893): plugs 2.16.** The glob recurses, `-Max 0`
   means the whole corpus (the default stays a cap of 60 so a bare run is not a
   sweep), and every score line names what it was drawn from
   (`60 selected of 996 eligible`). Eligible is **996**, not 383: re-measured,
   and the "44 more under `codex/test/ops`" this row used to carry is **40**
   (L-COUNT). The top-level 383 is byte-identical to the old rule, so the
   change is additive. New lesson `L-DENOM`.
   **First grading of `codex/test/ops`, with its control:** wasm 17 pass 23
   fail; x86-64 over the SAME 40, 39 pass 1 fail. So **22 are wasm parity gaps,
   not 23** -- `ops/real-mode-fields` is red on BOTH arms, x86-64 exiting
   0xC0000005, which is an access violation in the hosted x86-64 lift that
   nothing had graded and is UNOWNED. Of the 22, thirteen are builtins with no
   wasm arm (`$to_real_trapping`, `$real_to_int`, `$vec_load_at` and six more),
   one is a units literal, eight are wrong answers. The list is in 2.16.
   **`negate` on a Real and the real-mode family are CLOSED (reek, 2026-09-01).**
   The site was `wat-try-builtin`, NOT `IrNegate:834` as 2.16 first said; settled
   by marking the builtin arm and requiring the wat to move. Nine builtins had no
   wasm arm and each fell through that dispatch's final `else ""` to the funcref
   path, which is L-BAILVALUE. `ops/*` went 17 pass 23 fail to **23 pass 17
   fail**, six closed outright and six moved from refusal to a wrong answer
   (L-PARTIAL). **Those six are ONE missing primitive, not a mode problem:** a
   real prints as its raw f64 BIT PATTERN because nothing in this plug renders
   one, and every mode prints the same wrong thing. Scope and the 225-line
   oracle (`X86_64TextHelpers.codex:590`) are in 2.16. No regression:
   the depot revision rebuilt over the default 60 gives the same 44/16.
   **THE x86-64 CONTROL ITSELF HAS A SILENT DEFECT (reek, 2026-09-01; plugs
   2.17; REEK's, root's ruling superseding 20953).** The hosted lift mis-renders CONSTRUCTOR NAMES: the same
   12-line chapter answers `Zebra 7` on bare metal, `Z 7` at exit 0 on hosted
   Windows, and `Ze` then `e` forever (304 MB) on hosted Linux. Both containers
   wrong and wrong differently while bare metal is right, so it is the shared
   hosted CODEGEN path, not a wrapper. A `Real` field inside a constructor
   faults separately. This matters beyond one subject: the hosted arm is the
   CONTROL this campaign grades parity against, so for any subject whose
   `opening` returns a constructor, "x86-64 passes and wasm does not" was never
   a safe reading. Seed-affecting, so not taken here.
   **RE-GRADED 2026-09-01 AGAINST THE FIXED CONTROL (reek, seed 1CC3265D): the
   campaign's list is 14, not 22.** The x86-64 arm over `ops/*` is now **40 pass
   0 fail** and wasm is **26 pass 14 fail**, so for the first time every red is
   measured against a control that renders constructors correctly. The 14 are:
   three that now ASSEMBLE and trap at runtime for a missing builtin arm
   (`is_letter`, `__list_head`, `vec_load_at`, shape changed by red 20969), one
   units literal wat2wasm refuses (`unit-pattern-lit`, token `sin`), and ten
   wrong answers.
   **`ops/*` IS 40 PASS 0 FAIL (reek, 2026-09-01, seed D3A0C75A), from 26 pass
   14 fail; the default 60 is 52 pass 8 fail, from 46.** Closed: real-to-text,
   the three character-class predicates, the wrapping bounded-integer mode,
   `__list-head`/`__list-tail` as a VIEW rather than a copy, Text literal
   PATTERNS (which also freed all six `apps/browser-*`), the saturating and
   trapping MODES, which needed approximate arithmetic moved to f32 before a
   clamp could fire at all, the f32 ULP width for `~` and `~0`, and printing a
   CONSTRUCTOR from `opening`, and the SIMD family: a vector is a pointer to a
   16-byte box, which is one `v128`, and that is the language's documented
   allocation rather than a choice, and the MASK family with it. **THE SIMD
   FAMILY IS CLOSED: the `*vec*`,`*vector*`,`*mask*` slice is 25 pass 0 fail,
   from 11 pass 13.** A mask needed no representation of its own, being the
   same 16-byte box with all-ones lanes, so `vec-select` is `v128.bitselect`
   and the four queries are `i64x2.bitmask`; the width is forced because every
   mask builtin is declared over `VectorMask 2`. A vector COMPARISON was also
   answering a scalar compare on two pointers, and now does not. Also open
   beside the campaign and not in it: `lir-selector-smoke` and `apps/console-test`. Details
   and every measurement are `plugs-backlog.md`.
   **REAL-TO-TEXT IS CLOSED (reek, 2026-09-01): `ops/*` is 31 pass 9 fail.**
   `$f64_to_text` ports `__real_to_text`; `show` and the entry printer route a
   `RealTy` to it. It closed five of `ops/*` plus `neg-real-repro`, and the
   grouping it was taken from was wrong: `real-saturating` and
   `real-approx-modes` print only INTEGERS and are the saturating/trapping MODE
   clamps missing, and `real-mode-fields` returns a CONSTRUCTOR, which this plug
   cannot print at all. Those three, `real-approx-equality`'s f32 ULP compare,
   the three builtin arms, the units literal and `bounded-modes-smoke` are what
   is left. Measurements and the regression control are `plugs-backlog.md`,
   "Real-to-text".
   **996 is the DENOMINATOR, not a run target (Damian, 2026-09-01): we do not
   run it.** `-Subject` takes wildcards expanded against the eligible set, so a
   run names its slice (`-Subject 'ops/*'`, `-Subject '*negate*'`) and the score
   still reports `2 matching *negate* of 996 eligible` without being a sweep. A
   pattern matching nothing refuses rather than reporting an empty green. The
   campaign's standing slice is `ops/*`, 40 subjects.
3. The page side. **arm64 SHIPS (reek, 2026-09-01, dev 21171) and its
   `ship = $false` was NOT residue**: there was no `Arm64Elf` chapter in the
   tree, so flipping the flag alone would have shipped 271 KB the page cannot
   reach. `RiscVElf` is ported, the ELF mode line is wired, and board arm 12d
   grades it: `AArch64 ELF64 machine 183 entry 0x40000800`, wire control still
   a wire. The kernel is NOT booted; the arm grades the container.
   **`page-workspace-arm.js` arm 12b is GREEN, measured 2026-09-01 (reek):**
   `ELF kernel ELF32 entry 0x100020; usermode ELF64 entry 0x4000d0;
   overstated-section control refused`, with the whole suite at ALL ARMS OK.
   The RED this row carried is stale and `plugs-backlog.md` carries its
   symptom (`payload 84791 shorter than its own header claims 21703180`) as a
   record of what was fixed, not of what is open. What IS left here:
   `build-page.ps1` has no incremental
   path. **DONE 2026-09-01 (reek, dev 21194), and the 451 s floor was stale:
   the full build is 168.1 s** at seed FD18B0C8, of which the module emit and
   assemble is 145.2 s, 86 per cent. `-Incremental` (opt-in; the default stays
   FULL) skips that one phase when its inputs are unchanged and the build is
   **20.6 s**. The x86 truth anchor was gated first and the gate came OUT on
   measurement: it is 4.3 s, and caching it would have meant caching the
   anchor hash the page asserts byte-identity against. The deck-consumption
   question 2.10 left open; and
   want 3 of the `build-page-modules` row under "Registers carrying unowned
   work" (why `wat2wasm` never started on `riscv-stdio`; 2.03's addendum
   names the npm shim as the candidate and nobody has confirmed it is the
   same event).
4. Plugs 2.11, emit the binary wasm encoding and retire `wat2wasm`: Damian
   approved it 2026-08-30 for later. It is this campaign's tail, taken when
   1-3 are green unless he schedules it sooner.

Games arriving from val's campaign join list 1 as subjects. Claims:
`codex/plugs/wasm/**` moves to reek for the campaign; fester keeps
`apps/landing/web/compile/**` and the Prism page, and reek announces before
touching `build-page.ps1` or `page-lenses.ps1`. Seed-affecting only if a
compiler chapter moves; the plug alone takes no token.

## MAIN IS OPEN

Latest public release: **Update 53** (red, 2026-08-28), github `58b08c38`
(master) and gitlab the same commit, from main 20765, seed
`B066CEB5FE8FC9E8`. `GitHubUpdate53.md` is the report; `GitHubUpdate54.md`
is rotated and carries the open items. Earlier releases (51, 50 and before)
are recorded in `docs/PM/Active/GitHubUpdates/`, not here. The head seed
has moved since the release (`2B69CDD246E7EE23` at main 20824). Main is
open: seed-affecting copy-ups are unblocked.

## The brand boundary (Damian, 2026-08-29)

The public name is **the Cobblestone Project**: the OS and every brand
surface outside the compiler is Cobblestone; the language, the compiler
and the artifacts stay Codex. The rename campaign is done and nothing in it
is open; the ruled boundary and the traps around brand strings are in
`docs/Designs/Done/Marketing/Cobblestone.md`.

## The network demo pair (Damian, 2026-08-24)

Two items, deliberately decoupled.

1. **A webserver app in the guios** (val; blu consults on the net side). It
   serves HTTP and the browser app's own `codex://` wire
   (`apps/browser/PageFetcher.codex` / `DataChannel.codex` are the client
   side). The server is `codex/os/net/WebServer.codex` (`cites Net chapter
   WebServer`), NOT the same-named `apps/works/WebServer.codex`, which is a
   socketless router called only by its own test. **RULED (Damian,
   2026-08-28): the desktop never gains `Network.*`; the webserver becomes
   the first system SERVICE under a preemptive scheduler and the pane is its
   admin console.** Design and stage register:
   `docs/Designs/Active/OS/PreemptiveScheduler.md` (val). WORKS-48's pane
   half (window slot, request-log ring in `ds` 244, state word in 248,
   start/stop) is unblocked; the serving half is stage 5 of that design.
   Bed first via codex-vm NAT port-forward; metal rides a future sitting.
   Originates in the works app: `apps/works/works-backlog.md` is the
   register, this row is the pointer.
2. **The compiler running in WASM, building itself, in a static webpage**
   (fester): SHIPPED and witnessed (plugs 1.83, main 19774;
   `codex/plugs/wasm/page/index.html` plus `build-page.ps1`; the anchor is
   computed at page build from the served bytes, never hard-coded). The
   account of how it got there is plugs 1.60 through 1.94. **Hosting that
   page from our own kernel and OS is EXPLICITLY a separate later step
   (Damian: it requires environment he does not have yet); do not couple
   item 1 to it.**

## Track A -- the stick is an OS

**Sittings are coordinated by red (Damian, 2026-08-18) and grouped, not
serial.** Every metal question rides ONE diagnostic boot per sitting: a lane
routes its question to red with its arm and expected readings, red composes
the boot (bank before you risk, L-BANK; rehearse the exact bytes,
L-REHEARSE), and Damian sits once. **Agents do not propose flights or
sittings (Damian's standing ruling).** Standing metal questions: the sink's
2.7 MB write (WORKS-9), the e1000 ring successor (NIC-4), the TCP
conversation (B3), ASDE (finding 4), NIC-5 last, and the GOP row below if
red does not close it from sitting 6.

- **The diagnostic stick (red, approved 2026-08-18): one image that detects
  the box and says what needs to happen.** Design and stages:
  `docs/Designs/Active/OS/DiagnosticStick.md`. Steps 1, 3 and 4 are landed
  (root) and the stick flies; **step 2 lifts are per lane** (each lane lifts
  the probe it flew into a stage, coordinated with root), **step 5 is the
  grouped sitting.** Flight cards and every banked reading:
  `HardwareSitting.md`. Trap: a stage that can wedge the box runs AFTER the
  bank, never before it (L-BANK; sink executes last for this reason).
- **The I219 medium-death hunt is PARKED (Damian, 2026-08-24, after sitting
  12).** The production path (boot, bring up once, talk TCP) is proven on
  metal; the death has only been seen inside the ladder's own mid-session
  re-reset, which production never does, and it moved between same-shape
  ladders with no mechanism named. **Revive only on production evidence:**
  the resume point is the sitting cards (10, 11, 12 and card 19188 in
  `HardwareSitting.md`) and `docs/Designs/Active/OS/I219IsNotAnE1000.md`;
  the next arm is blu's to compose. No lane draws from it while parked.
- **WORKS-9 (reek). The USB mass-storage driver's second write, and the
  sink's own 2.7 MB write on metal.** Metal-gated; the arm and account are
  in `apps/works/works-backlog.md`. What is open is why sink REFUSES on the
  board: the bed reproduces the bank loss (a `-usb-bot-drop` keyed into
  sink's DATA phase) but not the cause, metal refusing at `rty=1` where the
  bed reaches `rty=2`, so a board reading is what is wanted (L-ARENA). Any
  rebuild of `sinkladder.img` needs a fresh full-mission run (L-REHEARSE).
- **A8 the desk build loop (fester).** Plan, roads and traps:
  `docs/Designs/Active/OS/DeskBuildLoop.md`. The allocation is GRANTED on
  the ASUS at `-AllocPages 131072` (`HardwareSitting.md` "A8"), `compile
  <path>` is wired and gated to the compare against `CODEX.CDX`
  (`codex/test/apps/gcon-compile-read`, `gcon-cdx-verdict`). **What waits
  for metal is the launch alone**, `vm-compile-cdx` and below, because
  codex-vm is itself a hypervisor and its guest sees no VT-x. The image is
  NOT flight-ready for anything else (no `-Identity`, no source).
- **Native GOP resolution (red).** Bed half done (`build/gop-mode-arm.ps1`;
  `ExaminersAssay.md` "The GOP Mode Arms"); the `SetMode` half in
  `codex/build/cdxtopeScript.codex` is red's too. Sitting 6 answered
  `gopmode honoured` at 1920x1080 with 10 modes on the ASUS
  (`HardwareSitting.md` "FLOWN 2026-08-21"); red closes the row or names
  what the metal half still lacks.
- **Identity, RULED 2026-08-18 (queue 11, 12): the identity file stays on
  the ESP; auto-unlock is bed-only.** Rotation (`RotationFact`) stays with
  `Designs/Done/OS/Identity.md`; nothing else is open.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE": the standing questions ride ONE diagnostic boot per sitting,
in an argued order (bank before you risk, L-BANK). Every flight's card and
archive row is there, not here. NIC-1, NIC-2 and NIC-3 are answered on
metal; NIC-4's ring half is answered (`rdh-writable=y`, sitting 6).

Open, in the order the ladder flies them:

- **The i219 acquire-loop fix** (blu): unblocked by sitting 12, which
  eliminated both candidate register writes as the medium's cause
  (`HardwareSitting.md`, sitting 12).
- **NIC-4's successor question**: whether a frame ARRIVES during
  `nicring`'s own window. `pre=3` says the part receives before the stage
  looks; the during-window GPRC read has not survived a flight, so "nothing
  arrived" and "arrived and was invisible" both stand. The discriminator
  can say NO (`-e1000-rdh-ro`, the `nic-rdhro` arm) and rides B3's boot.
  Details and the caveat are on the NIC-4 card in `HardwareSitting.md`.
- **From NIC-3**: `aneg-done` is never set on this part while `STATUS.LU`
  comes up, so `phy-bring-up` returns 0 against a link that is up.
- **B3, a real TCP conversation with a real peer**: `DiagB3.codex`, ladder
  stage 13, has answered on metal (sitting 11: thirteen bytes echoed back
  unchanged over the real I219). What the composer owes each sitting: the
  peer named in `DIAG.CFG` must ECHO (`build/boot/echo-peer.ps1 -Port 7`),
  because the conversation is raw TCP and not the repository wire. The
  next sitting is the gate for what b3 still cannot say, not for whether
  it works. **Every diag image built before main 18665 carries a BLIND b3
  (`sent=` was the intended length, not the sent one), and `45239937` is
  one of them: it does not fly.**
- **Finding 4 (ASDE)**: `DiagAsde.codex`, ladder stage 14, risk writes,
  flies last after b3; built and bed-verified both ways, awaits a sitting.
- **NIC-5: what wedged the box on 2026-08-11.** Not `CTRL.RST` (discarded
  on this part). Terminal by construction, flies last.
- **B4 step 6**, the repository protocol served on the part, is B3's
  flight. Steps 1-5 are done in the bed; the wire is `DevelopersRulebook.md`
  "The repository wire".

Rulings that bind this track:

- **The NETIO ceiling (Damian, 2026-08-21): cut the drain, the NIC comes
  first.** *"tcp correctness is a working nic, not adherence to a standard
  I can't use because the nic is broke."* Campaign rule 2 wins: no stage may
  end the run. Both halves landed (`net-io-drain-ticks = 96`,
  `codex/test/net-drain-budget` refuses a crossing of the give-up ladder;
  the unchecked x86 send surface has no production caller). The one live
  unchecked send path is arm64's (`Arm64NetIO.codex`), registered under the
  deferred OracleCloudArm64 project.
- **ICMP is send-only** (rulings queue 1): we do not answer a ping and
  `icmp-parse` stays latent. `Tftp`, `Syslog` and `Icmp` have no production
  caller; `syslog-decode-bytes` builds its body with the quadratic `acc &`
  accumulator (CostModel 3.6), and whoever gives `syslog-parse` a production
  caller fixes that in the same change.

## Track C -- the trust audit (val)

C1 and C2 are landed and enforced (`IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`). The rechecker fork is CALLED (red,
2026-08-20, rulings queue 3; L-CAPABILITY-LOST) and is not open. **C2.5
stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce (RULED 2026-08-15, CLOSED 2026-08-16)

The census, the ranked queue (10.1, take order in its last paragraph) and
how a row can be wrong (10.3) are `VerifiedFormatParsing.md` section 10;
the guard pattern is settled in `ExaminersAssay.md` (clamp where a length
decides a slice, refuse where it decides WHERE a read lands, the ablated
call IN the arm). Still open in 10.1, unowned unless named:

- 8b, `VirtioBlk`'s device-written used-ring index: waits for a bed.
- 18, `OtaBoot boot-load` (reek): LATENT, no production caller.
- The latent corpus rows 6, 7, 11 and 13.

## The Prism dev environment (Damian, 2026-08-28; multi-lane)

Prism is a full local HTML/WASM dev environment on the STATIC page: source
tree on disk, multi-file editor, worker compile, cite resolution in-tab,
kernel builds through the in-memory pe/img chain, webpages through the html
lens, user-mode `.exe` and Linux executables as a hosted-runtime backend,
board kernels for the native plugs, and an optional Claude REPL/agent panel
behind a provider interface. Design and stage register:
`apps/prism/design/Active/PrismDevEnvironment.md`; register row:
`apps/prism/prism-backlog.md` PRISM-7. Stages 0, 1, 2 (compiler half AND
page wiring with the toolbox tree), 4 to test-limit, 5a, the native-build
configs, boards and bench have landed; the deployed page was refreshed at
main 20818.

**Two traps that bind every page deploy.** Rebuild from a seed at or after
`7B6A4950` (20783: before it RESOLVE emitted its frame in CCE, unreadable to
the page); head is `2B69CDD246E7EE23` at main 20824. And regenerate the
source bundle `build/output/Codex.codex` FIRST: a stale bundle made two
correct compilers read as one defective one (L-SAMEVER) and cost a deploy
cycle on 2026-08-28.

Open, in order. val and reek are on the disposition campaigns at the top of
this file; the items below that named them are queued behind those.

1. **Stage 2c, the in-tab signer** (red; design updated 20708).
2. **Stage 3, templates and the build tab** (unowned; was val's, queued
   behind the games campaign). Stage 2's cite resolution, which it needed,
   is on main (20796).
3. **Stage 4, the Claude panel: what is left is a hand at a browser with a
   key, not more code.** 4a and 4b are on main (20580, 20604) and the arm is
   green, but the stage's acceptance ("a chat round trip streams") needs a
   real key and a real billed call, which no lane can supply from a test.
   The request shape is pinned in the design at 20578 and must not be
   written from memory (`budget_tokens` is a 400 on `claude-opus-5`, a
   refusal arrives as an HTTP 200).
4. **PreemptiveScheduler stages 1+2** per the corrected design
   (`PreemptiveScheduler.md`, 20727: the kernel ALREADY has preemptive SMP
   processes, uncalled; the webserver becomes a spawned process and the
   desk never gains Network). val's, queued behind the games campaign.
5. **Stage 5c, sockets**: untouched, unowned.
6. **The guios webserver app** (WORKS-48, `apps/works/works-backlog.md`;
   stage 3's named server template): val's, queued behind the games
   campaign.

Damian's alone: the Anthropic API key for stage 4's live round trip, and
the one-command public `compile/` page refresh.

Rulings that bind the campaign (Damian, 2026-08-28): the stage-5a Linux
verification bed is ALL of the options (the narrow WSL exception for
verification arms only, R-SHELL amended in `CLAUDE.md`; a QEMU Linux guest
bed beside it; 5b's Windows `.exe` verified natively), *"we are supporting
all these options for the people"*; **boards** means IoT board build
targets, a Prism project per HAL board chapter with per-board output beside
the kernel chain; **bench** means our codegen benchmarks run and compared
against any configured output build chain, consuming the native-build
configs. The zig work (Steve's PRs) rides LAST behind the campaign: *"is
not the broadest brush right now, we are trying to paint bigger"*. The
design's foot lists the rulings it still asks of Damian.

## The lanes -- RULED by Damian 2026-08-15, re-pointed 2026-08-18

**root commands the fleet (Damian, 2026-08-28): assignments and status
reports route to root.** "THE FLEET DISPOSITION" at the top of this file
(2026-08-31) is the current assignment and wins over this table where they
disagree. The table is the assignment, not a suggestion; re-read it on every
merge-down. An item here is a pointer; the register named beside it holds
the detail.

Standing rulings that shape the rows: the compiler-bug order is whatever
`codex/compiler/compiler-backlog.md` shows open, and only that file knows it
(this section carried a stale order twice; read the backlog, not a
sentence). fester is otherwise held in reserve for the hardest problems
(Damian, 2026-08-26). **The DeskScheduler is PARKED (Damian, 2026-08-26),
not cancelled**: `docs/Designs/Active/OS/DeskScheduler.md` is the proposal,
its measurement arm is on main (the topbar counter: about 20,000 desk-loop
iterations a second idle, 60 with a 3D pane focused), the choppiness that
prompted it is not reproduced in the bed, and it carries two questions only
Damian can rule on (rate or budget; skip or run late on a miss).

| agent | now | then | standing |
|---|---|---|---|
| **blu** | **NOW (2026-08-31): finishing the work suspended for token budget: the COMPILER bugs, `codex/compiler/compiler-backlog.md` in the register's order; 34 and 24 landed 2026-08-31/09-01, COMPILER-15 in hand, BATCHED with the next items and gated once, alone on the box (CoordinationProtocol, "The token does not cover RAM")** | CostModel: `fixed` is unshipped and blocked on the registry (81 of 265 `bs-alloc` rows read `unknown`, re-measured 2026-08-25; `CostModel.md` 5.1 has the closed decisions); B3 stage 13 and B4 step 6 fly at the grouped sitting (Track B carries the flying caveats: the desk peer is an ECHO listener, the discriminator counts the writeback half only, `match=y` is valid only while identity-mapped) | `codex/os/kernel/E1000e.codex`, `codex/os/net/**`; WORKS-16 |
| **val** | **NOW (2026-08-31): the games reach the landing site**, the campaign in "THE FLEET DISPOSITION" near the top of this file. Everything this row used to carry has landed; the accounts are `ShellRefinement.md` (stages 6 through 9) and `apps/works/works-backlog.md` | PreemptiveScheduler stages 1+2 per the corrected design (`PreemptiveScheduler.md`, 20727); task-frame stages 3 and 4; the hover preview | `ShellRefinement.md` "WHAT IS STILL OPEN IN 6.4" is the only list; WORKS-47/41/44/46/40 in the works register |
| **fester** | **PARKED (Damian, 2026-09-01): the lane is shut down to free RAM on the one-DIMM box, and Renode leaves the loop.** CL 20867 (plugs 1.3, the riscv frameless temp collision: fix, test, and the attribution that all 77 riscv reds are pre-existing while ablation moves on the fix) is SHELVED on `//Codex/fester`, not landed; the riscv rows it held (1.3 family, 1.38, riscv-729) wait with it. The non-riscv items are unowned until a lane runs dry: the FAT32 long-names row below and the memory-contract runner (text-mode hwm, standing rules). Prism page claims (`web/compile/**`, `build-page.ps1`, `page-lenses.ps1`) are released; reek's campaign may touch them without announce | A8 the desk build loop when VT-x metal is available (Track A) | `apps/landing/web/compile/**`, `build-page.ps1`, `page-lenses.ps1` (RELEASED to reek without announce, this row's NOW column); `deck-headroom`; WORKS-24 rides a sitting; WORKS-17's syntax half is a `Theme` decision; ProductBuilder stage 6 is ON HOLD pending customer approval (`codex/product/product-backlog.md` 6) |
| **reek** | **NOW (2026-08-31): the wasm plug at parity with the hosted x86-64 lift**, the campaign in "THE FLEET DISPOSITION" near the top of this file | the plugs close-out lane, `codex/plugs/plugs-backlog.md` in the register's order, one entry at a time | WORKS-9 is metal-gated, routed to red's sitting; `ShellDslReadability.md` stays reek's |
| **red (HANDED OFF 2026-09-01; the lane is REEK's, Damian: take red's work, closed to un-serialize the fleet on one DIMM)** | **THE COMPILER MEMORY CAMPAIGN (Damian's direct assignment, 2026-09-01, outranks the handoff above). Stage 1 LANDED main 21187 (red 21185, seed FECCDD90): per-definition reclamation in CHECK, SCOPE, PARSE and LEX, self-compile host peak 1,147 to 537 MB measured with the SUT as kernel; the account is `ArchitectsSketchbook.md` "Per-definition reclamation". It also closed the sem-equiv release blocker above. NEXT, measure before choosing: the CHECK-RESOLVE tail (126 MB of sort scratch), LOWER's 200 MB deck, DESUGAR's 49 MB. Two collectors owed from the blocker, both red's: `$tSemantic` should fire on `IR/Lowering.codex` (the `BuildScript.codex` generator, with the scoping fix below), and `compare-codex-semantic.ps1` should name an empty source body instead of reporting a mismatch (its generator `comparecodexsemanticScript.codex`). Then NOW (2026-09-01): COMPILER-36, the trapping integer default on x86-64 (root's ruling and red's sizing are on the row; measure the wrap-by-design foreword sweep FIRST, in scratch, before any seed moves).** Steve Howell's queue is absorbed: every open PR (99-114) is on main with credit and a comment, flagged for the next GitHubUpdate; COMPILER-35 (PR 114), the PR 108 + PR 101 batch, PRs 111+112 (plugs 2.18), issue 104, and COMPILER-38 (issue 113) landed 2026-09-01, the last at main 20995 with seed DE664C4E. Still red's from that queue: COMPILER-36 (issue 109); the wasm shadow-stack deletion COMPILER-38 makes dead is DONE (reek, main 21009: same-seed before-baseline 27 pass 14 fail unchanged, act-let-scope answering arm-local 23, zero `_sh` slots in the emitted wat); the `-Internal` scoping fix in the `BuildScript.codex` generator (`CoordinationProtocol.md`, "The batch gate must SEE the batch"). Not red's: COMPILER-37 (issue 106, unowned), issue 102 (COMPILER-32, blu), issue 110 and PR 112's `wasm-exports` (ruled to reek) | Prism stage 2c, the in-tab signer (design 20708); sittings, and the diag step-2 lifts red owes (xHCI truth, keyboard, MSC align, largest GOP mode + `SetMode`); the Review pane stages 3+ (`works-backlog.md` WORKS-44, WORKS-46); identity stage 4 (trust-root write, passphrase change, `IDENTITY.DAT` on the ESP); COMPILER-23 re-presentation when Damian calls; `BatteryReorg.md` step 6 | releases, personally and end to end; `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; the 4.3 seed hash check runs BEFORE build-complete, not after the report |
| **root** | **NOW (2026-08-31): commander**: the register, the dispatches, the pulse | DiagnosticStick composition (`DiagnosticStick.md`; step-2 lifts by the lane that flew the probe); `ComplianceEvidence.md` (FactStore ingestion pending); `HardwareAbstractionLayer.md` open question 5, hardware crypto dispatch: unit 1 is built (`VirtioRng`, main 18963), steps 2 and 3 are BLOCKED on a board crypto manual `docs/Reference` does not hold (red's ruling, 2026-08-21); OracleCloudArm64 DEFERRED | `build/boot/diag/**`; plugs 1.34 is rulings queue 10; the HAL carries its full designed surface |

**Plugs are reek's close-out lane** (from val, Damian's direction
2026-08-18): the register in order, one entry at a time, said in
status.json. Entries other lanes hold are named in the register (1.33 blu,
1.38 and 1.3 fester, 1.36 and 1.32 reek, 1.34 root). `codex/plugs/zig/**`
is ORDINARY FLEET CODE, edited like any other plug (Damian, 2026-08-18);
credit Steve in a CL that changes what he wrote and flag it in the next
GitHubUpdate, which is courtesy and not a gate.

## Approved campaigns and the pool (Damian, 2026-08-18)

Damian approved every open design campaign in `docs/Designs/Active/` as
available work; a lane that empties draws from the pool, in order, and says
so in the table above. Where the pool and the table disagree, the table
wins. **Strike an item from the pool when you draw it**; four entries here
were once live work on four lanes because nobody did. Assigned in the
table: CostModel 3.4+ (blu), the diagnostic stick and BatteryReorg step 6
(red); ProtocolStack + OTA (reek) and PlugDeepRecursion (val) queue behind
the disposition campaigns.

Taken and NOT available: `HardwareAbstractionLayer.md` (root; hardware
crypto dispatch is its open question 5, see root's row), `GameEngine.md`
phase 2 (val), `ShellDslReadability.md` (reek, with a file claim),
`ComplianceEvidence.md` (root). `EdgeMeshGameServers.md` phase 2 and
`ThreatModel.md` are DONE (the latter in `docs/Designs/Done/IoT/`).

**The pool holds NO drawable item.** The `DeviceEmulationCatalog.md` queue
is demand-driven: red's sittings produce the next entry rather than a lane
picking one up, and `tools/codex-vm.c` carries a file claim, so announce.

Seed-affecting campaigns take the token per CL as usual.

## The battery choreography (Damian, 2026-08-22; red coordinates)

DONE 2026-08-22: all three items landed (19081 batch parser, 19089
`codex-vm -run-list`, 19086 size-dealt batches) and the quiet-box
re-measure read 123 s wall against ~10.5 min. Ruling (red): the two-phase
shape stays; an in-guest test runner (REPL or mini-kernel) is NOT taken.
Numbers and the bed facts are in `ExaminersAssay.md` "Batch Compile
Architecture" and `OperatorsManual.md` "Batch mode: `-run-list`".

## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads.

- **`docs/Designs/Active/OS/OracleCloudArm64.md`: DEFERRED by Damian
  2026-08-18.** Deferred with it: `codex/os/net/Arm64NetIO.codex` is a full
  twin of the x86 send path and carries NEITHER the checked-send fix NOR the
  NETIO drain cut (still `arm64-net-io-max-ticks` 500). Whoever lifts the
  deferral inherits both as the first item; an arm64 TCP send that hangs or
  truncates before then is this row, not a new defect.
- **Six `-Internal` phases have their runner under `build/` and do not
  trigger on it** (`jonquil`, `plug-binary`, `cross-smoke`, `plug-smoke`,
  `app-sweep`, `sem-equiv`; reek, 2026-08-25; PARKED, not claimed).
  Blanket-widening `$tBuild` would make `-Internal` the full gate for any
  build change, so it is a question, not a build (L-LESS). Interim rule:
  whoever edits one of those six harnesses runs its phase by hand and says
  so. Table with costs: `docs/Designs/Active/Build/Build.md`.
- **Four harnesses are dead on every box: `extract-x86-output`,
  `test-disk-compile`, `sim-test`, `gdb-watchpoint`** (reek, 2026-08-24;
  UNOWNED). codex-vm never parsed `-data-port`/`-ctrl-port`; the
  refuse-unknown halves are DONE (codex-vm exits 2 naming the flag; the
  compiler refuses an unknown mode). Reviving them is not a flag parse but
  an interactive serial loop, and `extract-x86-output` is unrevivable at any
  price (its `ELF` mode does not exist). The one live question is whether
  the four are wanted at all; delete is a real answer. Not push-blocking.
  `tools/codex-vm.c` is a claimed file (claims table); announce.
- **COMPILER-23 (compiler-backlog): three CCE print/encode defects, open
  and UNOWNED.** `char-to-text` truncates multi-byte CCE codes to the low
  byte; `text-to-unicode-bytes` answers 0 when its input is the topmost
  allocation; the tier-2 encode collapses u=8364 onto u=192. Damian's
  repair-2 ruling rested on a mechanism since retracted and red is
  re-presenting. Whoever takes it decides whether the uncalled second
  printer `__cce_print` (`X86_64Helpers.codex`) is the replacement or dead
  weight (L-UNCALLED). The two UEFI print loops fixed under COMPILER-21 stay
  ungated: nothing in `codex/test` runs under `-uefi`.
- **A battery batch can hand every test another test's output: CONTAINED
  (red, 2026-08-28).** A dropped-bytes report or a short stream now
  invalidates the whole batch, and `Get-FailHint` names misattribution.
  Still open and UNOWNED: WHICH layer loses the bytes; `_batches/*.err` is
  the instrument that answers it on the next occurrence. Record:
  `ExaminersAssay.md` "The batch stream can lose bytes".
- **`deck-headroom.ps1 -Plugs` crashes on an empty corpus, and the gate
  dance's `p4 sync -f` can empty it** (red, 2026-08-28; UNOWNED). A forced
  sync rewrites every source mtime, every bundle reads stale, the corpus
  measures 0 units and the script dies on `.Count`. Two halves: an empty
  corpus is a named verdict, not a crash; and decide what bundle staleness
  means across a forced sync. Until bundles are rebuilt after the 08-27
  plug-wide stdio change, any lane's `-Plugs` run measures ~10 of 55 units.
- **VM admission (`Get-VmAdmittedSlots`, `vm-config.ps1`): wired at the
  fan-outs of `deck-headroom` and the battery's two phases** (fester,
  2026-08-28). The budget is 1100 MB per guest against 1024 MB host reserve
  (`-mem` is a ceiling, not footprint; L-REQUEST); re-measure on a
  different box (L-COUNT). Left open, UNOWNED: `test-cross-batch`'s run
  phase dies at 466 subjects while a filtered slice runs clean (reek,
  2026-08-28); a Renode instance and a session-launched guest are VM loads
  the check cannot see (`OperatorsManual.md` "A Renode instance is a VM
  load"); the battery half is unproven end to end until Damian runs one.
  A run launched as a background shell dies with a fleet session restart
  and reads as a load-independent kill; launch detached
  (`OperatorsManual.md`).
- **codex-vm SMP teardown defect** (red, 2026-08-22, from the battery
  re-measures; UNOWNED, goes to whoever holds `tools/codex-vm.c`):
  `smp-halt`'s child faulted on the HOST (0xC0000005) at teardown after
  complete output, and `smp-affinity` hit the 60 s wall with complete
  output on the wire; each is green standalone. A host crash after complete
  output still PASSES, as it did under `test-run.ps1`.
- **FAT32 has no long names.** VFAT long names landed for FAT16 (fester,
  2026-08-28, `codex/test/img-longname`); `Fat32.codex` and
  `Fat32Writer.codex` still have none, and it does not come for free.
  fester's standing item.

## Decisions

**Numbers are stable ids, not an order.** A ruled item shrinks to one line
here and its reasoning moves to the doc that owns the work; the number stays
so the citations across `GitHubUpdate*`, `CostModel.md`, `plugs-backlog.md`
and the designs keep resolving. Gaps are expected. A ruled item whose work
has landed is deleted; the CL and the owning doc are the record.

**What belongs in PENDING, and it is a narrow test (Damian, 2026-08-20):**
only a decision he alone can make. An outside relationship, an account, a
spend, a product direction. A technical trade-off with a defensible answer
is the commander's call, not his.

### Pending -- only Damian can answer

6. **Free-vs-solved wire marker: OPEN, WITH STEVE HOWELL (Damian,
   2026-08-28), and it BLOCKS NOTHING.** Damian's read is ADD the marker
   with Steve's zero-sized-default rider; the final call rides Steve's
   answer. No lane implements until it comes back. When ruled it is
   seed-affecting, token per CL, priced at one lane-day (the 20327 sweep);
   scope and evidence: COMPILER-32/33, the ir-fidelity census, issue 94.

**On hold, customer-gated (Damian, 2026-08-20):** 16, ProductBuilder stage 6
(the protected merge, deploy and rollback half) needs a protected-side host,
an outside organisation or a stand-in we build. ON HOLD pending customer
approval; not drawable by any lane until that arrives.

**Deferred by Damian, not pending:** 6, OCI account access for
`OracleCloudArm64.md` phases 5b-5d (the whole design is deferred).

**Not a question until there is a design partner:** secure-element support
in `Identity` (`ThreatModel.md`'s fourth open question).

### Ruled, work in flight (one line each; reversible in one line)

- **COMPILER-30 ErrorTy: SPLIT NOW** (Damian, 2026-08-27). `ErrorTy` is
  reserved for genuine type failures; a distinct no-expectation marker takes
  the sentinel meaning. Every plug inherits; Steve's PR rebases. Open work,
  unowned; row in `codex/compiler/compiler-backlog.md`.
- **Heavy-pane stranding: option D, FIX THE ALLOCATOR** (Damian,
  2026-08-27). val's campaign; acceptance is the reopen-after-buried-close
  row (+2,465,912 today) falling toward zero, and the frontier table in
  `ShellRefinement.md` 6.4 carries it.
- **Prism's product scope** (Damian, 2026-08-24): compile/transpile on the
  fly; the pre-baked IR path goes. `apps/prism/prism-backlog.md` is the
  register.
- **COMPILER-18: "give it the word"** (Damian, 2026-08-24). The
  partial-application closure gains a remaining-arity word; seed-affecting;
  blu. Row and measurements in `codex/compiler/compiler-backlog.md`.
- **plugs 1.34, the ARM64 MMIO boundary: (a)**, gate the MMIO window in the
  effect system; (b), a real EL0 boundary, is a different project.
  Seed-affecting, token. Until it lands the ARM64 capability gate covers the
  `block-*` builtins only. (red, routed from root.)
- **plugs 1.57: the Rulebook's over-application rule binds every plug that
  keeps an arity map** (red, 2026-08-24). The java half stands as ruled;
  the riscv wiring named in the row is INERT and the real miscompile site is
  unnamed, so reek hunts it from the reproducer rather than re-wiring.
  Account: `plugs-backlog.md` 1.57.
- **A ping goes unanswered, deliberately** (red, 2026-08-20): no production
  caller for `icmp-parse` until something needs one. (Track B, blu.)
- **The rechecker keeps deriving type-variable instantiation itself** (red,
  2026-08-20); the compiler does not emit it. That is the fork's whole value
  (L-CAPABILITY-LOST); the abstentions are the price. (Track C, val.)
- **`check-vm-differential` retries once, only when an arm produced NO
  BINARY**; "hosts disagree" is never retried. (red; ruled, NOT BUILT as of
  2026-08-31: the script has no retry.)
- **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`,
  `.cross-refusal`, `.no-cross`, `.vmargs`) and warns on everything else.
  (red; ruled, NOT BUILT as of 2026-08-31: `Show-Untracked` still only
  warns.)
- **The sem-equiv trigger is widened to `opening.codex`** (fester,
  2026-08-28); residue: other compiler chapters still cannot trigger
  sem-equiv under `-Internal`, so the next instance one chapter over is
  found by a full gate.
- **5** (2026-08-16): zig 0.16.0, at `D:\zig-0.16.0`.

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex` | fester (kernel-side) |
| `codex/plugs/arm64/Arm64Runtime.codex` | root; the block/servicer sections are fester's by agreement |
| `codex/os/kernel/{VirtioNet,VirtioBlk}.codex`, `codex/plugs/pe/Arm64PeWriter.codex`, `build/build-arm64-img.ps1` and its generator | FREE -- announce |
| `tools/codex-vm.c` | reek, 2026-08-24, for the dead-harness row (red's grant); the row is the shape, not this line. Announce to blu before touching the NAT paths |
| `build/test-cross-batch.ps1` | FREE -- announce |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, the lifted probes) | root, 2026-08-18, `DiagnosticStick.md`. Step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex`, `GopComposite.codex`, `GopFiles.codex`, `GopIcon.codex`, `GopSettings.codex`, `codex/foreword/ui/**` | val, 2026-08-20, the Shell Refinement campaign (`ShellRefinement.md`). Announce-before-you-start stands, and so does checking which `ds` cells are spoken for. `comp-text` stays fester's |
| `apps/works/GopEdit.codex` | FREE -- announce; the Editor's standing rules are `works-desk-contract.md` 0.6 |
| `apps/works/RepoProtocol.codex`, `RepoProtocolPersist.codex` | FREE -- announce |
| `apps/works/AgentBundle.codex`, `codex/test/apps/agent-bundle-*` | FREE -- announce |
| `apps/works/GopReview.codex` | FREE -- announce; `GopFacts.codex` is red's |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `apps/works/GopWeb.codex` and `ds` cells 244 and 248 | val, 2026-08-28, WORKS-48. 244 is a pointer to the request-log ring, 248 the state word, both written in `desk-run` before the base mark; the `WebMux` lives in `DeskApps`; `codex/os/net/WebServer.codex` is read, not changed; 252 stays free |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/os/sched/**` and the preemptive scheduler work | val, 2026-08-28, `PreemptiveScheduler.md`. blu keeps `codex/os/net/**`; the scheduler reads that side and does not change it |
| `codex/test/cost/**` and `CostModel.md` | blu; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | reek, the close-out lane (from val, 2026-08-18). Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table). **`codex/plugs/wasm/**` is reek's for the parity campaign (2026-08-31, Damian); fester keeps `apps/landing/web/compile/**`; `build-page.ps1` and `page-lenses.ps1` are RELEASED to reek without announce (fester's row, 2026-09-01, on that lane being parked)** |
| `apps/games/**`, `apps/landing/**` except `web/compile/**` | val, 2026-08-31, the games campaign (Damian; the disposition section). `web/compile/**` is the Prism page and stays fester's |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | reek, with the plugs lane |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | blu, 2026-08-18 |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators | reek, 2026-08-16, by Damian's direction. Catalog and order: `ShellDslReadability.md` |
| `codex/foreword/compress/**` and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 item 18) | reek, 2026-08-16, red's routing. Seed-reachability is measured per file, not assumed from the row |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce, and it takes the token (seed-affecting) |

A claim nobody honours is worse than no claim. Announce before you go into
a claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

**THE COMPILER'S MEMORY CONTRACT (Damian, 2026-08-28): the FULL
SELF-COMPILE must complete within a 2 GB heap high-water mark, in BOTH text
and CDX modes.** A contract on the compiler, not a scheduling policy: a
self-compile needing more is a DEFECT to fix, never a reason to grow the
guests. Measured 2026-08-28, the CDX-mode self-compile peaks ~1.09 GB at
head, flat across the release interval. The gate's self-compile phases gain
the <= 2 GB assertion as the runner (fester, with the admission work); the
text-mode hwm is still to be read from the gate's fast path, since a
standalone `-Text` compile measures the serial channel rather than the
compiler.

**BATCH YOUR GATES (Damian, 2026-08-28).** Small CLs land on your DEV stream
with targeted tests only; `-Internal` runs once per work ARC or window, never
per one-line CL, and the batch copies up grouped (P-COPY1). Seed lands are
unchanged: token and gate at the land. Docs and registers need no gate. Until
the VM admission check lands, gate windows are coordinated through the
commander so heavyweight builds do not stack on the box.

Battery runs are Damian's (release proofs excepted, per the release skill).
Goldens stay parked during active GUI work. No new platform-wide register.
Prose about our own code is deleted in files you touch. The em-dash stays
banned. `-Jobs 4` on every parallel harness (Damian 2026-08-27, RAM-bounded;
ExaminersAssay "The parallelism default"). Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (a wire emitter wants `-raw`, everything else
the plain name; `DevelopersGuide.md` "Effects and Act Blocks").

**Do not add a test to the gate or the battery on your own initiative; get
red's clearance first.** Damian, 2026-08-21: *"haphazardly adding tests to
the gates slows everyone down."* The cost is every agent's gate run for the
rest of the project. The rule is about the GATE and the BATTERY, not a ban
on arms: a `build/boot/diag-arm.ps1` row is pre-flight rehearsal and costs a
gate run nothing.

**A finding about someone else's project is not ours to publish.**
`//Codex/main` mirrors to public GitHub and GitLab, so anything landed there
is published; a bug report or critique about an external project is that
author's to receive first and goes in the depot at no path while it is
unpublished. Nor does a note saying one was withheld. In a design, state the
target's behaviour as a fact about the machine we build on ("TMOD is the
truncating remainder, measured"), not as a defect in somebody's document.
Perforce CL descriptions are not mirrored; only files are pushed.

### Declined, and therefore not available work

Damian has ruled these out; they are here so the ruling is reachable by
whoever is about to spend a session on one.

- **Line-level debug info.** A statement about what Codex is for, not a
  scheduling call.
- **An app compile gate.** Compiler work must not be coupled to app drift.
- **The ARM64/RISC-V LIR retarget.** What landed stays; the rest is not
  reopening.
- **Plug arms for targets whose runtime is not on this box** (Damian,
  2026-08-25: no toolchains installed to close them). `char-encode` has arms
  in the five plugs that run here (python, javascript, zig, csharp, wasm);
  of the ten without, only ada and fortran can build a `Char` and would
  newly lose a site, both recorded in `build/plug-builtin-baseline.txt`.
- **The store cutover** waits on infrastructure and is not available work.

Declined is not deferred. Do not re-propose one of these, do not build a
smaller version of it, and do not open a design that assumes it. If you
think a ruling has been overtaken by events, that is one sentence to Damian,
once.
