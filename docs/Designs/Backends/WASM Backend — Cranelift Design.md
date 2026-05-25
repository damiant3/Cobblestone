# WASM Backend — Cranelift Design

**Date**: 2026-03-21
**Status**: Design
**Author**: Claude (Opus 4.6, claude.ai)
**Depends on**: Codex IR (`Codex.IR`), Emission framework (`Codex.Emit`)

---

## Summary

This document describes the design of the Codex WebAssembly backend. The backend
emits WASM modules from Codex IR using Cranelift as the code generation engine.
This is backend #13 — the first to target a portable binary format rather than
a textual source language.

The WASM backend is different from the existing twelve backends in a fundamental
way: those backends emit source text that another compiler compiles. This backend
emits a binary artifact directly. The Codex compiler becomes, for the first time,
a real compiler in the traditional sense — not a transpiler.

---

## Why WASM, Why Now

The self-hosted compiler is stable. The IR is proven across twelve backends. The
type system is sound. The fixed point is established. We have earned the right to
target something harder.

WASM gives us three things no other target does:

1. **Portable execution.** A `.wasm` module runs in browsers, on servers (Wasmtime,
   Wasmer, WasmEdge), on embedded devices, and inside other programs as a plugin
   format. One compilation, every platform.

2. **Sandboxed by default.** WASM modules cannot access the file system, network,
   or host memory unless the host explicitly grants capabilities. This aligns
   perfectly with the Codex effect system — effects are declared, not smuggled.

3. **Binary distribution.** No toolchain required on the consumer side. A `.wasm`
   file is the deliverable. No `dotnet`, no `node`, no `rustc`. Just a runtime.

---

## Architecture

### Where It Sits

```
Codex IR (IRModule)
    │
    ▼
Codex.Emit.Wasm (new project)
    │
    ├── WasmEmitter : ICodeEmitter      — orchestrates module generation
    ├── CraneliftBridge                  — FFI to cranelift-codegen via C API
    ├── WasmModuleBuilder                — constructs WASM module structure
    ├── TypeMapper                       — Codex types → WASM value types
    ├── BuiltinEmitter                   — 22 builtins → WASM implementations
    └── WasiBindings                     — WASI preview 2 imports for effects
```

### The Cranelift Path

We use Cranelift rather than emitting raw WASM bytecode by hand. The reasons:

- **Register allocation.** WASM is a stack machine, but Cranelift's IR is
  register-based. It handles the stack machine encoding, spilling, and
  instruction selection. We describe what we want; it figures out how.

- **Optimization.** Cranelift performs constant folding, dead code elimination,
  and instruction combining. Our IR arrives unoptimized — Cranelift picks up
  the slack without us writing optimization passes.

- **Correctness.** Cranelift is battle-tested in Wasmtime and Firefox. It has
  been fuzzed extensively. We inherit that confidence.

The alternative — emitting WASM bytecode directly via `wasm-encoder` — is simpler
for trivial programs but becomes a maintenance burden as we need GC proposals,
multi-value returns, tail calls, and exception handling. Cranelift gives us a
path to all of these.

### Integration Strategy

Cranelift is a Rust library. Our compiler is C#/.NET (bootstrap) and Codex
(self-hosted). We bridge via Cranelift's C API (`cranelift-native` crate
exposing `extern "C"` functions), consumed through .NET P/Invoke or through
a thin CLI wrapper that reads Codex IR as JSON and emits `.wasm`.

**Phase 1 (CLI bridge):** A standalone Rust binary `codex-cranelift` that reads
a serialized `IRModule` on stdin and writes a `.wasm` file to stdout. The Codex
emitter serializes IR, invokes the binary, and returns the result. This is crude
but gets us running immediately with zero FFI complexity.

**Phase 2 (native integration):** A shared library (`libcodex_cranelift.so` /
`codex_cranelift.dll`) with a C API that the .NET host or the self-hosted
compiler calls directly. Lower latency, no serialization overhead.

Phase 1 is the shipping strategy. Phase 2 is an optimization.

---

## Type Mapping

WASM's type system is minimal. The mapping from Codex types is:

