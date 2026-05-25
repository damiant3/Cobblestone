# Trace-Alloc Hot-Path Crash

## Status: ROOT CAUSE FOUND (2026-05-25)

## Root Cause

**Two bugs, both fixed:**

### Bug 1: Trace buffer address in code segment

`trace-enabled-addr` was initially set to `1084736 (0x108D40)`,
computed as `prof-buf-addr + prof-max-samples * 16`. But the profiler
entries are 8 bytes (not 16) — the actual prof buffer end is
`36160 + 65536 * 8 = 560448 (0x88D40)`. Address `0x108D40` falls
inside the binary code segment (load addr `0x100000`). Writing `1` to
`trace-enabled-addr` overwrote machine code at `__alloc + 0x45`,
causing a GPF in the next function call.

**Fix:** Changed trace addresses to start at `560448 (0x88D40)`, right
after the profiler buffer. Trace buffer capacity reduced from 131072
to 30000 entries to fit in the gap before the code segment.

### Bug 2: Stale seed baked-in addresses

After fixing the addresses in source, the crash persisted because the
**seed** still had the old addresses baked into its machine code as
`li` immediates. The seed compiled the SUT using its own
`emit-alloc-trace` which hardcoded `li reg-r11, 0x108D40`. Changing
the constant in `X86_64Boot.codex` has no effect until the seed is
rebuilt — this is a general property of all ~50 constants used in
`emit-*` functions.

**Fix:** Seed rebuild required. Pending CL with corrected addresses.

## Baked-In Constants — Class Vulnerability

Any constant used in `li reg-X <value>`, `cmp-ri reg-X <value>`, or
`add-ri reg-X <value>` inside an emit function becomes a hardcoded
immediate in the seed binary. Changing the source constant without a
seed rebuild silently produces wrong code. ~50 constants have this
property (kernel metadata addresses, buffer sizes, hardware ports,
scheduler thresholds, watchdog timers).

Prevention options:
1. Document which constants are load-bearing (comment block in X86_64Boot)
2. Build-time hash check: concat script hashes constants, compares to seed
3. Move constants to rodata table (eliminates the class but adds indirection)
