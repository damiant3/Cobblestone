# Bounds-Prover Reach

Campaign to drive the CDX2051 silent-truncation warning count to zero
by making the compiler smarter, never by weakening the bounds.
Successor to the widening CLs that were ruled the wrong direction
(FabledTreasureMap entry 2, Damian ruling 2026-07-02): the bounds on
these fields are load-bearing twice over -- documentation of the
value's domain and automatic bounds checking at every store. A
CDX2051 indicates a COMPILER FEATURE NOT YET BUILT. Endgame: promote
CDX2051 warning → error at zero count, the promotion CDX9002 got.

## Status

- Slice 1 (constants + expression skeleton): SHIPPED (blu CL 6611 +
  seed 6612, main 6615). Selfhost 66 -> 53; the 13 proven are the
  reg-rax/reg-rdx/sev-error constant family. Test const-narrow-proven.
- Slice 3 (let-local range flow): SHIPPED (blu, 2026-07-03).
  TypeEnv gains a `local-ranges` side table; infer-let-bindings
  records a local's range when its value proves narrower than full
  i64; the lint's name arm consults it. Added `__buf-write-bytes`
  builtin fact. Selfhost 48 -> 45 (the two `field = new-len` where
  `new-len = __buf-write-bytes` + one bonus local flow). Test
  local-narrow-proven.

  After slice 3 the lint-side low-hanging fruit is essentially
  exhausted. The remaining ~45 are dominated by PARAMETER
  pass-through (~24: `code = code`, `reg = l-reg`, `severity = sev`,
  ...) where a field bounded lo..hi is assigned a parameter whose
  real domain fits but whose TYPE is plain Integer because bounded
  ints do not parse in signatures. These cannot be cleared lint-side;
  they need bounded integers in function signatures (a parser
  feature) or interprocedural argument-range join. Field
  source-bounding (the old slice-4 sketch) is BLOCKED on this: e.g.
  bounding `EmitResult.reg` to 0..31 would clear the two downstream
  `ptr-loc = ptr-loc.reg` / `slot = loc.reg` reads, but the field's
  own `reg = <param>` stores would then trip CDX2050/2051 because the
  param is unbounded - so the field cannot be narrowed until the
  params are bounded first. The remaining long tail also includes
  list-element bounds (`arity = list-length (d.params)` needs
  list-length proven <= 255, which it structurally is not) and
  bounded arithmetic. Recommendation: the next real step is the
  bounded-ints-in-signatures parser feature (clears the ~24 param
  bucket and is a language capability with independent
  safety/IoT value), decided with Damian - NOT more lint slices.

- Slice 2 (builtin return ranges): SHIPPED (blu, 2026-07-02).
  list-length and __deck-pos facts + __narrow passthrough in the
  apply arm, guarded by env-is-local against shadowed heads. Selfhost
  53 -> 48 (the four `code-len/data-len = list-length ...` sites +
  `deck-origin = __deck-pos`). The `new-len = __buf-write-bytes`
  code-len sites are let-LOCALS at the store -- they move to the
  local-flows slice, where a __buf-write-bytes fact becomes
  reachable. Test builtin-narrow-proven pins both directions
  (list-length proven into 0..2^32-1, still warning into 0..255).

## Baseline classification (2026-07-02, 66 warnings)

Measured by compiling the selfhost concat and mapping each warning
back to its source expression. Flow families, largest first:

| Family | Count | Example | Feature needed |
|--------|------:|---------|----------------|
| Function parameter stores | ~30 | `code = code` in mk-cdx / make-diagnostic; `reg = reg` in alloc-temp | Bounded params in signatures (parser gap) or interprocedural ranges |
| Constant name refs | 13 | `reg = reg-rax`, `severity = sev-error` | **Slice 1** |
| Builtin/function returns | ~9 | `code-len = list-length (tramp.bytes)`, `new-len = __buf-write-bytes ...` | Return-range knowledge for builtins (slice 2) |
| Field reads (genuinely wide) | 2 | `ptr-loc = ptr-loc.reg` (0..2^32 into 0..65535) | Source bounding (reg is 0..15 in reality) |
| Guarded locals | 2 | `if looked >= 0 then TypeVar looked` | Guard refinement (slice 3) |
| Constant arithmetic | 3 | `handler-table-base-addr + i * 8` | Arithmetic ranges (needs params too) |
| Intrinsics | 4 | `__deck-pos` into 0..2^32 | Intrinsic return ranges |
| Let-local flows | ~3 | `reg = l-reg`, `max-level = new-max` | Local range tracking |

