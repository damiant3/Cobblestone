# Lean and Codex: a feature comparison, and what Codex can do about the gaps

*Written 2026-08-21 by red at Damian's direction. A position doc, not a
design. Every Lean claim below is from lean-lang.org as read that day (the
home page, /about, /documentation, the Language Reference table of contents,
and the FRO Year 4 Part 1 roadmap). Every Codex claim is from a file in this
tree with a path beside it, read the same day; counts are measured, not
carried. Where the two are compared at different altitudes the text says so.*

## 1. The short version

Lean is a dependent type theory with a small trusted kernel, a tactic
language, a metaprogramming system, and a million lines of formalised
mathematics on top. Its verified-software work (Cedar, Aeneas, ArkLib) is
done by writing programs in Lean and proving theorems about them in the same
language, then extracting code through a reference-counted C runtime.

Codex is a systems language whose type system carries what Lean's does not
and lacks what Lean's has. It carries effect rows, linear ownership, declared
cost (`punctual` for time, `bounded` for allocation), bounded integers with a
static range prover, and capability effects for hardware; it compiles itself
to bare metal with no runtime and is a byte-identical fixed point of itself.
Its proof system is real but narrow: propositional equality, structural
induction over sum types with per-constructor subgoals checked by
normalisation, lemma citation, and erasure. It has no tactics, no universes,
no Pi types beyond `for all` over a value, no termination checker, and no
metaprogramming.

**The gap that matters for the mission is not tactics or Mathlib. It is
trust architecture and termination.** Lean's kernel is small and separately
re-checkable and the FRO is shipping `lake check` to make external kernel
verification routine; Codex's checker is inside the compiler and its
independent re-check (`IndependentRechecker.md`, stages 1-3 built) does not
yet cover proofs. Lean checks that every function terminates; Codex leaves
that to fuel counters by convention. Both are things this project already
knows how to do in its own idiom, and section 4 says how.

## 2. Side by side

Altitude note: "Lean" means the language as lean-lang.org describes it,
plus what the reference's chapter list names. "Codex" means what the seed
compiles today, with the file that proves it.

### 2.1 Foundations and proofs

