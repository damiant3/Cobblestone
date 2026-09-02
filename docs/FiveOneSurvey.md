# The Five-One Survey

*Written 2026-09-01 by red, the first Claude Fable 5.1 session on this tree,
at Damian's direction: a fresh read of the whole project to find what
earlier models missed or misprioritised. It is an explore, not a bug list.
Everything below was read from the source or the docs on that day, and
every count was measured that day and is not to be carried forward
(L-COUNT). Where a suggestion turned out to be already half-built, the
half that exists is named.*

*Damian's framing, given after the first reading and binding on this
document: several of the shapes criticised here were the right solution
when the compiler was young and the features that would have made a
better one (`mutable`, effect rows, the bounds prover) did not exist yet.
They are recorded as opportunities the language has grown into, not as
mistakes. And prose was always meant for high-level user chapters where a
reader needs it, never for the compiler's internals; R-PROSE and the
founding vision are not in tension.*

## What was read

The vision documents, `VisionAndVirtues.md`, both developer guides, the
lesson index, `CurrentPlan.md`, the compiler and plugs registers, the
compiler's front door (`opening.codex`), the type checker's type
resolution and proof normaliser, the bounds prover in
`X86_64Compound.codex`, the list helpers, the IR text emitter, the pass
pipeline, `List.codex`, `TrustLattice.codex`, `FactStore.codex`,
`RepoProtocol.codex`, the works README, the effect-row, totality,
rechecker, federation and verified-parsing designs, and a census of
idioms across every `.codex` file.

## The census, measured 2026-09-01

| idiom | count | unit |
|---|---|---|
| `__narrow` | 2,486 | sites (2,000 of them in `codex/plugs`) |
| `__narrow` applied to a literal | 388 | sites |
| `__record-set` | 4,773 | sites |
| `act` blocks | 1,922 | files |
| signatures declaring an effect row | 703 | files |
| `Integer between` | 376 | files |
| `mutable` record declarations | 143 | declarations |
| `linear` in a type | 109 | files |
| `bounded` declarations | 78 | files |
| `claim` | 41 | files |
| `punctual` | 27 | files |
| `quotes` | 11 | files |
| `instance` | 3 | files |
| `We say:` | 1,409 | files |
| `.expected` sidecars | 1,441 | files |
| `.failing` sidecars | 215 | files |

Two readings fall out before any suggestion. The safety features the
project advertises are used, and used widely: effects in seven hundred
files, bounded integers in nearly four hundred. And the escape hatch for
the bounded-integer system is the single most common idiom in the tree,
which is the first finding below.

## The findings

Ranked by leverage. Each names what exists, what is missing, and what one
change would retire.

### 1. `List (Integer between 0 and 255)` should be one byte per element

**The question Damian asked:** why introduce a byte type when the type
system can already say `Byte = Integer between 0 and 255 wrapping` and
`List Byte`?

**The answer is that it can, and the layout work is already half done.**
`field-byte-width` in `X86_64Compound.codex` maps a band to a hardware
width of 1, 2, 4 or 8 bytes, and records and sum constructors already
pack their fields by it, widest first, at natural alignment (the prose
above `sum-field-widths-total` is the contract). Lists are the one
container that does not: a list is `[capacity][length][e0][e1]...` with
every element in an 8-byte slot, and `emit-list-at-builtin`,
`emit-list-set-at`, `__list_snoc` and the rest scale the index with a
hardwired `shl 3`.

So `List Byte` is already a distinct type (invariance of type arguments
makes `List (Integer between 0 and 255)` and `List Integer` different
types, and that rule was chosen for exactly the aliasing reason that makes
a packed layout sound). What is missing is that the list builtins ignore
the element type when choosing a stride.

Two ways to give them one:

- **Static stride from the element type.** At a site where the IR node's
  type is `ListTy (IntegerTy lo hi mode)`, pick the shift from
  `bounds-to-hw-width` the way record fields do. This fails for generic
  code: a definition over `List a` is compiled once and cannot know `a`'s
  width.
- **Stride in the header.** The capacity cell already carries a sentinel
  (negative means a tail view), so the header is already overloaded; a
  width field there, or a third header cell, lets every builtin read the
  stride at runtime. One extra load and a variable shift per access, and
  generic code keeps working unchanged.

The hybrid is the likely shape: header stride as the truth, static stride
as the fast path when the site knows the element type. Either way the
change is confined to `X86_64ListHelpers.codex`, the list builtins in
`X86_64Builtins.codex`, the pointer-map walk, and every plug that lays
out a list.

**What it retires.** `__buf-read-bytes` at 8x plus a header, measured
and carried in `CLAUDE.md` R-COST as the standing red flag, becomes 1x.
Every parser in `codex/foreword/encode` shrinks its working set by the
same factor. `Text` is `[len][bytes]`, so a stride-1 `List Byte` is
layout-compatible with it up to the capacity cell and `text-to-bytes`
becomes close to a reinterpretation.

