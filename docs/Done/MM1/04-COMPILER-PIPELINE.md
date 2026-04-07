# 04 — Compiler Pipeline

## Overview

The Codex compiler is a multi-phase pipeline. Each phase transforms data from one representation to the next. Every phase is a standalone library that can be tested in isolation.

```
Source Text (.codex)
       │
       ▼
┌─────────────┐
│    Lexer     │  Codex.Syntax
│  (Tokenizer) │
└──────┬──────┘
       │  Token Stream
       ▼
┌─────────────┐
│    Parser    │  Codex.Syntax
│              │
└──────┬──────┘
       │  Concrete Syntax Tree (CST)
       ▼
┌─────────────┐
│  Desugarer   │  Codex.Ast
│              │
└──────┬──────┘
       │  Abstract Syntax Tree (AST)
       ▼
┌──────────────┐
│Name Resolver │  Codex.Semantics
│Scope Analyzer│
└──────┬───────┘
       │  Resolved AST (names bound to definitions)
       ▼
┌─────────────┐
│ Type Checker │  Codex.Types
│ + Elaborator │
└──────┬──────┘
       │  Elaborated AST (all types explicit, proofs checked)
       ▼
┌─────────────┐
│   Lowering   │  Codex.IR
│  AST → IR    │
└──────┬──────┘
       │  Intermediate Representation (typed, optimized)
       ▼
┌─────────────┐
│ Optimization │  Codex.IR
│   Passes     │
└──────┬──────┘
       │  Optimized IR
       ▼
┌─────────────┐
│   Emitter    │  Codex.Emit.*
│  (per target)│
└──────┬──────┘
       │  Target code (C#, Rust, JS, Python, WASM, LLVM IR)
       ▼
    Output
```

Each phase also feeds into the **Diagnostic System** — errors, warnings, and suggestions accumulate and are reported at the end (or incrementally in IDE mode).

---

## Phase 1: Lexer

### Input
Raw UTF-8 source text.

### Output
A flat list of `Token` values, each with:
- `Kind` : `TokenKind` (75+ variants: identifiers, keywords, operators, literals, structural tokens)
- `Value` : the text of the token
- `Span` : `SourceSpan` (start/end position, file path)

### Key Design Decisions

**Hand-written lexer**, not a generator (Flex, ANTLR, etc.). Reasons:
1. The prose/notation mode switching requires context-sensitive tokenization
2. Error recovery in the lexer is critical for IDE support — a generated lexer gives up too easily
3. The Codex-in-Codex lexer is also hand-written (functional, character-by-character)

**Prose-aware tokenization**: The `ProseParser` in `Codex.Syntax` handles mode switching:
- `ChapterHeader` / `SectionHeader` tokens for prose structure
- `ProseText` tokens for prose content
- Notation blocks (indented ≥ 4 spaces) are tokenized as formal notation

**No trivia tracking**: The current lexer does not preserve whitespace or comments as trivia. Tokens carry spans but not leading/trailing trivia. Round-trip fidelity relies on the source text, not the token stream.

> **Original design** envisioned explicit `LeadingTrivia`/`TrailingTrivia` on tokens, a mode stack (`ProseMode`, `NotationMode`, `StringMode`, `InterpolationMode`), and `Indent`/`Dedent` tokens. The implementation uses a simpler approach: `Newline` tokens for line breaks, space-skipping in the lexer, and indentation handled by the prose parser. The simpler design proved sufficient for the bootstrap.

### Implementation

```csharp
// Actual API — Codex.Syntax/Lexer.cs
public sealed class Lexer(SourceText source)
{
    public IReadOnlyList<Token> Tokenize();
}
```

The lexer produces the complete token list eagerly (not pull-based). The parser indexes into it by position.

---

## Phase 2: Parser

### Input
Token stream from the lexer.

### Output
Concrete Syntax Tree (CST) — a lossless representation of the source. The CST preserves:
- All whitespace and formatting
- All prose text
- All trivia
- Parentheses, even when redundant
- The exact token sequence

### CST vs. AST

