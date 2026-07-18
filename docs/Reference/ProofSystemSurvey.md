# Proof System Survey — 2026-05-23

Survey of what exists, what's stubbed, and what's needed to implement
the proof system described in the type system design doc
(`docs/Reference/03-TYPE-SYSTEM.md`).

## What the Design Says

The type system doc describes dependent types with proof obligations
(Phase 4 of the type system roadmap). The vision:

```
index : (i : Integer) -> Vector n a -> {proof : i < n} -> a
```

Proofs come from: literal evidence, context (pattern match guards),
explicit proof terms, or automatic search. The language has `claim`,
`proof`, `qed` keywords and a turnstile operator `|-`.

CPL (prose) includes proof assertion sentences:
```
claim: reversing a list twice gives the original.
```

## What Exists in the Compiler

### Lexer (Token.codex, Lexer.codex)
- `ClaimKeyword`, `ProofKeyword`, `QedKeyword` token types — IMPLEMENTED
- Reserved words `claim`, `proof`, `qed`, `forall`, `exists` — IMPLEMENTED
- Turnstile `|-` and unicode `|-` — IMPLEMENTED as operators

### Parser (Parser.codex, ParserCore.codex)
- `PtProofTemplate` recognized in CPL prose parsing — IMPLEMENTED but DEAD
  - Lines starting with `claim:`, `Claim:`, `therefore,`, `it follows that`
    are classified as `PtProofTemplate`
  - The template is then SKIPPED (falls through to `acc` with no action)
- `ClaimKeyword`/`ProofKeyword`/`QedKeyword` treated as definition
  terminators in `is-definition-end` — IMPLEMENTED

### AST (Ast/)
- NO proof nodes. No ClaimDef, ProofDef, TheoremNode, or similar.
- No representation for proof obligations, proof terms, or claims.

### Type System (Types/)
- `ForAllTy` exists — used for polymorphism (generic quantification), NOT
  proof quantification
- NO dependent type support. Types cannot contain values.
- NO type-level computation or normalizer
- NO proof obligation tracking
- NO refinement types
- Bounded integers (`Integer between L and H`) exist but are runtime-checked
  via `__narrow`, not statically verified

### IR (IR/)
- NO proof-related IR nodes
- NO representation for proof terms or obligations

### Code Generator (Emit/)
- CDX header has `proof-off` and `proof-sz` fields — always 0
- CDX flags include `cdx-flag-has-proofs` — never set
- NO proof compilation or verification

## What's Been Implemented (2026-05-23)

### Tier 1: Proof Language Surface — DONE
- **Parser**: `claim` lines skipped (propositions stored as annotations),
  `proof` definitions parsed as real code, `induction` keyword (= `when`)
- **Keywords**: ClaimKeyword, ProofKeyword, QedKeyword, InductionKeyword
- **Builtins**: Refl, sym, trans, cong, assume — in type env and emitter

### Tier 2: Dependent Types Foundation — STARTED
- **PropEqTy**: `PropEqTy (CodexType) (CodexType)` — types carry values
- **`===` in types**: `Nil === Nil` → `PropEqTy (ConstructedTy "Nil") (ConstructedTy "Nil")`
- **Refl verification**: `Refl : forall a. PropEqTy a a` — unifier checks
  both sides are equal; invalid proofs are type errors
- **ProofTy**: general proof type, subtype of PropEqTy
- **Proof type name**: `Proof` resolves to ProofTy in type annotations

### Tier 2: NOT YET DONE
- Pi types (general dependent function types)
- Type-level normalizer
- Totality checker

### Tier 3: Proof Obligation System — PARTIALLY DONE
- **Static bounds prover**: ir-expr-proven-range computes value ranges
  through arithmetic, builtins (int-mod, bit-and, bit-shru), conditionals
- **Bounds elision**: emit-narrow-store-proven skips runtime checks when proven
- **CDX4010**: diagnostic at each elision site
- **CDX4020**: diagnostic at each proof erasure site
- NOT YET: explicit obligation generation/discharge for claim propositions

### Tier 4: Proof Terms and Verification — PARTIALLY DONE
- **Proof erasure**: proof-typed defs skipped during emit (zero machine code)
- **Proof verification**: Refl verified by unifier (PropEqTy structural unification)
- NOT YET: CDX proof embedding, full proof term language

## What Now Exists — Static Bounds Prover (2026-05-23)

The first proof milestone is complete. The compiler now statically proves
bounded-integer range safety and elides runtime checks when proven.

### Implementation (codex/compiler/Emit/X86_64Compound.codex)

**`ir-expr-proven-range`** — recursively computes proven value ranges:

