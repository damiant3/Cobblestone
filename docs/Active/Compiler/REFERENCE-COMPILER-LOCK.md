# Reference Compiler Lock

**Date**: 2026-03-19 (verified via system clock)
**Locked at commit**: `6d8bb2c`
**Locked by**: Copilot (Windows agent)

---

## What This Means

The C# reference compiler (`src/` projects) is **frozen**. All future language
development happens in Codex source (`.codex` files), compiled through the
self-hosted pipeline.

The reference compiler's sole purpose going forward is to serve as **Stage 0** —
the bootstrap compiler that builds the self-hosted compiler from `.codex` source.
It should not receive new features. Bug fixes to Stage 0 are permitted only when
they are necessary to compile the self-hosted compiler's `.codex` source.

---

## Bootstrap Chain

```
Stage 0:  C# reference compiler (src/) → compiles .codex → Codex.Codex.cs
Stage 1:  Codex.Codex.cs (compiled by dotnet) → compiles .codex → stage1-output.cs
Stage 2+: stage1-output.cs → compiles .codex → stage2-output.cs  (= stage1-output.cs)
```

**Fixed point**: Stage 1 output = Stage 2 output (byte-for-byte identical).

---

## Checksums at Lock Time

| File | SHA-256 | Size |
|------|---------|------|
| `Codex.Codex/out/Codex.Codex.cs` (Stage 0 output) | `3E2E7796D9CF7C6745099ABA9C513261BB079FA36BBFBCE699A134CE2FBCC319` | 295,507 chars |
| `Codex.Codex/stage1-output.cs` (Stage 1 = fixed point) | `F4E12D0F66682502435AB5308A268241A237026C643015B6E9DD8E64FCCC4BEE` | 231,568 chars |
| `Codex.Codex/stage3-output.cs` (Stage 3 = Stage 1) | `F4E12D0F66682502435AB5308A268241A237026C643015B6E9DD8E64FCCC4BEE` | 231,568 chars |

---

## What the Reference Compiler Supports (Frozen Feature Set)

- Algebraic types (sum types, record types)
- Pattern matching with exhaustiveness checking
- Bidirectional type inference with unification
- Effects: built-in (Console, State, FileSystem) + user-defined (`effect ... where`)
- Effect handlers (`with Effect expr` + `resume` continuations)
- Do-notation with typed returns (works with any effect)
- Linear types
- Dependent types (type-level arithmetic, proof obligations)
- Proofs (refl, sym, trans, cong, induction)
- Module system (import/export, visibility control)
- Standard prelude (Maybe, Result, Either, Pair)
- String interpolation (`#{expr}`)
- Literate programming (prose documents)
- 12 backends (C#, JS, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL, IL, Babbage)
- LSP server (diagnostics, hover, completion, go-to-def, semantic tokens)
- Content-addressed repository with collaboration protocol

---

## Verification at Lock Time

| Check | Result |
|-------|--------|
| `dotnet build Codex.sln` | ✅ 0 errors, 0 warnings |
| `dotnet test Codex.sln` | ✅ 810 tests passing |
| Bootstrap fixed point | ✅ Stage 1 = Stage 3 (231,568 chars) |
| Type debt | 7 `object` refs, 0 `_p0_` proxies |
| Exit criterion | ✅ `expr-calculator.codex` — 125-line recursive descent parser, 10/10 PASS |

---

## Rules Going Forward

1. **Do not add features to `src/` projects.** New language features are implemented in `.codex` source.
2. **Bug fixes to Stage 0 are permitted** only when the self-hosted compiler cannot compile due to a Stage 0 bug.
3. **Test projects (`tests/`) may still be modified** — they test the reference compiler which remains the build system.
4. **The CLI (`tools/Codex.Cli/`) may still be modified** — it's the driver, not the compiler.
5. **Regenerating Stage 0 output** (`codex build Codex.Codex/`) is permitted and expected as `.codex` source evolves.

---

## Lock Overrides

### Override 1: `char-code-at` builtin (2026-03-20)

**Authorized by**: User (project owner)
**Agent**: Claude (Opus 4.6, Linux)
**Justification**: Performance-critical. The self-hosted lexer was **800× slower** than the
reference compiler's lexer because `char-at` returns `Text` (heap-allocated string per
character). For a 163K-char input, this produces billions of bytes of garbage.