The CST is the parser's output. It is a tree of `SyntaxNode` values where every node contains its child tokens and nodes. It can reconstruct the source text exactly.

The AST is a simplified tree used for semantic analysis. It discards trivia and normalizes syntax.

We maintain both because:
- The CST is needed for the IDE (formatting, refactoring, syntax highlighting)
- The AST is needed for the compiler (type checking, code generation)

### Parser Design

**Recursive descent** with **Pratt parsing** for expressions.

Top-level structure:
```
parseDocument()
  → parseChapter()*

parseChapter()
  → expect("Chapter" header)
  → parseProse()
  → parseSection()*

parseSection()
  → expect("Section" header)
  → parseProse()
  → parseSectionItem()*

parseSectionItem()
  → parseDefinition()
  | parseClaim()
  | parseProof()
  | parseProseBlock()

parseDefinition()
  → parseProse()        -- the introductory explanation
  → parseSignature()    -- the type signature
  → parseBody()         -- the implementation
```

Expression parsing uses Pratt parsing with precedence levels:

| Precedence | Operators |
|-----------|-----------|
| 1 (lowest) | `→`, `←` |
| 2 | `\|\|` |
| 3 | `&&` |
| 4 | `≡`, `≠`, `<`, `>`, `≤`, `≥` |
| 5 | `++` |
| 6 | `+`, `-` |
| 7 | `*`, `/` |
| 8 | `^` |
| 9 | unary `-`, `not` |
| 10 (highest) | function application (juxtaposition) |

### Error Recovery

The parser must be **error-tolerant**. A syntax error in one definition should not prevent parsing the rest of the document. Recovery strategies:

1. **Synchronization tokens**: on error, skip tokens until we find a chapter header, section header, or definition header at the expected indentation level
2. **Error nodes**: insert `ErrorNode` in the CST containing the skipped tokens
3. **Partial parse**: if a definition's signature parses but its body doesn't, keep the signature

This is essential for IDE support — the user is always in the middle of typing, and the parser must produce useful output from incomplete/incorrect input.

---

## Phase 3: Desugaring (CST → AST)

### Input
CST from the parser.

### Output
AST — a clean, normalized representation with:
- Prose blocks preserved but separated from notation
- Syntactic sugar expanded
- All structure explicit

### Desugaring Rules

| Sugar | Expansion |
|-------|-----------|
| `if a then b else c` | `when a if True → b if False → c` |
| `a \|\| b` | `when a if True → True if False → b` |
| `a && b` | `when a if False → False if True → b` |
| `[1, 2, 3]` | `Cons 1 (Cons 2 (Cons 3 Nil))` |
| `do { x ← e; body }` | `bind e (λx → body)` |
| `f \|> g` | `g (f)` (pipe) |
| Record update `r with { f = v }` | Constructor call with shared fields |

### Prose Template Matching

During desugaring, recognized prose templates are matched and converted to AST nodes:

```
"An Account is a record containing:" 
  → RecordTypeHeader { name = "Account" }

"such that balance equals the sum of all amounts in history"
  → DependentConstraint { ... }
```

Unrecognized prose becomes `ProseBlock` nodes in the AST — preserved for documentation, but not semantically analyzed.

---

## Phase 4: Name Resolution & Scope Analysis

### Input
Raw AST.

### Output
Resolved AST where every identifier is bound to its declaration.

### Process

1. **Collect declarations**: walk the AST and record all top-level definitions (types, functions, claims, proofs) in a symbol table
2. **Build scope tree**: nested scopes for chapters, sections, let-bindings, lambda parameters, pattern variables
3. **Resolve references**: walk every expression and resolve identifiers to their declarations
4. **Detect errors**: undefined names, ambiguous names, shadowing warnings

### Module Resolution

When an `import` is encountered:
1. Look up the chapter by name in the current document
2. If not found, look in the repository (content-addressed lookup)
3. Bind the imported names into the current scope

### Order Independence

