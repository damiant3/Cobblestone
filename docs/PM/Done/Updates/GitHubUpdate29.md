# GitHub Update 29 -- 2026-06-28

Covers main CLs 6145-6300 (since Update 28 at CL 6144, 2026-06-26).
Two days, 35 copy-ups from four agent streams (val, fester, reek, blu).

## Codegen Quality Campaign -- All Three Architectures

Three parallel optimization campaigns brought all three backends to
native-class codegen on an expanded 8-benchmark suite: fib, fact, gcd,
sum, ack, tak, collatz, locals.

### x86-64 (val, 12 optimizations)

| Bench | Before | After | GCC -O0 | GCC -O2 | MSVC /O2 |
|-------|-------:|------:|--------:|--------:|---------:|
| fib   |     21 |    21 |      19 |      20 |       20 |
| fact  |     15 |    15 |      16 |      15 |       12 |
| gcd   |     17 |    17 |      18 |      14 |       14 |
| sum   |     14 |    14 |      20 |      23 |        9 |
| ack   |     42 |    24 |      35 |      21 |       21 |
| tak   |     77 |    44 |      46 |      32 |       30 |
| collatz| 35    |    20 |      34 |      26 |       16 |
| locals |  59   |    54 |      52 |      41 |       41 |
| Total |    280 |   209 |     240 |     196 |      163 |

Key optimizations: pow2 strength reduction (collatz 35->28),
emit-push/cmp-fusion/simple-arg-regs (tak 77->44, ack 42->36),
dead jump elimination, TCO direct dispatch with dependency analysis,
dest-driven TCO fallback, single-complex TCO routing, relaxed
leaf-args-all-bound (ack 30->24), merged adjacent NOPs.

### ARM64 (fester, 15 optimizations)

| Bench | Before | After | GCC -O0 | GCC -Os |
|-------|-------:|------:|--------:|--------:|
| fib   |     36 |    19 |      31 |      28 |
| fact  |     26 |    12 |      28 |      21 |
| gcd   |     31 |    11 |      33 |      23 |
| sum   |     28 |    11 |      32 |      22 |
| ack   |     -- |    28 |      45 |      35 |
| collatz| --   |    20 |      41 |      26 |
| tak   |     -- |    46 |      49 |      48 |
| locals |  --  |    29 |      58 |      30 |
| Total |    121*|   176 |     317 |     233 |

*4-bench total before; 8-bench total after.
Codex ARM64 beats GCC -Os by 24% aggregate. All 8 benchmarks beat
or match GCC -Os.

Key optimizations: destination-driven emission, direct arg emission,
compact prologue, TCO skip-save, CMP-immediate, peephole MOV
eliminator, NOP compaction, STP-pre/LDP-post frame merge, TBZ/TBNZ
for mod-2 checks, dead-branch elimination, direct TCO arg emission.

### RISC-V (val, 25 optimizations)

| Bench | Before | After | GCC -O0 | GCC -Os |
|-------|-------:|------:|--------:|--------:|
| fib   |     41 |    19 |      34 |      22 |
| fact  |     37 |    14 |      27 |       9 |
| gcd   |     39 |     6 |      26 |       6 |
| sum   |     39 |     8 |      27 |       9 |
| ack   |     -- |    22 |      -- |      -- |
| collatz| --   |    16 |      -- |      -- |
| tak   |     -- |    32 |      -- |      -- |
| locals |  --  |    24 |      -- |      -- |
| Total |    156*|   141 |     114*|     46* |

*4-bench total; 8-bench GCC data not yet collected for new benchmarks.
gcd (6) matches GCC -Os exactly. sum (8) and fib (19) beat GCC -O0.
17 optimization CLs, 135 instructions eliminated.

Key optimizations: pow2 strength reduction, NOP compaction, direct
TCO with dependency analysis, expanded frameless TCO, direct N-arg
emission, reordered mixed-TCO, last-arg skip, SRAI encoder.

