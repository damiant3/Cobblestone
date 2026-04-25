# Self-Host Parity Audit

> **⚠ STALE AS OF CL 128 (2026-04-20).** This audit was written against the
> pre-CL-128 REF. CL 128 consolidated builtins into `src/Codex.Types/BuiltinChapters.cs`
> (chapter-keyed), introduced cite-gating (typed builtins require an explicit
> `cites Codex chapter X`), renamed runtime primitives with a `__` prefix,
> reserved `Nothing` as a lexer keyword, added a new `src/Codex.IR/LambdaLifting.cs`
> pass, fixed `SubstituteTypeVarsFromArg` to walk `ConstructedType` / `SumType`
> / `RecordType`, threaded `TypeChecker.ExprTypes` into `Lowering`, added
> state-effect emit (`IRRunState` / `IRGetState` / `IRSetState`), gave
> user-defined functions emit priority over same-name builtins, and turned on
> SSE on bare-metal entry. Many rows below reference the pre-reset REF and
> are now misleading: some "matched" rows were matched because both sides
> shared a latent bug; some "gap" rows have been superseded by a REF
> mechanism this audit doesn't describe; some "nitpick" rows now have
> load-bearing implications. Do not treat this matrix as a current
> instruction set.
>
> The doc is retained for historical context and as a shopping list for
> a post-BS1 re-audit. The live port guide is
> `docs/Active/Compiler/REF-LESSONS-FOR-SELFHOST.md`; the live correctness
> gate is `tools/ref-sweep.sh`.

## What parity means

See **principle 11 "Parity Is Narrow"** in `docs/10-PRINCIPLES.md`. Briefly:
the reference compiler (`src/`) is a **baseline**, not a mirror. The self-host
(`Codex.Codex/`) is expected to be a strict superset — doing more, doing
better, diverging on shape.

The parity requirement is narrow and sharp. Only things that affect the
**compilation output** must mirror precisely — lexing, parsing, desugaring,
type checking, lowering, codegen semantics. Two compilers operating on the
same source must reach the same program; `pingpong.sh` is the acceptance
test.

## Classification

Every difference between ref and self-host falls into one of four buckets:

- **Gap** — self-host is missing or wrong; fix the self-host. Affects
  compilation output.
- **Bug-parity** — both sides share the same defect, often because one
  side was written mirroring the other. **Never preserve.** Fix in ref
  AND self-host together in the same CL.
- **Nitpick** — UI/diagnostics wording, output ordering, naming
  convention differences. Explicitly out of scope per principle 11.
  Record as deliberate divergence; leave alone.
- **Deferred** — a gap that is genuinely not urgent (future
  capability, e.g. multi-chapter loading before OS stack needs it).
  Mark and keep visible; fix when the blocking work arrives.

When in doubt: does this change what program comes out the far end of
the pipeline for a legitimate input? If yes, it's a Gap (or Bug-parity).
If no, it's a Nitpick.

## Parity matrix

Legend: 🟡 partial / different · ❌ missing · ⏭️ deliberately diverged

Findings are grouped by file pair. Each row cites file:line on both
sides so it's verifiable. "Status" is one of Gap / Bug-parity / Nitpick
/ Deferred.

---

## NameResolver

`src/Codex.Semantics/NameResolver.cs` vs `Codex.Codex/Semantics/NameResolver.codex`
(audited 2026-04-19 post CL 96 / CL 98)

