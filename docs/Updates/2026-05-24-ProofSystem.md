# Proof System and Dependent Types -- 2026-05-24

Codex now has dependent types, a static bounds prover, and proof erasure.
Twenty CLs across two days, all gates green, new seed installed.

## What's New

### Dependent Types

Types can carry values. The `===` operator in type position creates a
propositional equality type (`PropEqTy`). The unifier verifies proof
terms structurally -- an invalid proof is a compile-time type error.

```codex
  nil-eq : Nil === Nil
  nil-eq = Refl              -- verified by unifier

  -- bad : Nil === Cons
  -- bad = Refl              -- REJECTED: type error
```

Proof terms with real dependent types:

| Term | Type | Purpose |
|------|------|---------|
| `Refl` | `forall a. a === a` | Reflexivity |
| `sym` | `forall a b. (a === b) -> (b === a)` | Symmetry |
| `trans` | `forall a b c. (a === b) -> (b === c) -> (a === c)` | Transitivity |
| `assume` | `Proof` | Axiom (unverified assertion) |

### Proof Erasure

All proof-typed definitions are erased at emit time -- zero machine code.
The compiler reports each erasure via CDX4020. The `Proof` type name is
first-class: functions can accept and return proofs.

### Claim / Proof / QED Syntax

```codex
  claim id-nil : Nil === Nil
  proof id-nil = Refl
  qed
```

`claim` declares a proposition (parsed as a `PropEqTy` type annotation).
`proof` provides the evidence (compiled, type-checked, then erased).
`qed` marks the end of the proof block. The `induction` keyword is a
synonym for `when` in proof bodies.

### Static Bounds Prover

The compiler statically proves bounded-integer range safety. When a value's
proven range fits within a field's declared bounds, the runtime bounds check
(`cmp`/`jcc`/`ud2`) is elided and CDX4010 is emitted.

The prover tracks ranges through 12 expression patterns:
- Literals, field access, `__narrow` pass-through
- Arithmetic: `+`, `-`, `*`, `/`
- Builtins: `int-mod`, `bit-and`, `bit-shru`
- Control flow: `negate`, `if`/`else` union
- **Let bindings**: ranges carry through `let x = expr in body`

All three bounded-integer store sites now use the prover:
field assignment, `__record-set`, and record construction.

### CodexMagic Game Engine

Twenty-six game modules implementing a Magic: The Gathering-style card
game engine. MagicServer, matchmaking, seasons, simulation runner, web
portal with HTML/CSS/JS frontend.

## Seed

New seed installed. CDX fixed point verified.

| | Value |
|---|---|
| Size | 2,180,885 bytes (+26 KB) |
| SHA-256 | `F763E4A566C4F7B8B6C74EEF0337DEF7519C109A1D205A5745D94397ED0D05E2` |

## CLs

| CL | Description |
|---|---|
| 2073 | Static bounds elision: `value-fits-field` + `emit-narrow-store-proven` |
| 2079 | Recursive range: `ir-expr-proven-range` (add/sub/negate) |
| 2082 | CDX4010 diagnostic + `st-add-info` |
| 2084 | Mul, div, if/else union |
| 2094 | int-mod, bit-and, bit-shru recognition |
| 2105 | bounds-proof.codex test (10 patterns) |
| 2113 | ProofTy, claim/proof parser, induction keyword, proof builtins |
| 2118 | claim/proof at column 2 parsed as code |
| 2122 | Proof erasure in emit |
| 2126 | Proof as first-class type name |
| 2128 | CDX4020 erasure diagnostic, claim annotations |
| 2132 | PropEqTy, Refl carries propositional equality |
| 2140 | === in type position, PropEqTy verified by unifier |
| 2151 | README update |
| 2163 | sym/trans carry PropEqTy types |
| 2172 | Site 3 bounds elision (record construction) |
| 2185 | Claim proposition parsing via PropEqTy |
| 2194 | Fix: PropEqTy in CodexTypeTree walkers |
| 2198 | IrLet range tracking with environment threading |
| 2213 | Seed rebuild |

## What's Next

The proof system is complete for this milestone. The next chapter:
- Pi types: `(x : A) -> B(x)` -- general dependent function types
- Type-level normalizer: evaluate arithmetic in types
- Parametric claims: `claim name (params) : prop`
- CDX proof section: embed verified proofs in the binary
