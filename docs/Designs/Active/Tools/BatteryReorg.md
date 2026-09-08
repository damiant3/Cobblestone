# Battery Reorg -- census and redesign

*Steps 1-5 are landed. Steps 6 (the language axis), 7 (dedup) and 9 (the
coverage axis) are QUEUES, not steps: you close cells, and there is no state
in which any of them is done. Step 11 is the open design.

Owner: red; step 11 is fester's. Every number below is from one instrumented
run against seed AFFD4511 and is stale by construction -- re-measure before
quoting any of it (L-COUNT). A matrix is only as good as the SHAPES its
fixtures are written in (L-CONSTRUCT).*

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
6. **Language axis matrix. CENSUSED 2026-09-07 (red); the assertions are
   healthy and what is left is not in `ops/`.** 51 chapters in
   `codex/test/ops`, zero `.skip`, 50 carrying an `.expected`. The one
   without is `real-trapping-overflow`, a `.fatal`, which this file's own
   ExaminersAssay section already records as a compile check plus a manual
   read. Two `.expected` carry no digit (`match-shadowed-arm`,
   `unit-pattern-lit`) and both were read: each asserts real text, so the
   L-NAMED digit heuristic flags them and does not convict them.

   **Two keyword censuses over this corpus were WRONG and reading caught
   both, which is L-CENSUS at its own subject.** Searching `Integer
   <mode>` found nothing because the grammar suffixes the mode after the
   band (`Integer between <lo> and <hi> wrapping`); searching for an
   Integer `trapping` mode found nothing because no such word exists.
   `Integer`'s modes are `wrapping`, `clamping` and `error`, and `error`
   IS the trapping default, spelled by ABSENCE, so every unannotated
   `Integer` op is already a trapping cell. A cell that is covered by
   default cannot be counted by grepping for its name.

   Coverage measured on the real grammar: Integer carries `wrapping` and
   `clamping`, Real carries `saturating` and `trapping`, and Integer's
   trapping cells are the plain-`Integer` arms of `int-add-wrapping`,
   `int-mul-wrapping` and `int-wrapping-spelling`, which COMPILER-36 built
   as its fixtures. **The residual risk is not a missing ops chapter; it
   is that app sites are compile-checked and executed only by the
   battery** (L-CENSUS's runner column, and COMPILER-36's four misses).

   **THE GAP IS MEASURED NOW (red, 2026-09-08), and it is 910 chapters
   and every entry unit.** Of 1,137 `.codex` chapters under `apps`, 227
   are reached by the cite closure of the 1,746 chapters under
   `codex/test`; **910 are reached by none**. Of the **295 app ENTRY
   chapters**, the units `build/sweep-app-classes.ps1` compiles,
   **zero** are reached, and that zero is STRUCTURAL rather than an
   oversight: an entry chapter declares `opening`, so no test can cite it
   without colliding on a second entry point. The citing corpus is
   therefore incapable of executing any app entry unit, whatever is added
   to it, and the app sweep compiles all 295 and boots none
   (`build/build.ps1`, the `app-sweep` phase, `-Check`).

   **The 227 is an UPPER bound and the 910 a lower one**: reaching a
   chapter through a cite means it was compiled into the test's unit, not
   that any of its functions was called (L-UNCALLED). Counts re-measured
   against the sweep's own rule, `^  opening\s*:` under `apps` outside
   `build-output`, which answers 295 today; the `267` in `build.ps1`'s
   `app-sweep` comment and the `265` in `build/app-sweep-baseline.txt`
   are stale (L-COUNT), and `build.ps1` is generated from
   `codex/build/BuildScript.codex`, so they are corrected by whoever next
   edits that generator rather than as an errand.

   **RULED 2026-09-08 by Damian: there is NO runner for the 295 app
   entries, and the residual stays open by choice.** In his words: "app
   drift is acceptable during this phase; the blast radius of a change to
   compiler or foreword should not be a gating factor; app lift is a
   secondary concern; it costs too much to maintain; apps are a test bed
   until the underlying code is more stable."

   So the 910 unexecuted chapters and the 295 compile-only entry units
   are a KNOWN and accepted state, not a gap awaiting a lane. The
   measurement above stays because it prices the ruling: a later phase
   that wants app execution starts from a counted corpus rather than
   re-deriving one. Nobody is to build the runner, and a compiler or
   foreword change is not held for app fallout.

   The original scope: new coverage organized by
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

