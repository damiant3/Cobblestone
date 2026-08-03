# Plug Compiler Crash -- Test Plan (Session 3)

Companion to `PLUG-CRASH-INVESTIGATION.md`. That file documents the bug
and what has been ruled out. This file proposes new angles and concrete
probes, ordered by cost-to-execute.

## Re-Reading the Evidence

Three observations from the prior session's data that may have been
under-weighted.

### 1. R15 = 0x1a6f7c5 is not 8-byte aligned

R15 ends in `5`. Bivy/deck records always come off R10, which is
8-aligned (bumped by 8 or 16). So R15 cannot be the start of a real
TypeBinding -- it is 3 bytes low of one. The "corrupt value at +3"
re-reads as the *aligned* first 4 bytes of the real record at
`0x1a6f7c8`, with 3 bytes of preceding noise sampled at `0x1a6f7c5–7`.

Reframe: the wrong pointer is being handed around. The record itself
may be intact.

### 2. CR2 drifts (0x2ee56a / 0x2ee7af / 0x2eeef7) across runs

Heap layout is deterministic for a given build. The contents written
into a fixed slot should not drift. Drift implies the value comes from
something non-deterministic across runs -- and on this machine, the
only such source is a **return address pushed on the stack** at a
slightly different call site per run (WHPX scheduling jitter).

Implication: something is copying bytes from near RSP onto the heap,
or dereferencing a Text pointer that points into stack memory which
has since been overwritten.

### 3. Corruption stride is 4 bytes

Bytes 3–6 hold the live value. A record-set through Codex's
`record-set` builtin would be 8-byte aligned. So the write is more
likely:
- a `__buf-write-i32`,
- a 32-bit `MovStore`,
- a memcpy with miscomputed length.

### 4. "WHPX only" probably means "WHPX visible"

WHPX and TCG should execute the same instructions on the same data.
The serial-chunking difference changes heap layout slightly, so the
corruption likely happens under TCG too -- but lands somewhere
harmless. The bug is latent under TCG, not absent.

## Test Plan

### Probe A -- Sentinel-fill the heap, run under TCG

**Goal**: Convert TCG into a reproducer by detecting silent corruption.

**Change**: In `codex/Emit/X86_64Boot.codex`, before any allocation,
fill `[0x600000 .. stack_min]` with `0xCDCDCDCDCDCDCDCD`.

**Procedure**:
1. Rebuild seed with the fill.
2. Run plug compile under QEMU TCG (no WHPX), let it run to
   completion or until the watchdog stops it.
3. Dump the heap and scan for any 8-byte word that is neither the
   sentinel nor a plausible value (record header, list spine, text
   byte). Flag seed code addresses (range `0x100000 .. 0x600000`)
   appearing anywhere they should not.
4. Record the *address* of every offender. That tells you which
   allocation region (phase / record type) was scribbled.

**Why first**: cheapest probe that gives you a reproducer in the
debuggable environment.

### Probe B -- Identify the corrupt value's identity

**Goal**: Name the perpetrator function.

**Procedure**:
1. Emit the seed's symbol map / IR text once.
2. Look up `0x2EEEF7`, `0x2EE7AF`, `0x2EE56A` in the symbol map.
3. If all three land in the same function: that function's code is
   what is being captured (return address, closure code-ptr, or PC
   at the moment of write).
4. If they straddle several functions: cluster by section
   (lowering / sort / ISR / serial) -- the cluster identifies the
   subsystem.

**Why second**: ten minutes. Dramatically narrows the search.

### Probe C -- Closure-record collision hypothesis

**Hypothesis**: `sort-by xs compare-binding-names` materializes a
closure record `(code-ptr, env-ptr)`. The 8 bytes of `name : Text` and
the first 8 bytes of a closure record have the same shape -- a code
address followed by something. If a closure record allocated by
`sort-by` lands adjacent to (or is being mistaken for) a TypeBinding,
the `code-ptr` would naturally be a seed function address. A 32-bit
code-ptr write with 32-bit pad would match the observed 4-byte stride.

**Procedure**:
1. Grep `emit-closure-alloc`, `make-closure`, closure-build sites for
   non-8-aligned starting offset.
2. Look for any closure constructor that writes the code-ptr as a
   32-bit relocation followed by padding.
