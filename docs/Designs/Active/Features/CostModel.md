# The Cost Model
*What Codex promises about allocation and time, and what it currently leaves to the caller's luck.*

**Status: 3.1, 3.2 and 3.3 are DONE. 3.3 shipped as the `bounded` declaration
(main 16020), with rule 3 of its inference at 16118.** It is a FIRST SLICE:
`linear` and `growing` are inferred and checked, while `none` and `fixed` are
named rungs the compiler refuses with CDX6103 rather than take on trust.
It is written up because three defects on 2026-08-14 were one defect, and the
shape they share is the shape this project already says it exists to remove.

- **3.1 is published**, in `DevelopersGuide.md` under Text, from the
  measurements in 3.5 and 3.6 below. Text was the measured gap and is what
  the table covers; the other families are still unwritten.
- **3.2 is implemented.** `__out_of_memory` now prints `SP=` and `HEAP=`
  after the `OUT OF MEMORY` line, so it names which side of the collision
  ran away.
- **3.3 is shipped**, declaration and check both. Its INSTRUMENT was built
  first (2026-08-16, section 7): the kill-rate corpus that question 2 said had
  to exist before the check. The check then landed against it -- `bounded` with
  a transitive refusal at main 16020, rule 3 of the `growing` inference at
  16118, and `none`/`fixed` refused with CDX6103 rather than taken on trust.
  The `growing` inference scores 10 of 11 on
  `codex/test/cost/accumulator-corpus`, 5 of 5 on the quadratic half, table in
  section 8b. What remains open is COMPILER-7: whether the one over-refusal
  (`n-fixed-appends`, linear but shaped quadratic) is worth lifting, revisited
  only once real chapters carry `bounded` declarations.

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

## 5. What ships: `bounded`, and the lattice

**The declaration is a CLASS, never a function of the inputs** (Damian,
2026-08-16). It is a CEILING in a small lattice, and the compiler infers each
function's class bottom-up from its body, the way effect sets are inferred:

```
  none  <  fixed  <  budgeted  <  linear  <  growing
```

- `none` -- no heap.
- `fixed` -- the same bytes every call, whatever the input.
- `budgeted` -- bounded by something nobody wrote down: `substring line 0 4`
  by the literal it is passed, `integer-to-text` by the twenty digits an
  `Integer` has. Neither is `fixed`, because the bytes are not the same every
  call, and calling either `linear` promises something about the input that is
  false.
- `linear` -- one walk over an input, no copies of the accumulator.
- `growing` -- the accumulator copied inside a loop, or a walk nested in a
  walk.

The class is the worst case over the code's STRUCTURE and is blind to data on
purpose: bubble sort is honestly `fixed` in heap whatever the data does to its
time, and a walk nested in a walk is `growing` even when the data would keep
it small. Quadratic TIME is a WCET question and stays with `punctual`'s budget
number. Refusal is transitive exactly as CDX6001: a declared ceiling breaks at
the caller, at compile time, when a callee's inferred class exceeds it. No
declaration means no check, as with `punctual`; the bottom rung is `punctual`
minus the WCET budget and the effect ban, which is why the two felt like
siblings.

**Abstain toward refusal.** The false-refusal cost is paid in a declaration
the author can choose not to write; a missed `growing` is the defect this
whole document exists for. An unchecked promise reads exactly like a checked
one, which is worse than no promise.

The syntax sits in the same slot as `punctual`, followed by the class:

```
  bounded linear unpack-text : Bytes -> Text
  unpack-text (bs) = unpack-go bs 0 ""

  unpack-go (bs) (i) (acc) =
    if i == list-length bs then acc
    else unpack-go bs (i + 1) (acc & byte-to-text (list-at bs i))
```

which is refused with the site named:

```
  CDX6101 unpack-text declares bounded linear but calls unpack-go, inferred
          growing: argument acc is copied by & at Unpack.codex:14 inside a
          self tail call
```

