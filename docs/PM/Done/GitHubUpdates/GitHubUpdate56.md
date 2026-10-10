# GitHub Update 56

**PUSHED 2026-09-08 as commit `6cd2ca1bd9dddca5e6358021bbc0fb5d6d5e874e`,
874 files, to github `master` and gitlab `main`, no force.** Verified at the
remotes rather than locally. Shipped: seed `D9CF240465C3D0BC` at 3,217,563
bytes, `seed/Codex.img` `066BC1D70211A485`, `seed/Codex.map` 187,294 bytes,
`build/boot/diag.img` unchanged at `6F077EEB` with its own 50-of-50
rehearsal record. 10,132 files on the mirror. Verified absent from the
PUSHED tree, not merely from the staging list: `apps/games/magic/` 0, the
ten third-party specifications 0, the seven `diag-sitting*.cfg` 0.
`docs/Reference/CONTENTS.md`, which had never been on the mirror, ships in
this commit.

**Scope: main CLs after the Update 55 release push commit.** Update 55
covers the cycle from the Update 54 push through its release head (seed
`BBB9907CBE21CB16`, 2026-09-02: trapping integer arithmetic, memory stage 3,
the apps on the landing page, safari-codex, the box and main rule).
Accumulate this cycle's themes here as they land; every number in the final
report gets re-measured at the release head, not carried forward (L-COUNT).

## Open from Update 55

- **COMPILER-42, the rewrite pass** (blu): the ownership analysis and the
  copying helpers are on main with the pass OFF; applied to the compiler
  itself it crashes in `mcopy-labels` (22254), and the fix named on the
  row is that the chunk functions in `opening.codex` must return the list
  they build.
- **safari on the site** (val): the port runs and grades; the page and
  card are staged.
- **plugs 2.26**: 48 plug runners still write their IR to one fixed scratch
  path (L-SHARED); wgsl is fixed. Unowned.
- **The seed install runner** (fester, `build/sign-seed.ps1`): landed
  2026-09-07 as the lane-invocable signer, the gate's sign phase lifted
  verbatim, and every lane seed of 2026-09-08 went through it; the other
  half of the finding, seeds built without `-Repl`, is P-REPL and the
  `-Repl` seeds below.
- **The arm64 and riscv ban's scope**: whether emission-only compiler work
  on those backends is covered (blu's CL4, red's 2.21 halves, plugs 2.06).
  Damian's.

## Landed this cycle

570 changelists reached `//Codex/main` between the Update 55 release head
(main 22310) and main 24062 on 2026-09-08 (measured 2026-09-08 by
`p4 changes //Codex/main/...@22312,@now`, merge-downs and empty copy-ups
excluded; re-measured at the release head in the proofs section). Fifteen
of them moved the seed: blu's PR 127 and PR 117 ingestions (22435, 22475,
22500) and the CostModel rungs (23058); fester's COMPILER-30 lazy and
for-comprehension repairs (22544, 22584), COMPILER-35 (22739) and
COMPILER-44 (23158); red's Prism host sockets (23339, 23375), COMPILER-63
(23574, 23655), COMPILER-66's emitted half (23732), COMPILER-32 (23859) and
COMPILER-67 (23894).

**Chess, complete, playable in the arcade** (val, GAME-10). Board and
pseudo-legal move generation, then castling, en passant and promotion,
then legality with check, mate and stalemate (perft 4 at 197,281), then
the fifty-move clock and exact threefold repetition, which finished the
rule set. A player follows (material evaluation, alpha-beta, a
decidable-tactics arm), and the last stage puts chess in the arcade page
with its own wasm module.

**The IoT protocol stack reaches the wire, encrypted** (blu,
`ProtocolStack.md`). `coaps://` as CoAP over DTLS application data, then
an anti-replay window on the application-data path, then a second one on
the handshake epoch, then CoAP message-id dedup so a retransmitted
Confirmable is re-acknowledged, then MQTT over TLS, then LwM2M over
coaps with its registration lifecycle. The firmware path gained a
production caller for `boot-commit`, gated on a completed LwM2M
re-registration.