11. **Composable batteries by blast radius. DESIGNED 2026-09-07 (fester),
    not built.** Damian's ask: test packages named for their COVERAGE rather
    than their purpose -- apps, compiler, kernel, plugs, board, and so on --
    so a lane mixes the batteries a change can break and runs nothing else,
    because the box is the bottleneck.

    **What the existing tiers are, and why they do not answer it.**
    `test.ps1 -Tier` already composes (`-Tier lib,apps`), so the mechanism
    is present and the NAMES are the gap: `lang`, `lib`, `fw`, `apps`,
    `hardware`, `traps`, `slow`, `oracles` are LOCATIONS in `codex/test`,
    not subsystems a change can break. Measured 2026-09-07 over 1,725 test
    chapters (root 625, apps 472, forewords 318, errors 206, ops 51, lib 40,
    ui 7, cost 5, examples 1): the default `lang` tier IS the 625-chapter
    root directory, and it holds kernel capability tests, arch boot tests
    and language pins in one bucket. A lane that changed the kernel has no
    word for what it needs.

    **Coverage is derivable from the cite graph, and that is measured, not
    assumed.** 500 of the 625 root chapters carry at least one `cites`
    line, and 1,198 of their 1,199 cite edges resolve against a
    chapter-name index built over the 2,047 non-test chapters in `codex`
    and `apps` (`DiagPci` is the single unresolved name). Where those edges
    land:

    | subsystem | edges |
    |---|---|
    | `codex/foreword/**` | 819 (core 519, ui 116, encode 74, engine 47, game 44, math 36, punctual 13, signal 11, ai 8) |
    | `codex/os/**` | 200 (kernel 102, net 85, trust 7, dev 6) |
    | `apps/**` | the remainder |
    | `codex/compiler/**` | **0** |
    | `codex/plugs/**` | **0** |

    **That table was built with a name-only index and its middle rows are an
    upper bound, not a measurement.** 66 chapter names are defined in more
    than one place (`Console` and `VirtioBlk` in both `codex/foreword/core`
    and `codex/os/kernel`; `BitmapFont`, `DriveManager`, `SystemDb` colliding
    with `apps/**`), and a first-writer-wins index assigns every one of them
    to whichever file was walked first. A cite names its quire
    (`cites Foreword chapter Board`), so the disambiguation is available and
    the index must key on (quire, chapter) with the quire read from the
    owning directory. The two ZEROS are unaffected either way: a name
    collision can move an edge between `foreword`, `kernel` and `apps`, and
    cannot manufacture an edge into `codex/compiler` or `codex/plugs`.
    `check-test-compile.ps1:115` deliberately does NOT match the quire,
    which is right for a CHECK, where over-inclusion is the safe direction,
    and wrong for a SELECTOR, where it is both over- and under-inclusive at
    once.

    **The batteries are two different KINDS and one mechanism for both
    would report green over the two that matter (L-AXIS).** The compiler and
    the plugs have no cite edges from `codex/test` at all -- the compiler
    because it is global by construction (`build.ps1:1319`), the plugs
    because their harnesses live beside them. A cite-selected `compiler`
    battery selects zero chapters and passes.

    - **CORPUS batteries** select chapters by the subsystem their cites land
      in, derived per run and never stored: `foreword`, `kernel`, `apps`.
      `foreword` is the sixth name Damian's list does not give and it is the
      largest class by a distance; "and so on" is the licence to add it.
    - **PHASE batteries** run named harnesses, because their subject has no
      corpus to select: `compiler` (the fixed-point core, `test-bvt`,
      `sem-equiv`, `check-errors`), `plugs` (`plug-binary`, `plug-smoke`,
      `plug-selftest`), `board` (the 146 chapters carrying a machine sidecar
      -- `.disk`, `.disk2`, `.disk-src`, `.vmargs`, `.keys`, `.smp` -- plus
      `boards-test`). The `hardware` tier already computes almost exactly
      this set, so `board` is that tier renamed rather than new code, with
      one correction it needs anyway: `$machineSidecars` in
      `build/test.ps1:156` lists FIVE extensions and omits `.disk-src`,
      which step 10 of this document names as a machine-sidecar class and
      which `codex/test/manifest-pin.disk-src` carries. That chapter attaches
      a compiled CDX as a disk and cannot run without a machine, so it is a
      one-chapter hole in the tier today, and the count is 146 with it and
      145 without.

    **A CITE IS A DEPENDENCY, NOT A SUBJECT, AND THAT BREAKS THE KERNEL
    BATTERY SILENTLY.** This is the finding the design turns on and it was
    missed on the first pass. A test cites what it needs in order to
    COMPILE, not what it is ABOUT. For library tests the two coincide, and
    for machine-side tests they systematically do not:
    `codex/test/hpet-interrupt.codex` declares `Chapter: HpetInterrupt`,
    `grounds Device.Mmio`, pins the HPET counter at `#FED00010` and the
    IOAPIC at `#FEC00000` -- the subject of `codex/os/kernel/Hpet.codex` --
    and its ONLY cite is `Foreword chapter Board`. A cite-derived selector
    files it under `foreword`. It is not selected by `kernel` and it is not
    in the residue either, BECAUSE IT HAS A CITE, so the run reports nothing
    missing. A no-cite residue list catches the honest gap and cannot see
    this one.

    **`.no-cross` looked like an independent label for this and IS NOT, which
    was found by building the check and reading the reasons.** 31 chapters
    carry it and the extent of the disagreement with the cite graph was
    briefly written up here as 21 misroutes. That was wrong twice.
    `.no-cross` means only "does not run on a cross board" and the stated
    reasons are heterogeneous: x86 port and MMIO hardware
    (`hpet-interrupt`), no block device on the cross lane (`fat16-write`),
    an x86-64 EMITTER difference (`eq-generic-recursive`) and a riscv PLUG
    gap (`text-helper-spec`). The last two have the compiler and the plugs
    as their subject, so counting them as machine-side misroutes is a false
    positive. And the six `fat16-*` are not misrouted at all: they cite
    `Foreword chapter Fat16`, the Fat16 library IS
    `codex/foreword/core/Fat16.codex`, and a foreword change should select
    them. `codex/os/kernel/FatReader.codex` is a different consumer, not
    their subject.

    **NEEDING A MACHINE AND COVERING A SUBSYSTEM ARE ORTHOGONAL AXES, and
    conflating them is what produced both errors.** A machine sidecar
    (`.disk`, `.keys`, `.smp`, `.vmargs`) is a hard fact about what the
    runner must attach: a COST. A cite is a claim about what the chapter
    depends on: a SUBJECT. `fat16-write` carries a `.disk` and cites the
    foreword, and both are correct simultaneously. Any rule that reads one
    axis as evidence about the other fires on chapters that are classified
    correctly, and a check that cries wolf is one nobody reads.

    **What survives is narrower and is a REVIEW QUEUE, not a refusal.** A
    chapter that needs a machine and whose cites reach no machine-side
    chapter at all is a candidate: no kernel or board change selects it.
    `hpet-interrupt` is the clean example -- it pins the HPET counter and
    the IOAPIC and cites only `Foreword chapter Board`, so a change to
    `codex/os/kernel/Hpet.codex` does not select it. Whether that is a
    defect is a judgement per chapter, and `.covers` records the answer once
    somebody makes it.

    **THE QUEUE IS EMPTY: all 62 judged (fester, 2026-09-07), and the answer
    was not uniform.** The instrument was the TRANSITIVE cite closure, which
    the classifier deliberately does not walk, and the depth mattered: 15
    chapters reach `codex/os` at two hops and 20 do so transitively, so a
    two-hop reading would have under-classified five. Those 20, every one of
    them under `codex/test/apps`, carry `.covers` adding `kernel`: they reach
    `Kernel chapter Pci` through `GopXhci`, `GopUsb`, `GopAhci` or `GopDisk`,
    and `gopweb-spawn` reaches twelve chapters across `kernel/` and `net/`
    including `net/webserver`. The other 42 record the opposite judgement,
    which is a real answer and not a shrug: their whole closure stays out of
    `codex/os`, so no kernel or board change selects them and none should. The
    FAT family is the clean case, and it is worth stating because the names
    invite the other conclusion: `Foreword chapter Fat16` reaches
    `Foreword chapter VirtioBlk`, which cites nothing, while
    `codex/os/kernel/VirtioBlk.codex` is a SEPARATE implementation citing
    `Kernel chapter VirtioPci`. Changing the kernel's block driver cannot
    break `fat16-write`, and the shared name is the whole trap.

    Battery counts moved exactly as that predicts: `kernel` 282 to 302, every
    other battery unchanged, which is the signature of sidecars that preserved
    each chapter's existing landing set and added `kernel` only where it was
    earned.

    **THE INDEX UNDER ALL OF THIS WAS WRONG, AND THE JUDGEMENTS SURVIVED IT**
    (fester, same day, hours later). The script derived a chapter's quire from
    the last segment of its directory. The tree does not encode that:
    `build/quire-map.ps1` does, and it is the authority `check-cite-names.ps1`
    already dot-sources. The two disagree in both directions, `codex/os/core`
    being quire OS rather than the Foreword and `Games` being
    `apps/games/classic`, whose last segment names no quire, so 43 live cites
    resolved to nothing and their chapters fell out of every battery. Fixed by
    reading the map instead of guessing. **Dated counts at head 2026-09-07,
    which supersede every figure above them:** 1,735 test chapters, foreword
    1,317, kernel 304, board 13, apps 241, compiler 0, plugs 0, unclassified
    310, and cite names resolving to no chapter down from 45 to **2**, both of
    them the deliberate error fixtures (`missing-cite`,
    `unregistered-quire-cite`), which is what a correct index should leave.

    **All 62 judgements were re-derived against the corrected index and NONE
    flipped**, so the sidecars stand as landed; what the defect moved was the
    script's own counts and its unresolved list, not any judgement made from
    it. That is luck rather than method: the queue's members happened not to
    cite the quires the old rule mangled, and the check cost one script.

    **The cost to watch is not the release, it is the per-gate guest count**
    (red, clearing this 2026-09-07). A kernel change now selects 20 more
    chapters in every lane's gate, and 19 of the 20 join for the same reason,
    so the number moves again the moment anything else reaches
    `Kernel chapter Pci`. Re-measure it rather than quoting this line
    (L-COUNT).

    **`.covers` is now orphan-checked** (`build/check-sidecars.ps1`). It was
    not in that script's extension list, so 62 new sidecars would have been a
    new unguarded class: rename a test and its judgement is left pointing at
    nothing, silently. The control was fired and its file removed and its
    absence verified.

    **So `.covers` is the AUTHORITY, not a patch for the residue.** A
    per-chapter `.covers` sidecar naming the subsystem and the reason (the
    `.no-cross` shape, which `check-sidecars.ps1` already has a place for)
    is read FIRST; the cite graph is the fallback for chapters that do not
    carry one. 125 root chapters cite nothing and the tempting reading --
    a chapter that cites nothing tests the language -- is also FALSE, and
    was checked before it was believed: that set holds the `cap-*` family
    (the process table IS the subject), `arm64-boot-test`, `arm64-net-gate`,
    `arm64-proc-cells`, `block-select-drives` beside genuine language pins
    like `arith-narrow-proven`. An unclassified chapter belongs to NO
    battery, and every battery prints the size of the set it selected FROM
    beside its own count (L-DENOM).

    **BUILT: `build/check-battery-coverage.ps1`** (fester, 2026-09-07). It
    builds the (quire, chapter) index, classifies every chapter under
    `codex/test` by `.covers` then by cites, prints each battery's count
    against the 1,725 it selected FROM, and prints the review queue. It
    needs no guest and no kernel. Its counts are dated below, after the index
    defect that moved them was found and fixed; the figures first published
    here (foreword 1,308, kernel 282, apps 226, unclassified 313, and 45
    unresolved cite names) were taken with the broken index and are not
    carried.
    Battery membership is a SET, not a partition -- a chapter citing both
    the foreword and the kernel is in both, which is what "run the batteries
    a change can break" requires.

    The only hard failure is a `.covers` naming a battery that does not
    exist, and all three controls were fired before the script was believed
    (L-FALSIF): `.covers kernel` on `hpet-interrupt` put it in `kernel` AND
    removed it from `foreword`, proving the sidecar overrides the cites; a
    typo'd `kernal` exited 1 naming the file; and the control file was
    removed and its absence verified, because after a control run the tree
    is in the CONTROL state and that is what ships if nobody checks.

    **Names are not the instrument.** `cap-*`, `arm64-*` and `block-*` all
    name their subsystem, and step 8 of this document already records that
    names are a proven weak instrument in `build/` (`test-boards.ps1` and
    `boards-test.ps1` are near-identical names with different subjects). A
    name prefix is a hint for writing the `.covers` sidecar by hand, never
    a selector.

    **The controls, and none of them is optional.**
    - *positive:* editing a chapter's `cites` line moves it between
      batteries on the next derivation.
    - *negative:* a change touching only `apps/**` selects zero `kernel`
      chapters.
    - *the one that decides whether any of it is real:* a battery selecting
      a subject-shaped set is not evidence it can FAIL on that subject
      (L-VACUOUS). For EACH battery, sabotage one member -- corrupt its
      `.expected` -- and require that battery to go red and the others to
      stay green. A sabotage that moves no colour is the corpus saying it
      cannot reach the branch (L-CONSTRUCT), not a passing control.
    - *the residue arm:* a chapter with no cite and no `.covers` appears in
      the unclassified list and in no battery's count. Fabricate one and
      check both halves.
    - *`.covers` precedence, FIRED:* give a chapter a `.covers` that
      contradicts its cites and check the sidecar wins in both directions,
      the battery it joins and the one it leaves.
    - *the review queue is not a control and must not be dressed as one.*
      It is non-empty at head by construction, so it cannot pass or fail and
      proves nothing on its own (L-VACUOUS). What it does is put a
      per-chapter judgement in front of a person; the `.covers` that follows
      is the record that the judgement was made.

    **What this does not do.** It selects tests; it does not decide when a
    battery runs. `-Internal` is banned and this step does not revive it:
    the batteries are what a LANE invokes for the change in front of it, and
    the release gate is unchanged. Nothing here enters the default gate
    without Damian's call.