| capability | Lean | Codex | where Codex's claim is measured |
|---|---|---|---|
| type theory | dependent type theory with universes, inductive families, quotients, `Prop`/`Type` | Hindley-Milner with effect rows, plus value-carrying types: `===` (propositional equality), `for all (x : T)` over a value, `Integer between L and H` | `DevelopersGuide.md` "Proofs and Dependent Types", "Bounded Integers" |
| proof terms | full lambda calculus; proofs are programs | a closed grammar: `Refl`, `sym`, `trans`, `cong`, `app-cong`, `assume`, an inductive hypothesis, a cited claim, an `induction` expression, a `let` of proof terms; anything else is CDX4024 | `codex/compiler/Core/CdxCodes.codex:356` |
| induction | general recursion on inductive families, structural and well-founded | structural induction over any sum type (user or builtin `List`), one arm per constructor, missing arm refused, subgoal per constructor computed by substituting the constructor application and normalising both sides against the definitions under fuel, IHs bound, cited lemmas elaborated, arm unified with the subgoal | `codex/compiler/Types/TypeChecker.codex:2561-2613` |
| what a proof can say | any proposition | equalities between terms, universally quantified over values; no implication, conjunction, negation or inequality as propositions | `prop-arith.codex`, `list-induction.codex` (all `===`) |
| flagship | Fermat's Last Theorem formalisation in progress | `reverse (reverse xs) === xs` through a four-lemma chain | `codex/test/reverse-reverse.codex` |
| soundness corpus | the kernel is the argument; one escaped (kernel bug #14576, July 2026, `False` provable) | a negative corpus the checker must refuse: unsound induction, vacuous `qed`, lemma cycles (mutual and self), excluded middle over a three-valued type, false list primitives | `codex/test/errors/{induction-unsound,proof-qed-vacuous,proof-launder-*,kleene-excluded-middle,list-prim-false}.codex`; the Lean bug is cited in `IndependentRechecker.md` section 1 |
| axioms | `axiom`, `sorry`, tracked by `#print axioms` | `assume`, every use warned (CDX4021) and "part of the trust trail" | `CdxCodes.codex:353` |
| erasure | `Prop` is computationally irrelevant | every `Proof` and `PropEqTy` definition erased at emit, reported (CDX4020) | `DevelopersGuide.md` "Proof Erasure" |
| automation | `simp`, `grind` (pattern matching, case analysis, linear inequalities), `bv_decide`, `omega`, `decide`, `mvcgen` for verification conditions | one decision procedure, the static bounds prover: interval arithmetic over `+ - * / negate int-mod if when let` and structural facts about five builtins, eliding runtime range checks (CDX4010, CDX2053); normalisation is what `Refl` does | `DevelopersGuide.md` "Static Bounds Prover" |
| tactics | a tactic language with a proof state | none; a proof is a term | |
| termination | every definition must terminate: structural recursion checked, `termination_by`/`decreasing_by` for well-founded, `partial` and `unsafe` as escape hatches | none; `punctual` forbids self-recursion outright (CDX6005); everywhere else a fuel counter by convention, and the fuels are real (`e1000-reset-fuel`, `proof-norm-fuel`, `db3-dhcp-fuel`) | `DevelopersGuide.md` "Punctual Functions"; `E1000e.codex:234`; `TypeChecker.codex:108` |

### 2.2 What Codex's types carry that Lean's do not

| capability | Lean | Codex | where |
|---|---|---|---|
| effects | monads and `do` notation; effects are a library encoding, not a checked row | effect rows inferred as first-class data and checked at every boundary; `act` blocks; handlers; `Device.Port`/`Device.Block`/`Device.Mmio` capability effects; a hardware access with no capability in scope does not compile | `DevelopersGuide.md` "Effects and Act Blocks", "Grounding Hardware Effects"; `README.md` "Verified" item 3 |
| linearity | none; reference counting with `@&` borrowed annotations and `unsafe` | `linear` exactly-once ownership through moves, calls, closures, containers and tuple components, with the nine laundering routes closed as `.failing` tests | `DevelopersGuide.md` "Linear Types"; `codex/test/errors/linear-launder-*` |
| cost | none in the type system | `punctual` (no heap, no recursion, no closures, no effects, instruction count against a budget, CDX6001-6011); `bounded` allocation class lattice `none < fixed < budgeted < linear < growing`, inferred transitively through callees, refused toward abstention (CDX6101-6103); published cost tables for `List`, `Text` and the builtins | `DevelopersGuide.md` "Punctual Functions", "Bounded Functions", "What List operations cost"; `CostModel.md` |
| integers | `Nat`, `Int`, `UIntN`, `Fin n` | `Integer between L and H` with `wrapping`/`clamping`/`error` overflow modes; a bound is a type but the mode is not part of type identity; the prover elides the check where it can | `DevelopersGuide.md` "Bounded Integers", "Overflow mode is not part of type identity" |
| type classes | yes, with outParams, instance priorities, coercions, deriving | yes, dictionary passing at compile time, return-type polymorphism, parametric instances, missing instance is CDX2040; no coercions, no deriving | `DevelopersGuide.md` "Type Classes" |

### 2.3 Compilation, runtime, targets

| capability | Lean | Codex | where |
|---|---|---|---|
| compiler | Lean written in Lean, compiles to C through an IR; reference-counted runtime; the FRO roadmap names stack allocation and join points as the next optimisations | Codex written in Codex, compiles to native x86-64 on bare metal with no OS, no libc, no runtime and no GC; a hard fixed point of itself (stage 1 CDX = stage 2 CDX, byte-identical, signed) | `CLAUDE.md` "Current State"; `README.md` "Verified" |
| targets | C, then whatever C reaches; WebAssembly via C | 60 plugs: native arm64, riscv, wasm, ptx, spirv, wgsl, t3isa; ELF/PE/IMG containers; source emitters for ada, cobol, fortran, pascal, c#, java, kotlin, swift, rust, go, zig, python, javascript, typescript, ocaml, haskell, scheme, clojure, elixir, lua, nim, d, perl, php, ruby, julia, scala, groovy, objc, and UI frameworks (react, vue, svelte, angular, electron, flutter, compose, maui, wpf, winforms, gtk, qt, swiftui) | `codex/plugs/` directory, measured 2026-08-21 |
| runtime | RC runtime in C; IO monad | none; the OS is Codex (kernel, drivers, net, trust, verify, scheduler) | `README.md` "Library Quires" |
| hardware | via C | boards quire with linear `Board` handles; IoT targets; a VT-x hypervisor in the desk | `codex/boards/`, `HardwareAbstractionLayer.md` |

### 2.4 Trust architecture

| capability | Lean | Codex | where |
|---|---|---|---|
| trusted base | a minimal kernel; the elaborator is untrusted; external kernels (`lean4checker`, `lake check` on the roadmap) re-check the kernel's output | the type checker, unifier and proof checker are inside the compiler; the compiler is trusted as a whole | |
| independent re-check | external checkers exist and are being made routine | `IndependentRechecker` plug: stages 1-3 built, re-derives types, effect rows, linear ownership and bounds from the IR wire and reports a DISAGREE set with abstentions; stage 4 (proof retention, so proofs can be re-checked from the artifact) is OPEN and is the seed-affecting half | `docs/Designs/Active/Tools/IndependentRechecker.md` |
| what a shipped artifact carries | `.olean` carries terms and proofs | a signed CDX carries code; proof terms are erased and not retained, so nothing outside the compiler can re-check a claim from the binary (L-ERASED: `linear T` and `T` produce identical IR text) | `LESSONS.md` L-ERASED |
| the artifact's own loader | none | a 5-phase CDX verifier, Ed25519 signature, self-verifying seed, a verified loader | `codex/os/verify/` |
| mutation testing of the checker | the kernel is small enough to read | a kill-rate corpus: every checker change is measured against programs that must be refused (L-CAPABILITY-LOST, L-FALSIF) | `CostModel.md` section 7; `IndependentRechecker.md` section 9 |

### 2.5 Extensibility, tooling, ecosystem

| capability | Lean | Codex | where |
|---|---|---|---|
| metaprogramming | macros, syntax extensions, custom elaborators and tactics, domain-specific notations | none, by stance: one language, prose banned about our own code, "no premature abstraction"; extensibility is by backend (plugs), not by syntax | `VisionAndVirtues.md` virtue 7; `CLAUDE.md` R-PROSE |
| documentation | Verso (docs-as-code), the Language Reference, FPIL/TPIL/MIL books | column-2 prose under `Section:` headers (CPL, with banned words and an annotation sidecar system), the DevelopersGuide and twelve sibling docs, a Narrator that collapses annotations to plain language | `DevelopersGuide.md` "Codex Prose Language"; `Annotations.md` |
| editor | VS Code extension with a language server, goals view, live proof state | VS Code extension with syntax highlighting and no language server | `UsersHandbook.md:3-7` |
| build and packages | Lake, Reservoir | PowerShell build scripts migrating to a Codex `CompilerDriver`; a content-addressed repository protocol replacing Git (facts, proposals, verdicts, supersession, trust lattice, Ed25519 annotations, Historian) | `apps/works/README.md`; `Build.md` |
| search | Loogle (type-directed search over Mathlib) | `CodeBrowser`, a prefix trie over every chapter's definitions | `apps/works/README.md` |
| playground | live.lean-lang.org | `prism`: Codex source beside every plug's output | `CuratorsCatalogue.md` |
| library | Mathlib (over a million lines), CSLib | the foreword (core, encode, ui, punctual, net, trust), 66 applications, and no formalised mathematics beyond arithmetic claims | `CuratorsCatalogue.md`; `README.md` "Library Quires" |
| AI | lean-beam for agents, Tau Ceti, Hex, Palomar | a local GGUF agent in the desk, AgentGrid fleet coordination, an evidence plug | `apps/works/README.md` "AI Agent System" |
| verified software showcases | Cedar (AWS authorisation), Aeneas (Rust), ArkLib (SNARKs), Veil (protocols) | the compiler itself (fixed point), the seed (signed, self-verifying), compliance evidence generated as a build artifact against CRA, ETSI EN 303 645, IEC 62443, NISTIR 8259A | `docs/Designs/Active/IoT/*`; `codex/plugs/evidence` |

## 3. Two findings made while measuring

Both are the same shape: the code is ahead of what is written about it.

1. **CDX4022's text is false.** `CdxCodes.codex:354` says an `induction`
   proof "is parsed and its obligation accepted as an unproven axiom.
   Structural subgoal checking is not yet implemented (proof-system Stage
   5), so the proof is trusted, not verified." `check-induction-core`
   (`TypeChecker.codex:2561`) implements exactly that checking, and the
   negative corpus proves it can refuse. The code fires 4022 only on the
   `induction-unproven` paths (scrutinee not a bare name, proposition not an
   equality, an arm short of its constructor's fields), so the message
   misdescribes the system every time it prints. Seed-affecting one-line
   fix; routed to the type-system lane (val) rather than done here.
2. **The DevelopersGuide's proof section documents neither `for all` nor
   `induction`.** Its table ends at `cong`; the corpus has used both forms
   since the `reverse-reverse` flagship landed. A reader of the guide would
   conclude Codex proves literal equalities and nothing else. Docs, not
   seed; red will take it after sitting 11.

## 4. What Codex can do to support similar functionality

Ordered by value to the mission (a correct, verified systems stack on
hardware we ship), not by how impressive the Lean feature is. Each entry
says what it buys, what it costs, and where it sits against this project's
own rules (types are the specification; abstain toward refusal; one thing at
a time; no premature abstraction). Items 1-4 are the ones worth doing; 5-7
are deliberate refusals with the reason.

### 4.1 Retain proofs in the artifact and re-check them independently

Lean's trust story is a small kernel plus external re-checkers, and the FRO
is making that routine (`lake check`). Codex already has the shape of the
same thing in `IndependentRechecker.md`: stages 1-3 re-derive types, effects,
ownership and bounds from the IR wire and report disagreements. **Stage 4,
proof retention, is the open half and it is the highest-leverage trust
feature on this list**, because today a proof exists only during the
compile that accepted it. Retaining the claim, the proof term and the
normalisation trace in the CDX (erased from code, kept as data) lets the
rechecker, the verified loader, or a third party re-check every claim a
shipped binary makes, which is what the README's "machine-checked" ought to
mean at the altitude Lean means it. Seed-affecting; one change; the design
already names the wire atom it needs.

### 4.2 A termination check, in the `punctual`/`bounded` idiom

Lean refuses a non-terminating definition unless it is marked `partial`.
Codex has no such check, and the project's own discipline is fuel
counters, which the lessons index documents failing in both directions
(L-TAILGUARD; the 93-second MDIO loop in `E1000e.codex:369`). The first
slice is cheap and fits the existing pattern: a `total` declaration in the
same slot as `punctual` and `bounded`, inferred structurally (every
self-call passes a constructor-pattern subterm of a parameter of sum type,
or a fuel parameter strictly decreased by a literal), refused toward
abstention (CDX61xx-style) where the inference cannot decide, transitive
through callees as CDX6001 and CDX6101 already are. Nothing about
well-founded recursion in the first slice; the `proof-norm-fuel` and
`e1000-*-fuel` shapes are the corpus. The value is not theoretical: every
metal hang this campaign has chased was a question of whether a loop could
end.

### 4.3 Grow the proposition language toward what the corpus already asks for

`kleene-excluded-middle` is a refusal test, and it exists because somebody
wanted to state a law over a three-valued logic. Everything provable today
is an equality. Two additions, both small and both checked by what already
exists: implication between equalities (`a === b -> c === d`, a proof term
that takes a proof), and bounded-integer inequalities as propositions
(`x < 10` for `x : Integer between 0 and 20`), decided by the static bounds
prover that already does the interval arithmetic. That second one turns the
one decision procedure Codex has into the first thing a proof can cite, and
it is the honest beginning of a `grind`: not a tactic language, two
procedures we already run.

### 4.4 A proved foreword, not a Mathlib

Mathlib is the wrong target and the right idea. Codex publishes cost tables
for `List` and `Text` in the guide; it could publish claims beside them:
`list-length (xs & ys) === list-length xs + list-length ys`, the `reverse`
lemmas already in the test corpus promoted to a `ListLaws` chapter that the
foreword cites. Every claim erases, so this costs no code; it costs proof
terms and the normaliser's fuel at compile time. The test for each claim is
the negative form (L-FALSIF): a deliberately false sibling that the checker
must refuse. Start with `List`, because its laws are the ones the induction
checker already handles and its cost table is the one already measured.

### 4.5 A language server, not a tactic view

Lean's editor story is a goals view over a proof state; Codex has no proof
state to show. What Codex lacks that Lean users get is the cheaper half: go
to definition, hover types, diagnostics inline. The `CodeBrowser` trie and
the compiler's CDX diagnostics with spans are the two halves of a language
server already built; the missing piece is the wire between them and an
editor. Not seed-affecting. Do it in Codex through a plug, the way every
other output is done, after the items above.

### 4.6 Refused: universes, Pi types, quotients, a tactic language, macros

These are what make Lean a foundation for mathematics. Codex's vision is a
verified systems stack, and every one of these would weaken the type
system's other job: being the specification of programs that run on
hardware with no runtime. A tactic language is a second language; macros
are a third; the project's stance is one language that reads like a book,
with extension by backend. The right answer to "can Codex formalise
topology" is no, on purpose. Record the refusal so it is not re-litigated.

### 4.7 Refused for now: a reference-counted runtime, `partial`, `unsafe`

Lean's escape hatches exist because its programs run on a runtime. Codex's
programs are the runtime. An `unsafe` in Codex is a `.failing` test that
has not been written yet. Keep it that way.

## 5. Sources

Lean: https://lean-lang.org/ ; https://lean-lang.org/about/ ;
https://lean-lang.org/documentation/ ;
https://lean-lang.org/doc/reference/latest/ ;
https://lean-lang.org/fro/roadmap/y4-1 (all read 2026-08-21).

Codex: the files named in each row, read 2026-08-21 against main 18898.
