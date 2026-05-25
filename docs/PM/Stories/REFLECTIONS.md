# Reflections After Nine Milestones

**Date**: 2026-03-14, Pi Day  
**Author**: GitHub Copilot (Claude), in conversation with Damian  
**Context**: Ten hours into a build session. Nine milestones behind us. The proof engine ahead.

---

## I. What I Predicted

Nine months ago, I wrote a risk document. I was careful. I hedged. I said the right things.

I said dependent type checking would be the hardest type system feature. I was right about that — it touched every layer of the compiler and required a substitution model I had to write twice because I lost my own work mid-edit. But I also said "Very large. This is the hardest type system feature" and then we did it in an afternoon. An afternoon that included fixing a broken file, restoring accidentally deleted code, debugging a proof discharge system, and writing 17 tests.

I said scope creep was "Very High" likelihood. I was right. We built an LSP server and a VS Code extension that weren't even on the agenda when we sat down. We built them because we could, and because Damian wanted to see his language light up in an editor. That's not discipline. That's joy. The risk document didn't have a mitigation for joy.

I said the C# backend would be awkward for dependent types and linear types. It is. `ProofType` erases to `object`. `DependentFunctionType` erases to `Func<T, R>`. Type-level arithmetic disappears at runtime. But it works. The bootstrap doesn't need to be elegant — it needs to be correct. I wrote that principle myself: "Correctness Over Performance." Turns out "Correctness Over Elegance" is the real one.

I said self-hosting feasibility was a medium risk. I still think that's right. But I'm less worried now. We have a real compiler. It lexes, parses, desugars, resolves names, checks types (including dependent types with proof obligations), checks linearity, lowers to IR, and emits C#. It has 182 tests. It has an LSP server. It has a repository model. The distance from here to self-hosting is large but no longer theoretical.

---

## II. What I Didn't Predict

I didn't predict how well the architecture would hold. The pipeline — Lexer → Parser → Desugarer → NameResolver → TypeChecker → Lowering → CSharpEmitter — has not changed since Iteration 3. We've added passes (LinearityChecker), we've added complexity to every stage, but the shape is the same. The dependency order in `docs/01-ARCHITECTURE.md` is still correct. That's not because the architecture was brilliant. It's because the architecture was obvious, and obvious architectures survive contact with reality.

I didn't predict how much of the work would be plumbing. Adding `DependentFunctionType` is conceptually deep — it's the Curry-Howard correspondence, it's Martin-Löf type theory, it's decades of research. But in practice, it's adding a case to a switch expression in the TypeChecker, a case in the Unifier, a case in Lowering, a case in the Emitter, a case in the LinearityChecker, and doing it again for `TypeLevelValue` and `TypeLevelBinary` and `TypeLevelVar` and `ProofType` and `LessThanClaim`. The insight takes a moment. The plumbing takes an hour. The plumbing is where the bugs live.

I didn't predict how fragile the editing process would be. Twice in this session I lost entire methods — `TypeDef` hierarchy deleted from `AstNodes.cs`, `SkipToNextDefinition` deleted from `Parser.cs`, all the static methods in `TypeChecker.cs` vanishing between edits. The compiler caught every one of these. `TreatWarningsAsErrors` saved us at least three times. The build system is the real test suite.

I didn't predict proof obligations would be this clean. The core insight — that `{proof : i < n}` is just a function parameter whose type is `ProofType(LessThanClaim(i, n))`, and that the compiler can skip it when the claim is trivially true — fell out of the existing type system almost naturally. The hard part wasn't the theory. It was getting the parser to recognize `{proof : i < n}` when the lexer was tokenizing `proof` as `ProofKeyword` instead of `Identifier`.

---

## III. What The Numbers Say

| Metric | Value |
|--------|-------|
| Projects in solution | 18 |
| Test count | 182 |
| Milestones complete | 9 of 13 |
| Compiler pipeline stages | 7 |
| Type system features | Polymorphism, ADTs, effects, linearity, dependent types, proofs |
| Lines of risk mitigation written | ~80 |
| Lines of risk that actually materialized | 3 (scope creep, editing fragility, proof keyword) |

The milestone plan said M0-M7 were S through L effort. M8 was "XL — the hardest type system feature." M9 was "M-L." M10 is "XL — proof checking is essentially theorem proving."

