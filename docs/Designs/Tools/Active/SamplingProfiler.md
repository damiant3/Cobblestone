# Sampling Profiler

**Status:** ISR sampling code landed (CL 2287). Builtins and host
dump processing not yet implemented.

## What It Does

Interrupt-driven sampling profiler for the bare-metal compiler. Samples
the interrupted RIP at ~1 KHz via the timer interrupt, accumulates
samples in a fixed buffer, then dumps a function-level histogram over
serial after compilation. No per-function instrumentation — zero
overhead except one ISR per millisecond.

## Architecture

```
Timer ISR (every ~1ms)
    │
    ├── Read interrupted RIP from stack frame [RSP+40]
    ├── Check prof-enabled-addr (skip if 0)
    ├── Check prof-cursor-addr < prof-max-samples (skip if full)
    └── Store RIP at prof-buf-addr[cursor], increment cursor

Post-compile dump
    │
    ├── Set prof-enabled-addr = 0
    ├── Emit "PROF:<count>" over serial
    ├── For each sample: emit hex RIP over serial
    └── Host resolves RIPs via symbol map → function histogram
```

## Kernel Metadata (in X86_64Boot.codex)

| Address | Name | Width | Purpose |
|---------|------|-------|---------|
| 36136 | prof-enabled-addr | 8 | 1 = sampling active, 0 = off |
| 36144 | prof-cursor-addr | 8 | Next write index in sample buffer |
| 36160 | prof-buf-addr | 512 KB | 65536 × 8-byte RIP samples |

Buffer ends at 36160 + 524288 = 560448 (well before serial ring
buffer at 5 MB).

## Implementation Status

### Done (CL 2287)
- Timer ISR sampling code in `emit-common-interrupt-handler`
- Metadata addresses defined
- Sampling guarded by `prof-enabled-addr` flag
- Buffer overflow check (`prof-cursor-addr >= prof-max-samples`)

### Remaining

1. **Runtime helpers** in X86_64Helpers.codex:
   - `__prof_start`: set prof-enabled-addr = 1, prof-cursor-addr = 0
   - `__prof_dump`: set prof-enabled-addr = 0, emit sample count
     and RIP values over serial as `PROF:` lines

2. **Builtins** in X86_64Builtins.codex:
   - `prof-start : Nothing` — calls `__prof_start`
   - `prof-dump : Nothing` — calls `__prof_dump`

3. **Name resolver** in NameResolver.codex:
   - Add `prof-start`, `prof-dump` to known builtins

4. **Compile flag** in opening.codex:
   - `profile` flag (like `prose`, `poison`, `debug`)
   - When set: call `prof-start` before `compile-frontend`,
     call `prof-dump` after emit

5. **Host-side processing** in compile.ps1 or a new script:
   - Capture `PROF:` lines from serial
   - Resolve each RIP hex value against `seed/Codex.map`
   - Tally per-function sample counts
   - Sort descending, print top-N with percentages
   - Output format:
     ```
     === Profile: 12847 samples ===
       emit-expr              3214  25.0%
       emit-function          1847  14.4%
       st-append-code         1203   9.4%
       __str_concat            891   6.9%
       ...
     ```

## Usage

```powershell
# Compile with profiling
build/compile.ps1 -Src build/output/Codex.codex -Out out.cdx -Log out.log -Profile

# Or in opening.codex directly:
# emit-cdx clean "CDX profile" flags
```

## Design Notes

- The timer fires at PIT rate (~1 KHz on codex-vm). With a 50s compile,
  that's ~50000 samples — well within the 65536 buffer.
- Sampling only runs when `prof-enabled-addr` is set, so normal
  compilation has zero profiler overhead.
- The ISR adds ~20 instructions to the timer path when profiling is
  enabled, plus a 2-instruction check when disabled. Negligible.
- RIP resolution happens on the HOST, not bare-metal. The compiler
  doesn't need to carry the symbol map at runtime.
- Future: could add a second buffer for stack sampling (sample
  `[RSP+48]` = return address for 2-deep call stacks).