| Codex Type | WASM Representation | Notes |
|-----------|---------------------|-------|
| `Integer` | `i64` | Truncated from arbitrary precision to 64-bit. Sufficient for bootstrap. |
| `Number` | `f64` | Direct mapping. |
| `Boolean` | `i32` | 0 = False, 1 = True. Standard WASM convention. |
| `Text` | `i32` (pointer into linear memory) | UTF-8 encoded, length-prefixed. Managed by a bump allocator. |
| `Nothing` | (no value) | Functions returning Nothing return void. |
| `List a` | `i32` (pointer to cons-cell chain) | Linked list in linear memory. |
| Records | `i32` (pointer to struct in linear memory) | Fields at fixed offsets. |
| Sum types | `i32` (pointer to tagged union) | First word is tag discriminant. |
| Functions | `i32` (table index) | Indirect calls via `call_indirect`. Closures are {table_index, env_ptr} pairs. |
| `Result a` | Sum type encoding | `Success` = tag 0, `Failure` = tag 1. |
| `Maybe a` | Sum type encoding | `Just` = tag 0, `Nothing` = tag 1. |

### Memory Management

For the initial implementation: a bump allocator. Allocate forward, never free.
This is correct for short-lived computations (which is what the bootstrap needs)
and avoids the complexity of a GC.

Future path: the WASM GC proposal (`struct`, `array`, `ref` types) will let us
move heap objects into the GC'd space. When that stabilizes in runtimes, we
switch. Until then, bump allocation is honest — it doesn't pretend to manage
memory, and programs that need long-running allocation know to use streaming
or bounded patterns.

---

## Effect Mapping via WASI

The Codex effect system maps naturally onto WASI capabilities:

| Codex Effect | WASI Interface | Import |
|-------------|---------------|--------|
| `Console` | `wasi:cli/stdio` | `fd_write` (stdout), `fd_read` (stdin) |
| `FileSystem` | `wasi:filesystem/types` | `open-at`, `read`, `write`, `stat` |
| `Time` | `wasi:clocks/wall-clock` | `now` |
| `Random` | `wasi:random/random` | `get-random-bytes` |
| `Network` (future) | `wasi:sockets/tcp` | `connect`, `send`, `receive` |

WASI Preview 2 uses the component model, which means effects become typed
imports. A Codex function with effect `[Console, FileSystem]` emits a WASM
module that imports exactly those WASI interfaces — nothing more. The host
can inspect the import section to see exactly what capabilities the module
requires. The type system's promise is enforced at the binary level.

This is the single most compelling alignment between Codex and WASM. The effect
row IS the capability list. No other language makes this correspondence so direct.

---

## Builtin Encoding

The 22 Codex builtins map to WASM as follows:

| Builtin | WASM Strategy |
|---------|--------------|
| `print-line` | Call `fd_write` on stdout fd (WASI) |
| `read-line` | Call `fd_read` on stdin fd (WASI), scan for newline |
| `show` | Type-dispatched: `i64` → itoa in linear memory, `f64` → dtoa, etc. |
| `text-length` | Read length prefix from text pointer |
| `char-at` | UTF-8 index into text buffer (with codepoint-aware offset) |
| `substring` | Allocate new text, copy bytes |
| `text-replace` | Linear scan + copy, allocate result |
| `text-to-integer` | Atoi in WASM (hand-rolled or imported helper) |
| `integer-to-text` | Itoa in WASM |
| `list-length` | Walk cons-cell chain, count |
| `list-at` | Walk cons-cell chain, index |
| `negate` | `i64.mul` by -1 or `f64.neg` |
| `char-code` | Read first byte/codepoint, return as `i64` |
| `code-to-char` | Encode codepoint to UTF-8, allocate 1-char text |
| `read-file` | WASI filesystem: `path-open` + `fd-read` + `fd-close` |
| `open-file` / `read-all` / `close-file` | WASI filesystem operations |
| `is-letter` / `is-digit` / `is-whitespace` | Codepoint range checks in WASM |

String operations are the heaviest lift. We implement a small string runtime
(~200 lines of hand-written WASM or Cranelift IR) that ships as a precompiled
module linked into every output. This is the one place we have a "runtime library"
— but it's baked into the `.wasm` binary, not a separate dependency.

---

## Tail Call Optimization

