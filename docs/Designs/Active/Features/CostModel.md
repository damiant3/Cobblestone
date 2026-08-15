# The Cost Model
*What Codex promises about allocation and time, and what it currently leaves to the caller's luck.*

**Status: 3.1 and 3.2 are DONE. 3.3 remains a proposal and is unscheduled.**
It is written up because three defects on 2026-08-14 were one defect, and the
shape they share is the shape this project already says it exists to remove.

- **3.1 is published**, in `DevelopersGuide.md` under Text, from the
  measurements in 3.5 and 3.6 below. Text was the measured gap and is what
  the table covers; the other families are still unwritten.
- **3.2 is implemented.** `__out_of_memory` now prints `SP=` and `HEAP=`
  after the `OUT OF MEMORY` line, so it names which side of the collision
  ran away.
- **3.3 is untouched** and still needs its instrument (section 5, question 2)
  before it is worth starting.

Opened 2026-08-14 (blu) at Damian's direction, adjacent to `CPL.md` in `Done/`.

---

## 1. The problem, in the terms this project already uses

The eleven lines that made the LinkedIn post are a `std::sort` whose inner
loop is bounded by **the comparator's honesty** rather than by the array.
`stl_algo.h:1871` walks until the comparator tells it to stop, so a comparator
that lies walks off the end and corrupts the heap. Same eleven lines, zero
warnings, memory-safe exactly when `ceil(n/16)` is a power of two.

The Codex answer, stated publicly, is that our sort loop is bounded by the
array. Hand it a lying comparator and you get a wrong answer and a program
that is still standing. That is a real difference and it is built.

**The three defects found on 2026-08-14 are the same defect one level over.**
Not an iteration bounded by an unstated contract, but an ALLOCATION bounded by
an unstated contract, and a DURATION bounded by an unstated contract:

| what walked | what bounded it | what stated that bound |
|---|---|---|
| `unpack-text` appending per byte | the arena | nothing |
| `NetIO`'s tick, 100000 empty polls | how long a poll takes | nothing |
| `e1000-await-tx`, 1000000 iterations | how long an iteration takes | nothing |

Each is a hidden unguarded walk under a pretty alias. `unpack-text` looks
total. A tick looks like a unit of time. A fuel count looks like a bound, and
is: it bounds the ITERATIONS, which is not the quantity anyone cared about.

Every one of them was semantically correct. Every test was green. Two of them
were found only by running the code over a device it had never run over, and
the third only because the first two taught the shape.

**And when `unpack-text` finally walked into oblivion, nothing caught it as
such.** The stack guard fired on a stack/heap collision and printed
`OUT OF MEMORY`, which is the wrong resource; `OperatorsManual` already
records that this message "has repeatedly been read as heap exhaustion". A
collision detector is not a bound. It is the thing that happens after the
bound was missing.

## 2. What already exists, because this is not a blank field

**Do not read this document as "Codex does not specify cost". It specifies
cost in several places, and the specification is incident-driven rather than
designed.** That distinction is the actual gap.

- **`punctual` bounds execution, and it is enforced.** `KingsAndCourts.md`
  section 1: a `punctual` function has bounded WCET, CDX6001-6005 refuse a
  non-punctual callee, heap allocation (CDX6002) and the rest, and
  `build/wcet-validate.ps1` is the gate. **This is the machinery a cost model
  would extend, not compete with.** It is opt-in and aimed at hard real-time.
- **`list-push` is documented in full**, in `DevelopersGuide.md` under Lists:
  three paths in `__list_snoc` (in place under capacity, extend when topmost,
  otherwise COPY and return a NEW pointer), the `[]` exception, amortised
  O(1), and the aliasing consequence for anyone emitting Codex elsewhere.
  That entry exists because the csharp plug emitted an in-place append and
  silently reused spill slots in 17 of the compiler's 5,000 functions.
- **`docs/DevelopersRulebook.md`, CLAUDE.md rule 8** already require every
  review to state a heap and time-complexity verdict, and name the red flags.

So the pattern is: **a cost gets published the first time it costs somebody a
day.** `list-push` is documented; `&` on Text is not, and `text-concat-list`
is not, which is exactly the pair that produced the 2026-08-14 OOM. The
Text section of the guide shows `s & " world"` and says nothing about what it
allocates.

