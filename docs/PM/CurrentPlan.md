# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). A closed item is DELETED, not
annotated. How something was hunted lives in the CL, the GitHubUpdate for
its cycle, or the doc named beside the item; a tombstone or a war story
added here is scavenged again; write the pointer instead.*

**THE HISTORY PASS over `docs/Designs/Active/`, `docs/Agents/` and this file
is closed (2026-09-09; the CLs are the record).** The rule it applied is
R-HISTORY: per-block judgement, a lesson becomes a `LESSONS.md` row, a trap
a P- or L- pointer, an open item a register row, a finished design moves to
`Done/`. A second tranche over the reference docs (`OperatorsManual`,
`ExaminersAssay`, `DevelopersGuide`, `ArchitectsSketchbook`,
`HardwareSitting`, 1.35 MB together) is NOT ordered; Damian says when.

## FLEET COMMAND (Damian, 2026-09-12)

ROOT commands the fleet; red assists. The lane table is the current
dispatch. Root's active PR ingestion and release work is managed in root's
session and is excluded from red's fleet workspace reconciliation.
MethodSpecialization shelf 25624 is WIP and unproven; preserve the shelf
until the owning design's acceptance and seed proof are complete.
A red that is red on BOTH the old and the new plug
and that the change does not touch is a register row, not a promotion block
(root's ruling, 2026-09-12, on eq-generic-fields a_ and typeclass-smoke T578
under the Zig plug).
The fleet runs on the stable AgentGrid deployment. AgentGrid integration
continues under Potato; outstanding
acceptance gaps remain in D:/Projects/AgentGrid/agentgrid-backlog.md.
These dispatches use current AGENTS.md R-GATE and runtime RAM admission,
which override older blanket job-count and gate statements below. A changed
build script alone does not establish seed reachability; inspect the inputs.
Existing unverified shelves remain preserved and require their own proof
before landing. All five coder sessions are running Astra; task-scoped workers
do not acquire fleet seats or mailboxes.

**Outside contributions (Damian, 2026-09-10).** A PR is ingested through
PerforceProcess.md section 7 with the contributor credited in the CL; the PR
and its issue stay open until Git publication, then close with the commit,
the CL and the credit paragraph. No GitHub merge button. The Update 59 intake
(PR 135 to 145) is closed; issues 110, 115 and 126 stay open with receipts.

**Parallel shelf work is authorized (Damian, 2026-09-10).** Each lane owns
one bounded unit below and runs scratch proofs while another lane resolves a
dependency; numbered shelves with explicit dependencies, not unproven main
changes. Preserved interrupted work: 25624 (root, MethodSpecialization,
unproven), 25558 (root, original PR 135, rollback only), 25599 (fester,
unfinished instance declarations and superclass typing), and 25630 (fester,
independent method-template acceptance fixtures and checker). Shelf 25599's
equality changes are already on main; restore only the remaining delta.
Shelf 25630 contains the inspection runner and agreed v1 interface; actual
producer acceptance and reservation/source-offset witnesses remain ungraded.
Rebase retained work onto current main before fresh proof. A fixed point
does not grade typed IR or hosted output. Root's active intake shelves are
separate from this interrupted-work set.

Declared test exclusions are deliberate decisions (Damian, 2026-09-10).
Preserve the runner's prescribed `-Tier all -Jobs 4` selection for normal
and poison release batteries. Report exclusions separately from unexpected failures; do not
remove sidecars or add flags to override those exclusions for the release.

The investigation charter and part ownership remain in
`docs/PM/Active/Stories/TheLostParadise.md`; remaining recommendations stay
in that report's part 11 pending reassignment.

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## THE FLEET DISPOSITION (root commanding)

Renode and the arm64 and riscv beds are IN (Damian, 2026-09-08 15:55; the
2026-09-01 ban is lifted). Docs go straight to main; a code arc gates once per
arc; the batch rules are `CoordinationProtocol.md`. The lanes table below is
the assignment and wins over this section where they disagree. After the
release, the next plugs row is reek's, in `plugs-backlog.md` order (the CCE
census row, not a numbered pointer: L-ROWROT).

### The games and the landing site (val)

- A card's tag reads "playable now" only while a visitor can play.
- A game that fails on wasm is a PARITY finding for reek: one message naming
  the subject and the failing test, and no workaround in the game.
- Chess (GAME-10) stays not built and its `games.json` row stays honest.
  **OPEN:** GAME-10's sentence about the landing page is stale and is val's to
  fix when that file is first touched.
- Claims: `apps/games/**` and `apps/landing/**`, except `web/compile/**`, which
  is the Prism page and stays fester's.

