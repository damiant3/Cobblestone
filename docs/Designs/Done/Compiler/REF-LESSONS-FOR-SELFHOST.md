# REF lessons applied to the self-host (live audit log)

Running notes originally collected while chasing REF-side correctness bugs.
Lifting-scope items (C# target -- the selfhost-cs sweep surface) are now
CLEARED as of CL 209. What remains in this doc is **bare-metal-target work**
for the MM4 path (bootstrap 2 / bootstrap 3 -- pingpong and bare-metal emit).
Keep here while those targets are still red; cut to Done when MM4 closes.

## Baseline: self-host sweep status

### selfhost-cs (C# target -- lifting complete, CL 209)

`tools/ref-sweep.sh --compiler=selfhost-cs` runs each sample through
`Codex.Bootstrap --emit-sample`, builds the emitted C# via dotnet, and runs
it. Current: **75 pass / 54 verified / 20 expected-to-fail / 11 skip / 3 fail**
of 89. The 3 fails (`effectful-hello`, `shapes`, `w3`) are all **target-semantic
divergence** -- REF bare-metal vs selfhost-cs differ on stdin EOF handling,
Number-to-text formatting, and record ToString -- not compiler correctness
bugs. Out of lifting scope.

### selfhost (bare-metal target -- audit surface)

`tools/ref-sweep.sh --compiler=selfhost` boots `build-output/bare-metal/Codex.Codex.elf`
via QEMU. This is the MM4 critical path -- the bare-metal selfhost has its
own correctness gaps (most of the Lessons below). Re-audit when the bare-metal
selfhost can produce ELFs for the full battery.

### 14. Type-aware bare-metal REPL print

**REF site**: `X86_64CodeGen.EmitStart` / `EmitCallMainAndPrint`. REF picks a
formatter for `opening`'s result based on the declared return type -- Integer
via `__itoa`, Text via `EmitPrintText`, Boolean via `True`/`False` rodata
strings, etc.

**Self-host site**: `Codex.Codex/Emit/X86_64Chapter.codex` `emit-start`. The
existing code looks like it always passes the result through
`emit-inline-itoa-and-print`, which is Integer-only. Every Text-returning
`opening` then prints its pointer value as a number. This is the single
biggest source of self-host runtime-mismatch output in the sweep (~14 of 19
mismatches).

## Bug categories to sweep for in the self-host

### 1. In-place register mutation in builtin emit

**REF site**: `src/Codex.Emit.X86_64/X86_64CodeGen.cs`, `TryEmitBuiltin`. Builtins
`is-letter`, `is-digit`, `is-whitespace`, and `char-at` were doing destructive
arithmetic on the register returned by `EmitExpr(args[i])`. When that register
was the caller's parameter slot (e.g. inside a tail-recursive helper that passes
a param straight into the predicate), the parameter got clobbered and the next
TCO iteration read garbage. Fix: `AllocTemp()` + `MovRR(rd, src)` before
mutating.

**Symptom that surfaced it**: `samples/string-ops.codex` counted only 1 letter
out of 10 in `"hello world 123"`; `samples/tco-stress.codex` worked because it
used pure arithmetic without any predicate builtin. The bug hides behind any
hot loop that doesn't read a predicate result.

**Self-host candidates to audit**:
- `Codex.Codex/Emit/X86_64IO.codex` (EmitIntegerLit, string/char ops).
- `Codex.Codex/Emit/X86_64Helpers.codex` (likely has the predicate emit).
- Any `emit-is-letter`, `emit-is-digit`, `emit-is-whitespace`, `emit-char-at`
  equivalents -- look for the pattern "EmitExpr → op-on-returned-reg" without an
  intervening fresh-temp copy.

Audit procedure: grep for `emit-expr` followed within 3 lines by `sub-ri` /
`add-rr` / `cmp-ri` / `neg-r` targeting the same register. Rewrite to copy
first.

### 2. Arity-aware over-apply for chained `IRApply`

**REF site**: `X86_64CodeGen.EmitApply`. The emitter originally flattened
`IRApply(IRApply(f, a), b)` into a single call with two args, regardless of
`f`'s arity. Caught `(make-adder 10) 5` treating `make-adder` as arity-2.

**Self-host candidates**: the selfhost emitter's apply flattener -- find where
`IrApply` chains are unwrapped. Probably `Codex.Codex/Emit/X86_64Compound.codex`
around `flatten-apply`. Verify it stops at the function's arity and emits an
indirect call for the remainder.

### 3. Indirect call from non-`IRName` function expressions

**REF site**: `X86_64CodeGen.EmitApply`, same function. An `IRApply` whose
function is an `IRFieldAccess` or `IRApply` result (a closure value) emitted no
call at all -- `funcName == null` fell through with nothing. Fix: evaluate the
function expression, save as closure local, indirect-call.

