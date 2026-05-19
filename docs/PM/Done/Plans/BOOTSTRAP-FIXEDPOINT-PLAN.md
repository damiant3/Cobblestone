# Bootstrap Fixed-Point Plan

**Goal:** Make `Codex.Codex/out/Codex.Codex.cs` (Stage 0) byte-identical to
`Codex.Codex/stage1-output.cs` (Stage 1). This is the definition of a
self-hosting compiler at fixed point.

**How to read this document:** Execute phases 1–7 in order. Each phase
modifies `.codex` files, then verifies. Do NOT read any other plan or
status document. Do NOT explore the codebase beyond the files listed here.
Everything you need is in this document.

---

## CRITICAL: Codex Language Syntax Rules

A previous agent wasted its entire context window fixing syntax errors.
**Read this section first.** Codex is NOT C#, NOT Haskell, NOT ML.

### Operators — THE #1 MISTAKE

| What you want | Codex syntax | NOT this |
|--------------|-------------|----------|
| Boolean AND | `a & b` | ~~`a && b`~~ |
| Boolean OR | `a \| b` | ~~`a \|\| b`~~ |
| String concat | `a ++ b` | ~~`a + b`~~ |
| List cons | `x :: xs` | ~~`x : xs`~~ |
| Equality | `a == b` | `a == b` (same) |

`&&` and `||` do NOT exist in Codex. The parser will reject them. Use
single `&` and single `|`.

### If-then-else

Codex requires `then` after every `if` condition:
```
if x == 0 then "zero"
else if x == 1 then "one"
else "other"
```

NOT:
```
if (x == 0) "zero"     -- WRONG: no parens, needs 'then'
```

Flat `else if` chains should stay at the **same indentation level**:
```
      if n == "a" then True
      else if n == "b" then True
      else if n == "c" then True
      else False
```

Do NOT ramp indentation deeper with each `else if`. The parser accepts it
but it creates fragile, unreadable code.

### Function calls

Codex uses juxtaposition for application: `f x` not `f(x)`.
Multi-arg calls are curried: `f a b c` not `f(a, b, c)`.
Parentheses are for grouping: `f (g x)` passes `g x` as one arg to `f`.

### String literals containing special characters

When emitting C# code that contains `&&`, `||`, etc., those appear inside
**string literals** (inside `"..."`) and are fine. The operator restriction
only applies to Codex-level boolean logic.

Example — this is CORRECT:
```
"(" ++ emit-expr a ++ ".Length > 0 && char.IsLetter(" ++ emit-expr a ++ "[0]))"
```
The `&&` is inside the string being built. The `++` is the Codex operator.

### Record field access

Use dots: `d.name`, `d.params`, `p.type-val`. Dashes in field names are fine.

### When expressions (pattern matching)

```
when expr
  if Pattern1 (x) (y) -> body1
  if Pattern2 -> body2
  if _ -> fallback
```

The `when` keyword, then each branch starts with `if`. Wildcard is `_`.

### File structure

Every `.codex` file MUST start with `Chapter: Name` on line 1. Then
sections with `Section: Name`. Code blocks are indented 4 spaces under
sections.
Damian says: HOWEVER the tools that you will use misreport this frequently.  It is a tool error.
If you find yourself frustrated "what happened to line 1?"  it is probably a tool reporting a false absence.  In that case, try writing a script in ps1 file or just terminal command that checks the line for you, instead of relying on the native tool.

---

## Architecture (read once, do not explore)

```
.codex source files
       │
       ├──(codex build)──→ Codex.Codex/out/Codex.Codex.cs    ← Stage 0 output
       │   uses: src/Codex.Emit.CSharp/CSharpEmitter.*.cs     ← "reference emitter"
       │
       └──(Codex.Bootstrap)──→ Codex.Codex/stage1-output.cs   ← Stage 1 output
           uses: tools/Codex.Bootstrap/CodexLib.g.cs           ← compiled from .codex
           which is a copy of: Codex.Codex/out/Codex.Codex.cs
```

Both pipelines read the **same** `.codex` source files and should produce
**identical** C# output. They don't today because the Codex-language emitter
(`Codex.Codex/Emit/CSharpEmitter.codex`) produces simpler output than the
reference C# emitter (`src/Codex.Emit.CSharp/CSharpEmitter.*.cs`).

**Strategy:** Modify the `.codex` emitter to produce the same patterns as the
C# emitter. The C# emitter is the **ground truth**. Do not modify it.

---

## Verification after every phase

