# ARM64 Register Allocator: Why It's Needed

**Date:** 2026-06-20
**Agent:** val
**Status:** Design justification. Still unbuilt — the thesis holds. No
general allocator exists; `next-local` is still monotonic and
`a64-can-retarget-last` still returns `False`. The workaround table
below has been refreshed against today's code (the codegen was split
into three files, and selective callee-save has since shipped).

---

## Executive Summary

The ARM64 codegen has 9 callee-saved registers (X19-X27) for local
variables. The current allocation scheme is monotonic: each let-binding,
function argument save, binary operand save, and record field eval
permanently advances `next-local`. Complex functions exhaust all 9
registers and spill to the stack. The spill mechanism works but the
local counter never recycles, so functions with multiple multi-argument
calls (like `ecam-read-32` with 4 args) consume 4+ locals per call
just for argument staging.

**21 bugs were found and fixed in this session.** Of those, at least
8 trace directly to local register pressure or its workarounds:

1. Save-args exhaustion in direct-call (4 locals per 4-arg call)
2. Save-args exhaustion in closure-call
3. Record field eval exhaustion (8 locals for 8-field record)
4. Binary-reg save-if-needed pressure
5. Epilogue not restoring recycled registers
6. Tail-call sentinel leak (-1 in locals list)
7. Spill-count recycling causing frame undersize
8. Retarget optimization corrupting MOVZ immediates

Each fix was a patch: recycle locals after use, skip trivial args,
always save/restore all register pairs, disable retarget. These
patches interact badly with each other and with merge-downs from
other agents. The fundamental architecture is unsound.

---

## The Evidence

### Pattern 1: Multi-Argument Function Calls

Every call to a 4-argument function (like `ecam-read-32 bus dev func offset`)
uses `a64-save-args` which evaluates each argument and saves to a
callee-saved local. For 4 arguments, that's 4 locals consumed
permanently. A function that calls `ecam-read-32` three times
(like `arm64-pci-read-dev`) consumes 12 locals just for arg staging
-- more than the 9 available.

**Workaround applied:** `a64-emit-direct-call` saves `next-local`
before save-args and restores after the BL. This recycles the
arg-staging locals. But it interacts with the epilogue (which uses
`next-local` to decide how many registers to restore) and with
spill-count (which must NOT be recycled or the frame is undersized).

**Failure mode:** `ecam-address` (4 params + 3 bit-shl calls + 3
additions) exhausted locals, corrupting the caller's X23 because
the epilogue didn't restore it (next-local was recycled below X23).

### Pattern 2: Record Construction

`a64-eval-record-fields` evaluates each field value and saves to a
local. An 8-field record (like `Arm64PciDevice`) consumes 8 locals.
Combined with the function's own params and let-bindings, this
overflows immediately.

**Workaround applied:** Inline record construction -- evaluate each
field and store immediately, recycling the temp local per field.
This reduced 8 simultaneous locals to 1.

### Pattern 3: Nested Match Expressions

`when result is Just (d) -> d is None -> when ...` generates
deeply nested code with result-locals for each match level. Each
`if/match` allocates a result-local that persists until the end
of the match. With 2-3 levels of nesting, 2-3 result-locals are
consumed.

**Workaround applied:** Flatten nested matches into separate
helper functions. This is a source-level workaround, not a codegen
fix.

### Pattern 4: Binary Operations with Function Calls

`a + f(x)` evaluates `a`, saves to a local (via `save-if-needed`),
evaluates `f(x)` (which may call functions that consume more locals),
then reloads `a` for the ADD. The save-if-needed local is dead
after the ADD but permanently consumed.

**Workaround applied:** Recycle `next-local` to pre-save value
after the ADD instruction.

### Pattern 5: The Retarget Optimization

`a64-retarget-last-rd` patches byte 0 of the last emitted
instruction to change the destination register. This avoids a
MOV instruction. But the patch uses `bit-and byte0 0xE0` to
preserve bits 7:5 and `bit-or ... new-rd` to set bits 4:0. For
MOVZ instructions, bits 7:5 of byte 0 contain part of the
immediate field. The patch corrupts the immediate, turning
`MOVZ X12, #1` into `MOVZ X24, #0`.

**Workaround applied:** Disabled entirely (`a64-can-retarget-last`
returns False). This costs ~1 extra MOV instruction per local
store but eliminates the corruption.

