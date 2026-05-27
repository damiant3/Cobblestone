# IL Effect Handlers — Design Notes

**Date**: 2026-03-21
**Author**: Claude (Opus 4.6, Linux)
**Status**: Phase 1 complete, Phase 2 complete (Approach A — inline-only handlers)

---

## What's Done (Phase 1): Built-in `run-state` Handler

The IL emitter now handles `IRRunState`, `IRGetState`, and `IRSetState`:

- `EmitRunState` declares a `__state` local, stores the initial value, then
  inlines the do-block statements directly (no closure needed — IL methods
  already have local scope).
- `IRGetState` → `ldloc __state`
- `IRSetState` → emit new value, `stloc __state`
- Nesting works naturally: `LocalsBuilder.TryGetLocal` searches last-to-first,
  so a nested `run-state` shadows the outer `__state`.

**Lowering fix**: Added `m_currentStateType` field to the lowerer, set during
`run-state` computation lowering. This ensures `get-state` returns the correct
type (e.g. `TextType`) instead of `ErrorType`, which was causing `s ++ " world"`
to lower as `AppendList` instead of `AppendText` — producing invalid IL.

6 new runtime-verified tests: simple get, set-then-get, increment, arithmetic
chain, text concatenation, plus 1 emit validation test.

---

## Phase 2 Challenge: User-Defined `with` Handlers

### The IR Structure

```
IRHandle(
    Computation: IRExpr,           // the effectful code
    EffectName: string,            // e.g. "Logger"
    Clauses: IRHandleClause[],     // one per operation
    Type: CodexType                // result type
)

IRHandleClause(
    OperationName: string,         // e.g. "log"
    Parameters: string[],          // e.g. ["msg"]
    ParameterTypes: CodexType[],   // e.g. [TextType]
    ResumeName: string,            // e.g. "resume"
    ResumeParamType: CodexType,    // what resume accepts
    Body: IRExpr                   // handler body
)
```

### The Fundamental Problem

Consider:

```codex
effect Logger where
  log : Text -> Integer

program : [Logger] Integer
program = log "hello"

main : Integer
main = with Logger program
  log (msg) (resume) = resume 0
```

After lowering, the `IRHandle` node's computation is `IRName("program")` — a
reference to a top-level static method. Inside `program`, the call to `log` is
an `IRApply(IRName("log"), IRTextLit("hello"))`. But `log` is not a real
method — it's an effect operation that only has meaning within a handler scope.

The C# emitter generates:

```csharp
((Func<int>)(() => {
    Func<string, Func<int, int>, int> _handle_log_ = (msg, resume) => {
        return resume(0);
    };
    return program;  // ← just returns the reference, doesn't intercept
}))()
```

**This doesn't actually work at runtime** — the existing C# tests only verify
type-checking and string presence (`Assert.Contains("_handle_log_", cs)`), not
execution. The handler closure is defined but never called because `program` is
a pre-compiled static method that has no way to reach the local `_handle_log_`.

### Three Possible Approaches

#### A. Inline-Only Handlers (Simplest)

Only support handlers where the computation is a literal expression (lambda,
do-block, or direct operation call), not a named function reference. In this
model, the lowerer inlines the computation body into the handler scope, and
effect operation names are rewritten to handler closure invocations.

**Pros**: Simple, no closures needed in many cases.
**Cons**: Doesn't work when the computation is a function reference.

#### B. CPS Transform (Algebraic Effects)

Transform effect operations into continuation-passing style. Each operation
call captures the current continuation and passes it to the handler as the
`resume` parameter. This is how Koka, Eff, and Multicore OCaml implement
algebraic effects.

For IL, this would mean:
- Effect operations become delegate invocations
- The handler installs a "prompt" (like a try/catch frame)
- Operations unwind to the prompt and invoke the handler closure
- `resume` reinstates the continuation from the operation site

**Pros**: Fully general, composable, handles multi-shot continuations.
**Cons**: Requires continuation capture (expensive on CLR without Fibers),
significant IL complexity.

#### C. Interface/Virtual Dispatch (Practical)

Model each effect as an interface with one method per operation. The handler
creates a concrete implementation. The computation receives the handler as an
implicit parameter (or via a thread-local / ambient context).

```csharp
interface ILogger { int log(string msg); }
class LoggerHandler : ILogger {
    public int log(string msg) => 0;  // from the handler clause
}
// program receives ILogger as a parameter
static int program(ILogger __effect_Logger) => __effect_Logger.log("hello");
```

**Pros**: Natural on CLR, efficient dispatch, no continuation overhead.
**Cons**: Doesn't support `resume` (one-shot continuations) — the handler
body can't "resume" back into the computation. Only works for simple
handlers that immediately return a value.

### Recommended Path

**Start with Approach A** (inline-only) for the IL emitter. This handles
the test cases that exist today and is sufficient for Camp II-A. The
computation in practice is usually a do-block or lambda, not a named function.

