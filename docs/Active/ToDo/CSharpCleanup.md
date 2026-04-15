# C# Style Cleanup Audit

**Date:** 2026-03-25
**Branch:** master
**Auditor:** Agent Windows

Rules reference: `.github/copilot-instructions.md` → **C# Code Style** section.

---

## Legend

| Priority | Meaning |
|----------|---------|
| **P1** | Clear rule violation, mechanical fix |
| **P2** | Style inconsistency, easy fix |
| **P3** | Low priority / spec-standard naming (debatable) |

---

## 1. Public static fields using `s_` prefix (P1)

**Rule:** `s_` prefix is for **private** static fields. Public static fields should use PascalCase.

| File | Line | Current | Should Be |
|------|------|---------|-----------|
| `src/Codex.Core/SourceText.cs` | 12 | `public static readonly SourceSpan s_synthetic` | `Synthetic` |
| `src/Codex.Core/Set.cs` | 16 | `public static readonly Set<T> s_empty` | `Empty` |
| `src/Codex.Core/Map.cs` | 18 | `public static readonly Map<TKey, TValue> s_empty` | `Empty` |
| `src/Codex.Core/Map.cs` | 65 | `public static readonly ValueMap<TKey, TValue> s_empty` | `Empty` |
| `src/Codex.Types/CodexType.cs` | 10 | `public static readonly IntegerType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 16 | `public static readonly NumberType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 22 | `public static readonly TextType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 28 | `public static readonly BooleanType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 34 | `public static readonly NothingType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 40 | `public static readonly VoidType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 114 | `public static readonly ErrorType s_instance` | `Instance` |
| `src/Codex.Types/CodexType.cs` | 210 | `public static readonly ReflProof s_instance` | `Instance` |

> **Impact:** These are widely referenced (e.g. `IntegerType.s_instance`, `Set<T>.s_empty`).
> Renaming will touch many files. Recommend doing as a single mechanical rename pass per symbol.

---

## 2. Private static fields missing `s_` prefix (P1)

**Rule:** Private static fields use `s_` prefix.

| File | Line | Current | Should Be |
|------|------|---------|-----------|
| `src/Codex.Emit.Arm64/Arm64CodeGen.cs` | 43 | `static readonly uint[] CalleeSaved` | `s_calleeSaved` |
| `src/Codex.Emit.Arm64/ElfWriterArm64.cs` | 20 | `static readonly byte[] ShstrtabData` | `s_shstrtabData` |
| `src/Codex.Emit.RiscV/RiscVCodeGen.cs` | 46 | `static readonly uint[] CalleeSaved` | `s_calleeSaved` |
| `src/Codex.Emit.X86_64/X86_64CodeGen.cs` | 28 | `static readonly byte[] TempRegs` | `s_tempRegs` |
| `src/Codex.Emit.X86_64/X86_64CodeGen.cs` | 29 | `static readonly byte[] LocalRegs` | `s_localRegs` |
| `tests/Codex.AgentToolkit.Tests/McpToolNameTests.cs` | 8 | `static readonly Regex MakeToolPattern` | `s_makeToolPattern` |
| `tests/Codex.AgentToolkit.Tests/McpToolNameTests.cs` | 12 | `static readonly Regex ValidToolName` | `s_validToolName` |

---

## 3. XML doc comments (`///`) (P1)

**Rule:** No XML doc comments. Code should be self-documenting.

| File | Lines | Count |
|------|-------|-------|
| `src/Codex.Emit.X86_64/ElfWriter32.cs` | 3–7 | 5 |
| `src/Codex.Emit.X86_64/ElfWriterX86_64.cs` | 91–94 | 4 |
| `src/Codex.Repository/FactStore.Network.cs` | 8–10, 13–15, 22–24, 31–33, 39–41, 44–47, 77–78+ | 27 |
| `src/Codex.Repository/FactStore.Proposals.cs` | (multiple) | 23 |
| `src/Codex.Repository/FactStore.Trust.cs` | 5–7, 20–23, 132–134 | 10 |
| `tools/Codex.Cli/PackageResolver.cs` | 11–14, 59–64, 96–99, 124–127, 158–161, 177–183 | 29 |
| `tests/Codex.Types.Tests/LinuxNativeTests.cs` | 8–12, 270–272 | 8 |

---

## 4. `var` usage (P1)

**Rule:** No `var`. Always use explicit types.

| File | Line | Code |
|------|------|------|
| `tools/Codex.Bootstrap/Program.cs` | 13 | `var thread = new Thread(...)` |
| `tools/Codex.Bootstrap/Program.cs` | 67 | `var tokens = Codex_Codex_Codex.tokenize(combined);` |
| `tools/Codex.Bootstrap/Program.cs` | 70 | `var st = Codex_Codex_Codex.make_parse_state(tokens);` |
| `tools/Codex.Bootstrap/Program.cs` | 71 | `var doc = Codex_Codex_Codex.parse_document(st);` |
| `tools/Codex.Bootstrap/Program.cs` | 75 | `var ast = Codex_Codex_Codex.desugar_document(doc, "Codex_Codex");` |
| `tools/Codex.Bootstrap/Program.cs` | 79 | `var checkResult = Codex_Codex_Codex.check_module(ast);` |
| `tools/Codex.Bootstrap/Program.cs` | 84 | `var diagLines = new List<string>();` |
| `tools/Codex.Bootstrap/Program.cs` | 89 | `var tb = checkResult.types[i];` |
| `tools/Codex.Bootstrap/Program.cs` | 90 | `var resolved = Codex_Codex_Codex.deep_resolve(...)` |
| `tools/Codex.Bootstrap/Program.cs` | 106 | `var errLines = new List<string>();` |
| `tools/Codex.Bootstrap/Program.cs` | 118 | `var tb = checkResult.types[i];` |
| `tools/Codex.Bootstrap/Program.cs` | 119 | `var resolved = Codex_Codex_Codex.deep_resolve(...)` |
| `tools/Codex.Bootstrap/Program.cs` | 123 | `var ir = Codex_Codex_Codex.lower_module(...)` |
| `tools/Codex.Bootstrap/Program.cs` | 129 | `var d = ir.defs[j];` |
| `tools/Codex.Bootstrap/Program.cs` | 130 | `var paramStr = string.Join(...)` |

