# Battery Reorg -- census and redesign

*Steps 1-5 are landed. Steps 6 (the language axis), 7 (dedup) and 9 (the
coverage axis) are QUEUES, not steps: you close cells, and there is no state in
which any of them is done. Step 11 is built and has one open row.

Owner: red; step 11 is fester's. Every number below carries the date it was
measured and is stale by construction: re-measure before quoting any of it
(L-COUNT). A matrix is only as good as the SHAPES its fixtures are written in
(L-CONSTRUCT).*

## Why

The battery is the product of an additive process never systematically
reviewed. Damian's direction (2026-07-27): reorganize and refactor, much
faster, better reporting, far more language-feature coverage, duplication
eliminated, smarter execution. The battery is what makes "the language works" a
claim with a runner behind it, and one cheap enough to run after every fleet
arc catches same-day fallout on the day it lands rather than at a release.

## What the harness does now (steps 1-5, landed)

- **Containment rounds.** A batch VM death standalone-confirms the killer (exit
  4 plus `!EXC` dump) and re-batches every other non-clean result from that
  batch into fresh batch VMs; rounds repeat until one has no death (cap 5, then
  a standalone fallback).
- **Marker-scan parse.** Native ordinal IndexOf over a Latin-1 shadow string,
  memoized per marker, with an anchored-at-pos case for markers directly after
  a binary block. Kept-log lines are capped at 2000 per test. **Beware
  PowerShell's binding of the 5-argument `[string]::CompareOrdinal`: about 2 s
  PER CALL. Use `Substring -ceq`.**
- **The CDX9002 rule.** A deck overflow reported from a batch session is always
  confirmed standalone: `foreword-all-compile` hits LOWER's deck floor
  in-session and compiles clean alone on the same seed and flags.
- **Reporting.** Every run writes `_results\_rollup.txt` (per-category counts,
  slowest-10, one actionable line per failure) and a delta against
  `test-output\last-run.json`.
- **Tiers with honest names.** `-Tier lang/lib/fw/apps/hardware/oracles/all`;
  oracles are pinned into the gate after `test-bvt` and into `-All`;
  collections stay author-owned.

## 6. The language axis

`codex/test/ops` is the operator-correctness-by-operand-type home (lang tier,
scanned by `test.ps1` and the cross-arch batch). Members enter as smoke-bundle
members, because compile is free and boots are not. New coverage is organized
by axis: operators x types x sign x value-versus-branch position, bounded modes
and `__narrow`, CCE boundaries, linear and mutable probes, vector lanes, unit
types, remainders.

**Censused 2026-09-07 (red): the assertions are healthy and what is left is not
in `ops/`.** 51 chapters, zero `.skip`, 50 carrying an `.expected`. The one
without is `real-trapping-overflow`, a `.fatal`, which `ExaminersAssay` records
as a compile check plus a manual read. Two `.expected` carry no digit
(`match-shadowed-arm`, `unit-pattern-lit`) and both were read: each asserts real
text, so the L-NAMED digit heuristic flags them and does not convict them.

**`Integer`'s trapping mode is spelled by ABSENCE.** Its modes are `wrapping`,
`clamping` and `error`, and `error` IS the trapping default, so every
unannotated `Integer` op is already a trapping cell and no grep for its name can
count it. The grammar also suffixes the mode after the band (`Integer between
<lo> and <hi> wrapping`), so a search for `Integer <mode>` finds nothing. Real
carries `saturating` and `trapping`. Integer's trapping cells are the
plain-`Integer` arms of `int-add-wrapping`, `int-mul-wrapping` and
`int-wrapping-spelling`.

**The residual risk is that app sites are compile-checked and executed only by
the battery**, and its extent is measured (red, 2026-09-08). Of 1,137 `.codex`
chapters under `apps`, 227 are reached by the cite closure of the 1,746
chapters under `codex/test` and **910 are reached by none**. Of the **295 app
ENTRY chapters**, the units `build/sweep-app-classes.ps1` compiles, **zero** are
reached, and that zero is STRUCTURAL: an entry chapter declares `opening`, so no
test can cite it without colliding on a second entry point. The citing corpus is
therefore incapable of executing any app entry unit, whatever is added to it,
and the app sweep compiles all 295 and boots none. The 227 is an UPPER bound and
the 910 a lower one, because reaching a chapter through a cite means it was
compiled into the test's unit, not that any function was called (L-UNCALLED).