For the inline model:
1. When `IRHandle.Computation` is `IRDo` or `IRLambda`, inline it directly.
2. For each effect operation call within the computation, substitute the
   handler clause body with arguments bound.
3. The `resume` parameter becomes a local that holds the operation's return
   value (for simple one-shot cases, `resume x` just means "the operation
   returns `x`").

**Later**: Move to Approach C for named function references, which requires
changing the lowering to pass effect handlers as implicit parameters.

**Eventually**: Approach B for full algebraic effects with multi-shot
continuations, likely coinciding with the native codegen backend (Camp II-B)
where we control the calling convention.

---

## What's Done (Phase 2): User-Defined `with` Handlers (Approach A)

The IL emitter now handles `IRHandle` via inline-only handler emission.

### Implementation

Three new fields on `ILAssemblyBuilder`:

- `m_definitions` — stores all module `IRDefinition` entries so named
  computations can be resolved to their bodies for inlining.
- `m_activeHandlerClauses` — operation name → `IRHandleClause` map, active
  during emission of a handled computation. Checked in `EmitExpr` (`IRName`
  for zero-arg ops) and `EmitApply` (ops with arguments).
- `m_activeResumeName` — the resume parameter name, active during clause body
  emission. When `EmitApply` sees a call to this name, it emits just the
  argument (one-shot resume: `resume x` = `x`).

**`EmitHandle` method** — the core orchestrator:

1. Builds a clause map from the `IRHandle.Clauses`.
2. Saves the current handler context (supports nesting).
3. Resolves the computation: if `IRName("comp")` references a zero-param
   definition, retrieves that definition's body for inlining.
4. Emits the resolved computation with interception active.
5. Restores the previous handler context.

**`EmitHandlerClauseInline` method** — emits a single clause body:

1. Evaluates each operation argument and stores it as a local bound to the
   clause parameter name.
2. Installs the resume name so `resume x` calls are intercepted.
3. Emits the clause body expression.

**Interception points** in `EmitExpr` and `EmitApply`:

- `IRName` case: if the name matches an active zero-arg handler clause,
  calls `EmitHandlerClauseInline` instead of the normal name resolution.
- `EmitApply`: checks for resume calls first (emit argument only), then
  checks for operation calls with arguments (bind params, inline clause).

### What Works

- Zero-arg operations: `ask` → inline clause body
- Operations with parameters: `log "hello"` → bind `msg`, inline clause body
- One-shot resume: `resume 42` → pushes `42`
- Named computations: `with Ask comp` where `comp` is a zero-param definition
- Direct computations: `with Ask ask` (operation is the computation itself)
- Handler results in expressions: `ask + 8` with `resume 34` → `42`
- Nested handlers (save/restore context)

### Limitations (Approach A)

- **Named computations with parameters** are not inlined — the handler
  context won't intercept operations inside a called method.
- **Multi-shot resume** is not supported — `resume` can only be called once.
- **Higher-order computations** (passing effectful functions around) won't
  have their operations intercepted.

These limitations are acceptable for Camp II-A. Approach C (interface
dispatch) or Approach B (CPS) would be needed for full generality.

### Tests Added (Phase 2)

5 new tests in `ILEmitterIntegrationTests`:

| Test | What It Verifies |
|------|-----------------|
| `Handle_ask_returns_42` | Named def inlining, zero-arg op, resume |
| `Handle_ask_direct_computation` | Direct operation as computation |
| `Handle_log_with_parameter` | Operation with Text parameter |
| `Handle_ask_resume_with_arithmetic` | Handler result used in `ask + 8` expression |
| `Handle_emits_valid_il` | Assembly bytes are non-empty |

---

## Files Changed (Phase 1)

| File | Change |
|------|--------|
| `src/Codex.IR/Lowering.cs` | Added `m_currentStateType` field; set during `run-state` lowering; used by `get-state` to resolve correct type |
| `src/Codex.Emit.IL/ILAssemblyBuilder.cs` | Added `IRGetState`, `IRSetState`, `IRRunState` cases in `EmitExpr`; added `EmitRunState` method |
| `tests/Codex.Types.Tests/ILEmitterIntegrationTests.cs` | 7 new tests (6 runtime-verified + 1 emit validation) |

## Files Changed (Phase 2)

| File | Change |
|------|--------|
| `src/Codex.Emit.IL/ILAssemblyBuilder.cs` | Added `m_definitions`, `m_activeHandlerClauses`, `m_activeResumeName` fields; `IRHandle` case in `EmitExpr`; handler/resume interception in `IRName` and `EmitApply`; `EmitHandle` and `EmitHandlerClauseInline` methods; `m_definitions` populated in `EmitModule` |
| `tests/Codex.Types.Tests/ILEmitterIntegrationTests.cs` | 5 new runtime-verified tests for user-defined handlers |
