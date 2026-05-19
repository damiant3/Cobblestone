# 07 — Transpilation & Code Generation

## Overview

Codex compiles to a semantic intermediate representation (IR) and then lowers to target languages. The targets are not equal — some can represent the full Codex type system, others can only represent a subset. The emission framework manages this gracefully.

This document defines the IR, the emission framework, and the design of each backend.

---

## The Codex IR

### Design Principles

1. **Typed**: every IR node carries its `CodexType`
2. **Curried**: function application is single-argument (`IRApply` takes one arg)
3. **Explicit closures**: `IRLambda` captures are implicit (closed-over by .NET runtime)
4. **Target-agnostic**: no target-specific constructs in the IR
5. **Minimal**: no optimization passes yet — the IR is a direct lowering of the AST

### Actual IR Node Types (as implemented in `Codex.IR/IRModule.cs`)

```
IRModule
├── Name           : QualifiedName
├── Definitions    : IRDefinition[]
└── TypeDefinitions: Map<string, CodexType>

IRDefinition
├── Name       : string
├── Parameters : IRParameter[]   (name + type)
├── Type       : CodexType       (full function type)
└── Body       : IRExpr

IRExpr =
  | IRIntegerLit  (Value : long)
  | IRNumberLit   (Value : decimal)
  | IRTextLit     (Value : string)
  | IRBoolLit     (Value : bool)
  | IRName        (Name : string, Type)
  | IRBinary      (Op : IRBinaryOp, Left : IRExpr, Right : IRExpr, Type)
  | IRNegate      (Operand : IRExpr)
  | IRIf          (Condition : IRExpr, Then : IRExpr, Else : IRExpr, Type)
  | IRLet         (Name, NameType, Value : IRExpr, Body : IRExpr)
  | IRApply       (Function : IRExpr, Argument : IRExpr, Type)
  | IRLambda      (Parameters : IRParameter[], Body : IRExpr, Type)
  | IRList        (Elements : IRExpr[], ElementType)
  | IRMatch       (Scrutinee : IRExpr, Branches : IRMatchBranch[], Type)
  | IRDo          (Statements : IRDoStatement[], Type)
  | IRRecord      (TypeName, Fields : (FieldName, Value)[], Type)
  | IRFieldAccess (Record : IRExpr, FieldName, Type)
  | IRError       (Message, Type)

IRPattern =
  | IRVarPattern     (Name, Type)
  | IRLiteralPattern (Value, Type)
  | IRCtorPattern    (Name, SubPatterns : IRPattern[], Type)
  | IRWildcardPattern

IRDoStatement =
  | IRDoBind  (Name, NameType, Value : IRExpr)
  | IRDoExec  (Expression : IRExpr)

IRBinaryOp =
  AddInt | SubInt | MulInt | DivInt | PowInt
  AddNum | SubNum | MulNum | DivNum
  Eq | NotEq | Lt | Gt | LtEq | GtEq
  And | Or | AppendText | AppendList | ConsList
```

Notable differences from the original spec:
- **No A-Normal Form**: expressions nest freely (e.g., `IRApply(IRApply(f, x), y)`)
- **No `IRLetRec`**: mutual recursion not yet supported in IR
- **No `IREffectOp` / `IRHandle`**: effects use direct I/O, not algebraic effect handlers
- **No `IRRecordWith`**: record update syntax not yet lowered
- **Curried application**: `IRApply` takes a single argument; multi-arg calls are nested applies
- **Type definitions stored separately**: `TypeDefinitions` map on `IRModule`, not IR nodes

The IR carries full `CodexType` information on every node. Each backend decides
what to keep: C# uses the types for parameter and return type annotations, JS
erases everything to dynamic, Rust emits full type signatures.

---

## Emission Framework

### The Emitter Interface

The actual interface is minimal by design:

```csharp
public interface ICodeEmitter
{
    string TargetName { get; }
    string FileExtension { get; }
    string Emit(IRModule module);
}
```

Three backends implement this: `CSharpEmitter` (.cs), `JavaScriptEmitter` (.js),
and `RustEmitter` (.rs). The CLI selects a backend with `--target cs|js|rust`.

The original design envisioned a `TargetCapabilities` flags enum and a
`ValidateCapabilities` method. In practice, each emitter handles unsupported
features by emitting runtime checks or `Box<dyn Any>` / `object` fallbacks
rather than refusing to emit. This keeps all backends usable for all programs.

### Capability Degradation Strategy

When a backend cannot represent a Codex feature, the emitter has three options (in order of preference):

1. **Encode**: represent the feature using the target's available features (e.g., sum types as class hierarchies in C#)
2. **Insert runtime check**: replace the compile-time guarantee with a runtime assertion (e.g., dependent type constraints become `Debug.Assert`)
3. **Reject**: refuse to emit with a clear explanation ("Cannot emit this module to Python because it uses linear types, which Python cannot enforce")

The user is always informed. Safety is never silently lost.

---

## Backend Designs

### C# Backend (Bootstrap Target) — `Codex.Emit.CSharp`

**Status**: ✅ Complete. Primary backend. Used for self-hosting bootstrap.

| Codex Feature | C# Encoding |
|--------------|-------------|
| `Text` | `string` |
| `Number` | `double` |
| `Integer` | `long` |
| `Boolean` | `bool` |
| `Nothing` | `void` |
| `List a` | `List<T>` |
| Sum types | `abstract record` + `sealed record` subtypes |
| Record types | C# `record` with positional parameters |
| Pattern matching | C# `switch` expressions with type patterns |
| Pure functions | Static methods |
| Effectful functions | Direct I/O (Console, File) |
| Linear types | Runtime checks |
| Dependent types | Runtime assertions |