---

## 5. Explicit `private` modifier on members (P1)

**Rule:** Omit default modifiers. `private` on members is default.

| File | Line | Code |
|------|------|------|
| `tests/Codex.Ast.Tests/DesugarerTests.cs` | 10 | `private static Module ParseAndDesugar(...)` |
| `tests/Codex.Semantics.Tests/NameResolverTests.cs` | 11 | `private static (ResolvedModule, DiagnosticBag) ResolveSource(...)` |
| `tests/Codex.Syntax.Tests/LexerTests.cs` | 9 | `private static IReadOnlyList<Token> Tokenize(...)` |
| `tests/Codex.Syntax.Tests/LexerTests.cs` | 17 | `private static IReadOnlyList<Token> NonTrivialTokens(...)` |
| `tests/Codex.Syntax.Tests/ParserTests.cs` | 9 | `private static DocumentNode Parse(...)` |
| `tests/Codex.Syntax.Tests/ParserTests.cs` | 19 | `private static (...) ParseWithDiags(...)` |
| `tests/Codex.Syntax.Tests/ProseParserTests.cs` | 9 | `private static DocumentNode ParseProse(...)` |
| `tests/Codex.Syntax.Tests/ProseParserTests.cs` | 17 | `private static (...) ParseProseWithDiags(...)` |
| `tests/Codex.Types.Tests/LinuxNativeTests.cs` | 15 | `private readonly ITestOutputHelper m_output;` |
| `tests/Codex.Types.Tests/TypeCheckerTests.cs` | 11 | `private static (...) Check(...)` |

---

## 6. Redundant implicit usings (P1)

**Rule:** `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Threading` are implicit. Don't add them.

| File | Lines | Usings |
|------|-------|--------|
| `tools/Codex.Bootstrap/Program.cs` | 1–6 | `using System;` `using System.Collections.Generic;` `using System.IO;` `using System.Linq;` `using System.Threading;` |

---

## 7. Fully-qualified types instead of `using` + short name (P2)

**Rule:** Prefer a `using` directive over inline fully-qualified names.

| File | Line | Fully-Qualified Type |
|------|------|---------------------|
| `src/Codex.Syntax/Lexer.cs` | 182 | `System.Text.StringBuilder` |
| `src/Codex.Syntax/Lexer.cs` | 297 | `System.Text.StringBuilder` |
| `src/Codex.Syntax/Lexer.cs` | 372 | `System.Globalization.CultureInfo.InvariantCulture` |
| `src/Codex.Syntax/Lexer.cs` | 379 | `System.Globalization.CultureInfo.InvariantCulture` |
| `src/Codex.Core/Diagnostics.cs` | 69 | `System.Collections.Immutable.ImmutableArray.Create(...)` |
| `src/Codex.Repository/FactStore.Views.cs` | 335, 350, 400, 419 | `System.Text.Json.JsonSerializer` |
| `tools/Codex.Cli/Program.Repl.cs` | 358 | `System.Text.StringBuilder` |
| `tools/Codex.Cli/Program.Incremental.cs` | 177 | `System.Text.Encoding.UTF8` |
| `tools/Codex.Cli/Program.Project.cs` | 55 | `System.Text.Encodings.Web.JavaScriptEncoder` |
| `tools/Codex.Cli/Program.Run.cs` | 38, 65 | `System.Diagnostics.Process` |
| `tools/Codex.Cli/Program.Repl.cs` | 283, 310 | `System.Diagnostics.Process` |
| `tests/Codex.Core.Tests/CoreTests.cs` | 151 | `System.Collections.Immutable.ImmutableArray<Diagnostic>` |
| `tests/Codex.Repository.Tests/ViewTests.cs` | 1079, 1095, 1118, 1139 | `System.Collections.Immutable.ImmutableArray` |

---

## 8. Constants not PascalCase — EXEMPTED

**Status:** Blanket exemption granted (2026-03-25).

ELF, ISA, and Wasm spec-standard constant names (`ELFCLASS64`, `EM_RISCV`, `PT_LOAD`,
`OpI32Load`, `WasmI32`, etc.) in native-emit code are exempt from the PascalCase rule.
These names match the specs they implement and aid readability for domain experts.

No action required.

---

## Summary by Priority

| Priority | Count | Description |
|----------|-------|-------------|
| **P1** | ~70 items | `s_` on public statics, `var`, `///`, `private`, redundant usings |
| **P2** | ~20 items | Fully-qualified types |
| **P3** | ~0 items | ISA/spec constant naming — exempted |

### Suggested Fix Order

1. **Section 1** (public `s_` → PascalCase) — high impact, many callers; do one symbol at a time with rename refactoring
2. **Section 4** (`var` → explicit types in Bootstrap) — isolated to one file
3. **Section 5** (drop `private`) — mechanical, test files only
4. **Section 6** (redundant usings) — one file, trivial
5. **Section 3** (remove `///` comments) — spread across 7 files
6. **Section 2** (private static `s_` prefix) — 7 fields across 4 files
7. **Section 7** (FQ types → using) — many files, add usings + shorten