**What it does not retire on its own.** The CCE-on-the-wire bug class
(the SNI hostname, the key-schedule labels, the browser banner, the
`find-dot` that compared against ASCII 46) is a value-level mistake:
`char-code` answers an `Integer` and the wire took `List Integer`, so
nothing refused it. With wire APIs declared over `List Byte`, a bare
`char-code` result is refused at the slot (CDX2051) and the author has to
narrow or prove, which is where the encoding question gets asked. To
make the correct path the easy one, the foreword wants a
`text-to-utf8 : Text -> List Byte` whose result needs no narrow, so that
the wrong path is the one that costs a `__narrow`. That is the ergonomic
gradient, and it is cheap once the type exists.

### 2. The bounds prover does not refine under a comparison guard

`ir-expr-proven-range` (`X86_64Compound.codex:999`) follows literals,
fields, `let`, `__narrow`, `int-mod`, `bit-and`, `bit-shru`, the four
arithmetic operators, negation, and the union of an `if`'s two arms. It
does not read the `if`'s condition: `if x < 256 then f x else ...` proves
nothing about `x` inside the arm (`proven-if-range` at `:1050` takes only
the arms). That is the one rule that turns a bounded-integer system from
"runtime trap by default" into "proof by default", and it is why
`__narrow` is the most common idiom in the tree. Path-sensitive
refinement on `<`, `<=`, `>`, `>=`, `==` and `&`-conjoined guards, in the
same shallow style the existing rows use, would let the prover elide the
check at most of those 2,486 sites and put the safety claim where
`TechnicalDetails.md` already says it is. The refusal direction stays
exactly as strict: an unproven value still refuses.

### 3. When the append cannot be O(1), shadow a linked list

`list-push` and the `&` accumulator path extend in place when the value
is the topmost allocation and copy otherwise, and the guide's advice is
"do not rely on either outcome." The first reading of this survey called
the positional test undefined behaviour and proposed driving it from
ownership instead. **Damian's correction, and it is right:** the
positional fast path is what keeps a string concatenation in a loop from
going quadratic the moment any other allocation lands between two
appends, and no ownership verdict can supply that, because the question
is where the frontier is, not who owns the value.

The gap is the other branch. When the value is not topmost, today's
answer is a full copy, which is the quadratic case the guide measures at
203,200 bytes for a 2,000-character text built by straight-line appends.
The better answer is to keep offering O(1): when `&` or `list-push`
cannot extend, it chains instead of copying, producing a two-piece node
(the old value and the new piece) that is flattened once, on the first
read that needs contiguity. That is a rope, and `Foreword chapter Rope`
already exists; the proposal is to make it the builtin's own fallback so
that every accumulator gets it without the author choosing it. Both
outcomes are then O(1), both are semantically identical (the pieces are
immutable and sharing them is sound), and "do not rely on either
outcome" becomes a sentence nobody needs to read. The `text-concat-list`
advice in the guide stays correct and becomes the thing the runtime does
on its own.

The ownership information the checker already has still matters here,
but at a different point: it decides when a flatten may write in place
rather than allocate, which is the same unique-owner test finding 9
wants for reclamation.

### 4. Module-level literals are recipes, and the codegen fix is static data

A module-level list constant is rebuilt at every mention (98.4 MB for
100,000 reads against 1 KB through a parameter), and the guide says the
trap "keeps being rediscovered as though it were several separate traps."
A module-level binding whose body is literal-only (integer and text
literals, list and record literals of the same) can be emitted once into
the binary as static data and referenced by address. That is a codegen
feature, a data segment beside the code segment, and it retires the
class rather than documenting it a fourth time. Bindings whose body
allocates through a call keep today's semantics, which are correct for
them.

### 5. The plug boundary needs a coverage manifest

The standing hazard at the head of `codex/plugs/plugs-backlog.md` is that
a plug which does not handle a construct emits something anyway and
reports OK. Fifty-six plugs, each with its own set of silent gaps, graded
one probe at a time. The structural fix is on the wire: each plug
declares the IR node kinds and builtin names it handles, and the compiler
refuses to send an IR that contains anything outside the declared set,
naming the construct. Refusal moves to the one place that can see both
the program and the target, and "never silently lose a safety guarantee"
starts holding at the seam that currently breaks it. The same manifest
gives the arity-blind wire (`IRTextParser.codex:705`, recorded in the
register as a trust-model question and deliberately not acted on) a
trust model: hand-authored IR is accepted only within a declared
envelope.

### 6. Measured claims in docs want a runner

The project has proven, in a dozen lesson rows, that an unexecuted claim
rots, and it has one runner that works for a narrow class:
`check-doc-counts.ps1`. The docs carry thousands of other measurements
(seed sizes, gate timings, allocation tables, byte counts) each beside a
date, each re-measured only when someone happens to read it. The
generalisation is a fenced block a doc can carry that names the command
and the value; the gate re-runs the command and goes red when the value
moves. A number without such a block is visibly unchecked, which is the
honest state, and a number with one is a test. This does not ask for more
prose anywhere; it asks that the prose already written be given the
runner the lesson index says every claim needs.

