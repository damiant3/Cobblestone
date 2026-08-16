# The deck-short miscompile

*Opened 2026-08-05 by val. Live bughunt. This file is the record: the
repro, what is measured, what is theory, what is ruled out, and the plan.
A theory that dies is struck through and KEPT, because the ruling-out is
half the evidence.*

**Status: SOLVED TO THE ROOT. See FINDING 7.** The CHECK deck overruns
its reservation by 9.3 MB at scale 32 and writes into the bivy holding
the recorded types. Four silent tolerances in series turned that into
wrong bytes with a clean compile. The overrun now refuses (CDX9002), a
corrupt type cannot leave the copy as a pointer, and the append dispatch
refuses rather than defaulting. CL 13303, landed main 13483, gate green,
seed rebuilt and self-verifying.

**Reviewed by red 2026-08-06 (Damian's assignment via val): the fix is
CORRECT and the miscompile is closed.** Both proof arms re-measured
independently against the depot seed `B05BBA6B00C494B3`: `-Decks 32`
refuses with CDX9002 and emits no binary; the derived scale compiles
clean. Memory and time verdict: no risk (the guard is one comparison;
the dispatch's `deep-resolve` short-circuits allocation-free when the
type has no TypeVars, the common case after CHECK). The lesson is
L-TAILGUARD in LESSONS.md. **Open residue, none of it the miscompile:**

- **Step 4 is DONE, 2026-08-06, and the floor of 64 is confirmed by
  output equality:** 1369 entry points compiled at their derived scale
  and at `-Decks 150` against depot seed `BCD3BB89`, coverage asserted
  COMPLETE, **0 byte-divergences among the 1298 that compiled at both**.
  70 failed at both scales for reasons that are not the deck (no log in
  the run mentions CDX9002). The single unit that diverged by outcome is
  `foreword-all-compile`, which carries `decks=200` in its own `.flags`
  sidecar and is the only file in the tree that overrides the scale --
  the sweep rediscovered the one genuine case unaided, and that case was
  already handled. **The instrument was validated before it was
  believed** (L-FALSIF, which is the whole reason this step exists): four
  pre-registered arms on one file with only the scale varying -- 32 vs
  150 must diverge and did, 150 vs 150 must match and did (compilation is
  deterministic, so byte-equality is a valid oracle at all), derived vs
  150 must match and did, and a sabotage that truncates one output by a
  byte must report DIFFER and did. Without that fourth arm nothing here
  would have exercised the branch a finding has to travel through.
  **Step 5 is DONE:**
  `check-generated-scripts.ps1` is a leg of `build/build.ps1` as of main
  13638, behind a 26-generator drift baseline, with a compile failure, an
  empty emission, an unhandled-node stub and any new drift failing the
  build. The observability gap that let this sit silent is closed for the
  41 PowerShell generators; it is NOT closed for bash, because BashEmit is
  missing 73 of ShellTypes' 140 constructors and the one bash generator
  emits a target that does not exist, so it is skipped before the stub
  scan runs.
- "No dangling pointer can leave the copy" holds for TYPES only. The
  seven sibling `mcopy-*` ceiling arms (text, name, row, lists, fields,
  ctors) still forward the ORIGINAL scratch pointer; the compile is
  refused afterwards by the keep-ceiling comparison, so nothing wrong
  ships, but the pointer does leave the copy. The identity `mcopy-sat`
  that used to wrap those seven returns is DELETED as of main 13795,
  so the name no longer lies about what happens there.
- **The keep-ceiling comparison is CALIBRATED and it is not another
  `mc-sat`** (2026-08-06). Two arms, each a one-off compiler built from
  the compiler source against the depot seed `B05BBA6B`, both compiling
  the `cutA` repro at `-Decks 36`, where the stock seed is clean:

  | `mc-ceiling` forced to | vs measured use | result |
  |---|---|---|
  | `keep-base + 262144` | BELOW | `CDX9002: Deck overflow in CHECK-KEEP; deck floor exceeded` |
  | `keep-base + 1048576` | ABOVE | clean, exit 0, no CDX9002 |

  The second arm is the one that makes the first mean anything: it edits
  the same line and produces nothing, so the guard is responding to the
  threshold and not to the file being touched.

  **Why it had never been observed to fire: it is 58x from firing on this
  workload, which is the expected reading and not evidence of death.**
  `-EscapeCheck` prints `KEEP-STAT used=548480` for the repro (identical
  at `-Decks 36` and `-Decks 150`). `demand-check-keep-floor` is
  100663296 with a zero band, so at 36 per cent `keep-height` is
  36,238,752 and the trigger is `used >= 32,044,448`. Measured use is
  548,480.

  **The monotonicity the soundness rests on holds, by inspection.** The
  copies test `__heap-save >= mc-ceiling` while the guard tests
  `keep-metrics.deck-end >= mc-ceiling`, so the guard is only sound if
  the frontier never rewinds between `__deck-set keep-base` and
  `phase-measure`. Everything in that window (`opening.codex` 628-635) is
  a `deck-record`, and `Unifier.codex` contains no `__heap-restore` at
  all, so `deck-end` is the phase high-water mark and no copy can alias
  without the guard seeing it.
- The prose above `MemoCtx` in `Unifier.codex` still says the ceiling
  path "shares the original instead" and that degraded paths return "the
  original box unchanged" -- both false for `mcopy-type` since this fix
  (it answers `ErrorTy`). Rule 12: correct or delete when the file is
  next open.
- The arithmetic arms of `lower-bin-op` still default (`OpAdd` ->
  `IrAddInt`), deliberately out of scope here. With `ErrorTy` now a
  designed answer from the copy, an `ErrorTy` reaching an arithmetic arm
  is a silent integer op; `append-op-unclassified` is the template that
  makes refusing cheap.

*(Historical below. FINDINGS 1 to 5 are the investigation in order and
several of their conclusions were superseded; they are kept because the
ruling-out is the evidence.)*

**Superseded status line: ROOT CAUSE FOUND (FINDING 3) and the REFUSAL IS
BUILT AND PROVEN (FINDING 5, CL 13303, shelved, gate green).** `&` on Text was
lowered as a LIST append because `lower-bin-op`'s type dispatch defaulted
an unrecognised type to `IrAppendList` instead of refusing. It now
refuses, with CDX2000 on the exact expression. **Still open: why the type
is lost at scale 32 at all.** The deck is the trigger; the dispatch was
what turned a lost type into a silent miscompile, and that half is
closed.

## The bug in one line

A deck scale of 32 compiles without a diagnostic and emits a program that
produces wrong output. 28 and below fails loudly with CDX9002. 36 and
above is correct.

## Why it matters more than the generators

`deck-scale-min = 32` (`codex/compiler/opening.codex`) is the derivation's
floor, and 1436 of 1674 entry points derive below it and therefore receive
exactly 32. Every binary built since the 12971 seed at a derived scale of
32 is suspect. The Shell DSL generators are how it was noticed, not its
extent.

## The repro

`checkappsScript.codex` cut to `ca-body = [ScSequence s01, ScBlank,
ScSequence s02]`, nothing else changed. Kept as the minimal case because
it is 776 bytes of correct output against 779 of corrupt, and it runs in
about a minute.

```powershell
build/compile.ps1 -Src cutA.codex -Out cutA.cdx -Log cutA.log `
  -Kernel seed\Codex.cdx -Decks 32       # corrupt
build/compile.ps1 -Src cutA.codex -Out cutA.cdx -Log cutA.log `
  -Kernel seed\Codex.cdx -Decks 36       # correct
build/test-run.ps1 -Kernel cutA.cdx -OutFile cutA.out
```

The correct output is byte-identical to what the pre-regression seed
`37A7EF8E4EF603AE` produces at its default, on this repro and on the full
`checkappsScript`. **The seed is not damaged; only the deck scale is.**

## The dose-response

| decks | result |
|---|---|
| 20, 24, 28 | CDX9002, Deck overflow in CHECK, deck floor exceeded |
| **32** | **compiles clean, output CORRUPT (779 bytes)** |
| 36, 40, 60, 80, 100, 150 | byte-identical to the clean seed (776 bytes) |
| 400 | OUT OF MEMORY |

`-Decks 0` (derived) is byte-identical to `-Decks 32` on this unit, which
is how the floor was confirmed to be the setting in play.

## FINDING 1: LOWER loses 42,504 bytes of IR, and nothing says so

`build/compile.ps1 -Measure` prints one `DECK-<n>` line per phase. Run at
32 and at 36, every phase reports an IDENTICAL `used=` except one:

| phase | used @32 | used @36 | delta |
|---|---|---|---|
| LEX | 797,080 | 797,080 | 0 |
| PARSE | 645,216 | 645,216 | 0 |
| PARSE-KEEP | 356,264 | 356,264 | 0 |
| DESUGAR | 1,240,944 | 1,240,944 | 0 |
| SCOPE | 751,688 | 751,688 | 0 |
| CHECK | 209,239,432 | 209,239,432 | 0 |
| **LOWER** | **31,425,024** | **31,467,528** | **-42,504** |

Everything upstream of LOWER is byte-for-byte the same amount of work, so
the AST and the type-checked tree are not the casualty. **LOWER produces
less IR at 32 than at 36 and the compile reports success.** That is the
dropped/stillborn thing, localized to one phase with a number on it.

## ~~FINDING 2: both detectors are structurally incapable~~ HALF REFUTED

**The `lower-sat` half is FALSE and was measured false 2026-08-08. See
FINDING 8.** `lower-ov` genuinely cannot fire, for the reason given
below, and that part stands. But the bag is
`lower-ov == 1 | lower-sat == 1`, so only ONE of the two has to work,
and `lower-sat` works. The error below is quoting `Lowering.codex`'s
prose -- which is about the IN-LOOP guard, genuinely dead, which is
exactly why it was moved to `deck-bound-short-of` -- and applying it to
the CALLER-side flag, a different predicate evaluated in a different
place. `BuildSettings.codex` lines 152-155 say so in as many words and
were the doc to believe. Kept in full below, because the ruling-out is
half the evidence and because the way it went wrong is the lesson.

*(Original text follows.)*

This is the answer to "why do we not catch it", and it is actionable
without settling the mechanism.

LOWER has two ways to notice it ran short and neither can fire.

**`lower-sat` asks a dead predicate.** `opening.codex` computes
`lower-sat = if deck-short-of lower-ceiling demand-lower-guard-band`.
`Lowering.codex` (prose above `lower-defs-acc`) already records why that
cannot work:

> The predicate is deck-bound-short-of, which reads R10, and NOT
> deck-short-of, which reads the deck-pos cell. lower-chapter is called
> inside a phase-wide deck-record extent and the cell is written back only
> when the nesting counter returns to zero, so inside the extent it is
> frozen at the deck's base and deck-short-of reduces to a question about
> the floor's width that never mentions the usage. **This guard was
> written with the cell and was therefore dead from the day it shipped.**

The in-loop guard was moved to `deck-bound-short-of` when that was found.
**The `*-sat` flags in `opening.codex` were not**, and they are what feeds
`deck-overflow-bag`, which is CDX9002.

**`lower-ov` cannot fire either, by construction.** It is
`check-deck-overflow (lower-end - lower-origin) lower-deck-height`. The
in-loop bail stops one guard band SHORT of the ceiling, so usage never
reaches the height it is compared against. The bail guarantees the
overflow check answers no.

So the only thing that knows the phase ran short is the loop that bailed,
and it returns a truncated accumulator and says nothing. **Whatever the
mechanism turns out to be, the fix for detection is the same: the bail
sites must RECORD that they bailed and that record must raise the error,
instead of re-asking a predicate afterwards.**

## FINDING 3: ROOT CAUSE. `&` on Text is lowered as a LIST append

`bench/disasm-cdx.ps1` (dumpbin via a synthetic COFF obj, driven off the
`.map`) settles it. The same expression, the same compiler, two deck
scales:

| | decks=36, CORRECT | decks=32, CORRUPT |
|---|---|---|
| shape | five pairwise calls | one n-ary call |
| helper | `__str_concat` x5 | **`__list_concat_many` x1** |
| operands | folded left to right | spilled to `[rbp-30h]`..`[rbp-58h]`, gathered into a 6-slot array via `mov rsi,r10` / `add r10,30h`, then `mov rsi,6` |

Call targets resolved against the map: `FFFFFFFFFFFF5608` is
`__str_concat+0`, `FFFFFFFFFFFF7642` is `__list_concat_many+0`.

**`__list_concat_many` is a LIST helper being handed six TEXT values.** A
Text's header word is its BYTE LENGTH; a list's is its ELEMENT COUNT. The
helper reads one as the other, so it walks the wrong spans and
concatenates whatever lies at them. That is precisely the output we have:
the first piece correct, then a walk of adjacent heap allocations, each
8-byte aligned behind a header word. `ArchitectsSketchbook` already
records this helper "read CCE text as a list length and marched R10 off
the top of RAM" in an unrelated incident.

### The line

`codex/compiler/IR/LoweringTypes.codex:165`

```
is OpAppend -> if is-text-type ty then IrAppendText else IrAppendList
```

`is-text-type` (same file, line 94) answers True for `TextTy` and for
nothing else. **So any type the predicate does not recognise -- `ErrorTy`,
an unresolved type variable, an unresolved `ConstructedTy` -- silently
selects the LIST opcode.** `emit-binary` then routes `IrAppendList`
straight to `emit-append-list` -> `emit-concat-many` ->
`__list_concat_many`, with no further type question asked.

Line 167 has the same shape for `OpAnd`, and `binary-result-type`
(line 183) feeds it: for `OpAppend`, if neither the left type nor the
expected type is text it answers `left-ty` unchanged, so an unknown type
propagates into the dispatch that then defaults it to a list.

**This is a whole class, not one line.** Every arm of `lower-bin-op` is
total by falling through to a default opcode: `OpAdd` ends in `IrAddInt`,
`OpSub` in `IrSubInt`, and so on. A type the predicates do not recognise
does not raise anything; it gets the default and the program is wrong.

### The tree already paid for this exact bug once

The prose immediately above `lower-bin-op` (lines 133-147) is the
post-mortem of the same defect in the arithmetic arms:

> Without that strip a `unit Real` matched none of the floating arms and
> fell through to `IrAddInt`, so `Metre 1.5 + Metre 2.5` emitted an
> INTEGER add of two IEEE-754 bit patterns. **Wrong on every input, with
> no diagnostic**: `CDX9010` guards the comparison emitter, and arithmetic
> never reaches it. A `unit Integer` was right by accident.

That was fixed by adding `strip-unit-ty` so the known shape reaches the
predicates. **The structural defect was not fixed: the fallthrough is
still a silently-wrong opcode rather than a refusal**, which is why it
came back on the append arm with a different way of losing the type.

### What this makes of the deck

The deck scale is the TRIGGER, not the defect. Something about a tight
LOWER deck leaves the operand type unrecognised at this dispatch, and the
dispatch converts that gap into wrong code instead of an error. Which of
the two to fix is not a real question: **both**, but the dispatch first,
because it is the thing that turns any future type gap into a silent
miscompile.

Still open, and now narrow: WHY the type is unrecognised at 32. LOWER
using 42,504 fewer bytes (FINDING 1) is consistent with types arriving
less resolved, but that is not yet demonstrated.

## FINDING 4: the floor was raised (13268) and the defect is untouched

fester raised `deck-scale-min` and rebuilt the seed as
`E0B667443430D9C7`. Measured against that seed, on the same repro:

| | result |
|---|---|
| `-Decks 0` (derived, new floor) | **clean**, 776 bytes |
| `-Decks 32` | **CORRUPT**, 779 bytes, unchanged |

So the raise does what it was meant to do: the default no longer lands in
the band, and the fleet is unblocked. **It fixes nothing about the bug.**
A Text concatenation is still lowered as a list append at any scale that
loses the type, `lower-bin-op` still defaults an unrecognised type to a
wrong opcode, and the whole `lower-bin-op` class is exactly as it was.

This is the measured form of the warning at the end of the plan: raising
the floor moves the band, it does not close it. Nothing here is a
criticism of landing it -- unblocking the fleet was right and urgent --
but **the red flag should not be recorded as closed on the strength of
13268**, and the repro must keep being run with an explicit `-Decks 32`,
because at the derived scale it now passes.

## FINDING 5: the refusal is BUILT and PROVEN. CL 13303, shelved.

Step 0 is written. `lower-bin-op`'s append arms no longer default an
unrecognised type to the list opcode.

- `LoweringTypes.codex` gains `is-list-type` and
  `append-op-unclassified`, which asks the question the dispatch cannot
  answer. It lives beside the predicates it uses, so the `unit Real`
  post-mortem's own lesson (derive a predicate once, a copy will drift)
  still holds.
- `Lowering.codex`'s `lower-binary-maybe-eq` asks it before taking the
  last arm and answers `IrError` when it cannot classify. That is
  **CDX2000, `cdx-ir-error`, sev-error at phase-codegen**, whose own
  description is "Codegen encountered an IrError node, indicating an
  earlier phase silently failed". The code already existed for exactly
  this.
- The type is `deep-resolve`d first, so a resolvable type variable is not
  refused.

**The two arms, run against the newly built compiler `525B5675`:**

| arm | result |
|---|---|
| `-Decks 32` | **REFUSES**: `PowerShellEmit.codex:20:51: error CDX2000: append operand is neither text nor a list` |
| `-Decks 36` | compiles, 776 bytes, **byte-identical** to the pre-regression output |

Line 20 column 51 is `header & binding & params & ")\n\n" & body & "\n"`
-- the exact expression that was being miscompiled. The diagnostic lands
on the defect, not near it.

**`build/build.ps1` is GREEN**, hard fixed point in one pass, 223.9 s,
BVT and oracles and cross-smoke and plug-smoke all OK. So the
unclassified case occurs nowhere in the compiler's own source, the BVT,
the oracles or the plugs: this refusal costs no working code.

**Rebased and re-gated.** blu's 13293 extracted prose from the Codex
quire IR, including both files this change touches, so the shelf's base
moved. Rebase resolved clean (1 yours + 6 theirs and 1 yours + 2 theirs,
zero conflicting), the added prose block was trimmed to the runtime
representation fact per rule 12, and `build/build.ps1` is green again on
the rebased source (182.5 s, SUT === stage1 in one pass, constants match,
`check-cdx-registry` OK at 110 codes with all raised codes documented).
**Both proof arms re-run identically on the rebased build, and the
compiler digest is unchanged at `525B5675`** -- prose extraction does not
reach codegen, which is the expected answer and worth having measured
rather than assumed.

**Scoped to the append arms on purpose.** Every arm of `lower-bin-op` has
the same fallthrough shape, and `OpAdd` ending in `IrAddInt` is precisely
the `unit Real` defect whose post-mortem sits directly above the
function. Fixing those too is right and is a separate change with its own
blast radius.

**CL 13303 is SHELVED, not submitted: it is seed-affecting and no token
was requested.** It also wants a seed rebuild before it can ship.

### What this does and does not close

It closes the detection hole for this class: a lost type at an append can
no longer become a silent miscompile, at any deck scale, forever.

It does NOT explain why the type is lost at scale 32 in the first place.
That question is now much cheaper to answer, because the compiler will
say where it happened instead of emitting wrong code, but it is still
open and it is the next thing.

## FINDING 7: THE ROOT CAUSE. The CHECK deck overruns its reservation by 9.3 MB

*FINDING 6 below found the proximate cause and contained it. It did not
explain what corrupted the type. This does, and it supersedes 6's
"still open" section.*

**The work AFTER the definition walk allocates on the CHECK deck with no
ceiling guard.** `check-all-defs` checks `deck-short-of ceiling` per
definition, which is the loud CDX9002 at 28 and below. But `resolved-env`,
`sorted-all0` and `sorted-et0` (`opening.codex`, immediately after the
walk) allocate on that same deck and are guarded by nothing.

Measured, same source, two scales:

| decks | ceiling | deck position after `sorted-all0` | outcome |
|---|---|---|---|
| **32** | 263,802,816 | **273,088,584** | **overruns by 9,285,768 bytes** |
| 36 | 294,672,888 | 277,115,112 | 17.6 MB of headroom, clean |

**The CHECK deck's ceiling is exactly where the bivy begins.** At scale 32
CHECK's `bivy-origin` is 263,798,352 against a ceiling of 263,802,816. So
a deck that runs past its reservation writes directly into the scratch
above it, and that scratch is where the types recorded in `expr-types`
live: `record-expr-type` puts the ENTRY on the deck via `deck-record` but
the TYPE it points at is whatever the caller passed, and `check-chapter`
issues `__deck-exit` before the walk, so those types are bivy
allocations. 133 of them are destroyed at 32 and none at 36. The corrupt
type read at lowering sat at 284,239,952, above the ceiling, inside the
overrun; the word found in it was a code pointer belonging to a closure
allocated there afterwards.

### How it was pinned, because the method is reusable

A tripwire, not a debugger. `et-tripwire` walks the recorded entries and
reports the first whose type has a first word outside 0..28. It allocates
nothing, so it cannot perturb the layout it measures. Six bisection steps,
all at full WHPX speed:

| checkpoint | result at 32 |
|---|---|
| before each definition, in the walk | never fires |
| `TRIPWIRE-DONE`, end of the walk | never fires |
| `twF`, after `check-chapter` returns | -1, clean |
| `twG`, before `resolved-env` | -1, clean |
| `twG2`, after `resolved-env` | -1, clean |
| **`twG3`, after `sorted-all0`** | **80** |
| `twH`, after `sorted-et0` | 80 |
| `twC`, before the keep copy | 80 |

Entry 80 named the window; `twG2` against `twG3` named the expression.
GDB was prepared (`build/gdb-watchpoint.ps1`, TCG write-watchpoint) and
was not needed.

### FOUR silent tolerances in series

Each is individually defensible. In series they convert a 9 MB deck
overrun into wrong bytes with a clean compile.

1. **`check-ov` cannot see the overrun.** `check-end` is sampled before
   the post-loop work and `check-ov` is computed from it, so the
   measurement window closes before the overrun opens.
2. **`mcopy-sat` could never fire** (FINDING 6): `__record-set` on a
   non-`mutable` record, result discarded.
3. **`mcopy-type` detects the invalid tag and forwards the pointer.**
4. **`lower-bin-op` defaults an unrecognised type to the list opcode**,
   and `__list_concat_many` reads a Text's byte length as an element
   count.

### The fix, and what each part is for

- **Root cause:** the deck extent is re-measured after the post-loop work
  and raises `CHECK` deck overflow. The overrun now refuses.
- **Containment:** `mcopy-type`'s four unsafe escapes answer `ErrorTy`, so
  no pointer into scratch can leave the copy whatever the cause.
- **Backstop:** the append dispatch refuses rather than defaulting.
- **Hygiene:** the dead `mc-sat` field is deleted and the CHECK-KEEP
  ceiling arm compares the keep deck's measured end against `mc-ceiling`.

Proof: gate green, SUT === stage1, hard fixed point in one pass. At
`-Decks 32` the repro now REFUSES with `CDX9002: Deck overflow in CHECK`;
at 36, 64 and the derived scale it compiles to output byte-identical to
the pre-regression seed.

## FINDING 8: the three bails are already caught on the CDX path, and the TEXT path faults instead

*2026-08-08, val. Seed `43189C1E7D762144`.*

**The plan carried a proposal to thread a bailed flag out of the three
bail sites and raise from that, because they bail silently. The premise
is false and the proposal has been removed.** The sites do return a
partial accumulator, but they do not do it silently: each phase's
caller-side `*-sat` flag asks the same question about the same frontier
a few lines later, and raises CDX9002 naming the phase.

It is airtight by construction and not merely observed. The bail tests
`__heap-save + band >= ceiling`. `*-sat` tests
`__deck-pos + band >= ceiling` **after** the phase-wide `deck-record`
extent has exited, and exiting is what writes the live frontier back
into the cell. Same quantity, same ceiling, same band, so bail implies
sat. Nothing allocates on that deck between the bail and the write-back.

Measured by sabotage, because an absent guard looks identical to a
working one. Each arm starves ONE floor in the concatenated source,
builds a compiler from it against the depot seed, and compiles the
`cutA` repro at `-Decks 100`, where the stock compiler is clean:

| floor starved to | CDX-mode result on cutA |
|---|---|
| `demand-lower-floor` 10 MB | `CDX9002: Deck overflow in LOWER`, no binary |
| `demand-lower-floor` 8.45 MB | `CDX9002: Deck overflow in LOWER`, no binary |
| `demand-resolve-floor` 8.45 MB | `CDX9002: Deck overflow in RESOLVE`, no binary |
| `demand-lift-floor` 8.45 MB | `CDX9002: Deck overflow in LIFT`, no binary |
| **control, unstarved** | **clean, 126,088 byte CDX, 776 byte output** |

**The two 10 MB arms that did NOT refuse are the reason the tighter
arms exist**, and they are the L-FALSIF row: `demand-resolve-floor` and
`demand-lift-floor` at 10 MB compiled clean AND produced the correct
776 bytes, which means the bail never fired rather than that the guard
failed. A clean compile cannot tell those two apart. Starving to
band + 64 KB is what forced the bail and let the guard be observed.

**No call site is unguarded.** `lower-chapter`, `rewrite-ir-defs` and
`lift-lambdas` have exactly four call sites, all in `opening.codex`,
and each is followed by its own check: 717 -> 731 -> 738,
764 -> 770 -> 811, 781 -> 782 -> 812, 789 -> 795 -> 813.

### Two counts re-measured, both stale (L-COUNT)

**`BuildSettings.codex` says "-Decks 93 refuses and 94 compiles".** Both
compile now, byte-identically, and so does 70. That figure was measured
2026-07-21, before `demand-lower-floor` moved; the line is still in the
file and still reads as current. It is the prose above
`demand-lower-floor` and should be re-measured or dropped by whoever
next opens that file.

**The selfhost dose-response, measured on the concatenated compiler
against seed `43189C1E7D762144`:**

| `-Decks` | result |
|---|---|
| 20 | `CDX9002` in **LEX** |
| 30, 36, 40, 50, 60 | `CDX9002` in **DESUGAR** |
| 70, 80, 90, 93, 94 | clean, and all five byte-identical to each other |

Every clean compile differs from `seed/Codex.cdx` in exactly 96 bytes at
offsets 40..135, which is the signature block and not codegen. **That
comparison is the one to get right**: a fresh compile is unsigned, so
"differs from the seed" is the expected reading and says nothing. The
compiles have to be compared against each other.

LOWER, RESOLVE and LIFT never bind on the selfhost at any scale --
DESUGAR and LEX refuse first -- which with CHECK refusing first on
`cutA` is why no reachable knob setting exercises these three bails.

### The gap that is real, and it is the other output mode

Same compiler, same source, same starved floor, same `-Decks 100`, one
variable changed:

| output mode | call site | LOWER floor 10 MB |
|---|---|---|
| CDX | `opening.codex:764` | `CDX9002: Deck overflow in LOWER` |
| **text (`-Text`)** | **`opening.codex:717`** | **general protection fault in `bag-add+0x21`, no CDX9002** |

The faulting register is identical across runs and across both starved
floors (`RSI`/`R12` = `0xCE232800CEE6AC8`, non-canonical), so it is
deterministic. The log is 684 bytes against the control's 58,327: it
faults before emitting anything, and before any diagnostic is printed.

**The sentence that used to close this paragraph said "nothing wrong
ships, because no output is produced, so this is NOT the miscompile
class". That is true of `-Text` and FALSE of the IR mode, which was never
run. See the resolution below: at the same starved floor `-IrCce` emits
corrupt IR silently.**

**It is specific to LOWER, not to the text path's reporting.** Starving
`demand-desugar-floor` instead raises `CDX9002: Deck overflow in DESUGAR`
cleanly in BOTH modes, so the non-CDX path reports a starved phase
perfectly well in general. What differs is the region
`opening.codex:730-740`.

`bag-add` (`DiagnosticBag.codex:38`) reads `d.severity` as its first act,
and the faulting register holds a non-canonical value, so a bag is being
walked with a garbage `Diagnostic` in it. `lower-precise-bag` at 734 is
the one bag in that merge built BEFORE the `compact-phase` at 735 that
reclaims the deck, and it is merged after, at 740.

**That candidate was probed and the probe was INVALID. It is not
confirmed and should not be quoted as the cause.** Substituting
`no-overflow-bag` for `lower-precise-bag` in the 740 merge appeared to
remove the fault under a starved floor -- and then the unstarved control
built from the same edit CRASHED on an ordinary compile, which means the
edit broke the compiler and the starved arm measured nothing. The control
is the only reason this is not written up as a pin. Whoever takes this
needs a probe that does not alter the merge, and needs to run the
unstarved arm of it first.

### RESOLVED 2026-08-09 (val). The cause is pinned and the fix is one reordering

**`compact-phase` runs BEFORE the merge that reads a bag built before it.**
In `compile-frontend-passes`, `lower-precise-bag` is built, then
`compact-phase flags poison-lower` reclaims the LOWER deck, and then
`frontend-bag-with-passes` merges that bag. `deck-record` does not save it
from the reclaim. The CHECK path never had this: it merges into
`result0`'s bag and compacts AFTER, which is exactly the order this now
uses. The whole change is moving one line below the five that follow it.

**The IR mode was never run, and it is the serious half.** Same starved
`demand-lower-floor` of 10 MB, same `cutA`, same `-Decks 100`:

| output mode | before | after |
|---|---|---|
| CDX | `CDX9002: Deck overflow in LOWER` | unchanged |
| `-Text` | GP fault in `bag-add+0x21`, 684-byte log | `CDX9002` in LOWER, 129-byte log |
| **`-IrCce`** | **46,163 bytes of CORRUPT IR, no CDX9002, no fault, exit success** | **`CDX9002` in LOWER, 0 bytes emitted** |

The correct IR is **130,924 bytes and the starved output first diverges at
byte 17,326**, so it is not a truncation, it is wrong content. **That is
the silent miscompile class, on the shipping path for `-Ir`,** and the
entry above had it recorded as impossible because only `-Text` had been
measured. L-GAP: the suite could not express the question it was being
read as answering.

**Controls, and the previous probe failed on exactly this one.** The
unstarved arm was run FIRST every time, as this section demands:

- unstarved + fix, `-Text`: clean, log **58,327 bytes**, the control's
  figure to the byte
- unstarved + fix, `-IrCce`: **130,924 bytes, BYTE-IDENTICAL** to the
  unfixed control
- the same 2.7 MB concatenated compiler source through the fixed and
  unfixed compilers: **BYTE-IDENTICAL**, 2,745,998 bytes
- the unstarved control output is byte-identical to the depot seed's, so
  the concat-built compiler is a fair instrument (L-SAMEVER)

**One candidate was probed and REFUTED, and it is worth recording so it is
not tried again.** `passed` is built at
`if run-passes then deck-record (...) else IRPassResult { ... }`, and in
text mode `run-passes` is False, so the record is NOT deck-recorded while
the CDX path always records it. That asymmetry is real and it is not the
cause: wrapping the else branch in `deck-record` builds a working compiler
(unstarved arm clean at 58,327) and the starved arm faults identically,
same register value. The asymmetry is left alone.

### Why that proposal read the way it did

FINDING 2 was written before FINDING 7 found the root cause. It asked
"why do we not catch it" while the mechanism was still believed to be a
dropped definition in LOWER, and the answer it reached outlived the
theory that motivated it: the ruled-out list in this same file already
records **196 symbols at 32 and 196 at 36, none missing**, so no bail
was ever implicated in the actual defect. A plan step can survive the
finding that superseded it, and this one did, for two days.

**And its second half would have caused the bug it was meant
to prevent.** It proposed moving the `*-sat` flags to
`deck-bound-short-of` or deleting them. `deck-bound-short-of` reads
`__heap-save`, which at that point is the frontier of whatever deck is
current, not the phase's; and deleting them removes the only detector
that fires. Either edit opens the hole the step was written to close.

## FINDING 6: the proximate cause, and the containment that fixed the symptom

The type reaching the append dispatch is **not an unrecognised type. It is
not a type at all.** The same `ty-debug-tag` function, at the same call
site, rendered `expected=TextTy` correctly and `left-ty` as heap bytes.
The tag word at that address is **3293622** (0x3241B6), which is no
constructor index; there are 27. It is below the 6 MB heap floor, so it
reads as a pointer into static data rather than a length.

**It took three independent silent tolerances to turn one bad word into a
corrupt program.**

**1. `mcopy-type` sees it and forwards it.** `Unifier.codex` reads

```
else if peek-qword a 0 < 0 then ty
else if peek-qword a 0 > 28 then ty
```

The compiler tests whether the value is a CodexType, concludes it is not,
and returns the pointer unchanged into scratch the caller reclaims a
moment later. Instrumented per path, the failing arm is `> 28`.

**2. `mcopy-sat` could never fire.** It read
`let z = __record-set mc "mc-sat" 1 in x`, and `MemoCtx` is **not
`mutable`**, so the set built a record, bound it to `z`, and dropped it.
`mc.mc-sat` was 0 on every path ever taken. `opening.codex` reasoned from
that flag in choosing the floor: *"`mc-sat` is not it, since that one is
checked and does raise (see CHECK-KEEP below)"*. It is checked. It could
not be true.

**3. `lower-bin-op` defaults an unrecognised type to the list opcode**
(FINDING 3), so the bad type became `IrAppendList` and
`__list_concat_many` read a Text's byte length as an element count.

### The fix

- `mcopy-type`'s four unsafe escapes (ceiling, depth, tag<0, tag>28)
  return `ErrorTy` instead of a pointer into scratch. **No dangling
  pointer can leave the copy.**
- The dead `mc-sat` field and its assignment are deleted; the caller now
  compares the keep deck's measured end against `mc-ceiling`, which is
  sound because that frontier is monotonic across these copies.
- The append dispatch refuses rather than defaulting (FINDING 5), as the
  backstop.

**Why returning `ErrorTy` FIXES rather than merely refuses.**
`lower-name-normal` already has the right fallback: when
`lookup-expr-type` answers `ErrorTy` it looks the name up in the overlay
and base, which hold the correct type. The dangling pointer was not a
missing answer, it was a WRONG answer that suppressed the good path.
Handing back `ErrorTy` lets the existing fallback run.

### Proof

`build/build.ps1` green: SUT === stage1, hard fixed point in one pass,
constants match, `check-cdx-registry` OK, 179.4 s.

The band is gone:

| decks | before | after |
|---|---|---|
| 20, 28 | CDX9002 | CDX9002 (unchanged, genuinely out of room) |
| **32** | **CORRUPT, silently** | **776 bytes, byte-identical to pre-regression** |
| 36, 64, derived | correct | correct |

The full `checkappsScript` is byte-identical to the pre-regression output
at both `-Decks 32` and the derived scale. And
`build/check-generated-scripts.ps1` over all 42 generators with a live
target reports **zero COMPILE FAILED and zero unhandled-node stubs**,
against blu's original 12 failures and 38 corrupted; `check-apps` now
matches its shipped script byte for byte. The 26 still marked DRIFTED are
the pre-existing maintenance drift measured 2026-08-03, before the
regression, where the shipped script is the maintained side.

### Still open, and it matters

**Nothing here explains what writes 3293622 over that type.** The value
is already corrupt when the keep-copy inspects it, so the write happens
in CHECK or earlier, and it is deterministic and identical at both
failing sites. The fix stops a corrupt type from reaching code
generation; it does not stop the corruption. At a different deck size or
in a different unit that write will land on something else, and the next
thing it lands on may not be a type with a validity check in front of it.

**Do not close the red flag on this.** What is closed is the silent
miscompile: the compiler can no longer emit wrong bytes from this defect.
Finding the writer is the next investigation and it now has a much
sharper starting point, because the corrupt word has a known value and
two known sites.

## Ruled out this session, with the instrument that ruled it out

Each of these is a theory that died. They are kept because the ruling-out
is what makes the remaining space small.

- ~~Definitions dropped by `lower-defs-acc`'s bail~~. The emitted symbol
  map has **196 symbols at 32 and 196 at 36, none missing, none extra**.
  Only two functions differ, and only in size. Nothing was truncated out
  of the def list.
- ~~An IR optimization pass~~. `-Passes none` at 32 is still corrupt,
  byte-for-byte the same 779. The default pipeline (fold-constants,
  inline-leaf-calls, inline-single-caller) is not involved.
- ~~Structurally invalid IR~~. `-Passes +ir-check`, the purpose-built
  validator, reports **no violations at either scale**. A third instrument
  that cannot see this.
- ~~Deck/bivy handing out the same bytes at a compact~~ (the
  `ArchitectsSketchbook` hazard, and the theory this file opened with).
  **`-PoisonCompact` changes nothing**: 32 is still corrupt, 36 is still
  clean, and nothing faults. The control staying clean is what makes the
  negative worth trusting.
- ~~A damaged or mis-addressed string constant pool~~. Every `movabs`
  constant pointer in `emit-powershell` resolves, at BOTH scales, to a
  constant of identical length and identical CCE bytes. The pool is intact
  and correctly addressed; only its base has relocated.

## The instrument that cracked it, and the two that did not

**`bench/disasm-cdx.ps1` is the disassembler and it works.** It reads the
CDX text-section offset out of the header, slices each function by the
`.map`, wraps the bytes in a synthetic COFF object and runs `dumpbin
/disasm`, writing one file per function under
`bench/build-output/codex/<name>/funcs/`. To point it at an arbitrary
build, stage the pair as `bench/build-output/codex/<name>/<name>.cdx` and
`<name>.map` and run `-Name <name> -Functions @('a','b')`.

**Call it in-session with `&`, not `pwsh -File`.** With `-File` the
`-Functions` array arrives as one comma-joined string, every function is
filtered out, and the script still prints its normal "196 functions ->"
line and exits 0. It looks like it ran.

Two instruments could not see this and cost time:

- **IR text mode is unavailable on this repro.** `-IrUni` OOMs at both 32
  and 36 on cutA, and crashes in `__str_concat+0xF9` on the full
  `checkappsScript`. That crash reproduces identically on the clean
  pre-12971 seed, so it is a separate pre-existing defect (logged below).
  Note the irony: the IR text is where `append-text` versus `append-list`
  would have been legible in one line, and it is the one output we cannot
  get on this unit.
- **`-Passes +lir-dump` produced no `LIR` lines** on this unit at either
  scale, presumably the whitelist.

## The superseded theory

*Kept because it opened the investigation and because its refutation
above is the useful part.*

## ~~The LOWER reservation-copy truncates~~ REFUTED

`ArchitectsSketchbook.md` "Compilation Phase Map" records that LOWER uses
the **reservation-copy pattern** (CL 3805): the RESOLVE deck is reserved
FIRST, a bounded LOWER scratch is built above it, and `rewrite-ir-chapter`
deep-copies the survivors down into the reservation. One `phase-compact`
then reclaims the scratch.

Both the reservation and the scratch are sized through `scaled-floor`, so
both shrink with the knob. If the reservation is too small for the
survivors, a deep copy that does not check its own bound either stops
early or writes past the end into scratch that the following compact then
reclaims. Either produces exactly what is measured: less IR, no error.

It also explains why the guards stay quiet. `deck-short-of` asks whether a
deck ran into its guard band; a copy that fits in the SCRATCH while
overrunning the RESERVATION is not that question.

## Ruled out earlier, with the evidence

- ~~Text/rope rendering, `&`, or a string builder~~. Left- and
  right-nested concatenation at 4/16/64/256 iterations is byte-correct
  under the bad seed. reek independently measured five-deep `&` chains
  rendering exact through `gop-draw-text` on the same seed.
- ~~`__str_concat`'s in-place fast path aliasing a shared left operand~~.
  A probe that reads the left operand back after extending it, with a
  control that allocates in between so the fast path cannot apply, is
  correct under BOTH seeds.
- ~~A size threshold on concatenated output~~. 2,560-character strings
  are correct; the 776-byte repro is corrupt.
- ~~The seed is damaged / the 12971 grounds CL broke codegen~~. The same
  seed at `-Decks 150` reproduces the pre-regression output byte-for-byte.
  The grounds CL added a field to `IRTextMeta`, an IR-text-only record; it
  perturbed layout and exposed this, it did not cause it.
- ~~A saturation guard that fires and is ignored~~. Every `*-sat` in
  `opening.codex` feeds a `deck-overflow-bag`, which is CDX9002. At 32
  none of them fire. The detection genuinely is absent, not merely
  unhandled.
- ~~`-IrUni`/`-IrCce` crashing in `__str_concat+0xF9` on a large
  chapter~~. Real, and NOT this: it reproduces identically on the clean
  pre-12971 seed. Logged separately below.

## The plan

**Step 0 is the fix Damian asked for and it is now writable.**

0. ~~**Refuse instead of defaulting, in `lower-bin-op`.**~~ **DONE, CL
   13303, shelved. See FINDING 5.** Original statement kept below.
   The `OpAppend`
   and `OpAnd` arms must not answer `IrAppendList` for a type that is
   neither text nor a list. Give the dispatch an explicit failure and
   raise a compiler error naming the operator and the type it could not
   classify. The arithmetic arms want the same treatment; the `unit Real`
   post-mortem above them is the same defect and its fix left the
   fallthrough in place.
   **Test, and it is already built:** at `-Decks 32` the repro must
   REFUSE; at 36 it must still compile and still produce the byte-identical
   776-byte output. The sabotage arm is to force the unknown branch on a
   known-good build and require exactly that refusal.
   This is worth doing even though it does not by itself explain why the
   type is lost, because it converts every future instance of the whole
   class from a silent miscompile into a build failure.

2. **Get a disassembler onto the two divergent functions.** Settle why
   `-Passes +lir-dump` prints nothing on this unit, since LIR is the layer
   the remaining theory is about; failing that, codex-vm's `-Break` on
   `emit-powershell` in both builds.
3. **Decide whether the deck is trigger or cause.** If the defect is in
   spilling, register pressure alone should reach it at a generous scale.
   Build a unit with heavy pressure at `-Decks 100` and look for the same
   signature. If it reproduces, the knob is a red herring for the DEFECT
   even though it is the whole story for the EXPOSURE.
4. ~~**Re-derive the floor against output equality, not against CDX9002.**~~
   **DONE 2026-08-06. The floor holds; see the result at the top of this
   file.** The old account stated that a sweep of all 1674 entry points at
   32 raised no CDX9002, which is true and answers only the loud half of
   the question (L-FALSIF). The replacement compiles each unit at its
   derived scale and at `-Decks 150` and compares OUTPUT BYTES.
   Two counts in the old sentence were wrong and are worth naming rather
   than quietly replacing. **There are 1369 entry points, not 1674**
   (1541 files define `opening`; 172 of them live under `codex/test/
   errors/` and refuse at every scale, so they can carry no output
   evidence). And a sweep at 32 is no longer the measurement to want at
   all, because the floor moved to 64 in main 13268 -- the derived scale
   is the thing to compile at, not a literal that was current when the
   sentence was written. L-COUNT: re-measure, never carry forward.
5. **Wire `check-generated-scripts.ps1` into a gate.** It is wired into
   none, which is why this sat silent.

**Do not raise the floor and call it fixed.** 36 is the answer for one
unit. Until step 3 settles trigger-versus-cause, a higher floor moves the
band rather than closing it.

## Open defect found beside this, not part of it

`-IrUni` / `-IrCce` on a large chapter crashes the compiler in
`__str_concat+0xF9` (general protection; RBX and R12 hold CCE text, not
pointers) or times out at 600 s. It reproduces IDENTICALLY on the clean
pre-12971 seed, so it is pre-existing and is not this regression. It does
block using IR text as an instrument on large units, so anything that
wants IR text as evidence needs a fallback.

**It and FINDING 8's fault are both general protection faults reached
through `compile-frontend-passes`, the non-CDX path.** That is an
adjacency and not yet a claim of common cause: the faulting sites differ
(`__str_concat+0xF9` against `bag-add+0x21`) and only FINDING 8's is
known to need a starved deck. Whoever diagnoses either should check the
other before assuming two defects.

## Lessons this is already an instance of

- **L-FALSIF**: the floor's validating sweep could only observe CDX9002,
  so it could not fail on the case that matters.
- **L-ERASED / measurement discipline**: an instrument pointed at part of
  a question, read as an answer to all of it.
- **L-SELF**: the seed changed at my CL, and suspecting it first is what
  produced the `-Decks 150` control that cleared it.
