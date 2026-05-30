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

### Multi-statement def body kept only the first statement — FIXED (CL 2709)

A function body written as several newline-separated statements at the
same column (the natural way to write an in-place mutation sequence)
parsed only the FIRST statement and silently dropped the rest. It
compiled cleanly and produced wrong results — e.g.

```codex
add-score (gs) (points) =
  gs.score = gs.score + points   -- this ran
  gs.turn = gs.turn + 1          -- silently DROPPED
  gs
```

returned `gs` with `score` updated but `turn` unchanged. Single-statement
bodies were unaffected (a field-assign returns its record), which is why
it hid for so long. The compiler itself never tripped it: every compiler
def body is a single expression (a let-chain).

Fix: `finish-def` / `unwrap-type-for-def` now parse the body via
`parse-def-body-seq`, which sequences trailing same-column statements into
`SeqExpr` (desugared to throwaway `let __seq = stmt in rest`). Sequencing
triggers ONLY when the preceding statement is a `FieldAssignExpr`, so
def bodies with no field-assign (i.e. all compiler code) parse
byte-identically — the CDX hard fixed point is unchanged. Regression:
`codex/test/mutable-def-seq.codex`.

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

## Type System — Linearity / mutable-aliasing checker

The checker in `Types/TypeChecker.codex` (`lin-of` for `linear`, `consume-of`
for `mutable`) is sound for current code but deliberately approximate at a few
edges. Do NOT "fix" these without reading this note — at least one cure is worse
than the disease.

### Borrow-vs-move is inferred from the callee's RETURN type — record-field only

`apply-threads` decides a call consumes its bare mutable argument iff the
callee's return type mentions the mutable record via `type-mentions-mut`, which
walks `RecordTy`/`ConstructedTy` fields, `FunTy` returns, `ForAllTy`/`EffectfulTy`
bodies — but **intentionally NOT `SumTy`/`ListTy`/`LinkedListTy`**. This is not an
oversight. Adding Sum/List recursion (tried, CL 2710) makes `make-token : ... ,
LexState -> Token` look like a thread because `Token` transitively mentions
`LexState` through a list/sum field — but `make-token` only *reads* `s` to
snapshot a position; it borrows. The narrow record-field rule matches the real
threading pattern (`-> CheckResult { state : UnificationState }`) and avoids that
false positive. Consequence (accepted): a function that genuinely threads by
returning `Result`/`List`-of-mutable is treated as a borrow, so such aliasing is
not flagged. False-negative, never false-positive.

### Other known false-negative edges (narrow, no current code affected)

- `peel-returns-n` uses `peel-fun-return`, which returns `ErrorTy` on
  `EffectfulTy`; a call whose signature is effectful at the peeled position is
  treated as a borrow.
- `apply-threads` resolves the call head through `rename-lookup`, but the
  `__mutable-<name>` probe in `check-one-param` uses the un-renamed type name; a
  mutable record threaded across a chapter boundary with renames may not be
  matched. Wants a cross-chapter test.

### Effect-handler clauses ARE counted (CL 2710)

`lin-of`/`consume-of` walk `AHandleExpr` clause bodies (summed, with
clause-param/resume shadowing). A `linear`/`mutable` value used only inside a
handler clause is no longer mis-reported as a leak. The sum is approximate: a
value used in both the handle body and a *conditional* clause can over-count
(rare). Sound-leaning.