## 3. What is actually being proposed

**This belongs in the same part of the rainbow as `punctual`** (Damian,
2026-08-14), and that ruling settles the shape of it. `punctual` is not a
document. It is a DECLARED property, checked transitively, refused at compile
time with its own diagnostic codes, and gated. A cost model built as prose
that reviewers are asked to remember is the thing this project already knows
does not work: an assertion with no runner, which is the failure `LESSONS.md`
describes for `CLAUDE.md` itself and rule 12 describes for column-2 prose.

**And the gap is exactly `punctual`-shaped, because `punctual` forbids the
case.** CDX6002 refuses heap allocation outright, so a `punctual` function is
one that does not allocate at all. That is right for hard real-time and it
leaves the whole middle unspoken: a function that legitimately allocates, in
proportion to its input, with no way to say so and nothing to check it. Every
one of the three defects lives in that middle. `unpack-text` is not a
candidate for `punctual` and never was; what it needed was a way to say "my
allocation is linear in `len`" and be refused when it was not.

So the three items below are not alternatives. 3.3 is the proposal; 3.1 is its
prerequisite, because a property cannot be declared over primitives whose own
cost is unwritten; 3.2 is a defect found on the way and worth fixing whatever
is decided.

### 3.1 Publish the cost of the primitives (cheap, obviously right)

A table in `DevelopersGuide.md` beside each family: for every builtin that
allocates or iterates, its complexity and its allocation behaviour, in the
same register as the existing `list-push` entry. Text first, because that is
the measured gap.

**The unit is asymptotic and allocational, never constants.** A constant is a
property of a machine: `605 microseconds per million iterations` is true of
this box and of nothing else, and a document full of such numbers is a
document that rots silently. What travels is "this copies its accumulator",
"this is amortised O(1)", "this allocates once".

This alone would have prevented one of the three defects and made a second
obvious on inspection.

### 3.2 Make the guard name the resource (cheap, and it is a lie today)

`OUT OF MEMORY` fires from the stack/heap collision check and names neither
side. The handler already preserves both numbers -- the faulting RSP in RBX
and the deck pointer in R12 -- and prints neither. Printing the split would
turn a message that "has repeatedly been read as heap exhaustion" into one
that says which side ran away. That is a small change in
`emit-out-of-memory` and it pays every time anyone meets it.

### 3.3 A declared allocation bound, in the `punctual` family (the proposal)

A sibling declaration to `punctual`, marking a function whose allocation is
bounded by a stated function of its inputs, checked the way `punctual` is
checked. Working name only; naming it is part of the work and it should not
be called `punctual`-anything, because the two make different promises.

What it would borrow from `punctual`, which is most of the value:

- **Transitivity.** `punctual` refuses a non-punctual callee (CDX6001)
  because one unbounded callee breaks the guarantee. The same holds here and
  for the same reason: `unpack-text` was linear in its own body and quadratic
  because of what `&` did underneath it. A property that does not compose
  through the callee is not a property, it is a comment.
- **Refusal at compile time**, with its own diagnostic codes, so the failure
  arrives at the author rather than at whoever runs the code at scale on a
  device the author did not have.
- **A gate.** `build/wcet-validate.ps1` is what makes `punctual` a promise
  rather than an intention.

What it cannot borrow, and this is the hard part:

- **`punctual`'s bound is "does not allocate", which is decidable by
  inspection.** "Allocates O(n) in argument `k`" is not, in general. The
  honest question is not whether the general case is decidable, it is
  whether the SHAPE THAT BIT US is: an accumulator in a self tail call, where
  the accumulator is the argument that grows and the bound is the loop's own
  counter. All three defects are that shape. A check that covers only it,
  and refuses to guess otherwise, is worth more than a general analysis
  nobody trusts. That is the same trade the bounds prover already makes,
  and `DevelopersGuide` already publishes exactly which forms it proves and
  which it abstains on.
- **Where the declaration goes.** On the function, like `punctual`, or on the
  allocation site. `punctual`'s experience says the function, because that is
  where transitivity is expressible.