All five rungs are inferred and checked. `bounded fixed` is decided by a
`strict` flag on the overbudget walk, and the whole of the difference between
`fixed` and `budgeted` is that a plain `budgeted` callee keeps a `budgeted`
promise and breaks a `fixed` one: `none`, `fixed` and `budgeted:N` with a
literal argument N keep it; `input`, `linear`, `growing` and `unknown` break
it.

### 5.1 The rules the checker rests on

- **A name in call position that is neither a definition in this unit nor a
  measured row is read as ALLOCATING.** That is abstain-toward-refusal at the
  only place it can be applied, and it is why an `unknown` registry row
  refuses one declaration at one call site rather than holding back a rung.
  Widening the zero-byte set is a MEASUREMENT, not a rule change.
- **A `budgeted:N` call is accepted when argument N is a LITERAL and refused
  otherwise.** Blunt on purpose: it also refuses a computed length that
  happens to be small, which is where the compiler cannot know.
- **`none` means NO HEAP, not no resource.** A spawn consumes a fixed-pool
  slot; slot exhaustion is a bounded resource this lattice does not describe,
  the same way quadratic TIME stays with `punctual`'s budget.
- **The registry is never consulted for a NULLARY builtin.**
  `cost-head-allocates` is reached from a call HEAD, and a bare name is not a
  call. Measure such rows anyway, because "no consumer asks" is not "the
  answer does not exist", but do not count them as widening a rung.
- **Do not infer a class from the code's shape alone.** A single `&` allocates
  in proportion to its operands, so a straight-line body with no loop is
  already not `fixed`, and a shape-only rule would accept exactly the case the
  class exists to exclude.

### 5.2 The registry, and the structural gap under it

`bs-alloc` on `BuiltinSpec` (`codex/compiler/Types/Builtins.codex`) carries
the class beside the name and the type, and `cost-builtin-nonalloc` reads
`builtin-alloc-by-name` rather than a hand-kept list, so a measurement widens
what `bounded none` accepts by editing one row.

**Measured 2026-09-07 off `Builtins.codex` at head, 265 rows: `none` 161,
`unknown` 80, `fixed` 14, `input` 8, `budgeted` 1, `budgeted:3` 1.**
Re-measure before quoting (L-COUNT); this moves a long way while nobody is
reading it. `unknown` is the refusing side, so every row still reading it is a
builtin read as allocating without bound.

**`bs-alloc` is keyed by builtin NAME, and that is a structural gap the
measurements do not close.** `show`'s allocation is a property of the TYPE it
is instantiated at: `emit-show-builtin` dispatches on the argument's type to
four emission paths with at least two allocation behaviours, and
`__real_to_text` is measured by nobody. `__list-tail`'s allocation happens in
`__list_tail` (`X86_64ListHelpers.codex:38`), a level below the registry,
which has no row for it. A per-name class cannot describe either, so rows like
these are correct today only by inspection of code no registry row covers.

**`build/check-builtin-alloc.ps1` is the runner for that gap.** It carries the
row-to-allocation-site table the registry does not, and refuses two things: an
allocation site in a pinned body that does not take an immediate, and any edit
to a pinned body at all. The second exists because the first is only as wide
as the spellings it knows (L-CENSUS), and it was controlled by sabotaging a
site with an allocation call the first rule has never heard of: rule 1 stayed
silent and rule 2 caught it alone. A row reading `fixed` with no recorded site
is refused by name rather than skipped (`vec4-select`, added 2026-09-10
reading `fixed` with no site, was refused by the Update 59 gate on
2026-09-12: it shares `emit-vec-select-builtin` with `vec-select`, whose one
allocation is `emit-bivy-alloc st9 16`, an immediate 16 bytes per call at
either width, because four f32 lanes and two f64 lanes are the same 16
bytes). The table is hand-written because
crawling the emitters pins `emit-expr`, which every builtin calls to evaluate
arguments, and a check that reds on unrelated codegen churn teaches people to
re-pin without looking. **Nothing runs it yet** (L-NOGATE); wiring it into the
gate means changing the generator under `codex/build/`.