```powershell
# 1. Regenerate Stage 0 from .codex sources
dotnet run --project tools/Codex.Cli -- build Codex.Codex

# 2. Copy Stage 0 into Bootstrap
copy Codex.Codex\out\Codex.Codex.cs tools\Codex.Bootstrap\CodexLib.g.cs

# 3. Build everything
dotnet build Codex.sln

# 4. Run tests
dotnet test Codex.sln

# 5. Generate Stage 1
dotnet run --project tools/Codex.Bootstrap

# 6. Diff
fc Codex.Codex\out\Codex.Codex.cs Codex.Codex\stage1-output.cs
```

If step 3 or 4 fails, you broke something. Fix it before continuing.

---

## Execution discipline

- **ONE PHASE AT A TIME.** Complete a phase, run full verification, commit
  mentally that it works, then move to the next.
- **Back up before editing.** Copy `CSharpEmitter.codex` to
  `CSharpEmitter.codex.bak` before each phase. The edit tool can nuke content.
- **Read the file before editing.** Always `get_file` first.
- **If `codex build` fails, read the error carefully.** The error will say
  which function and which line. 90% of the time it's `&&` instead of `&`,
  `||` instead of `|`, or a missing `then` after `if`.
- **Do NOT attempt all phases at once.** The previous agent tried phases 2–7
  in one shot and got stuck in an infinite syntax-fix loop.

---

## Files you will modify

| File | What changes |
|------|-------------|
| `Codex.Codex/Syntax/Parser.codex` | Right-associativity for `++`, `::`, `^`, `->` |
| `Codex.Codex/Emit/CSharpEmitter.codex` | All emission logic |

## Files you must read as reference (read-only, do not modify)

| File | Why |
|------|-----|
| `src/Codex.Emit.CSharp/CSharpEmitter.cs` | Reference: `Emit()`, `EmitDefinition()`, `EmitArgument()` |
| `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs` | Reference: `EmitExpr()`, `EmitApply()`, `EmitLet()`, `EmitBinary()` |
| `src/Codex.Emit.CSharp/CSharpEmitter.Match.cs` | Reference: `EmitMatch()`, `EmitCtorPatternBody()` |
| `src/Codex.Emit.CSharp/CSharpEmitter.TailCall.cs` | Reference: `HasSelfTailCall()`, `EmitTailCallDefinition()` |
| `Codex.Codex/out/Codex.Codex.cs` | The target output you must match |

## Previous attempt reference file

`Codex.Codex/Emit/CSharpEmitter.codex.new` contains a **previous agent's
attempt** at the full rewrite. It is ~874 lines and covers ALL phases (2–7).
The logic is largely correct but has syntax bugs:
- Uses `&&` instead of `&` (lines 112, 309, 311, 313, 315, 330, 332, 870)
- Uses `||` instead of `|` (lines 356, 681, 690)
- Missing `Chapter: CSharp Emitter` on line 1

You can use this file as a **reference for the logic structure** but you
MUST fix the operator syntax when transcribing. Do NOT copy it verbatim.

---

## Phase 1: Parser — Right-associative operators

**File:** `Codex.Codex/Syntax/Parser.codex`

**Problem:** `parse-binary-loop` always recurses with `prec + 1`, making all
operators left-associative. The reference C# parser uses `prec` (not
`prec + 1`) for right-associative operators (`++`, `::`, `^`, `->`).

**Changes:**

1. Add after `operator-precedence` (after the `if _ -> 0 - 1` line):

```
    is-right-assoc : TokenKind -> Boolean
    is-right-assoc (k) = when k
      if PlusPlus -> True
      if ColonColon -> True
      if Caret -> True
      if Arrow -> True
      if _ -> False
```

2. In `parse-binary-loop`, replace the line:
```
              in let right-result = parse-binary st2 (prec + 1)
```
with:
```
              in let next-min = if is-right-assoc op.kind then prec else prec + 1
                in let right-result = parse-binary st2 next-min
```

The `op` variable is already bound as `let op = current st` on the line above.

---

## Phase 2: Definition body style

**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

**Problem:** `emit-def` emits `=> expr;` (expression-bodied). The reference
emitter emits `{ return expr; }` (block body).

**Change `emit-def`** — replace the string that ends with:
```
++ ") => " ++ emit-expr d.body ++ ";\n"
```
with:
```
++ ")\n    {\n        return " ++ emit-expr d.body ++ ";\n    }\n"
```

This is a small, safe change. Verify it compiles before moving on.