Nothing enters the default gate without Damian's call. Build the instrument;
do not gate it.

## Measurement notes (so the numbers can be re-derived)

Instrumentation lives in `build/test.ps1` (phase stopwatches, per-test
`.run-ms`, `_results/_timings.tsv`) and `build/test-compile-batch.ps1`
(resolve/vm/parse split in the sweep log, per-test `.src-bytes`). The gate
(`build.ps1` -> `bvt.ps1`, `test-run.ps1`) is untouched. Battery runs for
measurement happen under Damian's standing grant to red's lane (2026-07-27),
never on private initiative.

## What "unclassified" is, measured 2026-09-07 (fester)

**310 of 1,735 test chapters are in no battery, and that is three different
things, not one number.** Characterised before calling any of it a gap.

- **118 are error fixtures** under `codex/test/errors`. They must NOT compile;
  their home is `check-errors`, which runs them all. No battery should select
  them and none does. Correct.
- **178 cite nothing and carry no machine sidecar.** The tempting reading, that
  a chapter citing nothing tests the language, is the one this document already
  refuses, so what these need is a shape statement rather than a sweep: the
  compiler-side harnesses (the fixed-point core, the BVT, `sem-equiv`,
  `check-errors`) are what stand behind them, and no battery claims them.
- **13 cite nothing AND carry a machine sidecar**, which is the interesting
  set, because a chapter that needs a machine and belongs to no battery is the
  misroute this script refuses for classified chapters, one step where the
  refusal cannot reach. Named, because a set this small should never be
  described rather than listed: `smp-affinity`, `smp-arm64-boot`, `smp-cores`,
  `smp-dispatch`, `smp-halt`, `smp-preempt`, `smp-proc0-pinned`,
  `smp-riscv-boot`, `smp-tss`, `block-sector-count`, `block-select-drives`,
  `manifest-pin`, `apps/block-io-basic`.

