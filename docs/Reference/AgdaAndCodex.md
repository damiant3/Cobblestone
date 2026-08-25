# Agda and Codex: a survey, and the one axis where we are inverted

*Written 2026-08-23 by fester at Damian's direction. A survey, not a design.*

*Every Agda claim below is from the Agda project's own published
documentation as read that day: the repository at `github.com/agda/agda`, and
the 2.9.0 user manual pages for literate programming, run-time irrelevance,
compiler backends, termination checking, and the package system. **No Agda
was installed and nothing here was verified against Agda's source or against
a running Agda.** Where a claim is about what the documentation does not
mention rather than about what Agda does not do, the text says so.*

*Every Codex claim is measured in this tree on 2026-08-23, with a path beside
it. Counts are measured, not carried (L-COUNT).*

## 1. The short version

Agda is a dependently typed language and interactive proof assistant, written
in Haskell, out of Chalmers. The 2.9.0 manual is dated 2026-03-30; the
repository carries roughly 24,200 commits on master. It is the oldest of the
dependent-type vehicles still in daily use, and it is where a good deal of
type theory gets tried first: cubical types, higher inductive types, and
erasure modalities all have a mature Agda implementation.

Agda's proof language is far beyond ours and its interactive editing loop is
something we have no analogue for at all. Our type system carries three
things Agda's does not carry at any strength -- effect rows, exactly-once
linear ownership, and declared cost -- and our trust chain terminates in
ourselves where Agda's terminates in GHC.

**The interesting result is not the feature ledger. It is that Agda has had
literate programming for longer than this project has existed, in six
markups, and its polarity is the exact opposite of ours: in a `.lagda.md`
file the prose is discarded before the type checker sees a byte.** Section 4
is that comparison and it is the part worth reading.

## 2. Side by side

Altitude note: "Agda" means what the 2.9.0 manual and the repository README
describe. "Codex" means what the seed compiles today, with the file that
proves it.

### 2.1 Proofs and foundations

| capability | Agda | Codex | where Codex's claim is measured |
|---|---|---|---|
| type theory | dependent type theory with a universe hierarchy, indexed inductive families, records with eta, instance search, and Cubical Agda for higher inductive types and univalence | Hindley-Milner with effect rows, plus value-carrying types: `===` (propositional equality), `for all (x : T)` over a value, `Integer between L and H` | `DevelopersGuide.md` "Proofs and Dependent Types", "Bounded Integers" |
| what a proposition can say | anything expressible in the theory | equalities between terms, universally quantified over values. No implication, conjunction, negation or inequality in proposition position | `DevelopersGuide.md` "What a claim cannot yet say"; `codex/test/errors/kleene-excluded-middle.codex` is a refusal test precisely because its law is not one equality |
| proof terms | the full lambda calculus; proofs are programs | a closed grammar: `Refl`, `sym`, `trans`, `cong`, `app-cong`, `assume`, an inductive hypothesis, a cited claim, an `induction` expression, a `let` of proof terms. Anything else is CDX4024 | `codex/compiler/Core/CdxCodes.codex:356` |
| induction | structural and well-founded recursion over inductive families | structural induction over any sum type including the builtin `List`, one arm per constructor, missing arm refused, subgoal per constructor computed by substituting the constructor application and normalising both sides under fuel, inductive hypotheses bound, cited lemmas elaborated | `codex/compiler/Types/TypeChecker.codex:2561`, `check-induction-core` |
| termination | checked by default: structural descent on a strict subexpression, call-graph analysis, lexicographic decrease, configurable termination depth (default 3 as of 2.9.0) | none. `punctual` forbids self-recursion outright (CDX6005); everywhere else a fuel counter by convention | `DevelopersGuide.md` "Punctual Functions" |
| automation | instance search, now backed by discrimination trees rather than a linear scan | one decision procedure, the static bounds prover: interval arithmetic over `+ - * / negate int-mod if when let` plus structural facts about five builtins, eliding runtime range checks (CDX2053, CDX4010) | `DevelopersGuide.md` "Static Bounds Prover" |
| interactive development | hole-driven: write `?`, ask the type checker what belongs there, case-split, refine, with editor integration | none. A definition is written whole and checked whole | |
| erasure | the `@0` / `@erased` modality on arguments, record fields, constructors, definitions, data types and whole modules, with the checker forbidding erased values from reaching runtime computation | every definition returning `Proof` or `PropEqTy` erased at emit, reported as CDX4020 | `DevelopersGuide.md` "Proof Erasure" |
| axiom escape | `postulate`, `{-# TERMINATING #-}`, forbidden under `--safe` | `assume`, warned at every use (CDX4021) and part of the trust trail | `CdxCodes.codex:353` |