---

## Phase 3: Multi-arg calls, constructor calls, argument wrapping

**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

This is the largest mechanical refactor. All `emit-*` functions must gain
a `List ArityEntry` parameter so they can look up definition arities.

### What to add

1. **ArityEntry record** and helpers:
```
    ArityEntry = record {
      def-name : Text,
      arity : Integer
    }

    build-arities : List IRDef -> Integer -> List ArityEntry
    lookup-arity : List ArityEntry -> Text -> Integer
    is-upper-char : Text -> Boolean        -- uses char-code, & not &&
    is-ctor-name : Text -> Boolean
```

2. **Thread `arities` parameter** through every emit function.

3. **Rewrite `IrApply` case** to flatten chains:
   - Constructor calls → `new Ctor(a, b, c)`
   - Known multi-arg defs → `f(a, b, c)`
   - Otherwise → `f(a)(b)(c)` (current behavior)

4. **Add `emit-argument`** that wraps function-typed names in
   `new Func<T,R>(name)` (matching reference `EmitArgument`).

5. **Record emission** — drop field names, use positional args.

### WATCH OUT for `&`

The `is-upper-char` function uses `&`:
```
    is-upper-char (c) =
      let code = char-code c
      in code >= 65 & code <= 90
```

NOT `&&`.

Similarly, multi-arg builtin checks use `&`:
```
      if root == "char-at" & list-length args == 2
```

### Reference

See `CSharpEmitter.codex.new` lines 85–391 for the full implementation.
The logic is correct — just fix `&&` → `&` on lines 112, 309, 311, 313,
315, 330, 332.

---

## Phase 4: Builtin special-casing

**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

Add single-arg and multi-arg builtin detection in `emit-apply`, checked
BEFORE the general flatten logic.

Single-arg builtins (checked via `IrName` on the function):
`show`, `negate`, `print-line`, `text-length`, `integer-to-text`,
`text-to-integer`, `is-letter`, `is-digit`, `is-whitespace`, `char-code`,
`code-to-char`, `list-length`, `open-file`, `read-all`, `close-file`

Multi-arg builtins (checked via `find-apply-root`):
`char-at` (2 args), `substring` (3), `list-at` (2), `text-replace` (3)

### Reference

See `CSharpEmitter.codex.new` lines 251–316 for the full implementation.

---

## Phase 5: Match emission

**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

Replace the current `switch`-style match emission with the reference
emitter's ternary `is`-chain pattern.

### Reference pattern

```
((Func<ScrutType, ResultType>)((_scrutineeN_) =>
  (_scrutineeN_ is Ctor1 _mCtor1N_ ?
    ((Func<F0Type, R>)((f0name) => BODY))((F0Type)_mCtor1N_.Field0)
  : ...
  : throw new InvalidOperationException("Non-exhaustive match")))
))(SCRUTINEE)
```

### Match counter

The counter `N` increments per match expression. Thread an `Integer`
match-ID parameter through `emit-expr` and all functions that call it.
Increment it when entering `emit-match`.

### Field extraction order

Fields are extracted with **reverse-order wrapping** (outermost Func =
last field, innermost = first field) but **forward-order closing**
(first `))( cast field0 )`, then `))( cast field1 )`).

### Key helpers needed

```
    count-ctor-lit-branches : List IRBranch -> Integer -> Integer
    emit-match-branches : ... (recursive, builds ternary chain)
    emit-ctor-pattern-body : ... (collects var bindings, wraps in Funcs)
    collect-var-bindings : List IRPat -> Text -> Integer -> List VarBinding
    emit-var-bindings-and-body : ... (reverse-open, forward-close)
    emit-cast : CodexType -> Text   (empty for ErrorTy/TypeVar/NothingTy/VoidTy)
    repeat-close-parens : Integer -> Text
```

### WATCH OUT

`emit-var-bindings-and-body` must handle the zero-bindings case (just emit
body directly). The reference does `bindings.Count == 0` → `EmitExpr`.

### Reference

See `CSharpEmitter.codex.new` lines 392–503 for the full implementation.
The logic is correct.

---

## Phase 6: Tail-call optimization

**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

### Detection

```
    has-self-tail-call : IRDef -> Boolean
    expr-has-tail-call : IRExpr -> Text -> Boolean
    is-self-call : IRExpr -> Text -> Boolean
```

WATCH OUT — `expr-has-tail-call` uses `|` (not `||`):
```
    expr-has-tail-call (e) (name) =
      when e
        if IrIf (c) (t) (el) (ty) -> expr-has-tail-call t name | expr-has-tail-call el name
```