**`bs-varies` must not be pressed into service here.** It is consumed by
`const-name-invariant` for constant-expression invariance, not by the cost
model, so it says nothing about allocation.

### 5.3 How a row is measured, and the traps that have bitten

- **AN ARM MUST VARY THE THING IT IS MEASURING.** `alloc-bytes`'s first arm
  passed the literal 64, duly read `fixed` at both sizes, and was measuring
  the arm: the only quantity that varied was the argument the caller chose.
  Varying it, the same builtin reads 64 against 256, exactly 4.0x, `input`.
- **The discriminator is INPUT SIZE, not iteration count.** Every arm makes
  one call and the two readings differ only in how large the argument is,
  which is the question `fixed` actually asks.
- **A NEW ARM SHAPE NEEDS ITS OWN CONTROL.** An effectful builtin cannot be
  bound with `let` (CDX2033), so its arm opens an `act` block and takes the
  answer with `<-`. A different bracket is a different instrument until
  something says otherwise, so the section opens with `act-control`: the same
  block, the same two marks, nothing measured inside. It reads 0, and only
  then is anything below it attributable.
- **A SCAN FOR `__alloc` AND `emit-bivy-alloc` IS TOO NARROW.**
  `chan-text-recv` rounds the received length up and does `add r10, rax`,
  advancing the bump allocator INLINE without calling either, so a scan for
  those two names would have published `none` on a builtin that allocates in
  proportion to a message. Any emitter classification must look for `reg-r10`
  advancement as well (L-CENSUS). A false `none` is a false promise, which is
  the dangerous direction.
- **Read the class four ways where the emitter branches on the value.**
  `emit-print-text-loop` branches on the code unit, so an ASCII-only probe
  never enters the multi-byte arm; the print rows are read ASCII, tier-0
  accented, tier-0 Cyrillic and tier-1 (L-CONSTRUCT).
- **A family that cannot be armed is classified from the EMITTER, and that is
  recorded rather than smoothed over.** The six proof terms lower through
  `emit-proof-builtin`, which is `emit-int-lit st 0`; the VMX/MSR and UEFI
  console rows would fault or need a firmware boot no test performs. A proof
  term also cannot close the `+ r - r` bracket that forces a result to be
  used, so an arm would be measuring dead-code elimination.
- **A ladder buys rows an arm at a time.** Allocation cannot be negative, so a
  chain reading EQUAL to a control containing a subset of it proves every
  added member is zero at once, which is how sixteen real-conversion rows came
  off eight arms.

### 5.4 What is deliberately still `unknown`, named rather than silent

The GPU four (`gpu-in`, `gpu-out`, `gpu-mem-read`, `gpu-mem-write`): an arm
for `gpu-mem-write` at offset 0 of the window writes over `DeviceBuffer`'s
allocation cursor, which is corrupting an allocator in order to measure one.
The two 16-bit port block forms and `runtime-init`. `chan-text-recv`, because
what it retains is proportional to the MESSAGE and the message size appears in
no argument, so no rung in this lattice describes it. `process-get-scope` and
`process-get-network-scope` return Text and both answer the EMPTY string on
this bed, so a zero length is a fact about a process with no scope set rather
than about the builtin. And the five sized-vector names are BROKEN rather than
unmeasured: `vec-empty` is CDX2040 unresolved, `vec-singleton` answers wrong,
`vec-cons` faults, with the account under the unowned registers in
`docs/PM/CurrentPlan.md`.

### 5.5 Published results that changed what can be built

- **The list family** (`codex/test/cost/builtin-alloc`, published in
  `DevelopersGuide.md`, "What List operations cost"): `list-length`,
  `list-at` and `list-set-at` are `none`, 0 bytes at both sizes;
  `__list-tail` is `fixed` at 24 bytes; `list-push` and `list-insert-at` are
  `input`, 4x with the input. `list-set-at` allocates nothing STRUCTURALLY --
  `emit-list-set-at` is a bounds check, an address, a store and a return of
  the same pointer -- so it widens what `bounded none` accepts, and
  `__list-tail` is the first builtin measured `fixed`, which makes that rung a
  non-empty class rather than a slot in a diagram.
