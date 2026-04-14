# Watchdog & Progress-Tick Design

**Status:** scoping doc. Captures a design conversation; no code yet. The minimum action item (define the `progress-tick` primitive as a NOP today) is filed at the bottom.

## The motivating problem

The bare-metal Codex compiler currently has two diagnostic surfaces for crashes:

- **OOM handler** (`__out_of_memory`): fires from the function-prologue stack-overflow check (`cmp rsp, r10; jb __out_of_memory`) when the heap top has crept past the stack pointer. Prints `OUT OF MEMORY RSP=<n> HEAP=<n>` then `HLT`.
- **CPU-exception ISR dump** (commit `e014553`): fires on page faults, GP, UD, double fault, etc. Prints `!EXC=<vec> RIP=<addr>` then `HLT`.

Both fire *only on actual faults*. Neither catches the third class of failure: an **infinite loop or pathological slowdown** in code that never traps. Concretely, BACKLOG `C4` (April 2026) localized a bare-metal compile hang to `emit-isr-stubs` / `emit-out-of-memory-handler` / `emit-start` for non-trivial inputs — pure silence, no fault, the existing handlers have nothing to say about it.

This document scopes a watchdog mechanism to close that gap, structured so that today's diagnostic need (catch C4-class hangs) and tomorrow's hardware need (a satellite target with a hardware WDT chip that must be petted) share one primitive.

## Prior art

### Hosted compilers don't have one

GCC, Clang, rustc, GHC, OCaml — all delegate hang detection to the OS. SIGINT, SIGALRM, `ulimit -t`, OOM killer, `gdb attach`, `perf record`, `py-spy`. Internally they have per-pass `--time-report` and SIGINT-flushes-state handlers. No progress counters. No infinite-loop detection. They don't need it because the OS is the watchdog.

We're a self-hosting bare-metal compiler. We don't get to delegate.

### Bare-metal kernels

This is where the prior art lives.

- **Linux soft-lockup detector** — a per-CPU kernel thread that's *supposed* to wake every N ms. If it doesn't (because the CPU is wedged in a kernel loop with preemption disabled), an NMI fires and dumps stack. Essentially "task didn't make progress; here's where it's stuck."
- **Linux hard-lockup detector** — NMI watchdog driven by perf counters. Fires even when normal interrupts are masked. Catches the worst-case CPU wedge.
- **Hardware watchdogs (WDT chips)** — embedded systems hardware. A timer that resets the box unless code "pets" it (writes to a register) periodically. Pure forward-progress counter, enforced in silicon. Routers, cars, satellites, anything safety-critical.
- **FreeRTOS / Zephyr / safety RTOSes** — task-level watchdog: each task declares "I'll check in every X ms," missing the deadline = panic. Per-task quotas, not global.

### JIT and metered runtimes

- **WASM runtimes (wasmtime, wasmer)** — *gas metering*. Every basic block decrements a fuel counter; hitting zero traps. Catches every loop, including pure computation that produces no output. Cost: per-block instrumentation, ~5-15% slowdown.
- **JS engine "this script is taking too long" prompts** — same shape, less precise.

### What maps cleanest to us

The Linux soft-lockup detector pattern. We are the kernel and the user code at the same time. A timer ISR samples `RIP` and a "did anything happen" signal, dumps if no progress observed, optionally halts. We already have IDT machinery and a tick-count handler from the existing IRQ 0 wiring.

Gas metering is the more invasive alternative — guaranteed to catch anything but pays at every basic block. Soft-lockup-style sampling is cheaper and good enough for diagnostic use.

## What "progress" means

Single-threshold, single-signal watchdogs are brittle in both directions: too aggressive panics on slow-but-working code; too conservative misses real hangs. The honest answer is **escalate** as evidence accumulates, and combine multiple signals.

Candidate signals:

1. **`text-len`** — monotonically increases in our compiler (we never shrink the text buffer). Strong forward-progress signal for the codegen path. Blind to phases that don't write to text-buf (typecheck, name resolution, lowering).
2. **`heap-hwm`** — monotonic but weak. A loop that does `heap-save` / alloc / `heap-restore` over and over leaves HWM stable but the heap pointer churning. Looks busy, isn't producing.
3. **RIP sampled at the timer interrupt** — tells you *where* the CPU is, not *whether* it's progressing. RIP-in-same-function across samples can mean "stuck in this function" OR "this function is being called in a tight loop and making real progress." Disambiguate with RSP (same activation = same frame) or with a stack walk.
4. **A tick counter incremented by a `progress-tick` builtin** — explicit, expensive, requires source cooperation. Also doubles as the hardware-WDT-pet primitive (see below).