---

## What The Allocator Needs To Do

### Core Requirement: Per-Expression Local Scope

The x86 compiler's emit phase uses `__heap-save` / `__heap-restore`
to scope allocations. The ARM64 codegen needs an analogous mechanism
for local registers: save the local counter at expression entry,
restore at expression exit. Locals allocated during expression
evaluation are temporary and can be reused.

The key invariant: **let-bindings are durable; everything else is
temporary.** A `let x = expr in body` allocates a local for `x`
that persists through `body`. But the locals consumed by `expr`'s
evaluation (save-args, save-if-needed, record field evals) are
dead after `x` is bound.

### What Must Be Preserved

- **Let-binding locals** must survive across all subsequent code
  in their scope (including function calls, which save/restore
  callee-saved regs in their prologues).
- **Function parameters** (bound at function entry) persist for
  the entire function body.
- **Act-block IrDoBind** locals persist across subsequent
  statements in the same act block.

### What Can Be Recycled

- **Save-args locals** after the BL returns.
- **Save-if-needed locals** after the binary operation completes.
- **Record field eval locals** after the field is stored.
- **If/match result locals** after the result is consumed by the
  enclosing expression.
- **Temporary locals from trivial arg evaluation** -- these are
  always dead after the call.

### The Spill Mechanism

When all 9 callee-saved registers are in use, locals spill to the
stack at `[SP + (slot - 64) * 8 + 96]`. The frame size is patched
at function end via `a64-patch-frame`. Spill loads use
`a64-alloc-temp` (cycling X12-X15).

The spill mechanism works but has issues:
- `spill-count` must track the PEAK, not the current value.
  Recycling `spill-count` causes frame undersize.
- Consecutive spill loads use different temp registers (fixed --
  was using fixed X9).

### Register Map

| Register | Role | Allocator Notes |
|----------|------|-----------------|
| X0-X7 | Arguments / return | Caller-saved. Used for call setup. |
| X8 | Indirect result | Not used by codegen. |
| X9-X11 | Temps | Caller-saved. Used for scratch (LI, address calc). |
| X12-X15 | Temps (alloc-temp) | Cycling pool. 4 registers. |
| X16-X18 | Platform | Reserved. |
| X19-X27 | Locals (alloc-local) | Callee-saved. 9 registers. THE constraint. |
| X28 | Heap pointer | Global. Never allocated. |
| X29 | Frame pointer | Saved/restored in prologue/epilogue. |
| X30 | Link register | Saved/restored in prologue/epilogue. |
| X31 | SP / XZR | Context-dependent. |

### What The x86 Compiler Does Differently

The x86 selfhost compiler has a similar register pressure problem
but solves it differently:

1. **Destination-driven emission** -- the emitter knows WHERE the
   result goes before emitting, so it can target the right register
   directly instead of emitting to a temp and MOVing.

2. **R8/R9-staged binary operands** -- binary expressions use R8/R9
   as intermediate staging, consuming zero locals.

3. **Minimal frame elision** -- leaf functions skip the frame pointer
   entirely. Near-leaves keep only the stack guard.

4. **Per-function heap-save/restore** -- the emit loop saves/restores
   the heap between functions, giving each function a clean slate.

The ARM64 codegen was "transliterated from old/src/Codex.Emit.Arm64/
Arm64CodeGen.cs" (line 10 of Arm64CodeGen.codex). The C# version
used .NET's register allocator. The Codex version does not have one.

---

## Files To Change

The codegen has since been split across three files. The allocator
primitives and the prologue/epilogue stayed in the first; the emitters
that consume locals moved out.

| File | What |
|------|------|
| `codex/plugs/arm64/Arm64CodeGen.codex` | The allocator itself: `a64-alloc-local`, `a64-alloc-temp`, `a64-update-peak`, `a64-save-if-needed`, `a64-load-local`, `a64-store-local`, `a64-emit-binary-reg`. Also the prologue/epilogue and `a64-compute-save-pairs`. |
| `codex/plugs/arm64/Arm64CodeGen2.codex` | Call emitters and `a64-emit-let`: `a64-emit-apply`, `a64-emit-direct-call`, `a64-emit-closure-call*`, `a64-save-args`, `a64-arg-is-trivial`. |
| `codex/plugs/arm64/Arm64CodeGen3.codex` | `a64-emit-record`, `a64-save-args-all`, `a64-emit-tail-call-general`. |
| `codex/plugs/arm64/Arm64Runtime.codex` | Runtime helpers. Stable. |
| `codex/plugs/arm64/Arm64Plug.codex` | Plug entry. Stable. |