The existing backends all implement TCO. The WASM backend has two options:

1. **WASM tail call proposal** (`return_call` / `return_call_indirect`). This is
   the correct solution. Cranelift supports emitting tail calls. The proposal
   has reached Phase 4 in the WASM standards process and is available in
   Chrome, Firefox, and Wasmtime.

2. **Trampoline fallback.** For runtimes that don't support the tail call proposal,
   we rewrite tail-recursive functions as loops (same strategy as the C# and JS
   backends). The self-hosted compiler already detects self-tail-calls via
   `HasSelfTailCall` — the WASM emitter reuses that analysis.

Default: emit `return_call` with a CLI flag `--wasm-no-tailcall` to fall back
to trampolines for maximum compatibility.

---

## Closures and Higher-Order Functions

Codex is a functional language. Functions are values. This requires closures.

WASM doesn't have closures natively. The encoding:

1. Every closure is a pair: `(func_index : i32, env_ptr : i32)`.
2. The `func_index` points into the WASM function table.
3. The `env_ptr` points to a heap-allocated environment containing captured
   variables.
4. Calling a closure: `call_indirect` with the env_ptr as the first argument.
5. All closureable functions are emitted with an extra leading `env_ptr`
   parameter. Non-capturing functions receive a null env_ptr and ignore it.

This is the same strategy used by OCaml's WASM backend and by AssemblyScript.
It works. It's not glamorous. It's correct.

---

## Module Structure

A compiled Codex program becomes a single `.wasm` module with:

```
(module
  ;; Type section: function signatures
  ;; Import section: WASI capabilities (from effect declarations)
  ;; Function section: all Codex definitions
  ;; Table section: function table for indirect calls (closures)
  ;; Memory section: one linear memory (bump-allocated)
  ;; Global section: bump pointer, stack pointer
  ;; Export section: main function, memory
  ;; Code section: Cranelift-generated bytecode
  ;; Data section: string literals, constant data
)
```

Multi-file Codex programs (built with `codex build`) compile to a single merged
module. Cross-module references are resolved at compile time — WASM doesn't need
to know about Codex's module system.

---

## Testing Strategy

The WASM backend inherits the integration test corpus. Every existing sample
program that runs on C#/JS/Rust must also compile and produce identical output
under Wasmtime.

| Test category | What it verifies |
|--------------|-----------------|
| Builtin tests | All 22 builtins produce correct output via WASI |
| Pattern matching | Sum type tags, nested patterns, exhaustiveness |
| Recursion & TCO | Stack doesn't overflow on deep recursion |
| Closures | Captured variables survive scope exit |
| Multi-file | `codex build` + WASM produces correct linked module |
| Self-hosting (stretch) | The Codex compiler compiles itself to WASM and the result works |

The self-hosting-via-WASM test is a stretch goal but a powerful one: if the
compiler can compile itself to WASM and that WASM binary can compile Codex
source, the backend is definitively correct.

---

## The Bytecode Alliance, Cranelift, and Us

Cranelift is developed under the Bytecode Alliance, alongside Wasmtime, WASI,
and the component model tooling. These are excellent projects. We use them
gratefully and with respect.

Our relationship to the Bytecode Alliance is straightforward: **we are
downstream consumers of their public, open-source tools.** We do not need
their permission to target WASM. We do not need their endorsement to use
Cranelift. These are tools released under permissive licenses (Apache-2.0)
for exactly this purpose — for people to build compilers with.

That said, we recognize that our usage of Cranelift is unusual. Most Cranelift
consumers are WASM runtimes (compiling WASM → native). We are using it in the
opposite direction (compiling our IR → WASM). This is a supported use case —
Cranelift's ISA targets include WASM — but it is less traveled. We may encounter
rough edges.

### Our Posture

Codex is a public, open-source project. Every line of our compiler is visible.
Every design decision is documented. Every test is runnable. We are not merely
open to audit — **we actively invite it.**

If the Bytecode Alliance, or anyone in the WASM ecosystem, finds a problem with
how we use Cranelift, WASI, or the WASM specification, we want to hear about it.
The preferred process:

1. **Open a GitHub issue** describing the problem, with a reproduction case if
   possible.