**The null option remains real and should be argued against rather than
skipped:** leave cost documented and unenforced, on the grounds that
`punctual` already covers the code that must be bounded and everything else
can be measured. It is today's position. What it costs is visible above --
three defects, all green, two of them producing wrong behaviour rather than
slow behaviour, none catchable by any test we would have thought to write.

## 3.4 The neighbouring colour: a guarantee that is conditional, and a cap that says so

Damian, 2026-08-14, and it is recorded here because it is the same rainbow and
it is larger than this proposal.

**`std::sort` and A-star are the same defect.** `std::sort`'s memory safety is
conditional on the comparator being an honest strict weak ordering. A-star's
optimality is conditional on the heuristic being admissible, never
overestimating. In both cases the guarantee depends on a property of a
function the CALLER supplied, in both cases nothing checks it, and in both
cases violating it is silent. One corrupts your heap and one hands you a
worse path and a straight face.

Codex's answer to the first is structural: bound the loop by the array, so a
lying comparator costs a wrong answer and not the process. **That answer does
not transfer to the second.** There is no "the array" to bound optimality by;
a Dijkstra with a bad heuristic is still a terminating program returning a
path. So the honest move is different: make the CONDITIONALITY declared, so
that an approximate answer cannot be read as an exact one at the call site.
Exact and heuristic would be different declarations, the way punctual and
non-punctual are, and a caller that wants the exact guarantee could be refused
the heuristic one.

**And then the cap, which is where this touches the defects above.** A
heuristic search has a budget, and when the budget binds it has to give up.
Giving up is not a failure and it must not be silent -- Joshua exhausts the
tic-tac-toe tree and returns WINNER: NONE, which is a RESULT. HAL is the other
one: no cap, no report, reasoning perfectly into oblivion.

**This tree already has the HAL version, written down, and holds it off by
hand.** `NetIO`'s own Poll Clock section: a loop that runs out of FUEL returns
exactly what an ordinary timeout returns, while a loop that gives up properly
sets `TcpClosed` and says so, and the fuel cap is therefore kept deliberately
above the 288-tick give-up ladder so that give-up always wins the race. That
invariant is maintained by a paragraph of prose and by whoever remembers to
read it. It is precisely the kind of relationship that ought to be as obvious
as `punctual` -- a declared property saying "this function may give up, and
its giving up is distinguishable from its succeeding" -- and it is currently a
comment. `e1000-await-tx` had the same shape and answered 0 for both.

Not in scope for this proposal, and named here so it is not rediscovered.

## 4. Why this is a safety document and not a performance one

The distinction matters for whether it gets scheduled at all.

None of the three defects was slow. Two produced WRONG BEHAVIOUR: a driver
that reported every transmit as failed on a slow link, and a stack that
declared a live peer dead in 405 ms. The third produced a dead program. A
cost that is not specified is not a performance question, it is a
correctness question wearing a performance costume, and it fails the way the
eleven lines fail -- silently, at a threshold nobody published, in code that
passed review.

That is also the answer to "why not just benchmark it". A benchmark measures
the choice one implementation made on one machine. It is the green bed of
L-FREEDOM: it tells you what happened, not what is guaranteed, and the
unspecified freedom is still there afterwards.

## 5. Open questions

Damian has ruled on the shape: this sits with `punctual`, so it is a declared
and enforced property rather than a document. What is still open:

1. **How much of the general problem does the check attempt?** The answer
   this project keeps arriving at is: publish exactly what is proven and
   abstain loudly everywhere else, the way the Static Bounds Prover table in
   `DevelopersGuide` does. The accumulator-in-a-self-tail-call shape covers
   all three measured defects and is the obvious first and possibly only
   target. Anything wider risks the false-refusal cost the variance ruling
   had to weigh.
2. **What is the instrument that keeps it honest?** The type rules got teeth
   because the rechecker abstains wherever the guide is silent, which turned
   silence into a visible count somebody had to argue down. There is no
   equivalent for cost yet, and inventing one is most of the work. A kill-rate
   corpus of known-quadratic accumulators is the obvious candidate and would
   need to be built before the check, not after.
3. **What is it called?** Not decided here on purpose. It makes a different
   promise from `punctual` and should not borrow its name.