**RULED 2026-09-08 by Damian: there is NO runner for the 295 app entries, and
the residual stays open by choice.** In his words: "app drift is acceptable
during this phase; the blast radius of a change to compiler or foreword should
not be a gating factor; app lift is a secondary concern; it costs too much to
maintain; apps are a test bed until the underlying code is more stable." So the
910 unexecuted chapters and the 295 compile-only entry units are a KNOWN and
accepted state. Nobody is to build the runner, and a compiler or foreword change
is not held for app fallout. The measurement stays because it prices the ruling:
a later phase that wants app execution starts from a counted corpus.

## 7. Dedup

Method: hash census (zero byte-identical sources; identical-expected groups are
mostly coincidental pass-counts), then name-family reading. **Every survivor
must be able to fail.**

Standing verdicts, so the judgement is not made twice:

- NOT duplicates, KEPT: the identity-* five (distinct kernel behaviours;
  merging spawn and capability tests would share one boot's process table
  across assertions that need isolation), the ui-*-test twelve (they compute
  values; the `forewords/` files are the compile smokes), `c64-sid` against
  `c64-sid-stream` (synthesis against HDA streaming), the narrow-proven six
  (prover pins by site, an axis family).
- `real-neg-neg`, `real-approx`, `real-bitcast` live in `codex/test/ops` as
  Real-operand pins. `real-cert` stays where it is: the Real there is English,
  and it is an X509 test.
- The `build/` scripts are a separate arc, scoped in step 8.

## 8. The build/ script dedup arc (scoped 2026-07-28, not started)

**117 `.ps1` files in `build/`.** Names are a proven weak instrument here:
`test-boards.ps1` and `boards-test.ps1` are near-identical names with DIFFERENT
subjects (a Renode cross-arch hello-world smoke against the nine-board
`-board-mmio` driver battery on codex-vm), which is a rename candidate and
explicitly NOT a merge. So each family row states the QUESTION a reading must
settle.

- **Core harness** (`test.ps1`, `test-compile-batch`, `test-run`, `bvt`,
  `vm-config`, `check-sidecars`, `audit-skips`, `test-gui`, `test-app-gui`):
  the product, no dedup question.
- **Cross-arch family: settled.** On the same 8-10 test subset,
  `test-cross-batch` compiled 10 tests in 14 s across 8 parallel per-test slots
  (about 0.35 s per test) against `test-cross-fast`'s 12 s through one seed-VM
  REPL plus one plug VM (about 1.3 s per test, SEQUENTIAL by design, its
  memory-safety constraint being one compile VM per agent). At battery scale
  that is about 160 s against about 600 s, so the problem `-fast` solved was
  solved better by parallel slots and there was nothing to fold. The harnesses
  agreed on results, so retiring lost no signal. `test-cross-fast` and
  `test-cross-compile-batch` are gone. One set-discovery quirk: `-batch` also
  scans `ops/`, so it found 14 eligible to `-fast`'s 13 on the same filter.
- **Brotli tooling** (11 scripts, about 2000 lines): mostly load-bearing, not
  dead one-offs. `dict-extract` plus `dict-chapter` GENERATE the 122,784-byte
  `BrotliDict.codex` and its re-proof path against .NET (a person does not
  hand-edit that chapter); `xform-extract` and `ctx2-extract` are the same shape
  for the transform and context tables; `ctx2-cases` and `xform-cases` build the
  foreign streams our encoder cannot emit, which is the L-ORACLE coverage for
  the decoder's mode-2 and transform paths; `hdr-probe` is a live
  refusal-localization diagnostic. The one clean retire candidate is
  `brotli-dict-probe`, the feasibility probe superseded by the extractor it
  spawned.
- **Store and disk scripts**, verdicts settled by reading:
  - KEEP `test-quote-from-store`: a store written FRESH by one VM then read by
    the compiler (`compile.ps1 -DiskFile`). `test-run.ps1` copies the disk to a
    throwaway temp, so the in-battery `quote-from-store` runs against a FROZEN
    fixture and cannot see writer-side format drift. This script is the
    crossing test.
  - KEEP `test-store-append`: pins the disk-load-against-disk-init append
    defect with two works through the real tools, one of which is exactly the
    case where appending and replacing agree.
  - KEEP `test-compile-from-store`: two compiles with a VM boot between,
    stage-one output being stage-two input.
  - KEEP `test-disk-compile`: the only driver of the compiler's DISK compile
    mode, which is live (`opening.codex:1891`, `emit-from-disk`). The battery
    feeds every compile over serial, so without this script DISK mode has no
    runner at all.
  - MERGE candidate: fold `test-store-real-file`'s exact-equality assertion
    into `test-store-append` as its first stage.
