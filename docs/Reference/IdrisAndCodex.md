# Idris 2 and Codex: the nearest neighbour we have

*Written 2026-08-23 by fester at Damian's direction. A survey, not a design.
Companion to `AgdaAndCodex.md` and `LeanAndCodex.md`.*

*Every Idris claim below is from the Idris project's own published material as
read that day: `idris-lang.org`, the `idris-lang/Idris2` repository, and the
Idris 2 documentation pages for multiplicities, types and functions, backends,
and installation. **No Idris was installed and nothing here was verified
against Idris's source or against a running Idris.** Where a claim is about
what the documentation does not state rather than about what Idris does not
do, the text says so.*

*Every Codex claim is measured in this tree on 2026-08-23, with a path beside
it. Counts are measured, not carried (L-COUNT).*

## 1. The short version

Idris 2 is a purely functional, dependently typed language built around what
its own site calls Type-Driven Development: write the type, let the machine
tell you what fits it, fill the hole. It is written in Idris 2, it bootstraps
from generated Chez Scheme source committed to the repository, and it carries
roughly 4,041 commits and 3.0k stars.

**Of the three dependent-type projects surveyed in this directory, Idris is
the one whose design decisions most resemble ours.** It is self-hosted, as we
are. It has real linearity, as we do. It puts erasure and linearity in the
same mechanism, which is arguably tidier than our two. Where Agda and Lean
are proof assistants that can also emit programs, Idris is a programming
language that can also prove things, and that is our posture too.

The differences that remain are the ones worth reading: what the trust chain
terminates in, whether effects and cost are in the type system, and whether
totality is checked at all.

## 2. Quantitative Type Theory, against our two mechanisms

This is the interesting comparison and it is the one where they may be ahead.

Idris 2 implements Quantitative Type Theory: every binder carries a
multiplicity, and there are three.

| multiplicity | meaning |
|---|---|
| `0` | erased at run time; may still be referenced in types for compile-time reasoning |
| `1` | used exactly once at run time |
| unrestricted | the default, no usage constraint |

`duplicate : (1 x : a) -> (a, a)` does not type check, because `x` cannot be
used twice. `openDoor : (1 d : Door Closed) -> Door Open` is how a resource
protocol is stated, and the documentation makes the point explicitly that
this gets resource discipline without monadic overhead.

We arrive at the same two guarantees through two separate mechanisms:

| guarantee | Idris 2 | Codex | where |
|---|---|---|---|
| erased at run time | multiplicity `0` on any binder | every definition returning `Proof` or `PropEqTy`, erased at emit and reported CDX4020 | `DevelopersGuide.md` "Proof Erasure" |
| used exactly once | multiplicity `1` on any binder | the `linear` type qualifier, tracked through moves, calls, closures, containers and tuple components | `DevelopersGuide.md` "Linear Types" |

**One mechanism against two is the honest way to put it, and one is better
design.** A multiplicity is a property of a binder, so `0` and `1` compose
with everything the language already has and need no new keyword per
discipline. Ours are two different things that happen to sit near each other,
and we have already paid for that: `DevelopersGuide.md` has to carry an
explicit paragraph saying that the `linear` in `bounded linear` is a cost
class and the `linear` in a type is ownership, sharing a word and nothing
else. QTT would not have produced that collision.

What we can say for ours is what is measured rather than what is elegant.
Sixteen laundering routes are closed as refusal tests
(`codex/test/errors/linear-launder-*`, `linear-capture-*`), the checker
follows a move through `let` and kills the original name, a linear value
crosses a call boundary only through a `linear`-declared parameter (CDX2065),
a bare linear return needs a `linear` return type (CDX2066), and a handler
clause may not capture a linear at all (CDX2067) because a clause may run
zero times or many. The un-tracked edges are written down rather than
implied: container literals in argument or tail position, a linear parameter
of tuple type, and the components of a tuple owner rebound through a second
name.

Nothing read for this survey says how Idris's linearity behaves at those same
edges, and I did not run Idris to find out. **That is the shape of question
worth answering later with an actual installation, and it is not answered
here.**

## 3. Side by side

