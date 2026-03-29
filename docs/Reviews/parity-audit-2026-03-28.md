# Parity Audit: What Got Left Behind

**Auditor**: Agent Linux  
**Date**: 2026-03-28  
**Scope**: Full codebase sweep for changes that didn't propagate to all
touch points. Triggered by the CCE encoding bugs found today.

---

## Critical: Backend CCE Encoding Gaps

The CCE encoding change reordered character bytes by frequency (e=13,
t=14, a=15, ..., E=39, T=40, A=41, ..., Z=64). The C#, x86-64, and
RISC-V backends were updated. Two native backends were NOT:

### ARM64 — ASCII ranges (BROKEN for bare metal)

`src/Codex.Emit.Arm64/Arm64CodeGen.cs` lines 1121-1160:
- `is-letter`: checks `'a'-'z'` and `'A'-'Z'` (ASCII 97-122, 65-90)
- `is-digit`: checks `'0'-'9'` (ASCII 48-57)
- `is-whitespace`: checks `== 32` (ASCII space), `== 9` (tab)
- **Should be**: letters 13-64, digits 3-12, whitespace 0-2 (CCE)

### WASM — ASCII ranges (BROKEN for bare metal)

`src/Codex.Emit.Wasm/WasmModuleBuilder.Builtins.cs` lines 81-130:
- `is-letter`: checks `'a'` and `'A'` ranges (ASCII)
- `is-digit`: checks `'0'` range (ASCII)
- `is-whitespace`: checks `' '`, `'\t'`, `'\n'`, `'\r'` (ASCII)
- **Should be**: CCE ranges matching x86-64 and RISC-V

### IL — Unicode via .NET (VERIFY)

`src/Codex.Emit.IL/ILAssemblyBuilder.cs` uses `Char.IsLetter`,
`Char.IsDigit`, `Char.IsWhiteSpace` — .NET Unicode methods. If the IL
backend only targets .NET (Unicode strings), this is correct. If IL
binaries will ever process CCE-encoded data, these need CCE ranges.

### Transpiler backends (Go, JS, Python, Rust, etc.) — CORRECT

These emit source code for platforms that use Unicode natively. ASCII/
Unicode character classification is correct for their use case.

---

## Moderate: Non-Exhaustive Matches in Self-Hosted Compiler

The reference compiler reports 6 `CDX2020` warnings when checking
`_all-source.codex`. These are missing pattern arms that could cause
runtime crashes on bare metal (no exception handler):

| Function | File | Missing |
|----------|------|---------|
| `emit-type-expr-tp` | CSharpEmitter.codex:62 | AEffectType |
| `resolve-type-expr-for-lower` | LoweringTypes.codex:141 | AEffectType |
| `resolve-expr` | NameResolver.codex:140 | AHandleExpr |
| `resolve-type-expr` | TypeChecker.codex:14 | **Fixed today** |
| `infer-expr` | TypeChecker.codex | AHandleExpr |
| `type-tag` | Unifier.codex:269 | EffectfulTy |

The AEffectType and AHandleExpr gaps mean effect annotations and handle
expressions are not fully propagated through the compiler pipeline. This
doesn't block current self-compile (the compiler doesn't use handle
expressions on itself) but would break if effect-heavy Codex programs
are compiled through the self-hosted path.

---

## Low: Stale Artifacts

### `_all-source.codex` needs regeneration

The concatenated compiler source (tools/_all-source.codex) predates
today's CCE fixes, CRLF fix, do-block boundary fix, text-concat-list
addition, and AEffectType handling. It should be regenerated from the
current 26 source files. Consider adding a script/Makefile target.

### BUG-001 still open

`return null` for string-returning recursive functions. 5 instances
remain in stage1-output.cs. Tracked in docs/BUGS.md.

---

## Summary: What needs fixing

| Priority | Item | Effort |
|----------|------|--------|
| **P0** | ARM64 CCE ranges (is-letter/is-digit/is-whitespace) | ~30 min |
| **P0** | WASM CCE ranges (same) | ~30 min |
| **P1** | Add AEffectType to emit-type-expr-tp | ~5 min |
| **P1** | Add AEffectType to resolve-type-expr-for-lower | ~5 min |
| **P1** | Add AHandleExpr to resolve-expr | ~10 min |
| **P1** | Add AHandleExpr to infer-expr | ~10 min |
| **P1** | Add EffectfulTy to type-tag | ~5 min |
| **P2** | Regenerate _all-source.codex | ~5 min |
| **P2** | Verify IL backend CCE assumptions | ~15 min |
| **P2** | BUG-001: return null in string functions | ~1 hr |

---

*This audit was triggered by the pattern: CCE encoding was implemented
but character classification ranges were only updated in some backends.
The same "forest through the trees" pattern could recur whenever a
cross-cutting change is made. Consider a parity checklist for encoding,
builtins, and type system features across all backends.*
