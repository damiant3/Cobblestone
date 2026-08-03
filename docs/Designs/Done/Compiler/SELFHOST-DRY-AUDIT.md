# Self-host Compiler DRY Audit (2026-04-21) -- CLOSED 2026-04-21

**Status: closed.** All actionable findings landed; M2 deferred with a language-feature trigger documented; Mo7 reclassified as non-finding. CL map is at the bottom of the Fix Order section.

Catalog of duplicate/parallel logic and un-threaded phase data in `Codex.Codex/`. Same triage scheme as the REF audit: **Major** affects compilation output or is a structural blocker; **Moderate** is a local refactor with no semantic impact; **Nitpick** is cosmetic.

Audit surface: all 46 files under `Codex.Codex/` -- Core, Syntax, Ast, Semantics, Types, IR, Emit, plus `opening.codex`. CCE/diagnostics excluded unless touching a listed finding.

Fix order is at the bottom. Work top-down in that sequence -- later items depend on earlier ones.

The REF audit (now at `docs/Done/Compiler/REF-DRY-AUDIT.md`) is closed; every major pattern surfaced there is re-checked here. Items that don't have a self-host analog are listed under "Non-findings" at the end.

---

## Major

### M1. Parallel walkers over CodexType variants

Every recursive descent over `CodexType` variants (`FunTy`, `ListTy`, `LinkedListTy`, `ConstructedTy`, `ForAllTy`, `SumTy`, `RecordTy`, `EffectfulTy`, `TypeVar`, primitives) is hand-rolled in multiple places. Adding a new variant (or changing an existing one's shape -- as CL 175 did for `RecordTy`'s third slot) requires edits to every walker.

- `Codex.Codex/Types/Unifier.codex` -- `deep-resolve-at` (line 438), `occurs-in` (139), `type-tag` (413), `resolve-silent`, `unify`.
- `Codex.Codex/Types/TypeCheckerInference.codex` -- `subst-type-var` (74), `instantiate-type` (61), `strip-fun-args` (499), `unwrap-effectful-or-error`, `is-arithmetic-type`.
- `Codex.Codex/Types/TypeChecker.codex` -- `parameterize-walk`, `resolve-applied-type`.

Each site enumerates ~16 variants. CL 175 fell out of this directly -- adding `(List CodexType)` to `RecordTy` required parallel edits in `subst-type-var`, `deep-resolve`, `unify`, `parameterize-walk`, and `type-desc`/`type-tag`.

