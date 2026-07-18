# reek -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status

Landing the LIR / middle-end codegen work (large, seed-carrying) -- tested through
the full battery, all plugs, all apps. Once it lands, every agent merges it down
before starting the polish round. You own the seed lane: seed-carrying changes flow
through you first.

## My target -- 3.9: CDX whole-program dead-code elimination

Every top-level def is emitted whether or not it is reachable from `opening`, so the
seed carries dead weight (the occurrence analyser found ~11 KB nothing calls). The
LIR/middle-end you just landed makes the root-set analysis this needs tractable.
- Machinery half-exists: `ir-prune-unreachable` (Emit/IRTextEmitter.codex) is a
  flood-fill DCE, but it runs ONLY on IR-text output for plugs, never on CDX emit.
- NOT a wire-it-in one-liner. The compiler reaches code indirectly -- effect handlers
  via the dispatch table, the REPL loop, builtin-dispatched functions -- so `opening`
  alone is an insufficient root set and a naive flood-fill ships a broken compiler.
  The work IS the correct conservative root set.
Seed-carrying: land ahead of blu's 7.20 so the seed order is clean.

## My lane (own it; others stay out)

codex/compiler Emit, IR, LIR, middle-end, Semantics; codex/plugs codegen. Not blu's
Types/Syntax, not fester's apps/boot/codex-vm.c.

## Open in my lane (BACKLOG, after the target)

- 3.8 residue: the LIR selector reaching the emitted binary (if the landing work did
  not fully close it); range analysis at the emit boundary -- `IrLet`/`IrName` lose a
  proved bound at its use.
- 3.1 ARM64 allocator folds INTO the LIR; do not build a standalone one.
- 2.15/2.18 the silent-shadow diagnostic (a def shadowing a builtin/chapter with
  different COST is silent, e.g. the O(n) Hamt `list-insert-at`). Error-vs-warning is
  a Damian decision.
- 3.6 cross-arch battery honest + gated (2 ARM64 fails; build-leg vs tier).

## For other agents

- LIR landed: re-vet any codegen claim in your memory against the new tree.
- BACKLOG 2.31 (REPL 2nd-compile #GP in find-effect-op-addr) is FIXED (CL 8872).
  It was the same defect as the closed 2.28: field-cache stale-RAX in `emit-text-lit`
  made `list-length (st.effect-op-addrs)` read the "read-text" literal length word (9).
  Verified: all three crashing ordered pairs exit 0 on the depot seed. The
  "shared `[]` across the R10 reset" lead was a wrong hypothesis -- do not chase it.

## The one lesson to keep from the LIR campaign

`gen2 === gen3` proves DETERMINISM, not correctness -- a compiler that miscompiles
the same way twice still reaches a byte-identical fixed point. A wrong answer needs a
`.expected`; a wasted instruction needs a bench; neither is caught by the fixed point.
