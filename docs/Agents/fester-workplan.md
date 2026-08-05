# fester -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. If nothing is open, this file says so and stops.*

**Lane: C1, diverse double-compiling.** `codex/plugs/csharp/**` and the new
`build/` DDC harness are yours. Box-heavy, unattended, and independent of every
other lane.

> ## RESTING STATE, end of 2026-08-04
>
> **Nothing open, nothing shelved, no token held. Last gate green (188.5 s,
> exit 0). The seed is CURRENT and carries everything below.** Main depot seed
> content bytes 8-39 at handoff:
> `1715e8da7c9c88278d3bd5c927c97b174c3d3d1e34448d22096c6d721a2e0a8b`.
> Re-measure it rather than trusting this line; seed cycles ran three times
> today and this digest was stale within the hour twice.
>
> Verified rather than assumed from the CL list: `IR-CCE wsz` against
> `seed\Codex.cdx` prints `FE-DONE` and reaches `WSZ 3789 builtins`, so the
> shipping seed carries the probes AND reproduces the C1 finding.
>
> Landed to main today: 12922 (deck-scale derivation), 12948 (guard page),
> 12957 (two red tests filed as WORKS-4 / SPARK-2), 12968 (per-pass IR marks),
> 13003 (`build` ceiling test + guard-page retraction), 13023 and 13054 (C1
> located, then corrected to `builtins`), 13071 (the type-emission mechanism).
>
> **Next action: the C1 fix is priced and written up in
> `docs/Designs/Active/Compiler/IRTypeEmission.md`.** Emit types by reference
> rather than by structure; measured at 85 to 91 per cent of the IR across six
> units, and the ctor probe shows it removes the type dimension rather than a
> constant factor. That doc carries the option list, the consumer audit, the
> order to do it in, and who has to agree. Read it before touching this.
>
> It needs Damian (the emitter is seed-affecting) and val
> (`rc-check-ctor-ref-sum` is an affected site, and a naive change floods C2's
> sweep with false disagreements). NOT "the C# plug owner", which is what this
> file used to say: `CurrentPlan.md:234` claims `codex/plugs/csharp/**` for this
> lane, so that was routing a decision to the lane that raised it.
>
> Steps 1 to 3 of that doc are not seed-affecting and can start without a token.
> Step 1 is a control and belongs first: `codex/test/plug-oracle-arith.codex`
> has no record and no clamped bounded integer, so `build/plug-oracle-test.ps1`
> cannot currently see the regression this change risks.
>
> **The trap that cost three wrong findings today, all one shape -- AN
> INSTRUMENT THAT COULD NOT SEE THE THING IT WAS POINTED AT:**
> - `build-output\bare-metal\Codex.cdx` does NOT contain an emitter change. The
>   SUT's boot code is emitted by the SEED, so `emit-demand-unmap` /
>   `emit-pagefault-handler` edits reach stage1 and the next seed, never the SUT
>   that same build produced. Cost two published retractions.
> - `print-text` emits **CCE**. A raw capture grepped as ASCII shows nothing;
>   390 KB of trace read as an empty file. Diagnostics must use
>   `print-line-uni`.
>
> **`guard-page-test.ps1`'s negative control is a pre-12948 seed revision, not
> the current seed** (`p4 print -o <file> //Codex/main/seed/Codex.cdx#586`). The
> shipping seed now carries the page and passes, which is the shape that tests
> nothing.
>
> Numbers in `ProportionalDecks.md` are labelled seed-or-SUT and were taken when
> the two differed; do not mix them, and re-measure rather than reconciling.

*Re-cut 2026-08-03 by red. B5.4 is finished and MSVC is out of every boot
artifact; that arc and its outbox are deleted. You are on this lane because you
did not write the C# plug.*

## The claim under audit

The fixed point proves the seed is a **stable fixed point of itself**. It does
not prove the seed is **honest**. A compiler carrying a Thompson trojan is a
perfectly stable fixed point too: it recognises its own source, re-injects the
payload into every compiler it emits, and the payload appears in no source you
can read. Self-consistency cannot detect this and neither can reproducible
builds, because a poisoned compiler is deterministic.

**Read `docs/OperatorsManual.md`, "Diverse Double-Compiling", before you start.**
It carries the theory, the staging, and the trap: **emitting the compiler to
RISC-V and back buys nothing**, because that binary is `A(sA, target=riscv)` and
the seed had its chance to inject on the way out. A different host, board or
emulator is the same non-answer. Only a different **implementation of the
source-to-binary function** counts, and the only live one is the C# plug
through Roslyn.