Agda's erasure is finer-grained than ours by a wide margin. Ours is
definition-granular and sufficient for what our propositions can currently
say, which is the honest reason it has not needed to be finer.

### 2.2 What Codex's types carry that Agda's do not

| capability | Agda | Codex | where |
|---|---|---|---|
| effects | none in the type system. `IO` is a postulated type wired through the FFI; everything else is a library monad | effect rows inferred as first-class data and checked at every boundary; `act` blocks; handlers; `Device.Port` / `Device.Block` / `Device.Mmio` capability effects, so a hardware access with no capability in scope does not compile | `DevelopersGuide.md` "Effects and Act Blocks", "Grounding Hardware Effects" |
| linearity | none. The erasure modality is a zero-versus-many quantity, not exactly-once; quantitative type theory is Idris 2's, not Agda's | `linear` exactly-once ownership tracked through moves, calls, closures, containers and tuple components, with seven diagnostics (CDX2061-2067) and sixteen laundering routes closed as refusal tests | `DevelopersGuide.md` "Linear Types"; `codex/test/errors/linear-launder-*`, `linear-capture-*` |
| cost | none. A total Agda function may allocate without bound | `punctual`: no heap, no recursion, no closures, no effects, instruction count against a budget (CDX6001-6011). `bounded`: an allocation class lattice `none < fixed < budgeted < linear < growing`, inferred from the body, refused transitively through callees (CDX6101-6103) | `DevelopersGuide.md` "Punctual Functions", "Bounded Functions" |
| integers | `Nat`, `Int`, `Fin n`, with the manual noting that pattern matching on a machine integer is slower than on a unary natural | `Integer between L and H` with `wrapping` / `clamping` / `error` overflow modes, the bound part of the type and the mode not part of type identity, with the prover eliding the check where it can | `DevelopersGuide.md` "Bounded Integers" |

This is the substance of the comparison in our favour and it is not a close
call. Agda proves what a program computes and says nothing about what the
program costs or what it touches. We prove considerably less about meaning
and considerably more about resources, which is the correct trade for
something that boots on bare metal with no garbage collector.

### 2.3 Compilation, runtime and trust

| | Agda | Codex |
|---|---|---|
| backends | GHC (via a generated `MAlonzo` Haskell program) and JavaScript (ES6, AMD, CommonJS) | CDX or text from the compiler itself; container formats (ELF, PE, GPT/FAT images) from plug CDX binaries in `codex/plugs/` |
| runtime dependency | the GHC runtime, and the `ieee754` package for `Float`; or Node and a browser | none. `seed/Codex.cdx` boots on bare metal under codex-vm with no OS and no libc |
| self-hosting | no. Agda is a Haskell program | yes, and a hard fixed point: the self-compile output compiled by itself is byte-identical to itself |
| what the trust chain ends in | GHC, the GHC runtime, and the C toolchain under them, none of which Agda checks or built | the seed, measured today at 2,877,350 bytes |
| scale | roughly 24,200 commits, 2.9k stars | the compiler measured today at 64 files and 56,509 lines of Codex; the whole `codex/` tree at 2,638 files and 559,569 lines |

The trust point is the one that matters for this project's stated mission
rather than for a feature table. Every theorem Agda checks is ultimately
cashed out through a runtime it did not write and does not verify. That is a
perfectly reasonable position for a proof assistant, whose product is the
proof rather than the binary. It is not available to us, because our product
is the binary.

### 2.4 Packaging and libraries

Agda uses `.agda-lib` files naming a library, its dependencies by name, its
include paths and its default flags, registered by absolute path in a
`libraries` file under `AGDA_DIR`. Library names may carry a version suffix
and Agda prefers an exact match, taking the newest when none is specified.
**There is no central registry and no dependency resolution service**, per
the manual's own package-system page.

Set against the repository protocol this project intends -- content
addressing, facts, proposals, verdicts, a trust lattice -- Agda has not
attempted the problem. That is not a criticism of Agda; a proof assistant is
entitled to leave distribution to the ambient ecosystem. It does mean there
is nothing here to learn from.

## 3. Where Agda is ahead, stated plainly

Three things, and the first two are large.