| # | Topic | Ref | Self-host | Status | Notes |
|---|-------|-----|-----------|--------|-------|
| N1 | `IsTypeName` uppercase check | `char.IsUpper(Value[0])` — Unicode (`src/Codex.Core/Names.cs:5`) | `code >= char-code 'E' & code <= char-code 'Z'` — CCE-native (`Codex.Codex/Semantics/NameResolver.codex:65`) | ✅ Matched | Not a gap despite appearances. In CCE (`src/Codex.Core/CceTable.cs:16–22`), uppercase letters occupy contiguous bytes 39–64 in frequency order: `E=39, T=40, A=41, D=48, L=49, …, Z=64`. So `>= 'E' & <= 'Z'` translates to CCE `>= 39 & <= 64`, which spans all 26 uppercase letters. The literal range looks wrong to Unicode eyes but is correct under CCE's encoding design. Relies on CCE keeping uppercase contiguous — stable design invariant. |
| N2 | Duplicate type-def detection | Emits CDX3001 DuplicateDefinition when a chapter repeats a type name (`src/Codex.Semantics/NameResolver.cs:81–88`) | `collect-ctor-names` + `check-type-name-dup` emit CDX3001 on duplicate type-def names (`Codex.Codex/Semantics/NameResolver.codex`) | ✅ Matched | Sorted-accumulator duplicate check, same diagnostic code and message template. |
| N3 | Duplicate constructor detection | Emits CDX3001 when a variant repeats a ctor name (`src/Codex.Semantics/NameResolver.cs:92–100`) | `collect-variant-ctors` emits CDX3001 on duplicate ctor names (`Codex.Codex/Semantics/NameResolver.codex`) | ✅ Matched | Same pattern as N2. |
| N4 | Effect op registration | Adds each effect operation name to topLevel scope and errors on collision with existing name (`src/Codex.Semantics/NameResolver.cs:104–119`) | `collect-effect-op-names` + `collect-ops-of-effect` register ops and emit CDX3001 on collision with top-level, ctor, or prior op names; registered op-names joined into the resolve scope by `resolve-chapter-with-citations` (`Codex.Codex/Semantics/NameResolver.codex`) | ✅ Matched | Enables user-declared effects without relying on `builtin-names`. |
| N5 | Citation / multi-chapter resolution | `IChapterLoader` hook resolves cited chapters on demand (`src/Codex.Semantics/NameResolver.cs:121–136`) | `ChapterScoper` mangles cross-chapter names before NameResolver runs; `resolve-chapter-with-citations` takes `List ResolveResult` but is always passed `[]` because the names are already flat by then | ⏭️ Deliberately diverged | Different architecture, equivalent result. Ref resolves-with-imports; self-host scopes-then-resolves. Leave the dead `imported` parameter for now — if nothing forces its use, worth removing in a later cleanup. |
| N6 | "Did you mean X?" suggestion | `StringDistance.FindClosest` appended to UndefinedName diagnostic (`src/Codex.Semantics/NameResolver.cs:174–178`) | Plain "Undefined name" message | **Nitpick** | Diagnostic wording is UX per principle 11. |
| N7 | Fuel sentinel wording | "compiler resource exhausted in name-resolver.ResolveExpr (depth 256)" | "compiler resource exhausted in name-resolver.resolve-expr (budget 256)" | **Nitpick** | Identifier casing + `depth`/`budget` word choice. Both surface the same event. |
| N8 | Builtin names list | `s_builtins` in `src/Codex.Semantics/NameResolver.cs:28–54` | `builtin-names` in `Codex.Codex/Semantics/NameResolver.codex:29–54` | ✅ Matched | Matched byte-for-byte as of CL 96. Keep in sync when ref changes. |
| N9 | Fuel-check mechanics | `m_depth` field + try/finally inc/dec (true stack-depth tracking) | Function-parameter depth (true stack-depth tracking) | ✅ Matched | Same semantics, different shape. |

### Follow-ups

1. **N5** — if a future cleanup removes the `imported` parameter path
   entirely, document it; otherwise leave.
2. **N1, N6, N7, N8, N9** — no action.

---

## Unifier

`src/Codex.Types/Unifier.cs` vs `Codex.Codex/Types/Unifier.codex`
(audited 2026-04-19)

### Type surface

Self-host's `CodexType` union (`Codex.Codex/Types/CodexType.codex:6–23`)
covers primitives, `FunTy`, `ListTy`, `LinkedListTy`, `TypeVar`,
`ForAllTy`, `SumTy`, `RecordTy`, `ConstructedTy`, `EffectfulTy`.

Ref (`src/Codex.Types/CodexType.cs`) additionally carries
**`LinearType`**, **`DependentFunctionType`**, **`TypeLevelValue`**,
**`TypeLevelBinary`**, **`TypeLevelVar`**, **`ProofType`**,
**`LessThanClaim`**, **`EffectRowVariable`**, and structured
`EffectType` (with a row variable on `EffectfulType`).

Every advanced-type-shaped gap in the Unifier is a consequence of
those types not existing on the self-host side.

### Findings