4. ~~**Are 3.1 and 3.2 worth doing ahead of any of it?**~~ CLOSED 2026-08-15:
   both taken. 3.1 paid for itself immediately by catching 3.5's own
   misattribution, which is the argument for measuring before publishing
   rather than publishing what a prior session recorded.

## 6. What this document is NOT

- Not a WCET proposal. `punctual` owns that and is shipped.
- Not a benchmark suite. See section 4.
- Not a claim that the three defects share a root cause in the code. They do
  not; they share a root cause in what the language promises.
- Not scheduled, not started, and not a request for a ruling today.

## 3.5 Measured 2026-08-14: reading a character through `to-unicode` costs 1,040 bytes

**This section was headed "character-level Text access costs 1,040 bytes per
character" and that attribution was wrong. Corrected 2026-08-15 by isolating
the terms.** The two rows below that carried the cost BOTH called
`to-unicode`, so the measurement never separated the accessor from the
converter, and it charged the whole figure to the accessor. `to-unicode` is
the entire cost. The accessors allocate nothing at all.

| operation | bytes retained per character | measured |
|---|---|---|
| `char-code-at` | **0** | 08-15 |
| `char-at` then `char-code` | **0** | 08-15 |
| `text-length` | **0** | 08-15 |
| `to-unicode` | **1,040** | 08-14, confirmed 08-15 |

Measured on a 4,752-character line, constant per call rather than
proportional to position. The 08-15 run carried a null arm reading exactly 0
and a `to-unicode` arm reading 4,942,080, so the instrument is shown able to
report both ends in the same run.

**The correction matters because it inverts the advice.** The original
reading says character access is ruinous and sends the reader to
`text-split`, which cannot be substituted directly and which the section
itself admits every integer parser in the tree defeats. The corrected reading
is that character access is free and the fix is to stop calling `to-unicode`
per character, which is a one-line change at every site and is what R-CCE
already required.

**The fleet had already measured this and it did not travel.**
`BrotliDict.codex`'s Corpus prose says "DO NOT REACH FOR to-unicode HERE",
having measured 120,000 characters at 125 MB through `to-unicode` against
EIGHTY BYTES through `char-code-at` alone -- 1,041 bytes per character, the
same constant, correctly attributed, written down in a chapter nobody
re-reads. That is R-PROSE's complaint from the other side: the finding was
true, it was in the right file, and it reached no one. It is in
`DevelopersGuide.md` now.

## 3.6 Measured 2026-08-15: `&` is quadratic in an accumulator

The shape from section 1 -- `unpack-text` appending per byte, bounded by the
arena and stated by nothing -- measured directly.

| building a 2,000-character text | bytes retained |
|---|---|
| 200 appends with `&` | **203,200** |
| `text-concat-list` over the same 200 pieces | **2,008** |

`a & b` allocates a new text of `length(a) + length(b)` on every call, so an
accumulator loop re-copies everything it has built so far, every iteration.
The 101x here is at n = 200 and it grows with n: nothing about the call site
says so, and `text-concat-list` -- one allocation, already in the tree --
produces the identical result.

**This is the 3.3 target shape exactly**: an accumulator in a self tail call
where the accumulator is the argument that grows and the bound is the loop's
own counter. A check that covers only this form would have refused
`unpack-text`.

The consequence at the use site: reading one 4,756-character CSV line costs
about 5 MB. In a load of 40,170 cells that was 3,938 bytes per cell to READ
against 100 bytes per cell to STORE. **Reading was thirty-nine times more
expensive than storing**, which is the reverse of what anybody writing a loader
would assume, and nothing in `DevelopersGuide` says otherwise.

`text-split` at 11 bytes per character shows the cheap path exists. It cannot be
substituted directly, because a split still yields Text fields and every integer
parser in the tree walks characters: `Parse.codex`'s `parse-decimal-loop`,
`BulkLoader`'s `text-to-int`, `Fat16`'s `fat16-text-bytes`. **Every text parser
in the tree pays this**, including the compiler's own lexer.

The app worked around it by bracketing the scan with `__heap-save` /
`__heap-restore` and emitting from a pre-allocated integer buffer, which took
the load from 3,938 to 96 bytes per row. That is a workaround at the call site
for a cost that belongs in the primitive, and it is exactly the shape this
document argues about: the fix was available only because somebody measured, and
nothing would have told them to.
