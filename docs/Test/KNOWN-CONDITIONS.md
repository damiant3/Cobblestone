# Known Conditions

Persistent record of known build/test conditions that agents should NOT
re-investigate. Last updated: 2026-06-10.

## Compiler

### Text emitter merges same-body adjacent arms; sem-equiv coupling

The TEXT emitter canonicalizes ADJACENT SAME-BODY `when` arms into a
single or-pattern arm, including across constructors of different
field counts. Source written in the split form fails sem-equiv
(source /= stage1); write the canonical or-pattern form, or route one
case through a helper so bodies differ (see leaf-arg-simple /
ir-is-int-lit in Lowering.codex).

CORRECTION (same day): an earlier version of this entry claimed
mixed-arity or-patterns MISCOMPILE. Wrong attribution. The battery
failures observed alongside the sem-equiv mismatch were present with
and without the or-pattern; they track the leaf-inliner pass being
active in the SELF-COMPILED compiler (stage1), which only the gate
battery tests (build.ps1 sets the battery kernel to stage1). Lesson
recorded for IR-pass work: a pre-gate battery against an
old-seed-compiled SUT does NOT exercise self-application; run the
battery against your own stage1 before declaring an IR pass green.
## Apps

### FishTankPage crashes the compiler at IR emit — SKIPPED IN build-apps.ps1

Compiling the fishtank bundle (`apps/fishtank/FishTankPage.codex` +
FishTankCss + FishTankBridge, ~75 KB) to IR-CCE GPFs the seed
(`!EXC=0d RIP=0x101b18`, runtime-helper region) at 2048 MB and 4096 MB.
The input bundle is byte-identical to one produced before the 2026-06-10
build-script changes, so the crash predates them; the checked-in
`apps/fishtank/web/fishtank.html` is stale output from an earlier
compiler. FishTankPage is a print-line HTML/JS generator with very large
text literals — likely related to long `&` chains / IR size, not the
WebApp quire (fishtank was never ported). `build-apps.ps1` skips it via
its `$Skip` table. Remove the skip after a clean repro + fix.

### cvmm server chapters fail compile with 21 reserved-keyword errors — PRE-EXISTING

`apps/cvmm/build.ps1` (server CDX build) fails with 21 errors:
`CDX1060: 'record' is a reserved keyword` (plus knock-on CDX1000/1023)
in UI--Widget-adjacent code paths of the cvmm bundle. Verified identical
error set with the pre-2026-06-10 depot build script, so not caused by
the shared quire map. The cvmm sources predate `record` becoming
reserved. The cvmm dashboard HTML path (`build-app.ps1`) is unaffected.

## Codegen

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