Altitude note: "Idris" means what `idris-lang.org` and the Idris 2
documentation describe. "Codex" means what the seed compiles today, with the
file that proves it.

### 3.1 Types and proofs

| capability | Idris 2 | Codex | where Codex's claim is measured |
|---|---|---|---|
| type theory | full dependent types, types as first-class values matchable by pattern, interfaces, dependent pattern matching | Hindley-Milner with effect rows, plus value-carrying types: `===`, `for all (x : T)` over a value, `Integer between L and H` | `DevelopersGuide.md` "Proofs and Dependent Types", "Bounded Integers" |
| what a proposition can say | anything expressible in the theory | equalities between terms, universally quantified over values. No implication, conjunction, negation or inequality in proposition position | `DevelopersGuide.md` "What a claim cannot yet say" |
| induction | general recursion over inductive families, checked total | structural induction over any sum type including the builtin `List`, one arm per constructor, subgoal per constructor by substitution and normalisation under fuel, inductive hypotheses bound, cited lemmas elaborated | `codex/compiler/Types/TypeChecker.codex:2561`, `check-induction-core` |
| totality | **coverage is required by default**; a function missing cases is an error unless marked `partial`. Total means terminating for all inputs, or productive. Only total functions are evaluated during type checking | none. `punctual` forbids self-recursion outright (CDX6005); everywhere else a fuel counter by convention | `DevelopersGuide.md` "Punctual Functions" |
| interactive development | holes (`?name`) as the central workflow: query the expected type and what is in scope, split cases, fill incrementally. The site calls this the point of the language | none. A definition is written whole and checked whole | |
| known gaps the project states itself | cumulativity, and rewrite limitations on dependent types | CDX4022's message is stale, and every proposition is an equality | `DevelopersGuide.md`, same section |

Note the row that is missing from ours and is not missing from theirs.
**Coverage checking by default is a large practical guarantee and we do not
have it.** Our nearest thing runs the other way: `DevelopersGuide.md` has a
section titled "Adding a variant: `is otherwise` absorbs it and no checker
will say so", which is a documented hole where Idris has a compiler error.

### 3.2 What Codex's types carry that Idris's do not

| capability | Idris 2 | Codex | where |
|---|---|---|---|
| effects | none in the core. `IO` is a parameterized type that *describes* operations for the runtime to perform, sequenced with `do`. Idris 1's `Effects` library is not part of Idris 2's core | effect rows inferred as first-class data and checked at every boundary; `act` blocks; handlers; `Device.Port` / `Device.Block` / `Device.Mmio` capability effects, so a hardware access with no capability in scope does not compile | `DevelopersGuide.md` "Effects and Act Blocks", "Grounding Hardware Effects" |
| cost | none. A total Idris function may allocate without bound | `punctual`: no heap, no recursion, no closures, no effects, instruction count against a budget (CDX6001-6011). `bounded`: the allocation class lattice `none < fixed < budgeted < linear < growing`, inferred from the body and refused transitively through callees (CDX6101-6103) | `DevelopersGuide.md` "Punctual Functions", "Bounded Functions" |
| bounded integers | `Nat`, `Int`, `Fin n` | `Integer between L and H` with `wrapping` / `clamping` / `error` overflow modes, and a static prover that elides the runtime check where it can (CDX2053, CDX4010) | `DevelopersGuide.md` "Bounded Integers", "Static Bounds Prover" |

This is the same result as the Agda survey and for the same reason. Idris
proves what a program computes and says nothing about what it costs or what
it touches. We prove less about meaning and more about resources, which is
the trade a language that boots on bare metal with no garbage collector has
to make.

### 3.3 Bootstrap, backends and trust

This is where Idris is closest to us and the remaining gap is sharpest.