The model that handles the false-positive cases:

> **The output-progress signal (text-len + heap-hwm flat) is the trigger.** RIP and stack are the *diagnosis*, not the trigger. A function called repeatedly in a productive loop will have text-len climbing → no panic regardless of where RIP lands. A wedged loop produces nothing → text-len flat → panic, then RIP tells us which function.

That handles the "function-called-in-a-loop" false positive cleanly, and it handles the "heap is bouncing pointlessly" failure mode (RIP samples cluster in the same loop body even if heap-hwm hasn't moved — the dump still tells you "stuck in this region, heap churning, no real output").

The blind spot it doesn't cover: pure analysis phases that don't write to text-buf. That needs the `progress-tick` primitive (below) sprinkled at loop backedges in those phases, OR gas metering, OR we accept the blind spot and instrument those phases manually when they become the suspect.

## Tiered escalation

A single threshold is brittle. A tiered scheme costs almost nothing extra and produces better diagnostics:

| Tier | Trigger | Action |
|---|---|---|
| 0 | every tick (~100Hz) | record `(RIP, RSP, text-len, heap-hwm)` to a small ring buffer (4 slots, ~32 bytes) |
| 1 | text-len + heap-hwm both flat for 1s | start dumping a `WD:tick` to serial each tick — gives human-visible "stalling now" signal in the log |
| 2 | flat for 5s | dump the ring buffer (5 RIP samples + frame info), keep running |
| 3 | flat for 30s **and** RIP stays in same 4 KB window | dump full state (RIP, RSP, top 8 stack frames, text-len, heap-hwm) and `HLT` |

Two properties this buys:

- **Gradual signal.** You see the system stall *before* the watchdog kills it. By the time we halt, we've already printed evidence.
- **Diagnose, don't decide.** The kernel doesn't need to know what `emit-isr-stubs-loop` is or how long it should take. Pure observation: "output stopped, RIP isn't moving on, here's where we are."

Example outputs:

```
WATCHDOG: RIP=0x1A4C7F (in emit-isr-stubs-loop) sampled 5x same fn
  text-len: 152340 (Δ=0 over 2s)
  heap-hwm: 4825600 (Δ=0 over 2s)
HLT
```

vs. legitimately-slow:

```
WATCHDOG: RIP=0x1A4C7F (in emit-isr-stubs-loop) sampled 5x same fn
  text-len: 152340 → 158420 (Δ=6080 over 2s)
HLT (skipped — text-len advancing)
```

## The `progress-tick` primitive

This is the **only thing we commit to building today**. It's a single source-level primitive that decouples three concerns:

1. **WHO decides where it goes** — codegen, via one policy: "at every loop backedge" or "at every function entry" or "at every basic block." Tunable later; default is "nowhere" until a target opts in.
2. **WHAT it lowers to** — target-specific:
   - `target=desktop` / current default → **NOP** (or fully elided in DCE)
   - `target=bare-metal-soft-watchdog` → `inc qword [tick-counter]` (~2 cycles)
   - `target=satellite` → `mov qword [WDT_REG], magic` (the hardware pet register)
   - `target=metered` → `dec qword [fuel]; jz <trap>` (gas metering)
3. **WHO reads it** — independent. The watchdog ISR (or hardware WDT, or fuel checker) reads whatever the lowering produced.

The key property: the source code and the codegen don't change between targets. Only the lowering does. This avoids forking the codepath — only the assembler emitter for `progress-tick` differs.

### Cost discipline

| Injection density | Approx. overhead |
|---|---|
| At loop backedges only | sub-1% |
| At every function entry | ~1-2% |
| At every basic block | 5-15% |

Default to backedges. Tighten only if a specific target needs it.

### One-binary vs per-target binaries

If you want **one binary that runs on both** regular and watchdog-required environments, `progress-tick` has to be an indirect call through a slot filled at boot. That costs ~5-10 cycles per tick site even when the target doesn't need it.

If **separate binaries per target** are acceptable (`--target=satellite` vs `--target=normal`), the lowering is a direct emission and the cost is exactly what the table above shows — zero when not used.

For now: separate binaries, no indirect-call overhead, decision deferred.

## Decision and minimum action

We do **not** build the watchdog ISR or the tiered escalation today. Reasons:

- The current C4 hang can be tackled by direct instrumentation (per-iteration markers in `emit-isr-stubs-loop`, RSP/HeapReg dumps via inline `__itoa` at finalize entry) — cheaper than a watchdog.
- The watchdog's design is settled enough above that we can build it when there's pressure (a hardware target, or a class of hangs the direct instrumentation can't reach).
- Building it speculatively without a target consumer means the lowering-table will rot before it's used.

We **do** define the primitive today, as a NOP, so the codegen has a hook to inject into when we need it. That's a 5-line change and preserves the option without paying for it.

### Action item

Define `progress-tick : Nothing` as a builtin. Lower it to a NOP in all current targets. Document it in the syntax quickref so its role is clear. Codegen passes that want to inject it can do so without a follow-up language change.

Filed as a non-blocking task; no MM4 dependency.

## Follow-ups from the v1 basic-watchdog review

Captured during review of `hex-hex/basic-watchdog` (commit `53af10b`) — the minimal soft-lockup detector that landed before this scoping doc's deferral: timer-ISR samples `R10` and saved `RIP`, panics `WD!\n` + HLT after 550 ticks (~30 s) with heap unchanged and RIP inside a 4 KB window. Real work (text pingpong, 39 s compiles) does not false-positive. These are the known-deferred improvements on top of that v1:

1. **Self-host mirror in `Codex.Codex/Emit/X86_64Boot.codex`.** The v1 lives only in the reference emitter (`src/Codex.Emit.X86_64/X86_64CodeGen.cs`, ~108 lines of codegen). The self-host's bare-metal emitter does not emit the watchdog, so `codex build --target x86-64-bare` on the self-host produces a watchdog-less ELF. Mirror is blocked on C4 finishing — binary-pingpong byte-identity between ref and self-host is already broken by C4, so adding the mirror while C4 is live won't move the needle. Once C4 lands: port the watchdog to `Codex.Codex/Emit/X86_64Boot.codex` to restore byte-identical ref/self-host ELFs.

2. **Richer panic dump.** v1 prints just `WD!\n` before `HLT`. Matching the Tier 3 output described earlier in this doc — `RIP`, `RSP`, top stack frames, `text-len`, `heap-hwm` in hex — is a direct upgrade. You'd combine it with BINARY-DIAG's `PH:*` markers to localize the stall, but a self-contained dump removes the dependency on other diagnostic paths being active.

3. **Idle-wait petting.** v1's known false positive: after the bare-metal compiler reaches `PH:compile-done` and enters its REPL read-line idle wait, the heap pointer stops moving and RIP sits in the serial-read loop — the watchdog fires ~30 s later. This is the canonical use case for the `progress-tick` primitive committed at the bottom of this doc: emit a `progress-tick` at idle-wait loop entry (or tick it on each received byte), and the v1 watchdog's heap/RIP-flatness check continues to work because the tick updates one of the state slots. Cleanest resolution once `progress-tick` exists.

4. **Runtime disable flag.** Single-stepping a stalled RIP in GDB would fire the watchdog within 30 s of the session, well before you've finished inspecting. A `--no-watchdog` build flag (or an env-gated emit path) skipping `EmitWatchdogCheck()` avoids this. Cheap; not a blocker for v1's use as a compile-time hang detector.

5. **Configurable threshold.** v1 hardcodes 550 ticks. Exposing the threshold via a compile-time constant or env-var makes it easy to tune per-sample (e.g., drop to 5 s for a test suite that should never idle at all; raise to 5 min for cross-quire regressions that legitimately take longer).

6. **Align with the tiered escalation.** v1 is Tier 3 only — it goes straight to halt. The tiered scheme described above (Tier 1 stall-now log, Tier 2 ring-buffer dump, Tier 3 halt) produces better diagnostics for the same engineering cost and fits the same ISR slot. Worth revisiting when we touch the panic path anyway (see follow-up #2).

## Scope: things this doc does not commit to

- Whether to build the full tiered watchdog (beyond v1)
- Whether to ever ship a satellite target
- Whether gas metering is the right answer for some future target
- Specific timer frequency, ring-buffer size, address layout

Those decisions can be made when there's a concrete consumer. The shape of the design above gives them a path; they don't need to be litigated now.