**Two security defects in that stack, both found by an arm and both
repaired.** The authenticated DTLS server flight shipped three records
at record sequence 0: `dtls-ep-server-emit-msgs` numbered records by the
difference of two `list-push` accumulator names, and `list-push` writes
through the caller's pointer, so every difference was 0 (L-ALIAS).
Certificate, CertificateVerify and Finished therefore went out under one
AES-GCM key at one nonce, from the day authenticated DTLS landed. The
second: the anti-replay window `Dtls.codex` owns had no caller anywhere
except its own test (L-UNCALLED).

**Generic equality in the compiler** (red, COMPILER-63 through 66).
`gen-eq-def` synthesises `__eq_<T>` for every sum whose fields can be
compared, an instantiated helper is minted for a generic sum and
attached to the wire, `lower-def` emits the stripped type so an
unrelated name's sort position stops deciding a definition's shape, two
dead type helpers go, and the BVT gains one equality test per mechanism.

**Prism serves from a hosted binary** (red, PRISM-10, stages 5c and 5d).
One builtin family for host sockets with a Linux half and a Windows half
through ws2_32, a serve loop in `HostedServe.codex`, a route catalogue
that cites no Net, and the in-tab template compile.

**The plug fleet's arithmetic and integer types** (reek). Exact integer
power across java, wpf, winforms, maui, csharp, compose, go, swiftui,
clojure, kotlin, scala and flutter; a Codex Integer emitted as a BigInt
on all eight JavaScript-number targets; the Codex Integer widened to 64
bits in java, kotlin and scala; the riscv literal encoder repaired above
`0x7FFFFFFF7FFFFFFF` and in the top 2048 of the 32-bit range; sum
equality reaching the fields on both arm64 and riscv; over-application
and partial application across 27 emitters; all 40 lenses streaming, so
the whole compiler emits through each.

**The desk** (val, `ShellRefinement.md` and the works backlog). Hover
preview, restore size, a virtual-desktop drag clamp, the task frame with
hot-launch pills, event sounds, one full name per pane, the Web Server
pane, and a clock application with a calendar and zone model, an
analogue face and a Mercator map.

**Preemptive scheduling reaches metal** (val). The spawner is the
holder, the core-0 pin is lifted, and a service runs on an application
processor with the desk loop yielding per iteration.

**The diagnostic stick and the last sitting** (fester, blu, red). A
network record channel that opens before the first bank write and ships
every bank line live, sitting 15's flight image certified over 50 arms,
the USB mass-storage driver's missing SYNCHRONIZE CACHE, and a per-step
bank so a wedge names its own step (L-BANK).

**The registers were cut to what is open** (every lane, R-HISTORY).
`LESSONS.md` went 62,115 to 21,097 bytes with all 73 ids kept and every
account moved to a story or verified at the pointer its row names; the
compiler and plugs backlogs were audited tranche by tranche; CurrentPlan
was cut to its open items.

**Outside contributions.** Steve Howell's PRs 117, 118, 119, 127, 128,
129, 130, 131, 132, 133 and 134 are ingested (the zig plug, wgsl shaders Firefox
accepts, the hosted compiler's check compact, the once-per-unit citation
scope of 132 at 22500, the CRLF foreword chapters of 133 as COMPILER-54 at
22466, and COMPILER-66), his
untracked submissions 106, 107 and 108 are closed and verified at head,
and his issue 94 was ruled on by Damian: the peel helpers answer the
no-expectation sentinel for a non-arrow (COMPILER-32, 23859), measured as a
two-byte change to the compiler with every wire we have identical.

**The `.codex` printer is source preserving again** (red, COMPILER-67,
23894). The equality arc lowered a sum comparison to a synthesised
`__eq_<T>` call and TEXT mode printed the lowered form, so semantic
equivalence failed on 14 definitions at the release gate; the printer
hands the call back as the operator the source wrote, the 14 go to 0, and
the text round trip is byte-identical.