| # | Topic | Ref | Self-host | Status | Notes |
|---|-------|-----|-----------|--------|-------|
| U1 | Sum × Sum unification propagates type args | Unifies ctor field-by-field and `TypeArguments[i]` × `TypeArguments[i]` (`src/Codex.Types/Unifier.cs:107–130`) | Name match → `{success=True, state=st}` — no field or arg unification (`Codex.Codex/Types/Unifier.codex:300–317`) | **Gap** | Leading suspect for the Maybe bare-metal drift. A function `Just : a -> Maybe a` applied to `Integer` needs `Unify(Maybe a, Maybe Integer)` to bind `a ↦ Integer`. Self-host silently succeeds without the binding, so downstream sees an unbound `TypeVar`. Only masked when the same `Maybe` value is represented as `ConstructedTy "Maybe" [TypeVar a]`, which routes through `unify-constructed-args` (line 352) where arg unification does happen. The representation choice leaks through. |
| U2 | Record × Record unification propagates type args | Unifies fields and `TypeArguments` pairwise (`src/Codex.Types/Unifier.cs:132–146`) | Name match → silent success (`Codex.Codex/Types/Unifier.codex:318–335`) | **Gap** | Same shape as U1 for records. |
| U3 | Constructed ↔ Sum / Record propagates type args | Unifies `Arguments[i]` × `TypeArguments[i]` on name match (`src/Codex.Types/Unifier.cs:186–224`) | Name match → silent success; arg/field lists ignored (`Codex.Codex/Types/Unifier.codex:283–294, 301–312, 319–330`) | **Gap** | Same family as U1/U2. |
| U4 | Effectful row-variable unification | `UnifyEffectRows` binds row vars in both directions on effectful-vs-effectful, effectful-vs-plain (`src/Codex.Types/Unifier.cs:427–446`) | EffectfulTy×EffectfulTy only unifies the return types; effect rows are dropped (`Codex.Codex/Types/Unifier.codex:337–343`) | **Gap** | Row-polymorphic effects (functions that abstract over their effect set) won't unify correctly. Not currently hit by self-host source which uses concrete effects only. |
| U5 | Resolve on fuel exhaustion | Returns `ErrorType.s_instance` + emits CDX9001 (`src/Codex.Types/Unifier.cs:316–322`) | State-threading `resolve`/`resolve-at` return `ErrorTy` and emit `cdx-resource-exhausted` using `st.context-span` (`Codex.Codex/Types/Unifier.codex:85–100`). `resolve-silent` used by `occurs-in` and `deep-resolve-at` returns `ErrorTy` without emitting | ✅ Matched on the unify path | Remaining gap: `deep-resolve-at` (state-less) still swallows exhaustion; would require threading state through 11 external Lowering sites. Accepted because ref's DeepResolve also depends on inner Resolve for emission, and self-host's Lowering callers never carry a reference-cycle-producing substitution chain. |
| U6 | LinearType support | Unifies `Unify(la.Inner, lb.Inner)` (`src/Codex.Types/Unifier.cs:148–151`) | Type not in `CodexType` union | **Gap (deferred)** | Unused until linearity / affine types become a compilation target for self-host. |
| U7 | DependentFunctionType support | Three cases: DFT×DFT, DFT×FT, FT×DFT (`src/Codex.Types/Unifier.cs:238–254`) | Type not in `CodexType` union | **Gap (deferred)** | Unused until dependent types / pi types enter the self-host. |
| U8 | TypeLevelValue / TypeLevelBinary / TypeLevelVar | Full structural unification + `NormalizeTypeLevelExpr` folding (`src/Codex.Types/Unifier.cs:256–285, 480–502`) | Not in union | **Gap (deferred)** | Compile-time arithmetic on type-level values. Post-MM4 feature. |
| U9 | ProofType / LessThanClaim support | Unified via `Claim` / `Left`/`Right` (`src/Codex.Types/Unifier.cs:287–295`) | Not in union | **Gap (deferred)** | Proof-system integration. V3+ feature. |
| U10 | Vector ↔ List special case | `ListType` unifies with `ConstructedType "Vector"` 2-arg form, taking `Arguments[1]` as the element (`src/Codex.Types/Unifier.cs:226–236`) | No special case | **Gap (deferred)** | Vector type not yet in self-host. |
| U11 | `types-equal` fast path | N/A — ref uses `.Equals` on the union (structural for records) | Self-host `types-equal` handles primitives + TypeVar only, returns False for composite types (`Codex.Codex/Types/Unifier.codex:145–175`) | **Nitpick** | Inefficiency, not incorrectness. Composite `Unify` correctly reaches the same answer via the full dispatch. |
| U12 | Mismatch diagnostic quality | `DeepResolve` + `TypeFormatter.Format` (`src/Codex.Types/Unifier.cs:504–518`) | `type-tag` shorthand: "Integer", "Fun", "Sum:Maybe" (`Codex.Codex/Types/Unifier.codex:379–398`) | **Nitpick** | Message wording per principle 11. Same `cdx-type-mismatch` code, same span. |
| U13 | Substitution store shape | `Map<int, CodexType>` — sparse (`src/Codex.Types/Unifier.cs:8`) | `List CodexType` indexed by var id, self-reference = unbound (`Codex.Codex/Types/Unifier.codex:14–17`) | ⏭️ Deliberately diverged | Same lookup semantics. List is O(1) index at O(N) memory in max var id. |
| U14 | Pre-seeded TypeVar 0, 1 slots | No preallocation (Map starts empty) | `substitutions = [TypeVar 0, TypeVar 1]`, `next-id = 2` (`Codex.Codex/Types/Unifier.codex:28–32`) | ⏭️ Deliberately diverged | Two reserved slots; harmless. Purpose unclear — could be investigated in a later cleanup. |
| U15 | `ContextSpan` on fuel / mismatch fallback | Optional `ContextSpan` property used as last-resort span when neither explicit span nor current arg-span is available (`src/Codex.Types/Unifier.cs:19, 32–46, 504–517`) | `UnificationState.context-span : SourceSpan`; `unify` entry pins it to the call-site span; `resolve-at` uses it on fuel exhaustion (`Codex.Codex/Types/Unifier.codex:18, 92, 149–150`) | ✅ Matched | Self-host initializes to `synthetic-span` and refreshes it at every `unify` entry. |
| U16 | `CharTy` in `unify-structural` enumeration | N/A (switch handles all types via `is X` patterns) | `CharTy` absent from the explicit `when a is X ->` arms; handled by the `is otherwise ->` fallback (`Codex.Codex/Types/Unifier.codex:193–350`) | **Nitpick** | Works correctly (types-equal short-circuits CharTy×CharTy, fallback handles CharTy×ErrorTy; everything else is a genuine mismatch). Asymmetric with other primitives. Cosmetic only. |
| U17 | Fuel mechanics | `m_depth` field + try/finally inc/dec (`src/Codex.Types/Unifier.cs:32–47, 52, 307`) | Function-parameter depth (`Codex.Codex/Types/Unifier.codex:102–110` et al) | ✅ Matched | Same stack-depth semantics, different idiom. |