| Expression | Proven range |
|---|---|
| `IrIntLit(n)` | `[n, n]` |
| `IrFieldAccess` with `IntegerTy(lo, hi)` | `[lo, hi]` |
| `IrApply(__narrow, arg)` | recurse on `arg` |
| `IrBinary(IrAddInt, l, r)` | `[l.lo+r.lo, l.hi+r.hi]` (non-neg, overflow guard) |
| `IrBinary(IrSubInt, l, r)` | `[l.lo-r.hi, l.hi-r.lo]` (non-neg operands) |
| `IrBinary(IrMulInt, l, r)` | `[l.lo*r.lo, l.hi*r.hi]` (non-neg, overflow guard) |
| `IrBinary(IrDivInt, l, r)` | `[l.lo/r.hi, l.hi/r.lo]` (non-neg, r.lo>0) |
| `IrNegate(x)` | `[-x.hi, -x.lo]` (i64-min guard) |
| `IrIf(_, then, else)` | union of branch ranges |
| anything else | `int-ty-default` (conservative) |

**`value-fits-field`** — checks `proven-range ⊆ field-range`.

**`emit-narrow-store-proven`** — wraps `emit-narrow-store-checked`. If
proven, emits just the store (no cmp/jcc/ud2) and CDX4010 diagnostic.

### Call sites
- `emit-field-store` (X86_64.codex) — field assignment via `=`
- `emit-record-set-builtin` (X86_64Builtins.codex) — `__record-set`
- `emit-store-record-fields-by-type` (X86_64Compound.codex) — unchanged,
  no IRExpr available at this site

### Diagnostic
CDX4010 `BoundsProven` (info, phase codegen) — emitted at each elision site.

### CLs
| CL | Description |
|---|---|
| 2073 | `value-fits-field` + `emit-narrow-store-proven` |
| 2079 | `ir-expr-proven-range` — add/sub/negate |
| 2082 | CDX4010 diagnostic + `st-add-info` |
| 2084 | mul, div, if/else union |
| 2094 | int-mod, bit-and, bit-shru recognition |
| 2105 | bounds-proof test (10 patterns) |
| 2113 | ProofTy, claim/proof parser, induction keyword, proof builtins |
| 2118 | claim/proof at column 2 parsed as code |
| 2122 | Proof erasure in emit |
| 2126 | Proof as first-class type name |
| 2128 | CDX4020 erasure diagnostic, claim annotations |
| 2132 | PropEqTy, Refl carries propositional equality |
| 2140 | === in type position, PropEqTy verified by unifier |

## Next Steps

### Near-term (extend existing infrastructure)
- Track ranges through `IrLet` bindings
- Extend Site 3 (record construction) with IRExpr in FieldLocal
- Parse claim propositions and match against proof types
- `sym : PropEqTy a b -> PropEqTy b a` (swap inner types)

### Medium-term (deeper dependent types)
- Pi types: `(x : A) -> B(x)` where return type depends on argument
- Type-level normalizer for arithmetic in types
- Totality checker for type-level functions
- CDX proof section: embed verified proofs in binary

### Long-term (full vision from 03-TYPE-SYSTEM.md)
- Universe levels (Type0 : Type1 : ...)
- Implicit arguments (solved by unification)
- Elaborated AST with all types explicit
- Integration with linear types and effects

## External Research (IRISA Harvest, 2026-06-23)

See `docs/Reference/IRISA_Research_Harvest.md` for full context.

### EPICURE — Verified Compilation Preserves Security Properties

The EPICURE team (IRISA D4, evolved from CELTIQUE) proves that
high-level security properties survive compilation to machine code.
Their CompCert-style verified compilation work shows that
information flow control and constant-time guarantees at source
level are preserved through codegen.

**Applicability to punctual verification:** Our `punctual` keyword
enforces five structural restrictions (CDX6001-CDX6005) at the IR
level. EPICURE's approach would verify that the x86-64 codegen
doesn't introduce timing side-channels in the emitted code — e.g.,
data-dependent branches, variable-latency instructions, or memory
access patterns that leak information. This extends punctual from
"bounded instructions" to "bounded and timing-invariant."

**Applicability to proof erasure:** Our CDX4020 erases proof
definitions during emit. EPICURE's framework could prove that
erasure is correct — that the erased proofs cannot affect runtime
behavior, which is the claim we make but do not formally verify.

### PACAP — WCET Analysis for Cycle-Accurate Bounds

The PACAP team (IRISA D3) studies Worst-Case Execution Time
analysis: formal bounds on execution time for real-time systems.

**Applicability to punctual (medium-term):** CDX6010 reports
instruction count per punctual function — a proxy for execution
time. PACAP's WCET techniques could give cycle-accurate bounds on
specific hardware targets. This would make the punctual budget
(CDX6011) not just "instruction count" but "wall-clock microseconds
on target X" — the real promise for hard real-time (Ada/Ravenscar
parity). Requires a hardware model per target: cycle counts for
each instruction, pipeline stall analysis, cache miss modeling.