**Two of the 13 are already correct and must not be "fixed".** `smp-arm64-boot`
and `smp-riscv-boot` have their AP stubs in `codex/plugs/arm64/Arm64Runtime.codex`
and `codex/plugs/riscv/RiscVRuntime.codex`, so their subject is a plug, and the
`plugs` battery selects no chapters by design and is covered by `plug-binary`,
`plug-smoke` and `plug-selftest`. Asserting `plugs` in a `.covers` would
contradict `test.ps1`, which refuses `-Battery plugs` in those words.

**The remaining 11 want a per-chapter judgement and it is NOT obvious**, which
is why none was written here. The x86 AP path is
`codex/compiler/Emit/X86_64Boot.codex`, so several of these are broken by a
COMPILER change, and the compiler battery selects nothing either; but
`codex/os/sched/OsScheduler.codex` and `CoreHeap.codex` are kernel-side and
some of the same chapters plainly ride them. Splitting the 11 between "the
kernel scheduler can break it" and "only the emitted boot stub can" is the
work, it needs red's clearance because it enlarges the kernel battery, and it
is the honest next item rather than something to guess at now.

**The denominator, stated so the number cannot be read as coverage** (L-DENOM):
1,735 is every chapter under `codex/test`, and the batteries are a SET over it,
not a partition. 310 in no battery is not 310 untested: 118 are run by
`check-errors` and most of the 178 stand behind the compiler harnesses. What
the number bounds is how much a lane running only the batteries its change can
break never selects.

