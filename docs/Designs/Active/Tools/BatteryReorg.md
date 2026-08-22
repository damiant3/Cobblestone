# Battery Reorg -- census and redesign

*Status: census complete 2026-07-27; plan steps 1-5 all LANDED 2026-07-27
(steps 1-2 main CL 10881; step 3 main CL 10921 with seed 558; steps 4-5
main CLs 10962 and 10982). Post-fix `-All` ran with zero batch-VM deaths,
~6.6 min under fleet load. Next: step 6, the language axis matrix.

**STEP 6 IS A QUEUE AND NOT A STEP, and "next: step 6" reads as one item
that can be finished** (fester, 2026-08-21, re-measured). You close CELLS of
the language axis; there is no state in which it is done. Steps 7 (dedup,
FIRST PASS) and 9 (the coverage axis, which Damian ruled is a queue
explicitly) are open on the same terms, so the line above names one of three
open things and makes it sound like the only one. Anyone planning off it
budgets for a step and finds an axis.

Re-measured 2026-08-21: **`codex/test/ops` holds 35 members**, against the
seven this entry names as its first landings. The axis has grown fivefold
since 2026-07-27 and nothing recorded it, which is the same shape as the
count this design already warns about -- `real-approx-negate` landed
2026-08-21 (fester, main 18612/18629) after `negate` on a `Real` was found
wrong on all three lanes, and no cell of the matrix predicted it: the corpus
built every negative operand as `0.0 - x`, so nothing ever called the
operator (L-CONSTRUCT). A matrix is only as good as the SHAPES its fixtures
are written in.
Owner: red. Baseline numbers are from ONE instrumented run against seed
AFFD4511; re-measure before quoting them anywhere else.*

## Results so far (2026-07-27, main CL 10881)

Verified `-All` after the harness fixes, under the standing grant, with
fester's install-boot run sharing the box: **440 s total** (phase 1 compile
225 s, containment rounds 136 s, phase 2 run 79 s) against the 1951 s
baseline. Fail set: exactly the four routed fleet regressions; zero harness
artifacts. What landed:

- **Containment rounds.** A batch VM death standalone-confirms the killer
  (exit 4 + !EXC dump) and re-batches every other non-clean result from that
  batch into fresh batch VMs; rounds repeat until one has no death (cap 5,
  then standalone fallback). Converged 632 -> 211 -> 12 -> 0 in three rounds
  with ten standalone confirms, against 683 sequential standalones before.
- **Marker-scan parse.** Native ordinal IndexOf over a Latin-1 shadow string,
  memoized per marker, anchored-at-pos case for markers directly after a
  binary block. Crashed-batch parse: 17-23 min -> sub-second; 300 MB healthy
  batch: 0.9 s (synthetic). Kept-log lines capped at 2000/test. Beware
  PowerShell's binding of 5-arg `[string]::CompareOrdinal`: ~2 s PER CALL;
  use `Substring -ceq` (measured, cost a full battery run to find).
- **CDX9002 rule.** A deck overflow reported from a batch session is always
  confirmed standalone: foreword-all-compile hits LOWER's deck floor
  in-session and compiles clean alone (26 s, same seed and flags).

**The killer list grew.** Containment surfaced four MORE batch-killers the
old retry design had been absorbing silently: `quotes-gate`, `quotes-parse`,
`prose-consistency` (all compile CLEAN standalone) and `quotes-corrupt`.
Ten total.