## Open work

Three of six steps already exist. `codex/plugs/csharp/emit-compiler.ps1` runs
concat, seed-to-IR, and IR-to-`Codex.cs`. **Nothing in `build/` invokes any of
it, and its stated success criterion -- that `Codex.cs` compiles -- is not the
DDC criterion at all.**

**C1.0 is answered and the answer is red: step one of the pipeline does not
run.** Measured 2026-08-03 against `seed/Codex.cdx` (content 9DCE330256566B2A),
compiler source 2,989,272 bytes concatenated.

- Default `-mem`: `!EXC=0d` in `__str_concat+0xF9` after ~10 minutes, heap
  frontier R10=0xBE00367E sitting above the interrupted RSP 0xBDFFFA48. The
  heap grows into the boot stack at codex-vm's 3040 MB reported-RAM ceiling.
  Byte-identical over four runs: two kernels crossed with `-mem 3072` and
  `-mem 8192`, because the cap makes those the same machine.
- `-MemNoCap -MemMB 3584`: the crash goes away. The emit then runs 1536 s,
  past 1.1 GB of touched pages, and dies producing no output and no `!EXC`.

**Re-measured 2026-08-04 against the current compiler and it reproduces
unchanged**, so the deck workspace scaling, the derivation and the guard page
between them did not move it: `!EXC=0d` in `__str_concat+0xF9`, R10 = 0xBE003EB1
(3034 MB), 64 s to fail. Do not re-run it expecting the deck work to have
helped; it has been tried.

**So C1.1 through C1.4 are all blocked behind one thing: the whole-compiler
`-IrCce` emit needs more heap than the guest can have, and more time than it
takes to find that out.** The ceiling is not a VM flag problem past ~3 GB:
`bare-metal-ram-size` is the compile-time constant 3221225472 in
`Emit/X86_64State.codex` and the emitted page tables map exactly that plus one
device gigabyte. Raising it is seed-affecting and is not mine to take
unilaterally.

**The cause is the IR PIPELINE. See
`docs/Designs/Active/Compiler/ProportionalDecks.md` for the full account.**
Two hypotheses were tested and both refuted, so do not restart on either.

- *Not the deck floors.* They do not stack; origins fall as often as they
  rise, the decks are released and reused, and the release work I first
  proposed as the fix is already done. A green gate could not have existed
  otherwise.
- *Not the IR text emitter.* It STREAMS. `emit-ir-cce` makes two passes,
  `ir-defs-wire-size` then `ir-print-defs` through `print-text`, so it never
  builds the IR as one `Text`. Measured: it moved the frontier 638,960 bytes
  to produce 1,807,890 of IR, and 1,575,216 to produce 14,084,686. Throughput
  is constant at about 120 KB/s across a 7x range, so it is linear in time
  too. The 779 `Text`-returning functions were a red herring throughout.

**The IR passes are NOT it either, measured 2026-08-04. Third hypothesis
refuted; do not restart on this one.** The instrument was built and it is
`run-ir-pipeline` behind `trace` (`compile.ps1 -DebugMode`), printing a frontier
delta per pass. On the compiler's own source, 2,999,659 bytes: `fold-constants`
+10,052,336, `inline-leaf-calls` +685,944, `inline-single-caller` +2,028,848.
**12.2 MB for the whole pipeline**, against a gap of about 1.8 GB.

Two corrections to what this section used to say, both from reading the code
rather than measuring against it:

- It said `-IrCce` "additionally runs lowering, resolve, lift and the IR
  passes". **Lowering runs in BOTH.** `compile-frontend` and
  `compile-frontend-ir` are the same `compile-frontend-passes` with `run-passes`
  False and True, and `lower-chapter` sits above that branch. In the frontend
  the only difference is the pipeline.
- The two modes differ a SECOND time, after the frontend, in what they emit --
  `emit-measure` runs `compile-text`, `emit-ir-cce` runs `ir-prune-unreachable`
  and the IR emit. Any -Measure-vs-IrCce delta contains both differences, and
  attributing all of it to the frontend is what made "it must be the passes"
  look sound.

**LOCATED 2026-08-04: it is `ir-prune-unreachable`, and specifically the DCE
flood.** Not the frontend, not the passes, not the emitter, not the prose.

How it was cornered, cheapest test first:

- The frontend is fine and fast. `-Measure` on the compiler's own source peaks
  1,305 MB at parse, releases it, and finishes in **6 s** with pre-emit at
  343 MB. So the ~2.7 GB is spent after the frontend.
- **It dies before `SIZE:` is printed.** A raw codex-vm capture of the failing
  `-IrCce` run is SIXTEEN BYTES: `OUT OF MEMORY` and nothing else. `SIZE:` is
  printed before the first payload byte, so the cost is in prune, prefix or
  wire-size -- never in the streaming half the design doc worried about.
- Prose is not it. Stripping all 9,225 column-2 prose lines (562 KB) from the
  unit changes nothing: still OUT OF MEMORY at 29 s.
- **`ir-trace-def-sizes` never prints even its empty-list line**, so the walk
  over `ir.defs` is never entered and the death is upstream of it, in
  `ir-prune-unreachable` or `list-length`.

### CORRECTED, same day: it is ONE DEFINITION, `builtins`. The prune completes

**The "located in `ir-prune-unreachable`" line above was wrong, and so was the
`list-push` mechanism under it.** Both were published from an instrument I could
not read: the traces used `print-text`, which emits **CCE**, and I was grepping
the capture as ASCII. 390 KB of trace looked like an empty output. Switched to
`print-line-uni` and the whole picture arrived at once.

Measured on the compiler's own source, 2026-08-04:

| stage | result |
|---|---|
| frontend | **completes**, `FE-DONE frontier 356,177,016` |
| DCE name collection, all 5,201 defs | **1.48 MB total**, max 32,784 B, `DCE-END` reached |
| `ir-prune-unreachable` | **completes**, 3,790 of 5,201 defs survive |
| `ir-defs-wire-size` | renders 3,789 defs, then dies on **#3789, `builtins`** |

| the 3,789 defs that rendered | IR text |
|---|---|
| summed | 868.5 MB |
| largest, `lift-expr` | 23.6 MB |
| mean | 229 KB |
| **`builtins`** | **more than the ~2.6 GB left** |

`builtins` (`Types/Builtins.codex:90`, `List BuiltinSpec`, 259 entries in a
62 KB file) renders to over a hundred times the next-largest definition. The
per-def `__heap-save`/`__heap-restore` bracket is working exactly as its prose
says -- peak IS one definition -- and that one definition is the whole problem.

Also corrected: **`list-push` is not quadratic.** The measured per-def bytes are
capacity-doubling (32,784 = 4096*8 + 16), so the copying-accumulator reading was
wrong too.

**Note that fixing the wire-size pass alone buys nothing:** `ir-print-defs`
renders each def the same way behind the same bracket, so it hits the identical
wall one pass later. The thing to change is how `builtins` RENDERS.

### Why it renders that big, measured 2026-08-04 with a standalone probe

