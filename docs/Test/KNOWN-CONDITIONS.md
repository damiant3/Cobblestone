# Known Conditions

Persistent record of known build/test conditions that agents should NOT
re-investigate. Last updated: 2026-06-10.

## Compiler

### Text emitter merges same-body adjacent arms — RESOLVED (or-group tag)

Was: the TEXT emitter canonicalized ADJACENT SAME-BODY `when` arms
into a single or-pattern arm, because or-patterns desugar to
duplicated branches at parse time and the IR carried no marker of
which branches came from one source arm — re-merging was a same-body
heuristic. Source written in the split form failed sem-equiv.

Fix: MatchArm/AMatchArm/IRBranch carry `alt-group` (the source offset
of the arm's first pattern). Alternatives expanded from one or-pattern
share it; distinct source arms differ; synthesized arms (derived
eq/cmp/show) are tagged -1 and never merge. `find-group-end` in
CodexEmitter merges only equal non-negative groups (body/guard text
equality kept as a second check). Split-form same-body arms now
survive the text round trip verbatim; or-patterns still re-merge.
Regression sample: codex/test/or-pattern-split-arms.codex.

Still true (lesson from the same investigation): a pre-gate battery
against an old-seed-compiled SUT does NOT exercise self-application;
run the battery against your own stage1 before declaring an IR pass
green (build.ps1 does this).
### Self-compilation source-size ceiling — CRASH adding any definition

As of CL 4439 (2026-06-15), the compiler source (1,507,407 bytes
concatenated, ~29,500 lines, ~2,600 definitions) is at its
self-compilation ceiling. Adding even one trivial function
(`foo (x) = x + 1`) to ANY chapter crashes the seed during CDX
build of the new source.

**Symptom**: `!EXC=0d` in `is-in-list` during the frontend phase.
R12/R14 contain CCE text data (e.g., `131c1c101f490f16`) used as a
pointer — a type confusion from an internal table overflow. Survey
raises do not help (tested lex-mul:60, parse-mul:400, scope-mul:80,
check-mul:600, headroom:150). The crash is consistent across files
and chapters.

**Root cause**: Unknown. The `is-in-list` crash with text-as-pointer
suggests a fixed-size lookup table (possibly in scope resolution or
name lookup) that overflows when the definition count increases,
causing a later read to pick up CCE text bytes from an adjacent
allocation instead of a list pointer.

**Workaround**: To add a new function, remove or consolidate an
existing one to keep the total definition count constant. Adding net
new definitions requires finding and raising the overflowing table.

**Impact**: Blocks ALL new compiler features until the table is
found and raised. Known blocked work: prose parameter checking
(CDX1102), x86 gcd further optimization.

### IrLambda variant present in IRExpr but unreachable at emission

LambdaLifting.codex eliminates all IrLambda nodes before IR reaches
the emitter -- every lambda becomes a top-level def + closure site.
The IrLambda variant still exists in the IRExpr type, so the emitter
dispatch has 25 variants but only 24 can appear. All three backends
(x86-64, ARM64, RISC-V) fall through to a default case for IrLambda.
Cleanup: either remove IrLambda from IRExpr (touches every pipeline
stage that constructs/matches it) or add an explicit trap case in the
emitters so a lifting failure produces a loud error instead of silent
zero.

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

### cvmm server build — RESOLVED (CL 3768)

Was: 21 errors starting with `CDX1060: 'record' is a reserved keyword`.
The original entry attributed them to "UI--Widget-adjacent code paths";
that was a mis-mapping — compiler diagnostic line numbers refer to the
full VM input stream (compile.ps1's resolved-cites prelude + bundle),
not the bundle file, and they drift further by source region. Map
diagnostics by CONTENT, not by line arithmetic against the bundle.

The error cap (CDX0001 at 21) hid five layers, all fixed in CL 3768:
1. `let record` bindings (Command.codex) — reserved keyword.
2. Multi-line application continuations dropped at newline
   (CvmmShell widget lists; CvmmDisplay decode-u32-le, which silently
   lost its high-16-bits argument), a missing `if` after `in` in
   dt-is-full-frame, and CvmmServer's opening mixing `<-` into a
   let-chain (needs `in act ... end`, and web-serve-framed for a
   Text -> Text route).
3. Double inclusion: build.ps1 bundled app chapters with PLAIN
   `Chapter: Name` headers; compile.ps1's embedded-chapter detection
   only recognizes `Chapter: Quire--Name`, so it re-pulled every
   intra-quire-cited chapter into its prelude (CDX3001 duplicates).
   Bundlers must render app chapters through Format-CiteChapters.
4. Name collisions with library chapters (names resolve globally
   across chapters): cvmm Capability vs Trust--AgentProtocol (renamed
   ResourceCap), HcPing vs Net--HealthChecker (renamed HcIcmp),
   monitor-new defined in both DisplayManager and Monitor (renamed
   display-monitor-new), hc-type-label vs Net (renamed
   ms-hc-type-label). Plus calls to nonexistent foreword functions
   (text-slice/text-insert-at/text-remove-at -> text-take/text-drop
   forms) and missing StringUtils cites, and stale UI constructor
   names (EdgeInsets -> Edges, StateSet -> StateStyles).
5. Library bug exposed: TrustNode node-msg-capability was
   non-exhaustive after AgentMessage grew MsgAnnotate/MsgVerdict
   (arms added).

`apps/cvmm/build.ps1` now produces cvmm-server.cdx cleanly. Runtime
behavior of the server is still unexercised (no battery test cites
Cvmm). The cvmm dashboard HTML path (`build-app.ps1`) still bundles
plain headers but its three chapters have no intra-quire cites, so
the double-inclusion does not bite there.

### `__record-set` silently fails on high-numbered fields (19th+) — WORKAROUND: sub-records

Discovered 2026-06-15 (CL 4395) while adding `result-dest` as the
19th field of the RISC-V plug's `RvState` record. Setting the field
via `__record-set st "result-dest" value` returns a record where
`.result-dest` still reads the old value. No error, no diagnostic —
the write is silently dropped.

**Confirmed scope:**
- Fails: field 19 of a 19-field record (RvState).
- Works: fields 1–15 of the same record. Fields 1–8 of a 9-field
  sub-record (RvTcoState) accessed via nested `__record-set`.

**Root cause:** Unknown. Likely a buffer size or field-index limit in
the compiler's `__record-set` implementation (`codex/compiler/Emit/`
or `codex/compiler/Core/`). `__record-set` uses the field name for
lookup — the limit is on the field's ordinal position in the record
definition, not on the name.

**Workaround:** Move the field into a sub-record with fewer fields.
Access via `st.tco.result-dest` instead of `st.result-dest`. Write
via a helper that does nested `__record-set`:

```
rv-set-result-dest (st) (d) =
 __record-set st "tco" (__record-set (st.tco) "result-dest" d)
```

**Guidance:** Keep record field count under ~15. If a record grows
beyond that, group related fields into sub-records. When adding a
field to a large record, verify `__record-set` on the new field
actually persists by reading the value back in a test.

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