### Follow-ups

1. **U1–U3 (likely Maybe drift root cause)** — add field/arg
   unification to the Sum×Sum, Record×Record, and Constructed↔Sum /
   Constructed↔Record arms of `unify-structural`. Mirror the ref's
   shape. Coordinate with Hex-Hex's in-flight drift fix.
2. **U4** — add an `unify-effect-rows` path once self-host's
   `EffectfulTy` carries a row variable. That's a `CodexType`
   surface change, not just a Unifier change.
3. **U6–U10** — deferred until the self-host's `CodexType` surface
   grows to cover these. Re-evaluate per feature as it lands.
4. **U11, U12, U13, U14, U16** — no action; deliberate divergence
   or cosmetic.

---

## Desugarer

`src/Codex.Ast/Desugarer.cs` vs `Codex.Codex/Ast/Desugarer.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| D1 | LinearTypeExpr preservation | **Gap (deferred)** | Ref preserves `LinearTypeExpr` wrapper (`src/Codex.Ast/Desugarer.cs:321–323`); self-host strips and recurses into the inner (`Codex.Codex/Ast/Desugarer.codex:168`). Linear-type information is dropped. Unused until linearity enters type-checker. |
| D2 | DependentTypeExpr / IntegerLiteralTypeExpr / BinaryTypeExpr / ProofConstraintExpr | **Gap (deferred)** | Ref has all four (`src/Codex.Ast/Desugarer.cs:324–341`); self-host parser doesn't produce these nodes, desugarer has no arm. Ties to Unifier U7–U9. |
| D3 | InterpolatedString desugaring | **Gap (deferred)** | Ref lowers `"x=${v}"` to `"x=" ++ show(v)` chain (`src/Codex.Ast/Desugarer.cs:221–258`). Self-host parser doesn't produce `InterpolatedStringNode`. User code using string interp won't compile on self-host. |
| D4 | Claim / Proof / ProofExpr desugaring | **Gap (deferred)** | Ref has `DesugarClaim`, `DesugarProof`, `DesugarProofExpr` with all proof constructors (refl, sym, trans, cong, induction, apply) (`src/Codex.Ast/Desugarer.cs:411–467`). Self-host: no proof system. V3+ feature. |
| D5 | ProseByFile / multi-file prose metadata | ⏭️ Deliberately diverged | Ref builds `Dictionary<string, ChapterProse>` indexed by filename (`src/Codex.Ast/Desugarer.cs:61–65`). Self-host is single-chapter; section-titles stored flat on AChapter. |
| D6 | Fuel sentinel span | **Nitpick** | Ref threads `node.Span` through the diagnostic. Self-host uses `synthetic-span` for the sentinel AST node, losing source location. Cheap fix: extract a span from the node's AST discriminant. |
| D7 | Exhaustive-match fallback | **Nitpick** | Ref has `_ => new ErrorExpr($"unknown expression node: {node.Kind}", ...)` (`src/Codex.Ast/Desugarer.cs:197`). Self-host `when` relies on compile-time exhaustiveness. Same class of safety. |

No bug-parity.

---

## Lexer

`src/Codex.Syntax/Lexer.cs` vs `Codex.Codex/Syntax/Lexer.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| X1 | Indent / Dedent tokens | ⏭️ Deliberately diverged | Ref emits `Indent`/`Dedent` via an indent stack and a two-phase `NextToken`. Self-host has no indent stack; parser handles columns directly. Both work; ref is easier to reason about, self-host is simpler to implement. |
| X2 | Interpolated string literals (`"x=#{v}"`) | **Gap (deferred)** | Ref produces `InterpolatedStart`, `TextFragment`, `InterpolatedExprStart`, `InterpolatedExprEnd`, `InterpolatedEnd` tokens (`src/Codex.Syntax/Lexer.cs:300–376`). Self-host has no `scan-interpolated-string` path. User code using interp won't compile on self-host. |
| X3 | CCE-invalid escape diagnostics | ✅ Matched | Self-host emits `cdx-invalid-tab-escape` (CDX5) / `cdx-invalid-carriage-return-escape` (CDX6) from `process-escapes` (text literal) and `scan-char-literal` (char literal). `LexState` carries an error accumulator; `tokenize` returns `TokenizeResult { tokens, errors }` which `compile-frontend` merges into the pre-emit bag. Translation behavior preserved. |
| X4 | Numeric underscores | ⏭️ Deliberately diverged | Ref accepts `1_000_000` and cleans via `.Replace("_", "")` before parse. Self-host's `scan-digits-end` also consumes `_` but numeric parse is deferred. Same effect. |
| X5 | Pre-parsed `LiteralValue` on tokens | ⏭️ Deliberately diverged | Ref tokens carry a parsed `LiteralValue` (long / double / string / bool). Self-host tokens carry only raw text; parse happens in desugar/lowering. Same downstream result. |
| X6 | `cc-cr = -1` sentinel | ⏭️ Deliberately diverged | CCE drops `\r` at the I/O boundary, so `\r` never appears in the scanner's input. Self-host sets `cc-cr = -1` as a never-matching sentinel so the conditional can stay in place without firing. Comment-worthy but not a bug. |

