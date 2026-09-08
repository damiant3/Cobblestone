# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

*Pruned 2026-08-15 and 2026-08-18 (red) and 2026-08-31 (root, 2,105 lines
to about 650), each at Damian's direction. How something was hunted lives
in the CL, the GitHubUpdate for its cycle, or the doc named beside the item
(`docs/Hardware/HardwareSitting.md` for flights, `ExaminersAssay.md` for
guards, the design or backlog that owns the subject). A tombstone or a war
story added here is scavenged again; write the pointer instead.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

Update 55 shipped 2026-09-02 23:35 (main 22312, seed 81F9E8171DCF6268, the
full gate's own fixed point built `-Repl`; mirrors at 675a0775). The
proofs table is `GitHubUpdate55.md`; the two process findings (a copy-up
drops a writable target silently; lane seeds were non-REPL builds, P-REPL)
are in `SomethingSeenDuringRelease.md`. MAIN IS OPEN.
Update 54 shipped 2026-09-01 (main 21229, seed FCBABF07479516DE, mirrors at
14ec571b); CobblestoneWeb republished the same evening (29bed9a).

## THE SEED INSTALL IS GUARDED BY PROSE AND A HUMAN NOTICING (L-BODY) -- blu, 2026-09-01

**A seed that did not contain its own fix reached main (21215) while the gate
was on screen refusing it.** `cdx-fixedpoint`'s two-pass branch prints that
`Sut.cdx` is the PRE-CONVERGENCE binary, that installing it ships a compiler
which does not reproduce itself, that this is P-STAGE2, and that `NewSeed.cdx`
must be installed and the gate re-run first. Then **the gate exits 0 and
nothing stops the copy.** The check is correct, the diagnosis is correct, the
wording is correct, and none of it is load-bearing: the guard is prose plus
attention, which is the definition of L-BODY.

**CLOSED (fester, 2026-09-07).** `build.ps1` writes
`build/output/seed-verdict.txt` on every run (`one-pass Sut.cdx <sha>`,
`two-pass NewSeed.cdx <sha>`, or `core-skipped none -`), and
`build/install-seed.ps1` is the only sanctioned way to move the seed: it
refuses a two-pass or core-skipped verdict, a verdict older than the artifact
it names, and a hash that does not match the bytes it is about to copy, then
installs, self-verifies, restores the previous seed if that fails, and
refreshes the four `seed/Codex.cdx` claims in `TechnicalDetails.md`.
`PerforceProcess.md` 4.3b and 4.4 prescribe it; the one raw copy left is 4.4's
deliberate unsigned intermediate, which the script refuses by design. The gate
still exits 0 on a two-pass, because that branch is a legitimate outcome: the
CL can be good and only the seed install premature, and a red there would
teach people to route around it.

**MEASURED AT THE UPDATE 55 RELEASE (root, 2026-09-02 evening): every seed a
LANE landed today is NOT the fixed point of the source it accompanies, and
every seed the FULL GATE built is.** fester's `04BA03DB` (22100): the gate
at 22163 converged in one pass on `BBB9907C`, 277,490 bytes differ outside
the signature, 8 bytes shorter. blu's `6AD77CCB` (22210): both DDC arms
agreed with each other at 3,187,745 bytes against the seed's 3,187,753. blu's
`A2E240BA` (22241): the gate at 22254 converged in one pass on `8DC212DA`,
274,580 bytes differ, 16 bytes shorter. Each lane seed was "one-pass fixed
point, BVT green, signed, self-verified" by its own scratch path, and each
is 8 to 16 bytes LONGER than the gate's artifact, so the lanes' scratch
compile is not the gate's compile. **CAUSE FOUND (blu, 22:58):
`build.ps1:260` builds the seed with `-Repl`, which sets the exit mode to
`ExitRepl` (`opening.codex:1599`); the lanes' scratch path never passed it,
so every lane seed was a non-REPL compiler.** The fixed point that ships is
the one `build/build.ps1` builds.

**CLOSED (blu, main 23105).** `install-seed.ps1 -Lane` is the lane path and it
passes `-Repl` at line 109, the same flag the gate passes at `build.ps1:260`.
It does not read a claim: it recompiles the source WITH the candidate and
requires content identity, so a candidate built by a different compile than the
gate's cannot be installed however green its own scratch run was.

**Scope note so nobody widens it:** this changes no compiler source and no
check that already works. `Get-CdxContentHash` compared correctly and caught
the difference; an earlier claim of mine that the gate's fixed-point test was
vacuous was WRONG and is retracted at 21226. Do not "fix" the gate.

## THE FLEET DISPOSITION (Damian, 2026-08-31; root commanding)

Standing, and still in force: **no lane runs riscv or arm64 work, and Renode
stays out of the loop, until Damian lifts it** (2026-09-01 evening, restated
2026-09-02). Docs go straight to main; a code arc gates once per arc; the batch
rules are `CoordinationProtocol.md`. The lanes table below is the assignment and
wins over this section where they disagree.

**Queued (Damian, 2026-08-31), REEK'S, the next drawable plugs row** (it said
UNOWNED until 2026-09-07, contradicting this file's own standing line 340 rows
below, "Plugs are reek's close-out lane", Damian's direction)**:** text
plugs emit CCE encoding code a simple program never needs, and the emitted
`opening` round-trips `to-cce (from-cce x)`; some emitters do not, and they are
the control. Census first, then one plug per CL: `plugs-backlog.md` 2.15.

### Campaign: the games reach the landing site (val) -- CLOSED 2026-09-02

The arcade is 34 of 34 playable, re-measured from the descriptors. The account
is `apps/games/games-backlog.md` GAME-44 through GAME-58 and the CLs named
there; the sizing lesson it produced six times is L-ADJECTIVE.

What survives the campaign and still binds:

- A card's tag reads "playable now" only while a visitor can play.
- A game that fails on wasm is a PARITY finding for reek: one message naming
  the subject and the failing test, and no workaround in the game.
- Chess (GAME-10) stays not built and its `games.json` row stays honest.
  **OPEN:** GAME-10's sentence about the landing page is stale and is val's to
  fix when that file is first touched.
- Claims: `apps/games/**` and `apps/landing/**`, except `web/compile/**`, which
  is the Prism page and stays fester's.

### Campaign: the wasm plug at parity with the hosted x86-64 lift (reek) -- parity CLOSED

**There is no genuine wasm parity gap left**, re-measured 2026-09-01 on seed
42ACED00: wasm 53 = hosted linux 53, one ahead of hosted windows 52. The last
candidate, `apps/dev-watch`, was an oracle pinning a raw allocator address and
was fixed at main 21790; `apps/codex-boot` passes on wasm and faults on both
hosted targets, so wasm is ahead rather than level. Do not compare those
figures to any score recorded before 21152: the corpus grew to 1002 and the
default 60 now samples `codex/test/apps` rather than the corpus (plugs 2.14).

RULED (Damian, 2026-09-02, via red): **"wat2wasm is fine."** Plugs 2.11 is
closed and neither a native binary wasm emitter nor a WAT assembler in Codex is
built; wat2wasm stays on the PATH as the assembler.

Still open on this row:

- Want 3 of the `build-page-modules` row under "Registers carrying unowned
  work": why `wat2wasm` never started on `riscv-stdio`. The npm-shim
  candidate is the note on `plugs-backlog.md` line 74 (not a 2.03 addendum,
  which does not exist at head); nobody has confirmed it is the same event.
- **No gate builds a web or wasm bundle.** `app-sweep` compiles 267 app entry
  chapters, which is the bare-metal side, and nothing in `build/` invokes
  `codex/plugs/wasm/build-spark.ps1`. The WGSL half closed at 21344
  (`apps/gpushow/tools/validate-all.mjs` grades all 83 shader modules and is
  calibrated both ways) but nothing invokes that either, because it needs a
  browser. Any lane touching a browser app inherits a blind spot no gate can
  see; the per-app gaps are in `spark-backlog` SPARK-4, `gpushow-backlog` and
  `starmap-backlog`.

Claims: `codex/plugs/wasm/**` is reek's for the campaign; fester keeps
`apps/landing/web/compile/**` and the Prism page; reek announces before touching
`build-page.ps1` or `page-lenses.ps1`. Seed-affecting only if a compiler chapter
moves; the plug alone takes no token.

## MAIN IS OPEN

Latest public release: **Update 55** (2026-09-02 23:35, main 22312, seed `81F9E8171DCF6268`); `GitHubUpdate56.md` is rotated and accumulating this cycle. This line read **Update 53** until 2026-09-07, two releases behind, while line 24 of this same file recorded 55: a register that contradicts itself about what is PUBLIC is the one claim a contributor checks first. Re-measure at the release head, never carry it forward (L-COUNT). Was: Update 53 (red, 2026-08-28), github `58b08c38`
(master) and gitlab the same commit, from main 20765, seed
`B066CEB5FE8FC9E8`. `GitHubUpdate53.md` is the report; `GitHubUpdate54.md`
is rotated and carries the open items. Earlier releases (51, 50 and before)
are recorded in `docs/PM/Active/GitHubUpdates/`, not here. The head seed
has moved since the release (`2B69CDD246E7EE23` at main 20824). Main is
open: seed-affecting copy-ups are unblocked.

## The brand boundary (Damian, 2026-08-29)

The public name is **the Cobblestone Project**: the OS and every brand
surface outside the compiler is Cobblestone; the language, the compiler
and the artifacts stay Codex. The rename campaign is done and nothing in it
is open; the ruled boundary and the traps around brand strings are in
`docs/Designs/Done/Marketing/Cobblestone.md`.

## The network demo pair (Damian, 2026-08-24)

Two items, deliberately decoupled.

1. **A webserver app in the guios** (val; blu consults on the net side). It
   serves HTTP and the browser app's own `codex://` wire
   (`apps/browser/PageFetcher.codex` / `DataChannel.codex` are the client
   side). The server is `codex/os/net/WebServer.codex` (`cites Net chapter
   WebServer`), NOT the same-named `apps/works/WebServer.codex`, which is a
   socketless router called only by its own test. **RULED (Damian,
   2026-08-28): the desktop never gains `Network.*`; the webserver becomes
   the first system SERVICE under a preemptive scheduler and the pane is its
   admin console.** Design and stage register:
   `docs/Designs/Active/OS/PreemptiveScheduler.md` (val). WORKS-48's pane
   half (window slot, the block shared with the service in `ds` 248,
   start/stop) is unblocked; the serving half is stage 5 of that design.
   Bed first via codex-vm NAT port-forward; metal rides a future sitting.
   Originates in the works app: `apps/works/works-backlog.md` is the
   register, this row is the pointer.
2. **The compiler running in WASM, building itself, in a static webpage**
   (fester): SHIPPED and witnessed (plugs 1.83, main 19774;
   `codex/plugs/wasm/page/index.html` plus `build-page.ps1`; the anchor is
   computed at page build from the served bytes, never hard-coded). The
   account of how it got there is plugs 1.60 through 1.94. **Hosting that
   page from our own kernel and OS is EXPLICITLY a separate later step
   (Damian: it requires environment he does not have yet); do not couple
   item 1 to it.**

## Track A -- the stick is an OS

**Sittings are coordinated by red (Damian, 2026-08-18) and grouped, not
serial.** Every metal question rides ONE diagnostic boot per sitting: a lane
routes its question to red with its arm and expected readings, red composes
the boot (bank before you risk, L-BANK; rehearse the exact bytes,
L-REHEARSE), and Damian sits once. **Agents do not propose flights or
sittings (Damian's standing ruling).** Standing metal questions: the sink's
2.7 MB write (WORKS-9), the e1000 ring successor (NIC-4), the TCP
conversation (B3), ASDE (finding 4), NIC-5 last, and the GOP row below if
red does not close it from sitting 6.

- **The diagnostic stick (red, approved 2026-08-18): one image that detects
  the box and says what needs to happen.** Design and stages:
  `docs/Designs/Active/OS/DiagnosticStick.md`. Steps 1, 3 and 4 are landed
  (root) and the stick flies; **step 2 lifts are per lane** (each lane lifts
  the probe it flew into a stage, coordinated with root), **step 5 is the
  grouped sitting.** Flight cards and every banked reading:
  `HardwareSitting.md`. Trap: a stage that can wedge the box runs AFTER the
  bank, never before it (L-BANK; sink executes last for this reason).
- **The I219 medium-death hunt is PARKED (Damian, 2026-08-24, after sitting
  12).** The production path (boot, bring up once, talk TCP) is proven on
  metal; the death has only been seen inside the ladder's own mid-session
  re-reset, which production never does, and it moved between same-shape
  ladders with no mechanism named. **Revive only on production evidence:**
  the resume point is the sitting cards (10, 11, 12 and card 19188 in
  `HardwareSitting.md`) and `docs/Designs/Active/OS/I219IsNotAnE1000.md`;
  the next arm is blu's to compose. No lane draws from it while parked.
- **WORKS-9 (reek). The USB mass-storage driver's second write, and the
  sink's own 2.7 MB write on metal.** Metal-gated; the arm and account are
  in `apps/works/works-backlog.md`. What is open is why sink REFUSES on the
  board: the bed reproduces the bank loss (a `-usb-bot-drop` keyed into
  sink's DATA phase) but not the cause, metal refusing at `rty=1` where the
  bed reaches `rty=2`, so a board reading is what is wanted (L-ARENA). Any
  rebuild of `sinkladder.img` needs a fresh full-mission run (L-REHEARSE).
- **A8 the desk build loop (fester).** Plan, roads and traps:
  `docs/Designs/Active/OS/DeskBuildLoop.md`. The allocation is GRANTED on
  the ASUS at `-AllocPages 131072` (`HardwareSitting.md` "A8"), `compile
  <path>` is wired and gated to the compare against `CODEX.CDX`
  (`codex/test/apps/gcon-compile-read`, `gcon-cdx-verdict`). **What waits
  for metal is the launch alone**, `vm-compile-cdx` and below, because
  codex-vm is itself a hypervisor and its guest sees no VT-x. The image is
  NOT flight-ready for anything else (no `-Identity`, no source).
- **Identity, RULED 2026-08-18 (queue 11, 12): the identity file stays on
  the ESP; auto-unlock is bed-only.** Rotation (`RotationFact`) stays with
  `Designs/Done/OS/Identity.md`; nothing else is open.

## Track B -- the network (blu). Metal-gated, and ONE sitting remains (standing rule): every question below rides THE LAST SITTING or is answered in the bed.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE": the standing questions ride ONE diagnostic boot per sitting,
in an argued order (bank before you risk, L-BANK). Every flight's card and
archive row is there, not here. NIC-1, NIC-2 and NIC-3 are answered on
metal; NIC-4's ring half is answered (`rdh-writable=y`, sitting 6).

Open, in the order the ladder flies them:

- ~~**The i219 acquire-loop fix**~~ **BUILT (blu, 2026-08-24), verified at head
  2026-09-07.** `e1000-swflag-request` masks the three ownership bits and sets
  ours rather than writing a bare `#0020`, which would zero the extended-
  configuration fields the register exists for. Measured against the unfixed
  driver with firmware holding MNG: `writes=6002 foreign=6002`, every write a
  protocol violation; after, `writes=194 foreign=0`, three sites x 64 polls plus
  two releases, `final=00000080` unchanged. The arm is
  `codex/test/e1000-swflag-strict` under `-i219-extcnf-strict
  -i219-mng-release-after 3`, and a firmware RELEASE is what makes it
  non-vacuous: with MNG never held the register starts at zero and the defect
  cannot appear, with MNG held forever no acquire succeeds however correct the
  protocol. Account: `I219IsNotAnE1000.md`, "How the acquire-loop fix was built".
- **NIC-4's successor question**: whether a frame ARRIVES during
  `nicring`'s own window. `pre=3` says the part receives before the stage
  looks; the during-window GPRC read has not survived a flight, so "nothing
  arrived" and "arrived and was invisible" both stand. The discriminator
  can say NO (`-e1000-rdh-ro`, the `nic-rdhro` arm) and rides B3's boot.
  Details and the caveat are on the NIC-4 card in `HardwareSitting.md`.
- **From NIC-3**: `aneg-done` is never set on this part while `STATUS.LU`
  comes up, so `phy-bring-up` returns 0 against a link that is up.
- **B3, a real TCP conversation with a real peer**: `DiagB3.codex`, ladder
  stage 14, has answered on metal (sitting 11: thirteen bytes echoed back
  unchanged over the real I219). What the composer owes each sitting: the
  peer named in `DIAG.CFG` must ECHO (`build/boot/echo-peer.ps1 -Port 7`),
  because the conversation is raw TCP and not the repository wire. The
  next sitting is the gate for what b3 still cannot say, not for whether
  it works. **Every diag image built before main 18665 carries a BLIND b3
  (`sent=` was the intended length, not the sent one), and `45239937` is
  one of them: it does not fly.**
- **Finding 4 (ASDE)**: `DiagAsde.codex`, ladder stage 16, risk writes,
  flies last after b3; built and bed-verified both ways, awaits a sitting.
- **NIC-5: what wedged the box on 2026-08-11.** Not `CTRL.RST` (discarded
  on this part). Terminal by construction, flies last.
- **B4 step 6**, the repository protocol served on the part, is B3's
  flight. Steps 1-5 are done in the bed; the wire is `DevelopersRulebook.md`
  "The repository wire".

Rulings that bind this track:

- **The NETIO ceiling (Damian, 2026-08-21): cut the drain, the NIC comes
  first.** *"tcp correctness is a working nic, not adherence to a standard
  I can't use because the nic is broke."* Campaign rule 2 wins: no stage may
  end the run. Both halves landed (`net-io-drain-ticks = 96`,
  `codex/test/net-drain-budget` refuses a crossing of the give-up ladder;
  the unchecked x86 send surface has no production caller). The one live
  unchecked send path is arm64's (`Arm64NetIO.codex`), registered under the
  deferred OracleCloudArm64 project.
- **ICMP is send-only** (rulings queue 1): we do not answer a ping and
  `icmp-parse` stays latent. `Tftp`, `Syslog` and `Icmp` have no production
  caller; `syslog-decode-bytes` builds its body with the quadratic `acc &`
  accumulator (CostModel 3.6), and whoever gives `syslog-parse` a production
  caller fixes that in the same change.

## Track C -- the trust audit (val)

C1 and C2 are landed and enforced (`IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`). The rechecker fork is CALLED (red,
2026-08-20, rulings queue 3; L-CAPABILITY-LOST) and is not open. **C2.5
stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce (RULED 2026-08-15, CLOSED 2026-08-16)

The census, the ranked queue (10.1, take order in its last paragraph) and
how a row can be wrong (10.3) are `VerifiedFormatParsing.md` section 10;
the guard pattern is settled in `ExaminersAssay.md` (clamp where a length
decides a slice, refuse where it decides WHERE a read lands, the ablated
call IN the arm). Still open in 10.1, unowned unless named:

- 8b, `VirtioBlk`'s device-written used-ring index: waits for a bed.
- 18, `OtaBoot boot-load` (reek): LATENT, no production caller.
- The latent corpus rows 6, 7, 11 and 13.

## The Prism dev environment (Damian, 2026-08-28; multi-lane)

**STATUS 2026-09-07 (Damian): OPEN AS FALLBACK ONLY. His words: "we can go
back to prism if we are running out of work." A lane takes a Prism stage
when it has nothing live on its own register, never ahead of live work,
and says so when it does. Still probably reek's or fester's.**

Prism is a full local HTML/WASM dev environment on the STATIC page: source
tree on disk, multi-file editor, worker compile, cite resolution in-tab,
kernel builds through the in-memory pe/img chain, webpages through the html
lens, user-mode `.exe` and Linux executables as a hosted-runtime backend,
board kernels for the native plugs, and an optional Claude REPL/agent panel
behind a provider interface. Design and stage register:
`apps/prism/design/Active/PrismDevEnvironment.md`; register row:
`apps/prism/prism-backlog.md` PRISM-7. Stages 0, 1, 2 (compiler half AND
page wiring with the toolbox tree), 4 to test-limit, 5a, the native-build
configs, boards and bench have landed; the deployed page was refreshed at
main 20818.

**Two traps that bind every page deploy.** Rebuild from a seed at or after
`7B6A4950` (20783: before it RESOLVE emitted its frame in CCE, unreadable to
the page); head is `2B69CDD246E7EE23` at main 20824. And regenerate the
source bundle `build/output/Codex.codex` FIRST: a stale bundle made two
correct compilers read as one defective one (L-SAMEVER) and cost a deploy
cycle on 2026-08-28.

Open, in order. val and reek are on the disposition campaigns at the top of
this file; the items below that named them are queued behind those.

1. **Stage 2c, the in-tab signer: LANDED main 21450** (red 21448): a bytes-transport sign module over the shipped Ed25519 (key and signature equal the bare-metal program byte for byte; a signed CDX passes test-self-verify and a flipped byte fails it), a Signing key entry heading the Toolchains panel (generate, import, export, forget), Save signed on a CDX build, three grader arms and a headless arm. What remains is not the page: the public compile page needs Damian's one-command refresh to carry it, and device acceptance of a user key is trust-lattice policy, which the panel says.
2. **Stage 3, templates and the build tab: RED'S**, per the two documents
   that own the subject, `apps/prism/design/Active/PrismDevEnvironment.md:689`
   ("Stage 3 -- Kernels and deployables (red)") and `prism-backlog` PRISM-7
   ("the dev-environment campaign (red)"). This row said "unowned" until
   2026-09-07 and root dispatched off it twice; a lane refused both times.
   It is not a doc afternoon either: the acceptance is a server template
   built entirely in-tab, carried to the host, booted in codex-vm and
   answering HTTP, so it needs a browser session and a guest, and the page
   lives at `codex/plugs/wasm/page/prism.html`, in reek's tree, not in the
   8 files under `apps/prism`. Stage 2's cite resolution, which it needed,
   is on main (20796).
3. **Stage 4, the Claude panel: what is left is a hand at a browser with a
   key, not more code.** 4a and 4b are on main (20580, 20604) and the arm is
   green, but the stage's acceptance ("a chat round trip streams") needs a
   real key and a real billed call, which no lane can supply from a test.
   The request shape is pinned in the design at 20578 and must not be
   written from memory (`budget_tokens` is a 400 on `claude-opus-5`, a
   refusal arrives as an HTTP 200).
4. **PreemptiveScheduler stages 1+2** per the corrected design
   (`PreemptiveScheduler.md`, 20727: the kernel ALREADY has preemptive SMP
   processes, uncalled; the webserver becomes a spawned process and the
   desk never gains Network). val's, queued behind the games campaign.
5. **Stage 5c, sockets**: untouched, and RED'S like every Prism stage.
   The design carries it at `PrismDevEnvironment.md:896` inside
   "### Stage 5 -- User-mode executables (red; seed-affecting; the big
   one)". This row said "unowned" until 2026-09-07, the same defect
   stage 3 carried one line above.
6. **The guios webserver app** (WORKS-48, `apps/works/works-backlog.md`;
   stage 3's named server template): val's, queued behind the games
   campaign.

Damian's alone: the Anthropic API key for stage 4's live round trip, and
the one-command public `compile/` page refresh.

Rulings that bind the campaign (Damian, 2026-08-28): the stage-5a Linux
verification bed is ALL of the options (the narrow WSL exception for
verification arms only, R-SHELL amended in `CLAUDE.md`; a QEMU Linux guest
bed beside it; 5b's Windows `.exe` verified natively), *"we are supporting
all these options for the people"*; **boards** means IoT board build
targets, a Prism project per HAL board chapter with per-board output beside
the kernel chain; **bench** means our codegen benchmarks run and compared
against any configured output build chain, consuming the native-build
configs. The zig work (Steve's PRs) rides LAST behind the campaign: *"is
not the broadest brush right now, we are trying to paint bigger"*. The
design's foot lists the rulings it still asks of Damian.

## The lanes -- RULED by Damian 2026-08-15, re-pointed 2026-08-18

**root commands the fleet (Damian, 2026-08-28): assignments and status
reports route to root.** "THE FLEET DISPOSITION" at the top of this file
(2026-08-31) is the current assignment and wins over this table where they
disagree. The table is the assignment, not a suggestion; re-read it on every
merge-down. An item here is a pointer; the register named beside it holds
the detail.

Standing rulings that shape the rows: the compiler-bug order is whatever
`codex/compiler/compiler-backlog.md` shows open, and only that file knows it
(this section carried a stale order twice; read the backlog, not a
sentence). fester is otherwise held in reserve for the hardest problems
(Damian, 2026-08-26). **The DeskScheduler is PARKED (Damian, 2026-08-26),
not cancelled**: `docs/Designs/Active/OS/DeskScheduler.md` is the proposal,
its measurement arm is on main (the topbar counter: about 20,000 desk-loop
iterations a second idle, 60 with a 3D pane focused), the choppiness that
prompted it is not reproduced in the bed, and it carries two questions only
Damian can rule on (rate or budget; skip or run late on a miss).

| agent | now | then | standing |
|---|---|---|---|
| **blu** | **THE RECEIVE PATH COSTS 120 BYTES ON AN UNRECOGNISED FRAME, DOWN FROM 208 (blu, 2026-09-08, `codex/test/net-recv-heap`, seed `75B414046BEE5208`).** Three landings: the 208 was ATTRIBUTED with no residual (main 24210), the doubled `TcpTransportState` in `transport-process-frame` removed, 208 to 144 (main 24214), the same doubled build removed at `NetIO`s and `Arm64NetIO`s drain paths (main 24218), and the vestigial `recv-buf` field removed from the record, 144 to 120, the record itself 64 bytes to 40 (main 24222). The three surviving stages cost 40 each: `net-process-frame`, `transport-feed-from`, `transport-try-recv`. Every web-mux figure fell by the same 24 independently: a connection is **66,322** bytes, a pooled re-accept **768**. **A CAP ON THE WEB-MUX POOL IS REFUSED, measured:** `drop-delta` moves the heap +144 and never back, so a cap returns nothing and forces a 66,304-byte re-mint per accept it turns away. **The spawned-child `write-file` fault is narrowed to the WRITE path alone** (main 24202): the cap travels, and the child s READ servicer completes, so the lookup, the elevation and the servicer call all work in a spawned process. **THE ABSOLUTE FIGURES ARE THE UNRECOGNISED-FRAME PATH (blu, 2026-09-08, self-caught):** the arm feeds `zeros 60`, whose ethertype is 0, so `net-process-frame` takes its unknown-ethertype branch and never calls `eth-payload`. A recognised frame pays two more full copies (`eth-payload`, then `ip-payload`) that nothing has measured. The REDUCTIONS hold for every frame, being per-construction; the absolute numbers do not. A recognised-frame fixture is owed and is registered on `ProtocolStack.md`. **THE RECOGNISED FRAME IS 699 BYTES AGAINST THE DROPPED 40 (main 24506), and that is the number the next unit starts from:** the two fixtures differ in exactly two bytes, the ethertype, so 659 is what recognition costs, all inside `net-process-frame`, and it is a FLOOR because `eth-payload` and `ip-payload` both copy the payload at 8 bytes a byte and 1,514 bytes is unmeasured. **NEXT UNIT, for whoever holds this row: the INBOUND THREE (`net-process-frame`, `net-process-ip`, `net-process-arp`) onto a buffer.** Not fourteen: six of the design's fourteen carry a 4-byte IP or a 6-byte MAC and a seventh builds 12 bytes (corrected at main 24494). Not one signature per CL either: `net-process-frame` cannot move without `eth-payload` and both consumers, so the group is the unit. Measure with `recognised-cost`, control with the 30-arm cite census over NetIO, Arm64NetIO, TcpTransport, HttpFetch and TrustTransport. **NOW: nothing taken.** **OPEN, none blocking:** the `Arm64CodeGen3` host-field-index tables encode `TcpTransportState`s layout by hand and NO arm observes them (a sabotaged index passed every available test), so any future field reorder is a silent arm64 miscompile; `net-io-send-drain` and its arm64 twin cannot be measured end to end without a card; the frame half of the receive-loop repair is still the 14-signature campaign `ProtocolStack.md` describes. | Metal-gated and not startable here: the asde per-step notes (main 22830) stay unproven until an off stage is reliably off; B4 step 6 needs b3; Track Bs NIC-4 successor, NIC-5 and the b3 TCP conversation all advance at sittings. WORKS-16 wants a metal capture, not another headless arm. | `codex/os/kernel/E1000e.codex`, `codex/os/net/**`; WORKS-16, WORKS-62 |
| **val** | **NOW: nothing in flight. Lane clean, level with main, no guest, no token, no shelf.** **WORKS-44 CLOSED** (main 24527): the Review pane carries its `GrvData` in `DeskApps` as `da-review`, filled in `desk-rev-open` before the mark that function restores to, so a keystroke reads a field instead of three `gfs-scan-all` passes; `grv-cast` writes, therefore the cast path reloads, stores through `__record-set` and answers NEGATIVE. `codex/test/apps/desk-rev-cache` is new and carries its own control. **SHEET-13 CLOSED** (main 24570): `sh-slack` was an empty `widget-panel` and a panel paints, restarting the gradient at its own top, which put a seam at y 684 and a different shade over the 141 rows below. `widget-spacer` resolves to `widget-style-bare` and all three renderers answer its tag without drawing. NOT seed-affecting, and the concat says so: the compiler unit is 103 chapters and Widget, Theme, Layout, Render and GpuRender are none of them. **WORKS-38 CLOSED** (main 24581): F12 at 1600x900 wants a 4,320,054 byte BMP against about 2.4 MB free, therefore `gfat-write-file` refuses with `gfat-w-nospace` before any device write; 800x600 writes and flushes 1,547,264 bytes. Capacity, not a driver defect. | **NEXT: ShellRefinement option D.5's CODE**, which is seed-affecting and therefore takes the scratch fixed point, the BVT and the token, and waits for the release to finish. WORKS-41 needs a scrolling panel, which is WORKS-23's capability and not a row of its own. WORKS-60 still needs Damian's own desk hands. | Damian's batch: WORKS-60, the virtual-desktop wording, WORKS-50's naming, FW-1's three fix options. A start-menu group of seven pushes the laid menu past the taskbar band and nothing clips or scrolls: registered in `ShellRefinement.md`, and it is why the Sheets launcher row sits in Accessories. `ShellRefinement.md` "WHAT IS STILL OPEN IN 6.4"; `PreemptiveScheduler.md` stage 2; WORKS-47/41/40 |
| **fester** | **HANDING OFF. Phase B stage 2 stands: the runner cites the compiler, walks a FAT16 directory, compiles each entry in-process, and reports the first diagnostic code.** `S21.SRC : errors=1 ok=no first=CDX2003` and `S22.SRC : ... first=CDX2005` against sidecars holding 2003 and 2005, matching exactly. Bag read ONCE per entry (a record carries count and code) so no chapter is compiled twice. **The `.failing` pairing stays on the HOST deliberately: the sidecars are not on the image, and putting them there is the namespace question, not a runner one.** Landed today: 24501 in-process compile, 24523 directory walk, 24549 the file-not-unit correction, 24556 the scoping, 24559 option B measured at 10.02x plus the Fat16 reader correction. **NEXT FESTER'S FIRST UNIT: subdirectory placement in `codex/plugs/img/Fat16Writer.codex`.** The reader needs nothing: `codex/foreword/core/Fat16.codex` already has `fat16-list-dir`, `fat16-resolve-path`, `fat16-walk-path`, `fat16-create-directory`, the `fat16-mkdir-*` family and a full VFAT LFN encoder/decoder. The writer already lays directory and dot entries for EFI/BOOT/SEED, so what is missing is placing FILES under a directory it can already create. That unblocks the 509-entry root bound and the 106 8.3 collisions among 646 tests. **`.expected` still needs execution and no arm claims otherwise.** | **LANDED ON MAIN TODAY, all proven, none shelved:** 23830 the app-sweep subject selector (a real blind spot: 3 apps chapters swept nothing, 25 swept fewer); 23877 DATA-W3 (17 chapters nothing had ever compiled, SEVEN red); 24045 STARMAP-8 and the nettool suite (both moved to `codex/test/apps` where `test.ps1` RUNS them, 20 and 35 arms, five red on their first run) plus Phase B stage 1 and COMPILER-68's BVT batch arm; 24054 `sweep-apps.ps1` `-Jobs`; 24114 the seed, `7A36B22409B9B1D8` (DISK mode honours its path). **Registered and unowned:** 43 `*-verify.mjs` graders no script invokes; plugs 1.101 (nine plug run drivers with no `-Kernel`); STARMAP-10; WORKS-63; the flaky step 2b in `test-disk-compile.ps1`; a load generator with a BOUNDED MEMORY ENVELOPE (the BVT is not one: `-Jobs 4` lasts 28.4s against a 50-minute rehearsal, and looping it takes free RAM to 733 MiB, where a starved guest and a slow one are the same colour). **Phase B stage 2 is blocked on 1.102 and on root's ruling (a), the thin `opening` over a citable compiler, which is seed-affecting and not started.** | **COMPILER-59 and COMPILER-60 are PARKED and measured, not abandoned:** `ir-fidelity -Disagree` counts a def disagreeing with itself and reads 3 sites over 2 programs of 615; the lambda-parameter-span change moves 2 of the 3 and REGRESSES `typeclass-poly`'s `convert`, and the shared-span reading is refuted, both digests in `docs/Designs/Active/Compiler/LambdaParamSpans.md`. The name-the-kernel class is closed: `test-self-verify`, `check-generated-scripts` and `test-cross` all take `-Kernel` (2026-09-07). What the Build register still carries unowned is that `test-cross` BOOTS RENODE whenever the binary exists, guarded only by the operator remembering. A8 desk build loop when VT-x metal is available; Renode out; `deck-headroom`; WORKS-24 rides a sitting; ProductBuilder stage 6 on hold |
| **reek** | **NOW: nothing taken; clean, no red gate.** The PipelineModel migration is Damian's direct direction of 2026-09-08 and is the lane: he approved the record shape, then directed the migration through the remaining generators. **FORTY-TWO of the 58 generators are on the model and on MAIN, every one `match / 0 drift`, and NO shipped `build/*.ps1` byte changed by any of it** -- say that in every copy-up, it is what lets a release owner skip re-running them. Landed 2026-09-08, twenty-one of them: `check-doc-counts` 446, `cdx-to-pe` 1482, `vm-config` 1157, `build-arm64-img` 252, `build-boot-img` 206, `build-img` 760, `ablate-doctrine` 576, `compare-codex-semantic` 534, `test-compile-batch` 352, `test-cross` 270, `quire-map` 356, `plug-build-lib` 246, `boot-arm64` 226, `compile-riscv` 288, `compile-arm64` 233, `check-plug-builtins` 240, `check-xdiag-cells` 171, `build` 1449, `test` 1457 (ELEVEN verdicts), `bvt` 544, `check-cdx-registry` 213. Census: **47 migrated, 1 flat, 10 blocked until the tree lands. THE FLAT SET IS NOW GENUINELY EMPTY** (the one left, `testrunBashScript`, has no target and is declared as such), so nothing more can be migrated until the tree lands. **CORRECTION that made the forty-seventh possible: "two can never be graded" was WRONG and I published it repeatedly today.** `cvmm-build` grades and has since 2026-08-07 (its target moved to `apps\cvmm\build.ps1` and `$AltTarget` maps it), so it was takeable all along and is now migrated (`match 69 / 0 drift`). **The drift gate now REFUSES an undeclared missing target** (`$NoTargetByDesign`, one declared entry with its reason), graded by ablation on both arms; `testrunBashScript` is KEPT rather than retired because it is the only bash generator and the unhandled-node scan reaches `BashEmit` through it alone. File size predicted nothing (`cdx-to-pe` is 102 KB and four stages, `build-magic-pages` 4.6 KB and blocked); the ASSEMBLY predicted it every time. **A `.codex` file is UTF-8 WITHOUT BOM in the workspace** whatever `p4 diff2` types it; rewriting one as UTF-16 gives a whole-file diff with correct content, so prefer the editing path and count changed lines before believing a rewrite. **L-VACUOUS has now been independently rediscovered FIVE times in shipped runners** and was recorded in none of them before this campaign. **A finding for whoever owns the cross battery's aggregation: `test-cross` returns 0 for EIGHT states and six are SKIPS**, so a caller adding up exit codes counts every skip as a pass (L-DENOM at the level of one subject). **`check-doc-counts` needs inline-block EXTRACTION rather than renaming** and is the one remaining subject that is not a mechanical migration. **The separator rule is per-body, not uniform**: `plug-build-lib` has two sections that follow their predecessor with NO `ScBlank`, so a body takes exactly the separator the flat assembly held before it. **A FOURTH VOCABULARY GAP, found by `boot-arm64`:** a script whose failure paths are all `throw` rather than `ScExit` can declare no refusal at all, so `ps-verdict` carries only its one success. Goes to the same second pass as the other three. **BURR 4 IS RULED (Damian, 2026-09-08, through root): SHAPE 3.** `pl-stages` becomes a tree; a step is a stage or a group; a group carries its own steps plus optional repeat, guard and handler. **The proof obligation is part of the ruling: every already-migrated generator stays byte-identical**, so the tree lands only when `check-generated-scripts` reads `match / 0 drift` across all 42, which is the one FAN-OUT run on this campaign and is asked of the commander. **Second half, the runner:** executing a pipeline as a process instance uses `apps/workflow/WorkflowTypes.codex`'s vocabulary (`ProcessInstance`, `StepDef`, `StepStatus`, `StepEvent`, `ConditionOp`) rather than a second one invented here; DESIGN it on the page, build nothing yet. Of the four scripts whose stage structure is worth the most, only `CompileScript` was ever blocked: `build.ps1`, `test.ps1` and `bvt` are migrated and were never blocked at all. **Still unruled, and now the only open decision here: the four vocabulary gaps** (a COMPUTED exit code, a stage whose output is a FUNCTION, a resource driven by a PARAMETER, and a refusal spelled `throw` rather than `ScExit`), recommended as one `PoComputed`, one `PkFunction`, widening the three Integer resource constructors, and one way to declare a throw. All on `docs/Designs/Active/Build/PipelineModel.md`. | **NEXT for whoever takes this: the next flat generator, one per CL**, taken from the census table on `PipelineModel.md` rather than by grepping, which has been wrong three times (L-CENSUS). Recipe: `p4 edit codex/build/<name>Script.codex`, migrate (cite Shell chapter Pipeline, declare artifacts, `sN` becomes `sN-body` plus `sN : PipeStage`, assembly becomes a `Pipeline`), then `build/check-generated-scripts.ps1 -Only <emitted-name>`, ONE guest, which must read `match / 0 drift`. Each stage body takes the leading `ScBlank` the old flat assembly held between sections, or the emitted script comes out short by one blank line per boundary. **MERGE DOWN BEFORE EVERY `-Only` RUN**: a proof under a superseded seed is void, and `plug-build` had to be re-run when the depot seed moved twice under it. **AND STASH ANY OPEN EDIT BEFORE A MERGE-DOWN**: `p4 submit` on the default changelist sweeps it into the merge CL, which put an unverified generator on `//Codex/reek` at 24034 (P-OPENMERGE). | Plugs close-out lane; WORKS-9 metal-gated at red's sitting; `ShellDslReadability.md`, which is the SAME campaign as the pipeline model (a raw node hides a name AND a refusal from every reader but the one holding the text); `tools/codex-vm.c`. gpushow live-compile is ON MAIN (23608, 23609, on Damian's ruling relayed by root); it is not published to the public site, and publishing is his call. Blocked on Damian: SPARK-4. Registered, unchased: COMPILER-46, COMPILER-39, COMPILER-48. |
| **red** | **NOW: COMPILER-73 LANDED (main 24537), row closed.** One dynamic over-application block per closure trampoline replaces the 6 - r inline blocks: r10 carries the surplus count until the captures are read through r11, a two-word stack frame carries the count and the next-argument address across the applications, and one add of 16 + (6 - r) * 8 restores rsp for every count. Seed 3,519,881 to 3,322,781, so 197,100 bytes come back, 66% of COMPILER-20's growth against the 80% the row predicted, because the fixed part of each block dominates. Stage 3 == stage 4 byte-identical, BVT 143/0, signed and self-verified, all 13 rows of `saturated-call-returning-function` identical, and the same test compiled by the prior seed is 6,912 bytes larger, so the new block is what produced them. Landed 2026-09-08: COMPILER-69 (main 24205), COMPILER-70 (24230), COMPILER-71 (24250), COMPILER-16's ratchet `build/check-compiler-warnings.ps1` (24264; row OPEN for its gate-retention half, on Damian's list), COMPILER-9 closed (24271), DeskBoot removed on Damian's instruction (24319), COMPILER-20 (24407: closure over-application returned a heap pointer; both defects fixed; seed +9.2%, root accepted; COMPILER-73 registered), COMPILER-23 residue (24438; the binary moved, so a deletion is proven seed-neutral only by the binary, standing note in the register header), COMPILER-43 closed (24474; the "two" definitions were the -IrUni log echoing its tail, OperatorsManual names it). Seed on main: `B63014D717B1A2F9` (#778, 3,322,781 bytes). | **NEXT: what root dispatches.** COMPILER-72 is blu's. The kbd and mscalign step-2 ladder lifts stay HELD by root until the last hardware sitting flies. | Releases, personally and end to end; `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; the 4.3 seed hash check runs BEFORE build-complete. |
| **root** | **NOW (2026-09-08 16:45, RELEASE UPDATE 57 IN FLIGHT, root at 63% measured; resume by the commander init, then this row and `D:\Projects\.agentgrid\root\status.json`).** Damian ordered a lean diagnostic release at 15:50 (proofs only, box measured). **Every proof is GREEN at head 24552, seed #778 `B63014D717B1A2F9`, run from `D:\Projects\Cobblestone-root-main`:** full gate 777 s; Sut === seed 3,322,781 B; ir-fidelity 8/0; battery 1,795/1,746/0 fail/49 skip; sweep 290 clean, 3 known-dirty, 0 regressions; poison battery 1,795/0 fail on the rerun (the first run had one FAIL_OUTPUT, `heap-bracket-shape`, not reproducible alone or on the rerun); DDC HOLDS (both arms 3,322,781 B, 95 differing inside the signature, 0 outside); map refreshed from `Sut.map` and validated 5,660/5,660 against MAP1 (LANDED on main 24574 with the img, by a copy-up submit that swept the root-main default changelist); img rebuilt `DD959678725F1833` (LANDED on main 24574 with the img, by a copy-up submit that swept the root-main default changelist); diag unchanged 6F077EEB, check-shipping-images OK; doc counts 61/61. Sampler CSV in root's session scratchpad (`box-release.csv`): floor 2.81 GiB with 4 test-run guests at 3.85 GB total plus 16 pwsh at 1.7 GB; peak 18 guests at 2.6 GB total. **MAIN PINNED for seeds until the push.** **NOT DONE, in order:** (1) Damian retracted the codexmagic half of the 14:30 purge (24272/24276, mirror 3907073c): the GAME (apps/games/codexmagic, codexmagic-mobile, edgemesh, explorer, magic-sim-fixes, edge-mesh tests, build-magic-pages and build-explorer-pages with their generators, sim-test.ps1, the Kickstarter plan, EdgeMeshGameServers.md, DataQuireCrash.md, PROLOGUE-YIELD-CLOBBER.md) goes BACK on the mirror; the SOLVER (apps/games/magic and now annotations/apps/games/magic), wademo (app, design, the five codex/test/wademo-* pairs) and codex/product stay withheld. State in root's workspace: `p4 undo //Codex/root/...@24272` is OPEN AND UNSUBMITTED on 35 files (the undo restores the pre-purge text); six files that moved on since were reverted out of the undo (CurrentPlan, TechnicalDetails, compiler-backlog, Build.md, release SKILL.md, GitHubUpdate50) and take hand edits instead: restore the codexmagic names in compiler-backlog line 144 and Build.md line 1752, and TechnicalDetails' img row to `DD959678725F183315E4EDE06E74C8C4B91C186F5DB344A8E9ACE46D51AB04C3`; a stray on-disk `docs/PM/Active/GitHubUpdates/GitHubUpdate50.md` left by the undo goes away by hand (its depot state is moved to Done, 24565). Then RE-PURGE only wademo/product/solver in the undone files: data-backlog (wademo), DevelopersGuide (the `Magic` quire row and the wademo loader line), Probes DOCTRINE-GA and README (Personality.codex), REPO-RESTRUCTURE (the Magic example row), GHU47/48/49 ProductBuilder lines, PublicPush 2b/2c and the Do-not-publish bullet (rewrite for the new block), CuratorsCatalogue codexmagic.org to cobblestoneproject.com (brand fix, keep); `.gitignore`: keep the restored block and ADD annotations/apps/games/magic/, codex/test/wademo-*, docs/Designs/Active/Apps/WaDemo.md under a comment naming the set as withheld on purpose. Submit as one CL, copy up. (2) Fill the note `GitHubUpdate57.md` (opened in root): POISON_TODO = the two runs above, BOX_TODO = the sampler numbers, the head paragraph after the push, and the head paragraph says the codexmagic withholding of 3907073c is reversed in this commit. (3) Copy up root, sync root-main, rerun the sweep on the final head (96 s), then PublicPush step 2: `git add -u`, `git add` the 15 survivors from the reconcile AND every restored game path (untracked now), `git rm -r --cached annotations/apps/games/magic`, secret scan, commit as damiant one line, push github master and gitlab master:main, no force; verify 404 on the solver, wademo and product, 200 on codexmagic. (4) MAIN OPEN to all lanes (reek 24566 and blu 24567 held for it); stop the sampler pid 12572; Step 8 has no PRs to receipt. **Lanes:** red on non-seed compiler units (22%); blu holding 24567 at 61%; fester (new) on FAT16 writer subdirectories; reek (new) on the shape-3 tree; val on SHEET-13 (27%). **Damian's decisions:** release-cadence remedy; COMPILER-16 retention half; deskboot digest row. | The pipeline model campaign is reek's (`PipelineModel.md`, 23627). fester's stocked queue after the flight starts at `pair-generic` (23364). Inventory 2026-09-08: about 125 startable units outside the four main registers, but seven register lines proved stale at head in one night, so every dispatch is verified at head first. | `DiagnosticStick.md` composition; `ComplianceEvidence.md`; `HardwareAbstractionLayer.md` question 5 blocked on a board crypto manual; OracleCloudArm64 deferred; `build/boot/diag/**` (released to red for the two stage lifts) |

**Plugs are reek's close-out lane** (from val, Damian's direction
2026-08-18): the register in order, one entry at a time, said in
status.json. Entries other lanes hold are named in the register (1.33 blu,
1.38 and 1.3 fester, 1.36 and 1.32 reek, 1.34 root). `codex/plugs/zig/**`
is ORDINARY FLEET CODE, edited like any other plug (Damian, 2026-08-18);
credit Steve in a CL that changes what he wrote and flag it in the next
GitHubUpdate, which is courtesy and not a gate.

## Approved campaigns and the pool (Damian, 2026-08-18)

Damian approved every open design campaign in `docs/Designs/Active/` as
available work; a lane that empties draws from the pool, in order, and says
so in the table above. Where the pool and the table disagree, the table
wins. **Strike an item from the pool when you draw it**; four entries here
were once live work on four lanes because nobody did. Assigned in the
table: CostModel 3.4+ (blu), the diagnostic stick and BatteryReorg step 6
(red); ProtocolStack + OTA (reek) and PlugDeepRecursion (val) queue behind
the disposition campaigns.

Taken and NOT available: `HardwareAbstractionLayer.md` (root; hardware
crypto dispatch is its open question 5, see root's row), `GameEngine.md`
phase 2 (val), `ShellDslReadability.md` (reek, with a file claim),
`ComplianceEvidence.md` (root). `EdgeMeshGameServers.md` phase 2 and
`ThreatModel.md` are DONE (the latter in `docs/Designs/Done/IoT/`).

**The pool holds NO drawable item.** The `DeviceEmulationCatalog.md` queue
is demand-driven: red's sittings produce the next entry rather than a lane
picking one up, and `tools/codex-vm.c` carries a file claim, so announce.

Seed-affecting campaigns take the token per CL as usual.

## The battery choreography (Damian, 2026-08-22; red coordinates)

DONE 2026-08-22: all three items landed (19081 batch parser, 19089
`codex-vm -run-list`, 19086 size-dealt batches) and the quiet-box
re-measure read 123 s wall against ~10.5 min. Ruling (red): the two-phase
shape stays; an in-guest test runner (REPL or mini-kernel) is NOT taken.
Numbers and the bed facts are in `ExaminersAssay.md` "Batch Compile
Architecture" and `OperatorsManual.md` "Batch mode: `-run-list`".

## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads.

**UNOWNED, and measured 2026-09-08 (fester): 43 `*-verify.mjs` graders under
`apps/` and NO script invokes any of them.** They are not weak tests. The one
read while closing C64-2, `apps/c64/c64-verify.mjs`, boots the real KERNAL to
its banner and carries a sabotage control that fires with the ROM zeroed, and
it passes 6 of 6 by hand. `apps/landing/build.ps1` records the gap in its own
prose beside the starmap arm, which is a claim with no runner recorded in a
comment, the shape R-PROSE exists to stop. This is the same class as the two
suites closed today under `apps/*/tests/`, one scale up and across many apps:
whoever takes it decides whether the landing build grades each module it
builds, or the graders move to where something already runs. Nothing here is
c64's or starmap's alone, which is why it is registered in this file rather
than in an app's.

**OWNERSHIP claims in this file were audited 2026-09-07 (fester), separately
from the status audit below, and the rule is that THE DOCUMENT THAT OWNS THE
SUBJECT WINS.** This file is third-hand about every campaign it is not itself
the register for, and six dispatches on 2026-09-07 went to a lane on an
ownership claim that the owning document contradicted. Two were corrected that
day: Prism stage 3 and stage 5c both read "unowned" while
`PrismDevEnvironment.md` heads every stage with red, and the next drawable
plugs row read "UNOWNED" while this file's own standing line says plugs are
reek's close-out lane. Check the owning register before dispatching from a row
here, and correct the row rather than the memory.

**Audited against head 2026-09-07 (fester), which is the only thing that stops
this block rotting.** Nothing re-reads it, so a row survives its own fix and is
then dispatched to a lane that spends a session discovering the work is done:
three did exactly that on 2026-09-07. Re-audit it before dispatching from it,
and put the date here. Rows carrying a MEASUREMENT are the fragile kind, and a
measurement older than the last change to its subject is not evidence (L-COUNT).

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
  call-site policy, in `codex/foreword/core/core-backlog.md`. The dead
  `emit-cce-to-utf8-helper` that this row held for Damian is RULED AND DELETED
  (2026-09-02), along with a closure of six definitions rather than the three
  first named; the account is `compiler-backlog.md`. Nothing here is waiting on
  him.
- **VM admission residue, UNOWNED. 466 IS RETIRED, DO NOT CARRY IT** (re-read
  host-side, fester 2026-09-07). `test-cross-batch`'s run phase was measured
  dying at 466 subjects on 2026-08-28, when `-Jobs` defaulted to **4**. Main
  21043 changed that default to **8** on 2026-09-01, in a single hunk that
  touched nothing else, so the figure describes a configuration that no longer
  exists and re-measuring it needs a run nobody should spend a box slot on.
  Anyone meeting admission death measures it at their own `-Jobs` instead.
- **AND THE SAME HUNK PUT THE RENODE RUN PHASE ON THE SETTING ITS OWN COMMENT
  REFUSES.** `test-cross-batch.ps1:331` is
  `$runJobs = if ($UseQemu) { [Math]::Max($Jobs, 8) } else { $Jobs }`, so under
  Renode the run phase takes the default: 4 when the comment above it was
  written (change 9963, which records that "eight slots was tried twice and
  flakes both times", a passing test returning FAIL_RUNTIME with no uart output
  after ~2 s), and 8 since 21043. Nothing announced the change of meaning and
  the comment still reads as a guard. Pass `-Jobs 4` explicitly on the Renode
  leg until somebody rules on the default; no behaviour was changed here
  because Renode is out by standing rule, so the fix cannot be verified now.
  The admission arithmetic that argues the same way is in `vm-config.ps1`
  beside `Get-VmAdmittedSlots`: 8 x 1100 = 8,800 MB overcommits the ~6,400 MB
  free it was measured against, 4 x 1100 = 4,400 MB fits. The per-guest budget
  is 1100 MB against a 1024 MB host reserve (`-mem` is a ceiling, not a
  footprint, L-REQUEST; re-measure on another box, L-COUNT). The battery half is
  unproven end to end until Damian runs one. A run launched as a background
  shell dies with a fleet session restart and reads as a load-independent kill;
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

**From the 2026-09-01 night shift (root), in the order the lanes are
blocked on them. RULED 2026-09-02 via red (21487) and struck: COMPILER-23
repair 2 YES (blu; it needed NO build, already landed 19813); plugs 2.11 / stage 4 CLOSED, wat2wasm stays;
COMPILER-36 GO (red).** SPARK-4 is RULED 2026-09-02 10:30: val's, after
the games campaign (val's row). Still open: COMPILER-42 list-snoc is
STRUCK (Damian, 2026-09-02: never his decision again; see the standing
rule "LIST APPEND IS THE AGENTS' PROBLEM"). L-NOGATE's second instance
is RULED 2026-09-02 10:25 (the standing rule "A TEST RUNS WHEN IT IS
LIKELY TO FAIL"): `-Internal` runs the cited test chapters; fester, after
sem-equiv-always.

**RULED 2026-09-02 10:45 (Damian, to root) and struck from the list above:**
the `apps/dev-watch` oracle: FIX the hardcoded address, LANDED main 21790
(reek). The two unexplained
gate deaths of 2026-09-01: investigation DROPPED; the one-gate-at-a-time
policy stands and the item reopens only if a death occurs under it.
**THIRD INSTANCE 2026-09-02 13:12:31 (val):** the gate over 21739 + 21820
stopped after `check-cross-smoke OK` with no verdict and no error line,
process gone, while red's token proof ran beside it (two gates up, the
box at ~5 GiB free at launch and falling toward the 13:20 overcommit).
NOT under the serial rule, so it does not reopen the investigation.
**FOURTH INSTANCE 2026-09-02 ~13:05 (fester):** the 21788 gate died
silently inside text-stage1, no FAIL, no diagnostic, empty stderr, log
just stops, beside red's token proof. All four deaths share one
signature (silent, mid-phase) and one condition (another lane's gate
beside it). With ONE GATE AT A TIME now the rule, the condition cannot
recur; a fifth death under the serial rule reopens the investigation,
per Damian's 10:45 ruling. The
AgentGrid grant wording ("token only, ask root for the box") is root's
call and goes to `D:\Projects\AgentGrid\agentgrid-backlog.md`, not here.

**RULED 2026-09-02 10:35 (Damian, to root): COMPILER-36's 64-bit `wrapping`
SPELLING.** Three things, all meaning the same arithmetic mode: (1) a bare
`Integer wrapping` is the mode (the parser takes `wrapping` as a suffix
there; today it parses as a type application); (2) 64-bit literals are
supported, so the i64 endpoints are writable (the low endpoint currently
fails as negate-of-one-past-max); (3) `Integer between -9223372036854775808
and 9223372036854775807 wrapping` is accepted by the band check as the same
band as plain `Integer` and lowers to the identical mode. Out of scope: the
u64 high endpoint does not fit a signed Integer and is not a literal this
row provides. red owns it; add and sub inherit. Struck from the list above.
**BUILT 2026-09-02 (red, CL 21809): the parser takes `wrapping` after a bare
`Integer` as the i64 band in that mode (`parse-type-args`), and the checker
accepts the one-past-max magnitude directly under a unary minus
(`is-int-min-literal`), so `-9223372036854775808` is a literal; (3) was already
so. Fixture `codex/test/ops/int-wrapping-spelling`.**

**RULED 2026-09-02 10:20 (Damian, to root) and struck from the list above:**
`-Internal` ALWAYS runs sem-equiv, YES (fester builds it; `build.ps1`, gated);
delete `emit-cce-to-utf8-helper`, YES, it is dead (reek; seed-affecting,
token per CL, queued behind blu's COMPILER-17 proof); releases are not a
question at this time.

**Prism is SEPARATE from the above and is a FUTURE BUILD-OUT (Damian,
2026-09-02 10:28):** "which will also become a new build out for reek or
fester, probably. we need to get back to that, but not quite yet." The
Prism section below stays as the register; its open items (stage 3
templates, stage 4 Claude panel and its API key, stage 5c sockets, the
public compile page refresh) are not pending decisions and not drawable
until he opens the build-out.

**Deferred by Damian, not pending:** 6, OCI account access for
`OracleCloudArm64.md` phases 5b-5d (the whole design is deferred).

**Not a question until there is a design partner:** secure-element support
in `Identity` (`ThreatModel.md`'s fourth open question).

### Ruled, work in flight (one line each; reversible in one line)

- **NO RUNNER FOR THE APP ENTRY UNITS (Damian, 2026-09-08 04:15).** 910 of
  1,137 app chapters are executed by nothing and the 295 entry units are
  uncitable (BatteryReorg step 6, main 23269); that stays so. His words: "app
  drift is acceptable during this phase of the project. The blast radius of a
  change to compiler or foreword should not be a gating factor; app lift is a
  secondary concern. That is why there is no runner: it costs too much to
  maintain. Apps are a test bed until the underlying code is more stable."
  Reversible when the compiler and foreword are declared stable.

Audited against head 2026-09-07 (fester). Three entries were describing work
that had landed and are gone; two were right that their ruling is unbuilt and
now say when that was last checked rather than when it was first noticed.

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
- **`check-vm-differential` retries once, only when an arm produced NO
  BINARY**; "hosts disagree" is never retried. (red; ruled 2026-08-31, BUILT
  2026-09-07 by blu through the generator. Proof extracts the shipped
  function text and is calibrated both ways: on a no-retry sabotage the two
  retry arms go red while the two never-retry arms stay green.)
- **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`,
  `.cross-refusal`, `.no-cross`, `.vmargs`) and warns on everything else.
  (red; ruled, STILL NOT BUILT at 2026-09-07: `p4-stale-check.ps1:56` says
  "Reported as a warning, not a failure" and `Show-Untracked` fails nothing.)
- **zig 0.16.0 is installed at `D:\zig-0.16.0`** (2026-08-16; verified
  present 2026-09-07). This entry read as a bare "5" for three weeks.

**Two of the above are rulings nobody has built, not work in flight**, and
they have sat here since 2026-08-31 being re-read as though they were
progressing. Whether an unbuilt ruling belongs in this list or in the register
that owns its script is Damian's call; they are left here, correctly dated,
rather than moved on a lane's judgement.

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

**ONE HARDWARE SITTING REMAINS, FOR ALL TIME (Damian, 2026-09-07 21:20, after
sitting 14 returned one bit).** Fourteen sittings have not answered why the
stick loses writes, two of them flew on claims that were false when made (the
sitting-3 guard "sees" a lost bank; `cfg=off` "honoured"), and each flight's
only durable record was the medium under test. So: nothing flies until ONE
image carries every open metal question at once, ships its whole record to the
dev box over TCP (the channel this board has proven three times) so the stick
is a subject and not the register, carries the WORKS-62 flush, and is
rehearsed as those exact bytes with every arm answering. Its composition is
`HardwareSitting.md` "THE LAST SITTING"; root signs it off; no lane asks for a
sitting for any other reason. A question that can be answered in the bed, by
reading, or by a different design is answered there.

**MAIN AND THE BOX ARE SYNCHRONIZED BY DIFFERENT THINGS (Damian, 2026-09-02
15:55).** The token lands an already-proven seed CL on main; the commander
grants the box. `-Internal` is banned (15:52, "it shaln't be run"): a change is
verified by the tests it touches, one at a time, plus for a seed CL the scratch
fixed point and the BVT as granted runs. Each lane audits and kills its own
leftovers at every handoff and after any killed run. **The rule and its
measurements live in `CLAUDE.md` R-GATE**; the afternoon of floor rules that
preceded it (self-granting on a 3 GiB reading, one gate at a time, the watcher
ban) is superseded and binds nothing.

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
allocator. The in-place append is the contract, not a defect: it took more than
4 GB before appends grew in place. Everything built on the other reading was
ripped back out of the seed (blu, 2026-09-03), and
`codex/compiler/Types/Builtins.codex` now states the contract at the table,
because the ABSENT contract is what pulled two agents into rebuilding it.

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
entry chapter is not one. **UNOWNED, from Steve Howell's issue 115:** four
deck-discipline dependencies are declared nowhere -- Lexer cites nothing and
rewinds the bivy needing `deck-record` to be the real intrinsic, so in a subset
bundle it compiles clean and page-faults, the fallback being a silent identity
(L-BAILVALUE's shape). It belongs in `codex/compiler/compiler-backlog.md`.

**THE 2026-09-07 BATTERY RESULTS NAME THE PREVIOUS SEED.** Kernel 303 total,
293 pass, 9 skip and apps 243 total, 233 pass, 9 skip (fester, on Damian's
grant, the one fail in each being a stale golden since re-recorded) ran
against seed `9E5C7780B7FBC69C`; blu landed seed `E103FF07EA9EE7B7` at main
23058 afterwards. They are the right answer about the tree at that time and
the classifier fix stands, but quoted against the current seed they name the
wrong compiler. The first `-All` after this is the first run of three newly
reachable test directories AND the first on the new seed at once: a red
anywhere but `accumulator-corpus` must be separated into those two causes
before it is read as either (L-LINEAGE).

**DO NOT ADD A TEST TO THE GATE OR THE BATTERY ON YOUR OWN INITIATIVE; GET
RED'S CLEARANCE (Damian, 2026-08-21: "haphazardly adding tests to the gates
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
banned. `-Jobs 8` on every parallel harness; Renode arms run ALONE; every heavy
run is granted by the commander (Damian, 2026-09-02 06:16, superseding the
2026-09-01 two-run allowance and the 2026-08-27 `-Jobs 4`). Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (`DevelopersGuide.md`, "Effects and Act Blocks").

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
once.