- **`list-push` is input-proportional even on the extend-in-place path**,
  because path 2 doubles the capacity and the frontier advances by the whole
  of it. The aliasing rule in the guide tells you which path you get and not
  what it costs, and only the copy path was ever assumed expensive. The arm
  that makes the instrument report all three classes was added after the first
  reading: at length n + 1 the identical push takes the spare-capacity path
  and retains 0, and before it every arm sat on the doubling boundary because
  `base` is a power of two, so the harness could only ever report the
  expensive path.
- **`buf-read-bytes` is 8x plus a 16-byte header** -- 64 x 8 + 16 = 528,
  256 x 8 + 16 = 2,064. `CLAUDE.md` rule 8 lists it under red flags as an "8x
  blowup" and four designs cite the figure; it is now right by measurement
  rather than by repetition.
- **Every vector this compiler produces is BOXED at 16 bytes**, split cleanly
  along produce-versus-read: eight names return a vector and each pays a box,
  nine read out of one and pay nothing. So a chain of vector operations pays
  one box per step, and `bounded none` cannot construct a vector at all, only
  read one it was handed.
- **`&` is `OpAnd`, not `OpAppend`.** `desugar-bin-op` maps the token to
  `OpAnd` and `infer-and` splits on the left operand's resolved type, sending
  `BooleanTy` to logical-and and everything else to concatenation. `OpAppend`
  reaches the AST only from the `show`-of-a-record desugaring, so a rule
  matching `OpAppend` scores zero while reading as correct. `infer-and`
  records the append route at the binary span through `record-expr-type`, and
  both `check-rt-no-alloc` and the cost check read that record; the recorded
  type is the RESOLVED LEFT OPERAND, so the Text and List routes are
  distinguishable to any later rule.

### 5.6 A LIST BUILT BY `&` COSTS 6.5x MORE TO READ, and the mechanism is not measured

Measured 2026-09-08 (`codex/test/net-recv-heap`, `eth-payload-cost` against
`eth-payload-flat-cost`): the same function over the same 1,514 bytes measures
**107,435** when the list was assembled as `zeros 12 [] & [8, 0] & zeros 1500
[]` and **16,400** when assembled by one `list-push` per byte. Nothing else
differs -- same loop, same count, same `list-at` reads -- and `list-at` itself
is free, since a loop reading every element costs the same as one pushing a
constant.

**So the READ cost of a list depends on how it was BUILT, which no row in the
table above expresses, and a cost stated per operation cannot capture it.**
What the concatenated representation is, and whether the multiplier grows with
the number of concatenations or with their sizes, is NOT measured. It has
already bitten a real measurement: a per-frame figure published from a
`&`-built fixture was 6.5x what the driver's own flat build costs
(`ProtocolStack.md`).

## 7. The kill-rate corpus

`codex/test/cost/accumulator-corpus`. Eleven entries: five quadratic, which a
check MUST catch, and six linear, which it MUST NOT flag.

**Every label is measured, not declared.** Each entry runs at n, 2n and 4n and
reports bytes retained across the call from `__heap-save`. Quadratic
allocation quadruples into roughly sixteen times the bytes, linear into
roughly four, and `verdict` thresholds the n-to-4n ratio at eight.
Hand-labelling would have made the corpus an assertion with no runner, graded
by the same judgement that wrote it.

| entry | n=64 | 4n=256 | ratio | label |
|---|---|---|---|---|
| `p-append-one` | 20,240 | 277,520 | x13.7 | quadratic |
| `p-append-show` | 5,304 | 82,528 | x15.5 | quadratic |
| `p-append-chunk` | 71,696 | 1,073,168 | x14.9 | quadratic |
| `p-expand-blocks-old` | 140,304 | 2,134,032 | x15.2 | quadratic |
| `p-syslog-body` | 20,240 | 277,520 | x13.7 | quadratic |
| `p-append-text` | 72 | 264 | x3.6 | linear |
| `n-push-one` | 528 | 2,064 | x3.9 | linear |
| `n-push-block` | 4,112 | 16,400 | x3.9 | linear |
| `n-fixed-appends` | 320 | 320 | x1.0 | linear |
| `n-fresh-not-acc` | 7,232 | 28,768 | x3.9 | linear |
| `n-append-empty` | 3,088 | 12,304 | x3.9 | linear |