**The proof language.** Universes, indexed families, implicit arguments,
instance search, cubical types. Ours is propositional equality and structural
induction over sum types, with lemma chaining, and no implication or
conjunction or negation at all. We can prove `reverse (reverse xs) === xs`
through a four-lemma chain (`codex/test/reverse-reverse.codex`) and that is
genuinely more than most systems languages carry. It is a rounding error
against what Agda's users routinely formalise.

**Totality.** Agda checks that every definition terminates and treats the
escape hatches as escapes: `{-# TERMINATING #-}` cannot be used under
`--safe`, and `{-# NON_TERMINATING #-}` exists as the honest alternative that
declines to reduce during type checking. We have no termination checker.
Every fuel counter in this tree -- `proof-norm-fuel`, `e1000-reset-fuel`,
`db3-dhcp-fuel` -- is a convention enforced by whoever wrote it.

**Hole-driven editing.** Type a `?`, ask the machine what goes in it, split
the case, refine. It is the feature Agda users name when asked what converted
them, and we have nothing pointed at it. Our Reader/Writer/Verifier ambition
is aimed at the same place and has not arrived.

## 4. The inversion: literate programming

Agda supports literate programming in six markups, all of which strip the
prose:

| format | extension | code delimiter |
|---|---|---|
| TeX | `.lagda`, `.lagda.tex` | `\begin{code}` ... `\end{code}` |
| Markdown | `.lagda.md` | a fenced block, optionally tagged `agda` |
| reStructuredText | `.lagda.rst` | lines following `::` |
| Typst | `.lagda.typ` | a fenced block |
| Org | `.lagda.org` | `#+begin_src agda2` |
| Forester | `.lagda.tree` | `\agda{...}` |

The rule in every one of them is the same: all code must appear inside a code
block, and text outside a block is ignored. The reStructuredText page states
the invariant outright -- the file must be a valid Agda file once all the
literate text is replaced by whitespace. `--literate-markdown-only-agda-blocks`
exists so that fenced blocks in other languages can sit in the document as
verbatim text without being type-checked.

**So Agda's literate mode makes it possible for prose to be near the code.
Ours makes prose part of the code.** CPL is a checked subset of English with
three axioms (no implicit referent, no implicit quantity, no implicit order),
banned words, transition markers, scope rules, and prose-notation consistency
warnings, activated by the `prose` compile flag
(`DevelopersGuide.md`, "Codex Prose Language").

It is worth being honest about which polarity is safer, because ours is not.
Inert prose cannot rot into a false claim that the surrounding code appears
to endorse. Ours can and has: R-PROSE exists because 52,393 prose lines
across 2,117 chapters did exactly that, and the block above
`rv-emit-frameless-mod` asserted the opposite of the four-token function
beside it while reading as authoritative. Agda's answer to that hazard is to
give prose no authority. Our answer is to give prose authority and then
enforce it with a checker. That is the harder position, it is the one the
founding vision actually asks for, and we are not yet winning it.

The concrete thing to take from Agda here is narrow and real: **their
delimiters make the boundary between checked and unchecked text mechanical
and visible in the file.** Ours is column position, which is cheaper to type
and easier to get wrong.

## 5. Escape hatches, both sides

Agda's `postulate` asserts a type without a term. Ours is `assume`, and it is
the same device with the same consequence: it produces a `Proof` from
nothing. Agda's answer is `--safe`, a flag that forbids the unsafe pragmas
outright and is the flag a library states it compiles under. Ours is
CDX4021, a warning at every use, and the claim that those uses are "part of
the trust trail".

A warning nothing counts is not a trail. **The cheap, unasked question is how
many `assume` sites this tree actually has and what leans on them**, and it
is answerable with a grep by whoever wants it. A `--safe`-equivalent compile
mode that refuses `assume` outright is the obvious follow-on and does not
exist. Neither is claimed here as a finding; both are named because this
survey is where somebody would look for them.

## 6. What is worth taking

1. **Hole-driven editing**, in whatever form our environment can carry it.
   The largest usability gap between us and any mature dependent-type system.
2. **Finer erasure granularity**, when our propositions grow enough to need
   it. Agda's per-field, per-constructor, per-module erasure is the shape to
   copy, and copying it early would be premature.
3. **A `--safe` equivalent.** A compile mode under which `assume` is a
   refusal rather than a warning, so a unit can state that it compiles
   without axioms.
4. **A visible delimiter for checked prose.** Section 4.

Not worth taking: the package system, the backend strategy, and the runtime,
all three of which are answers to questions we have deliberately answered
differently.