No bug-parity. X3 is actionable; others are architectural or scoped to unsupported features.

---

## Parser

`src/Codex.Syntax/Parser*.cs` vs `Codex.Codex/Syntax/Parser*.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| P1 | Claim / Proof / Qed parsing | **Gap (deferred)** | Ref has `TryParseClaim` / `TryParseProof` / full proof-expression parser (`src/Codex.Syntax/Parser.Proofs.cs`). Self-host reserves the `ClaimKeyword`/`ProofKeyword`/`QedKeyword` tokens but has no `parse-claim` / `parse-proof` paths. A chapter starting with `claim …` would fall through to "expected a definition". |
| P2 | Interpolated string expressions | **Gap (deferred)** | Ties to X2. Ref builds `InterpolatedStringNode` from the Interpolated* token stream. Self-host has no such AST node. |
| P3 | Dependent type syntax | **Gap (deferred)** | Ref has `ParseDependentType` for `(x : T) -> U` form. Self-host's type parser has no dependent-type arm. |
| P4 | Where-clause / suchthat clause on definitions | 🟡 Partial | Ref has `WhereKeyword`-scoped helpers. Self-host reserves `WhereKeyword`/`SuchThatKeyword` but inlining unclear — call sites exist in parse-top-level for effect bodies only. Worth a deeper pass if/when where-clauses get used. |
| P5 | Type-level expressions (Integer literals, binary ops in type position) | **Gap (deferred)** | Ref has `IntegerTypeNode` / `BinaryTypeNode` / `ProofConstraintNode`. Self-host's type parser has no arms for these. |
| P6 | Linear-type syntax | 🟡 Partial | Ref produces `LinearTypeNode`. Self-host parses `linear T` as `LinearTypeExpr` but desugar-type-expr strips it (D1). Lexer recognizes; parser produces; downstream drops. |
| P7 | Page marker handling | ⏭️ Deliberately diverged | Ref recognizes `Page N` / `Page N of M` and records on `DocumentNode`. Self-host skips page markers silently via `is-page-marker` in `parse-top-level`. Both skip them semantically. |
| P8 | Fuel semantics | ⏭️ Deliberately diverged | Ref uses stack-depth (256). Self-host uses total-work fuel in `ParseState.fuel` (10M). Different classes — self-host catches exponential blowup too. See CL 95 history. |

No bug-parity.

---

## ChapterScoper

`src/Codex.Semantics/ChapterScoper.cs` vs `Codex.Codex/Semantics/ChapterScoper.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| C1 | Multi-file chapter combine | ✅ Matched (architecturally) | Both walk per-file chapters, detect name collisions, mangle with chapter slug. |
| C2 | `opening` collision handling | ✅ Matched | Both hard-error on two `opening` defs; never mangle the entry point. |
| C3 | Selective cite aliases (`cites X chapter Y (a, b, c)`) | ✅ Matched | Both route selected names through `{importedSlug}_{name}`. |
| C4 | Case folding in `slugify` | ⏭️ Deliberately diverged | Ref uses `.ToLowerInvariant()` (Unicode). Self-host uses CCE-native `c - 26` (uppercase block 39–64 shifts down to lowercase block 13–38). Correct in CCE. |
| C5 | `ClaimDef` / `ProofDef` merge across files | **Gap (deferred)** | Ref scopes `allClaims` and `allProofs`; self-host doesn't carry claim/proof decls. Ties to P1. |