### JavaScript Backend — `Codex.Emit.JavaScript`

**Status**: ✅ Complete. All samples compile and execute under Node.js.

| Codex Feature | JS Encoding |
|--------------|-------------|
| `Text` | `string` |
| `Number` | `number` |
| `Integer` | `BigInt` (`42n`) |
| `Boolean` | `boolean` |
| `Nothing` | `undefined` |
| `List a` | `Array` |
| Sum types | `Object.freeze({ tag: "Name", field0: ..., field1: ... })` |
| Record types | `Object.freeze({ fieldName: value, ... })` |
| Pattern matching | Nested ternary with `_s.tag === "Name"` checks |
| Pure functions | `function` declarations |
| Effectful functions | Direct I/O (`console.log`, `require('readline-sync')`, `require('fs')`) |
| Linear types | Not enforced |
| Dependent types | Not enforced |

### Rust Backend — `Codex.Emit.Rust`

**Status**: ✅ Complete. All samples compile to valid Rust with typed signatures.

| Codex Feature | Rust Encoding |
|--------------|---------------|
| `Text` | `String` |
| `Number` | `f64` |
| `Integer` | `i64` |
| `Boolean` | `bool` |
| `Nothing` | `()` |
| `List a` | `Vec<T>` |
| Sum types | `enum` with `#[derive(Debug, Clone, PartialEq)]` |
| Record types | `struct` with `#[derive(Debug, Clone, PartialEq)]` |
| Pattern matching | Native `match` with `Enum::Variant(bindings)` |
| Pure functions | `fn` declarations with full type signatures |
| Effectful functions | Direct I/O (`println!`, `stdin().read_line`, `fs::File::open`) |
| Linear types | Ownership semantics (natural fit, but no explicit move analysis yet) |
| Dependent types | Runtime assertions (`panic!`) |
| Polymorphic types | `Box<dyn std::any::Any>` fallback |

### Future Backends (Not Yet Implemented)

| Backend | Strategy |
|---------|----------|
| **Python** | Dataclasses + `match` statement (Python 3.10+). Types as PEP 484 hints. |
| **WASM** | Compile through C/LLVM or emit WASM text format directly. |
| **LLVM IR** | Emit LLVM IR text for native binaries. Full control, maximum performance. |

## Built-in Functions

All three backends implement the same set of built-in functions:

| Built-in | C# | JavaScript | Rust |
|----------|-----|-----------|------|
| `print-line` | `Console.WriteLine` | `console.log` | `println!` |
| `read-line` | `Console.ReadLine()` | `require('readline-sync').question('')` | `stdin().read_line()` |
| `show` | `.ToString()` | `String()` | `format!("{}", x)` |
| `text-length` | `.Length` | `.length` | `.len() as i64` |
| `char-at` | `[i].ToString()` | `[i]` | `.chars().nth(i).to_string()` |
| `substring` | `.Substring(i, n)` | `.substring(i, i+n)` | `.chars().skip(i).take(n).collect()` |
| `text-replace` | `.Replace(a, b)` | `.replaceAll(a, b)` | `.replace(&a, &b)` |
| `text-to-integer` | `long.Parse()` | `parseInt(x, 10)` | `.parse::<i64>().unwrap()` |
| `integer-to-text` | `.ToString()` | `String(x)` | `.to_string()` |
| `is-letter` | `char.IsLetter` | `/^[a-zA-Z]/.test()` | `.is_alphabetic()` |
| `is-digit` | `char.IsDigit` | `/^[0-9]/.test()` | `.is_ascii_digit()` |
| `is-whitespace` | `char.IsWhiteSpace` | `/^\s/.test()` | `.is_whitespace()` |
| `char-code` | `(long)s[0]` | `.charCodeAt(0)` | `.chars().next() as i64` |
| `code-to-char` | `((char)n).ToString()` | `String.fromCharCode(n)` | `char::from_u32(n)` |
| `list-length` | `.Count` | `.length` | `.len() as i64` |
| `list-at` | `[i]` | `[i]` | `[i as usize].clone()` |
| `open-file` | `File.OpenRead` | `require('fs').readFileSync` | `File::open().unwrap()` |
| `read-all` | `StreamReader.ReadToEnd` | `.toString()` | `.read_to_string()` |
| `close-file` | `.Dispose()` | (no-op) | `drop()` |
| `negate` | `-(x)` | `-(x)` | `-(x)` |

---

## Self-Hosting

**Status**: ✅ Structural parity achieved (March 2026).

The Codex compiler is written in Codex (`codex-src/`, ~2,500 lines across 14 files)
and compiles itself through the C# backend:

```
Stage 0: C# compiler (src/) compiles codex-src/ → output.cs (105KB, 264 records, 222 defs)
Stage 1: output.cs compiled with dotnet → Stage 1 binary
Stage 1: compiles codex-src/ → stage1-output.cs (69KB, 264 records, 220 defs)
```

264/264 type definitions, 0 missing functions. Stage 1 uses `object` types
(no Codex-side type checker) which accounts for the size difference.

See [M13-BOOTSTRAP-PLAN.md](M13-BOOTSTRAP-PLAN.md) for details.

---

## Resolved Design Questions

1. **Number representation** — `double` for Number, `long` for Integer. Arbitrary precision deferred.
2. **Effect encoding in C#** — Direct I/O (Console, File). No monadic encoding. Simple and working.
3. **Runtime library** — No separate runtime library. Built-in functions are emitted inline by each backend.
4. **Source maps** — Not yet implemented. Source spans are carried through the pipeline but not emitted.
