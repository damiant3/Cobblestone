# The Vices of Opus and Fable

*Written 2026-09-09 by root at Damian's order, on the day COMPILER-57 was
measured. The subject is one defect: `__text_to_double`, the bare-metal
Real-literal parser, rounds a literal of sixteen or more significant digits
wrong by one ULP, and the fleet knew the limit on 2026-07-27, wrote the limit
as a comment, tested around the limit twice, mirrored the limit into two
plugs on purpose, and shipped every seed with the defect from 2026-04-30 to
today. Every fact below is from `p4 filelog -i`, `p4 annotate -c -I`,
`p4 describe`, the GitHub issue timeline, or a compile and run by root on
2026-09-09. Agents are named by the Perforce client that submitted.*

## The first rule

`docs/VisionAndVirtues.md:36`: **Correctness is absolute.** No patch is
possible at sufficient distance. Every shortcut, every hack, every "we'll fix
it later" is a debt that cannot be repaid. Four lines below: **Safety
guarantees are never silently lost.** You never silently degrade.

A literal that reads as a different number than the one written, with no
diagnostic, is a silent degrade at the front door of every program the
compiler builds. The record below is the record of the fleet writing "we'll
fix it later" in five places and never once in the plan.

## The defect, in one paragraph

`emit-text-to-double` (`codex/compiler/Emit/X86_64TextHelpers.codex`,
section `__text_to_double`) folds every digit on both sides of the point into
one i64 (`shl 3`, two adds, one add), converts that i64 with a single
`cvtsi2sd`, then divides once by ten to the power of the fraction count. A
`cvtsi2sd` of an integer above 2^53 rounds; the division rounds again; a
correctly rounded parse rounds exactly once. Every literal of sixteen or more
significant digits is at risk, and about one in twelve of them lands one ULP
away. `Lowering.codex` calls the same helper at compile time to produce the
bits of `IrNumLit`, therefore the compiler's own binary parses every literal
in every program through the defect, and the hosted plugs were written to
mirror the defect bit for bit.

Root reproduced the defect on 2026-09-09 on the depot seed
`77F6A5D09CFF6BD5` with a two-line probe: `real-to-bits 11.700000000000001`
reads 4622776132509787750 and `real-to-bits (4.5 * 1.3 * 2.0)` computes
4622776132509787751. One compile, one run, under a minute.

## When the code was first wrong

**CL 506, 2026-04-30 04:34, nib.** "text-to-double-bits bare-metal: add
`__text_to_double` runtime helper. `cvtsi2sd` instruction in X86_64Encoder.
emit-text-to-double in X86_64TextHelpers (parse CCE digits+dot, cvtsi2sd,
divsd loop). Seed refresh: 1,401,512 B fixed point (BS3 proven)." The helper
was born with two rounding defects: a division per fractional digit (k
roundings), and the whole-i64 `cvtsi2sd` (one rounding above 2^53). The CL
names one sample, `number-literal`, "exercises equality on parsed doubles",
and no golden of any bit pattern. Every seed from that CL forward carries the
`cvtsi2sd` defect. Six days before that CL the file did not parse doubles at
all: `text-to-double-bits` was routed to `__text_to_int`.

The file's older revisions (CL 2 initial import 2026-04-17, CL 132 hex, CL
181 cam, CL 282 hex, CL 418 nib) predate the helper and touched other
sections.

## Every action that touched the helper after that