## Architecture ruling: lint-side analysis, not type refinement

The obvious design -- register a literal-defined constant with type
`IntegerTy n n` so its range rides the inferred type -- is UNSOUND
against the unifier: `infer-arithmetic` and `infer-comparison` unify
the two operand types, and list literals unify every element with the
first (`unify-list-elems`). IntegerTy unification is
overlap-permissive but DISJOINT-REJECTING (`unify-structural`), so
`cdx-a == cdx-b`, `cdx-a + cdx-b`, and `[reg-rax, reg-rcx]` would all
become CDX2001 errors the moment two distinct singleton ranges meet.

Instead the range knowledge lives beside the types, consulted only by
the narrowing lint at the moment it would warn -- the same shape as
the emit-side prover (`ir-expr-proven-range` in X86_64Compound, which
walks IR structure at narrow-store time and elides the runtime check
with CDX4010). This checker-side twin walks the AST value expression
at lint time and elides the WARNING. The two provers stay independent:
the checker one silences false alarms; the emit one removes runtime
checks. Nothing about unification, inferred types, codegen, or the
runtime check changes.

`aexpr-proven-range : TypeEnv, AExpr -> (Integer, Integer)` returns
the value expression's proven range, (i64-min, i64-max) when unknown.
Slice 1 shapes:

- `ALitExpr IntLit` → [n, n] (via lit-text-to-integer -- hex and
  underscore grouping included)
- `ANameExpr` → const-range table lookup, guarded by env-is-local so
  a local shadowing a constant never falsely proves; name resolved
  with the same rename-lookup infer-name uses
- `AIfExpr` → union of the branch ranges
- `ALetExpr` → range of the body
- otherwise → unknown

The constant table is `TypeEnv.const-ranges : List ConstRange`
(name, lo, hi), filled during `register-all-defs`: a def with zero
params whose body is an integer literal records [n, n] under its
resolved name. The registered TYPE is untouched. The table is
complete before check-all-defs runs, so ordering cannot miss a
cross-chapter constant.

A store proven by the lint reports info CDX2053 (NarrowingProven) --
the checker-side analog of CDX4010 -- which makes elision assertable
in .diag tests and visible in build logs.

## Cost

Registration adds one O(1) body-shape test per def and one small
record per integer constant (tens of KB on the CHECK deck). The
analysis runs only on the would-warn path (66 sites on the selfhost
today), with a linear scan of the constant table per name lookup --
sub-millisecond per compile. No new allocation on the silent path.

## Slice plan

1. **Constants + expression skeleton** (this slice): table +
   aexpr-proven-range with literal/name/if/let arms. Expected
   66 → 53 on the selfhost (the reg-rax/reg-rdx/sev-error family).
2. **Return ranges**: teach the analysis the ranges of builtins
   whose results are structurally bounded (list-length,
   __buf-write-bytes, __deck-pos → 0..2^32-1 style facts), as
   AApplyExpr head cases -- again lint-side only, no TypeEnv
   signature changes.
3. **Guard refinement**: `if x >= 0 then ... x ...` narrows x's
   range in the branch. Needs a small refinement environment
   threaded through the analysis; pairs with function-return
   ranges for the `find-param-entry` pattern.
4. **Source bounding where the domain is real** (per ruling): reg
   0..15 from the allocator rotation, EmitResult.reg et al. Record
   fields only -- bounded ints do not parse in function signatures
   today.
5. **Parameter bounds**: the largest family. Either the parser
   feature (bounded ints in signatures) or interprocedural argument
   range join. Decide with Damian when slices 1-4 have drained the
   rest.