- **Checks: `check-apps` and `lint-unused-cites` are invoked by NOTHING**, and
  both are KEEP as on-demand instruments. `check-apps` deliberately so: it
  asserts runtime invariants across the generated app pages and Damian DECLINED
  coupling apps to the gate. `lint-unused-cites` is an on-demand lint with
  `-Src`/`-All`. Neither is part of the gate. Invoked by `build.ps1`:
  `p4-stale-check`, `check-constants`, `check-effect-vocab`, `check-sidecars`,
  `check-cdx-registry`, `check-facts-guid`, `check-doc-counts`,
  `check-plug-types`, and `check-cross-smoke` in the cross leg.
- **Interop, serve and oracle harnesses** (tls, mqtt, mqtts, https, coap x2,
  `cdx-serve`, `quote-from-peer`, `registry-locate`/`probe`, `gguf-foreign`,
  `plug-oracle`, `agent-bundle`, `oracle-scalar`/`vector`/`cce`,
  `wcet-validate`, `boards-test`, `install-boot-test`): author-owned
  instruments, on-demand by design, out of scope for dedup.
- **Probe singletons**, verdicts settled, each with a live subject the battery
  cannot express: KEEP `deck-floor-test` (starved floors name themselves:
  `-Decks` 5/20/40 must raise CDX9002 from the right phase, 100 must compile),
  `inline-fire-test` (does the single-caller inliner FIRE, visible only in the
  symbol map; the must-SURVIVE capture arm is the discriminator),
  `list-ceiling-test` (CDX9004 at the literal's own span, over and at the
  ceiling; its 447 KB fixture is generated, which is why this cannot be an
  in-depot test), `test-growth` (ballast pingpong, the only runner for the
  grown-source deck hazard class), `test-exception-handler` (the `.fatal` class
  asserts nothing at runtime by design, so this is the only automated reader of
  the dump FORMAT, including the deep-frames walk floor and the OUT OF MEMORY
  path), `stress-sweep` (flake hunter looping `test.ps1`, carrying `-ApprovedBy`
  so Damian's battery approval gate is built in), `ablate` (IR-pass ablation
  with bench instruction counts, reek's instrument), `ablate-doctrine` (blu's
  LESSONS harness, deliberately unrun in this workspace, because every agent
  here has read the answer key).

**Method when the arc starts:** read each candidate, verify its subject against
the tree, delete or merge in small CLs, and record verdicts here the way step 7
records the judged-kept list. A script kept must have a subject that exists; a
script deleted must have its unique assertion either preserved or shown
subsumed.

## 9. The coverage axis

The library half of coverage: which foreword chapters are never asked an
answer. Step 6 covers the compiler's own semantics; this axis covers the
foreword chapters, and it is a QUEUE, not a gate. **Damian's ruling stands: no
coverage machinery in the battery; re-derive on demand, by hand.**

**Method, re-derivable in one rg pass plus aggregation:** rg `cites (\w+)
chapter (\w+)` over `codex` and `apps`, key each hit by (quire, chapter) against
the foreword file list. A chapter is SMOKE-ONLY if its only citing tests are
`codex/test/forewords/` or `foreword-all-compile`; its consumer weight is the
count of citing files outside `codex/test` and `codex/foreword`. **Both are
proxies and both biases were confirmed on the run's own output:** citation is an
upper bound on consumption (the mechanical top four included `SearchBar`,
`FilterableList`, `FontGen` and `TrueTypeWriter`, all measured DEAD by a callee
grep), and a dependency-chain citation is not answer coverage (counting
`foreword-all-compile` as a test reads 429 of 429 covered).

**Measured 2026-07-28:** 429 chapters, 298 cited by a test outside the smokes,
**131 smoke-only**, of which **23 carry a non-test consumer citation** and 108
carry none, which are parked on Damian's bar of no consumer, no urgency. Treat
131 as an order of magnitude and re-derive before quoting.

**The queue rule:** rank the smoke-only-with-consumer set by consumer count;
filter by callee grep (grep the chapter's defined names, never the citation
table) and by recorded verdicts; within a rank prefer chapters with a published
external answer (L-ORACLE). The filtered live top at that measurement:
`gpu/DeviceMath` (34 gpushow kernels, every `[Device]` kernel's math library,
zero answer tests); `ui/RichText` (7), `ui/Markdown` (6), `ui/Editor` (3),
`ui/Window` (2), `ui/Canvas` (2), val's lane; `foreword/RankedTextSet` (4 plug
emitters) and `foreword/SourceDefWire` (2, repo protocol wire format), unowned.
`shell/PowerShellEmit` ranks first at 48 and is PARKED: every consumer is a
`codex/build/*Script.codex` shadow generator, and those do not produce the
hand-maintained `build/*.ps1`, so the weight belongs to a parallel system rather
than to the product. The ai/AssetForge cluster (ImageTo3d, DiffusionPipeline,
UNet, SafeTensors, Tokenizer, TextEncoder) waits on whether assetforge is on the
bar.