3. Confirm whether `sort-by`'s comparator wrap allocates a closure
   at all (vs. inlining the function reference).

### Probe D -- In-place sort + `&` aliasing

**Hypothesis**: `lower-chapter` runs:

```codex
let all-types = sort-bindings (types & env.bindings)
```

If `&` aliases the tail of `env.bindings`, then `sort-by`'s
`list-set-at` mutates `env.bindings` storage. If `env.bindings` is
referenced again later (or threaded back via `__record-set ust`),
corruption follows.

**Procedure**:
1. Read the implementation of `&` for `List a` -- does it build a new
   spine or alias the right operand?
2. Walk every use of `env.bindings` after `lower-chapter` to see
   whether the original is reachable post-sort.
3. Quick mitigation test: replace with
   `sort-bindings (list-copy (types & env.bindings))` (or an explicit
   spine copy). If the crash disappears, aliasing is the cause.

### Probe E -- `__record-set ust "expr-types"` linearity violation

**Hypothesis**: `lower-chapter` calls
`__record-set ust "expr-types" (sort-expr-types …)`. Known Condition:
`record-set` is in-place mutation, safe only on linearly-owned state.
If the *same* `ust` is passed in by the driver across chapters or
otherwise shared, the linearity invariant is broken.

**Procedure**:
1. Find every caller of `lower-chapter`.
2. Check whether `ust` is freshly constructed per chapter or reused.
3. If reused: rewrite `__record-set` use as a deck-record
   reconstruction of `ust` and retest.

### Probe F -- Isolated `sort-by` probe sample

**Goal**: Reproduce the bug without the rest of lowering.

**Procedure**:
1. Write a sample that builds a ~500-entry `List TypeBinding` with
   mixed-length names matching the plug's type-table distribution.
2. Sort it with `sort-bindings`.
3. Walk the result; assert each `.name` equals what was inserted and
   the list is monotone under `text-compare`.
4. If this repros on the seed: bug is in `sort-by`, `list-set-at`,
   `list-at`, or `text-compare` -- not in the rest of lowering.

### Probe G -- Post-sort invariant scan

**Hypothesis**: `sort-bindings` corrupted only the list spine -- two
entries became aliases of the same physical TypeBinding, or a
TypeBinding pointer was replaced by garbage. The bsearch hits the
midpoint and dereferences.

**Procedure**: Between `sort-bindings` and the first use of
`all-types`, walk the list once and assert:
- monotone `text-compare` ordering across adjacent entries,
- no two adjacent entries share the same TypeBinding pointer,
- every `.name` Text pointer is in `[heap-base, R10)` and the first
  8 CCE bytes are printable.

First failing assertion names the failure mode.

### Probe H -- In-binary watchpoint

**Goal**: A watchpoint that works under WHPX (where GDB hardware
watchpoints don't).

**Procedure**: Add a runtime invariant scanner that runs at the end
of every phase (or at major allocation points). Scan all live
TypeBindings and assert `.name` points into the heap and is
printable CCE. The first phase that fires the assertion is the
perpetrator. Cheaper than a GDB session and works under WHPX.

### Probe I -- Re-check the ISR rule-out

**Hypothesis**: The doc rules out the timer ISR because slot 0 has
`slice=0`. But the ISR also fires for serial RX, ATA completion, and
any other IRQ. WHPX vs TCG deliver interrupts differently --
interrupts may arrive under WHPX that TCG never delivers. A leaked
interrupt frame is exactly the shape of "seed PC copied somewhere."

**Procedure**:
1. Add per-IRQ counters (not just timer).
2. Print all counters at end of run.
3. If any non-timer IRQ fired during plug compile but not during
   self-compile, audit that ISR's stack discipline.

## Recommended Order

1. **B** (10 min) -- name the perpetrator function from the three
   drifting addresses.
2. **A** (medium) -- sentinel-fill + TCG scan, to make the bug
   debuggable without WHPX.
3. **F** (medium) -- isolated `sort-by` probe, to confirm or eliminate
   the lowering-sort hypothesis.
4. Branch on what A/B/F reveal: C/D/E/G/I as applicable.

## Out of Scope

- Re-running ruled-out probes from `PLUG-CRASH-INVESTIGATION.md`
  unless one of the above probes produces evidence that contradicts
  the prior rule-out.
- Editing the C# reference compiler under `old/` (permanently
  retired).
