# reek -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status (2026-07-22, FIFTEENTH session -- HANDED OFF)

**No red gate anywhere. Tree clean: nothing opened, pending or shelved, token
released, no VMs. `build/test.ps1` NOT run** (not an agent command). Every CL
below is IN MAIN.

| CL | copy-up | what | seed |
|---|---|---|---|
| **10386** | **10387** | LirRetarget 4i: `LiLoad` on ARM64. **Whitelist COMPLETE.** | no |
| **10395** | **10397** | **BACKLOG 3.1 closed** (entry deleted) | docs only |
| **10420** | **10421** | `tag-equal` on arm64+riscv; unresolved plug calls WARN | no |

**Depot seed UNCHANGED at
`B180343660EBB6CF67C7B7319DB4980EE501E9FF7C31CA5949C8741699D0EAA8`.** No CL
this session carried a seed; `Sut === seed` was verified on each gate.

**Gate truth:** 10386 and 10420 both GREEN, one-pass hard fixed point,
`constants.hash` unchanged. **ARM64 battery, zero regressions at a fixed seed
on every change**, measured per-test against the immediately prior plug:
4i moved `db-row-update` only; 10420 moved `tag-equal` only. Current lane:
**320 PASS / 5 compile-only / 49 FAIL / 58 SKIP**.

## BACKLOG 3.1 is closed -- do not recreate it

Its finish line was "delete the `a64-alloc-temp`/`a64-alloc-local`/
`rv-alloc-temp`/`rv-alloc-local` bump allocators", which cannot happen while
the tree emitter runs -- and it handles every shape `lower-def-to-lir`
declines (closures, act blocks, vector patterns, text). Unreachable exit
criterion, so the entry accumulated detail for four days instead of closing.
The gap it actually named -- "no general allocator was ever built" -- is
closed: `lir-alloc-linscan` is that allocator, and as of 4i the ARM64 selector
whitelists every construct the lowering emits.

## Next action -- LirRetarget step 5, the RISC-V selector

The descriptor is decided (t0/t1/t2 are heavily used by the tree emitter, so
the pool is forced):

- `rv-lir-phys` = `[18..27, 28, 10..17]` -- s2..s11 callee, t3 preference, a0..a7 args
- `ncallee` 10, `nargregs` 11, `nregs` 19, `diva` -1, `divb` -1, `budget` 26
- scratch pair t5 (30) / t6 (31); heap reg is s1 (x9), excluded

Recipe: mirror `codex/plugs/arm64/Arm64Lir.codex` into
`codex/plugs/riscv/RiscVLir.codex`; add `'RiscVLir'` to Chapters in
`codex/plugs/riscv/build.ps1` and pass `-WithLir`; wire `rv-lir-emit-try` at
the head of `rv-emit-function` (`RiscVCodeGen3.codex:1145`). Start with the
descriptor plus zero-instruction functions -- byte-identical for everything
else, which is the property the ratchet is for.

**Decline `opening` by name.** `RiscVCodeGen3.codex` has two
`def.name == "opening"` sites. ARM64's 4b regression was exactly this: the tree
path special-cases a function by NAME and a whitelist written in instruction
shapes cannot see it.

## 3.19's 49 rows are now split -- do not re-derive this

The `[WARN] unresolved call` diagnostic (10420) is the instrument. **24 of the
49 are unresolved calls to 21 missing builtins**, every one kernel / process /
x86-port surface that does not exist on the lane: `process-spawn`,
`process-wait`, `process-exit`, `process-yield`, `process-get-pid`,
`process-get-cap`, `process-get-network-scope`, `process-spawn-with-heap`,
`port-in-16/32/byte`, `port-out-32`, `gpu-mem-read/write`, `poke-mmio`,
`poke-mmio-32`, `key-load`, `key-status`, `key-zero`, `net-status`,
`block-sector-count`. **That is 3.20's work, not codegen.**

**The genuine codegen remainder is ~14.** Best next targets:
`shadow-builtin-fold` (answers `lit 3` where 99 belongs -- the plug routes to
its own `text-length` helper over a user definition that shadows it, and
`a64-check-builtin-collisions` already detects this and only warns);
`tco-bitop-loop` (empty output); `ttt-perfect`, `console-readline-cite`,
`annotation-under-header` (empty or missing output). `list-pattern` and
`list-view-probe` need the `__list-len`/`__list-head`/`__list-tail` intrinsics
on the plug lanes.

## Two traps worth not rediscovering

- **An unresolved-call diagnostic must WARN, not fail.** Shipped as
  `[UNSUPPORTED]` (which `run.ps1` fails on) it regressed 16 PASSING tests --
  their unresolved calls sit on paths never executed. Dead code must not fail
  a build.
- **A cross-lane test failing on line 1 at a WRITE is missing a disk, not a
  capability.** `build/test-cross.ps1` has no disk/keys/vmargs/mouse handling
  and the Renode boards are CPU+UART+RAM. A backend with no scope enforcement
  would print `in-scope write True`; the lane prints `False`, and
  `fat16-overwrite` (no capability involved) fails identically.

## Standing defects filed earlier, still open

- **BACKLOG 3.24:** merging one more DiagnosticBag into `compile-frontend`'s
  `bag-merge-all` GP-faults in `bag-add`. Costs `-Passes ir-check`/`occ-report`
  in IR mode.
- **BACKLOG 3.21:** the RISC-V frameless-binop register-form fallback still
  clobbers its left operand (`let pf=... in bit-and pf 4095` answers 4095 where
  x86 answers 254). **Do not patch the bump allocator for this** -- with four
  rotating temps and no frame to spill into, every patch mirrors the bug. Step
  5 supersedes it, the same call as 3.2.

## Note to whoever picks this up

Damian's standing instruction from this session: **fix more, document less.**
He deleted two pieces of my work for being bookkeeping rather than codegen and
both deletions were right. Do not add backlog entries or design-doc sections.
Fix code. **ASCII only in everything, prose included** -- a non-ASCII character
has no CCE code point and vanishes silently at the I/O boundary.