**Every seed is built the way the gate builds it** (red, P-REPL). Three
seeds of this cycle (23574, 23655, 23732) were compiled without
`compile.ps1 -Repl`, shipped in Exit mode, and ended a batch VM after its
first member; the BVT never batches, so it passed all three (COMPILER-68).
Both seeds landed on 2026-09-08 are `-Repl` builds and a 2-member batch
proves each; the lane recipe now names the flag.

**Sheets, a spreadsheet on the desk** (val, SHEET-1 through SHEET-8): the
cell store, the formula parser, the dependency graph and evaluator, CSV
in and out, lookups, a view pane, and a chain stress arm.

**Composable test batteries by blast radius** (fester, `BatteryReorg.md`
steps 1 to 11): the battery is composed from the subjects a change can
reach, the app sweep selects its subjects the same way, and the
`apps/data` chapters were swept in.

**The build scripts move into Codex** (reek, `PipelineModel.md`): 17 of
the 58 generated build scripts now come from generators under
`codex/build/`, `ShellDslReadability` step 0 puts a ratchet on raw shell
text, and the star map's catalogue is HYG at v2.

**The compiler register audited at head** (red, L-ROWROT, fourteen
slices, main 23749 to 24060): every row verified against the tree and the
remote; fifteen rows deleted as landed (COMPILER-13, 21, 25, 26, 36, 38,
40, 42, 44, 47, 49, 51, 52, 58 and 67), eight rewritten to their live residue,
a dozen drifted citations corrected, two new rows (COMPILER-68, the BVT
never batches; COMPILER-69, an unused `let` bound to a partial application
compiles clean and does nothing, val's release red).

## What the release proofs found

- **Semantic equivalence was red at head on 14 definitions, all one
  shape**, and the shape was this cycle's own equality arc: the printer
  emitted `__eq_TokenKind (current-kind st) kind` where the source reads
  `current-kind st = kind` (blu found it; COMPILER-67 above closed it).
  The text leg runs only in the release gate (L-NOGATE), so the regression
  sat unobserved from its landing to the push.
- **`check-errors` was red on `unit-field-assign`**: a three-column prose
  line in the fixture lexed as code and 22739's CDX0008 fired on the
  apostrophe pair in the sentence. blu rewrote the fixture's prose; the
  compiler defect (COMPILER-55, Steve Howell's issue 120) stays open with
  this as its first measured bite.
- **`test-compile-batch` ended after member 1 on the three Exit-mode
  seeds** (root's proof: a 2-member batch on seed #760 gives both `.cdx`,
  on #768 and #769 member 1 only). Not codex-vm and not the script: the
  seeds were not built `-Repl`. Repaired by rebuilding (COMPILER-68 for the
  gap in the BVT).
- **A desk test painted nothing and compiled clean** (val, 23978):
  `desk-cursor-arrow` called a nine-argument paint function with eight,
  bound the partial application to an unused `let`, and printed nothing
  where `198 136 0` belongs. Registered as COMPILER-69.
- **Two proof runs died without a diagnostic, and the two causes are
  different.** The first release gate stopped mid-phase with no refusal
  line on a box root measured at ZERO guests and 5.38 GiB free, so memory
  was not the cause: it was launched by `Start-Process` from inside a tool
  call and was therefore still a member of that call's process tree. The
  same gate, relaunched outside the tree, ran straight through the line it
  had died on. `OperatorsManual.md` had named `Start-Process` as the
  MITIGATION for exactly that failure and is corrected. The third gate died
  in the stage-1 CDX compile with three lanes' guests beside it, and that
  one was the overcommit: the same phase ran clean when root held every
  other guest. The 2.3 GiB reading taken during the battery was not a kill
  condition at all but the battery's own admission-limited footprint, which
  its `[vm admission]` line predicts and which stayed flat for six minutes.
  The day then ran on one non-release guest at a time, granted FIFO by root.

- **The push reconcile hid every exact-path ignore rule** (blu, landed).
  `git check-ignore --stdin` fed CRLF returns only paths matched by a
  DIRECTORY rule and silently drops every exact-path one, so the survivor
  list carried all ten third-party specifications the 2026-09-07 rule
  withholds. Measured both directions on the same three paths; the recipe
  in `PublicPush.md` is corrected, the ignored set went 417 to 430 and the
  survivors 323 to 310. Seven `build/boot/diag-sitting*.cfg`, each carrying
  a real LAN address, were withheld by one sentence of prose and are in
  `.gitignore` now, falsified both ways.
- **Two rows opened, neither a blocker** (blu). The poison build has no
  positive control: a green poison battery and an inert `-Poison` flag are
  the same colour, and no chapter at head reads an uninitialized field
  (`ExaminersAssay.md`). The diag image hash is not reproducible across
  workspaces because `DIAG.RCP` bakes the absolute cfg path
  (`DiagnosticStick.md`).
- **The seed on main IS the fixed point of its source this time.** The
  full gate's `build/output/Sut.cdx`, `seed/Codex.cdx` and
  `build-output/bare-metal/Codex.cdx` are byte-identical, so the finding
  of Updates 54 and 55 does not recur; the seed's full digest is in
  `TechnicalDetails.md`, where `check-doc-counts` enforces it.

**The proofs (blu, `build/update56-proofs.md` on blu's stream). The gate
proved main 23978 in every phase but the app-class sweep; the sweep's one
regression (`apps/works/GopBoot.codex`, CDX3002) was fixed by val and the
sweep alone reran at 24007; the pre-push rerun was scoped by the cite
graph, 51 arms where changed chapters alone would have run 6; the push
head is main 24062:**

| proof | result |
|---|---|
| Full gate (every phase) | green at 23978; the app-class sweep alone rerun green at 24007 |
| Sut === seed | `build/output/Sut.cdx`, `seed/Codex.cdx` and `build-output/bare-metal/Codex.cdx` byte-identical, 3,217,563 bytes |
| ir-fidelity `-Grade` | green, 8 cases, unexpected 0, 24 compiles in 13.2 s, every case naming the release seed as its kernel |
| Battery `-Tier all` | 1,788 total, 1,739 pass, **0 fail**, 49 skip; compile 485 s, run 205 s at 4 slots; oracles scalar 2013/2013, vector 130/130, cce 1485/1516 with 31 in documented gaps and 0 unexplained |
| App sweep | 294 units, 291 clean, 3 known-dirty, 0 regressions, 4.6 min at the fix head, kernel named as the release seed |
| Poison (`-Poison`, `-Tier all`) | poison seed `BC28B4D1ADC90502`, 3,217,571 bytes; 1,788 total, 1,739 pass, **0 fail**, 49 skip; newly red 0 against the real battery |
| DDC witness | **HOLDS**: both arms 3,217,563 bytes, differing only inside the signature region 40..135 and 0 bytes outside it; Roslyn took the emitted compiler with 0 errors. The differing-byte count inside that region is the region's width less the bytes two unrelated signatures happen to share, so it varies by one or two between releases and is not a target |
| `check-doc-counts` | exits 0 on all 61 claims; 20 had drifted and are corrected in `TechnicalDetails.md` at main 24027 |
| Diag | **NOT refreshed this cycle, and the shipped `build/boot/diag.img` is unchanged at `6F077EEB`**, which carries its own 50-of-50 rehearsal record taken 2026-09-08 against those exact bytes, so L-ARTIFACT is satisfied by the record rather than by a fresh run. A rebuild against the release seed was made and WITHDRAWN: it is a new artifact (its recipe names `EFE7A6AC9103A884`, two seeds back) and it could not earn a rehearsal, because the harness fails four NIC arms (`b3-pass`, `b3-record`, `b3-clockstuck`, `nic-kills-msc`) in the full 50-arm sequence while each of them PASSES in isolation on that image and on the shipped one alike. Reproduced in two consecutive full runs with identical failure text, so it is a deterministic ordering defect in the rehearsal harness and not the image. What was not run, and what would separate the two readings for certain, is the full sequence on the shipped image |