**The populations do not touch.** Worst positive x13.7, best negative x3.9,
and the threshold sits in the gap between them rather than just past one side.
The ratio is printed and not only the verdict, so a later reader can see the
margin and judge whether eight is still the right cut.

**The negatives are the half that makes it an instrument.** A corpus of
quadratic cases alone cannot separate a good check from one that refuses every
append, and that check scores a perfect kill rate. Four negatives look like
positives to any test that reads for the append operator. `p-append-text` is
the one that was not written that way and became it -- `acc & "x"` in a self
tail call is the textbook quadratic shape and is linear on this compiler after
COMPILER-8 -- so it is now the row that separates a rule reading the OPERATOR
from one reading the ALLOCATION. It moved from x12.7 to x3.6 on identical
source, which is what a measured corpus is for and what a hand-labelled one
would have hidden. The other three were built for the job: `n-fixed-appends`
appends to a growing accumulator in a self tail call but a constant four times
however large n gets; `n-fresh-not-acc` appends per iteration to something
that is not the accumulator; `n-append-empty` appends the accumulator to an
always-empty list, so the result aliases and nothing is copied. A static
filter over `acc &` flags all three.

**The corpus carries its own control.** Entries 4 and 7 are the same task in
two implementations, `pb-expand-blocks` as it was written and as it was fixed,
and they land on opposite verdicts from measurement alone. Entry 4 is the one
real instance with a published before and after: at the default 4096 blocks
the append form needed two to three gigabytes and died silently with exit zero
and truncated output; at n=256 it already retains 2.1 MB against the fixed
form's 16 KB.

**Bytes RETAINED is the honest unit and not an approximation.** Bare metal has
no collector, so what a loop allocates and abandons is retained until the
producing function returns, which makes a heap-pointer difference an exact
measure rather than a sample.

**It is run by `build/cost-corpus.ps1`, on demand, and it is NOT in the
battery.** `codex\test\cost` is deliberately absent from `build/test.ps1`'s
`$allDirs` and must stay absent: harnesses are built and not gated (Damian,
2026-07-27; `ExaminersAssay.md`, "Build the instrument; do not gate it"). The
script is what stops that being the same defect the corpus exists to avoid one
level up. **A corpus with no runner is an assertion with no runner, and the
first version of this work shipped exactly that** -- ten measured rows, a
recorded answer key, and nothing anywhere that would ever run them again. It
was caught by handing the task to an agent that had not seen the work, which
is the only reading that could have caught it, since the author knows how to
run it by hand and therefore cannot notice that nobody else does.

**The script checks the PROPERTIES, not the bytes.** Every `p-` entry must
measure quadratic and every `n-` entry linear -- the name declares intent, the
run measures it, and disagreement either way is the finding -- and the two
populations must still not touch with 8 between them. A moved number is
reported separately from a broken property, because an allocator change can
move every figure in the table without invalidating anything. Ablated by
declaring one linear entry quadratic, it fails twice, on the misdeclaration
and on the collapsed separation.

### 7.1 The rules, and what they score

The `growing` inference is compiler-side only: no declaration, no surface
syntax, reached through a `cost-report` mode flag that is off for every
ordinary compile.

- **Rule 1**: an argument in position *i* of a self call is that function's
  own parameter *i* with `&` applied to it.
- **Rule 2**: an append whose right operand is an empty literal aliases rather
  than copies, so it does not grow.
- **Rule 3**: a Text append with a non-allocating right operand does not grow;
  every List append does, and so does a Text append whose right operand
  allocates. The right operand is read as allocating unless it is a literal, a
  name, or a field access of one, which is abstain-toward-refusal pointed the
  only safe way.

