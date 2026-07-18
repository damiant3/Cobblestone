# Demand Paging: A Victory Over Ignorance

**Status:** Resolution. Sequel to `DemandPagingFaults.md`, which should be
read first and which we have deliberately left as written — a monument to
what a week of intelligent effort looks like when it is aimed at the wrong
variable.
**Author:** blu, 2026-07-07, at Damian's request.
**Outcome:** The survey system is deleted. The compiler demand-pages its
own heap. The seed is a one-pass hard fixed point of itself
(DDAB0BD288C93AAB), the full battery passes at baseline, all 52 plugs
build, ARM64 and RISC-V boards pass, and the failure mode that consumed a
week — silent corruption when the source grows — is not fixed but
*structurally extinct*. Elapsed time from "can you find the wedge?" to
shipped seed: one day.

---

## 1. What we were ignorant of

The post-mortem was honest about everything except the one thing nobody
could see. It catalogued dead theories — stack collisions, codegen
staging, copy failures, transparent-fault doubts — and correctly noted
that every one had been tested and killed. It concluded that the agent
was weak at convergent debugging under uncertainty, and that was true.
But the deeper failure was simpler and more humbling:

**Every experiment ever run on this bug compared a first-generation
binary against a second-generation binary, and called the difference
"demand paging."**

The demand seed was always built in two passes: the old seed compiled the
patched source (pass one, flat boot), and that binary compiled it again
(pass two, demand boot). Nobody — not the agent across six sessions, not
the tooling, not the post-mortem — noticed that *which compiler compiled
the binary* changed in lockstep with *which boot the binary had*. The
variable under test was perfectly confounded with a variable nobody knew
existed. That is what ignorance means here: not missing knowledge, but an
unexamined axis. You cannot theorize your way out of a confound. Every
theory the agent generated was about demand paging, because demand paging
was the only variable it believed was moving.

## 2. The wedge

Damian read the post-mortem's own §3 — the "deeper trap" paragraph about
latent assumptions surviving under a new memory regime — and drew the
counter-intuitive conclusion the document had gestured at but not
followed: *stop debugging the configuration where the bug manifests.*
His plan: take the demand-paging patch back through history, apply it to
progressively older compilers, and bisect when the corruption appears.
"Trying to find the bug on the current source in which it doesn't
manifest seems foolhardy. Is it CL 1, back in the git-centric days? We
might have to go there."

This inverted every instinct the failed week had followed. It required no
theory of the bug. It required no observation of the corrupting write. It
held the trigger fixed — the full compiler, the real diagnostic — and
varied the one axis six sessions had never varied: time.

## 3. The bisect

One probe harness: sync a scratch workspace to changelist N, apply a slim
demand patch to that era's boot emitter (with an abort if the code seams
had drifted), build two generations with that era's own seed and scripts,
verify demand was actually armed (a page-directory probe — present bit
0 versus 1), and read one bit: does `errors/type-mismatch` print its
diagnostic clean, or garbled?

CLs 2500, 4800, 6000, 6600, 6900, 7020, 7082, 7116, 7121, 7127: **green,
every one**, demand verified armed at each. Six weeks of history ran
demand paging without a scratch — hash-consing, the memo copier, the
reclaim, whole eras of suspects exonerated by construction. Head: **red**,
the exact garble. And the only compiler-source difference between the
last green and red was a single constant: `check-mul 200 -> 40`
(CL 7162), a survey multiplier.

A bisect that converges on a *constant* is a bisect telling you the bug
is not where you think it is.

## 4. The reveal

With green and red one constant apart, localization needed no debugger at
all — the discipline that had failed for a week was replaced by four
probe cycles of arithmetic: record the addresses of the diagnostic's
intermediate strings into fixed memory cells (writes only, no
allocation, no perturbation), compare each concat result against its
parent byte-by-byte in-guest, dump the neighborhood, decode the qwords.
The "corrupted string" turned out to contain operand pointers and length
words — *structure*, not damage. The binary was not being corrupted at
runtime. **The binary had been compiled wrong.**