## A COMPILER CHANGE SELECTS NO SMP TEST, and kernel is the wrong home for the 11

**Filed 2026-09-07 (fester), at red's condition, after red cleared adding the
11 to `kernel` and delegated the split. The split came out otherwise, so the
11 got NO `.covers` and that is the finding rather than an omission.**

The decisive fact is one line of reasoning that needs no run: **a test that
cites no chapter can reach nothing but builtins, and builtins are emitted by
the compiler.** All 11 cite nothing. Grepped at head, every primitive they
stand on is compiler-side: `process-spawn` and `process-spawn-on-core` are in
`codex/compiler/Emit/X86_64Boot.codex`, `X86_64Helpers.codex` and
`X86_64ProcessHelpers.codex`; `block-read-sector`, `block-select` and
`block-sector-count` are in `codex/compiler/Types/Builtins.codex` emitted
through `X86_64Helpers.codex`; the per-core TSS and IST1 descriptors
`smp-tss` pins exist only under `codex/compiler/Emit/X86_64*.codex`, with
nothing in `codex/os` mentioning them.

`OsScheduler.codex` and `CoreHeap.codex` under `codex/os/sched` are the OS's
own scheduler, a different layer these tests never enter, because entering it
would require citing it.

**So `kernel` would have been actively wrong**, not merely imprecise: it would
run 11 tests on every kernel change that no kernel change can break, and leave
them unselected by the thing that CAN break them. "Kernel beats no battery" is
true only where the kernel is somewhere in the blast radius, and here it is
not.