| rule set | positives caught | negatives left alone | total |
|---|---|---|---|
| rules 1 + 2 | 5 of 5 | 4 of 6 | 9 of 11 |
| rules 1 + 2 + rule 3 | **5 of 5** | **5 of 6** | **10 of 11** |

**Both rows are measured by ablation and neither is derived.** The shipped
rule is not reconstructed on paper: the previous seed is still a compiler that
carries it, so the ablation is that binary run over the SAME corpus in
`cost-report` mode. Rule 3 buys `p-append-text` alone and nothing else moves;
with rule 2 removed, `n-append-empty` joins the flagged set and nothing else
moves. Each rule buys exactly the entry the corpus put there to buy it.

**The bar is all five quadratic entries caught** (ruled 2026-08-16, red under
Damian's go-forward). **A future rule that lifts the over-refusal must keep 5
of 5 on the quadratic half**; trading a caught quadratic for a quieter linear
is the one move this corpus exists to forbid.

**Nothing outside the corpus is flagged**: six diagnostics in the whole
compilation unit, all six in the corpus chapter, so the report is complete
rather than truncated.

**`&` IS A COPY TODAY, and the emitter says otherwise.**
`emit-str-concat-prologue` (`X86_64TextHelpers.codex:160-182`) computes
`left_base + aligned(len(left))` and compares it against the allocation
frontier in `r10`, which reads as an in-place fast path. That path is DEAD
CODE: the prologue ends `cmp-rr r13 r10` followed by `jmp 0` at `:178`, an
UNCONDITIONAL jump patched by `patch-jmp-at` (`:222`) to the slow path, while
every real conditional in that file uses `jcc`, and the comparison's result is
discarded. `__str_concat` has fresh-allocated on every call since CL 2823
(2026-05-30, val, Bug 2), for aliasing safety. Two probes are kept as
instruments rather than discarded: `codex/test/cost/literal-alloc` measures
that a Text literal allocates nothing per evaluation and that hoisting it out
of the loop is byte-identical; `codex/test/cost/str-concat-inplace` appends a
fixed 64-character piece one, two and three times in a straight line and reads
the deltas climbing by exactly the piece length, which is copying measured
without a loop or a tail call to blame.

### 7.2 What the corpus does not do

It does not cover the non-tail-recursive or mutually recursive forms; every
entry is the accumulator-in-a-self-tail-call shape, and `punctual` already
needed a `punctual-mutual-recursion` refusal test, so that gap has bitten the
sibling feature. **It tests no intermediate growth rate**: every entry sits at
roughly x4 or x13-15, so nothing establishes where an n log n allocator lands,
and the threshold of 8 is unvalidated against one. And it does not establish
that the tree's **523** sites of `(acc & ` are mostly this shape -- that count
is the raw operator, unclassified, and classifying it is a next step rather
than a claim made here.

## 5.7 What is open

- **The `&`-built list read multiplier of 5.6**: the representation, and
  whether the multiplier grows with the number of concatenations or their
  sizes.
- **`build/check-builtin-alloc.ps1` runs nowhere** (L-NOGATE), and wiring it
  in means changing the generator under `codex/build/`.
- **80 registry rows read `unknown`** (2026-09-07), each refusing one
  declaration at one call site. 5.4 names the ones that are deliberate.
- **The surviving false positive is `n-fixed-appends`**, a List accumulator
  that rule 3 never reaches. Catching it needs a rule that decides whether the
  append count is bounded by a literal rather than by an input, which is real
  analysis and not a predicate. Under abstain-toward-refusal it is the cheap
  direction, and COMPILER-7 asks to revisit it.
- **`bs-alloc` cannot key on instantiation**, which is what `show` and
  `__list_tail` need, and 5.2 is the account.

## 6. What this document is NOT

- Not a WCET proposal. `punctual` owns that and is shipped.
- Not a benchmark suite. See section 4.
- Not a claim that the three defects share a root cause in the code. They do
  not; they share a root cause in what the language promises.

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