**Fix:** Introduce `codex-type-rewriter` / `codex-type-folder` base pattern (analogous to REF's `CodexTypeRewriter` / `CodexTypeFolder<T>` from CL 141). Each walker re-expresses as overriding the leaf cases; structural recursion lives once. Adding a new variant or changing a variant's shape becomes a single-file edit.

### M2. IR-expression dispatch duplicated across text emitters

`Codex.Codex/Emit/CSharpEmitterExpressions.emit-expr-at` (568-593) and `Codex.Codex/Emit/CodexEmitter.emit-expr-at` (259-284) have nearly-identical variant switches over `IRExpr` (`IrIntLit`, `IrNumLit`, `IrTextLit`, ..., `IrRecord`, `IrHandle`, `IrFork`, `IrAwait`, `IrError`). Bodies differ per language; the dispatch shape is identical.

Adding a new `IRExpr` variant (and shape changes to existing variants) requires parallel edits in both. The register-allocating `X86_64Compound` dispatch is different in kind (imperative, per-register) and stays out of scope, mirroring the REF audit's carve-out for X86_64/Arm64/RiscV.

**Deferred.** The REF `IRExprTextEmitter` pattern (CL 172) depends on virtual-method default dispatch and subclass-override semantics. Codex has neither class inheritance nor polymorphic record types, so the equivalent is a record-of-lambdas with a dispatch helper. Investigation during CL 195/M3 cycle surfaced three issues that make the translation net-negative:

1. **No default-handler inheritance.** REF's base class declares virtual `EmitFoo` with an `EmitUnhandled` default; subclasses override only variants they care about. In Codex, records must be constructed with every field present at once -- no partial override. Since both CSharpEmitter and CodexEmitter actually handle all 21 variants, the default-handler benefit is moot for today's callers.

2. **Edit-count net-increase per variant.** With inheritance: add to IRExpr + base switch + optional override per subclass. Without inheritance in Codex: add to IRExpr + shared dispatch record type + dispatch switch + handler entry in each backend's record build. Net: 3-4 edits per variant vs. today's 2 (one switch per emitter).

3. **Per-call closure allocation.** Each backend would build a dispatch record of ~21 lambdas on entry to `emit-expr`. For programs with deep expression trees, the top-level entry is called once per def body, so allocation is bounded. Still a measurable heap cost (~500 bytes/def) with no corresponding DRY win given points 1-2.

The REF-style shared dispatch is an aesthetic win (centralized 22-arm switch, structural per-backend handler mapping) but not a DRY win in this language. Revisit when Codex gains either polymorphic records or record-field defaults -- at that point, the inheritance analog becomes feasible and the net edit count drops.

Adding a new `IRExpr` variant today still means updating both `emit-expr-at` switches. `when`-exhaustiveness checking (when it lands) would catch missed variants at compile time.

### M3. X86_64 builtin dispatch is a raw string-literal if-chain

`Codex.Codex/Emit/X86_64Builtins.codex:666-680` -- 11 `else if name == "__record-set"` / `"__heap-save"` / `"__list-with-capacity"` / ... branches. CSharp already uses a registry (`BuiltinEmitter` table sorted for `bsearch` at `CSharpEmitterExpressions.codex:232-246`), so the self-host has a working precedent.

Adding or renaming a builtin means touching the X86_64 chain with no compiler help, and -- because the CSharp registry is a separate table -- the two can drift (a builtin added to CSharp without an X86_64 branch silently falls through).

**Fix:** Convert `X86_64Builtins` dispatch to the same `BuiltinEmitter`-shape registry (a `BuiltinX86Emitter` record with `name` + `emit` lambda, sorted, `bsearch`-queried). Bonus: expose a shared builtin-name constants list (analog of REF's `Builtins.HeapSave` from CL 156) so CSharp and X86_64 share the identifier source-of-truth.

---

## Moderate

### Mo1. Fun-type unwrapping helpers duplicated across IR + Emit

Two logically-identical operations -- "walk past `FunTy` parameters to the final return" and "walk past `FunTy` params to the trailing return, through `ForAllTy`" -- exist in four files:

- `Codex.Codex/IR/LoweringTypes.codex:55` -- `peel-fun-return`.
- `Codex.Codex/IR/LoweringTypes.codex:109` -- `strip-fun-args-lower`.
- `Codex.Codex/Emit/CSharpEmitter.codex:398` -- `peel-fun-return`.
- `Codex.Codex/Emit/X86_64Compound.codex:361` -- `strip-fun-args-emitter`.
- `Codex.Codex/Emit/X86_64Chapter.codex:302` -- inline `strip-fun-args-emitter` call.

`peel-fun-param` similarly lives in `LoweringTypes` and is duplicated once in `Emit/`.

**Fix:** Single definition in a shared module (`Codex.Codex/Types/CodexTypeHelpers.codex`, alongside `peel-effectful-ty`). Every caller imports and uses that one copy.

### Mo2. `"opening"` literal re-searched across emit + scope phases

Five sites compare against the raw string `"opening"`:

- `Codex.Codex/Emit/CSharpEmitter.codex:395` -- `opening-return-type` scans IR defs.
- `Codex.Codex/Emit/X86_64Chapter.codex:89` -- `emit-call-to st47 "opening"` in the entry-emission path.
- `Codex.Codex/Semantics/ChapterScoper.codex:35` -- excludes `opening` from collision reporting.
- `Codex.Codex/Semantics/ChapterScoper.codex:44` -- counts `opening` definitions.
- `Codex.Codex/Emit/CSharpEmitter.codex:406-407` -- `opening-emit-entry` sites.

If the entry-point name ever changes, every site must be updated. CLAUDE.md Rule 7 codifies `opening` as the language's entry-point; the REF fix (CL 156 `Names.OpeningEntryPoint`) is the precedent.

**Fix:** Expose a single `opening-entry-point : Text = "opening"` constant in `Codex.Codex/Core/Name.codex` or similar, and retire the literal.

### Mo3. Constructor-name collection re-walks in CodexEmitter

`Codex.Codex/Emit/CodexEmitter.codex:546-559` -- `collect-ctor-names` walks the chapter's type-defs every emit to build a `List Text` of variant ctor names, consumed by `skip-def` to suppress already-emitted constructors.

`Codex.Codex/Semantics/NameResolver.codex:128-133` -- `ResolveResult` already carries `ctor-names : List Text`, populated during name resolution.

The resolve-time list is the authoritative one. CodexEmitter doesn't have access to it because `ResolveResult` isn't threaded through `compile-text` past resolution. Same class of issue as REF M2.

**Fix:** Thread `ResolveResult` (or at least `ctor-names`) into lowering, and onto `IRChapter` (new slot, analogous to REF's `IRChapter.ConstructorNames` from CL 167). Downstream consumers read from `IRChapter` directly.

### Mo4. Tail-call detection replicated per-emitter

`Codex.Codex/Emit/CSharpEmitter.codex:158-175` -- `is-self-call` + `has-tail-call` + `has-tail-call-branches` walk `IRExpr` checking for a tail-position recursive call.

X86_64 has its own TCO infrastructure (see commit history for the parameter-locals rewrite) that computes the same predicate inline against the IR shape. Not textually duplicated today, but the *shape* of the predicate is -- and the REF audit's Mo6 flagged exactly this.

**Coverage gap closed (this CL):** pre-existing CSharpEmitter `has-tail-call` omitted the `IrAct` case that X86_64's covered. Consequence: an act-bodied tail-recursive user def (e.g. `loop n = act ... exec (loop (n - 1)) end`) was TCO'd on bare-metal (X86_64) but emitted as a C#-stack-recursing Func&lt;&gt; IIFE on bootstrap1 -- silently stack-blowing at sufficient recursion depth. Fix: added `IrAct` arm to CSharp `has-tail-call` with new `has-tail-call-act` helper (mirroring X86_64), plus a corresponding `emit-tco-act` in the TCO body emitter that inlines act-stmts as plain C# statements and delegates the tail `IrDoExec` to `emit-tco-body` so self-calls hit the `continue` jump. CSharp and X86_64 TCO coverage is now aligned.

**Consolidation deferred.** `is-self-call` differs in impl between backends -- CSharp uses `collect-apply-chain` (which is reused elsewhere in the same file); X86_64 uses direct `IrApply` recursion. Functionally equivalent, structurally different; consolidating would require picking one shape and possibly refactoring call sites of the chain helper. Low payoff today (only 2 backends, aligned coverage). Revisit when a third backend appears or if the chain helper gets a second co-user worth extracting.

### Mo5. Unify direction bias missing from self-host

REF's M3 fix (CL 176) added "when both sides of `Unify(a, b)` are `TypeVariable`, bind higher-id to lower-id." Without this, transient inference vars (empty-list fresh, fresh return-type slots) can become canonical representatives of signature-scope vars, and DeepResolve leaks them to emit as out-of-scope type references.

`Codex.Codex/Types/Unifier.codex` `unify` doesn't have this bias today. The bug won't trigger on the current self-host acceptance battery because the self-host doesn't thread per-AST inferred types into lowering (see Mo6). But it's a latent issue once the threading lands.

**Fix:** Mirror the REF fix: when both `a` and `b` are `TypeVar`, bind the higher-id one to the lower. One-line guard before the existing `TypeVar` cases.

### Mo6. Rigid/instantiation tie-back not done per definition

REF's M3 fix also added `Unify(checkType, expectedType)` per definition with `ForAllTy` env-type to bind instantiation vars back to the rigid signature vars. Without this, body-internal polymorphic sites' `ExprTypes` entries retain the per-body instantiation ids.

`Codex.Codex/Types/TypeChecker.codex` `check-chapter` (or the equivalent per-def loop) should perform the same tie-back after body inference. Today it doesn't -- again, latent until per-AST types are threaded to lowering.

**Fix:** Mirror the REF fix: after inferring the body, if the signature was a `ForAllTy`, unify `checkType` (instantiated) with `expectedType` (rigid).

### Mo7. `is-effectful` predicate on IRDef duplicated

`Codex.Codex/Emit/CSharpEmitter.codex` and `Codex.Codex/Emit/X86_64Chapter.codex` each inline the "unwrap `ProofTy` params, peek final return for `EffectfulTy`" check. Two copies of the same type-logic. Mirrors REF's Mo1.

**Non-finding -- audit was wrong.** Re-checked during CL 192 and this CL. Self-host has no `ProofTy` type (REF-only -- added for refinement types). CSharpEmitter's `opening-emit-entry` dispatches on opening's return-type shape (EffectfulTy / NothingTy / VoidTy / TextTy / other) to pick a C# invocation form. X86_64Chapter has no parallel -- it emits `emit-call-to "opening"` raw and uses the return value register unconditionally. There is no duplication to consolidate today.

**Revisit trigger.** If X86_64 ever gains a structured entry-emit that dispatches on opening's return type (e.g., to format a `Text` result differently than an `Integer`), the two backends would have the same shape-check and the helper becomes worth extracting. Likewise if self-host gains `ProofTy`, both backends will need to strip proof params before looking at returns -- same extraction opportunity. Until then this entry stays as documented non-finding.

---

## Nitpick

### N1. `parameterize-walk` only added `SumTy`/`RecordTy` cases in CL 175

The older `parameterize-walk` fell through to `is otherwise -> ty` for `SumTy`/`RecordTy`, leaving field types un-parameterized. CL 175 added those cases explicitly but didn't remove the `is otherwise` safety net. Small cleanup once M1's folder lands -- the base class's default recursion replaces the `is otherwise`.

### N2. `type-tag` and `type-desc` are parallel diagnostic formatters

`Codex.Codex/Types/Unifier.codex:413` `type-tag` and `:XXX` `type-desc` both format `CodexType` for user-facing messages. Two formatters, two conventions. Same pattern as REF's M5 (`CodexType.ToString` vs `TypeFormatter.Format`).

**Fix:** Delete `type-tag`, route everything through `type-desc` (the more detailed one). Folds into M1 once the folder lands.

---

## Suggested Fix Order

1. ~~**M1 (CodexType folder)**~~ -- landed CL 184 + CL 191 + CL 189's fold addition.
2. ~~**Mo5 (Unify direction bias)**~~ -- landed CL 188.
3. ~~**Mo6 (rigid tie-back)**~~ -- landed CL 197.
4. ~~**Mo1 + Mo2**~~ -- landed CL 192.
5. ~~**M3 (X86_64 builtin registry)**~~ -- landed CL 198.
6. **M2 (IRExpr visitor)** -- **deferred**; see item body for rationale. REF pattern depends on language features Codex lacks.
7. ~~**Mo3 (thread `ResolveResult` into IR)**~~ -- landed CL 199.
8. ~~**N1**~~ -- implicitly resolved by CL 191. `parameterize-walk-children` now has explicit FunTy/ListTy/LinkedListTy/ForAllTy/ConstructedTy/SumTy/RecordTy cases; the `is otherwise` fallthrough only catches leaves (primitives, TypeVar, EffectfulTy -- the last intentional, current self-host does not parameterize into effectful-return types).
9. ~~**N2**~~ -- landed this CL. `type-tag` merged into `type-desc`; duplicate deleted.
10. **Mo4** -- coverage gap closed this CL (CSharp TCO for act-bodied tail recursion matches X86_64). Consolidation-of-impl deferred; see item body.
11. **Mo7** -- audit mis-identified a duplication that doesn't exist in self-host. Documented as non-finding; see item body.

## Non-findings (explicitly checked)

- **Cited-chapter type-check cycles (REF M1 analog).** The self-host's `opening.codex` / `compile-text` path doesn't yet support cross-chapter citations beyond Foreword loading (CL 165/169). No triple-check pattern to flag today; revisit when multi-chapter compilation lands in the self-host.
- **ExprTypes threading (REF M3 analog).** The self-host doesn't record per-AST inferred types into a table consumed by lowering. Same narrow gap, but because the feature doesn't exist, there's no DRY violation to report -- it's a missing feature, not a duplicated one. Mo5/Mo6 are the prerequisites for eventually adding it.
- **Name sanitization (REF Mo8 analog).** Only `CSharpEmitterExpressions.sanitize` (35) exists. `CodexEmitter` emits identifiers verbatim; X86_64 uses mangled names via its own encoding. No duplication.
- **Register convention tables (REF Mo9 analog).** Only one register target (X86_64). Moot until a second register backend lands in the self-host.
- **Builtin chapter lookup O(n) scan (REF Mo10 analog).** The self-host's `BuiltinEmitter` registry is already sorted + `bsearch`-queried (`CSharpEmitterExpressions.codex:164`). No linear-scan lookup to fix.

---

## Applying to REF

Items M1, M2, Mo5, Mo6 here have REF precedents already landed (CL 141, 172, 176 respectively). New findings (M3 registry-in-X86_64, Mo1 peel-fun consolidation, Mo3 `ResolveResult` threading) are self-host-specific and don't need REF mirroring.