---

## Current Workaround State

**Still true: no general allocator was ever built.** Every row below is
a patch around the monotonic `next-local` counter, and the thesis of
this document stands.

Two things changed since the CL 5046 snapshot. First, the codegen was
split — `a64-emit-prologue` / `a64-emit-epilogue` / `a64-can-retarget-last`
stayed in `Arm64CodeGen.codex`, while the call and record emitters moved
to `Arm64CodeGen2.codex` and `Arm64CodeGen3.codex`. Second, the
always-save prologue rows are **obsolete**: the Optimization Plan's
Phase 1 shipped selective callee-save.

| Workaround | Location | Status |
|------------|----------|--------|
| Direct-call local recycling | `a64-emit-direct-call` (CodeGen2) | Active — saves `next-local`, restores it after the BL |
| Closure-call local recycling | `a64-emit-closure-call` (CodeGen2) | Active |
| Binary-reg local recycling | `a64-emit-binary-reg` (CodeGen) | Active |
| Record inline construction | `a64-emit-record` (CodeGen3) | Active — one `ptr-loc` local, fields stored flat |
| Tail-call save-args-all | `a64-emit-tail-call-general` (CodeGen3) | Active |
| **Selective callee-save** | `a64-compute-save-pairs` + `a64-nop-unused-saves` (CodeGen) | **Shipped** — supersedes the old always-save-5-pairs prologue. `peak-local` sizes the save set; unused STPs are patched to NOP and removed by the peephole compactor |
| Selective restore | `a64-emit-epilogue` (CodeGen) | Recomputes `save-pairs` from `peak-local`; emits only the needed LDPs |
| Frame always emitted | `a64-emit-prologue` (CodeGen) | **Still unconditional.** `SUB SP,SP,#96` + `STP x29,x30` + `STP x19,x20` for every function, leaf or not. Frame elision (Optimization Plan Phase 5) is unbuilt |
| Retarget disabled | `a64-can-retarget-last` (CodeGen) | Returns `False` — unchanged. The MOVZ-immediate corruption is still unfixed |
| Trivial-arg skip | `a64-arg-is-trivial` (CodeGen2) | Returns `False` — still disabled |
| Stack guard | Prologue | CMP SP, X28 + B.HI/WFI |
| Spill load via alloc-temp | `a64-load-local` (CodeGen) | Uses cycling temps |

These workarounds carry the full cross-architecture battery (ARM64 is
at 135/135). They did not stop being workarounds: `next-local` still
never recycles except where a caller explicitly saves and restores it,
`a64-arg-is-trivial` and `a64-can-retarget-last` are still two
optimizations turned off rather than fixed, and every new emit path has
to remember to do the save/restore dance by hand.

---

## Test Inventory

The cross-architecture battery is the regression gate:
`build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -RenoTimeout 10`, at
135/135. If it breaks, the allocator change is wrong.

(The original inventory here listed three tests and a
`arm64-web-server.codex` that crashed in `arm64-pci-find-dev-loop`
after exhausting the workarounds' capacity. The battery has since grown
past that; the register pressure it exposed is the same pressure this
document is about, and it is still latent — the workarounds hide it,
they do not remove it.)

---

## Recommendation

Build a proper per-expression local scope mechanism. The simplest
correct design:

1. At each `a64-emit-expr` entry, save `next-local`.
2. At each `a64-emit-expr` exit, restore `next-local` to the
   saved value UNLESS the expression is a let-binding value
   (which needs its local to persist).
3. `spill-count` tracks the PEAK across all scopes (never
   decreases).
4. The epilogue uses the peak `next-local` (tracked separately
   from the recycling counter).
5. Remove all the individual recycling workarounds -- they become
   unnecessary.

This is ~50 lines of change in `a64-emit-expr`, `a64-emit-let`,
and `a64-emit-act-loop`. The workarounds in `a64-emit-direct-call`,
`a64-emit-binary-reg`, and `a64-emit-record` can be removed.
