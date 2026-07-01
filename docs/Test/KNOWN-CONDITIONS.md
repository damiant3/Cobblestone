# Known Conditions

Persistent record of known build/test conditions that agents should NOT
re-investigate. Last updated: 2026-06-18.

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

### `integer-to-text` produces garbled output for INT64_MIN — DISPLAY ONLY

`__itoa` in `X86_64TextHelpers.codex` negates the input to work with
positive magnitudes. `neg` of INT64_MIN (-2^63) wraps back to INT64_MIN
(signed overflow), so the digit-extraction loop divides a negative
number, producing negative remainders. Adding the CCE digit base (3)
to negative remainders yields garbage bytes that render as Cyrillic
characters in CDX2051 warning text.

Visible as: `value type is -х х0 ҿ҃Ҁѿ0҅.9223372036854775807` where
the low bound should be `-9223372036854775808`.

**Impact**: Display only — the garbled text appears in CDX2051
bounded-integer warnings. Does not affect compilation correctness,
type checking, or codegen. The type-check phase uses INT64_MIN for
`int-ty-default` bounds, so any bounded-integer field assigned a
default-range value triggers the garbled output.

**Fix**: Special-case INT64_MIN before the `neg` instruction, or
handle the negative-remainder case in the digit loop. This is a
compiler codegen change requiring a two-pass seed rebuild. Deferred
until a seed rebuild is needed for another reason.

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

## LOWER Deck Survey — Small Programs with Large IR

The LOWER phase deck height formula (`opening.codex` line 462) is:

    def_count * lower_mul * headroom / 100 + survey_lower_base

This assumes IR size scales linearly with definition count. Programs
with few definitions but deeply nested IR trees (e.g., a single
function that builds a list of 50 sum-type constructors) overflow
LOWER despite being tiny in line count.

**Discovered:** 2026-06-22 (CL 5800). `CompileScript.codex` — 567
lines, 5 definitions — overflowed LOWER. The self-host (30K lines,
2000+ definitions) compiles fine. Splitting into 14 small definitions
fixed the issue because the deck formula sized the budget correctly.

**Workaround:** keep per-definition IR small. Split large literal
constructions across many named definitions. Compose via `ScSequence`
(a single constructor wrapping a reference) rather than `&` (which
creates nested `list-append` IR trees).

**Root fix:** the survey formula could account for IR depth or total
node count, not just definition count. Low priority — the workaround
is simple and the pattern is rare (only affects programs that build
large data structures as literal values).

### Effect-handler clauses ARE counted (CL 2710)

`lin-of`/`consume-of` walk `AHandleExpr` clause bodies (summed, with
clause-param/resume shadowing). A `linear`/`mutable` value used only inside a
handler clause is no longer mis-reported as a leak. The sum is approximate: a
value used in both the handle body and a *conditional* clause can over-count
(rare). Sound-leaning.