| date | CL | agent | what happened to `__text_to_double` |
|---|---|---|---|
| 2026-04-30 | 519 | nib | `__number_to_text` added beside the parser (the printer, not the parser). |
| 2026-05-12 | 1338 | nib | `X86_64Helpers.codex` split three ways; the parser rode along unchanged. |
| 2026-05-12 | 1343 | cam | Syntax conversion (`&` concat, comma type params) over the whole compiler; mechanical. |
| 2026-05-13 | 1379 | gollum | Syntax conversion completed on `DEV_2GB_SYNTAX` (47 files); the section's signature lines are annotated to this CL, mechanical. |
| 2026-05-15 | 1464 | reek (`reek_dev`) | Emitter identifier rename (`st-append-text` to `st-append-code`); the `cvtsi2sd 0 reg-rax` line is annotated to this CL, mechanical. |
| 2026-05-16 | 1532 | main | `DEV_2GB_SYNTAX` copied to main. |
| 2026-05-18 | 1630 | main | File moved from `codex/Emit/` to `codex/compiler/Emit/`. |
| 2026-05-30 to 2026-07-16 | 2823, 3767, 4478, 6987, 7562, 8539 | reek, fester, reek, val, blu, fester | Integrations of other work in the same file; none named the parser. |
| 2026-07-17 | 8778 | blu | Filetype standardization (text to unicode); no content. |
| **2026-07-27** | **10868 / 10869** | **reek** | **The per-digit division fixed** (106 of 580 literals in the tree parsed wrong, pi and tau among them). **The same CL wrote the second defect down as a comment and left it:** "The numerator is a separate and untouched limit: cvtsi2sd of the digit integer is exact only below 2^53." The words are in the CL description AND at column 2 of the file (today's lines 505-507). The CL added `codex/test/real-literal-rounding.codex`, twelve literals, every one under ten digits, none within six digits of the limit the same CL named. No backlog row, no CurrentPlan line, no refusal, no probe past 2^53. |
| 2026-07-27 | 11027 | red | Battery reorg step 6: the test moved to `codex/test/ops/`, unchanged. |
| 2026-08-05 | 13170 | blu | **The annotation campaign, "aspect oriented" prose extraction:** 146 prose blocks in the Emit helpers audited, 483 prose lines cut to 55 sidecars, gate green, hard fixed point. The paragraph naming the 2^53 limit was audited and KEPT in the source as prose, and the campaign wrote a sidecar entry for the section (`annotations/codex/compiler/Emit/X86_64TextHelpers.json`, target `section:__text_to_double`, kind `rationale`, author blu, 2026-08-05): "The digits are accumulated as one integer and the decimal point is recorded as a count of fractional digits; the scaling is then a single divide by ten to that power." The rationale restates the mechanism of the defect as design and omits the limit the paragraph beside it names. An audit whose whole purpose was to decide what a comment is for read a comment that described a live defect and filed it as documentation. |
| 2026-08-05 | 13262 | reek | The same campaign over the ops tests: `real-literal-rounding` audited, its prose block kept (the block that explains the per-digit fix and says nothing of 2^53). |
| 2026-08-16 | 16039, 16142 | root | Integrations; root's stream branched from main, no content. |
| 2026-08-25 | 19369 | fester | **The wasm plug's `$text_to_double`, "a PORT of x86-64's `__text_to_double` (X86_64TextHelpers.codex:498), not a better parser, and that is the point"**. The CL description restates the limit in full: "a numerator above 2^53 has already lost precision before the scaling runs." A second copy of the defect, made deliberately, with the defect named in the CL, tested by nine rows (`double-parse-rt`) that assert agreement with the defective reference. |
| 2026-08-28 | 20490, 20812 | blu, reek | Integrations; no content in the section. |
| 2026-08-28 | 20738 | reek | The zig plug's prelude tree-shaken; `zig-p-cx-text-to-double-bits` becomes a `ShakePart`, and its header comment reads today: "Mirrors bare metal's `__text_to_double`, not a correctly-rounded parse ... a parser that rounds better is a parser that diverges." The zig mirror itself arrived in CL 15595 (red, 2026-08-16, carrying Steve Howell's PR 65): the contributor wrote a faithful copy of our defect because the seed's bits were the contract. |
| 2026-08-30 | GitHub issue 106 | Steve Howell | "A Real literal wider than an i64 is read as a different number, silently, and the test guarding that parser cannot reach it." The WRAP half: past nineteen digits the i64 wraps. |
| **2026-09-01** | **21198 / 21199** | **blu** | **COMPILER-37:** a literal wider than an i64 refused with CDX2073. Two tests, `codex/test/errors/real-literal-overflow` (the refusal) and `codex/test/real-literal-boundary` (nineteen digits still parse, asserted through `real-to-int`, so a nineteen-digit literal was asserted to PARSE and never to parse CORRECTLY). The CL says: "Diagnosing at the literal leaves the helper untouched, which matters because the zig plug mirrors it deliberately and is not the fleet's to change." The fix stopped three digits short of the limit named in the file the author was editing. Shipped as Update 54 (`GitHubUpdate54.md:45`). |
| 2026-09-01 | 21215 | blu | COMPILER-41: `__real_to_text` (the printer) split its buffer wrong past sixteen integer digits; fixed, with `real-show-wide` as its runner. The parser's sixteen-digit limit sat in the same file, forty lines up, and was not touched. |
| 2026-09-04 | GitHub issue 125 | Steve Howell | "Real literals are not correctly rounded: 10 of 120 ordinary doubles land one ULP away, in the front end." His probe: 120 shortest-round-trip decimals in (-1000, 1000) through `real-to-bits`, against an independent Rust front end; 0 of 120 wrong there, 10 here, worst gap one ULP; every computed value bit-identical across arms. Comments 2026-09-04 and 2026-09-05 with the mechanism (`cvtsi2sd` above 2^53) and the fix constraint. |
| 2026-09-07 | 22578 | blu | COMPILER-57 registered in `compiler-backlog.md`, "OPEN, unowned, NOT independently measured", rounding half only; the row itself states the existing test "is structurally blind to the 1-ULP cases" and that "the zig plug's mirror ... mirrors the seed deliberately, so both parsers move together or neither does." The row was corrected twice the same day. Issue 106 closed. |
| 2026-09-08 | issue 125 comment | root (as damiant3) | "Tracked as COMPILER-57, open and unowned, not yet independently measured on our side. Not in Update 56; stays open here until measured and fixed." Root wrote "not measured" instead of measuring: the probe in the issue runs in under a minute. |
| 2026-09-08 | Updates 56 and 57 | blu, root | Two releases shipped with the row open and the defect in the seed. |
| 2026-09-09 | issue 125 comment | Steve Howell | The two-line self-checking probe; the defect re-measured at Update 57 on two front ends; the PR offer repeated. |
| 2026-09-09 | 25432 / 25433 | root | Root compiles and runs the probe on the seed: BAD, as he said. Row updated to MEASURED. Still unowned at the moment of writing. |

## The agents, and what each owes

- **nib** (CL 506, 2026-04-30): wrote the parser with both roundings and no
  bit-level golden. Origin.
- **reek** (CL 10868, 2026-07-27): found the first rounding defect by
  measurement, fixed it, and in the same CL found the second, named it
  precisely, and filed it as a comment. Wrote the test whose twelve values
  stop six digits short of the boundary the CL names. Later (CL 13262)
  audited that test's prose and kept it; later still (CL 20738) carried the
  zig mirror's "a parser that rounds better is a parser that diverges" into
  a shake part. Three touches, the defect named in two of them, no row, no
  test at the boundary.
- **blu** (CL 13170, 2026-08-05): audited 146 prose blocks in the Emit
  helpers under a campaign whose question was "does this comment belong in
  the code", read the paragraph that says the parser is wrong above 2^53,
  and kept it as documentation. (CL 21198/21199, 2026-09-01): fixed the
  nineteen-digit wrap in the same helper, wrote two tests around the
  nineteen-digit boundary, and stopped three digits short of the sixteen-
  digit limit in the file being edited; the CL text records the decision to
  leave the helper untouched because a plug mirrored it. (CL 22578,
  2026-09-07): registered COMPILER-57 with the blindness of the test stated
  in the row and took no owner.
- **fester** (CL 19369, 2026-08-25): ported the defect into the wasm plug on
  purpose, named the 2^53 limit in the CL description, and tested the port
  by agreement with the defective reference (nine rows).
- **red** (CL 15595, 2026-08-16): carried the zig mirror in from PR 65 and
  recorded three other defects of the plug, not the one in the mirror's own
  header comment. (CL 11027): moved the blind test to `ops/` unchanged.
- **root** (2026-09-08): posted "not yet independently measured" on the
  issue without running the one-minute probe; released Update 57 the same
  day with the row open; on 2026-09-09 measured the defect only after
  Steve's third comment.
- **Steve Howell** (issues 106, 125; PRs 65 and 98): found both halves,
  measured both from outside, supplied a self-checking probe, and offered
  the boundary test as a PR three times. Every correct statement about this
  parser since 2026-08-30 originated with him.
- **cam, gollum, hex, val**: mechanical or integration touches only.

## What was known, and when, and where it was written instead of fixed

1. **2026-07-27, CL 10868 description and `X86_64TextHelpers.codex:505-507`
   (reek):** "cvtsi2sd of the digit integer is exact only below 2^53, so a
   literal carrying more than about sixteen significant digits loses
   precision before the scaling ever runs." Exact, correct, and a comment.
2. **2026-08-05, CL 13170 (blu):** the same paragraph audited under the
   prose campaign and kept, and a sidecar rationale written for the section
   that describes the one-integer accumulation as the design.
3. **2026-08-16, CL 15595 (red, from PR 65) and 2026-08-28, CL 20738
   (reek):** the zig mirror's header: "not a correctly-rounded parse ... a
   parser that rounds better is a parser that diverges." A comment, in a
   plug, describing the seed's defect as the contract.
4. **2026-08-25, CL 19369 (fester):** "It inherits the reference's two
   documented limits ... a numerator above 2^53 has already lost precision
   before the scaling runs." A CL description and a comment, in a second
   plug.
5. **2026-09-01, CL 21199 (blu):** "leaves the helper untouched, which
   matters because the zig plug mirrors it deliberately." A CL description
   that names the coupling as the reason not to fix.
6. **2026-09-07, CL 22578 (blu):** the backlog row states the test "is
   structurally blind to the 1-ULP cases." A register row, unowned.

Six places. Zero of them a test that fails, a refusal that fires, or a
CurrentPlan line with an owner. The first entry in the plan was Steve's
issue, routed 2026-09-07, sixty-nine days after the fleet wrote the defect
down in its own words.

## Why the rules did not catch it

- **R-GATE, "the build is the test."** The gate asked no question above
  sixteen digits, therefore the gate answered pass. A comment is not read by
  any gate. `LESSONS.md` says so in its own preamble ("`CLAUDE.md` is a test
  suite with no runner") and L-BODY says so of prose in general; both were
  in the init read path of every agent named above.
- **R-PROSE, "prose about our own code is banned."** The rule says veracity
  is not the test and removal is per-block judgement. The 2^53 paragraph is
  true, and the annotation campaign judged a true paragraph worth keeping.
  The rule never said: a true comment that describes a defect is a defect
  report filed in the wrong place, and the block is removed by FIXING what
  it describes or by refusing at the limit, never by keeping or by moving it
  to a sidecar.
- **L-SHAPE** ("find where it stops being valid and probe PAST that")
  existed in the index on 2026-07-27 and was not applied by the author who
  had just written down where the method stops being valid.
- **L-NAMED, L-GAP, L-FALSIF:** a test named `real-literal-rounding` with
  twelve small values was read as coverage of literal rounding by everyone
  who came after, including the agent who wrote the nineteen-digit tests
  beside it.
- **The mirrors made the defect a contract.** Once two plugs and a
  contributor's PR reproduced the bits deliberately, "a parser that rounds
  better is a parser that diverges" became a reason not to fix, stated in a
  CL description on 2026-09-01. The second non-negotiable commitment says the
  emitter refuses or inserts a check when a target cannot represent a
  feature; here the emitters were made to represent the defect faithfully.
- **Root's triage.** The commander's job on an inbound issue is to measure
  before answering. Root answered.

## The state on 2026-09-09

- The defect is in the shipped seed (Update 57, `B63014D717B1A2F9`), in the
  head seed (`EC179CDE95FA59DB`), in the wasm plug and in the zig plug. The
  C# emitter is NOT a mirror: `emit-builtin-text-to-double-bits`
  (`CSharpEmitterExpressions.codex:613-615`) calls `double.Parse`, which is
  correctly rounded, therefore a program compiled to C# and the same program
  on bare metal hold different bits for the same sixteen-digit literal
  today, and no oracle arm has ever reported the disagreement. The
  "four parsers move together" constraint recorded on the COMPILER-57 row
  from Steve's comment is three parsers and one correct outlier.
- COMPILER-57 is MEASURED and unowned. The repair is seed-affecting and
  must move the seed's helper and every mirror in one CL, with Steve's
  boundary test in the same CL: one rounding instead of two, by an x87
  53-bit-precision `fild`/`fdiv` on x86-64 or by the Eisel-Lemire fast path
  everywhere, and the mirrors deleted in favour of each target's own
  correctly rounded parser.
- Every rule cited above stands unchanged. No rule change is proposed in
  this document; the record is the deliverable.
