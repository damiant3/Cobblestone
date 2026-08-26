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

## 5. Open questions

Damian has ruled on the shape: this sits with `punctual`, so it is a declared
and enforced property rather than a document. What is still open:

1. ~~**How much of the general problem does the check attempt?**~~ RULED
   2026-08-16 (Damian, via red): **a class, never a function of the inputs.**
   The declaration is a CEILING in a small lattice and the compiler infers each
   function's class bottom-up from its body, the way effect sets are inferred:

   ```
     none    < fixed          < linear         < growing
     no heap   fixed bytes/call  one walk over    accumulator copied
     (punctual) regardless of    an input, no     inside a loop, or
                input            copies of the    a walk nested in
                                 accumulator      a walk
   ```

   Worst case over the code's STRUCTURE and blind to data on purpose: bubble
   sort is honestly `fixed` in heap whatever the data does to its time, and a
   walk nested in a walk is `growing` even when the data would keep it small.
   Its quadratic TIME is a WCET question and stays with `punctual`'s budget
   number. Refusal is transitive exactly as CDX6001: a declared ceiling breaks
   at the caller, at compile time, when a callee's inferred class exceeds it.
   No declaration means no check, as with `punctual`; the bottom rung is
   `punctual` minus the WCET budget and the effect ban, which is why the two
   felt like siblings. Abstain toward refusal; the false-refusal cost is paid
   in the declaration you can choose not to write. The keyword-free inference
   is what blu is building as step 2, and the corpus grades its `growing` rung.

   **FIRST SLICE SHIPPED (blu, 2026-08-16): two rungs of the four.** `linear`
   and `growing` are inferred and checked; `none` and `fixed` are named in the
   lattice and NOT yet inferred, so a declaration naming one is refused
   (CDX6103) rather than accepted unchecked. That is this ruling's own
   abstain-toward-refusal applied to the declaration itself: an unchecked
   promise reads exactly like a checked one, which is worse than no promise.
   **The four-class lattice above remains the target**; what shipped is a
   subset that refuses honestly where it cannot yet decide, and `punctual`
   already enforces the no-heap case that `none` describes.

   **THIRD RUNG SHIPPED (blu, 2026-08-19, main 17299): `none` is inferred.**
   A definition's body is walked for allocation and the answer is closed
   under calls the way `growing` already was, so `bounded none` is refused
   with CDX6101 when the target allocates directly or through a callee. The
   arms are `codex/test/errors/bounded-none-exceeded` (transitive: the
   declared function's whole body is one call) and
   `codex/test/apps/bounded-none-accepted` (the control, which a refuse-
   everything check would fail).

   **A name in call position that is neither a definition in this unit nor a
   builtin measured at zero bytes is read as allocating.** That is this
   ruling's abstain-toward-refusal at the only place it can be applied here,
   and it is why the promise is narrow: the zero-byte set is what 3.1
   measured (the text accessors) plus the accessors `punctual` already treats
   as safe, so a body calling any unmeasured builtin cannot declare `none`
   today. Widening the set is a measurement, not a rule change.

   **A MEASURED OVER-REFUSAL IN THE SHIPPED `none` RUNG (blu, 2026-08-19),
   found while building the arms for `budgeted` and NOT introduced by it.**
   `cost-binop-allocates` is written to tell `&`'s two jobs apart by the
   recorded type -- boolean AND allocates nothing, concatenation allocates a
   new value -- and the chapter prose beside it says so. It does not: measured
   against `Sut` 278AF7C4, `bounded none andy (a) (b) = a & b` over two
   `Boolean` parameters is refused CDX6101. So `bounded none` currently
   refuses any body that joins two conditions, which is a large and ordinary
   class, and the `budgeted` arm had to be written without `&` to avoid
   testing this instead of itself.

   **FOUND AND FIXED the same day, and the cause was neither the type scan
   nor the cost check.** `infer-and` recorded the expression type on its TEXT
   arm and not on its boolean one, so a boolean `&` had no entry at all and
   `expr-type-scan` answered `ErrorTy`; every reader of that table treats the
   unknown as the worst case, which is correct of them. One line: the boolean
   arm records too.

   Two theories came first and both were wrong -- unresolved type variables,
   then parameter resolution. What killed them was widening the probe:
   `True & False`, two literal Booleans, is refused exactly as `a & b` is, and
   three shapes failing together pointed at the recording site rather than at
   any resolution. **The regression guard is all three shapes**, in
   `codex/test/apps/bounded-none-accepted`.

   **It was never only this rung.** `punctual` reads the same table through
   `check-rt-no-alloc` and refused the same shape, so both checks were
   charging a boolean AND for a heap allocation it never makes.

   **THE LIST FAMILY IS MEASURED (blu, 2026-08-19).**
   `codex/test/cost/builtin-alloc` classifies it, and the discriminator is
   INPUT SIZE rather than iteration count: every arm makes one call, and the
   two readings differ only in how large the argument is. That is the
   question `fixed` actually asks and no existing instrument asked it.
   Published in `DevelopersGuide.md`, "What List operations cost".

   | | class | |
   |---|---|---|
   | `list-length`, `list-at`, `list-set-at` | none | 0 bytes at both sizes |
   | `__list-tail` | **fixed** | 24 bytes, flat |
   | `list-push`, `list-insert-at` | **input** | 4x with the input |

   Three results change what can be built on top. **`list-set-at` allocates
   nothing**, structurally rather than at two points -- `emit-list-set-at` is
   a bounds check, an address, a store and a return of the same pointer --
   so it can join the zero-byte set and widen what `bounded none` accepts.
   **`__list-tail` is the first builtin measured `fixed`**, which is what
   makes the rung a non-empty class rather than a slot in a diagram.
   **`list-push` is input-proportional even on the extend-in-place path**,
   because path 2 doubles the capacity and the frontier advances by the
   whole of it; the aliasing rule in the guide tells you which path you get
   and not what it costs, and only the copy path was ever assumed expensive.

   The instrument reports all three classes in one run, and the arm that
   makes that true was added after the first reading rather than designed in:
   at length n + 1 the identical push takes the spare-capacity path and
   retains 0. Before it, every arm sat on the doubling boundary because
   `base` is a power of two, so the harness could only ever report the
   expensive path -- which is asserting, not measuring.

   **THE TEXT FAMILY RE-MEASURED WITH THE SIZE DISCRIMINATOR (blu,
   2026-08-19), and it raises TWO QUESTIONS THAT NEED A RULING before more
   rows can be filled in honestly.** Results in `DevelopersGuide.md`. Three
   predicates (`text-contains`, `text-starts-with`, `text-compare`) allocate
   nothing at any length and are safe `none`. `char-to-text` is a clean
   `fixed`. The other two are the problem.

   **1. `substring`'s class depends on its ARGUMENT, and `bs-alloc` is one
   word per builtin.** Holding the output at four characters it is flat at 16
   bytes whether the input is 64 or 256; letting the output grow with the
   input it is 40 then 136. So it is `fixed` in the shape that dominates
   parsing here -- a fixed-width field out of a line of any length -- and
   `input` when the slice grows. A single scalar class has to take the worst
   case and call it `input`, which refuses exactly the case `fixed` was
   introduced to permit. Either the class becomes per-ARGUMENT, or the
   over-refusal is accepted and written down as the price. ~~This is a design
   question, not a measurement, and it is the first thing that needs deciding
   before `fixed` can ship. It is CurrentPlan rulings queue 17.~~ **RULED
   2026-08-19 (Damian): the class becomes PER-ARGUMENT. Shipped the same day;
   the record is below.**

   **2. `integer-to-text` is bounded but not constant, and the lattice has no
   rung for that.** 16 bytes at 2 and 3 digits, 16 and 24 at 8 and 10: the
   allocation follows the digit count in 8-byte steps. An `Integer` cannot
   exceed twenty digits, so the call can never allocate more than about 32
   bytes and cannot cause blow-up, which is what `fixed` exists to promise.
   But "the same bytes every call" is false. Reading it strictly makes it
   `input` and abstain-toward-refusal says take the stricter rung; reading it
   by the rung's PURPOSE makes it `fixed`. ~~Its row stays `unknown` -- which
   is read as allocating, the safe side -- until that is ruled on. It is CurrentPlan rulings queue 18.~~
   **RULED 2026-08-19 (Damian): NEITHER. Between `fixed` and `input` there is
   a bounded-or-budgeted class and `integer-to-text` is in it. Shipped the
   same day; the record is below.**

   The narrow arm is worth keeping in view: at 64 and 256 alone,
   `integer-to-text` reads flat and would have been published `fixed`. That is
   the same single-point error the 08-15 table made, caught here only because
   the harness was rerun at a wider spread.

   **THE `budgeted` RUNG SHIPPED (blu, 2026-08-19, main 17581), and it closes
   17 and 18 together because they were one question.** Both readings above
   describe the same gap: an allocation that is genuinely bounded by something
   nobody wrote down. `substring line 0 4` is bounded by the literal it is
   passed; `integer-to-text` is bounded by the twenty digits an `Integer` has.
   Neither is `fixed`, because the bytes are not the same every call, and
   calling either `linear` promises something about the input that is false.
   So the lattice gained a rung between them rather than forcing either answer:

   ```
     none  <  fixed  <  budgeted  <  linear  <  growing
   ```

   `cost-class-rank` carries it at rank 2 (`TypeChecker.codex`), and the
   registry says which builtins are in it: `integer-to-text`'s `bs-alloc` row
   is a bare `budgeted`, `substring`'s is **`budgeted:3`** -- the per-argument
   form question 1 asked for, naming the argument that supplies the bound. **A
   `budgeted:N` call is accepted when argument N is a LITERAL and refused
   otherwise**, which is blunt on purpose: it also refuses a computed length
   that happens to be small, and that is this feature's abstain-toward-refusal
   applied where the compiler cannot know.

   The arms are `codex/test/apps/bounded-budgeted-accepted` (the control,
   carrying both shapes the rung was ruled into existence for) and
   `codex/test/errors/bounded-budgeted-exceeded` (the refusal, whose
   declaration sits on `clip` while the offending `substring` is in its
   UNDECLARED callee `half`, so the arm proves the refusal is transitive and
   not merely local). **Re-measured 2026-08-20 against depot seed A6D49D19**
   rather than taken from the CL: the control compiles at exit 0 and the
   refusal answers CDX6101 at exit 4, naming `clip`.

   `fixed` is still the rung that has not shipped, and the reason has not
   changed: separating "the same bytes every call" from "one walk over an
   input" needs the 264-entry registry measured, and `bs-alloc` reads
   `unknown` on 132 of them (re-measure before quoting; it was 245 on
   2026-08-20 and moved six times on 2026-08-21). What HAS changed is that this no longer blocks
   ordinary parsing code from declaring anything, because `budgeted` is the
   rung that code actually sits on. `bounded fixed` is refused as unsupported
   (CDX6103) rather than accepted unchecked, which is the same
   abstain-toward-refusal one level up: an unchecked promise reads exactly
   like a checked one.

   **`fixed` is the one rung left and it is STILL blocked, on less than it
   was.** Separating "fixed bytes per call" from "one walk over an input"
   needs a per-builtin allocation class; 3.1 published the text family and
   the list family is now measured too, but the 264-entry builtin registry is
   only half read -- 132 rows still `unknown` -- and the check cannot read a
   class that lives only in a document. **`bs-alloc` on `BuiltinSpec` SHIPPED (blu, 2026-08-19, main
   17450)** (`codex/compiler/Types/Builtins.codex`), which is where the class
   belongs: it puts the answer beside the name and the type, and
   `cost-builtin-nonalloc` reads `builtin-alloc-by-name` rather than the
   hand-kept list it used to carry, so a measurement widens what `bounded
   none` accepts by editing the row that names the builtin. The registry is
   the mechanism now and the measurement is the remaining work. **Re-measured
   2026-08-21 after a second family, the 264 rows read: `unknown` 219,
   `none` 35, `input` 5, `fixed` 3, `budgeted` 1, `budgeted:3` 1.** The second
   pass measured the character predicates, the small conversions and the raw
   memory accessors: `is-letter`, `is-digit`, `is-whitespace`, `code-to-char`,
   `text-to-integer`, `compare`, `peek-byte`, `peek-32`, `poke-byte` and
   `poke-32` all read 0 at both sizes; `show` is `fixed` at 16 bytes;
   `text-split` and `alloc-bytes` are `input`.

   **`alloc-bytes` IS WHY AN ARM MUST VARY THE THING IT IS MEASURING.** Its
   first arm passed the literal 64 and it duly read `fixed` at both sizes,
   which is a true statement about the arm and says nothing about the builtin:
   the only quantity that varies is the argument the caller chose. It is
   `substring`'s shape -- the class follows a parameter -- and an arm holding
   that parameter constant cannot see the class at all. Varying it, the same
   builtin reads 64 against 256, exactly 4.0x, `input`. The earlier reading
   was not wrong about what it measured; it measured the wrong thing.

   **THIRD PASS, the buffer family: `unknown` 214, `none` 38, `input` 7,
   `fixed` 3, `budgeted` 1, `budgeted:3` 1.** `__buf-write-byte`,
   `__buf-write-bytes` and `__narrow` retain nothing; `__buf-read-bytes` and
   `__list-with-capacity` are `input` at 528 bytes for 64 and 2,064 for 256.

   **That pass also settled a number this project has been carrying unmeasured
   in `CLAUDE.md` itself.** Rule 8 lists `buf-read-bytes` under red flags as an
   "8x blowup", and four designs cite the figure as settled. It is 8x plus a
   16-byte header -- 64 x 8 + 16 = 528, 256 x 8 + 16 = 2,064 -- so the rule is
   RIGHT, and is now right by measurement rather than by repetition. A claim in
   the file that loads every session, cited across a campaign, had never been
   read off the machine; the outcome happened to be confirmation, and the value
   of checking did not depend on that.

   **FOURTH PASS, the proof terms and the pointer family: `unknown` 202,
   `none` 48, `input` 7, `fixed` 5.** `tag-equal`, `variant-tag`, `address-of`
   and `__memset` measured at zero; `__linked-list-empty` and
   `__linked-list-push` are `fixed`, which is the contrast that makes a linked
   list worth having against `list-push`'s `input` worst case.

   **The six proof terms are `none` STRUCTURALLY and not by measurement**, and
   the distinction is recorded rather than smoothed over: `Refl`, `assume`,
   `sym`, `trans`, `cong` and `app-cong` all lower through
   `emit-proof-builtin`, which is `emit-int-lit st 0`. There is no allocation
   path to find. They are also the one family this instrument CANNOT arm, since
   a proof term cannot close the `+ r - r` bracket that forces a result to be
   used -- so an arm would be measuring dead-code elimination. Classifying them
   from the emitter is the same footing `list-set-at` already stands on.

   **PASSES FIVE TO TEN, all 2026-08-21, and the log stops accumulating here.**
   Appending a paragraph per family was already the wrong shape at four; what
   follows is the current measurement plus the one thing each pass established
   that is NOT a count. **Re-measure before quoting any of it (L-COUNT):
   `unknown` 86, `none` 156, `fixed` 13, `input` 7, `budgeted` 1,
   `budgeted:3` 1, of 264.** The sequence of `unknown` readings was 232, 219,
   214, 202, 186, 169, 163, 153, 143, 132 across 2026-08-21, then 125, 107 and 86 on
   2026-08-25.

   - **The process and channel family, twenty-one of twenty-two** (blu,
     2026-08-25). Process memory does not come from the heap:
     `__spawn_pool_carve` is shift-and-add into a statically reserved pool, the
     three out-of-family targets are defined in `ProcessHelpers` and allocate
     nothing, and `process-get-scope` returns a Text by loading a pointer
     already in the process table rather than building one.
     **THE TWENTY-SECOND IS WHY THE SCAN PATTERN IS NOW WRITTEN DOWN.**
     `chan-text-recv` allocates: it rounds the received length up and does
     `add r10, rax`, advancing the bump allocator INLINE, without ever calling
     `__alloc` or `emit-bivy-alloc`. A scan for those two names -- which is
     what classified the eighteen in the entry below, and what I ran first here
     -- cannot see it, and would have published `none` on a builtin that
     allocates in proportion to a message. **A false `none` is a false promise,
     which is the dangerous direction; the too-narrow pattern found it in the
     cheap direction only by luck of reading the body.** Any future emitter
     classification must look for `reg-r10` advancement as well as the two
     allocator names. `chan-text-recv` stays `unknown`: what it retains is
     proportional to the MESSAGE, and the message size appears in no argument,
     so no rung in this lattice describes it.
     **AND `none` MEANS NO HEAP, NOT NO RESOURCE.** A spawn consumes a
     fixed-pool slot; slot exhaustion is a bounded resource this lattice does
     not describe, the same way quadratic TIME stays with `punctual`'s budget.
   - **The VMX/MSR and UEFI console families, eighteen rows, classified from
     the EMITTER** (blu, 2026-08-25). `builtin-alloc` cannot reach any of them:
     `vmxon` and its family would fault the moment an arm executed one, and the
     console calls need a firmware boot nothing in `codex/test` performs. So
     they take the footing the six proof terms already stand on. Each is a
     named runtime helper in `X86_64Helpers.codex`, and across the whole span
     that holds all eighteen there is no `emit-bivy-alloc`, no call to
     `__alloc`, and no `emit-call-to` at all, so none can allocate directly or
     through a callee. `uefi-read-key-ex` reserves a stack frame
     (`sub rsp, 0x58`); that is the stack, and this rung is about the heap.
     **THE PART THAT IS NOT A COUNT: the cost check reads a builtin's class
     only in CALL position.** Eleven of the eighteen are refused CDX6101 by the
     preceding seed and accepted after; the other seven are NOT, and the split
     is exactly nullary against argument-taking. A nullary builtin used as a
     value never has its class consulted, so reclassifying those seven changes
     nothing observable today, and an arm over one would pass under both
     compilers while testing nothing. Seven such arms were written, measured as
     vacuous, and REMOVED rather than kept as decoration -- the same
     vacuous-control trap COMPILER-18 shipped and this file's own corpus exists
     to forbid. Whether any nullary builtin allocates is NOT established: the
     one candidate tried, `current-dir`, does not resolve on this target.
   - **The print family, seven rows on four emitters** (blu, 2026-08-25). All
     seven measure `none`, and the count is the least of it. `print-line`,
     `print-line-uni`, `print-error` and `print-error-uni` all lower through
     `emit-print-line-builtin`, so they CANNOT differ in class; measuring the
     aliases anyway is what shows that rather than assuming it, and it is the
     same footing the proof terms stand on one section up. **The ASCII probe
     would not have earned the class.** `emit-print-text-loop` branches on the
     code unit, so a text of `x` never enters the multi-byte arm at all; the
     rows are therefore read four ways -- ASCII, a tier-0 accented unit, a
     tier-0 Cyrillic unit and a tier-1 unit -- and all four retain zero at
     n=64 and n=256 against a control that also reads zero. Reading one shape
     and publishing the class is exactly the fixture-shape trap (L-CONSTRUCT)
     that COMPILER-21 and COMPILER-22 were both found by, on the same surface,
     the same day.

   - **The real conversions, sixteen rows on eight arms** (main 18985). A Real
     cannot close the `+ r - r` bracket, so every reading is a chain that
     charges its arm for everything in it. Allocation cannot be negative, so a
     chain reading EQUAL to a control containing a subset of it proves every
     added member is zero at once. That LADDER is what buys sixteen rows for
     eight arms, and the f32 rungs sit on the f64 ones because the only route
     from an Integer to an f32 runs through a pair the rung below settled.
   - **The SIMD family, seventeen rows** (main 19025 for the arm shape, 18995
     for the rows). **Every vector this compiler produces is BOXED at 16
     bytes**, and the split is clean along produce-versus-read: eight names
     return a vector and each pays a box, nine read out of one and pay
     nothing. So a chain of vector operations pays one box per step, and
     `bounded none` cannot construct a vector at all -- only read one it was
     handed, which is why those arms take theirs as parameters. Published in
     `DevelopersGuide.md` with the table.
   - **The atomics, six rows** (main 18999). `bs-varies` and `bs-alloc` come
     apart here: all six read True for `bs-varies` because their ANSWER
     depends on another core, and that decides nothing about what they retain.
   - **The CPU reads and the rest of raw memory, ten rows** (main 19012).
   - **The port and MMIO rows, ten rows** (main 19025), which needed a new arm
     shape. See the effectful-arm note below.
   - **The read-only process and identity rows, eleven rows** (main 19040).

   **A NEW ARM SHAPE NEEDS ITS OWN CONTROL, and this is the general form of
   the `alloc-bytes` lesson above.** An effectful builtin cannot be bound with
   `let` (CDX2033), so its arm opens an `act` block and takes the answer with
   `<-`. A different bracket is a different instrument until something says
   otherwise, so the section opens with `act-control`: the same block, the same
   two marks, nothing measured inside. It reads 0, and only then is anything
   below it attributable. Without it, ten rows reading the same nonzero would
   be indistinguishable from ten builtins that each allocate that much, and the
   section would have published `fixed` on the strength of the bracket. The
   same discipline one level down is why `vec-extract` and `vec4-extract` are
   measured alone before any arm that contains them, and why the first mask arm
   was wrong: it built its `VectorMask` inside the heap mark and read 16 bytes
   for four builtins that return a Boolean or an Integer.

   **THE REGISTRY IS NEVER CONSULTED FOR A NULLARY BUILTIN**, found by an arm
   written on the opposite assumption. `cost-head-allocates` is reached from a
   call HEAD, and a bare name is not a call, so `cpu-read-cr0`, `cpu-read-cr3`,
   `flush-tlb` and the six process constants have rows because they were
   measured and not because any consumer asks. The `machined` arm in
   `codex/test/apps/bounded-none-accepted` is the control that established it:
   the kill refused four of five and accepted that one with all three of its
   rows still `unknown`. Measure them anyway -- "no consumer asks" is not "the
   answer does not exist" -- but do not count them as widening the rung.

   **WHAT IS DELIBERATELY STILL `unknown`, so the gap is named rather than
   silent.** The GPU four (`gpu-in`, `gpu-out`, `gpu-mem-read`,
   `gpu-mem-write`), the two 16-bit port block forms and `runtime-init`: an arm
   for `gpu-mem-write` at offset 0 of the window writes over `DeviceBuffer`'s
   allocation cursor, which is corrupting an allocator in order to measure one.
   `process-get-scope` and `process-get-network-scope` return Text and both
   answer the EMPTY string on this bed, so a zero length is a fact about a
   process with no scope set rather than about the builtin; reading `none` off
   the empty path is the corpus-shape failure exactly. And the five sized-vector
   names are BROKEN rather than unmeasured -- `vec-empty` is CDX2040 unresolved,
   `vec-singleton` answers wrong, `vec-cons` faults -- with the account under
   the unowned registers in `docs/PM/CurrentPlan.md`.

   The earliest passes read `unknown` 232, then 219, then 214; before them, 247. The registry grew by two rows and
   fifteen were measured that day: the twelve integer and bit names, `negate`,
   `real-from-int` and `real-to-int`, all at 0 bytes retained at both input
   magnitudes (`codex/test/cost/builtin-alloc`, the account in
   `DevelopersGuide.md`). **Twelve of the fifteen widen nothing**, because
   `is-rt-safe-builtin` already carried them and `cost-builtin-nonalloc` reads
   that set first; what they buy is a registry that agrees with a measurement
   instead of a second hand-kept list. The three that DO widen the rung are
   `negate` and the two real conversions, and the kill side was taken before
   the rows moved: against depot `Sut` 96CB73CB the new section of
   `codex/test/apps/bounded-none-accepted` is refused CDX6101 on both
   declarations at exit 4, and it compiles once the rows read `none`. It was
   2026-08-20's reading of
   `unknown` 245 that this line carried, and the earlier one before it. `unknown` is the refusing side,
   so every row still reading it is a builtin read as allocating without bound
   -- 132 of them at the last measurement, and that count IS the block on the
   rung. CDX6103 now names only `fixed` and says so. Do not infer it
   from the code's shape alone: a single `&` allocates in proportion to its
   operands, so a straight-line body with no loop in it is already not
   `fixed`, and a shape-only rule would accept exactly the case the class
   exists to exclude.
2. ~~**What is the instrument that keeps it honest?**~~ **THE CORPUS IS BUILT
   (blu, 2026-08-16), which answers the "before the check, not after" half.
   What it grades is still open.** `codex/test/cost/accumulator-corpus` is ten
   entries, five quadratic and five linear, and section 7 is its account. The
   question that remains is not what the instrument is but what a check has to
   score on it to be worth shipping, and that is a ruling rather than a
   measurement. **RULED 2026-08-16 (red, under Damian's go-forward): 9 of 10
   SHIPS.** The bar is all five quadratic entries caught, and it is met. The
   single miss is an OVER-REFUSAL, not a miss in the dangerous direction:
   `n-fixed-appends` is a linear entry that the check flags, linear because
   its append runs exactly four times however large the input gets. That is
   the abstain-toward-refusal trade of question 1, and it is paid in a
   declaration an author can decline to write. **A future rule that lifts the
   over-refusal must keep 5 of 5 on the quadratic half**; trading a caught
   quadratic for a quieter linear is the one move this corpus exists to
   forbid. Section 8 has the ablation and the argument.
   **REOPENED the same day, before anything shipped:** the rule that produced
   that score tests the append rather than the allocation between appends, and
   `&` extends in place while the accumulator is topmost. The 9 of 10 is the
   score of an over-strong rule; the ruling stands on the SHAPE of the trade
   and the number is being re-taken. Section 8.
3. ~~**What is it called?**~~ RULED 2026-08-16 (Damian): **`bounded`**, in the
   same slot as `punctual`, followed by the class:

   ```
     bounded linear unpack-text : Bytes -> Text
     unpack-text (bs) = unpack-go bs 0 ""

     unpack-go (bs) (i) (acc) =
       if i == list-length bs then acc
       else unpack-go bs (i + 1) (acc & byte-to-text (list-at bs i))
   ```

   which today compiles and is quadratic, and under the declaration is refused
   with the site named:

   ```
     CDX6101 unpack-text declares bounded linear but calls unpack-go, inferred
             growing: argument acc is copied by & at Unpack.codex:14 inside a
             self tail call
   ```

   The diagnostic number is illustrative; blu allocates the real codes in
   `CdxCodes.codex`.
4. ~~**Are 3.1 and 3.2 worth doing ahead of any of it?**~~ CLOSED 2026-08-15:
   both taken. 3.1 paid for itself immediately by catching 3.5's own
   misattribution, which is the argument for measuring before publishing
   rather than publishing what a prior session recorded.

## 8. The kill rate, measured (2026-08-16, blu)

**This is the number question 2 asks for. The ruling it wants is whether it
ships.** The `growing` inference of section 5 question 1 is built and scored
against the corpus in section 7. It is compiler-side only: no declaration, no
surface syntax, reached through a `cost-report` mode flag that is off for
every ordinary compile, so the tree is unaffected either way (the standing
gate is green with it in, 265 clean, 0 regressions).

| rule set | positives caught | negatives left alone | total |
|---|---|---|---|
| rule 1 alone | 5 of 5 | 3 of 5 | 8 of 10 |
| rule 1 + rule 2 | **5 of 5** | **4 of 5** | **9 of 10** |

**This table is the 2026-08-16 measurement against the ten-entry corpus and it
is superseded. Section 8b has the current one**, against eleven entries and a
compiler that extends a Text accumulator in place; both the denominator and one
entry's label changed underneath it, so the numbers here do not compare with
the numbers there.

- **Rule 1**: an argument in position *i* of a self call is that function's own
  parameter *i* with `&` applied to it. **It fires on that shape AS SUCH and
  does not ask whether anything allocates between the appends.**
- **Rule 2**: an append whose right operand is an empty literal aliases rather
  than copies, so it does not grow.

**Rule 1 as stated is too strong, and the correction is pending (red and
Damian, 2026-08-16).** `&` on Text is not an unconditional copy.
`emit-str-concat-prologue` (`X86_64TextHelpers.codex:160-182`) computes
`left_base + aligned(len(left))` and compares it against the allocation
frontier in `r10`; when they meet, the left operand is the TOPMOST allocation
and `emit-str-concat-fast-copy` rewrites its length in place and appends the
bytes. Only otherwise does it branch to the copying slow path. That is the
same three-path shape `__list_snoc` has and `DevelopersGuide.md` already
documents for lists.

So accumulating with `&` is linear while the accumulator stays topmost, and
quadratic when something allocates BETWEEN the appends and pushes it off the
frontier. `unpack-text` was quadratic for that second reason -- `byte-to-text`
allocated between appends -- and not because `&` copies as such. **The rule
that ships must test the intervening allocation, not the append.** Until it
is re-scored the 9-of-10 above is the score of the over-strong rule and
should not be quoted as the score of the check.

**RESOLVED 2026-08-16, and the 9 of 10 stands as the score of what runs.**
The in-place path is DEAD CODE and deliberately so. `emit-str-concat-prologue`
ends `cmp-rr r13 r10` followed by `jmp 0` at
`X86_64TextHelpers.codex:178` -- an UNCONDITIONAL jump, patched by
`patch-jmp-at` (`:222`) to the slow path, where every real conditional in that
file uses `jcc`. The comparison's result is discarded. `__str_concat` has
fresh-allocated on every call since CL 2823 (2026-05-30, val, Bug 2), for
aliasing safety. So `&` IS a copy today, three paths collapse to one, and the
guide's Text sentence is right about what runs even though it is wrong about
what the emitter contains.

Two probes were built to get there and are kept as instruments rather than
discarded: `codex/test/cost/literal-alloc` measures that a Text literal
allocates NOTHING per evaluation (0 bytes at n and 4n) and that hoisting the
literal out of the loop is byte-identical, which removes the obvious
explanation; `codex/test/cost/str-concat-inplace` appends a fixed 64-character
piece one, two and three times in a straight line and reads the deltas: 552
then 616, climbing by exactly the piece length. In-place extension gives flat
deltas. Growing deltas are copying, measured without a loop or a tail call to
blame.

**The consequence for the rule is a deferral, not a change.** Keying `growing`
on an allocation BETWEEN the appends is the right shape for after COMPILER-8
makes the linear-accumulator case in-place, and it is the wrong rule today:
today `p-append-text` has nothing between its appends and is quadratic anyway,
so a between-appends rule would score 4 of 5 on the quadratic half and breach
the bar question 2 set. **The refinement therefore lands in the COMPILER-8 CL,
with the semantics change that makes it true, and not before.** A rule and the
optimisation it models have to move together or one of them is lying.

### 8b. RULE 3, and the deferral above is now closed (2026-08-16, blu)

COMPILER-8 landed at main 16039 and the deferral's own condition came true, so
the refinement above is built. **Rule 3: a Text append with a non-allocating
right operand does not grow; every List append does, and so does a Text append
whose right operand allocates.** The type comes from `infer-and`, which records
its append route at the binary span (the same record the CDX6002 fix
installed), and the right operand is read as allocating unless it is a literal,
a name, or a field access of one, which is question 1's abstain-toward-refusal
pointed the only safe way.

**The worry that caused the deferral did not survive contact.** It was that a
between-appends rule would drop the quadratic half to 4 of 5. Measured on the
eleven-entry corpus it does not: `p-append-show` is Text with an allocating
right operand and is caught, so the quadratic half stays at 5 of 5.

| rule set | positives caught | negatives left alone | total |
|---|---|---|---|
| rules 1 + 2 (what shipped) | 5 of 5 | 4 of 6 | 9 of 11 |
| rules 1 + 2 + rule 3 | **5 of 5** | **5 of 6** | **10 of 11** |

**Both rows are measured by ablation and neither is derived.** The shipped rule
is not reconstructed on paper: the previous seed is still a compiler that
carries it, so the ablation is that binary run over the SAME corpus in
`cost-report` mode. It flags seven definitions to the refined rule's six, the
difference is `p-append-text` alone, and nothing else moves. That is rule 3
buying exactly the entry it was written to buy, on the same evidence standard
rule 2 was measured to.

**The surviving false positive is still `n-fixed-appends` and it is unchanged
by any of this.** It is a List accumulator, so rule 3 never reaches it; the
over-refusal COMPILER-7 asks to revisit is the same one, for the same reason,
and this refinement neither helps nor hurts it.

**Nothing outside the corpus was flagged**, six diagnostics in the whole
compilation unit and all six in the corpus chapter, so the report is complete
rather than truncated.

Both numbers are measured by ablation, not derived. With rule 2 removed,
`n-append-empty` joins the flagged set and nothing else moves, so rule 2 buys
exactly the one entry the corpus put there to buy it.

**The surviving false positive is `n-fixed-appends`, and it is the honest
shape of the remaining cost.** It appends to a growing accumulator, in a self
call, with the append operator, and is linear because the append runs exactly
four times however large the input gets. Catching it needs a rule that decides
whether the append count is bounded by a literal rather than by an input,
which is real analysis and not a predicate. **Under "abstain toward refusal"
(question 1's ruling) a false positive is the cheap direction**: it is paid in
a declaration an author can choose not to write, whereas a missed `growing` is
the defect this whole document exists for. On that reading 5 of 5 with one
over-refusal is already the right side of the trade, and the third rule is a
comfort improvement rather than a correctness one. That is the argument, not
the decision.

**Nothing outside the corpus was flagged.** Six diagnostics total across the
whole compilation unit, all six in the corpus chapter, so the report is
complete rather than truncated and no foreword chapter the corpus cites
carries this shape.

**Two things found while building it, and the second is a defect in shipped
code.** First, `&` is `OpAnd` and not `OpAppend`: `desugar-bin-op` maps the
token to `OpAnd` (`Desugarer.codex:249`), and `infer-and` then splits on the
left operand's resolved type, sending `BooleanTy` to logical-and and
everything else to concatenation. `OpAppend` reaches the AST only from the
`show`-of-a-record desugaring. A rule matching `OpAppend` scores zero while
reading as correct, which is what the first build did.

Second, and it follows from the same fact: **`TypeChecker.codex:1401`'s
punctual heap-allocation check matches `OpAppend` and cannot fire on a `&`
written in source.** Its message says "uses text concatenation (&)". It can
only ever see the synthetic nodes from `show`. So a `punctual` function doing
text concatenation is not refused by CDX6002 today. That is reported here
because this document owns the cost subject; the fix belongs with whoever owns
`punctual`, and it wants a test either way (L-UNCALLED).

**FIXED (blu, 2026-08-16).** `infer-and` already makes the only decision that
separates the two jobs of `&`, so it now records the append route at the binary
span through `record-expr-type`, and `check-rt-no-alloc` refuses an `OpAnd`
carrying that record. Measured both directions on one file of three punctual
concatenations: the depot seed compiles it clean at exit 0, the fixed compiler
answers three CDX6002 at the three spans. The arm is
`codex/test/errors/punctual-text-append` and the control is a punctual
`is-imminent` in `codex/test/examples/missile-warning` folding two comparisons
with a Boolean `&`, which must stay legal and is printed rather than merely
compiled. The recorded type is the RESOLVED LEFT OPERAND, so the Text and List
routes are now distinguishable to any later cost rule that needs to tell them
apart.

## 7. The kill-rate corpus (built 2026-08-16, blu)

`codex/test/cost/accumulator-corpus`. Eleven entries: five quadratic, which a
check MUST catch, and six linear, which it MUST NOT flag.

**Two of those rows moved after COMPILER-8 (main 16039) made a Text
accumulator extend in place, and the corpus is the thing that noticed.**
`p-append-text` was measured quadratic at x12.7 when the corpus was built and
is measured linear at x3.6 now, on identical source: it changed sides because
the compiler changed underneath it, which is what a measured corpus is for and
what a hand-labelled one would have hidden. `p-append-show` was added in the
same pass as its pair, appending `show i` rather than a literal, so the Text
half now carries both directions instead of only the one COMPILER-8 turned
green.

**Every label is measured, not declared.** Each entry runs at n, 2n and 4n and
reports bytes retained across the call from `__heap-save`. Quadratic allocation
quadruples into roughly sixteen times the bytes, linear into roughly four, and
`verdict` thresholds the n-to-4n ratio at eight. Hand-labelling would have made
the corpus an assertion with no runner, and it would have been graded by the
same judgement that wrote it, which is the defect `battery-reorg` and
`gpu/DeviceMath` are named for.

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

**The populations do not touch.** Worst positive x13.7, best negative x3.9, and
the threshold sits in the gap between them rather than just past one side. The
ratio is printed and not only the verdict, so a later reader can see the margin
and judge whether eight is still the right cut; a row that says "quadratic" and
nothing else hides exactly that.

**The negatives are the half that makes it an instrument.** A corpus of
quadratic cases alone cannot separate a good check from one that refuses every
append, and that check scores a perfect kill rate. Four negatives look like
positives to any test that reads for the append operator. `p-append-text` is
the one that was not written that way and became it: `acc & "x"` in a self tail
call is the textbook quadratic shape and is linear on this compiler, so it is
now the row that separates a rule reading the OPERATOR from one reading the
ALLOCATION. The other three were built for the job:
`n-fixed-appends` appends to a growing accumulator in a self-tail-call but a
constant four times however large n gets (flat at 320 bytes, the strongest row
in the table); `n-fresh-not-acc` appends per iteration to something that is not
the accumulator; `n-append-empty` appends the accumulator to an always-empty
list, so the result aliases and nothing is copied. A static filter over `acc &`
flags all three, which is the shape of the 575-findings-none-real result
recorded for the subset-cites filter.

**The corpus carries its own control.** Entries 4 and 7 are the same task in
two implementations -- `pb-expand-blocks` as it was written and as it was
fixed -- and they land on opposite verdicts from measurement alone, with no
label doing the work. Entry 4 is also the one real instance with a published
before and after: the chapter's prose records that at the default 4096 blocks
the append form needed two to three gigabytes and died silently with exit zero
and truncated output. At n=256 it already retains 2.1 MB against the fixed
form's 16 KB.

**Bytes RETAINED is the honest unit here and not an approximation.** Bare metal
has no collector, so what a loop allocates and abandons is retained until the
producing function returns. That is what makes a heap-pointer difference an
exact measure rather than a sample.

**It is run by `build/cost-corpus.ps1`, on demand, and it is NOT in the
battery.** `codex\test\cost` is deliberately absent from `build/test.ps1`'s
`$allDirs` and must stay absent: Damian's 2026-07-27 ruling is that harnesses
are built and not gated (`ExaminersAssay.md`, "Build the instrument; do not gate
it"). The script is what stops that being the same defect the corpus exists to
avoid, one level up. **A corpus with no runner is an assertion with no runner,
and the first version of this work shipped exactly that** -- ten measured rows,
a recorded answer key, and nothing anywhere that would ever run them again. It
was caught by handing the task to an agent that had not seen this work, which is
the only reading that could have caught it, since the author knows how to run it
by hand and therefore cannot notice that nobody else does.

**The script checks the PROPERTIES, not the bytes.** Every `p-` entry must
measure quadratic and every `n-` entry linear -- the name declares intent, the
run measures it, and disagreement either way is the finding -- and the two
populations must still not touch with 8 between them. A moved number is
reported separately from a broken property, because an allocator change can move
every figure in the table without invalidating anything. Ablated by declaring
one linear entry quadratic, it fails twice, on the misdeclaration and on the
collapsed separation.

**What the corpus does not do.** It does not say what score a check must reach,
which is a ruling. It does not cover the non-tail-recursive or mutually
recursive forms; every entry is the accumulator-in-a-self-tail-call shape that
question 1 names as the first and possibly only target, and `punctual` already
needed a `punctual-mutual-recursion` refusal test, so that gap has bitten the
sibling feature. **It tests no intermediate growth rate**: all ten entries sit
at roughly x4 or x13-15, so nothing establishes where an n log n allocator
lands, and the threshold of 8 is unvalidated against one. And it does not
establish that the tree's **523** sites of `(acc & ` are mostly this shape --
that count is the raw operator, unclassified, and classifying it is the next
step rather than a claim made here. (This said 516 until 2026-08-16; the first
figure came off a truncated grep and was carried into the document unchecked,
which is L-COUNT committed in the same paragraph that calls the number raw.)

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