**Revised defect hypothesis.** The original blob-reader-past-end-of-unit
guess is out. The signature -- same RIP every time, CCE text bytes used as a
pointer, crashes ONLY in a batch session, clean standalone -- fits an
UNINITIALIZED-MEMORY read: a fresh VM's heap is zero-filled (the read is
benign), a session's heap holds prior compiles' debris (the read is a garbage
pointer). Candidate sites: the quotes/works parsing path
(`parse-works`/`join-work-lines` with the WORK line's untrusted line count)
and whatever prose-smoke/prose-consistency share with it. Diagnosis is the
lane's next arc; the fix is compiler source (gate, token, likely seed).

## Why

`-All` was believed to cost about an hour and be functionally useless as a fleet
instrument. The battery is the product of an additive process never
systematically reviewed. Damian's direction (2026-07-27): reorganize and
refactor -- much faster, better reporting, far more language-feature coverage,
duplication eliminated, smarter execution. Two-week push context: the battery is
what makes "the language works" a claim with a runner behind it.

## The baseline, measured

One `-All` run, instrumented (per-test run_ms, per-batch resolve/vm/parse split,
phase stopwatches). 1282 tests, 38 skipped, 1244 compiled, 1057 run.

| Phase | Wall | What it actually was |
|---|---|---|
| Phase 1 batch compile | 1392 s | resolve ~5 s/batch, VM 2-12 s/batch, parse 4-67 s healthy but **1011 s and 1374 s on the two crashed batches** |
| Phase 1a retry | 479 s | **683 of 1244 tests** re-compiled standalone, sequentially, because 8 batch-VM crashes orphaned their streams |
| Phase 2 run | 76 s | 1057 individual VM boots, 8 parallel; median 489 ms, p90 576 ms, max 7.4 s |
| **Total** | **1951 s (32.5 min)** | |

**The compile is nearly free.** A batch VM compiles ~155 tests in 2-12 seconds.
The hour-class cost is entirely harness pathology:

1. **Batch-killer tests.** Five `quotes-*` error tests and `prose-smoke` crash
   the batch VM deterministically (`!EXC=0d`; the quotes five all at
   `text-starts-with+0x2B` walking CCE text bytes as a pointer). Every one
   passes standalone -- correct diagnostics, clean run -- so this is a
   REPL-batch-session defect, not a test defect. Working hypothesis: the
   `%%QUOTED-WORKS%%` blob reader consumes past end-of-unit into the next
   test's bytes; `prose-smoke` looks like a downstream victim of the same
   session corruption. The quotes tests landed 2026-07-14, one day AFTER the
   last recorded clean full battery (2026-07-13), so plausibly every `-All`
   since has paid this. Each crash orphans the rest of that batch's stream
   into exit 99.
2. **The parse pathology.** The batch output parser is a PowerShell byte loop.
   On a healthy batch it costs 4-67 s; on a batch whose VM died mid-binary it
   walks megabytes of truncated binary one byte at a time: 17 and 23 MINUTES
   on the two worst batches. This alone was most of phase 1.
3. **The retry design.** One crash triggers re-running every non-clean result
   standalone and sequentially -- 683 tests this run.

**What is NOT the problem:** phase 2. A VM boot+run is ~0.5 s. 1057 boots cost
76 s wall at 8 jobs.

**Ceiling estimate:** with crashes contained and the parse fixed, `-All` on this
box is roughly resolve 5 s + VM ~15-30 s/batch + parse seconds + phase 2 ~80 s,
i.e. **~3-5 minutes**, before any consolidation work.

## What the run caught, which is the thesis

Four red tests, all same-day fallout of fleet CLs, none gated by anything:

| Test | Cause | Routed to |
|---|---|---|
| ~~`uefi-console-test`~~ | CDX3002 on `uefi-scan-*`: UefiKey contract rework (10550) did not update this test | fester -- **FIXED**, rewritten against the KeyInput contract; ablation on the ctrl-alt-del validity gate fires on exactly one line |
| `annotation-under-header` | its `.diag` pins CDX6010, no longer emitted after the `@` removal (10784) | blu |
| `sidecar-load-test` | expected `codex.annotations/...` path; the sidecar path fix moved it to `annotations/...` | blu |
| `wave3-test` | expected records the PRE-FIX ConsistentHash ring (10728 fixed the 993-of-1000 skew); `k100`/`k200` legitimately moved | val |

A battery cheap enough to run after every fleet arc would have caught all four
the day they landed. That is what this reorg buys.

## Static census

- 1282 tests: root 467, errors 163, apps 325, forewords 305, lib 22.
  `-Fuzz` names `codex/test/fuzz`, which does not exist (dead switch).
- Assertion coverage is healthy: 26 tests total lack any assertion sidecar.
- Cites: 265 tests cite nothing, 773 cite 1-2, 38 cite 11+
  (`foreword-all-compile` cites 418).
- BVT is 59 tests (docs have said 10 and 16; count drift).
- 16 smoke bundles exist; the consolidation trick is proven.
- forewords/ is 305 one-cite compile smokes that each also boot a phase-2 VM
  to print a constant.
- 47 test-related scripts in `build/`, ~7000 lines, accreted.

## The plan, in order

1. ~~**Contain crashes (harness).**~~ Done, main CL 10881 (see Results). The
   runner already had a 60 s host wall clock in `test-run.ps1`.
2. ~~**Fix the parse (harness).**~~ Done, main CL 10881 (see Results).
3. ~~**Fix the REPL batch-session defect (compiler).**~~ Done, main CL
   10921, seed 558 (2026-07-27). Root cause: `__text_split` omitted the
   list capacity header at [-8] that every other list builder writes
   (`__list-with-capacity`, `__linked_list_to_list`, `__list_concat_many`)
   and whose SIGN the inlined `list-at` reads to pick the inline or
   indirect element path. The header slot was inherited heap: zeros on a
   fresh VM (positive, inline, correct by luck), 0xCD poison in a session
   after any failed compile (negative, "indirect", element base read from
   inside element 0's bytes -- the CCE-bytes-as-pointer #GP). Diagnosed
   via two-unit pair repro (any failing unit + quotes-forged), codex-vm
   -break/-watch probes, and a memory diff at parse-works entry:
   byte-identical machine state except [list-8]. All ten killers verified
   clean in one batch session against the new seed. The REPL loop's
   deliberate between-units poison sweep is what turned this latent read
   into a deterministic crash: the instrument worked.
4. ~~**Reporting.**~~ Done, main CL 10962. Every run writes
   `_results\_rollup.txt` (per-category counts, slowest-10, one actionable
   line per failure) and a delta against `test-output\last-run.json`.
5. ~~**Tiers with honest names.**~~ Done, main CLs 10962 (tiers
   `-Tier lang/lib/fw/apps/hardware/oracles/all`, dead `-Fuzz` removed) and
   10982 (oracles pinned into the gate after test-bvt, ~2.4 s, and into
   `-All`; collections stay author-owned).
6. **Language axis matrix. IN FLIGHT 2026-07-27.** New coverage organized by
   axis (operators x types x sign x value-vs-branch position, bounded modes
   and `__narrow`, CCE boundaries, linear/mutable probes, vector lanes, unit
   types, remainders), entering as smoke-bundle members -- compile is free,
   boots are not. The oracle fold-in landed with step 5. First landing:
   `codex/test/ops/` is the operator-correctness-by-operand-type home (lang
   tier; scanned by `test.ps1` and the cross-arch batch). reek's nine
   operator pins moved in with names unchanged, so the delta stays stable.
   `bounded-modes-smoke` is the first new member: the overflow-mode sign
   lattice (wrapping and clamping bands against over-range positive,
   under-range negative, at-bound, far out). Before it, clamping was tested
   at one cell and wrapping at none. Its probe found two bounded-mode
   defects (band-narrower-than-width wrapping does not wrap at the band and
   breaks the prover's field range; clamping outside i32 silently drops the
   saturate); both routed to reek's lane, and the bundle deliberately pins
   only the settled cells. `vec-lanes-smoke` is the second member: lane
   distinctness for vector arithmetic and indexing (extract per lane, all
   four operators per lane, horizontal sum), the half of the splat-only
   gap that vec-mask-hazards does not cover. `int-rem`,
   `div-negative-pow2` and `real-negate` moved into the axis with names
   unchanged (bvt.ps1 path updated for real-negate). The text-ordering
   axis completed same-day across three lanes: reek's refusal pin
   (`errors/text-order-refused`) and positive control
   (`ops/text-order-allowed`), and val's collation vectors shipped with
   the Collate chapter and moved into ops/ (`collate-order` -- both
   orders side by side, the zebra/Apple case cell, accent folding, a
   seeded key sort).
7. **Dedup. FIRST PASS 2026-07-27.** Method: hash census (zero
   byte-identical sources; identical-expected groups are mostly
   coincidental pass-counts), then name-family reading. Acted:
   - DELETED `list-append-perf-N8-L7`: a generated probe from the
     Mountain-stream bounds campaign (in via CL 4744) -- eight clone
     functions, a `check` helper nothing calls, `opening = 0`,
     expected `0`. It could not fail and asserted nothing.
   - DELETED `mqtt-packet`: its four wire-length assertions are
     strictly implied by `mqtt-encode`'s exact-byte pins
     (connect/publish/subscribe full byte vectors, pingreq and
     disconnect length==2).
   - MERGED `coap-encode` into `coap-packet`: packet pins exact
     lengths (the stronger assertion; encode's builder checks were
     list-length > 0, the blindness the CoAP interop story names);
     encode's one unique assertion, the three content-format
     constants, moved into packet. coap-parse and coap-block untouched
     (decode and blockwise are different subjects).
   - Judged NOT duplicates and kept: the identity-* five (distinct
     kernel behaviors; merging spawn/capability tests would share one
     boot's process table across assertions that need isolation), the
     ui-*-test twelve (they compute values; the forewords/ files are
     the compile smokes), c64-sid vs c64-sid-stream (synthesis vs HDA
     streaming), the narrow-proven six (prover pins by site, an axis
     family).
   - Moved `real-neg-neg`, `real-approx`, `real-bitcast` into
     `codex/test/ops/` (Real-operand pins; `real-cert` stays -- the
     Real there is English, it is an X509 test).
   - RESOLVED (second pass, same day): `db-test` vs `db-full-test`.
     The assertion-level diff found 8 of db-test's 13 assertions
     subsumed in stronger form (richer filters, txn with isolation
     levels and counts, locks with release, deadlock with a negative
     control, WAL with an abort case) and FIVE unique: the
     `catalog-table-row-count` accessor, a NULL column threaded
     through insert-query-render (every row in full's three tables was
     fully populated), the global aggregate (`RelGroup` with an empty
     group-by list), `heap-delete` visible through a following query,
     and `catalog-drop-table` with an exists-after check. The five
     folded into db-full-test as Tests 49-52 (48-line control prefix
     byte-identical before the four new lines were recorded);
     `db-test` deleted.
   - STILL DEFERRED: the build/ scripts are a separate dedup arc,
     scoped in step 8 below.
   Every survivor must be able to fail.
8. **The build/ script dedup arc. SCOPED 2026-07-28, not started.**
   Measured: **117 .ps1 files in build/** (the "47 test-related, ~7000
   lines" this doc carried was one reading of a subset; the family sweep
   below is the whole directory). Names are a proven weak instrument
   here, so each family row states the QUESTION a reading must settle,
   and two data points are already verified by content rather than name:
   `measure-survey.ps1` explicitly exists "to inform survey heuristics"
   and the survey system was deleted 2026-07-07 (retirement candidate);
   `test-boards.ps1` and `boards-test.ps1` are near-identical names with
   DIFFERENT subjects (Renode cross-arch hello-world smoke vs the
   nine-board `-board-mmio` driver battery on codex-vm) -- a rename
   candidate, explicitly NOT a merge.
   - Core harness (test.ps1 812 lines, test-compile-batch 238, test-run
     107, bvt 246, vm-config 447, check-sidecars 62, audit-skips 175,
     test-gui 314, test-app-gui 63): the product; no dedup question.
   - Cross-arch family: VERDICT SETTLED 2026-07-28 by the side-by-side
     the previous entry asked for. Same 8-10 test subset (`-Filter
     list`, riscv64, QEMU runtime on both): `test-cross-batch` compiled
     10 tests in 14 s across 8 parallel per-test slots (~0.35 s/test
     effective) against `test-cross-fast`'s 12 s through one
     seed-VM REPL + one plug VM (~1.3 s/test SEQUENTIAL by design --
     its memory-safety constraint is one compile VM per agent). At the
     ~460-test battery that is ~160 s vs ~600 s: the problem -fast
     solved (expensive per-test VM boots) was solved better by
     parallel slots, so there is nothing to fold. **The harnesses
     agree on results** (same 4 passes, same 5 shared failures), so
     retiring loses no signal. RETIRE `test-cross-fast` +
     `test-cross-compile-batch` (its orphaned subroutine): landed;
     both are absent from `build/` (verified 2026-08-05). One set-discovery quirk recorded: -batch found 14
     eligible to -fast's 13 on the same filter (-batch also scans
     `ops/`).
     The run also surfaced a real cross-parity finding, routed to reek
     (red-workplan outbox): the riscv lane is red on a list cluster,
     all truncated-output shapes, and the plug COMPILES list-intrinsic
     tests clean instead of reporting `[UNSUPPORTED]` -- the
     refusal-by-design arms `.cross-refusal` exists to pin are absent
     for the list family, so `list-pattern` (documented x86-only)
     reads as FAIL_OUTPUT instead of PASS_REFUSED.
   - Brotli tooling (11 scripts, ~2000 lines). READ 2026-07-28 and the
     scoping guess was WRONG in the keep direction: these are mostly
     load-bearing, not dead one-offs. `dict-extract` + `dict-chapter`
     are the GENERATORS of the 122,784-byte `BrotliDict.codex` and its
     re-proof path against .NET (a person does not hand-edit that
     chapter); `xform-extract`/`ctx2-extract` are the same shape for
     the transform and context tables; `ctx2-cases`/`xform-cases`
     build the foreign streams our encoder cannot emit, which is the
     L-ORACLE coverage for the decoder's mode-2 and transform paths;
     `hdr-probe` is a live refusal-localization diagnostic. The one
     clean retire candidate is `brotli-dict-probe`, the feasibility
     probe superseded by the extractor it spawned. All eight reference
     deleted BACKLOG rows in their comments; fix the references when a
     script is next touched, no sweep.
   - Store/disk scripts: READ 2026-07-28, verdicts below. The retire
     deletions landed: `run-with-disk.ps1`, `test-disk-persistence.ps1`,
     `test-disk-boot.ps1`, `test-store-real-file.ps1`,
     `measure-survey.ps1`, `test-cross-fast.ps1` and
     `test-cross-compile-batch.ps1` are all absent from `build/`
     (verified 2026-08-05).
     - KEEP `test-quote-from-store` (79): a store written FRESH by one
       VM then read by the compiler (`compile.ps1 -DiskFile`). Its own
       comment says why the battery cannot do it: `test-run.ps1` copies
       the disk to a throwaway temp, so the in-battery
       `quote-from-store` runs against a FROZEN fixture and cannot see
       writer-side format drift. This script is the crossing test.
     - KEEP `test-store-append` (76): pins the disk-load-vs-disk-init
       append defect with two works through the real tools; one work is
       exactly the case where appending and replacing agree.
     - KEEP `test-compile-from-store` (80): two compiles with a VM boot
       between, stage-one output is stage-two input; its prose already
       records why it is a script and not a battery test, and its
       stage one (`checkout-emit`) is separately in the BVT.
     - KEEP `test-disk-compile` (107): the only driver of the
       compiler's DISK compile mode, which is live
       (`opening.codex:1891`, `emit-from-disk`). The battery feeds
       every compile over serial, so without this script DISK mode has
       no runner at all.
     - MERGE candidate: `test-store-real-file` (52) into
       `test-store-append` as its first stage -- append's
       per-body containment plus length-sum nearly subsumes the
       single-file byte-exact equality; fold the exact-equality
       assertion in and delete the file (the coap-encode shape).
     - RETIRE candidates: `run-with-disk` (41) + `test-disk-persistence`
       (66), a QEMU-era pair verified by content. run-with-disk passes
       QEMU `-drive file=...` syntax as ExtraArgs, which codex-vm does
       not take (`-disk` is its flag), and its only caller is
       test-disk-persistence; test-disk-persistence's own comment
       documents its mechanism as a retry loop around a QEMU/WHPX IDE
       flake ("~40% of boots read correctly"), the
       workaround-outliving-its-condition shape. Its subject,
       persistence across boots, is held in-battery by the disk-facts
       write/read pairs and `facts-partition`.
     - `test-disk-boot` (69): RETIRE candidate, settled 2026-07-28.
       Zero callers; its default input `build-output\Codex.img` is an
       artifact nothing builds (the live image is `seed\Codex.img`);
       and run against the live image its codex-vm path fails to start
       at all ("FAIL: codex-vm did not start" -- it hands a GPT image
       to `Start-CodexVmRun -Kernel` without `-uefi`). The img-boot
       subject is held by fester's `install-boot-test.ps1`. If a CHEAP
       one-minute img-boot smoke is ever wanted, write it fresh against
       `-uefi`; this script is not it.
   - Checks: CONFIRMED INVOKED 2026-07-28 by grep over build.ps1 --
     p4-stale-check, check-constants, check-effect-vocab,
     check-sidecars, check-cdx-registry, check-facts-guid,
     check-doc-counts, check-plug-types, and check-cross-smoke in the
     cross leg. **The old roster was wrong about two: `check-apps` and
     `lint-unused-cites` are invoked by NOTHING** -- not build.ps1, not
     any script. `check-apps` deliberately so (it asserts runtime
     invariants across the 74 generated app pages, and Damian DECLINED
     coupling apps to the gate); `lint-unused-cites` is an on-demand
     lint with -Src/-All. Both KEEP as on-demand instruments; neither
     is part of the gate and this doc stops saying they are.
   - Interop/serve/oracle harnesses (tls/mqtt/mqtts/https/coap x2,
     cdx-serve, quote-from-peer, registry-locate/probe, gguf-foreign,
     plug-oracle, agent-bundle, oracle-scalar/vector/cce, wcet-validate,
     boards-test, install-boot-test): author-owned instruments,
     on-demand by design; out of scope for dedup.
   - Probe singletons: READ 2026-07-28, verdicts settled. Eight KEEP,
     each with a live subject the battery cannot express, one RETIRE:
     - KEEP `deck-floor-test` (starved floors name themselves: -Decks
       5/20/40 must raise CDX9002 from the right phase, 100 must
       compile; subject live, both controls present).
     - KEEP `inline-fire-test` (does the single-caller inliner FIRE --
       visible only in the symbol map; the in-battery
       `inline-single-caller` pins no-miscompile and nothing more; the
       must-SURVIVE capture arm is the discriminator).
     - KEEP `list-ceiling-test` (CDX9004 at the literal's own span,
       over and at the ceiling; the 447 KB fixture is generated, which
       is why this cannot be an in-depot test; CDX9004 live in 7
       compiler files).
     - KEEP `test-growth` (ballast pingpong; the grown-source deck
       hazard class is live and this is its only runner).
     - KEEP `test-exception-handler` (all five exc-* samples exist;
       the `.fatal` class asserts nothing at runtime by design --
       ExaminersAssay records why -- so this script is the only
       automated reader of the dump FORMAT, incl. the deep-frames
       walk floor and the OUT OF MEMORY path).
     - KEEP `stress-sweep` (flake hunter looping test.ps1; carries
       -ApprovedBy so Damian's battery approval gate is built in).
     - KEEP `ablate` (IR-pass ablation with bench instruction counts;
       reek's instrument. Its home campaign -- the middle end -- is
       closed as a measured negative, so it is an instrument whose
       next customer is unknown; reek's call, not the arc's).
     - KEEP `ablate-doctrine` (blu's LESSONS harness, deliberately
       unrun in this workspace -- every agent here has read the
       answer key; recorded in blu's workplan).
     - **RETIRE `test-uefi-heap`: its subject is GONE.** It drives the
       compiler's internal `IMG fat16 uefi [heap=N]` mode and asserts
       `WD:UEFI-HEAP` / `WD:UEFI-HEAP-WARN` lines; measured 2026-07-28,
       no such mode and no such string exists anywhere in the tree --
       not in `codex/compiler` (the only "Img" is the plug-quire
       effect-exempt list), not in `codex/plugs/img`. Its own header
       said "when the compiler's IMG mode is eventually removed, this
       test will move to the IMG plug"; the mode was removed and
       nothing moved. The script cannot pass against any kernel in the
       depot. If a UEFI heap-sizing check is ever wanted again it must
       be written fresh against the plug or build-img.ps1. Deleted,
       main CL 11319.
   Method when the arc starts: read each candidate, verify its subject
   against the tree, delete or merge in small CLs, and record verdicts
   here the way step 7 records the judged-kept list. A script kept must
   have a subject that exists; a script deleted must have its unique
   assertion either preserved or shown subsumed.

9. **The coverage axis. DESIGNED and MEASURED 2026-07-28.** The library
   half of coverage: which foreword chapters are never asked an answer.
   The language axis (step 6) covers the compiler's own semantics; this
   axis covers the 429 foreword chapters, and it is a QUEUE, not a gate
   -- Damian's ruling stands (no coverage machinery in the battery;
   re-derive on demand, by hand).
   - **Method, re-derivable in one rg pass plus aggregation:** rg
     `cites (\w+) chapter (\w+)` over `codex` and `apps`, key each hit
     by (quire, chapter) against the foreword file list (directory
     `core` maps to quire `Foreword`; others are the last segment). A
     chapter is SMOKE-ONLY if its only citing tests are
     `codex/test/forewords/` or `foreword-all-compile`; its consumer
     weight is the count of citing files outside `codex/test` and
     `codex/foreword`. Both are proxies and this run confirmed both
     biases on its own output: citation is an upper bound on
     consumption (the mechanical top four included `SearchBar`,
     `FilterableList`, `FontGen`, `TrueTypeWriter` -- all measured
     DEAD by val's callee grep), and a dependency-chain citation is
     not answer coverage (counting `foreword-all-compile` as a test
     reads 429 of 429 covered).
   - **Measured 2026-07-28:** 429 chapters, 298 cited by a test
     outside the smokes, **131 smoke-only**, of which **23 carry a
     non-test consumer citation** and 108 carry none (parked --
     Damian's bar: no consumer, no urgency). Not directly comparable
     to the 181 of the 2026-07-27 hand census: that one judged
     whether the citing test COMPUTES, this proxy counts any
     non-smoke citation, so it reads lower on method alone -- and the
     fleet also genuinely closed chapters in between. Treat 131 as
     the order of magnitude, re-derive before quoting.
   - **The queue rule:** rank the smoke-only-with-consumer set by
     consumer count; filter by callee grep (val's rule -- grep the
     chapter's defined names, never the citation table) and by
     recorded verdicts; within rank prefer chapters with a published
     external answer (L-ORACLE). The filtered live top at this
     measurement: `gpu/DeviceMath` (34 gpushow kernels -- every
     `[Device]` kernel's math library, zero answer tests);
     `ui/RichText` (7), `ui/Markdown` (6), `ui/Editor` (3),
     `ui/Window` (2), `ui/Canvas` (2) -- val's lane, routed via
     red's outbox; `foreword/RankedTextSet` (4 plug emitters) and
     `foreword/SourceDefWire` (2, repo protocol wire format) --
     unowned. `shell/PowerShellEmit` ranks first at 48 and is PARKED:
     every consumer is a `codex/build/*Script.codex` shadow
     generator, and those do not produce the hand-maintained
     `build/*.ps1` (reek's trap list), so the weight belongs to a
     parallel system, not the product. The ai/AssetForge cluster
     (ImageTo3d, DiffusionPipeline, UNet, SafeTensors, Tokenizer,
     TextEncoder) waits on whether assetforge is on the bar;
     `ImageTensor` is already measured fine (val) and needs no
     re-audit.
   - **Where the tests land:** `codex/test/lib` for pure answers,
     `codex/test/apps` where a machine is needed; names stable so the
     run-over-run delta holds; external oracle first and sabotage
     discipline per val's method notes. The census TSV is regenerated
     at need and never committed: a committed roster is the BACKLOG
     failure wearing a new name.

10. **Cross-lane honesty at the new scale. OPENED 2026-07-28, first
    full measurement taken.** The reorg tripled the cross-eligible set
    (153 -> 492 listed, 432 run) and the first full run of both lanes
    replaced the disputed parity numbers (ExaminersAssay has the
    figures): arm64 358 pass / 50 fail with ZERO arm64-only failures;
    riscv 259 pass / 149 fail. Three work items fall out, in order:
    - **The riscv wrong-value cluster is routed, not mine.** ~90
      riscv-only FAIL_OUTPUT rows answer wrong VALUES on coverage the
      old lane never ran, and every probed one passes on arm64, which
      localizes them to the riscv plug. reek's lane via red-workplan
      outbox; `test-output-cross/riscv64_cross_results.md` is the
      worklist.
    - **Run budgets need a quiescence exit, not a bigger wall.** The
      QEMU leg kills every guest at a flat 3 s and the Renode leg
      sleeps a flat 10 s ("cannot be ended early", per the phase-2
      comment). Probed: `av-codec-test` had produced 24 bytes at 15 s
      and was still going -- alive, starved, reported FAIL_OUTPUT
      `act=[]`, and never retried because empty-at-FAIL_OUTPUT is not
      the retry's silent-lane class. A run that ends when the uart
      goes quiet (non-empty and unchanged for ~1.5 s) makes the
      budget a ceiling instead of a sentence and would also cut the
      arm64 Renode run phase (783 s of mostly flat sleeps). Design
      before code: the retry classifier must learn the
      starved-vs-dead split or it will keep filing both as
      deterministic wrong answers.

      **DESIGNED 2026-07-28, and the first design was refuted by its
      own probe the same day.** Design one was a quiet window: exit
      when the uart is non-empty and unchanged for 1500 ms. Measured
      against `av-codec-test` on riscv64/QEMU it filed FAIL_OUTPUT at
      2.3 s: the test prints line 1 in about a second, then computes
      for 15 s or more before line 2, so the window read a
      between-lines compute gap as termination -- the same
      misclassification as the flat wall, arriving sooner, and a new
      threat to any currently passing Renode test with a slow gap.
      Silence is not termination. The instrument pointed at silence
      answers "is it printing", not "is it done" (the L-ORACLE shape).

      The exit signal is COMPLETENESS instead, and it was in hand all
      along: `.expected`. Phase 2 only runs tests that have one, so
      the runner knows how many lines a finished answer has. A run
      ends when its filtered output (same normalization as the
      compare: CR stripped, HEAP:/WD:/STACK: lines dropped, trailing
      blanks trimmed) reaches the expected line count and ends in a
      newline -- a complete answer, right or wrong, is a real one and
      is compared on the spot. The ceiling (`-RenoTimeout`, now
      honestly a ceiling) governs everything else. There is no quiet
      knob: fewer parameters, and nothing for a compute gap to fool.
      The output channel differs per leg because the file backends
      differ:
      - QEMU: `-serial file:` writes the log live, so the poller
        reads the file share-tolerantly (250 ms cadence) and kills
        the guest at completeness. The flat `$qemuTimeoutMs = 3000`
        is retired; QEMU shares the ceiling.
      - Renode: `CreateFileBackend` provably does not put a byte on
        disk before teardown (measured, recorded in the phase-2
        comment), so the file is not the channel. The design replaces
        it with `emulation CreateServerSocketTerminal` (raw mode,
        third arg false) connected to `uart0`; the resc no longer
        starts or quits the machine. The host connects to the socket
        FIRST, then issues `start` over Renode's own stdin, so no
        boot-time byte can be lost to the connect race; bytes
        accumulate host-side and are written to `uart.log` by the
        harness itself, keeping the downstream compare identical. At
        completeness or ceiling the host issues `quit` and kills
        only a process that ignored it. Per-slot port collisions are
        avoided by giving every test its own port (base + index).
      The classifier's classes, and what the exit condition change
      makes honest:
      - Complete output: a real answer. Compare; PASS_EXPECTED or
        FAIL_OUTPUT. FAIL_OUTPUT is never retried, unchanged. A
        passing test now exits at its answer, not at a wall or a
        window after it.
      - Zero bytes at ceiling: dead-silent, `no uart output`,
        retried once alone (the existing contention class). An empty
        log is filed here too, not as FAIL_OUTPUT `act=[]`.
      - Incomplete output at ceiling: NEW class `FAIL_STARVED`
        ("incomplete at ceiling: L of E lines, N bytes").
        Contention-shaped first (emulation under 8-deep load is
        slow, not wrong), so it joins the retry set: once, alone,
        same ceiling. Incomplete alone means genuinely over budget
        and it stays FAIL_STARVED with `still incomplete alone` -- a
        budget verdict, visibly distinct from a wrong answer, which
        is the whole point. A guest that deterministically stops
        mid-answer lands here rather than in FAIL_OUTPUT; the row's
        byte and line counts are what make that case investigable,
        and the eligibility item below is where the heavy-compute
        tests that can never finish under an honest emulation budget
        get ruled out rather than re-billed every run.
      Scope: `test-cross-batch.ps1` both legs. The gate leg
      (`check-cross-smoke`) keeps its 3 s budget: its two tests
      answer in under a second and it already has the silent-lane
      retry. `test-cross.ps1` single-runner parity is a follow-up,
      not this CL.

      **IMPLEMENTED same day, every class fired by probe** (an
      unfired guard is worth what no guard is worth): factorial
      passes at completeness in 0.3 s on QEMU and 3.1 s on the
      Renode socket path (was 3 s and 12.6 s flat); `av-codec-test`
      riscv64/QEMU reports `FAIL_STARVED incomplete at ceiling (10s:
      1 of 5 lines, 24 bytes), still incomplete alone` -- the same
      24 bytes the original probe saw; `capability-doors`
      riscv64/Renode reports `no uart output, still silent alone`
      through the socket path; `cms-spread` riscv64/Renode turns out
      to answer COMPLETELY and wrongly (`exact: 0` for `exact: 38`,
      the wrong-value-cluster shape) in 3 s, firing the
      never-retried direction with real data (`run 0/0`). The
      full-battery timing win lands with the next full cross run;
      re-measure, do not project.
    - **Eligibility is a design, not a filter flag.** The shared-51
      fail set is dominated by subjects the cross lane cannot host:
      machine-sidecar tests (`.disk`/`.disk2`/`.vmargs` -- 
      block-select-drives, factlog-layout, fat16-overwrite), kernel
      capability machinery (the cap-* family, fs-* servicers), and
      heavy compute that no honest budget saves under emulation.
      Decide per class: exclude machine-sidecar tests from the cross
      scan the way `.smp` already excludes from single-core; grow
      `.cross-refusal` where the plug should refuse; and only then
      call what remains parity defects. Probed dead-silent and
      real: `capability-doors`, `cms-spread` (0 bytes at 15 s).

      **DESIGNED 2026-07-28, from the measured shared set.** The
      shared fail set of the two full tripled-battery runs is 50, not
      51 (re-measured; L-COUNT), and every row was classified by
      reading rather than by name -- which mattered twice: two rows
      that pattern-match to eligibility classes are defects
      (`scope-try-region` is a language-level `trying`-scope
      miscompile pin, `network-effect` is headless by design), and
      the keys family turned out to carry a machine sidecar nobody
      had listed. The classes and their verdicts:
      - **Machine-sidecar, mechanical exclusion (scan change).** A
        test whose fixture is the x86 machine names itself with a
        sidecar: `.disk`, `.disk2`, `.disk-src` (attach a compiled
        CDX as disk), `.vmargs` (codex-vm flags), `.keys` (scancode
        timeline for `-keys-file`). None of these can exist on a
        Renode/QEMU cross board, so the scan skips them the way
        `.smp` already routes multi-core tests elsewhere, naming the
        sidecar in the skip reason. No per-test file to maintain.
      - **Kernel machinery, per-test `.no-cross` (14 files).** The
        cap-* seven (capability words read from the process table
        the kernel checks on syscalls; boot-table bits from
        X86_64Boot.codex), the spawn/process family (nested-spawn,
        spawn-reuse, proc-state-running, process-exit-status: the
        process table IS the subject) and the network-scope trio
        (the runtime admission gate reads the same capability
        machinery). The cross lane boots a bare runtime with no
        kernel; a test whose subject is the kernel has no subject
        there. `.no-cross` with the reason in the file, consistent
        with the existing 17.
      - **Heavy compute: ruled AFTER the next full run, not now.**
        FAIL_STARVED will name the alive-but-over-budget rows
        honestly (db-full-test, the crypto cluster, edvector,
        engine-software-render, collate-order are the candidates);
        each then gets a budget ruling or a `.no-cross`. One known
        limit, recorded rather than solved: a test that computes a
        long time before its FIRST byte (`ttt-perfect`) is
        indistinguishable from dead at the ceiling -- if its verdict
        matters, raise the ceiling for one run and watch.
      - **The residue is defects, and it is routed.** Seven rows fail
        standalone on BOTH cross lanes with complete, deterministic,
        identical-or-equivalent wrong answers, and pass on x86 --
        the shared-lowering fingerprint (L-SUSPECT), distinct from
        the ~90 riscv-only plug rows: `unit-show` and
        `unit-pattern-lit` (unit Text answers a pointer / never
        matches), `int-pow` (answers a
        pointer-looking value), `int-min-literal`,
        `hal-peripheral-linear` (SAME wrong value 536872972 on both
        arches), `scope-try-region` (the fallback's global Text
        prints empty), `network-effect` (diverges past line 3).
        Routed to reek via red-workplan outbox 2026-07-28. Three rows
        were deleted from this list 2026-08-18 (reek) after measuring
        them green: `real-saturating` and `unit-real-compare` pass on
        BOTH lanes, and `real-compare-negative` passes on arm64, so
        none of the three fits this list. The other seven were NOT
        re-measured; do not read their presence as current.
        `ui-orchestrator-test`'s compile exit=3 was separate and is
        CLOSED same day, four defects deep: the IR-emit monolith
        (fixed by print-text + streaming, main 11498/11500), the
        codex-vm 16 MB capture and input caps (main 11500), and the
        shared plug tokenizer's mutual-recursion stack overflow
        (this arc). PASS_EXPECTED on arm64 end-to-end; honest
        FAIL_STARVED on riscv (the lane's known slowness). Its
        `.cross-budget` prices both ceilings (run 90, compile 600).

      **RE-MEASURED 2026-07-28, full run per lane under the new
      budgets and eligibility (both landed same day).**
      - arm64/Renode: **22 fails (was 50), 357 pass, 15.7 min (was
        26.5)**, and the serial retry recovered **41 of 50**
        silent-or-starved rows -- under the flat budget, Renode
        contention had been filing dozens of good tests as failures
        every run. The starved residue is exactly the crypto cluster:
        ecdsa-cert, ecdsa-p256, edvector, rsa-pss, tls-cert,
        tls-cv-schemes.
      - riscv64/QEMU: 259 pass, **119 fails (was 149), 96 skipped**
        -- the eligibility classes absorbed the kernel and
        machine-sidecar rows. Wall time went UP (23.8 min vs 16.3):
        the honest classifier serially retries all 73 silent/starved
        rows and 0 recover, because they are the routed riscv-plug
        cluster wearing honest labels. That cost shrinks as reek's
        fixes land; it is the price of not filing contention as
        defects. 31 FAIL_STARVED rows each carry their progress (L of
        E lines, N bytes); the 28 riscv-only ones all pass on arm64,
        so they are the plug cluster, not budget cases.
      - **Heavy-compute ruling: `.cross-budget` sidecar, not
        exclusion.** All six crypto rows PASS alone at a raised
        ceiling on arm64, 14-41 s -- correct, just slow under
        emulation, and real coverage of the long-arithmetic paths.
        Each carries a `.cross-budget` of 90 (first line, seconds;
        2x the 41 s max observed); the harness reads it per test, and
        the completeness exit means a pass pays its true runtime,
        never the ceiling, so the sidecar costs wall time only on a
        genuinely broken row. `check-sidecars.ps1` knows the
        extension. `ttt-perfect` stays unruled: it computes before
        its first byte, so it reads dead-silent at any ceiling tried
        so far; raise `-RenoTimeout` for one run and watch, if its
        verdict ever matters.

Nothing enters the default gate without Damian's call. Build the instrument;
do not gate it.

## Measurement notes (so the numbers can be re-derived)

Instrumentation lives in `build/test.ps1` (phase stopwatches, per-test
`.run-ms`, `_results/_timings.tsv`) and `build/test-compile-batch.ps1`
(resolve/vm/parse split in the sweep log, per-test `.src-bytes`). The gate
(`build.ps1` -> `bvt.ps1`, `test-run.ps1`) is untouched. Battery runs for
measurement happen under Damian's standing grant to red's lane (2026-07-27),
never on private initiative.