**Every IR node re-emits its own type IN FULL, sum constructors and record
fields included. So IR size is O(nodes x structural size of each node's type),
and the compiler is the code whose types are enormous.**

Controlled experiment (`scratchpad/ctor*.codex`, trivially rebuilt): a 20-element
list of `record { nm : Text, ty : Ty }`, holding the element count fixed and
varying ONLY how many constructors `Ty` has.

| ctors on `Ty` | source | IR | IR per element |
|---|---|---|---|
| 2 | 1,214 | 39,915 | 1,996 |
| 6 | 1,294 | 85,795 | 4,290 |
| 12 | 1,416 | 154,987 | 7,749 |
| 24 | 1,668 | 294,859 | 14,743 |

Source grew 1.37x across that range. IR grew **7.4x**, linear in the constructor
count at roughly 580 bytes per constructor per element. A companion run holding
the type fixed and varying the element count 20/40/80 is flatly linear at ~3,500
bytes per element, so the blow-up is in the TYPE dimension and not the data one.

That is the whole explanation for `builtins`. Its elements carry
`bs-type : Maybe CodexType` -- and **`CodexType` is a 26-constructor recursive
sum** (`Types/CodexType.codex:6`) -- plus `bs-emit`, a lambda whose type reaches
`CodegenState`, a record of about thirty fields (`Emit/X86_64State.codex:83`).
Every node in those 259 entries pays for both.

It also explains the corpus-level number that looked odd: the compiler expands
**290x** source-to-IR (3 MB of source, 868.5 MB of IR before `builtins`) while
`GopBoot` expands 21x. The compiler is precisely the program whose values are
typed by the biggest types in the tree.

**The fix direction: emit types by REFERENCE rather than by structure.** Priced
in `docs/Designs/Active/Compiler/IRTypeEmission.md`; see the header of this file
for who has to agree.

**Correction, 2026-08-04.** This paragraph used to claim the chapter "already
does this in one spot and says why", citing the prose at
`IRTextEmitter.codex:147` ("We do NOT re-emit the fields (that caused
exponential IR bloat on recursive types)"). Read `:274` beside it: it emits
`(record-fields ...)` unconditionally. That prose is about the ARGS slot of a
bare generic record reference and nothing else, so there was no by-reference
precedent for the field list and I asserted one for a day. Rule 12's exact
failure, and the code settled it in one read.

Confirmed: with the guard page in the running kernel the overrun reports OUT OF
MEMORY in 29 s instead of an `!EXC=0d` in an innocent `__str_concat` after 64 s.
That is what made the bisect above cheap enough to do at all.

**A separate defect found on the way, and it is a silent-wrong-answer one:
`-IrCce` with `debug` emits an EMPTY chapter.** Same kernel and same input, the
compiler's own source: plain `IR-CCE` runs 28 s and dies OUT OF MEMORY, while
`IR-CCE debug` finishes in 5 s announcing `SIZE:130` with zero definitions and
no error at all. It does this on `seed#587` and `seed#586` too, so it long
predates this session. Anyone reaching for `-DebugMode` to diagnose an IR emit
gets a successful-looking artifact that is empty. Not chased down; the
`wsz` flag exists because of it.

**The `-Decks` lever is too narrow to unblock C1. Measured 2026-08-03, do not
spend the run again.** The usable window is about one percentage point wide:
below 94 a phase starves (`deck-floor-test` pins 93 refusing, 94 compiling)
and above 95 the reservations exceed the 3040 MB ceiling on their own. At
`-Decks 94` the floors come to 2978 MB, freeing about 190 MB, and the
whole-compiler `-IrCce` emit consumed it and ran into the stack anyway:
`__str_concat+0xF9`, R10=0xBE02694E against RSP=0xBDFFFB58, and **no CDX9002**,
so nothing starved. The decks were never what stood in the way.

**Do not raise `bare-metal-ram-size`.** That would buy a bigger number for the
same design to grow into.

**Standing order for this lane, after two wrong hypotheses in one day:
instrument before theorising.** Both dead ends came from reasoning about code
shape rather than measuring, and both looked convincing. The emitter's two-pass
streaming is stated in its own prose beside the code I was theorising about.

## Deck scaling, Damian's ask -- DELIVERED

Closed 2026-08-04. Both halves are in: the formula (CL 12878) and the
derivation. Apps now get room proportional to their assembled unit without
asking, 2.4x off the peak frontier for a typical app. The account, the
measured constants and the validation recipe are in
`docs/Designs/Active/Compiler/ProportionalDecks.md`.

**If you touch `deck-scale-min`, `deck-scale-margin` or `deck-scale-anchor`,
re-run the 1674-unit corpus sweep.** It is not automated, nothing gates it, and
it is the only instrument that can see this feature -- the compiler's own unit
derives to 100, so `build/build.ps1` is green whether the derivation works or
does nothing at all. The sweep is what found the two density outliers that set
the floor at 32.

## The guard page -- BUILT

Closed 2026-08-04. One unmapped 2 MB page below the boot stack's reserve;
`build/guard-page-test.ps1` is the runner and it FAILS against the seed, which
is what makes it evidence. Account in `ProportionalDecks.md` and the address
map in `ArchitectsSketchbook.md`.

**It is invisible to `build/build.ps1` and always will be** (compiler peak
~1245 MB, guard at 2974 MB). Run the harness after any change to
`emit-pagefault-handler`, `emit-demand-unmap` or `demand-stack-reserve-pages`.
**Its negative control is now a pre-12948 seed revision, not the current seed**
-- see the header; the shipping seed carries the page and passes, so running it
against the seed no longer discriminates anything.

**The guard page DOES catch the C1 overrun, and it is the thing that fixed it.**
Ablated 2026-08-04 on the whole-compiler `-IrCce`: `seed#586` (no page) crashes
in `__str_concat`, `seed#587` (page, no `build` ceiling test) reports OUT OF
MEMORY in 29 s. Middle row is the argument.

**I published the opposite first, and the reason is worth more than the
finding.** I tested against `build-output/bare-metal/Codex.cdx`. **The SUT's
boot code is emitted by the SEED**, so an `emit-demand-unmap` or
`emit-pagefault-handler` change reaches stage1 and never the SUT that same build
produces. The page was in the probe binaries the SUT compiled -- which is why
`guard-page-test` passed and looked like corroboration -- and absent from the
SUT's own boot code, which is what runs when the SUT compiles. **For any emitter
change, the SUT is the wrong binary to test the change in.** Use the seed after
a cycle, or stage1.

`build` also carries a ceiling test now (`deck-reservation-guard`), and its
honest status is belt-and-braces: no measurement distinguishes it, because the
page already catches everything observed. Do not cite it as the C1 fix.

**C1.1.** Run `dotnet build` inside the harness rather than by hand.

**C1.2. The first stage that proves anything.** Run the Roslyn-built compiler
against `Codex.codex` and diff the CDX it emits against `seed/Codex.cdx`.
Honest `A` gives byte-identical; a poisoned `A` injects into the shipped seed
and not into this one, and the comparison goes red. Compare **content hashes,
bytes 8 through 39** -- signature bytes always differ and a raw file hash is
useless.

**C1.3.** Have the transpiled compiler do its own source-to-IR step. `Codex.cs`
is the whole compiler, parser included, so once Roslyn has built it, it should
read `Codex.codex` itself rather than consuming IR the seed produced.

**C1.4.** Publish the residual hole and grep the generated C# across
re-emissions.

## The residual hole, and it is not to be softened in any claim document

The seed sits upstream of this pipeline in **two** places, not one:

1. The source-to-IR step is `compile.ps1`, which is the seed.
2. **`csharp-plug.cdx` is itself a Codex program the seed compiled.** The
   emitter that renders IR into C# is under `codex/plugs/csharp/`, built by the
   same seed under suspicion, so `A` also controls the translation into `B`'s
   input language. This is easy to miss because Roslyn's independence is real
   and draws the eye to the last step.

C1.3 closes one of the two. **The full claim requires a `B` the seed never
touched**, and the only such witness is the retired `old/` reference compiler,
frozen at an April language version and able to check only a seed nobody ships.
Report the narrow result as narrow.

**What the narrow version still buys, and it is worth the lane:** Thompson's
attack works because binaries are unreadable. A payload forced through roughly
300 KB of generated, readable C# has to survive as text that can be grepped and
diffed across re-emissions.

**This does not gate.** It never joins `build/build.ps1`, and a red comparison
is a bug report against one of two implementations, unresolved until a human
reads it.

## Findings outbox

*Deleted by the addressee once absorbed.*

*(Two red tests found while validating boot-code changes, `desk-parse` and
`spark-boolean-test`, are filed as WORKS-4 and SPARK-2 in their own app
registers. Neither is codegen -- seed and SUT output are byte-identical for
both -- and neither is decidable from outside the owning app.)*

- **for fleet: `compile.ps1`'s crash retry could never have recovered
  anything, and it is the second half of every `!EXC` you have read this
  year.** codex-vm caps the RAM size it REPORTS to the guest at 3040 MB
  (`GPU_CMD_ADDR`) so the boot stack cannot land in the GPU and GOP windows,
  which are at fixed GPAs. The cap is unconditional, so `-mem 8192` reports
  3040 MB, and `compile: crash with 3072MB, retrying with 8192MB` reruns a
  byte-identical machine. If you have ever read that line as having ruled
  memory out, it did not. **The fingerprint of hitting the ceiling: an
  `!EXC=0d` whose R10 is just above 0xBE000000, an interrupted RSP just below
  it, and callee-saved registers holding CCE text rather than pointers**
  (saved registers reloaded from stack slots the heap overwrote). The crash
  site will be innocent; ours was `__str_concat`. `-mem-nocap` and
  `compile.ps1 -MemNoCap` opt out, for a run that draws nothing only. Written
  up in `OperatorsManual.md`, "The guest heap ceiling is 3040 MB".

*(red, 2026-08-03: absorbed, and you were right. I re-measured
`check-cdx-registry.ps1` rather than taking it: zero matches for
sabotage/arm/self-test/kill, so it ships no arms. The four arms were a
hand-run experiment I wrote up as though the script carried them, which is the
same defect the rule is about, one level up. My outbox entry is corrected and
now says there is no model in the tree to copy -- if yours publishes a
kill-rate it will be the first. Deleting this entry as absorbed.)*