**Change**: Added `char-code-at : Text -> Integer -> Integer` as a new builtin.
Emits `((long)source[(int)index])` — zero allocation.

**Files modified** (4 files, ~10 lines total):
- `src/Codex.Types/TypeEnvironment.cs` — type binding
- `src/Codex.IR/Lowering.cs` — builtin type map
- `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs` — emission + multi-arg set
- `src/Codex.Semantics/NameResolver.cs` — builtin name set

**Scope**: This is a new builtin, not a language feature. It does not change parsing,
type checking, or any existing behavior. No existing tests are affected (836 passing).

**Impact**: Enables the self-hosted lexer to use integer character codes on the hot path,
eliminating per-character string allocation. Projected: lexer 800× → ~5×, overall 28× → ~4×.

### Override 2: `read-file` builtin (2026-03-20)

**Authorized by**: User (project owner)
**Agent**: Copilot (VS 2022, Windows)
**Justification**: The self-hosted compiler needed to read source files from disk.
The existing `open-file`/`read-all`/`close-file` builtins use linear `FileHandle` types
and return `Pair Text FileHandle`, which the self-hosted type checker doesn't model.
A clean `Text -> Text` builtin is the right abstraction for "read a file."

**Change**: Added `read-file : Text -> [FileSystem] Text` as a new builtin.
Emits `File.ReadAllText(path)`.

**Files modified** (4 files, ~10 lines total):
- `src/Codex.Types/TypeEnvironment.cs` — type binding
- `src/Codex.IR/Lowering.cs` — builtin type map
- `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs` — emission rule
- `src/Codex.Semantics/NameResolver.cs` — builtin name set

**Scope**: New builtin, not a language feature. No parsing, type checking, or existing
behavior changes. All 854 tests passing.

### Override 3: Dogfood builtins — 9 new system/IO/string builtins (2026-03-21)

**Authorized by**: User (project owner)
**Agent**: Claude (Opus 4.6, claude.ai, Linux)
**Justification**: To write the agent toolkit in .codex (dogfooding), the language
needed system interaction primitives that didn't exist. Without these builtins, every
tool had to be written in C# or shell scripts — defeating the purpose of having a
self-hosting language. The user explicitly directed: "if we have to break the seal on
reference, do it, write the justify, port this to real code and emit the IL."

**New builtins** (9):

| Builtin | C# Emission | Type |
|---------|-------------|------|
| `get-args` | `Environment.GetCommandLineArgs()` | `→ List Text` |
| `write-file` | `File.WriteAllText(path, text)` | `Text → Text → Nothing` |
| `file-exists` | `File.Exists(path)` | `Text → Boolean` |
| `list-files` | `Directory.GetFiles(dir, pattern)` | `Text → Text → List Text` |
| `text-split` | `text.Split(delim)` | `Text → Text → List Text` |
| `text-contains` | `text.Contains(sub)` | `Text → Text → Boolean` |
| `text-starts-with` | `text.StartsWith(prefix)` | `Text → Text → Boolean` |
| `get-env` | `Environment.GetEnvironmentVariable(name)` | `Text → Text` |
| `current-dir` | `Directory.GetCurrentDirectory()` | `→ Text` |

**Files modified** (9 files across reference + self-hosted compilers):
- `src/Codex.Semantics/NameResolver.cs` — builtin name set
- `src/Codex.Types/TypeEnvironment.cs` — type bindings
- `src/Codex.IR/Lowering.cs` — builtin type map
- `src/Codex.Emit.CSharp/CSharpEmitter.cs` — added `using System.IO;` to output
- `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs` — emission rules (zero-arg, single-arg, multi-arg)
- `Codex.Codex/Semantics/NameResolver.codex` — self-hosted builtin names
- `Codex.Codex/Types/TypeEnv.codex` — self-hosted type bindings
- `Codex.Codex/Emit/CSharpEmitter.codex` — added `using System.IO;` to self-hosted output
- `Codex.Codex/Emit/CSharpEmitterExpressions.codex` — self-hosted emission rules

**Scope**: New builtins only. No parsing, type checking, or existing behavior changes.
All changes are additive — existing builtins untouched. Both reference and self-hosted
emitters updated in parallel to maintain bootstrap consistency.

### Override 4: IL emitter — List\<T\> support + dogfood builtins (2026-03-21)