**Where the tests land:** `codex/test/lib` for pure answers, `codex/test/apps`
where a machine is needed; names stable so the run-over-run delta holds;
external oracle first and sabotage discipline. **The census TSV is regenerated
at need and never committed: a committed roster is the BACKLOG failure wearing a
new name.**

## 10. Cross-lane honesty at the new scale

**The exit signal is COMPLETENESS, not silence.** A quiet window (exit when the
uart is non-empty and unchanged for 1500 ms) was designed and refuted by its own
probe: `av-codec-test` prints line 1 in about a second and then computes for 15 s
or more before line 2, so the window read a between-lines compute gap as
termination, which is the flat wall's misclassification arriving sooner.
**Silence is not termination**, and an instrument pointed at silence answers "is
it printing", not "is it done".

Phase 2 only runs tests that have an `.expected`, so the runner knows how many
lines a finished answer has. A run ends when its filtered output (same
normalization as the compare: CR stripped, HEAP:/WD:/STACK: lines dropped,
trailing blanks trimmed) reaches the expected line count and ends in a newline.
A complete answer, right or wrong, is a real one and is compared on the spot.
`-RenoTimeout` is honestly a ceiling and governs everything else. There is no
quiet knob: fewer parameters, and nothing for a compute gap to fool.

The output channel differs per leg because the file backends differ:

- **QEMU:** `-serial file:` writes the log live, so the poller reads the file
  share-tolerantly at a 250 ms cadence and kills the guest at completeness.
  QEMU shares the ceiling; the flat 3 s budget is retired.
- **Renode:** `CreateFileBackend` provably does not put a byte on disk before
  teardown, so the file is not the channel. `emulation
  CreateServerSocketTerminal` (raw mode, third argument false) is connected to
  `uart0` and the resc neither starts nor quits the machine. The host connects
  to the socket FIRST, then issues `start` over Renode's own stdin, so no
  boot-time byte is lost to the connect race; bytes accumulate host-side and the
  harness writes `uart.log` itself, keeping the downstream compare identical. At
  completeness or ceiling the host issues `quit` and kills only a process that
  ignored it. Every test gets its own port (base plus index).

The classifier's classes:

- **Complete output:** a real answer. Compare; PASS_EXPECTED or FAIL_OUTPUT.
  FAIL_OUTPUT is never retried.
- **Zero bytes at ceiling:** dead-silent, `no uart output`, retried once alone.
  An empty log is filed here, not as FAIL_OUTPUT `act=[]`.