Definitions within a chapter can appear in any order (the reader's order, not the dependency order). The name resolver builds a dependency graph and topologically sorts it. Mutually recursive definitions are grouped into strongly connected components.

---

## Phase 5: Type Checking & Elaboration

### Input
Resolved AST.

### Output
Elaborated AST where:
- All types are explicit (no unresolved type variables)
- All implicit arguments are filled in
- All proof obligations are resolved or recorded as holes
- All linearity annotations are computed
- All effect rows are resolved

### Process

This is the big one. See `03-TYPE-SYSTEM.md` for the type system design. The implementation follows bidirectional type checking:

```
check(expr, expectedType) → ElaboratedExpr
infer(expr) → (ElaboratedExpr, Type)
```

Key sub-passes:
1. **Type inference**: bidirectional checking with unification
2. **Constraint solving**: unify type variables, solve effect rows
3. **Linearity checking**: verify usage counts for linear bindings
4. **Effect checking**: verify effect rows are consistent
5. **Proof checking**: verify proof terms for dependent type constraints
6. **Elaboration**: insert implicit arguments, coercions, proof witnesses

### Incremental Type Checking

For IDE support, the type checker must be **incremental**. When a single definition changes:
1. Re-check only that definition and its dependents
2. Reuse cached results for everything else

This requires tracking dependencies between definitions at the type-checking level.

---

## Phase 6: Lowering (AST → IR)

### Input
Type-checked AST with resolved names and inferred types.

### Output
`IRModule` — a typed intermediate representation suitable for code generation.

### IR Design

The IR is **not** in A-normal form. Expressions nest freely:

```
// AST: f (g x) + h y
// IR: IRBinary(AddInt, IRApply(f, IRApply(g, x)), IRApply(h, y))
```

This simplifies the lowering pass and makes each backend responsible for its
own linearization needs. See `07-TRANSPILATION.md` for the full IR node catalog.

### Lowering Rules

| AST Node | IR Node |
|----------|---------|
| Integer literal | `IRIntegerLit` |
| Text literal | `IRTextLit` |
| Boolean `True`/`False` | `IRBoolLit` |
| Variable reference | `IRName` |
| Binary operator | `IRBinary` (with typed op: `AddInt` vs `AddNum` etc.) |
| Function application | `IRApply` (curried: one argument per apply) |
| If-then-else | `IRIf` |
| Let-in | `IRLet` |
| Pattern match | `IRMatch` with `IRPattern` branches |
| Do-notation | `IRDo` with `IRDoBind` / `IRDoExec` statements |
| Lambda | `IRLambda` |
| Record construction | `IRRecord` |
| Field access | `IRFieldAccess` |
| List literal | `IRList` |
| Constructor application | `IRApply` (constructors are functions) |

The lowering pass also resolves binary operators to typed variants (e.g.,
`+` on integers becomes `IRBinaryOp.AddInt`, on numbers becomes `AddNum`,
on text becomes `AppendText`).

---

## Phase 7: Optimization

**Status**: Not yet implemented. The IR is emitted directly without optimization passes.

Planned passes (to be implemented as needed):
- Dead code elimination
- Constant folding
- Inlining of small functions
- Tail call optimization
- Monomorphization (for backends that need it)

---

## Phase 8: Code Emission

### Input
IR module (unoptimized).

### Output
Target language source code.

### Emitter Interface

```csharp
public interface ICodeEmitter
{
    string TargetName { get; }
    string FileExtension { get; }
    string Emit(IRModule module);
}
```

### Implemented Backends

| Backend | Project | Extension | Status |
|---------|---------|-----------|--------|
| **C#** | `Codex.Emit.CSharp` | `.cs` | ✅ Primary — used for bootstrap |
| **JavaScript** | `Codex.Emit.JavaScript` | `.js` | ✅ All samples run under Node.js |
| **Rust** | `Codex.Emit.Rust` | `.rs` | ✅ All samples produce valid Rust |

Each backend handles the full IR: literals, binary ops, if/let/match/do/lambda/apply,
records, field access, lists, sum types, and 20+ built-in functions.

See `07-TRANSPILATION.md` for backend encoding strategies and the built-in function table.