2. **Or submit a PR** with a fix. We will review it, test it against our 854+
   test suite, and merge it if it's correct.
3. **Or just tell us.** Email, Discussions tab, carrier pigeon. We don't care
   about the medium. We care about correctness.

We are a correctness-obsessed project. Our compiler proves fixed points. Our
type system has zero debt. We run 854 tests on every change. If someone finds
a bug in our WASM emission, that's not an embarrassment — it's a gift. We will
fix it, add a regression test, and credit the finder.

We don't gatekeep, and we don't expect to be gatekept. The WASM specification
is public. Cranelift's API is documented. WASI is standardized. We read the
specs, we implement them, and we test that our implementation is correct. If
we're wrong, the test suite catches it or a kind stranger points it out. That's
how open-source works.

### Licensing

Cranelift: Apache-2.0 with LLVM exception. Wasmtime: Apache-2.0. WASI
specifications: W3C Community Contributor License Agreement. Our usage is
fully compliant with all of these. The Codex compiler's output (`.wasm` files)
contains Cranelift-generated machine code but no Cranelift source code — the
same relationship as GCC-compiled binaries to GCC itself.

---

## Implementation Phases

### Phase 1: Hello WASM

- Implement `WasmEmitter` with Cranelift CLI bridge
- Support: integer arithmetic, text literals, `print-line`
- Target: `samples/hello.codex` compiles to `.wasm` and runs under Wasmtime
- Deliverable: 13th backend in `codex build --targets wasm`

### Phase 2: Core Language

- Records, sum types, pattern matching
- Let-bindings, if-then-else, closures
- All 22 builtins
- Target: all integration test samples pass

### Phase 3: Effects & WASI

- WASI Preview 2 imports for Console, FileSystem, Time
- Effect row → import section mapping
- Target: `samples/word-freq/` compiles and runs on WASI

### Phase 4: Self-Hosting (Stretch)

- The 26-file self-hosted compiler compiles to a single `.wasm` module
- That module, run under Wasmtime, compiles Codex source correctly
- Fixed point: WASM-compiled compiler produces identical output to the
  C#-compiled compiler

### Phase 5: Native Integration

- Replace CLI bridge with shared library (`libcodex_cranelift`)
- P/Invoke from .NET or direct FFI from self-hosted compiler
- Reduced compilation latency

---

## Open Questions

1. **GC proposal adoption timeline.** When do major runtimes ship WASM GC?
   This determines when we can move from bump allocation to managed references.
   Current status: Chrome and Firefox ship it. Wasmtime support is in progress.

2. **Component model.** Should Codex modules be WASM components (with typed
   interfaces) rather than core modules? The component model would let Codex
   libraries expose typed APIs to other WASM languages. This is the right
   long-term answer but may be premature for Phase 1.

3. **String encoding.** WASM and WASI expect UTF-8. Codex `Text` is UTF-8
   internally. No conversion needed — but we need to decide on string lifetime
   management. Currently: bump-allocated, never freed. Eventually: GC'd.

4. **Debug info.** DWARF in WASM is possible but tooling is immature. Do we
   emit debug info in Phase 1? Probably not — but we should preserve source
   spans through the pipeline so we can add it later.

5. **Cranelift version pinning.** Cranelift's API is not yet 1.0. We pin to a
   specific version and test against it. Upgrades are deliberate and tested.

---

## Relationship to Other Backends

The WASM backend does not replace any existing backend. It adds a new capability:

| Property | Source Backends (C#/JS/Rust/...) | IL Backend | WASM Backend |
|----------|--------------------------------|------------|-------------|
| Output format | Source text | .NET PE binary | `.wasm` binary |
| Requires toolchain | Yes (dotnet/node/rustc) | .NET runtime only | WASM runtime only |
| Sandboxed | No | No | Yes (by default) |
| Portable | Per-language | .NET platforms | Everywhere |
| Optimization | Deferred to target compiler | None (IL is interpreted/JIT'd) | Cranelift optimizes |
| Self-hosting | C# backend (proven) | IL backend (proven) | Stretch goal |

The WASM backend is the first step toward Codex programs that run everywhere
without asking the user to install anything beyond a WASM runtime — and WASM
runtimes are increasingly embedded in everything.