**The row this leaves, which is the real one.** `test.ps1` refuses
`-Battery compiler` in the words "nothing under `codex\test` cites the
compiler", and that sentence is TRUE ABOUT CITES AND FALSE ABOUT BLAST RADIUS.
Nothing cites the compiler because the compiler is reached through builtins,
which carry no cite; the 11 are proof that chapters exist whose only credible
breaker is a compiler change. The fixed-point core, the BVT and `sem-equiv`
stand behind the compiler, but none of them boots a multi-core guest or a
two-disk one, so a change to the AP stub, the process helpers or the block
builtins is graded by nothing that selects these chapters.

Deciding what to do about that is a design call and is NOT taken here: giving
them `.covers compiler` would make this script count them while `test.ps1`
still refuses that battery by name, so the two would disagree, which is the
`L-DENOM` failure one level up. The options are to let `compiler` select a
corpus, to add a battery for compiler-emitted runtime, or to say plainly that
these ride the harnesses and accept it. **13-to-2 must not read as solved
while this half is untouched (L-PARTIAL).**

**DECIDED 2026-09-07 (red, whose register this is): option one, `compiler`
gets the corpus.** No new battery, because a second name for the same set is
a second thing to keep in step. The selection rule is **cites nothing AND
carries a machine sidecar**, which is mechanically decidable and needs no
per-file judgement; that is exactly what separates it from the quire-map
audit landed the same day, where seven candidates each needed a human call
and a check would have cried wolf. A rule that can be evaluated is a runner;
one that cannot is an audit.

