# Known Conditions

Persistent record of known build/test conditions that agents should NOT
re-investigate. Last updated: 2026-05-12.

## Codegen

### `record-set` is in-place mutation — CONTROLLED CONCESSION

The `record-set` builtin mutates records in place on bare metal (`MovStore`
at field offset). Safe only because CodegenState is linearly threaded —
single owner, no aliasing. If this invariant breaks (concurrent compilation,
shared state), `record-set` becomes a bug factory.

**Action**: Only use `record-set` on linearly-owned state. When linear types
land, replace with type-system-enforced ownership.

### Interactive signing tool serial I/O race — KNOWN, WORKED AROUND

`tools/cdx-sign.codex` has a timing race on bare metal: the harness
sends stdin + EOF together, and EOF arrives before data is fully
drained from the COM1 ring buffer, causing `read-line` to return
`None`. The HLT→PAUSE fix (CL 751) didn't resolve the root cause.

The build pipeline works around this entirely by using inline sample
generation instead of the interactive tool — the signing key and
content hash are baked into a generated Codex source file as byte-array
literals. The interactive tool remains broken for general use.

**Action**: Do not use `tools/cdx-sign.codex` interactively. CDX
signing works via the inline sample approach in `build.ps1`.
Fix requires either: (a) not sending EOF immediately after stdin in
the harness, or (b) a drain-settle delay in the serial transport.

### `ConOut->ClearScreen` suspected of heap corruption on real hardware — DEFAULT-AVOIDED, REQUIRES CLEAN REPRO

CL 1223 removed `uefi-clear-screen` from the dev-console redraw path
based on indirect observation of heap corruption on Asus/Dell UEFI
boards. The UEFI 2.x spec and the EDK2 reference implementation
(`GraphicsConsoleConOutClearScreen`) only touch the framebuffer +
protocol-internal cursor state — no heap writes. R10 is correctly
saved across the call by `uefi-call-conout` (X86_64Helpers.codex:607).

A more likely root cause for the observed corruption: CL 1197 stores
the heap base pointer at absolute address `0x7580` (firmware-reserved
low memory in UEFI mode), which any firmware handler can scribble
during long-running calls. ClearScreen takes wall-clock time, giving
firmware a longer window to touch `0x7580` — looks correlated, isn't
necessarily causal.

**Action**: Default to row-fill (`uefi-con-fill-row` +
`uefi-con-blank-rows`) for screen clearing. It's safe under both
hypotheses. Don't reintroduce ClearScreen without a clean repro probe
(allocate, pattern-fill, ClearScreen, verify pattern survives) on the
suspect hardware. The real architectural fix is moving the heap
pointer storage off `0x7580`.

## Parser

### `[list] & when ... is ... is ... & [list]` chain mis-parses — FIXED (CL 1526)

Concatenating a list literal with a `when`/`is`/`is` branch
expression and then with another list literal in a single chain
trips the parser into reporting `CDX1023: Expected 'in' after let
bindings` at a downstream line nowhere near the actual problem.
The misreport often points 5–15 lines past the offending construct
(into a subsequent string literal or section header), which makes
the bug very hard to localize from the error alone.

Reproduction:

```codex
foo (b) = [
  "a",
  "b"
] ++ when b
  is X (p) -> ["c " ++ p]
  is Y -> ["d"]
++ [
  "e",
  "f"
]
```

**Root cause** (verified 2026-05-09): `parse-match-branch-body` in
`codex/Syntax/ParserExpressions.codex` calls `parse-expr` (no column
gate) for arm bodies. The binary loop greedily extends the last arm
body across the trailing `++ [...]`, treating it as part of the arm.
The match expression silently absorbs the outer chain's right-hand
side.

**Fix applied (CL 1526)**: changed `parse-expr st3` to
`parse-expr-col st3 col` in `parse-match-branch-body`. The arm body's
binary loop now stops at any operator at column ≤ the match arm's
column. Seed rebuilt with fix baked in; hard fixed point confirmed.

De-workarounds applied in CL 1526: KvStore, Rope, TaskQueue,
AgentAcquisition, VmRunner now inline `when` expressions directly
into list literals and record constructors.

**Status**: CLOSED. The workaround pattern (hoisting `when` to a
helper) is no longer necessary. Inline `when` in lists/records works.
