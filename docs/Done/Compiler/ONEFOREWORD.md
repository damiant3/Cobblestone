# ONEFOREWORD — The Builtin Wall and Cross-Project Function Compilation

## TL;DR

The design intent was: **two sources of truth for every table and library function — one in `src/` (ref compiler, C#), one in `foreword/` + `Codex.Codex/` (self-host, Codex).** Everything else is a consumer.

The code is not that. The 128-entry Tier 0 CCE table is hand-transcribed four times in source. ~2,000 lines across `X86_64Builtins.codex`, `X86_64ListHelpers.codex`, `X86_64TextHelpers.codex`, `CSharpEmitterExpressions.codex` are per-backend reimplementations of functions that `foreword/` already defines. The wall exists because **cross-project function compilation in codex-cli does not exist** — the `cites Foreword chapter X` mechanism brings TYPES into the emitted output but not FUNCTION BODIES. Every library function the self-host wants to use from foreword gets promoted to a "builtin" — declared in `TypeEnv.codex`, hand-coded in each backend. That promotion is the wall.

This is the same anti-pattern collapsed for the binary/text pipeline split in `62f0bd7` / `ae34f15` (see `docs/Done/OnePipeline.md`). The precedent: a wedge, taken for short-term convenience, crystallizes into a parallel architecture that blocks the milestone. The fix is structural: build the mechanism, then delete the wall one wedge at a time with pingpong green after each step.

This is **MM4-unblocking work**, not post-MM4 work. The pivot from "chase MM4" to "fix the foundations blocking MM4" is explicit.

## The forensic inventory — CCE Tier 0, four transcriptions

The `src/Codex.Core/CceTable.cs:4-8` comment is load-bearing rhetoric:

> *Every CCE lookup table in the project (compile-time encoding in the emitter, runtime preamble in generated code, CLI encode command, tests) derives from these arrays. **Do not duplicate them — reference them.***

Status today:

| # | Location | Form | Authoritative? | Consumers |
|---|----------|------|----------------|-----------|
| 1 | `src/Codex.Core/CceTable.cs:43-61` | `int[] s_toUnicode` | **YES — ref compiler canonical C#.** | `X86_64CodeGen.cs:84`, `RiscVCodeGen.cs:79` (both read it correctly at emit time). `s_fromUnicode` built at runtime from `s_toUnicode` (`CceTable.cs:64-75`). Runtime preamble in generated output regenerated via `GenerateRuntimeSource` reading `s_toUnicode` (`CceTable.cs:565-581`). Ref side does the right thing. |
| 2 | `Codex.Codex/Emit/X86_64State.codex:140-142` | `cce-to-unicode-table : List Integer` — 128-entry Codex list literal | Duplicate. Hand-transcribed. | Bare-metal rodata layout (`init-rodata` at line 156-159). |
| 3 | `Codex.Codex/Emit/X86_64State.codex:144-146` | `unicode-to-cce-table : List Integer` — 256-entry reverse-lookup literal | Duplicate. Hand-transcribed reverse. (The C# side builds this at runtime from #1; the self-host pre-computes and transcribes.) | Bare-metal rodata layout. |
| 4 | `foreword/CCE.codex:59-180` | `to-unicode` — ~122-line `else if b == N then X` cascade | Duplicate. Same data, function form instead of list. | Currently no compiler consumer (no one calls `to-unicode` from the self-host). Lives here as proof-of-language + CCE demo. |

Plus the range-class bounds (`0..2`, `3..12`, `13..64` etc.) are inlined in ≥ 3 additional places:

- `foreword/CCE.codex:15-40` — `is-whitespace`/`is-digit`/`is-letter`/etc. (authoritative Codex forms).
- `Codex.Codex/Emit/X86_64Builtins.codex:211-244` — `emit-is-letter-builtin` emits `sub-ri 13; cmp-ri 51; setcc cc-be` literal x86. `emit-is-digit-builtin` emits `sub-ri 3; cmp-ri 9`. `emit-is-whitespace-builtin` emits `cmp-ri 2`. Bounds hard-coded in assembly.
- `Codex.Codex/Emit/CSharpEmitterExpressions.codex` — paired `emit-builtin-is-letter`/`emit-builtin-is-digit` returning inline C# string templates. Bounds hard-coded again.

Five separately-maintained copies of the `[13, 64]` letter-range constants. Same for the digit range. Same for whitespace.

## The forensic inventory — the builtin wall

### File sizes

| File | Lines | What it is |
|------|-------|------------|
| `Codex.Codex/Emit/X86_64Builtins.codex` | 672 | x86 emit handlers for 53 named builtins |
| `Codex.Codex/Emit/X86_64ListHelpers.codex` | 564 | x86 emit for list ops (length/at/snoc/cons/append/set/insert) |
| `Codex.Codex/Emit/X86_64TextHelpers.codex` | 953 | x86 emit for text ops (concat/compare/contains/split/replace/etc.) |
| `Codex.Codex/Emit/CSharpEmitterExpressions.codex` | 769 | C# template emitters for the same 53+ builtins |
| **Total** | **2,958** | |

A large fraction of these lines are Category-2 library functions (see below) that `foreword/` already defines. The exact fraction is an output of Wedge 5+.

### The two categories hidden inside the word "builtin"

"Builtin" today collapses two distinct things:

**Category 1 — primitive.** Genuinely must be hand-coded per backend. No Codex body could ever exist.

Examples: `bit-and`, `bit-or`, `bit-shl`, `bit-not`, `heap-save`, `heap-restore`, `heap-advance`, `buf-write-byte`, `buf-write-bytes`, `buf-read-bytes`, `char-code`, `code-to-char`, `print-line`, `read-line`, `read-file`, `write-file`, `write-binary`, `file-exists`, `list-files`, `get-args`, `get-env`, `current-dir`, `fork`, `await`, `par`, `race`, `record-set`, `list-with-capacity`, `show` (polymorphic over all types), `linked-list-empty`/`push`/`to-list`, `integer-to-text`, `text-to-integer`, `text-to-double-bits`.

These stay builtins. Wall-invariant.

**Category 2 — library function that was promoted to primitive for convenience.** Has a Codex body already (in `foreword/` or `Codex.Codex/Core/Collections.codex`). Got declared as builtin solely because the emitter cannot pull in foreword function bodies.

Examples verified in source:
- `is-letter`, `is-digit`, `is-whitespace` — bodies in `foreword/CCE.codex:15-40`
- `list-map` (aka `map`), `list-filter` (aka `filter`) — bodies in `foreword/List.codex:37-44`
- `sorted-insert`, `sort-text-list` — bodies in `Codex.Codex/Core/Collections.codex:48-55`
- `text-concat-list`, `substring`, `char-at`, `char-code-at`, `char-to-text` — mostly expressible from `char-code` + text primitives
- `list-at`, `list-length`, `list-snoc`, `list-cons`, `list-append`, `list-insert-at`, `list-set-at`, `list-contains` — all expressible from list pattern matching over the ConsList type, modulo the self-host's array-backed List representation (open question flagged below)
- `text-compare`, `text-contains`, `text-starts-with`, `text-split`, `text-replace` — expressible from `char-code-at` + `text-length` + `substring`

This second category is the wall. Collapsing it is the work.

## Target state

For every piece of logic or data, exactly **two** sources of truth:

1. One in the ref compiler, `src/Codex.Core/` or `src/Codex.Emit.*` (C#).
2. One in the self-host, either `foreword/` (library, consumed by all Codex programs) or `Codex.Codex/` (compiler-internal logic not useful outside a compiler).

Both self-host and ref compiler read their own copy at runtime. **Neither copy is hand-transcribed from the other.** CI asserts equivalence via roundtrip tests, not via source-level diff.

### CCE, collapsed

- **Ref side, unchanged:** `src/Codex.Core/CceTable.cs:43-61` keeps `s_toUnicode`. `src/Codex.Emit.X86_64/X86_64CodeGen.cs:84` and `src/Codex.Emit.RiscV/RiscVCodeGen.cs:79` keep reading it. Do not touch `src/`.
- **Self-host side, one authoritative table:** `foreword/CCE.codex` replaces the 122-line `to-unicode` if-cascade with:
  ```
  cce-to-unicode-table : List Integer
  cce-to-unicode-table = [0, 10, 32, 48, 49, 50, ... 1091]  -- 128 entries

  to-unicode : Integer -> Integer
  to-unicode (b) = if b < 0 then 65533 else if b >= 128 then 65533 else list-at cce-to-unicode-table b
  ```
- **Reverse table computed at startup** from `cce-to-unicode-table`, mirroring how `src/Codex.Core/CceTable.cs:64-75` builds `s_fromUnicode`. No second hand-transcribed reverse. (Open question: the self-host bare-metal program has no "startup" in the C# sense; may need a pre-computed reverse emitted at compile time. That's fine — the compile-time step reads the authoritative `cce-to-unicode-table` constant from foreword, not a second transcription.)
- **`Codex.Codex/Emit/X86_64State.codex:140-146` deleted.** `init-rodata` references the foreword constant via the cross-project mechanism.

### Range checks, collapsed

- `foreword/CCE.codex:15-40` is the authoritative definition of `is-letter`, `is-digit`, `is-whitespace`.
- No separate `emit-is-letter-builtin` / `emit-builtin-is-letter`. When the self-host compiles an expression calling `is-letter`, it compiles the foreword body (two comparisons, `setcc`) — the same x86 sequence the hand-coded emitter was producing, but derived from the Codex body rather than hand-written.
- `sorted-builtin-names` shrinks. `TypeEnv.codex` shrinks. `X86_64Builtins.codex` shrinks. `CSharpEmitterExpressions.codex` shrinks.

## The missing mechanism — cross-project function compilation

### What the codex-cli does today

`tools/Codex.Cli/ForewordChapterLoader.cs` + `FileChapterLoader.cs` load foreword chapters for the *type-checker* and the *constructor resolver*. When `Codex.Codex/Syntax/Parser.codex:5` says `cites Foreword chapter Maybe`, the loader finds `foreword/Maybe.codex`, parses it, and makes the `Maybe` type constructor available for pattern matching (`Just`, `None`). This is how Maybe works in the parser today.

The Codex→C# emitter brings the TYPE DEFINITION into the output — `build-output/bootstrap/Codex.Codex.cs` contains `public abstract record Maybe<T>`, `Just<T>`, `None<T>` (observed lines ~336-339 in a prior build). **It does not bring function definitions from cited chapters.**

Proof point from this session: moving `Codex.Codex/Core/OffsetTable.codex` to `foreword/OffsetTable.codex` — `dotnet build Codex.sln` succeeded (codex-cli emitted without diagnostics), but `dotnet test tests/Codex.Bootstrap.Tests/` failed with 8× `CS0103 'offset_table_lookup' does not exist`. The type / constructor pipeline worked; the function-body pipeline did not exist to fail. Reverted.

That failure is the wall. Building past it is the feature.

### What the mechanism needs to do

When `Codex.Codex/Foo.codex` says `cites Foreword chapter CCE` and calls `cce-to-unicode-table` or `to-unicode` or `is-letter`:

1. The codex-cli locates `foreword/CCE.codex` (already does).
2. The parser / type-checker accepts references to names defined there (already does for types; **needs to do for functions**).
3. **New:** The emitter includes the compiled form of those function and constant definitions in its output unit. The references in `Foo.codex`'s emitted output resolve to the same names.

### Decisions the branch author must make (do not resolve in this doc)

- **Emission layout.** Three shapes, pick one:
  1. Concatenate cited foreword defs into the same `.cs` emitted for the citing chapter. Simple. Risks duplicate emission if two chapters cite the same foreword chapter — need a "mark emitted" set in the build.
  2. Emit one `.cs` per cited foreword chapter, wire into `csproj`. More files, cleaner ownership, but now codex-cli writes csproj-adjacent files too.
  3. Compile foreword to a `.dll`, linked at csproj level. Biggest architectural step. Gives a clean dependency story but requires the codex-cli to drive a second compile target. Probably overkill for now.

  Recommendation: start with option 1 (concatenation with a "already emitted" set). It is the smallest diff from today's emitter.

- **Bare-metal path.** The x86-64 backend does not output C# — it outputs ELF. The equivalent question is: how do foreword-defined functions get their IR emitted into the chapter's `.text` section? Probably the answer is the same `emit-full-chapter` loop, extended to include foreword defs referenced by the citing chapter. The per-def loop already exists (`Codex.Codex/main.codex` post-collapse `compile`).

- **Name resolution for "builtin" vs "foreword-defined".** Two subtly different models:
  1. `cites` is authoritative. If a name resolves via `cites Foreword chapter X`, it's a library call; the compiler emits the body. If a name does not resolve via any citation, it falls back to the builtin table. Clean.
  2. Foreword is implicit. Any name found in `foreword/` is library; anything in `sorted-builtin-names` is primitive. Shorter source (no `cites` lines) but ambiguous when both paths would resolve.

  Recommendation: model 1. Explicit is better than implicit. This also gives chapters the option of NOT importing a foreword chapter and using a builtin peephole instead — useful for the bare-metal runtime helpers that are size-critical.

- **Peephole optimizations — keep or delete.** When `is-letter` becomes a real function call, the x86 backend will emit `call is-letter` rather than an inline `sub/cmp/setcc`. For hot paths (tokenizer loops) this adds overhead. Options:
  1. Delete all peephole emitters, accept the call overhead. Simplest. Pingpong tells us if it's acceptable.
  2. Keep peephole emitters for a named allowlist (the tight inner-loop builtins) with a test that asserts the peephole output matches the general-path output byte-for-byte when both are exercised. Foreword is correctness ground truth; peephole is an optimization.

  Recommendation: 1 first, and if pingpong regresses, escalate to 2. Inline-at-callsite is probably cheap if the Codex body is small, which it is for Category 2.

- **Arity / polymorphism.** Foreword has polymorphic functions like `list-map : (a -> b) -> List a -> List b`. The self-host currently monomorphizes via the same path as any other polymorphic def. Confirm no special casing needed.

## Execution plan — ordered wedges

Rules:
- Each wedge is one commit or one small PR.
- Each wedge leaves pingpong green: `wsl bash tools/pingpong.sh` — bootstrap 2, bare-metal ELF under QEMU, stage1 ≡ stage2 byte-identical.
- Each wedge deletes more than it adds, in the long run.
- Compiler-touching work goes to a feature branch (CLAUDE.md rule): probably `hex-hex/oneforeword`.

### Wedge 0 — probe test

Write `samples/cite-fn-call.codex`:
```
Chapter: CiteFnCall
  cites Foreword chapter CCE

Section: Definitions
  main : [Console] Nothing = act
    print-line ("is-letter 13 = " ++ show (is-letter 13))
  end
```
Compile with self-host. Observe failure. Pin the exact failure mode (is it emit-time linker error? parse-time unresolved-name? type-check-time missing-binding?). This sample becomes the regression test — when it passes and prints `True`, cross-project emission works. Commit the sample under `samples/cite-fn-call.codex` with a comment describing its purpose. The sample is not added to `pingpong.sh` yet — it would break it.

### Wedge 1 — canonicalize foreword's CCE table

Replace `foreword/CCE.codex:59-180` (the `to-unicode` if-cascade) with a `cce-to-unicode-table : List Integer` constant plus a short `to-unicode b = if b < 0 then 65533 else if b >= 128 then 65533 else list-at cce-to-unicode-table b`. Keep the `from-unicode` cascade for now (its reverse pre-computation is Wedge 3 scope). Update `foreword/CCE.codex:332-357` main to still pass its roundtrip test (the test stays — it's the invariant). Pingpong unaffected (nothing cites CCE yet except the probe test). Commit.

### Wedge 2 — cross-project function emission **[LANDED on hex-hex/oneforeword-emit]**

Implementation summary (for reviewer + future-Hex):

- **Ref side (done).** `tools/Codex.Cli/Program.Compile.cs` has a new `LowerCitedDefs` helper. It runs a fresh `TypeChecker` per cited chapter (with other cited chapters in scope), calls `CheckChapter` to produce types, runs `Lowering.Lower`, and concatenates the resulting `IRDefinition`s into the main `IRChapter`. Name-collision dedup via a `HashSet<string>` (main chapter wins). Called from all four compile paths: `CompileToIR`, `CompileMultipleToIR`, `CompileViewToIR`, and the incremental path in `Program.Incremental.cs`. Layout decision: option 1 (concatenation into the same emit unit).
- **Latent bug fixed.** `src/Codex.Emit.CSharp/CSharpEmitter.Utilities.cs:CollectTypeVarIds` didn't descend into `SumType.TypeArguments` or `RecordType.TypeArguments`. Invisible before because main-chapter polymorphic functions tend to reach type variables through return types; foreword functions like `is-just : Maybe a -> Boolean` have `a` only under the SumType. Fix: added `case SumType st` and `case RecordType rt` to the walker.
- **Self side: not needed.** The self-host consumes a concatenated source (`tools/Codex.Bootstrap/Program.cs:LoadCodexSourceConcatenated` prepends every cited `foreword/*.codex` to the main source at the I/O boundary). So from the self-host's perspective, foreword chapters are already *in* the compilation unit as plain defs — there is no cross-project gap to bridge on that side. The self-host's `collect-type-var-ids` equivalent in `Codex.Codex/Emit/CSharpEmitter.codex:97` has the same latent SumTy/RecordTy miss, but `SumTy`/`RecordTy` in the self-host's `CodexType` don't carry a type-arguments field — parameterization flows through `ConstructedTy`, which the walker already handles. So no parallel fix lands on the self-host side in this step.
- **Name-collision with builtins.** Resolved implicitly by ordering: `Lowering.LookupName` in `src/Codex.IR/Lowering.cs:606` consults `m_localEnv`, then `m_typeMap`, then `m_ctorMap`, then `s_builtinTypes`, then the fallback. Foreword-defined `is-letter` would appear in `m_typeMap` (main's checker has it from `CiteChapter`) — but for the emission side, the builtin-emit handler in `CSharpEmitter.Expressions.cs` still wins at the call-site level. That's actually what we want until Step 4 demotes `is-letter` et al. explicitly.
- **Acceptance.** `samples/cite-fn-call.codex` (Step 0 probe) compiles *and runs* — prints `to-unicode 13 = 101`. Full `dotnet test Codex.sln` green (1170/1170). `codex bootstrap Codex.Codex/` green: stage1 === stage3 at 911,198 chars (Bootstrap 1, C# output) and stage1 === stage2 at 555,024 chars (Bootstrap 1.1, Codex text output). `codex build Codex.Codex/ --target x86-64-bare` emits a 1.06 MB ELF without error. **Bare-metal pingpong (bootstrap 2, `wsl bash tools/pingpong.sh`) NOT verified on this branch** — needs reviewer with WSL/QEMU before merge.
- **Observed side effect.** With cited foreword functions now emitted, `build-output/bootstrap/Codex.Codex.cs` gained `from_maybe`, `is_just`, `is_none`, `maybe_map`, `maybe_bind` (the Parser's cited Maybe chapter). Dead code at runtime today, but the *presence* of these defs is the proof that the wall is broken for one concrete case.

Open items deferred to later steps, not blocking merge of this step:

- The `s_builtinTypes` map in `src/Codex.IR/Lowering.cs:824-980` is a third copy of the builtin wall (ref-side, alongside `Codex.Codex/Types/TypeEnv.codex:46-110` and `Codex.Codex/Emit/X86_64Builtins.codex`). Adding that to the forensic inventory under "the builtin wall" — the wall is wider than the original doc said.

### Wedge 3 — collapse the CCE rodata tables

Delete `cce-to-unicode-table` and `unicode-to-cce-table` from `Codex.Codex/Emit/X86_64State.codex:140-146`. Modify `init-rodata` (line 156-159) to reference `cce-to-unicode-table` from foreword. If the compile-time reverse-table precompute is needed for bare metal, do it in the emitter using the foreword-sourced table as input — **no hand-transcribed second list**. Pingpong green.

### Wedge 4 — demote `is-letter`, `is-digit`, `is-whitespace`

Delete:
- `emit-is-letter-builtin`, `emit-is-digit-builtin`, `emit-is-whitespace-builtin` from `Codex.Codex/Emit/X86_64Builtins.codex:211-244`.
- Their entries in `sorted-builtin-names` (`X86_64Builtins.codex:33-35`).
- `emit-builtin-is-letter`, `emit-builtin-is-digit`, and the inline `<= 2L` case for `is-whitespace` from `Codex.Codex/Emit/CSharpEmitterExpressions.codex`.
- Lines 56-58 of `Codex.Codex/Types/TypeEnv.codex` (`env-bind` for the three).

Add `cites Foreword chapter CCE` to `Codex.Codex/Syntax/Lexer.codex` (and any other chapter that uses the range checks — audit via `Grep`). Pingpong green. Line count delta: net negative by ~40 lines; will go much more negative over subsequent wedges.

### Wedge 5+ — demote remaining Category-2 builtins, one family per wedge

Suggested order (simplest to hardest):
- List ops with pure-Codex bodies: `list-length`, `list-at`, `list-snoc`, `list-cons`, `list-append`, `list-insert-at`, `list-set-at`, `list-contains`, `map` → `list-map`, `filter` → `list-filter`, `fold`.
- Text scanning: `text-starts-with`, `text-contains`, `text-split`, `text-compare`.
- Text building: `text-concat-list`, `text-replace`.
- Utility: `sorted-insert`, `sort-text-list`.

Each wedge deletes one family from four files (`X86_64ListHelpers.codex`, `X86_64TextHelpers.codex`, `X86_64Builtins.codex`, `CSharpEmitterExpressions.codex`) and from `TypeEnv.codex` + `sorted-builtin-names`.

**Do not batch.** Each family gets its own commit with pingpong-green proof. If a family regresses pingpong, revert just that wedge, don't hold up the rest.

### Final state

- `X86_64Builtins.codex`, `X86_64ListHelpers.codex`, `X86_64TextHelpers.codex`, `CSharpEmitterExpressions.codex` shrink by whatever fraction of builtins turn out to be Category 2. Target: the aggregate 2,958 lines drops below 1,500.
- `sorted-builtin-names` contains only Category-1 primitives.
- `TypeEnv.codex` `Section: Builtins` contains only Category-1 signatures.
- Any remaining `emit-*-builtin` handler is documented as a peephole optimization with a link to the foreword source as correctness ground truth.

## Open questions for the branch author

Beyond the Wedge-2 decisions above:

- **Which foreword chapter holds what.** `list-contains` is currently in `Codex.Codex/Core/Collections.codex`. Should it move to `foreword/List.codex` (available to user programs) or stay compiler-internal? Same question for `sorted-insert`, `sort-text-list`. Rule of thumb: if a user program could plausibly want it, it's foreword; if it's only meaningful to a compiler writer, it stays `Codex.Codex/Core/`.
- **How do `cites` declarations propagate through the chapter build graph?** If chapter A cites CCE and chapter B cites A, does B implicitly see CCE? Probably not — explicit `cites` only. Confirm in the mechanism.
- **Test harness.** `samples/cite-fn-call.codex` from Wedge 0 becomes `tests/Codex.Bootstrap.Tests/CrossProjectFunctions_Tests.cs` (or equivalent). Add to pingpong or to the bootstrap test suite.
- **CCE roundtrip test.** `foreword/CCE.codex:316-357` has `test-roundtrip` — 128/128 passes. That needs to still pass after Wedge 1 (list-based lookup). Adds to the wedge's acceptance criteria.

## Risks

- **OnePipeline-shape drift.** Under pressure, a contributor adds a "just this once" hand-transcribed copy to unblock a demo. Mitigation: the `src/Codex.Core/CceTable.cs:4-8` rhetoric applies — every new copy is a `grep`-visible offense. Consider adding a CI check that fails if the Tier 0 128-entry sequence (or a recognizable prefix like `0, 10, 32, 48, 49, 50`) appears in more than two files under the repo root.
- **Silent correctness regressions.** A wedge looks green locally but pingpong only catches a narrow fault. Mitigation: after each wedge, also run the explicit CCE roundtrip (`codex run foreword/CCE.codex`) and the probe test from Wedge 0.
- **Wedge-2 scope creep.** The cross-project emission mechanism is tempting to over-design. Keep option 1 (concatenation) unless a specific test forces option 2 or 3. YAGNI.
- **"While I'm here" refactors.** Every wedge does exactly one demotion. Do not rename anything. Do not retile anything. Do not tighten anything unrelated. One thing per wedge.
- **Peephole deletion hidden perf regression.** If deleting `emit-is-letter-builtin` causes pingpong to get meaningfully slower (the tokenizer uses `is-letter` in its hot loop), the peephole comes back as a named, tested optimization. Not as a silent, correctness-drifting copy.

## Non-goals

- **Refactoring `src/`.** The ref compiler already does the right thing — `X86_64CodeGen.cs:84` and `RiscVCodeGen.cs:79` read `CceTable.s_toUnicode` directly, and the CCE table C# is canonical. Leave it alone.
- **Renaming foreword chapters.** Tempting. Not this branch.
- **Tier 1 CCE support in the self-host.** `src/Codex.Core/CceTable.cs:105-362` implements Tier 1 on the ref side. Adding Tier 1 to the self-host bare-metal path is a separate scope. Keep Tier 0-only for now.
- **Unifying the x86-64 and C# emitters into one emitter.** The ordering concerns are different (register allocation vs. C# expression nesting). They stay separate. What they share — the input IR — already unifies them at the boundary that matters.
- **Removing the concept of "builtin" entirely.** Category 1 primitives stay builtins forever. They have no Codex body. That is correct.

## Rules going forward

- No new hand-transcribed CCE tables anywhere under the repo root.
- No new `emit-*-builtin` handler for anything that has a foreword body.
- Any new foreword library function is callable from the self-host via the Wedge-2 mechanism, not by adding a builtin entry.
- Category-1 vs. Category-2 classification is explicit — `sorted-builtin-names` is only Category 1. A Category-2 entry there is a bug.
- The doc-rule from `docs/Done/OnePipeline.md` applies: "anything that affects the compilation output must mirror precisely" — but the target is NOT mirror-via-duplication; it's mirror-via-shared-source. Two sources of truth, not five.
