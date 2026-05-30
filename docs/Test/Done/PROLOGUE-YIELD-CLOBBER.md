# Prologue Yield-Path Argument Clobber

**Status:** FIXED — CL 2671 (2026-05-29, reek). `emit-prologue` now pushes
RDI/RSI on entry to the yield body and pops them after the `__task_yield`
call, balanced on every path (the skip-jumps retarget past the pop). Seed
rebuilt: hard fixed point (stage1 === stage2), self-verifies, full sweep
green.

The bug was latent — the yield flag is only set by the timer ISR when
`sched-current-task-addr != 0`, which never happens during a headless
compile (single process, no scheduler; the clobber path also needs a
non-empty ready queue), so the compiler self-compiled cleanly. The fix is
unconditionally correct regardless, and removes the landmine before the
scheduler ever drives a compile. The original "deterministic GPF >100KB"
claim below reflects a transport/scheduler era that no longer applied.

## Summary

Every function prologue emitted by `emit-prologue` (X86_64.codex:18-60)
includes a cooperative yield check. When the yield path fires, it
clobbers RDI and RSI (the first two function arguments) without
saving/restoring them. After the yield path completes, execution falls
through to the function body which reads the clobbered registers as its
arguments, causing a GPF from dereferencing a corrupt pointer.

## Symptom

Compiling programs with >~100KB combined source (foreword + app code)
crashes with a GPF. The crash is deterministic and always occurs at the
same address (`finish-unary+0x110`). The crash is both size-dependent
and content-dependent: it requires a variant type with record-type
constructor payloads AND a large enough input that compilation takes
long enough for a timer interrupt to set the yield flag.

### Crash Dump Pattern

```
CRASH in finish-unary+0x110 (page fault)
  RIP   0x002C2850
  RBX   0x00B5EA98    (valid heap pointer)
  R12   0xB534600000  (non-canonical — corrupt)
  R13   0xB534600000  (same corrupt value)
  RSI   0xB534600000  (arg1 — corrupt)
  RDI   0x00B5EA98    (arg0 — valid)
  R10   0x00CEC1F8    (heap @ 6.9 MB — normal)
  callR 0x7FFFF0B0    (actually old RSP, not a return address)
```

The function at the crash site receives two arguments (RDI, RSI) and
immediately pattern-matches on RSI by loading `[RSI+0]` as a variant
tag. RSI contains a non-canonical address, causing a page fault.

Note: `callR` in the crash dump is printed from `[R11+64]` in
`emit-cpu-exception-dump` (X86_64Boot.codex:341). R11 holds the
interrupt frame, and offset 64 is the CPU-saved RSP — NOT a return
address.

## Root Cause

### The Prologue (X86_64.codex:37-60)

```
st10c: mov r11, sched-yield-flag-addr
st10d: mov r11, [r11]           ; read yield flag
st10e: cmp r11, 0
st10f: je <yskip>               ; skip if no yield → body

st10g: mov r11, sched-ready-head
st10h: mov r11, [r11]           ; read ready queue head
st10i: cmp r11, 0
st10j: je <yskip2>              ; skip if queue empty → body

; === YIELD PATH (clobbers RDI and RSI) ===
st10k: mov r11, sched-yield-flag-addr
st10l: mov rdi, 0               ; ← CLOBBERS ARG0
st10m: mov [r11], rdi           ; clear yield flag
st10n: mov r11, sched-current-task-addr
st10o: mov r11, [r11]           ; current task ptr
st10p: mov [r11], rdi           ; task[0] = 0
st10q: mov rdi, sched-ready-head ; ← CLOBBERS ARG0 (again)
st10r: mov rdi, [rdi]           ; rdi = ready-head
st10s: mov rsi, [rdi+24]        ; ← CLOBBERS ARG1
st10t: mov [r11+24], rsi        ; linked-list insert
st10u: mov [rdi+24], r11
st10v: call __task_yield         ; yield (caller-saved regs gone)

; yskip and yskip2 both patch to HERE
; → falls through to function body with clobbered RDI/RSI
```