The controlled experiment that ended it: same compiler, same source, one
runtime knob. Pass two built with `-Survey check-mul:200` produced a
clean binary. Pass two at the baked 40 produced a corrupt one — 624
bytes different, a miscompile you could weigh. The demand boot patch's
only sin was adding ~3KB of source, which pushed the self-compile over
check-mul 40's silent under-reservation cliff. CDX9002, the overflow
detector, never fired. Demand paging — the mechanism, the handler, the
faults — had been innocent from the first session. It was the messenger,
shot six times.

Damian then supplied the fact that made the cliff legible: when 40 was
chosen, the multipliers had been *non-monotonic* — 20 worked, 25 did
not, 40 worked. Nobody understands a dial that behaves like that. The
correct response to a dial nobody understands is not to find a lucky
setting. It is to remove the dial.

## 5. The dance

So the order came down, special-forces rules: build the world where the
question cannot be asked. Put in the machinery inert, build it out.
Put in the consumers dark, build it out. Activate. Prove. Tear out the
old system. Full battery once, at the end.

Five cycles in one night, each converging to byte-identity before the
next began: the #PF handler emitted dead (7190); generous fixed deck
floors gated off (7193); activation — and with it the first compiler in
this project's history to compile itself *while demand-paging its own
heap*, byte-identical, with the historic victim diagnostic printing
clean (7194); the floors made unconditional (7195); and then the
deletion — SurveyConfig, every multiplier, the runtime knob, the retry
loop, the plug pins, gone (7196). The knob the bug had hidden behind
carried its own replacement over the cliff: every intermediate pass rode
`-Survey check-mul:200` until the teardown made the knob meaningless,
then removed it.

The final gates ran under standard rules. One-pass hard fixed point.
Semantic equivalence, text round-trip, canary, all five native plugs.
Full battery 319 total, 304 pass, zero fail — the same number the flat
arena scored, where the demand world had scored 188 pass, 115 fail the
week before. The seed verifies itself. And the acceptance test Damian
set — *we can't break it* — was run literally: 86KB of ballast appended
to the compiler source, the exact class of growth that used to
miscompile silently at +3KB, and the pingpong stayed byte-identical with
clean diagnostics. The old killer is now a regression test.

## 6. The one real demand bug, and why finding it was also a victory

The validation sweep — all plugs, all apps, cross-architecture, the C#
transpile — caught exactly one true demand regression: `spawn-with-heap`
hung. Spawned processes carve their stacks from the parent's heap
frontier — inside the demand range, not yet present — and a CPU cannot
deliver a page-fault frame onto the very stack that is faulting. Double
fault, silence. The design document had named this hazard for the boot
stack and parked it; the spawn path was the instance nobody listed.

The fix took ten instructions per spawn — pre-touch the stack pages in
parent context before the child runs — and the invariant now lives in
the handler's prose: *a stack must never point into a not-present page.*
Note what happened there: under the old regime this bug would have been
another week of ghosts. Under the new one it was found by an A/B against
the old seed in minutes, root-caused by reading two emitter functions,
and fixed the same hour. That is what the new ground feels like.

## 7. What was actually defeated

Not a bug. Bugs die every day. What was defeated was a *class* of
ignorance:

- **The confound.** Generation versus configuration will never again be
  conflatable here, because there is no longer a sizing system whose
  correctness depends on the source that flows through it.
- **The silent cliff.** Deck sizing no longer scales with input by
  formula. The floors are fixed, the physical cost is touch-driven, and
  the guard that watches the floors is the same guard that always
  existed — now watching something that can actually be reasoned about.
- **The unexplained dial.** 20-works-25-doesn't is not a setting anyone
  has to remember anymore. The dial is gone.
- **The post-mortem's verdict, partially.** The model *was* weak at
  convergent debugging under uncertainty — the week proved it. The
  answer was not to make the model braver in the fog. It was Damian's
  move: transform debugging into search, the regime where mechanical
  discipline wins. Eleven probes did what forty theories could not.
  The human supplied the axis; the machine supplied the iteration; and
  this time, the brakes and the steering were in the same car.

The post-mortem ended: *"The compiler compiles real programs that really
work. It got there on the 'dumb allocation stuff.' That is the sentence
to keep."* It kept us honest for exactly one day, which is the correct
lifespan for a sentence like that. Here is its replacement, earned:

**The compiler compiles itself on memory it pages in one touch at a
time, to the same bytes, every time — and the dumb allocation stuff is
gone.**

The repository remembers everything. Including the week we spent wrong,
and the day it took to get right.