**The refusal and the corpus move together or not at all.** `test.ps1`
refuses `-Battery compiler` in the words "nothing under `codex\test` cites
the compiler". Landing `.covers compiler` while that refusal stands is the
`L-DENOM` disagreement named above, so the sentence is deleted in the same CL
that gives the battery its members, and it is replaced by what the rule
actually is rather than by a claim about cites.

**Re-measure the size before implementing, and say which claim the number
is** (L-COUNT, L-REQUEST). Three different questions are in play and they
have three different answers: chapters that cite nothing; those that cite
nothing AND are gradeable; and those that cite nothing, are gradeable, AND
sit in no battery, which is the 13 above. Measured over all of `codex/test`
on 2026-09-07 (red): **1735 chapters, 305 citing nothing, 168 of those
gradeable** -- that last figure was written here as "carrying a machine
sidecar", and the predicates were transposed; corrected on re-measurement
(fester, 2026-09-07, at head 1,737 chapters: 309 cite nothing, 170 of those are
gradeable, and **13** carry a machine sidecar, which is the rule's own set). The census at the top of this file says 265
cite nothing and is dated 2026-07-27; both are believable and neither should
be carried forward without re-running.

**This adds no standing per-CL cost, which is why it is affordable.** A lane
compiles and runs the tests its change touches; the full battery is Damian's.

**IMPLEMENTED 2026-09-07 (fester), and the sizing sentence above needed one
correction.** Re-measured at head before implementing, as the row asks: of
1,737 chapters, **309 cite nothing**, **170 of those are gradeable** (carry an
`.expected`), and **13 cite nothing AND carry a machine sidecar**, which is the
rule's own set and is the same 13 that sat in no battery. So the "168" above is
the GRADEABLE count, not the machine-sidecar count, and the two predicates were
transposed in that sentence; the rule and the 13 agree with each other and only
the label was wrong. Naming which claim a number is, is what L-REQUEST asks
for, and this is what it looks like when it is not done.

The 13 carry `.covers compiler`. `test.ps1`'s refusal moved in the same CL:
`compiler` leaves `$phaseOnly` for `$corpus`, `plugs` stays, and the sentence
about cites is replaced by the rule.

**One interaction the ruling did not name and which would have put all 13 back
where they started.** `check-battery-coverage.ps1` holds a `$softwareOnly` set,
batteries that start no machine, and sends any machine-side chapter landing
only in those to the review queue. `compiler` was in it. Giving the 13 a
battery that was still marked machine-less would have moved them from "in no
battery" to "in the review queue", which is the same state wearing a different
name. `compiler` is out of `$softwareOnly`, because by the rule every member of
that battery carries a machine sidecar.

**Counts after, at head 2026-09-07:** foreword 1,319, kernel 304, board 13,
apps 243, compiler 13, plugs 0, unclassified 297, review queue 0. The corpus
was read back through `check-battery-coverage.ps1 -List compiler`, which is the
call `test.ps1` makes, and it returns those 13 paths. **`test.ps1` itself was
NOT run:** it is Damian's tool and refuses without his approval, so the
refusal's removal is verified by reading and by a parse, not by execution.
So a correct `compiler` battery is a tool that becomes usable rather than a
tax on every compiler CL, and a lane touching the AP stub or the block
builtins finally has something to name.

**The one part that is not red's to decide is the release sweep.** Adding
this battery there lengthens every release, and that is Damian's cost. The
recommendation is to add it: the gap lands on releases today regardless, just
later and with the cause further away.

## THIRTEEN CHAPTERS ARE REACHABLE BY NO TIER, INCLUDING `-All`

**Found 2026-09-07 (fester) when the kernel battery reported "303 chapter(s),
of 304 the classifier listed".** The one that fell out was
`codex/test/cost/giveup-beats-fuel`, and it is not alone.

`test.ps1` enumerates tiers from a fixed list of six directories, NON
recursively (`Get-ChildItem "$d\*.codex"`): `codex\test`, `codex\test\ops`,
`codex\test\errors`, `codex\test\apps`, `codex\test\forewords`,
`codex\test\lib`. `$allDirs`, which the `hardware`, `traps` and `slow` tiers
sweep, is the SAME six. So a subdirectory of `codex/test` that nobody added to
that list is invisible to every tier and to `-All`, which is built from tiers.

Three such directories exist, holding **13 chapters, 10 of them gradeable**,
none skipped, slow or fatal:

| directory | chapters | gradeable |
|---|---|---|
| `codex/test/cost` | 5 | 2 |
| `codex/test/ui` | 7 | 7 |
| `codex/test/examples` | 1 | 1 |

The arithmetic closes exactly: 1,724 chapters live in the six named
directories and 1,737 exist, so the 13 are the whole of the difference.

**The classifier walks `codex/test` recursively and the runner does not**,
which is the two-selectors-for-one-question shape again, and this time the
selectors disagree by construction rather than by drift. The runner is honest
about it, printing "of 304 the classifier listed" rather than reporting 303 as
the whole, so nothing here is hidden; but nothing fails either, and a battery
that selects a chapter the runner cannot reach is a green over a test nobody
ran (L-DENOM).

**Not fixed here, because wiring three directories into the tiers enlarges
`-All`, which is the release net and red's to clear.** The question is also not
purely mechanical: `cost/` and `ui/` plainly want tiers of their own or a home
in an existing one, while `examples/` holds a single chapter and may be
deliberate. Whoever wires them states which tier each joins and why, or writes
down the reason a directory must stay unreachable.