### Why It's Size-Dependent

The PIT fires timer interrupts at ~18.2 Hz. The timer handler
(emit-common-interrupt-handler, X86_64Boot.codex:536-543) sets the
yield flag when `sched-current-task-addr != 0`:

```
mov rdi, sched-current-task-addr
mov rdi, [rdi]
cmp rdi, 0
je <skip>
mov rdi, sched-yield-flag-addr
mov rsi, 1
mov [rdi], rsi     ; yield-flag = 1
```

For small inputs (<80KB), compilation completes in a few timer ticks.
For large inputs (>100KB), compilation takes more time, and enough
timer interrupts accumulate to hit a function prologue while the yield
flag is set.

### Why It's Content-Dependent

The crash requires code that produces enough function calls during
compilation for the yield flag to be checked while set. Variant types
with record payloads generate more type-checker work (computing
constructor payload sizes, verifying field accesses in match arms).
This extends compilation time and increases the yield-check window.

## Fix

**CL pending:** Save RDI and RSI at the start of the yield path (inside
the conditional), restore them after `__task_yield` returns.

```
st10k:  push rdi; push rsi        ; ← NEW: save args
st10k1: mov r11, sched-yield-flag-addr
...     (yield logic unchanged)
st10v:  call __task_yield
st10v1: pop rsi; pop rdi           ; ← NEW: restore args
```

This adds 0 bytes on the hot path (no yield requested) and ~4 bytes on
the cold yield path. Zero impact on the fixed-point binary size when the
yield flag is never set.

## Eliminated Theories

1. **normalize-whitespace O(n²):** The runtime uses `__str_replace`
   (native x86 helper emitted by X86_64TextHelpers.codex:2143-2399),
   which is O(n) — single scan, one heap allocation. The foreword
   `str-replace-loop` is never invoked at runtime.

2. **Stack overflow:** RSP at crash time is 0x7FFFF0B0 (only ~4KB used).
   The stack-heap collision check passes fine.

3. **Heap-stack collision:** R10 at crash is 6.9-12.1 MB, far below
   the 2 GB stack top.

4. **Breakpoint evidence:** Breakpoints in the current seed don't work.
   INT3 fires but the exception handler resumes without reporting.
   All "breakpoint not hit" observations were invalid.

## Reproduction

### Minimal (crashes with large foreword, works with small foreword)

```codex
Chapter: TestCrash
  cites CodexMagic chapter PlanarExchange
  cites CodexMagic chapter RPGEngine
  cites CodexMagic chapter GameRegistry
  cites CodexMagic chapter CampaignWorld
  cites CodexMagic chapter Token
  cites CodexMagic chapter ClanEconomy

Section: Types
  Rec1 = record { x : Integer, y : Integer }
  Rec2 = record { a : Text, b : Integer }
  Rec3 = record { p : Integer, q : Text, r : Integer }

  V = | A (Rec1) | B (Rec2) | C (Rec3)
  f : V -> Integer
  f (v) = when v
    is A (r) -> r.x
    is B (r) -> r.b
    is C (r) -> r.p
```

### Isolation results (2026-05-25)

| Test | Size | Result |
|------|------|--------|
| Same code, `cites Foreword chapter Maybe` only | 1.5 KB | OK |
| Same cites, Integer/Text payloads | 152 KB | TYPE-ERR (no crash) |
| Same cites, 3 local record payloads | 152 KB | **GPF** |
| MagicServer (different cites) | 257 KB | OK |
| CrossPlaneItems (these cites) | 168 KB | **GPF** |
| IntegrationTest (all cites) | 307 KB | **GPF** |

## Test Plan

1. `codex/test/variant-record-payload.codex` — new test with `.expected`
2. `build/test.ps1 -Jobs 4` — full battery regression
3. CrossPlaneItems compilation — must not GPF (type errors OK)
4. IntegrationTest compilation — the original 307KB case
5. `build/build.ps1` — fixed point must hold