- **Incomplete output at ceiling: `FAIL_STARVED`** ("incomplete at ceiling: L of
  E lines, N bytes"). Contention-shaped first, since emulation under 8-deep load
  is slow rather than wrong, so it joins the retry set once, alone, at the same
  ceiling. Still incomplete alone means genuinely over budget and it stays
  FAIL_STARVED with `still incomplete alone`, a budget verdict visibly distinct
  from a wrong answer. The row's byte and line counts are what make the case
  investigable.

Scope is `test-cross-batch.ps1`, both legs. The gate leg (`check-cross-smoke`)
keeps its 3 s budget: its two tests answer in under a second and it already has
the silent-lane retry. `test-cross.ps1` single-runner parity is a follow-up.

**Eligibility is a design, not a filter flag**, and every row was classified by
reading rather than by name, which mattered twice: `scope-try-region` is a
language-level `trying`-scope miscompile pin and `network-effect` is headless by
design, so neither is an eligibility case despite pattern-matching to one.

- **Machine-sidecar, mechanical exclusion.** A test whose fixture is the x86
  machine names itself with a sidecar: `.disk`, `.disk2`, `.disk-src` (attach a
  compiled CDX as disk), `.vmargs` (codex-vm flags), `.keys` (scancode timeline
  for `-keys-file`). None can exist on a Renode or QEMU cross board, so the scan
  skips them the way `.smp` already routes multi-core tests elsewhere, naming
  the sidecar in the skip reason.
- **Kernel machinery, per-test `.no-cross`.** The cap-* family (capability words
  read from the process table the kernel checks on syscalls; boot-table bits
  from `X86_64Boot.codex`), the spawn and process family (`nested-spawn`,
  `spawn-reuse`, `proc-state-running`, `process-exit-status`: the process table
  IS the subject) and the network-scope trio (the runtime admission gate reads
  the same capability machinery). The cross lane boots a bare runtime with no
  kernel, so a test whose subject is the kernel has no subject there.
- **Heavy compute: `.cross-budget`, not exclusion.** The crypto cluster
  (ecdsa-cert, ecdsa-p256, edvector, rsa-pss, tls-cert, tls-cv-schemes) passes
  alone at a raised ceiling on arm64 in 14-41 s: correct, just slow under
  emulation, and real coverage of the long-arithmetic paths. Each carries a
  `.cross-budget` of 90 (first line, seconds, twice the 41 s maximum observed);
  the harness reads it per test, and the completeness exit means a pass pays its
  true runtime and never the ceiling, so the sidecar costs wall time only on a
  genuinely broken row. `check-sidecars.ps1` knows the extension.
- **One known limit, recorded rather than solved.** A test that computes a long
  time before its FIRST byte (`ttt-perfect`) is indistinguishable from dead at
  the ceiling. If its verdict ever matters, raise the ceiling for one run and
  watch.

**Open, routed to reek:** about 90 riscv-only FAIL_OUTPUT rows answer wrong
VALUES on coverage the old lane never ran, and every probed one passes on arm64,
which localizes them to the riscv plug;
`test-output-cross/riscv64_cross_results.md` is the worklist. Seven further rows
fail on BOTH cross lanes with complete, deterministic, identical-or-equivalent
wrong answers and pass on x86, which is the shared-lowering fingerprint
(L-SUSPECT): `unit-show` and `unit-pattern-lit` (unit Text answers a pointer or
never matches), `int-pow` (answers a pointer-looking value), `int-min-literal`,
`hal-peripheral-linear` (the SAME wrong value 536872972 on both arches),
`scope-try-region` (the fallback's global Text prints empty), `network-effect`
(diverges past line 3). **Those seven have not been re-measured since
2026-07-28; do not read their presence as current.**

## 11. Composable batteries by blast radius

Damian's ask: test packages named for their COVERAGE rather than their purpose,
so a lane mixes the batteries a change can break and runs nothing else, because
the box is the bottleneck.

**The existing tiers do not answer it.** `test.ps1 -Tier` already composes
(`-Tier lib,apps`), so the mechanism is present and the NAMES are the gap:
`lang`, `lib`, `fw`, `apps`, `hardware`, `traps`, `slow` and `oracles` are
LOCATIONS in `codex/test`, not subsystems a change can break. The default `lang`
tier IS the root directory, holding kernel capability tests, arch boot tests and
language pins in one bucket, so a lane that changed the kernel has no word for
what it needs.

**The batteries are two different KINDS, and one mechanism for both would report
green over the two that matter (L-AXIS).** The compiler and the plugs have no
cite edges from `codex/test` at all: the compiler because it is global by
construction (`build.ps1`), the plugs because their harnesses live beside them.

- **CORPUS batteries** select chapters by the subsystem their cites land in,
  derived per run and never stored: `foreword`, `kernel`, `apps`, and
  `compiler` by the rule below. `foreword` is the name Damian's list does not
  give and it is the largest class by a distance.
- **PHASE batteries** run named harnesses, because their subject has no corpus
  to select: `plugs` (`plug-binary`, `plug-smoke`, `plug-selftest`) and `board`
  (the chapters carrying a machine sidecar plus `boards-test`). The `hardware`
  tier already computes almost exactly the board set, so `board` is that tier
  renamed rather than new code, with one correction it needs anyway:
  **`$machineSidecars` in `build/test.ps1:190` lists FIVE extensions and omits
  `.disk-src`**, which step 10 names as a machine-sidecar class and which
  `codex/test/manifest-pin.disk-src` carries (verified at head 2026-09-09).
  That chapter attaches a compiled CDX as a disk and cannot run without a
  machine, so it is a one-chapter hole in the tier today.

**The index must key on (quire, chapter), with the quire read from
`build/quire-map.ps1`.** Deriving a quire from a directory's last segment is
wrong in both directions (`codex/os/core` is quire OS rather than the Foreword,
and `Games` is `apps/games/classic`, whose last segment names no quire), and 66
chapter names are defined in more than one place (`Console` and `VirtioBlk` in
both `codex/foreword/core` and `codex/os/kernel`; `BitmapFont`, `DriveManager`,
`SystemDb` colliding with `apps/**`), so a first-writer-wins index assigns every
one of them to whichever file was walked first.
`check-cite-names.ps1` already dot-sources the map.
`check-test-compile.ps1:115` deliberately does NOT match the quire, which is
right for a CHECK, where over-inclusion is the safe direction, and wrong for a
SELECTOR, where it is both over- and under-inclusive at once.

**A CITE IS A DEPENDENCY, NOT A SUBJECT, and that breaks the kernel battery
silently.** A test cites what it needs in order to COMPILE, not what it is
ABOUT. For library tests the two coincide; for machine-side tests they
systematically do not. `codex/test/hpet-interrupt.codex` declares `Chapter:
HpetInterrupt`, `grounds Device.Mmio`, pins the HPET counter at `#FED00010` and
the IOAPIC at `#FEC00000`, which is the subject of `codex/os/kernel/Hpet.codex`,
and its ONLY cite is `Foreword chapter Board`. A cite-derived selector files it
under `foreword`; it is not selected by `kernel` and it is not in the residue
either, BECAUSE IT HAS A CITE, so the run reports nothing missing. A no-cite
residue list catches the honest gap and cannot see this one.

**NEEDING A MACHINE AND COVERING A SUBSYSTEM ARE ORTHOGONAL AXES.** A machine
sidecar (`.disk`, `.keys`, `.smp`, `.vmargs`) is a hard fact about what the
runner must attach: a COST. A cite is a claim about what the chapter depends on:
a SUBJECT. `fat16-write` carries a `.disk` and cites the foreword, and both are
correct simultaneously. Any rule that reads one axis as evidence about the other
fires on chapters that are classified correctly, and a check that cries wolf is
one nobody reads. `.no-cross` is likewise not an independent label for the
machine axis: it means only "does not run on a cross board", and its stated
reasons are heterogeneous (x86 port and MMIO hardware, no block device on the
cross lane, an x86-64 EMITTER difference, a riscv PLUG gap).

**The FAT family is the clean case, and it is worth stating because the names
invite the other conclusion.** `Foreword chapter Fat16` reaches `Foreword
chapter VirtioBlk`, which cites nothing, while `codex/os/kernel/VirtioBlk.codex`
is a SEPARATE implementation citing `Kernel chapter VirtioPci`. Changing the
kernel's block driver cannot break `fat16-write`, and the shared name is the
whole trap.

**`.covers` is the AUTHORITY, not a patch for the residue.** A per-chapter
`.covers` sidecar naming the subsystem and the reason (the `.no-cross` shape) is
read FIRST; the cite graph is the fallback for chapters that do not carry one.
The tempting reading that a chapter citing nothing tests the language is FALSE:
that set holds the `cap-*` family (the process table IS the subject),
`arm64-boot-test`, `arm64-net-gate`, `arm64-proc-cells` and
`block-select-drives` beside genuine language pins like `arith-narrow-proven`.
An unclassified chapter belongs to NO battery, and every battery prints the size
of the set it selected FROM beside its own count (L-DENOM). Battery membership
is a SET, not a partition: a chapter citing both the foreword and the kernel is
in both, which is what "run the batteries a change can break" requires.
`.covers` is orphan-checked by `build/check-sidecars.ps1`, so renaming a test
cannot leave its judgement pointing at nothing.

**`compiler` selects a corpus by a mechanically decidable rule: cites nothing
AND carries a machine sidecar** (red's decision, 2026-09-07). The reasoning
needs no run: **a test that cites no chapter can reach nothing but builtins, and
builtins are emitted by the compiler.** Every primitive the 13 members stand on
is compiler-side: `process-spawn` and `process-spawn-on-core` in
`codex/compiler/Emit/X86_64Boot.codex`, `X86_64Helpers.codex` and
`X86_64ProcessHelpers.codex`; `block-read-sector`, `block-select` and
`block-sector-count` in `codex/compiler/Types/Builtins.codex` emitted through
`X86_64Helpers.codex`; the per-core TSS and IST1 descriptors `smp-tss` pins
exist only under `codex/compiler/Emit/X86_64*.codex`. `OsScheduler.codex` and
`CoreHeap.codex` under `codex/os/sched` are a different layer these tests never
enter, because entering it would require citing it, so `kernel` would have been
actively WRONG: it would run 11 tests on every kernel change that no kernel
change can break and leave them unselected by the thing that CAN break them.

`smp-arm64-boot` and `smp-riscv-boot` are the two members of that set whose
subject is a PLUG (their AP stubs are in
`codex/plugs/arm64/Arm64Runtime.codex` and `codex/plugs/riscv/RiscVRuntime.codex`),
and the `plugs` battery selects no chapters by design.

**A battery that starts no machine must not be in `$softwareOnly`.**
`check-battery-coverage.ps1` sends any machine-side chapter landing only in a
software-only battery to the review queue, so leaving `compiler` in that set
would have moved its 13 members from "in no battery" to "in the review queue",
which is the same state wearing a different name (L-RENAMED). Every member of
`compiler` carries a machine sidecar, so `compiler` is out of it.

**BUILT: `build/check-battery-coverage.ps1`.** It builds the (quire, chapter)
index, classifies every chapter under `codex/test` by `.covers` then by cites,
prints each battery's count against the set it selected FROM, and prints the
review queue. It needs no guest and no kernel. **Counts at head 2026-09-07:**
foreword 1,319, kernel 304, board 13, apps 243, compiler 13, plugs 0,
unclassified 297, review queue 0; cite names resolving to no chapter, 2, both of
them the deliberate error fixtures (`missing-cite`, `unregistered-quire-cite`),
which is what a correct index should leave.

**The controls, and none of them is optional.**

- *positive:* editing a chapter's `cites` line moves it between batteries on the
  next derivation.
- *negative:* a change touching only `apps/**` selects zero `kernel` chapters.
- *the one that decides whether any of it is real:* a battery selecting a
  subject-shaped set is not evidence it can FAIL on that subject (L-VACUOUS).
  For EACH battery, sabotage one member by corrupting its `.expected` and
  require that battery to go red and the others to stay green. A sabotage that
  moves no colour is the corpus saying it cannot reach the branch
  (L-CONSTRUCT), not a passing control.
- *the residue arm:* a chapter with no cite and no `.covers` appears in the
  unclassified list and in no battery's count. Fabricate one and check both
  halves.
- *`.covers` precedence:* give a chapter a `.covers` that contradicts its cites
  and check the sidecar wins in both directions, the battery it joins and the
  one it leaves.
- *the review queue is not a control and must not be dressed as one.* It is
  non-empty at head by construction, so it cannot pass or fail and proves
  nothing on its own (L-VACUOUS). What it does is put a per-chapter judgement in
  front of a person; the `.covers` that follows is the record that the judgement
  was made.
- *after a control run the tree is in the CONTROL state*, and that is what ships
  if nobody checks. Remove the control file and verify its absence.

**Names are not the instrument.** `cap-*`, `arm64-*` and `block-*` all name
their subsystem, and step 8 records that names are a proven weak instrument in
`build/`. A name prefix is a hint for writing a `.covers` by hand, never a
selector.

**The cost to watch is not the release, it is the per-gate guest count.** A
kernel change selects 20 more chapters in every lane's gate than before the
2026-09-07 sidecars, and 19 of the 20 join for the same reason, so the number
moves again the moment anything else reaches `Kernel chapter Pci`. Re-measure
rather than quoting this line (L-COUNT).

**What this does not do.** It selects tests; it does not decide when a battery
runs. `-Internal` is banned and this step does not revive it: the batteries are
what a LANE invokes for the change in front of it, and the release gate is
unchanged. Nothing enters the default gate without Damian's call. Build the
instrument; do not gate it.

**Open, and Damian's call: whether the `compiler` battery joins the release
sweep.** Adding it lengthens every release, which is his cost. The
recommendation is to add it: the gap lands on releases today regardless, just
later and with the cause further away.

## 12. A load generator with a bounded memory envelope

**What it is for.** Some arms can only be read under sustained contention, and
nothing here can supply it safely. `diag-arm.ps1`'s `b3-record` and
`b3-clockstuck` are the named customers (`DiagnosticStick.md`): the BVT is not
an instrument for them, because `bvt.ps1 -Jobs 4` lasts 28.4 s against a
rehearsal of about 50 minutes and is gone long before b3, and a sustained loop
of it takes free RAM to 733 MiB, where a starved guest and a slow one are the
same colour on the row.

**BUILT: `build/box-load.ps1`** (fester, 2026-09-09).

**The envelope is a FLOOR on free memory, not a count of guests.** The
generator measures free memory before every wave and derives its slot count as
`(free - floor) / per-slot`, so the subject's guests are never the ones
squeezed and the load adapts to whatever else the box is doing. If free memory
will not carry one slot above the floor, the wave is SKIPPED and the stall is
counted: the generator never takes the box below its floor to keep its own
number up.

**The per-slot figure is a REQUEST until a wave has run, and what a slot costs
is MEASURED** (L-REQUEST). It depends entirely on the subject:
`field-range-proven` costs about 0.17 GiB a slot and
`apps/foreword-all-compile` about 0.88 (measured 2026-09-09, and re-measured
rather than quoted, L-COUNT). A parameter alone would therefore grant a heavy
subject four times too many slots. So the first wave is capped at two slots and
is the learning one, every wave since raises the figure to the largest cost
actually observed, the learned figure is priced 25% high, and the slot count
grows by at most one a wave. **The margin and the growth cap are not caution,
they are the repair for a measured breach:** without them the estimate, refined
only AFTER a wave has run, opened three slots on its second wave and took free
memory to 1.26 GiB against a 1.5 GiB floor while the run reported OK, because
the grading tolerated a slot's undershoot. The floor is graded with no slack
now, and the same subject holds at 1.96 GiB.

**The load unit is a COMPILE guest**, chosen because it carries its own
verdict, which is what lets the generator tell that it is still doing WORK
rather than merely still running.

**The generator must be able to say it was loading, or the rehearsal beside it
is green for the wrong reason** (L-FASTER: a component that silently stops
doing the work makes the probe beside it report a better number, so the reward
gradient points straight at the defect). It therefore prints, and grades, a
DUTY figure: the fraction of wall clock with at least one guest in flight. It
exits non-zero on a duty below its declared floor, on a compile that failed for
any reason but the stop signal, and on a wave that could never start. A run
whose duty is 0.4 did not apply the load the rehearsal's verdict assumes, and
saying so is the whole point of the instrument.

**Stopping is a duration and a stop file**, so a rehearsal starts it, runs, and
stops it without a kill, and the summary is written even when the stop arrives
early.

**What it is not:** not a gate, not a battery, and it takes no token. It is a
lane instrument, and the arms that use it name it in their own verdict.

**Every failure class was fired before the instrument was believed** (L-FALSIF,
L-VACUOUS), each on the shipped text: a floor above free memory refuses with
`no wave ever started` and counts its stalls; a duty floor of 0.999 fails a
real run at 0.939; a subject under `codex/test/errors` fails 30 of 30 compiles
and says so; and a floor set just under free memory fires the breach line at
3.45 GiB against 3.5. The control is the default subject, 189 guests over 60 s
at duty 0.948 with free memory never below 3.59 GiB, and the stop file ends a
five-minute run at 12.9 s with the summary written.

## THIRTEEN CHAPTERS ARE REACHABLE BY NO TIER, INCLUDING `-All`

`test.ps1` enumerates tiers from a fixed list of six directories, NON
recursively (`Get-ChildItem "$d\*.codex"`): `codex\test`, `codex\test\ops`,
`codex\test\errors`, `codex\test\apps`, `codex\test\forewords`,
`codex\test\lib`. `$allDirs`, which the `hardware`, `traps` and `slow` tiers
sweep, is the SAME six. So a subdirectory of `codex/test` that nobody added to
that list is invisible to every tier and to `-All`, which is built from tiers.

Three such directories exist, holding **13 chapters, 10 of them gradeable**
(measured 2026-09-07), none skipped, slow or fatal:

| directory | chapters | gradeable |
|---|---|---|
| `codex/test/cost` | 5 | 2 |
| `codex/test/ui` | 7 | 7 |
| `codex/test/examples` | 1 | 1 |

The arithmetic closed exactly at that measurement: 1,724 chapters lived in the
six named directories and 1,737 existed.

**The classifier walks `codex/test` recursively and the runner does not**, which
is two selectors for one question, disagreeing by construction rather than by
drift. The runner is honest about it, printing "of 304 the classifier listed"
rather than reporting 303 as the whole, so nothing is hidden; but nothing fails
either, and a battery that selects a chapter the runner cannot reach is a green
over a test nobody ran (L-DENOM).

**Not fixed, because wiring three directories into the tiers enlarges `-All`,
which is the release net and red's to clear.** The question is also not purely
mechanical: `cost/` and `ui/` plainly want tiers of their own or a home in an
existing one, while `examples/` holds a single chapter and may be deliberate.
Whoever wires them states which tier each joins and why, or writes down the
reason a directory must stay unreachable.

## What "unclassified" is (measured 2026-09-07)

**310 of 1,735 test chapters were in no battery, and that is three different
things, not one number.**

- **118 are error fixtures** under `codex/test/errors`. They must NOT compile;
  their home is `check-errors`, which runs them all. No battery should select
  them and none does. Correct.
- **178 cite nothing and carry no machine sidecar.** What these need is a shape
  statement rather than a sweep: the compiler-side harnesses (the fixed-point
  core, the BVT, `sem-equiv`, `check-errors`) are what stand behind them, and no
  battery claims them.
- **13 cite nothing AND carry a machine sidecar.** These are now the `compiler`
  battery, by the rule above.

**The denominator, stated so the number cannot be read as coverage** (L-DENOM):
1,735 was every chapter under `codex/test`, and the batteries are a SET over it,
not a partition. 310 in no battery is not 310 untested. What the number bounds
is how much a lane running only the batteries its change can break never
selects.

## Measurement notes (so the numbers can be re-derived)

Instrumentation lives in `build/test.ps1` (phase stopwatches, per-test
`.run-ms`, `_results/_timings.tsv`) and `build/test-compile-batch.ps1`
(resolve/vm/parse split in the sweep log, per-test `.src-bytes`). The gate
(`build.ps1` -> `bvt.ps1`, `test-run.ps1`) is untouched. Battery runs for
measurement happen under Damian's standing grant to red's lane, never on private
initiative.