| | Idris 2 | Codex |
|---|---|---|
| implementation language | Idris 2 | Codex |
| how the bootstrap starts | generated Chez Scheme source committed to the repository, built by Chez or Racket, then the compiler rebuilds itself with the result and runs the tests | `seed/Codex.cdx`, a signed CDX binary committed to the depot, which compiles itself |
| fixed point | the documentation read describes a two-stage rebuild-and-test. **A byte-identical fixed-point check is not described on the pages read**, and I did not check the build scripts | proven: the self-compile output compiled by itself is byte-identical to itself, and it is the standing gate |
| backends | five: Chez Scheme (default), Racket, Gambit, JavaScript/Node, RefC (C with reference counting) | CDX or text from the compiler itself; container formats from plug CDX binaries in `codex/plugs/` |
| runtime dependency | a Scheme runtime, or Node, or the RefC reference-counting runtime | none. The seed boots on bare metal under codex-vm with no OS and no libc |
| what the trust chain ends in | Chez Scheme and the C toolchain under it | the seed, measured today at 2,877,350 bytes |
| scale | roughly 4,041 commits, 3.0k stars | the compiler measured today at 64 files and 56,509 lines of Codex; the whole `codex/` tree at 2,638 files and 559,569 lines |

**The bootstrap-from-committed-generated-source arrangement is the classic
trusting-trust position, and Idris is in it honestly and by design**: the
committed Scheme is readable, and the rebuild-with-the-result step is real
diligence. It is also exactly the arrangement this project decided not to
accept, which is why BS2 and BS3 exist and why the gate proves a byte-identical
fixed point rather than a passing test run after a rebuild.

The distance from RefC to where we stand is smaller than the distance from
Agda's MAlonzo, and it is still a reference-counting runtime and a C
toolchain we would not have written.

### 3.4 Packaging

Idris has `pack`, a package manager built around a curated "pack collection"
that installs a matching compiler and resolves dependencies. That is
materially more than Agda's local `libraries` file with no registry, and it
is a working answer to the ordinary problem.

It is not an answer to ours. A pack collection is a curated list, so trust in
a dependency is trust in whoever curates, expressed nowhere in the artifact.
The repository protocol this project intends -- content addressing, facts,
proposals, verdicts, a trust lattice -- is aimed at making that relationship
checkable rather than social. Nothing in `pack` bears on that, and nothing in
it is a mistake either; the two are answers to different questions.

## 4. Where Idris is ahead, stated plainly

1. **Coverage checking by default, and totality at all.** We have neither,
   and the `is otherwise` hole is a documented consequence.
2. **Type-driven interactive development.** Holes as the primary way to write
   a program, not a debugging aid. It is the language's identity and we have
   no analogue.
3. **One mechanism where we have two.** QTT's multiplicities give erasure and
   linearity from a single annotation on a binder, without the keyword
   collision our two produced.
4. **A working package manager**, for the ordinary meaning of the problem.

## 5. What is worth taking

- **Multiplicity as the shape to grow toward**, if our erasure ever needs to
  be finer than definition-granular. Not a rewrite of `linear`, which is
  enforced and tested; a direction for the erasure half, which is currently
  crude.
- **Coverage checking.** This is the cheapest large win on the list. `is
  otherwise` absorbing a newly added variant with nothing to say about it is
  a real defect class, it is already written down as one, and the check is
  mechanical.
- **Holes.** Same conclusion as the Agda survey, and Idris makes the stronger
  case for it, because Idris is a programming language rather than a proof
  assistant and its users are doing the same kind of work we are.

Not worth taking: the bootstrap arrangement, the backend set, and the runtime
dependency, all three of which are answers to questions we have deliberately
answered differently.

## 6. The three surveys together

| | what it is | its centre of gravity |
|---|---|---|
| `LeanAndCodex.md` | a dependent type theory with a small trusted kernel, tactics, and formalised mathematics | proofs, and a kernel small enough to re-check externally |
| `AgdaAndCodex.md` | a dependently typed language and interactive proof assistant, written in Haskell | type theory itself, and being the place new theory lands first |
| `IdrisAndCodex.md` | a self-hosted dependently typed programming language with QTT | writing programs with types driving the work |
| Codex | a language, compiler, OS, repository protocol and trust lattice | the substrate, and owning every layer of it |

Read across the three, the same two conclusions repeat and are worth stating
once rather than three times. **We are behind all three on what a proof can
say and on interactive development, and we are ahead of all three on effects,
cost and what the trust chain terminates in.** The first gap is a matter of
work anybody could do; the second is a decision none of them made.