Low priority. No bug-parity.

---

## TypeEnvironment

`src/Codex.Types/TypeEnvironment.cs` `WithBuiltins` vs `Codex.Codex/Types/TypeEnv.codex` `builtin-type-env`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| E1 | `list-contains` type | ✅ Matched | Self-host binds `ForAllTy 0 (FunTy (ListTy (TypeVar 0)) (FunTy (TypeVar 0) BooleanTy))` (`Codex.Codex/Types/TypeEnv.codex:73`). |
| E2 | `run-process` type | ✅ Matched | Self-host binds `FunTy TextTy (FunTy TextTy TextTy)` (`Codex.Codex/Types/TypeEnv.codex:88`). |
| E3 | `run-state` type | 🟡 Partial | Self-host binds `ForAllTy 0 (ForAllTy 1 (FunTy (TypeVar 0) (FunTy (TypeVar 1) (TypeVar 1))))` — effect-stripped (`Codex.Codex/Types/TypeEnv.codex:92`). Effect-polymorphic version belongs to E6/U4 bucket. |
| E4 | `int-mod`, `abs`, `min`, `max` types | ✅ Matched | Self-host binds all four (`Codex.Codex/Types/TypeEnv.codex:110–113`). |
| E5 | Stale `filter` / `fold` bindings | ✅ Removed | Both bindings dropped; NameResolver `builtin-names` already excludes them. |
| E6 | Effect-polymorphic `map` / `fork` / `await` / `par` / `race` | **Gap** | Ref binds these with `EffectRowVariable` so user code like `map : (a → [e] b) → List a → [e] List b` unifies correctly through effect rows. Self-host binds plain signatures without effect polymorphism. Programs that pass effectful functions to `map` would fail type-check on self-host. Ties to Unifier U4. |
| E7 | `Task` type constructor | ⏭️ Deliberately diverged | Ref uses `ConstructedType(new Name("Task"), [forkA])`. Self-host does the same (`ConstructedTy (Name { value = "Task" }) [TypeVar 0]`). Matched shape, deliberately uses Constructed because `Task` isn't in the builtin type union. |
| E8 | `Concurrent` effect annotation on fork/await/par/race | **Gap** | Ref carries `[Concurrent]` effect rows. Self-host has no effect annotation. If/when the type-checker honors concurrent-effect tracking, this is needed. |

The coherence set (E1–E5) is closed as of CL 125. Known remaining bug-parity: `open-file`, `read-all`, `close-file`, `get-state`, `set-state`, `now`, `random-integer` are in both NameResolver builtin-names lists but bound in neither TypeEnv — same `UnknownName`-after-NameResolver failure mode, symmetric on both sides. Fix ref + self-host in one CL when addressed.

