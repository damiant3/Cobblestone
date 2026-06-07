# Dynamic Survey Sizing

**Problem**: The compiler's phase survey multipliers are hardcoded
constants tuned for the self-host (~1.2MB source, ~2 type definitions
per file). Any source with different density (plug compilers, Spark
bundles, app code with many records) hits deck overflow and crashes.
Every time this happens, someone stops working and blames "the
compiler can't handle it." The fix is always trivial (bump a
multiplier), but it wastes hours.

**Goal**: The survey system should never crash on valid source. If
source grows, surveys grow. No manual tuning. No "size limits."

## Current State

- `BuildSettings.codex` defines `survey-check-mul` (400),
  `survey-lower-mul` (300), `survey-headroom` (120%).
- `compile.ps1` passes `-Survey` overrides as a mode-line flag.
- `build-spark.ps1` now passes `check-mul:800,lower-mul:500,headroom:150`
  for Spark (CL 3195). This is a band-aid, not a fix.
- CDX9002 (deck overflow) is an error, not a warning (CL 2574).

## Plan

### Phase 1: Auto-retry on CDX9002 (in compile.ps1)

When compile.ps1 detects CDX9002 in the log output:
1. Parse which phase overflowed from the diagnostic.
2. Double that phase's survey multiplier.
3. Retry compilation with the new survey.
4. Log: "CDX9002 in CHECK, retrying with check-mul:800"

This makes CDX9002 self-healing. No crash reaches the user.

### Phase 2: Measure-and-feed-back (in compile.ps1)

The compiler emits `PM:` phase measurement lines showing actual
deck and bivy usage per phase. compile.ps1 already captures these
in the log. Add:

1. After a successful compile, parse `PM:` lines for each phase's
   deck high-water mark.
2. Write a `.survey` sidecar file next to the source:
   `spark-bundle.survey` with measured multipliers.
3. On the next compile, read the sidecar and use
   `measured × 1.5` as the survey overrides.
4. If no sidecar exists, use defaults. First compile might be
   slow (large headroom) but subsequent ones are tuned.

This is zero-config: compile anything, and the second compile
is perfectly sized.

### Phase 3: Compiler-internal proportional mode

Move the measure-and-feed-back into the compiler itself:
1. On first compilation of a source, use generous defaults.
2. If a phase overflows, double the survey and restart that phase
   (not the whole compilation — just `phase-compact` and re-pitch
   with more deck).
3. After compilation, persist the measured surveys in the CDX
   header or a companion fact.

This eliminates the PowerShell layer entirely. The compiler
self-tunes on every source it sees.

## Immediate Fix (Done)

`build-spark.ps1` passes `-Survey "check-mul:800,lower-mul:500,
headroom:150" -MemMB 4096` for Spark builds. This handles source
up to ~500KB / 10,000 lines with no manual intervention.

## Why This Matters

The survey system exists because bare-metal has no GC and no
demand paging. Every phase must pre-allocate its deck before
running. The survey is the contract: "I will use at most this
much memory." When the contract is wrong, the phase overflows
into the next phase's territory and corrupts data (pre-CDX9002)
or halts (post-CDX9002).

The fix is not "make the numbers bigger." The fix is "make the
numbers track reality automatically." Phase 1 (auto-retry) costs
one extra compile on overflow. Phase 2 (sidecar) costs zero after
the first compile. Phase 3 (compiler-internal) costs zero ever.