**Authorized by**: User (project owner)
**Agent**: Claude (Opus 4.6, Linux)
**Justification**: The agent toolkit (`tools/codex-agent/peek.codex`) compiled to C# but
produced invalid IL because the IL emitter had no `List<T>` support — no constructor, no
`Count`, no indexer. This meant `.codex` source could not produce standalone `.exe` files
for any program using lists. The user explicitly directed: "if that takes us breaking seal
again, fine do it and write the justify. that is why we are dogfooding this."

**What was missing**: The IL emitter could emit all primitive types, records, sum types,
generics, pattern matching, and tail-call optimization — but had zero support for
`System.Collections.Generic.List<T>`, which is the CLR backing type for all Codex `List`
values. It also lacked `File.ReadAllText`, `File.Exists`, `Environment.GetCommandLineArgs`,
and `String.Split` — all required by Override 3's dogfood builtins.

**Changes to `src/Codex.Emit.IL/ILAssemblyBuilder.cs`** (1 file, ~250 lines added):

| Category | What |
|----------|------|
| Assembly refs | `System.Collections`, `System.IO.FileSystem` |
| Type refs | `List`1`, `IEnumerable`1`, `Environment`, `File`, `StringSplitOptions` |
| TypeSpecs | `List<string>`, `IEnumerable<string>` (generic instantiation blobs) |
| Member refs | `List<string>.ctor(IEnumerable<!0>)`, `.get_Count()`, `.get_Item(int)` |
| Member refs | `String.Split(string, StringSplitOptions)`, `Environment.GetCommandLineArgs()` |
| Member refs | `File.ReadAllText(string)`, `File.Exists(string)` |
| EncodeType | `ListType` → `GenericInstantiation(List`1`, string)` |
| Builtins (6) | `get-args`, `text-split`, `list-length`, `list-at`, `read-file`, `file-exists` |
| IRName dispatch | Zero-arg builtins now route through `TryEmitBuiltinCore` (was hardcoded `read-line` only) |
| maxstack | Increased from 8 to 32 for user definitions (deeply nested Codex expressions exceed 8) |

**Scope**: IL emitter only. No changes to parsing, type checking, IR, or C# emitter.
All 700 tests pass (659 + 23 + 18). Existing IL compilation (e.g., `hello.codex`) unaffected.

**Proof**: `peek.codex` compiles through the IL backend and the resulting `peek.exe` correctly
reads files, displays line ranges, handles missing files, and prints usage — all from pure
`.codex` source → IL → native execution.

### Override 5: `run-process` builtin (2026-03-20)

**Authorized by**: User (project owner)
**Agent**: Copilot (Claude Sonnet 4, VS 2022)
**Justification**: The unified `codex-agent.exe` tool (written in Codex, compiled via IL)
needed `build` and `test` commands that invoke `dotnet build` and `dotnet test`. Without
a process-launching builtin, the agent tool could read files and manage snapshots but
couldn't actually build or test — the two most critical agent operations.

**Changes**:

| File | What |
|------|------|
| `src/Codex.Semantics/NameResolver.cs` | Added `"run-process"` to `s_builtins` set |
| `src/Codex.Types/TypeEnvironment.cs` | Type: `Text → Text → Text` (program, args → stdout) |
| `src/Codex.IR/Lowering.cs` | Same type in lowering builtin map |
| `src/Codex.Emit.CSharp/CSharpEmitter.Expressions.cs` | Emits `ProcessStartInfo` + `Process.Start` IIFE |
| `src/Codex.Emit.IL/ILAssemblyBuilder.cs` | Assembly ref `System.Diagnostics.Process`, type refs, 7 member refs, IL sequence |
| `Codex.Codex/Emit/CSharpEmitterExpressions.codex` | Self-hosted emitter updated |

**Scope**: New builtin only. No parsing, type checking, or existing behavior changes.
All 865 tests pass. Existing compilation unaffected.

**Proof**: `codex-agent.exe build` and `codex-agent.exe test` successfully invoke
`dotnet build Codex.sln` and `dotnet test Codex.sln` from pure `.codex` source.

### Override 6: Safe `text-to-integer` in IL emitter (2026-03-21)