## New Benchmarks

Four new micro-benchmarks added to `bench/` (CL 6204):
- **ack** (Ackermann): deep recursion, 3 args, no TCO
- **tak** (Takeuchi): 3-arg mutual recursion
- **collatz**: loop with even/odd branching, integer division
- **locals**: 6 local variables, function calls, demonstrates
  register pressure

These stress areas the original 4 benchmarks (fib, fact, gcd, sum)
don't cover: high register pressure, non-tail recursion, branching
loops, and complex argument shuffles.

## Codex Circuits -- Interactive EDA Suite

New application: `apps/circuits/` (9 chapters). A schematic capture
and circuit design tool running on bare metal with the GPU rasterizer.

- GPU triangle rasterizer at ~950 FPS (8526 triangles/frame)
- Mouse panning via codex-vm I/O port mouse
- Keyboard zoom controls
- Toolbar, tab bar, sidebar, status bar chrome
- 4-tier GPU depth buffering
- Demo circuit with components

Built by reek across two copy-ups (CLs 6255, 6293). Requires
`-mem 3072` for GPU command buffer region at 0xBE000000.

## codex-vm Fixes

Three VM bugs fixed (CL 6300):

1. **Serial EOF for no-input boots**: CDX binaries with REPL exit
   mode hung when run without `-input` because the VM required
   `input_file` to be non-null before signaling EOF. The HEAP:
   detection, COM2 LSR, and main-loop stdin-eof flag all guarded
   on `input_file`. Fix: signal EOF immediately when no input was
   provided.

2. **UEFI PE load crash**: pre-committed guest memory was 16 MB but
   the UEFI PE loader places binaries at 0x1000000 (16 MB) -- the
   first byte outside committed memory. Host-side `memcpy` hit an
   access violation. Fix: extend pre-commit to 32 MB.

3. **GPU region RAM cap** (CL 6255): GPU command buffer at
   0xBE000000 requires `-mem 3072` (3 GB) to be within the
   committed memory region.

## GPU Globe (blu)

CL 6195: earth + black hole renderer with GPU rasterizer. atan2 fix,
f32 compute, TCO, auto entry wrappers, atmospheric rim glow,
Keplerian accretion disc.

## ARM64 QEMU/OCI Web Server (reek)

CL 6183: ARM64 kernel running in QEMU with TCP web server. Heap
alignment fix, FPU enable, peek-16/poke-16, PCI BAR assignment,
TCP checksum, VirtIO queue-select fix.

## App Build Fixes (reek)

CL 6192: fixed build issues in codexmagic (None->Nothing, int-list-has),
fishtank (None->Nothing, VM mem), cvmm (chapter deps, WebRuntime stubs),
WebRuntime (dom-get-value, fetch-get-then).

## By the numbers

| Metric | Update 28 | Update 29 | Delta |
|--------|----------:|----------:|------:|
| Foreword modules | 367 | 367 | -- |
| OS/Kernel modules | 137 | 137 | -- |
| App count | 57 | 60 | +3 |
| Plugs | 52 | 54 | +2 |
| Seed size | 2.31 MB | 2.31 MB | -- |
| Seed digest | `E625476A` | `E625476A` | -- |
| x86-64 codegen (8 bench) | -- | 209 insns | new |
| ARM64 codegen (8 bench) | -- | 176 insns | new |
| RISC-V codegen (8 bench) | -- | 141 insns | new |
| ARM64 vs GCC -Os | -- | -24% | beats |
| Battery pass rate | 182/182 | 181/181 | -- |
| Copy-ups | -- | 35 | -- |
| Days | 2 | 2 | -- |
| Agent streams | 4 | 4 | -- |

## What's next

Register allocator improvements (linear scan for named bindings).
Circuits app Phase 2 (selection, move, wire routing). GuiOS window
management. Font hinting.