**Self-host candidates**: same emit-apply site. Specifically the path that
handles `func` = anything-but-`IrName`.

### 4. Lambda lifting

**REF site**: `src/Codex.Emit.X86_64/X86_64CodeGen.cs` had no `IRLambda` case at
all -- lambda expressions silently returned 0 via `EmitUnhandled`. Fixed with a
new lambda-lifting pass in `src/Codex.IR/LambdaLifting.cs` that rewrites every
`IRLambda` into a synthesized top-level `IRDefinition` with free-variable
captures applied.

**Self-host candidates**: check `Codex.Codex/Emit/X86_64*.codex` for an
`IrLambda` dispatch. If absent, a lifting pass must be added to the selfhost's
IR pipeline too. `Codex.Codex/IR/Lowering.codex` is the place that would need a
post-pass equivalent to `LambdaLifting.Lift`.

### 5. Absorption of outer lambdas into def params (eta-like)

**REF site**: `LambdaLifting.AbsorbOuterLambdas`. Turns `f = \x -> body` into
`f (x) = body` so a zero-arg function doesn't need to return a closure at every
call site. The IRRegion-wrapping from lowering has to be peeled through
transparently.

**Self-host candidates**: same place -- if the selfhost emits `f 7` and `f` was
lowered as `f : FunctionType, Parameters=[], Body=IRLambda`, it'll have the
same "returns closure pointer, caller doesn't indirect-call" bug.

### 6. `SubstituteTypeVarsFromArg` missing type shapes -- DONE

**REF site**: `src/Codex.IR/Lowering.cs`. The helper only handled `TypeVariable`,
`ListType`, `FunctionType` paramTypes. Adding `LinkedListType`, `ConstructedType`,
`SumType`, `RecordType`, and mixed `SumType ↔ ConstructedType` shapes was
necessary to propagate type args through polymorphic calls like
`head : ConsList a -> a`.

**Status**: self-host `subst-type-vars-from-arg` now covers the shapes
(ConstructedTy / SumTy / RecordTy) via CL 189 (`poly-runtime` emit fix) +
CL 196 (AFieldAccess ConstructedTy rargs-vs-cargs instantiation). Verified
by `poly-runtime` sample passing and BS1 byte-identical.

### 7. `ExprTypes` deeply-resolved types threaded to lowering -- DONE (CL 209)

**REF site**: `src/Codex.IR/Lowering.cs` constructor gained an optional
`IReadOnlyDictionary<Ast.Expr, CodexType>? exprTypes` parameter. `LowerApply`
prefers this authoritative type over its own re-derivation when available.

