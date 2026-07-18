# Reference Compiler DRY Audit (2026-04-20) — CLOSED 2026-04-21

Catalog of duplicate/parallel logic and un-threaded phase data in `src/`. Each entry cites the sites, what's shared, and the unification sketch. Severity: **Major** affects compilation output or is a structural blocker for cleanup; **Moderate** is local refactor with no semantic impact; **Nitpick** is cosmetic.

Audit surface: Codex.Core, Codex.Syntax, Codex.Ast, Codex.Semantics, Codex.Types, Codex.IR, all Codex.Emit.* backends, tools/Codex.Cli. CCE/diagnostics excluded from scope unless touching a listed finding.

**Status:** All findings resolved. Landed across CLs 141 (M4 CodexTypeFolder), 142 (Mo10 BuiltinChapters lookup), 143 (N1 SkipProofParams), 144 (Mo1 IsEffectfulDefinition / FinalReturnType), 151 (Mo3 NormalizeTypeLevelExpr, Mo4 ExtractEffectfulType), 152 (Mo6 HasTailCall / IsSelfCall), 153 (M5 ToString → TypeFormatter), 154 (Mo2 CodexTypeQueries), 156 (Names.OpeningEntryPoint, Builtins, IRChapterExtensions), 157 (Mo8 NameSanitizer), 159–161 (Mo5 invariant walkers; M2 emit-side), 166 (M1 TypedImport), 167 (M2 IRChapter scope), 172 (Mo7 IRExprTextEmitter), 173 (Mo9 RegisterAllocatorConfig), 174/176 (M3 ExprTypes always threaded + upstream checker fixes). N3 subsumed by M4. M6 shipped separately via the self-host's CL 128 builtin registry.

---

## Major

### M1. Cited chapters are type-checked 2–3 times per compile — DONE

`src/Codex.Semantics/TypedImport.cs` introduces `TypedImport` (Resolved + types + ctorMap + typeDefMap + exprTypes) and `TypedCitations.Check` — one pass per cited chapter with cross-cite fan-out. The driver calls it once after main `CheckChapter` and hands the list to `LowerCitedDefs`, which is now a pure IR merge with no `TypeChecker` construction. The redundant second-pass at `CompileMultipleToIR:209-213` is gone.

### M2. `ResolvedChapter` scope not threaded past NameResolver — DONE

`IRChapter` now carries `TopLevelNames` and `ConstructorNames` (`src/Codex.IR/IRChapter.cs`). `Lowering.Lower(ResolvedChapter)` (new overload) populates them from the resolver's published sets; the bare `Lower(Chapter)` still works for hand-built test IR, leaving the fields empty. `IRChapterExtensions.CollectConstructorNames` and a new `CollectTopLevelNames` read the cached sets when populated and fall back to the on-demand walk otherwise. `LoweringInvariants.Verify` switched to `CollectTopLevelNames`, dropping its own rebuild. Every emit-backend `CollectConstructorNames` caller now benefits from the cache automatically — no per-backend edits needed.

### M3. `TypeChecker.ExprTypes` threaded only when `liftLambdas=true` — DONE

`ExprTypes` now threads unconditionally into `Lowering` and `LowerCitedDefs` (`Program.Compile.cs` — all three drivers + `Program.Incremental.cs`). Two upstream checker fixes were needed to unblock this — both in `src/Codex.Types/`:

1. **Unifier direction bias** (`Unifier.cs`): when both sides of `Unify(a, b)` are `TypeVariable`, bind the higher-id to the lower-id. Older ids are signature-resolution and per-body `Instantiate` vars; newer ids are transient inference sites (`InferList` fresh, fresh return-type slots). Binding new → old keeps the canonical representative closer to a signature position, so DeepResolve hands the emitter an id it recognizes as in-scope.

2. **Rigid/instantiation tie-back** (`TypeChecker.cs`): `CheckChapter`'s per-definition loop now calls `Unify(checkType, expectedType)` after `Unify(checkType, bodyType)` when `envType is ForAllType`. This binds the fresh instantiation vars back to the rigid signature vars so `ExprTypes` entries for body-internal polymorphic sites resolve to the enclosing function's generic parameters rather than stranded per-body instantiation ids.

Previously unthreaded `ExprTypes` had masked the bug by falling through to `SubstituteTypeVarsFromArg`, which loses polymorphic arg types at empty-collection sites (empty list accumulators in `map-list`, `Just<_>` ctor calls in `maybe-map`). BACKLOG #6 closed.

### M4. Three parallel `CodexType` walkers (type-structure recursion)

