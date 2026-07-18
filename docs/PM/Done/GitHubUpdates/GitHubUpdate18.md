# GitHub Update 18 -- 2026-05-29

## Language

- **Linear types (Phase 3).** `linear` resources must be used exactly once on every path -- not dropped (leak, CDX2063), not reused (CDX2061). `freeze : linear a -> a` is the one-way bridge from a uniquely-owned value to a shareable immutable one (the identity; no copy needed). `mutable` records are the orthogonal discipline -- uniquely-owned data, read freely but never aliased (CDX2062), with borrow-vs-move inferred from callee signatures. **`linear` is for resources, `mutable` is for data.** Ergonomics: `mutable T` in signature position; field-assign statement sequencing in let and def bodies.
- **Type classes (full).** `class`/`instance` via compile-time dictionary passing: multi-instance dispatch, return-type polymorphism, generic constrained functions, parametric-type instances. Missing-instance is a static error (CDX2040).
- **Multi-pattern matching.** `|` alternation in `when`/`is` arms (P1).
- **Exhaustiveness checking (P8).** A non-exhaustive `when` is a static error, not a silent fall-through.
- **Constant folding (P9).** Compile-time arithmetic folding.

## Compiler

- **IR dead-code elimination.** In-compiler `ir-prune-unreachable` (replaces the external build/ir-dce.ps1 round-trip).
- **Pointer-map foundation.** `is-pointer-type`, `pmap-walk`, gated conformance check -- groundwork for precise heap/GC reasoning.
- **Mutable records.** In-place field mutation (`__record-set-mut`) under linear ownership.
- **Fixes.** read-line-cce 'e'-drop, cdx-fixedpoint signed/unsigned comparison, fold-constants double-fold, PROLOGUE-YIELD-CLOBBER (yield save/restore RDI/RSI), CHECK deck overflow, parameterize-walk stack, DESUGAR deck-record.
- **Build guards.** Untracked-source guard (build.ps1 rejects stray `.codex` that would be baked into the seed). Runtime-configurable survey multipliers (`survey=field:int` mode-line override, `compile.ps1 -Survey`).

## Tooling & Process

- **VSCode grammar** updated for the new keywords (`mutable`, `class`, `instance`, `lazy`, `induction`, `such`/`that`).
- **PerforceProcess** documents `p4 clean` in pre-gate hygiene -- force-sync leaves strays and depot-deleted-but-local files that the gather-and-build system can silently bake into the seed.
- **OperatorsManual** documents the seed-rebuild two-pass trap (install Sut.cdx only on a one-pass fixed point) and the Release-to-Public Gate (poison build, `seed/Codex.map` refresh).
- **KNOWN-CONDITIONS** records the linearity checker's deliberate borrow/move scope and the def-body multi-statement fix.

## Stats

- Seed: 2,400,385 bytes (SHA256 96E86720...)
- CDX hard fixed point on bare metal (SUT === stage1, one pass); full test sweep green
- 236 foreword modules, 54 compiler files, 50 transpiler plugs