---

## TypeChecker / TypeCheckerInference

`src/Codex.Types/TypeChecker.{cs,Inference.cs,Resolution.cs,Substitution.cs}` vs `Codex.Codex/Types/TypeChecker.codex` + `TypeCheckerInference.codex`

Already partially covered via Unifier pair. Additional findings:

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| T1 | Substitution.cs — full `SubstType` walker over all `CodexType` variants | **Gap (scoped)** | Ref has `TypeChecker.Substitution.cs` (~400 LOC) with substitution over LinearType, DependentFunctionType, TypeLevel*, ProofType, etc. Self-host's `subst-type-var` only walks the subset of CodexType it supports. In scope for the advanced-type deferred set. |
| T2 | Resolution.cs — generalisation / instantiation rules | **Gap (scoped)** | Ref's `TypeChecker.Resolution.cs` handles ForAll introduction (let-generalization) + systematic instantiation at use sites. Self-host has `instantiate-type` (fresh-var instantiation) but no let-generalization — ForAll only enters via explicit type annotation or builtin entry. Could lead to less polymorphic inferred types. |
| T3 | Effect inference on `act` / `handle` / `fork` | **Gap** | Ref propagates effect rows through act / handle / handlers and unifies row variables. Self-host infer-act (`infer-act-loop` in TypeCheckerInference.codex:354) doesn't track effects — treats last-stmt type as the act's type without marking it effectful. Ties to U4 and E6. |
| T4 | Record field-access type lookup | 🟡 Partial | Ref resolves to `RecordType` via substitution, looks up field, handles `ConstructedType` fallback. Self-host `AFieldAccess` arm does a similar thing but the `ConstructedTy → RecordTy` fallback path returns a fresh var when resolution fails, which can mask bugs. |
| T5 | Cascade suppression | ✅ Matched | Both treat unification involving `ErrorTy` as success to suppress cascading errors. |
| T6 | ForAll instantiation site | ✅ Matched | Both instantiate `ForAllTy` with fresh vars at use sites via `instantiate-type` / `Instantiate`. |

No bug-parity identified.

---

## Lowering