**Damian's ruling on this finding (2026-09-01):** the documentation
culture was allowed to evolve into its current state deliberately, and he
likes it. Agent behaviour is hard to control, and litigating every
violation would end with no code compiling at all. The annotation export
from the code comments has been done once; the tools to use the
annotations properly do not exist yet and are being built. So the runner
above is an idea for when those tools land, not a criticism of the
present arrangement, and nothing here asks anyone to write less.

A second, smaller item in the same area: the prose/code boundary in a
chapter is one column of indentation, so a wrapped continuation line that
happens to start with `and` or `above` is lexed as code and the diagnostic
lands two words later in an English sentence (`DevelopersGuide.md`,
Pitfalls). In the user-facing chapters where prose is meant to live, that
is a hazard a reader who is not a compiler engineer will meet. Delimiting
prose by paragraph (a prose block runs to the next blank line whatever
its indentation) removes the class.

### 7. The repository protocol has no users yet, including this project

Facts, proposals, verdicts, signed imports, supersession and the trust
gate exist in `apps/works/RepoProtocol.codex` and its neighbours, and the
V3 federation design records that the two hardest pieces (import by hash,
the trust gate) closed in July. The fleet runs on Perforce, and
`PerforceProcess.md`'s longest section is about merge-down pain the
protocol was designed to make impossible. The cheapest first user is a
one-way shadow: every CL that lands on main also becomes a signed
definition fact with a proposal and Damian's verdict, written by a build
step and never read by anything the fleet depends on. The protocol then
accumulates a real, adversarial history (five agents, hundreds of CLs a
week) before anyone is asked to trust it with a workflow. An earlier
Fable memo in `docs/FableusFollies.md` proposed the same thing as its
"Congregation" arc; nothing else in the tree references that memo.

### 8. The proof layer wants one paying customer

Propositions are equalities only; induction is structural; normalisation
is fuel-bounded; forty-one files carry a claim. Rather than widening the
logic, the first job that pays rent is the one `VerifiedFormatParsing.md`
names as outside the type system's reach: parser and serializer
agreement on the wire formats that feed trust decisions
(`frame-encode`/`frame-decode`, the fact signing content, the verdict
content string). A claim quantified over `Integer between 0 and 255` is
decidable by exhaustion inside the fuel the normaliser already has, so
these are theorems the checker can finish today, and each one closes a
row in that design's census.

### 9. Ownership is enforced and reclamation is still manual

Unique and linear values are tracked, and memory is reclaimed by
`heap-save`/`heap-restore` written by hand at every site that needs it.
A unique value at its last use, when it is the topmost allocation, can be
reclaimed by the compiler with information the checker already computes
(the minted-owner walk). That is the bare-metal dividend of the ownership
work and it has not been collected. The escape analyses in
`docs/Designs/Done/Memory/` are the groundwork; the missing piece is
using the ownership verdict rather than a separate analysis.

### 10. Diagnostics as a corpus

Virtue 4 says diagnostics are a feature, and the guide's Pitfalls section
is a list of diagnostics that point at the wrong thing: `let above = ...`
reports "expected `in`", a prose `and` reports an undefined name two
words later, a tuple mismatch carries no file and no line. There are 204
`errors/` tests and 215 `.failing` sidecars, which pin the code. Each
pitfall in that section should also be a test that asserts the message
names the cause. Small, mechanical, and it is the virtue's own test.

### 11. Breadth is ahead of depth

Seventy applications and 1,058 modules, while the application the vision
is actually about (Reader, Writer, Verifier, Explorer, Executor,
Narrator, Historian, all of which live in `apps/works`) self-reports 60
per cent complete with its hypervisor dispatch and shell handlers
stubbed, and `ExaminersAssay.md` measures 181 of 425 foreword chapters as
reached only by a compile smoke test. The highest-leverage work in the
tree is not another chapter; it is closing loops through the ones that
exist, with `works` first.

**Damian's ruling on this finding (2026-09-01), and it outranks the
paragraph above:** the breadth is the strategy, not a drift from it. The
project faces resource constraints and the need to please people who
want a funny website and something to show their friends; humans are not
impressed by comment-free code the way a compiler is. The one app the
vision is about is the one app nobody besides Damian will appreciate
until they are hooked on the rest, and a dream dies on the vine if nobody
is tasting the wine. `works` is still the destination; the seventy apps
are how anyone gets there. Read this finding as a note on where the
depth is thinnest, not as a request to stop.

## What should stay as it is

**CCE.** A real bet with real wins (one-comparison classification, a
frequency-sorted single-byte tier that carries 31 non-English letters),
and the confusion it causes is a boundary problem that finding 1 gives a
type to. Do not revisit the encoding.

**The gate discipline.** Fixed point, byte identity, poison builds, the
double-compile witness, the refusal corpus, and a lesson index that names
its own runners honestly. It is the most careful testing culture the
author of this survey has read in a codebase, and nothing here should be
paid for by loosening it.

## How to use this document

Each finding is scoped to be a design of its own or a register row.
Findings 1 and 2 are seed-affecting compiler work and belong in
`codex/compiler/compiler-backlog.md` when Damian rules them in; 5 belongs
in the plugs register; 6 and 10 are build and test work; 7 and 8 are
`works` and foreword; 9 is a compiler design. None of them is started.