### Emission

When `has-self-tail-call` is true, emit:
```
    public static TYPE NAME(PARAMS)
    {
        while (true)
        {
            TCO_BODY
        }
    }
```

Key helpers: `emit-tco-body`, `emit-tco-match`, `emit-tco-jump`,
`emit-tco-temp-assigns`, `emit-tco-param-assigns`, `make-pad`.

### Modify `emit-def`

Check `has-self-tail-call d` first. If true, call `emit-tco-def` instead
of the normal block body.

### Reference

See `CSharpEmitter.codex.new` lines 670–788 for the full implementation.
Fix `||` → `|` on lines 681, 690.

---

## Phase 7: Module-level differences

### 7a. Main call emission

Add `find-main-def` and `emit-main-call` helpers. Emit
`ClassName.main();\n\n` before type defs when there's an effectful main
with 0 params.

### 7b. Effectful detection

Add `is-effectful-def`, `is-effectful-type`, `is-do-type`. An effectful
def has `VoidTy` as its return type after stripping function arrows.

For effectful non-TCO defs, emit:
```
    {
        BODY;
        return null;
    }
```

### 7c. Error expression

The reference emitter emits `IrError` as:
```
throw new InvalidOperationException("MESSAGE")
```
NOT the current `/* error: MESSAGE */ default`.

### Reference

See `CSharpEmitter.codex.new` lines 790–874 for the full implementation.
Fix `&` on line 870 (it already uses `&` in the current `.codex` file).

---

## Success criteria

```
fc Codex.Codex\out\Codex.Codex.cs Codex.Codex\stage1-output.cs
```

Output: `FC: no differences encountered`

---

## What NOT to do

- Do NOT modify any file in `src/` (the reference C# compiler)
- Do NOT modify `docs/00-OVERVIEW.md` through `docs/10-PRINCIPLES.md`
- Do NOT read `BOOTSTRAP-STATUS.md`, `HANDOFF-*.md`, `M13-*.md`, `FORWARD-PLAN.md`
- Do NOT attempt multiple phases without verifying between each one
- Do NOT use `&&` or `||` as Codex operators — use `&` and `|`
- Do NOT delete the `Chapter:` line at the top of any `.codex` file
- Do NOT attempt to write the entire CSharpEmitter.codex from scratch —
  edit the existing file incrementally, phase by phase
- Clean up `CSharpEmitter.codex.bak` and `CSharpEmitter.codex.new` when done


-- What we just did, last sesion:
1. Removed duplicate is-upper-char from CSharpEmitter.codex
The function was defined in both NameResolver.codex and CSharpEmitter.codex. Since all .codex files in Codex.Codex compile as a single module, this caused a CDX3001: Duplicate definition error. Removed lines 109-112 from CSharpEmitter.codex; the usage in is-ctor-name resolves to the NameResolver.codex copy.
2. Added FileName to SourceSpan (diagnostic improvement)
src/Codex.Core/SourceText.cs — SourceSpan now has a required FileName parameter (no default). ToString() includes the file name when non-empty. Through() preserves the file name.
src/Codex.Syntax/Lexer.cs — Stores m_fileName from SourceText, threads it through MakeSpan and SourceSpan.Single calls.
src/Codex.Syntax/ProseParser.cs — MakeSpan passes m_source.FileName.
tools/Codex.Cli/Program.Compile.cs, Program.Incremental.cs — Fallback spans use "<combined>".
tests/Codex.Core.Tests/CoreTests.cs — Updated test to pass "test.codex".
3. Fixed the actual parser bug in Parser.Expressions.cs
src/Codex.Syntax/Parser.Expressions.cs — ParseAtom now consumes .field chains after parenthesized expressions, just like it already did for identifiers. Without this, (list-at branches i).body was parsed as a field access on the outer function application instead of on the parenthesized sub-expression, causing the type checker to see IRBranch where IRExpr was expected.
Verification
•	✅ dotnet build Codex.sln — 0 errors (excluding pre-existing Codex.Codex.csproj entry point issue)
•	✅ dotnet test Codex.sln — 689 tests pass
•	✅ codex build Codex.Codex — produces Codex.Codex/out/Codex.Codex.cs with no errors
•	✅ Stage 1 generated via Codex.Bootstrap
•	The remaining Stage 0 vs Stage 1 differences are the expected bootstrap fixedpoint gaps (phases 2-7 in the plan)