`src/Codex.IR/Lowering.cs` vs `Codex.Codex/IR/Lowering.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| L1 | LinearType / DependentFunctionType / TypeLevel* / Proof* lowering | **Gap (deferred)** | Ref handles all advanced types in IR lowering. Self-host doesn't (the types don't exist in its CodexType). Consistent with Desugarer D1–D4 and Unifier U6–U9. |
| L2 | Effect row preservation through lowering | **Gap** | Ref keeps effect information on IR nodes (IrAct, IrHandle carry effect metadata). Self-host IR drops row info; only the effect name list survives. Downstream emitters that need to know which effects a block requires can't query them. |
| L3 | `handle` / `with …` lowering with explicit handler binding | 🟡 Partial | Ref `LowerHandle` produces IR with handler-per-clause binding. Self-host `lower-handle` (IR/Lowering.codex equivalent) is similar but doesn't lower the resume continuation fully. Worth a detailed pass; not blocking self-host's own compile. |
| L4 | Interpolated string lowering | **Gap (deferred)** | Ties to D3, X2. |
| L5 | Match-exhaustiveness hint | 🟡 Partial | Ref records per-match hint of whether all ctors covered (for downstream codegen optimizations). Self-host does not. Minor. |

No bug-parity.

---

## Codex Emitter

`src/Codex.Emit.Codex/CodexEmitter.cs` vs `Codex.Codex/Emit/CodexEmitter.codex`

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| CX1 | `Maybe` emission drift (per Damian 2026-04-19) | **Bug-parity candidate** | Under active fix by Hex-Hex. Root cause traced through Unifier U1–U3 (type-arg unification on Sum/Record name match). Both emitters are downstream consumers; bug manifests when the IR they receive has unbound TypeVars that should have been bound. Audit this pair again after Hex-Hex lands the fix. |
| CX2 | Prose / section header emission | 🟡 Partial | Ref emitter reproduces chapter titles, section titles, prose blocks inline. Self-host emits chapter-title-only header, no sections, no prose (`emit-codex-text-chapter` in CodexEmitter.codex:600). Pingpong byte-identity depends on exact round-trip. When pingpong is green the divergence is benign; if prose-round-trip is added to the contract this is a gap. |
| CX3 | TypeVar normalization | ✅ Matched | Both renumber type vars 0..N at emit time via `collect-tvars` / `normalize-type` so emitted text is deterministic. |
| CX4 | Effect emission | **Gap** | Ref emits `[Effect1, Effect2]` from the full effect row including inferred effects. Self-host emits only the effect names declared on the source AST. If inference added an effect, it's lost at emit. Ties to T3, L2. |

---

## CSharp Emitter

`src/Codex.Emit.CSharp/CSharpEmitter.cs` vs `Codex.Codex/Emit/CSharpEmitter.codex` + `CSharpEmitterExpressions.codex`

Scope note: C# emitter is a retiring backend (MM4 goal is to drop the C# path). Parity here is low-priority; listing only significant divergences.

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| S1 | Interpolated string emission | **Gap (deferred)** | Ties to D3, X2, L4. |
| S2 | Linearity-aware emission | **Gap (deferred)** | Ref has `linear T` → `Span<T>` or similar; self-host doesn't. |
| S3 | Effect handler emission | 🟡 Partial | Ref emits effect handlers with stack-threaded `_eff` parameter. Self-host emits `Handle` as a plain expression using fork/await machinery. If handler semantics matter to the runtime, this is a gap. |
| S4 | Arity map construction | ✅ Matched | Both build a per-chapter arity map for partial application. |

No bug-parity.

---

## X86_64 Code Generator

`src/Codex.Emit.X86_64/X86_64CodeGen.cs` (+ siblings) vs `Codex.Codex/Emit/X86_64*.codex`

The x86-64 backend is ~8k LOC on the ref side and similarly large split across many files on the self-host side. Full side-by-side is an audit unto itself. High-level survey only here; deep audit deferred to a dedicated session.

| # | Topic | Status | Notes |
|---|-------|--------|-------|
| A1 | TCO heap-reset four-check gate | ✅ Matched | Both implement the selective-list-param check set (CL 90 pair). See `docs/Done/Compiler/TCO-HEAP-RESET-DIAGNOSTIC.md`. |
| A2 | Escape copy & regions | **Gap (deferred per MM4 plan)** | Ref has partial machinery; self-host doesn't. Phase 6 of SECOND-BOOTSTRAP deferred. |
| A3 | `__list_snoc` three-path append | ✅ Matched | Amortized O(1) path, grow-in-place path, copy-and-reallocate path all present on both sides. |
| A4 | Runtime helpers (`__itoa`, `__str_eq`, etc.) | ✅ Matched | 16/22 runtime helpers ported per SECOND-BOOTSTRAP.md Phase 4. Remaining six (`__read_file`, `__read_line`, `__bare_metal_read_serial`, `__cce_to_unicode`, `__unicode_to_cce`) deferred with rationale. |
| A5 | Codegen invariant verifier | ✅ Matched | Both have `X86_64CodegenInvariants.Verify` (ref) / `emit-finalize` post-checks (self-host) catching unresolved direct calls and `FuncAddrFixup`. |
| A6 | DWARF `.debug_line` | **Gap (both sides)** | Neither has it. Listed in `DIAGNOSTICS-AND-STAGING.md` as wishlist. Not a parity gap — both equally missing. |
| A7 | Instruction encoder variants | 🟡 Spot-check sample ok | Handful sampled (`mov-rr`, `call`, `add-ri`, `push-r`) match byte-for-byte. Comprehensive byte-compare needs a test harness; deferred. |

Recommendation: schedule a **follow-up audit CL** scoped solely to x86-64 emitter after the Maybe drift and type-env coherence fixes land.

---

## Summary of actionable gaps (2026-04-19)

Prioritised, excludes deferred-advanced-types set:

1. **U1–U3** — Unifier doesn't propagate type args on Sum/Record/Constructed name-match. Root suspect for Maybe drift. (Hex-Hex in flight.)
2. **T3, L2, CX4, E6, E8** — Effect-row handling: self-host drops effect information at type-check, lowering, emit, and has no effect-polymorphic builtin signatures. Feature-sized.

Deferred (full feature not in self-host):
- D1–D4, X2, P1, P2, P3, P5, P6, L1, L4, S1, S2, C5, T1, T2 — proof system, dependent types, linear types, type-level values, interpolated strings. Re-audit when any of these features enters the self-host.

No bug-parity on any audited pair. The self-host's gaps are self-host-only; the reference is consistent with itself throughout.

Next audits: the X86_64 emitter deep pass (A1–A7 superficial so far), and a re-audit of the Maybe-drift surface after Hex-Hex lands the type-arg-propagation fix.