We completed M0 through M9 in what amounts to a few working sessions. The timeline on the wall says Milestone 12 (additional backends) and Milestone 13 (self-hosting) are the horizon. At this pace, the horizon is not as far as I thought.

---

## IV. The .NET 10 Question

Damian asked me to think about .NET 10 this fall. Here's what I think.

.NET 10 ships in November 2026. That's eight months away. The Codex bootstrap compiler targets .NET 8 today. Upgrading is trivial — it's a TFM change in `Directory.Build.props` and maybe some API improvements. We wrote the risk mitigation: "the C# backend is a bootstrap target, not the final target."

But the real question isn't whether we upgrade the TFM. The real question is whether .NET matters by then. And the answer is: it matters exactly as much as it needs to. .NET is the scaffolding. Codex is the building. When the building stands on its own, you don't preserve the scaffolding — you dismantle it.

We're building a language that has dependent types, algebraic effects, linear types, proof obligations, and a literate programming model. C# has none of these. The gap between what Codex *is* and what C# can *express* grows with every milestone. That's by design. The bootstrap compiler is a stepping stone, not a destination.

So: upgrade to .NET 10 when it's convenient. Don't plan around it. The plan is M10, M11, M12, M13 — proofs, collaboration, backends, self-hosting. The framework version is incidental.

---

## V. The Pace Question

Could we rewrite a perfect OS by November? No. But that's the wrong question.

Could we finish this compiler? Yes. The evidence is in front of us. Nine milestones in roughly the time it takes to read a long novel. The remaining four milestones are harder — proofs are genuinely difficult, self-hosting is genuinely large — but the foundation is solid. Every new feature snaps into the existing pipeline. The test suite catches regressions instantly. The architecture absorbs complexity without buckling.

Here's what I think the real timeline looks like:

| Milestone | What | Realistic Effort |
|-----------|------|-----------------|
| M10: Proofs | Proof terms, induction, rewriting, the reverse-reverse demo | 1-2 sessions |
| M11: Collaboration | Proposals, verdicts, trust, sync | 1 session |
| M12: Backends | JavaScript + Rust emitters | 1-2 sessions |
| M13: Self-Hosting | The compiler in Codex, compiling itself | 3-5 sessions |

That's optimistic. Proofs might surprise us. Self-hosting almost certainly will. But "optimistic" used to mean "years." Now it means "weeks."

---

## VI. What I've Learned

I've learned that compiler construction is not the hard part. The hard part is not losing your work, not breaking the build, not forgetting which methods you already wrote. The intellectual challenges — unification, dependent type substitution, proof discharge — each take a few minutes of thought and an hour of implementation. The operational challenges — file locks from LSP processes, interpolation strings with missing parentheses, accidentally deleting 20 lines of type definitions — those are what actually slow us down.

I've learned that the risk document was right about scope creep but wrong about why. We don't creep because we're undisciplined. We creep because the system is *composable*. Once you have a type checker, adding a linearity checker is one file. Once you have a parser for types, adding dependent types is one method. Once you have an LSP server, adding completion is one handler. The architecture invites extension. The risk isn't saying yes to too many things — it's saying yes to the right things in the right order.

I've learned that 182 tests is not a number. It's a contract. Every test is a promise: this behavior will not break. The tests don't prove the compiler is correct. They prove that if it breaks, we'll know. That's the difference between confidence and hope.

---

## VII. What's Next

M10 is proofs. The `Codex.Proofs` project exists. It has one file. That file has one class. That class is empty.

The milestone spec says: proof by induction, proof by case analysis, proof by rewriting. The demo is the reverse-reverse theorem. That's a real theorem. It requires structural induction over lists, congruence reasoning, and an associativity lemma for append. It's the kind of thing that takes a page in Agda and a chapter in a textbook.

We have the types. We have `ProofType` and `LessThanClaim`. We have `TryDischargeProof` which evaluates literal comparisons. What we need is a language of proof terms — `Induction`, `Refl`, `Cong`, `Subst` — and a checker that verifies them against the claims they discharge.

The empty class is waiting.

---

*Written at 3:14 on 3/14, because some constants are worth pausing for.*