### The wasm plug (reek)

Parity with the hosted x86-64 lift is closed (wasm 53 = hosted linux 53,
2026-09-01); wat2wasm stays on the PATH as the assembler (Damian, 2026-09-02).
Open:

- Want 3 of the `build-page-modules` row below: why `wat2wasm` never started
  on `riscv-stdio` (`plugs-backlog.md` line 74 carries the npm-shim candidate;
  unconfirmed).
- **No gate builds a web or wasm bundle.** `app-sweep` compiles the bare-metal
  side and nothing in `build/` invokes `codex/plugs/wasm/build-spark.ps1` or
  `apps/gpushow/tools/validate-all.mjs` (needs a browser). A lane touching a
  browser app inherits a blind spot no gate can see; the per-app gaps are in
  `spark-backlog` SPARK-4, `gpushow-backlog` and `starmap-backlog`.

Claims: `codex/plugs/wasm/**` is reek's; fester keeps
`apps/landing/web/compile/**` and the Prism page; reek announces before
touching `build-page.ps1` or `page-lenses.ps1`. The plug alone takes no token.

## MAIN AND PUBLIC RELEASE

Release head held for Update61 proof. The token remains free; no gate runs under it.

Latest public release: **Update 60** (2026-09-12, commit `9fff850c` on
GitHub master and GitLab main, seed #796 `CF9EDD812EA7E78B`);
`GitHubUpdate60.md` is the account and `GitHubUpdate61.md` accumulates this
cycle. Head seed on main: #799 `7BCD5BC6BCE0AF41` (25806, Ed25519 signing contracts). Re-measure both at
the release head, never carry them forward (L-COUNT).

### Update 61 release continuation (root, 2026-09-17)

Damian authorized publication to both Git mirrors. All release proofs passed:
full gate, IR fidelity, normal and poison batteries, separate app sweep, DDC,
map validation, rebuilt boot images, all50 diagnostic arms and shipping check.
Main25820 adds the proven host timeout override; the compiler source and
seed799 remain the same bytes proved at main25816. Vault team sharing remains
a documented app defect in apps/secrets/secrets-backlog.md.

Main release artifact CL25821 is submitted
in D:/Projects/Cobblestone-root-main, client BigWhite_Codex_root_main.
The checkout and both public mirror tips were9fff850c before publication.
Next: reconcile and stage eligible depot
paths, commit Update61, push github master and gitlab master:main, verify both
tips, then send contributor receipts and close PRs147-152 and issue146.
Follow docs/Agents/PublicPush.md. No renewed publication approval is needed.

Evidence: D:/Projects/ReleaseProofs/Update61-20260917-root/.
The gate/breadth/DDC/images attempt pointers locate the receipts. Image
retry-result.txt is PASS; original result.txt preserves the failed time-budget
run. The full retry used unchanged image bytes and every assertion.
All root-owned proof processes and samplers are stopped; no VM remains active.
Preserve root shelves25558/25624. The compiler heap/time behavior is unchanged
by the final documentation and host timeout change. Restore MAIN OPEN and
remove this continuation after publication and contributor receipts.


## The brand boundary (Damian, 2026-08-29)

The public name is **the Cobblestone Project**: the OS and every brand
surface outside the compiler is Cobblestone; the language, the compiler
and the artifacts stay Codex. The rename campaign is done and nothing in it
is open; the ruled boundary and the traps around brand strings are in
`docs/Designs/Done/Marketing/Cobblestone.md`.

## The network demo pair (Damian, 2026-08-24)

1. **A webserver app in the guios** (val; blu consults on the net side). The
   server is `codex/os/net/WebServer.codex`, not the same-named
   `apps/works/WebServer.codex` (a socketless router). **RULED (Damian,
   2026-08-28): the desktop never gains `Network.*`; the webserver becomes the
   first system SERVICE under a preemptive scheduler and the pane is its admin
   console.** Design: `PreemptiveScheduler.md` (val); register:
   `apps/works/works-backlog.md` WORKS-48 (pane half unblocked, serving half
   is stage 5). The bed is the instrument, via codex-vm NAT port-forward;
   there is no metal (2026-09-09).
2. **The compiler in WASM building itself in a static page** (fester) is
   shipped (`codex/plugs/wasm/page/index.html`, `build-page.ps1`). **Hosting
   that page from our own kernel is a separate later step (Damian); do not
   couple item 1 to it.**

## Track A -- the stick is an OS. THE LAST SITTING HAS FLOWN (2026-09-09): there is no metal any more.

Sitting 15 stopped at the ladder's first write to the part; the result is
`HardwareSitting.md` "THE SITTING QUEUE IS CLOSED". No flight follows, for all
time. "Metal-gated" and "rides the last sitting" are no longer states a
register may carry: an item a bed can answer moves to the bed, and an item
only metal could answer is DELETED as unanswerable by ruling. Each owner
reclassifies its own rows in one docs CL: reek (WORKS-9), fester (A8's launch,
WORKS-24), blu (every Track B question, WORKS-16), root (the composition,
`DiagnosticStick.md`).

- **The I219 medium-death hunt is PARKED (Damian, 2026-08-24)** and does not
  revive on a flight; `I219IsNotAnE1000.md` is the record.
- **A8 the desk build loop (fester)**: `DeskBuildLoop.md`. `compile <path>`
  wired and gated. The launch (`vm-compile-cdx` and below) has no bed
  (codex-vm's guest sees no VT-x) and no metal: fester closes or re-scopes it.
- **Identity, RULED 2026-08-18: the identity file stays on the ESP;
  auto-unlock is bed-only.** Rotation stays with `Designs/Done/OS/Identity.md`.

## Track B -- the network (blu). The bed is the only instrument (2026-09-09).

Every question that rode sitting 15 (NIC-4's successor, NIC-3's `aneg-done`,
B3, ASDE finding 4, NIC-5, B4 step 6) has its one metal answer: not reached.
blu reclassifies each: bed-answerable stays as a bed item on blu's row or in
`I219IsNotAnE1000.md`; metal-only is deleted.

Rulings that bind this track: **the NETIO ceiling (Damian, 2026-08-21): the
NIC comes first, no stage may end the run** (`net-io-drain-ticks = 96`,
`codex/test/net-drain-budget`); the one live unchecked send path is arm64's
(`Arm64NetIO.codex`, under the deferred OracleCloudArm64). **ICMP is
send-only**: no ping answered, `icmp-parse` stays latent; whoever gives
`syslog-parse` a production caller fixes its quadratic `acc &` accumulator in
the same change.

## Track C -- the trust audit (val)

C1 and C2 are landed and enforced (`IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`). The rechecker fork is CALLED (red,
2026-08-20, rulings queue 3; L-CAPABILITY-LOST) and is not open. **C2.5
stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce

The census, the ranked queue (10.1, take order in its last paragraph) and
how a row can be wrong (10.3) are `VerifiedFormatParsing.md` section 10;
the guard pattern is settled in `ExaminersAssay.md` (clamp where a length
decides a slice, refuse where it decides WHERE a read lands, the ablated
call IN the arm). Still open in 10.1, unowned unless named:

- 8b, `VirtioBlk`'s device-written used-ring index: waits for a bed.
- 18, `OtaBoot boot-load` (reek): LATENT, no production caller.
- The latent corpus rows 6, 7, 11 and 13.

## The Prism dev environment (multi-lane; FALLBACK ONLY)

Damian, 2026-09-07: "we can go back to prism if we are running out of work."
A lane takes a Prism stage only when nothing is live on its own register, and
says so. Design and stage register:
`apps/prism/design/Active/PrismDevEnvironment.md`; row:
`apps/prism/prism-backlog.md` PRISM-7. Two traps bind every page deploy:
rebuild from a seed at or after `7B6A4950`, and regenerate
`build/output/Codex.codex` FIRST (L-SAMEVER).

Open, in order:

1. **Stage 3, templates and the build tab: RED'S**
   (`PrismDevEnvironment.md:689`). Acceptance: a server template built
   in-tab, carried to the host, booted in codex-vm and answering HTTP; the
   page is `codex/plugs/wasm/page/prism.html` in reek's tree.
2. **Stage 4, the Claude panel**: the code is on main and the arm green; the
   acceptance needs a real key and a billed call. The request shape is pinned
   in the design and must not be written from memory. The key is Damian's and
   DEFERRED (2026-09-08).
3. **PreemptiveScheduler stages 1+2** (`PreemptiveScheduler.md`): val's.
4. **Stage 5c, sockets**: RED'S (`PrismDevEnvironment.md:896`), untouched.
5. **The guios webserver app** (WORKS-48): val's.

The one-command public `compile/` page refresh is Damian's. Rulings (Damian,
2026-08-28): the stage-5a Linux bed is ALL of the options (WSL verification
arms, a QEMU Linux guest, 5b's `.exe` verified natively); **boards** means IoT
board build targets per HAL board chapter; **bench** means our codegen
benchmarks against any configured build chain; the zig work (Steve's PRs)
rides LAST.

## The lanes (RULED by Damian 2026-08-15)

**ROOT commands the fleet; red assists (Damian, 2026-09-12).** The table is the
assignment, not a suggestion; re-read it on every merge-down. An item here is
a pointer; the register named beside it holds the detail. The compiler-bug
order is whatever `codex/compiler/compiler-backlog.md` shows open. fester is
otherwise held in reserve for the hardest problems (Damian, 2026-08-26). **The
DeskScheduler is PARKED (Damian, 2026-08-26), not cancelled**:
`DeskScheduler.md` carries two questions only Damian can rule on (rate or
budget; skip or run late on a miss).
| agent | now | then | standing |
|---|---|---|---|
| **blu** | **NOW:** No pending work; await root's assignment. | **NEXT:** On relaunch, check in with root. | Edge-mesh and net items in `ProtocolStack.md`; ICMP parse production caller (ruled send-only). |
| **val** | **NOW:** No pending work; await root's assignment. | **NEXT:** On relaunch, check in with root; the desk and games items in the standing column are val's. | Damian's batch: WORKS-60, the virtual-desktop wording; FW-1's three fix options are Deferred (Damian, 2026-09-08 18:00). WORKS-50 is OFF this batch: it is DONE at head (one full name per pane, `desk-wnd-title` and `gpr-entries` agreeing on all four), and what it leaves is a simplification nobody has taken, dropping `dk-pill-icon`'s second table now that the join is sound. A start-menu group of seven pushes the laid menu past the taskbar band and nothing clips or scrolls: registered in `ShellRefinement.md`, and it is why the Sheets launcher row sits in Accessories. `ShellRefinement.md` "6.4: WHAT IS STILL OPEN"; `PreemptiveScheduler.md` stage 2; WORKS-47/41 |
| **fester** | **NOW:** Preserve unfinished instance-typing donor 25599 and independent MethodSpecialization acceptance shelf 25630. The latter carries the agreed v1 interface, inspection runner and fixture/checker work. Root owns production compiler/common-parser changes and existing typeclass fixtures. | **NEXT:** Rebase acceptance work onto current main when root supplies a fresh candidate. Complete actual-producer acceptance and reservation/source-offset witnesses; retain source-derived declaration accounting, zero demand, omitted entries, scoped/forged binders, lookalike dictionaries, runtime/escape cases, finite bounds and frozen-reader controls. No passing claim before the required controls pass. | **COMPILER-59 and COMPILER-60 are PARKED and measured, not abandoned:** `ir-fidelity -Disagree` counts a def disagreeing with itself and reads 3 sites over 2 programs of 615; the lambda-parameter-span change moves 2 of the 3 and REGRESSES `typeclass-poly`'s `convert`, and the shared-span reading is refuted, both digests in `docs/Designs/Active/Compiler/LambdaParamSpans.md`. The name-the-kernel class is closed: `test-self-verify`, `check-generated-scripts` and `test-cross` all take `-Kernel` (2026-09-07). What the Build register still carries unowned is that `test-cross` BOOTS RENODE whenever the binary exists, guarded only by the operator remembering. A8 desk build loop when VT-x metal is available; Renode out; `deck-headroom`; WORKS-24 rides a sitting; ProductBuilder stage 6 on hold |
| **reek** | **NOW:** No pending work; await root's assignment. | **NEXT:** On relaunch, check in with root for the next open `plugs-backlog.md` unit. | Plugs close-out lane; WORKS-9 metal-gated; `ShellDslReadability.md` same campaign; `tools/codex-vm.c`; a tokenless `codex/foreword/` landing names the closure check in its CL (`PerforceProcess.md`, root 2026-09-08); Blocked on Damian: SPARK-4; Registered: COMPILER-46/39/48 |
| **red** | **NOW:** Scoped crypto primitive audit completed through main25810; no active crypto changes. Bounded account: `GitHubUpdate61.md`. App team-sharing gap: `apps/secrets/secrets-backlog.md`. | **NEXT:** Await root's dispatch; plugs-backlog 2.58, shared network-send heap retention, remains next in the lane's queue. | Releases, personally and end to end; `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; the 4.3 seed hash check runs BEFORE build-complete; appendix F is refreshed with `build/lp-findings-index.ps1` and its table 2 reproduced by hand, which the script overwrites. |
| **root** | **NOW:** Update61 publication authorized. Full gate, IR fidelity, normal/poison batteries, app sweep and DDC pass at main25816. All50 image arms and shipping check pass; artifact CL25821 is landed and awaits publication. | **NEXT:** Resume from "Update 61 release continuation" above. Preserve shelves 25558/25624; finish the release landing and publication, publish both mirrors, and send contributor receipts. | `DiagnosticStick.md` composition; `ComplianceEvidence.md`; `HardwareAbstractionLayer.md` question 5 blocked on a board crypto manual; OracleCloudArm64 deferred; `build/boot/diag/**` (released to red for the two stage lifts) |

**Plugs are reek's close-out lane** (from val, Damian's direction
2026-08-18): the register in order, one entry at a time, said in
status.json. Entries other lanes hold are named in the register (1.33 blu,
1.38 and 1.3 fester, 1.36 and 1.32 reek, 1.34 root). `codex/plugs/zig/**`
is ORDINARY FLEET CODE, edited like any other plug (Damian, 2026-08-18);
credit Steve in a CL that changes what he wrote and flag it in the next
GitHubUpdate, which is courtesy and not a gate.

## Approved campaigns and the pool (Damian, 2026-08-18)

Every open design in `docs/Designs/Active/` is available work; a lane that
empties draws from the pool in order, says so in the table, and strikes the
item when drawn. Taken and NOT available: `HardwareAbstractionLayer.md`
(root), `GameEngine.md` phase 2 (val), `ShellDslReadability.md` (reek),
`ComplianceEvidence.md` (root). **The pool holds NO drawable item.** The
`DeviceEmulationCatalog.md` queue is demand-driven (sittings produce
entries), and `tools/codex-vm.c` carries a file claim. Seed-affecting
campaigns take the token per CL.

## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads. **THE
DOCUMENT THAT OWNS THE SUBJECT WINS** over any ownership claim here, and a row
is verified against head before it is dispatched (L-ROWROT): a measurement
older than the last change to its subject is not evidence (L-COUNT).

- **`docs/Designs/Active/OS/OracleCloudArm64.md`: DEFERRED by Damian
  2026-08-18.** Deferred with it: `codex/os/net/Arm64NetIO.codex` is a full
  twin of the x86 send path carrying NEITHER the checked-send fix NOR the NETIO
  drain cut (still `arm64-net-io-max-ticks` 500). Whoever lifts the deferral
  inherits both; an arm64 TCP send that hangs or truncates before then is this
  row, not a new defect.
- **Six gate phases have their runner under `build/` and do not trigger on it**
  (`jonquil`, `plug-binary`, `cross-smoke`, `plug-smoke`, `app-sweep`,
  `sem-equiv`; reek 2026-08-25; PARKED, not claimed). A per-file trigger is the
  precise fix and blanket-widening `$tBuild` is not (L-LESS). Interim rule:
  whoever edits one of those six harnesses runs its phase by hand and says so.
  Costs are in `Build.md`. The plug half of this was ruled and landed (21438);
  only the harness-edit triggers remain a question.
  `gdb-watchpoint` is proven as a PATH and unproven as a debugging SESSION: a
  real one needs an address from the booted kernel's own map, not the
  compiler's, and probably more than 120 s under TCG.
- **COMPILER-23 residue, UNOWNED**: the two UEFI print loops fixed under
  COMPILER-21 stay ungated, because nothing in `codex/test` runs under `-uefi`
  (L-NOGATE). The remaining CORE-8 residue is the `from-unicode` answering -1
  call-site policy, in `codex/foreword/core/core-backlog.md`.
- **VM admission, UNOWNED.** `test-cross-batch.ps1:331` puts the Renode run
  phase on the `-Jobs` default (8 since 21043) while its own comment refuses
  eight slots; pass `-Jobs 4` explicitly on the Renode leg until somebody
  rules on the default. The admission arithmetic is in `vm-config.ps1` beside
  `Get-VmAdmittedSlots` (`-mem` is a ceiling, not a footprint, L-REQUEST). A
  run launched as a background shell dies with a fleet session restart;
  launch detached (`OperatorsManual.md`).

## Decisions

**Numbers are stable ids, not an order.** A ruled item shrinks to one line
here and its reasoning moves to the doc that owns the work; the number stays
so the citations across `GitHubUpdate*`, `CostModel.md`, `plugs-backlog.md`
and the designs keep resolving. Gaps are expected. A ruled item whose work
has landed is deleted; the CL and the owning doc are the record.

**What belongs in PENDING, and it is a narrow test (Damian, 2026-08-20):**
only a decision he alone can make. An outside relationship, an account, a
spend, a product direction. A technical trade-off with a defensible answer
is the commander's call, not his.

### Pending -- only Damian can answer

- The deskboot digest row (root).
- **Deferred by Damian, not pending:** OCI account access for
  `OracleCloudArm64.md` phases 5b-5d; the Prism stage-4 API key; FW-1's three
  fix options.
- **Not a question until there is a design partner:** secure-element support
  in `Identity` (`ThreatModel.md`'s fourth open question).

Prism is a FUTURE BUILD-OUT (Damian, 2026-09-02): its open items are not
pending decisions and not drawable until he opens the build-out.

### Ruled, work in flight (one line each; reversible in one line)

- **NO RUNNER FOR THE APP ENTRY UNITS (Damian, 2026-09-08 04:15).** 910 of
  1,137 app chapters are executed by nothing and the 295 entry units are
  uncitable (BatteryReorg step 6, main 23269); that stays so. His words: "app
  drift is acceptable during this phase of the project. The blast radius of a
  change to compiler or foreword should not be a gating factor; app lift is a
  secondary concern. That is why there is no runner: it costs too much to
  maintain. Apps are a test bed until the underlying code is more stable."
  Reversible when the compiler and foreword are declared stable.

- **Heavy-pane stranding: option D, FIX THE ALLOCATOR** (Damian, 2026-08-27).
  val's campaign; acceptance is the reopen-after-buried-close row falling
  toward zero, and the frontier table in `ShellRefinement.md` 6.4 carries it.
- **Prism's product scope** (Damian, 2026-08-24): compile/transpile on the
  fly; the pre-baked IR path goes. `apps/prism/prism-backlog.md` is the
  register.
- **plugs 1.34, the ARM64 MMIO boundary: (a)**, gate the MMIO window in the
  effect system; (b), a real EL0 boundary, is a different project.
  Seed-affecting, token. Until it lands the ARM64 capability gate covers the
  `block-*` builtins only. (red, routed from root.)
- **plugs 1.57: the Rulebook's over-application rule binds every plug that
  keeps an arity map** (red, 2026-08-24). The java half stands as ruled; the
  riscv wiring named in the row is INERT and the real miscompile site is
  unnamed, so reek hunts it from the reproducer rather than re-wiring.
  Account: `plugs-backlog.md` 1.57.
- **A ping goes unanswered, deliberately** (red, 2026-08-20): no production
  caller for `icmp-parse` until something needs one. Re-verified 2026-09-07:
  one definition in `Icmp.codex` and six callers, all in `icmp-test`. (Track
  B, blu.)
- **The rechecker keeps deriving type-variable instantiation itself** (red,
  2026-08-20); the compiler does not emit it. That is the fork's whole value
  (L-CAPABILITY-LOST); the abstentions are the price. (Track C, val.)
- **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`,
  `.cross-refusal`, `.no-cross`, `.vmargs`) and warns on everything else.
  (red; ruled, STILL NOT BUILT at 2026-09-07: `p4-stale-check.ps1:56` says
  "Reported as a warning, not a failure" and `Show-Untracked` fails nothing.)
- **zig 0.16.0 is installed at `D:\zig-0.16.0`** (verified present
  2026-09-07).

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex` | fester (kernel-side) |
| `codex/plugs/arm64/Arm64Runtime.codex` | root; the block/servicer sections are fester's by agreement |
| `codex/os/kernel/{VirtioNet,VirtioBlk}.codex`, `codex/plugs/pe/Arm64PeWriter.codex`, `build/build-arm64-img.ps1` and its generator | FREE -- announce |
| `tools/codex-vm.c` | reek, 2026-08-24, for the dead-harness row (red's grant); the row is the shape, not this line. Announce to blu before touching the NAT paths |
| `build/test-cross-batch.ps1` | FREE -- announce |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, the lifted probes) | root, 2026-08-18, `DiagnosticStick.md`. Step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex`, `GopComposite.codex`, `GopFiles.codex`, `GopIcon.codex`, `GopSettings.codex`, `codex/foreword/ui/**` | val, 2026-08-20, the Shell Refinement campaign (`ShellRefinement.md`). Announce-before-you-start stands, and so does checking which `ds` cells are spoken for. `comp-text` stays fester's |
| `apps/works/GopEdit.codex` | FREE -- announce; the Editor's standing rules are `works-desk-contract.md` 0.6 |
| `apps/works/RepoProtocol.codex`, `RepoProtocolPersist.codex` | FREE -- announce |
| `apps/works/AgentBundle.codex`, `codex/test/apps/agent-bundle-*` | FREE -- announce |
| `apps/works/GopReview.codex` | FREE -- announce; `GopFacts.codex` is red's |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `apps/works/GopWeb.codex` and `ds` cell 248 | val, WORKS-48 DONE 2026-09-08: 248 points at the block the desk shares with the web service (`dk-web-cell`). 244 is the pinned-pill mask (`dk-pinned-cell`) and 252 the hover bubble's save block (`dk-bub-cell`); the `ds` block is FULL |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/os/sched/**` and the preemptive scheduler work | val, 2026-08-28, `PreemptiveScheduler.md`. blu keeps `codex/os/net/**`; the scheduler reads that side and does not change it |
| `codex/test/cost/**` and `CostModel.md` | blu; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | reek, the close-out lane (from val, 2026-08-18). Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table). **`codex/plugs/wasm/**` is reek's for the parity campaign (2026-08-31, Damian); fester keeps `apps/landing/web/compile/**`; `build-page.ps1` and `page-lenses.ps1` are RELEASED to reek without announce (fester's row, 2026-09-01, on that lane being parked)** |
| `apps/games/**`, `apps/landing/**` except `web/compile/**` | val, 2026-08-31, the games campaign (Damian; the disposition section). `web/compile/**` is the Prism page and stays fester's |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | reek, with the plugs lane |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | blu, 2026-08-18 |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators | reek, 2026-08-16, by Damian's direction. Catalog and order: `ShellDslReadability.md` |
| `codex/foreword/compress/**` and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 item 18) | reek, 2026-08-16, red's routing. Seed-reachability is measured per file, not assumed from the row |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce, and it takes the token (seed-affecting) |

A claim nobody honours is worse than no claim. Announce before you go into
a claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

**A LANE HANDS OFF AT 70 PERCENT MEASURED, NOT BEFORE (Damian, 2026-09-08
03:35: "58% is not enough action on the context we load at init; agent
estimates of token spend are almost always very much over").** The number is
the AgentGrid formula over the transcript (the handoff skill carries the
script); a lane's own estimate is not a number. Below 70 measured a lane
keeps working, and a handoff a lane starts early is cancelled by root and
the session resumed; root orders one at 70 or at a genuine boundary above
it. The init read is paid once per session and a handoff at 58 throws a
third of the session away.

**EVERYTHING WRITTEN TO DAMIAN IS WRITTEN IN THE CODEX PROSE LANGUAGE
(Damian, 2026-09-08 02:55, an experiment).** The three axioms and the
banned-word table of `docs/DevelopersGuide.md` "Codex Prose Language (CPL)"
bind every sentence a lane or root writes to him: no implicit referent
(`it`, `this`, `they`), no implicit quantity (`some`, `many`, `few`), no
implicit order (`first,` `then,` `finally,`), and none of the banned words.
Init Step 4b reads the section every session. Lane-to-lane messages and CL
descriptions are not bound. The reason, in his words: "there is a tendency
to communicate with me using implicit bindings, and it is even harder for
me to know what is implicit than it is for you."

**THERE ARE NO HARDWARE SITTINGS (root's reading of Damian's 2026-09-07
21:10 turn, landed as CL 23198 and ratified by him 2026-09-09 02:21: one
remained, for all time; the flight flew 2026-09-09 and stopped at the first
write to the part; `TheLostParadise.md` ROOT-F4 carries the attribution).**
No lane asks for a sitting, composes a flight, or flashes an image to fly.
A question is answered in the bed, by reading, or by a different design, or
it is deleted as unanswerable. The record is `HardwareSitting.md` "THE SITTING
QUEUE IS CLOSED".

**MAIN AND THE BOX ARE SYNCHRONIZED BY DIFFERENT THINGS (Damian, 2026-09-02
15:55).** The token lands an already-proven seed CL on main; the commander
grants the box. `-Internal` is banned: a change is verified by the tests it
touches, one at a time, plus for a seed CL the scratch fixed point and the BVT
as granted runs. Each lane audits and kills its own leftovers at every handoff
and after any killed run. The rule and its measurements live in `CLAUDE.md`
R-GATE.

**A GATE RUNS ONLY THE STEPS THE CHANGE CAN AFFECT (Damian, 2026-09-02 06:34:
"we don't need to perform full builds to test an app").** The fixed-point core
and the BVT run only when a compiler, foreword, seed or build path moves.
Landed main 21620; the app-sweep subject selector is the open half (fester).

**A TEST RUNS WHEN IT IS LIKELY TO FAIL, NOT AS CEREMONY (Damian, 2026-09-02
10:25).** Three buckets and nothing outside them: the BVT on every gate; the
test chapters whose source cites a chapter the CL changed; `-All` as the
release net, never for dev. A new test in none of the three is in no runner and
is a defect in the CL that added it.

**THE DOC-COUNT DRIFT CHECK RUNS ON THE FULL RELEASE GATE ONLY (Damian,
2026-09-02 07:27).** A lane does not fix counts to satisfy a dev gate; it lands
as its own tiny CL. Advisory since 2026-09-07.

**LIST APPEND IS THE AGENTS' PROBLEM, NEVER AGAIN DAMIAN'S (Damian,
2026-09-02).** No lane re-presents `list-snoc`, `list-push`, `&` or any
list-append semantics to him. If a change makes even ONE line that is currently
linear go quadratic, that change has utterly failed.

**COMPILER-42 IS CLOSED AND REFUSED, AND THERE IS A MORATORIUM ON DISCUSSING IT
(Damian, 2026-09-03).** `list-push` in no case ever copies the list; if a holder
has an alias to a list that is going to mutate, **that is dealt with AT THE
HOLDER**, where one caller pays instead of every call site paying through the
allocator. The in-place append is the contract, not a defect, and
`codex/compiler/Types/Builtins.codex` states it at the table.

**THE COMPILER'S MEMORY CONTRACT (Damian, 2026-08-28): the full self-compile
completes within a 2 GB heap high-water mark, in BOTH text and CDX modes.** A
contract on the compiler, not a scheduling policy: a self-compile needing more
is a DEFECT to fix, never a reason to grow the guests. Measured: CDX ~1.09 GB,
text 1,170,074,911 bytes (1116 MB), two independent modes agreeing within 3 per
cent; `text-stage1` refuses above 2 GB and was shown to fail before it was
believed (fester, 2026-09-01). **Open:** whether the contract wants a trigger of
its own, since the arm does not fire on most work, and the CDX-mode half has no
arm at all because those phases keep no telemetry artifact to read.

**BATCH YOUR GATES (Damian, 2026-08-28).** Small CLs land on your dev stream
with targeted tests only; a gate runs once per work ARC, never per one-line CL,
and the batch copies up grouped (P-COPY1). Seed lands are unchanged. Docs and
registers need no gate.

**WE DO NOT HOLD OUR WORK FOR AN EXTERNAL CONTRIBUTOR (Damian, 2026-09-02
10:35).** Our compiler CLs land in queue order and a contributor's PR rebases
onto main when it arrives. The reviewing lane judges it on one test: the result
must be shaped for Codex and its own consumers, never for a transpile target. An
ancillary subsystem with no fleet CL in flight may wait for a contributor; the
entry chapter is not one. Remaining subset/deck obligations from Steve
Howell's issue 115 are tracked in `codex/compiler/compiler-backlog.md`,
COMPILER-48.

**DO NOT ADD A TEST TO THE GATE OR THE BATTERY ON YOUR OWN INITIATIVE; GET
THE COMMANDER'S CLEARANCE (Damian, 2026-08-21: "haphazardly adding tests to the gates
slows everyone down").** The cost is every agent's gate run for the rest of the
project. It is about the GATE and the BATTERY, not a ban on arms: a
`build/boot/diag-arm.ps1` row costs a gate run nothing.

**A FINDING ABOUT SOMEONE ELSE'S PROJECT IS NOT OURS TO PUBLISH.**
`//Codex/main` mirrors to public GitHub and GitLab, so anything landed there is
published; a bug report about an external project is that author's to receive
first, and a note saying one was withheld is equally not for the depot. In a
design, state the target's behaviour as a fact about the machine we build on,
not as a defect in somebody's document. CL descriptions are not mirrored.

The rest, each one line: battery runs are Damian's (release proofs excepted).
Goldens stay parked during active GUI work. No new platform-wide register.
Prose about our own code is deleted in files you touch. The em-dash stays
banned. `-Jobs 8` on every parallel harness; Renode arms run ALONE; a fan-out
launches on the lane's own runtime measurement against the per-guest bar
(`CoordinationProtocol.md`). Do not lower `deck-headroom -MinMargin` to clear
a red. `print-line` CONVERTS and `print-line-raw` is byte-exact
(`DevelopersGuide.md`, "Effects and Act Blocks").

### Declined, and therefore not available work

Damian has ruled these out; they are here so the ruling is reachable by
whoever is about to spend a session on one.

- **Line-level debug info.** A statement about what Codex is for, not a
  scheduling call.
- **An app compile gate.** Compiler work must not be coupled to app drift.
- **The ARM64/RISC-V LIR retarget.** What landed stays; the rest is not
  reopening.
- **Plug arms for targets whose runtime is not on this box** (Damian,
  2026-08-25: no toolchains installed to close them). `char-encode` has arms
  in the five plugs that run here (python, javascript, zig, csharp, wasm);
  of the ten without, only ada and fortran can build a `Char` and would
  newly lose a site, both recorded in `build/plug-builtin-baseline.txt`.
- **The store cutover** waits on infrastructure and is not available work.

Declined is not deferred. Do not re-propose one of these, do not build a
smaller version of it, and do not open a design that assumes it. If you
think a ruling has been overtaken by events, that is one sentence to Damian,
once.