**Self-host status**: `UnificationState.expr-types` (sorted span-keyed list,
O(log N) bsearch) populated by `record-expr-type` in narrow inference sites
only: `infer-name` and the `ARecordExpr` arm. Broader population wrongly
overrides match result-ty (prior attempt reverted). `lower-name` consults
first, deep-resolves the recorded type, and uses directly -- bypasses the
`prefer-applied-ty` heuristic for ExprTypes-hit cases. Fixed `list-test`
polymorphic-function-ref type-var escape (flip_cons T19 bleeding into
list_reverse's body as `new Nil<T19>()`). Also the CS0266 Nil→ConsList
up-cast (via `emit-ctor-upcast` -- three emit sites) and CS0411 on
zero-arg `nil<T>()` (via ArityEntry.generic-count + `extract-fn-type-args`).
All 15 list-test checks now PASS.

### 8. Emitter priority: user-defined vs builtin

**REF site**: `X86_64CodeGen.EmitApply`. A user-defined function name (e.g.
`foreword/List.codex`'s `list-length` on `ConsList`) was losing to the native
builtin `list-length` because the emit-site switch didn't check whether a
user-defined function of the same name existed. Fix: pre-pass populates
`m_userDefinedFunctions`, emitter checks before falling through to builtin
dispatch.

**Self-host candidate**: the selfhost emitter's `try-emit-builtin` should
similarly consult a user-defined-names set first. Found in
`Codex.Codex/Emit/X86_64Builtins.codex` presumably.

### 9. Cite-gated builtins

**REF site**: Step 4 of the builtins-quire refactor. Typed builtins live in
`src/Codex.Types/BuiltinChapters.cs`, keyed by chapter, and are only brought
into scope by an explicit `cites Codex chapter X`.

**Self-host state**: deliberately NOT migrated per user direction. The selfhost
still has hardcoded builtin lists in `Codex.Codex/Semantics/NameResolver.codex`
and `Codex.Codex/Types/TypeEnv.codex`. Pingpong/bootstrap 1/bootstrap 3 all
expected red until this catches up. When it does, the selfhost needs the
equivalent of `BuiltinChapters` + a cite-interception in its name-resolver.

### 10. State effect emit -- DONE for C# target (CL 208); bare-metal still open

**REF site**: `X86_64CodeGen.EmitExpr`. Three IR nodes (`IRRunState` /
`IRGetState` / `IRSetState`) existed and Lowering produced them, but the
emitter had **no case for any of them** -- all fell through to `EmitUnhandled`
(returns 0). REF fix: dedicated emit methods, state lives in a single stack
slot scoped dynamically around the run-state body. Integer-width only.

**Self-host status**:
- **C# target (DONE)**: CL 208 adds `_State` runtime class + builtin emitters.
  No new IR nodes -- `run-state`/`get-state`/`set-state` recognized as builtin
  names at emit, run-state wraps init/try/finally inline around the act body.
  TypeEnv collapses the state ops from polymorphic ForAll to concrete
  IntegerTy (matches REF's Integer-only stance). Verified via state-demo PASS.
- **Bare-metal target (TODO)**: no IrRunState/IrGetState/IrSetState cases in
  `Codex.Codex/Emit/X86_64*.codex`. Bare-metal state-demo still fails. Fix
  when the bare-metal selfhost target is re-audited against the sample
  battery.

### 11. SSE enable on bare-metal entry

**REF site**: `X86_64CodeGen.cs`, 32-bit bootstrap. Added CR4.OSFXSR +
CR4.OSXMMEXCPT, cleared CR0.EM, set CR0.MP before entering long mode. Without
this, any SSE instruction (`movq %r9,%xmm0`, `mulsd`) `#UD`s.

**Self-host candidate**: `Codex.Codex/Emit/X86_64Chapter.codex`'s `emit-start`
writes the same 32-bit bootstrap region. Currently writes CR4.PAE-only and
just OR-PG into CR0. Same fix needed in the selfhost emitter before the
selfhost compiler can produce ELFs that run float-using samples.

### 12. Runtime primitive name hygiene (`__` prefix)

**REF site**: Runtime-chapter primitives renamed (`record-set` →
`__record-set` etc.) so user code can't collide.

**Self-host state**: selfhost data lists haven't been renamed yet. When
migrating, rename across:
- `Codex.Codex/Semantics/NameResolver.codex` builtin-names list.
- `Codex.Codex/Types/TypeEnv.codex` builtin-type-env.
- Every call site in `Codex.Codex/Emit/*.codex` that emits `record-set` /
  `heap-save` / `buf-*` / `linked-list-*` calls -- the string has to match REF's
  new name.

### 13. `True`/`False`/`Nothing` as lexer reserved words

**REF site**: `Lexer.cs` + `TokenKind.cs` + `Parser.Types.cs`.
`NothingKeyword` joins `TrueKeyword`/`FalseKeyword`; the type parser accepts
all three where `TypeIdentifier` is accepted.

**Self-host candidate**: `Codex.Codex/Syntax/Lexer.codex` classify-word and
`Codex.Codex/Syntax/ParserCore.codex` type-expression parsing. If the selfhost
lexer treats `Nothing` as a plain identifier, user code can shadow it locally.

## Things still to check

- **Self-host emitter's `record-set` call shape** -- the selfhost source still
  writes plain `record-set`; REF would now reject this until the selfhost gets
  the `__` rename and the Codex chapter Runtime cite. (Lesson 12, open.)
- **Effect handlers** in the self-host -- `handle…is` is entirely absent from
  samples. handler-basic in the sweep is .skipped. Worth verifying the
  handler-emit path has test coverage somewhere once bare-metal state-demo
  is sorted.
- **Polymorphism coverage** -- selfhost-cs covers this via poly-runtime PASS +
  list-test PASS (CL 209). Bare-metal coverage still needs re-audit.

## Polymorphism-coverage false alarm

The original `samples/polymorphism-coverage.codex` #PF was misdiagnosed as a
polymorphism miscompile. Turned out the sample had no `opening` and the
bare-metal trampoline was jumping to a missing symbol. Lesson: distinguish
"sample has no runtime entry point" from "runtime ran and crashed" in the
battery -- an `.expected` file present should require an `opening`, and missing
one should be either a skip-with-reason or an explicit compile-only sidecar.

## Test-battery methodology (to mirror on the self-host)

The REF test battery (`tools/ref-sweep.sh`) with `.expected` / `.failing` /
`.skip` sidecars caught four real bugs on day one (two compiler, one
sample-author mistake, plus two still-open as of this CL). The same
methodology should be set up for the self-host once it can produce correct
output again -- run pingpong + bootstrap3 with a fixed set of known-good
outputs, and diff. "Byte-identical between stages" is a weaker check than
"output matches a hand-verified expected value."