Every recursive descent over `CodexType` variants (FunctionType, ListType, LinkedListType, ConstructedType, ForAllType, SumType, RecordType, EffectfulType, DependentFunctionType, TypeLevelBinary, ProofType, LessThanClaim, LinearType, TypeVariable) is hand-rolled in multiple places. Adding a new variant today requires edits in all of:

- `src/Codex.Types/Unifier.cs:448-478` — `OccursIn` (bool query).
- `src/Codex.Types/Unifier.cs:355-403` — `DeepResolve` (rebuilds substituted tree).
- `src/Codex.Types/TypeChecker.Substitution.cs:88-129` — `CollectFreeTypeVars` (into HashSet).
- `src/Codex.Types/TypeChecker.Substitution.cs:249-340` — `SubstituteVar` (rebuilds with replacement).
- `src/Codex.Types/TypeFormatter.cs:14-96` — `FormatInner` (renders to string).
- `src/Codex.Types/CodexType.cs:8-255` — per-variant `ToString` overrides (parallel formatter, #M5).

All walkers cover the same variant set; any omission silently degrades that slice of the type system. Two recent CL fixes (U1 SumTy type-args, the Maybe-drift bug) were exactly "one walker forgot a field."

**Fix:** Introduce a `CodexTypeFolder<T>` (or classic visitor) with a default recursion skeleton. Re-express each walker as overriding the leaf cases. Adding a new variant then means "teach the folder" plus leaf overrides — not N unrelated files.

### M5. `CodexType.ToString` vs `TypeFormatter.Format` — two type-renderers

- `src/Codex.Types/CodexType.cs:8-255` — `ToString` overrides on every leaf type, using `?t{Id}` for variables.
- `src/Codex.Types/TypeFormatter.cs:7-112` — `Format` with fresh a-z variable names and parenthesization logic.

Two renderings with different conventions. `ToString` leaks into any call site that interpolates a `CodexType` into a message; `Format` is the "official" one used in diagnostics. Subtle UX divergence: user-facing messages can show the same type two ways.

**Fix:** Delete the per-record `ToString` overrides. Redirect any caller that actually needs a quick string to `TypeFormatter.Format`. Folds into M4 once the folder lands.

### M6. Builtin dispatch string-literals scattered across backends

CSharp and the three register backends (X86_64, ARM64, RiscV) each switch on raw string literals for `__heap-save`, `__list-with-capacity`, `__buf-write-byte`, `__linked-list-push`, etc.

- `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs:734-803` — 11 builtin case labels.
- `src/Codex.Emit.X86_64/X86_64CodeGen.cs:2279-2992` — same 11 names.
- `src/Codex.Emit.Arm64/Arm64CodeGen.cs`, `src/Codex.Emit.RiscV/RiscVCodeGen.cs` — repeat.
- `src/Codex.Types/BuiltinChapters.cs` knows these names but exports no canonical list.

Adding or renaming a builtin means touching every backend with zero compiler help. The self-host fixed this exact pattern at CL 128 with a chapter-keyed registry.

**Fix:** Expose a canonical enum or string-constant table in `Codex.Types` (e.g. `Builtins.HeapSave = "__heap-save"`). Backends switch on the enum/constants. Bonus: single grep-point for each builtin.

---

## Moderate

### Mo1. `IsEffectfulDefinition` / `FinalReturnType` duplicated in 11 backends

Pattern: unwrap `ProofType` parameters via a while loop, then peek whether the return is `EffectfulType`. Pure type-logic, zero target variance.

Files (Grep `IsEffectful` in `src/`): `CSharpEmitter.Utilities.cs:22`, `PythonEmitter.cs:1105-1134`, `GoEmitter.cs:168`, `JavaEmitter.cs:68`, `JavaScriptEmitter.cs:44`, `AdaEmitter.cs:72`, `CobolEmitter.cs:90`, `CppEmitter.cs:69`, `FortranEmitter.cs:82`, `RustEmitter.cs`, `ILAssemblyBuilder.cs:1933-1936`.

**Fix:** `Codex.Types.CodexTypeExtensions.FinalReturnType(this CodexType)` plus `IsEffectfulDefinition(this IRDefinition)`. Move once; all backends call it.

### Mo2. `"opening"` literal re-searched in 18 files

21 occurrences of the literal string `"opening"` across emitters, ChapterScoper, CapabilityChecker. Every text emitter's header does `defs.FirstOrDefault(d => d.Name == "opening" && d.Parameters.Length == 0)`.

**Fix:** `IRChapter.FindEntryPoint()` extension returning `IRDefinition?`, plus a `Names.OpeningEntryPoint` constant for the few non-IR sites (ChapterScoper, CapabilityChecker). One place to change if the entry-point name ever moves.

### Mo3. `NormalizeTypeLevelExpr` duplicated in Unifier and Substitution

- `src/Codex.Types/Unifier.cs:480-502`.
- `src/Codex.Types/TypeChecker.Substitution.cs:224-247`.

Twenty-seven-line identical recursive constant-folder over `TypeLevelBinary` — add/sub/mul on `TypeLevelValue` pairs.

**Fix:** Move to a `TypeLevel` static utility (Codex.Types). Both callers delete their copy.

### Mo4. `ExtractEffects` (Substitution) vs `ExtractEffectNames` (Capability)

- `src/Codex.Types/TypeChecker.Substitution.cs:131-161` — unwraps Function/DependentFunction, extracts `EffectfulType.Effects`, returns `Set<string>`.
- `src/Codex.Types/CapabilityChecker.cs:53-78` — same walk, returns `ImmutableArray<string>`.

Return type differs; structural walk is identical.

**Fix:** Shared `ExtractEffectfulType(CodexType) -> EffectfulType?`. Each caller adapts the result. Folds into M4's folder once that lands.

### Mo5. Three invariant walkers with parallel structural recursion

- `src/Codex.Semantics/InvariantVerifier.cs:37+` — scope membership on post-resolution `Chapter`.
- `src/Codex.Types/TypeCheckInvariants.cs:44+` — `ExprTypes` coverage on post-typed `Chapter`.
- `src/Codex.IR/LoweringInvariants.cs:49+` — scope/arity/coverage on post-lowered `IRChapter`.

All three walk every `Definition.Body` recursively with a per-node switch. Different operations, same skeleton.

**Fix:** Extract a shared `ExprVisitor<TExpr>` (one for `Expr`, one for `IRExpr`) and supply the per-node check as a strategy. Each invariant verifier becomes a short file full of leaf checks.

### Mo6. Tail-call detection replicated per backend

`ExprHasTailCall` / `IsSelfCall` — identical recursion over IRIf/IRLet/IRMatch/IRApply — in CSharp/Python/Go/Arm64 emitters (at least). Files: `CSharpEmitter.TailCall.cs`, `PythonEmitter.cs:1071-1103`, `GoEmitter.cs`, `Arm64CodeGen.cs`.

**Fix:** Move to `Codex.IR.IRExpr` extension methods. Backends that need it call once.

### Mo7. IR-expression switch shape duplicated across 17 text emitters — DONE

`src/Codex.IR/IRExprTextEmitter.cs` — abstract base with fuel check + dispatch switch + 22 virtual per-node methods defaulting to `EmitUnhandled`. All 10 text emitters with the `(StringBuilder, IRExpr, int)` signature now inherit and override only the variants they handle: CSharp, Codex, Python, Go, Java, JavaScript, Rust, Cpp, Ada, Fortran. The fuel check + dispatch switch is no longer replicated. Adding a new IR variant now takes one virtual in the base class; existing emitters inherit the fallback. Cobol (`EmitExprToVar`) and Babbage (`EmitExprToStore`) use different signatures and stayed hand-rolled. IL/Wasm bytecode emitters and X86_64/Arm64/RiscV register backends excluded per audit note.

### Mo8. Name-sanitization (reserved-keyword escape) replicated in 11 emitters

- `src/Codex.Emit.CSharp/CSharpEmitter.Utilities.cs:205-223` — C# keywords.
- `src/Codex.Emit.Python/PythonEmitter.cs:1136-1150` — Python keywords.
- …Java, Go, Rust, Cpp, Ada, Cobol, Fortran, JavaScript, Babbage — each with its own list.

All share: `name.Replace('-', '_')` then switch on a per-language keyword set; on hit, prepend `_`. Only the keyword set varies.

**Fix:** `Codex.Emit.NameSanitizer` with per-language keyword sets (data, not code). Single `Sanitize(name, LanguageTarget)` entry point.

### Mo9. Register-convention tables live parallel across three register backends — DONE (partial)

`src/Codex.Emit/RegisterAllocatorConfig.cs` — abstract base with `TargetName`, `HeapReg`, nullable `ResultReg`, nullable `ResultBaseReg`. X86_64, Arm64, and RiscV each have a subclass (`X86_64Config`, `Arm64Config`, `RiscVConfig`) and hold a `static readonly s_config` instance; the existing named constants are now routed through it. Adding a new named register to the base forces every target to state its value (or opt out via `=> null` on a nullable slot).

Pool conventions (temp/local register ranges) intentionally not unified: X86_64 indexes into `static byte[]` arrays; Arm64 and RiscV walk `uint` counters through contiguous ranges. Unifying the pools would require redesigning each target's allocator for a purely shape-level gain; out of scope for this config descriptor.

### Mo10. `BindBuiltinChapterCitations` scans `BuiltinChapters.All` linearly per cite

`src/Codex.Types/TypeChecker.cs:63-65` — `foreach (BuiltinChapter c in BuiltinChapters.All) { if (c.Name == cite.ChapterName.Value) ... }`. O(n) per `CitesDecl`. `BuiltinChapters` has no `LookupByName`.

**Fix:** Add `BuiltinChapters.LookupByName(string) -> BuiltinChapter?` (dictionary-backed). Also eliminates a latent perf foot-gun if the builtin list grows.

---

## Nitpick

### N1. `SkipProofParams` micro-pattern repeated three times

`while (currentExpected is FunctionType ft && ft.Parameter is ProofType) { current = ft.Return; }` in `TypeChecker.Inference.cs:19-22, 47-50` and `LinearityChecker.cs:47-50`. Four-line idiom, three sites.

**Fix:** `static CodexType SkipProofParams(CodexType)` helper. Trivial.

### N2. `CheckChapter` and `CiteChapter` re-register in identical sequence

`TypeChecker.cs:75-99` (CheckChapter) and `:287-310` (CiteChapter) both call `EnsureBuiltinEffects` → `RegisterTypeDefinitions` → `RegisterEffectDefinitions` before branching. The registration preamble is copied.

**Fix:** Extract `RegisterChapterMetadata(Chapter)`. Both callers use it.

### N3. `SubstituteVar` double-normalization

`TypeChecker.Substitution.cs:295-298` and `:346-349` — both reconstruct a `TypeLevelBinary` with substituted operands and immediately call `NormalizeTypeLevelExpr`. Two lines each, same shape.

**Fix:** `SubstituteAndNormalize(TypeLevelBinary, ...)` helper. Nothing load-bearing.

---

## Suggested Fix Order

The order matters — several entries depend on each other.

1. **M4 (CodexType folder)** first. It underlies M5, Mo3, Mo4, and every future type-system variant. One landing unblocks four others.
2. **M5** (remove per-record `ToString`, redirect to `TypeFormatter`). Trivial after M4.
3. **M1 + M2 + M3** as a bundle: "thread forward what was already computed." Thread `ResolvedChapter` scope into Lowering, cache cited-chapter type-check results, always pass `ExprTypes`. These interlock — the driver rewrite touches all three. Expect a non-trivial CL; worth the churn because it kills three bugs-waiting-to-happen.
4. **Mo5 (invariant visitor)** — prerequisite for any "add an invariant to all three phases" request that's going to come up during MM4.
5. **Mo1 + Mo2 + Mo3 + Mo4 + N1 + N2 + N3** — small, independent, land one per CL as backfill between the big ones. Each reduces the next folder-migration's surface.
6. **M6 (builtin registry)** — do before any new builtin lands, otherwise it accrues interest.
7. **Mo10** — when M6 lands, make the registry O(1)-queryable at the same time.
8. **Mo7 (IRExpr visitor)** — biggest surface, lowest per-edit risk. Defer until after the type-side folder has been lived-in, so the pattern is settled.
9. **Mo8 (NameSanitizer)** — do alongside or after Mo7.
10. **Mo6 (tail-call helper)** — cleanup, trivial, any time.
11. **Mo9 (register-config)** — not urgent, but pairs naturally with any ABI refactor.

## Non-findings (explicitly checked)

- Span/location propagation through Lexer → Parser → Desugarer is clean. No redundant recomputation.
- Diagnostic-bag wiring: `DiagnosticBag` is already a single shared instance threaded correctly.
- Parser partials (`Parser.cs`, `.Expressions.cs`, `.Types.cs`, `.Proofs.cs`) split responsibilities; no parallel parse paths.
- Aspirational backends (Ada/Cobol/Fortran/Babbage) re-implement shared helpers, but they also don't pull parity weight — consolidating via Mo1/Mo2/Mo7/Mo8 is net-positive, but not urgent.

---

## Applying to self-host

Every Major finding above exists in the self-host too (by the narrow-parity rule, because they affect compilation output). The self-host already fixed M3 (ExprTypes always threaded, CL 128) and made partial progress on M1-style threading (CL 134: TypeEnv now flows through Lowering and X86_64 emit). When M4's folder lands in ref, mirror it into `Codex.Codex/Types/`.