**Authorized by**: User (project owner)
**Agent**: Copilot (Claude Sonnet 4, VS 2022, Windows)
**Justification**: The IL emitter's `text-to-integer` builtin emitted raw
`Int64.Parse(string)`, which throws `FormatException` on non-numeric input.
Every `.exe` compiled from `.codex` source crashed on invalid integer strings —
including the dogfood agent toolkit. The IL emitter has no try/catch support,
so the fix must be at the builtin level.

**Change**: Replaced `Int64.Parse` with `Int64.TryParse(string, out long)` in
the emitted IL. Returns the parsed value on success, `0` on failure.

**Files modified** (1 file, ~20 lines changed):
- `src/Codex.Emit.IL/ILAssemblyBuilder.cs` — added `m_int64TryParseRef` with
  by-ref parameter encoding, replaced `text-to-integer` case with TryParse +
  branch pattern

**Concurrent cleanup**: Flipped `LEGACY_EMITTERS` default to opt-in in
`Codex.Cli.csproj` and `Codex.Types.Tests.csproj`; moved JS/Python refs inside
`#if` guards in `Program.Build.cs`, `Helpers.cs`, `CorpusEmissionTests.cs`.

**Scope**: IL emitter only. No changes to parsing, type checking, IR, or C# emitter.
63 IL emitter tests pass (57 existing + 6 new). 57 agent toolkit tests pass.

**Details**: See [REFERENCE-COMPILER-NOTES.md](REFERENCE-COMPILER-NOTES.md).

### Override 7: `__list_append` in-place fast path + geometric capacity (2026-04-13)

**Authorized by**: User (project owner)
**Agent**: Hex (Claude Code CLI, Opus 4.6, 1M context)
**Justification**: The bare-metal `__list_append` runtime helper emitted by
the x86-64 backend always allocated a fresh list with `capacity == count`
(no slack) and copied both inputs. Combined with `acc ++ [x, y]` patterns
in tight loops (notably `collect-func-addr-patches` and
`collect-rodata-patches` in `x86-64-finalize`), this produced O(n²) total
work per pattern and O(n) wasted heap per iteration. For the
self-compilation binary path, this manifested as `compile-to-binary` taking
hours and tripping `bin-finalize` past every reasonable timeout — even
though no fault was occurring. Bisection isolated a 107-line minimum repro
(`samples/list-append-perf-min.codex`) using only `Integer` and `Text`
primitives — no Hamt, no Maybe, no sum types, no citations.

**Change**: Rewrote `EmitListAppendHelper` in
`src/Codex.Emit.X86_64/X86_64CodeGen.cs` to mirror `EmitListSnocHelper`'s
strategy:
- **Path 1 (in-place)** when `a.cap - a.count >= b.count`: copy `b`'s
  slots into `a`'s spare slots, bump `a.count`, return `a`'s pointer.
  No `r10` rollback (would require contiguity check that fails under
  intermediate allocations between `a` and `b`); `b`'s small allocation
  leaks per call but is bounded.
- **Path 2 (alloc with geometric capacity)** when no spare: allocate new
  list with `capacity = max(2 * total, 4)` (matching `__list_snoc` Path 3),
  copy `a` then `b`. Subsequent `++` calls hit Path 1 until cap fills,
  then double again. Amortized O(1) per element.

**Files modified**:
- `src/Codex.Emit.X86_64/X86_64CodeGen.cs` — `EmitListAppendHelper` rewritten.
- `Codex.Codex/Emit/X86_64Helpers.codex` — `emit-list-append` mirrored so
  the self-host emitter produces byte-identical `__list_append` bytes.
  Required for binary-pingpong stage1 === stage2 (both stages must emit the
  same helper encoding).

**Linear ownership invariant**: Path 1 mutates `a` in place — same
invariant as `__list_snoc` Path 1/2 and `record-set!`. Documented in
`docs/Designs/Language/SAFE-MUTATION.md`. Both `++` and `list-snoc` now
share this contract.

**Scope**: x86-64 bare-metal runtime helper only. No parsing, type
checking, IR, or other emitter changes. ELF size impact: ~120 bytes per
output binary.

**Verification**:
- `dotnet build Codex.sln` — clean
- Text pingpong (`tools/pingpong.sh`) — PASS, stage1 === stage2 byte-identical at 539,852 bytes; sem-equiv PASS
- Smoke samples — PASS, sizes match pre-fix ±8 bytes (geometric cap header padding)
- Min repro `samples/list-append-perf-min.codex` — was hitting script's 120s timeout pre-fix, now completes in ~40s

