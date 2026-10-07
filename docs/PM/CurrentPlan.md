# CurrentPlan -- the shape and the priority order

*The fleet's open work and its priority order, and nothing else. A closed item
is deleted; the CL, the GitHubUpdate and the doc named beside an item are the
record. Write a pointer, never a tombstone or a story.*

A history pass over the reference docs (`OperatorsManual`, `ExaminersAssay`,
`DevelopersGuide`, `ArchitectsSketchbook`, `HardwareSitting`) is not ordered;
Damian says when.

## FLEET COMMAND (Damian, 2026-09-12)

ROOT commands the fleet; red assists. The lane table is the dispatch.

- General recursive method specialization and field-local runtime
  polymorphism are outside the approved scope; COMPILER-83 owns the boundary.
- A plug red on BOTH the old and the new plug, in a subject the change does not
  touch, is a register row, not a promotion block (root, 2026-09-12).
- AgentGrid integration is Potato's; its gaps are
  `D:/Projects/AgentGrid/agentgrid-backlog.md`.
- A changed build script alone does not establish seed reachability; inspect
  the inputs. An unverified shelf needs its own proof before landing.
- **Outside contributions (Damian, 2026-09-10):** a PR is ingested through
  `PerforceProcess.md` section 7 with the contributor credited in the CL; the
  PR and its issue close at Git publication with the commit, the CL and the
  credit. No GitHub merge button.
- **Parallel shelf work is authorized (Damian, 2026-09-10):** numbered shelves
  with explicit dependencies, rebased onto main before fresh proof. A fixed
  point does not grade typed IR or hosted output.
- **Declared test exclusions are deliberate (Damian, 2026-09-10):** release
  batteries keep the runner's `-Tier all -Jobs 16` selection; report
  exclusions apart from failures; never remove sidecars or add flags to
  override them.
- An item that originates in one app or quire lives in that register
  (`apps/<app>/<app>-backlog.md`, `codex/<quire>/<quire>-backlog.md`) and is
  named here only if it blocks a track. Do not recreate `docs/PM/BACKLOG.md`.
- The investigation charter and its open recommendations:
  `docs/PM/Active/Stories/TheLostParadise.md` part 11.

## THE FLEET DISPOSITION (root commanding)

**Focus (Damian, 2026-10-07): "we aren't working on prism right now, focus is
on compiler, forwards, kernel, and uoaix".** No lane takes Prism, Spark or
landing-page work until he reopens it. **"Spark and Fishtank can be ignored"**
(Damian, 2026-10-07): their rows and grades are not carried to him.

Docs go straight to main; a code arc gates once per arc
(`CoordinationProtocol.md`). The lanes table wins over this section.

- **Games and landing (val):** a card reads "playable now" only while a visitor
  can play; a game that fails on wasm is a parity finding for reek (one
  message, no workaround in the game).
- **The wasm plug (reek):** wat2wasm stays on the PATH as the assembler
  (Damian, 2026-09-02). The plug alone takes no token; reek announces before
  touching `build-page.ps1` or `page-lenses.ps1`.

## MAIN AND PUBLIC RELEASE

**MAIN OPEN.** The token is granted for proven seed CLs only.

Latest public release: **Update 68** (2026-10-07, commit `8557982b`, seed
`E29C439B`; `GitHubUpdate68.md`). **Update 69 is in flight (red)**: COMPILER-124,
COMPILER-129 (closed for 69 on every plug a runtime here grades), diag.img
DDD55081, the games `random_get` host. Re-measure the seed and mirror tips at
the release.

**GPU compute on our own OS is the GSP firmware route, TIER 1 (Damian,
2026-09-30: "use the gsp firmware is an absolute tier 1 requirement. forget this
dll stuff").** Our own driver against the GPU's GSP firmware and our own code
generation; no vendor DLL, no vendor driver in a VM, no hand-off to a native
helper. Design: `docs/Designs/Active/OS/GspGpuStack.md` (reek). Rulings
(Damian, 2026-09-30): target this box's RTX 4060 Ti, and "figure out how to use
the 970 too" (GTX 970, `10de:13c2`, pre-GSP, through the sittings); the metal
bed is this box rebooted into Codex OS, a planned window, "not now"; no
passthrough; the firmware stays out of the depot and every shipped image until
the licence is settled; no interim option C ("timewaste"); compute and 3D both
("we need both"); vendor graders deferred ("not critical now"). Scope: "we
obviously need to add that all to the real hardware tensor core support we need
for the diffusion app and replacing that nvidia dll": one stack serving the
desk's 3D on metal (WORKS-80), compute, tensor cores for diffusion (WORKS-3),
and in the end `nvcuda.dll` behind codex-vm's GPU bridge.

## Standing rulings by subject

- **Brand (Damian, 2026-08-29):** the public name is the Cobblestone Project;
  the language, compiler and artifacts stay Codex
  (`docs/Designs/Done/Marketing/Cobblestone.md`).
- **The webserver (Damian, 2026-08-28):** the desktop never gains `Network.*`;
  the webserver is a system service and its pane is the admin console. Hosting
  the compile page from our own kernel is a separate later step, not coupled
  to it. The bed is the instrument (codex-vm NAT port-forward).
- **Track A, the stick:** the I219 medium-death hunt is PARKED (Damian,
  2026-08-24; `I219IsNotAnE1000.md`). The identity file stays on the ESP;
  auto-unlock is bed-only (2026-08-18; rotation in
  `Designs/Done/OS/Identity.md`). Metal-only questions wait for a sitting root
  composes (standing rules).
- **Track B, the network (blu):** the bed is the only instrument. The NETIO
  ceiling (Damian, 2026-08-21): the NIC comes first, no stage may end the run
  (`net-io-drain-ticks = 96`, `codex/test/net-drain-budget`). ICMP is
  send-only; `icmp-parse` stays latent; whoever gives `syslog-parse` a
  production caller fixes its quadratic `acc &` in the same change.
- **Track C, the trust audit (val):** C1 and C2 are enforced
  (`IndependentRechecker.md`, `docs/Test/Active/DDC-QUINE-ARM.md`); C2.5 stage
  4 (proof terms) stays deferred unless Damian calls it. The rechecker derives
  type-variable instantiation itself; the compiler does not emit it
  (L-CAPABILITY-LOST).
- **Track D, bytes we did not produce:** `VerifiedFormatParsing.md` section 10
  is the census and queue; the guard pattern is in `ExaminersAssay.md`. Open:
  item 18, `OtaBoot boot-load` (reek), latent, no production caller.
- **Prism, a future build-out (Damian, 2026-09-02):** not drawable until he
  opens it. Register `apps/prism/prism-backlog.md`, design
  `apps/prism/design/Active/PrismDevEnvironment.md`. Product scope (Damian,
  2026-08-24): compile/transpile on the fly; the pre-baked IR path goes. Stage
  4's acceptance needs Damian's key and a billed call (DEFERRED). Page traps:
  rebuild from a seed at or after `7B6A4950`, regenerate
  `build/output/Codex.codex` first (L-SAMEVER). The public `compile/` refresh
  is Damian's. Rulings (Damian, 2026-08-28): the stage-5a Linux bed is ALL of
  the options (WSL arms, a QEMU Linux guest, 5b's `.exe` verified natively);
  "boards" means IoT board build targets per HAL board chapter; "bench" means
  our codegen benchmarked against any configured build chain; the zig work
  rides last.
- **The games rules (Damian, 2026-09-29: "i want a thorough review of all the
  games, every move from every possible board state class. no more half baked
  rules."):** every engine in `apps/games/classic` is graded. A new or changed
  game follows the backgammon idiom: the rules as prose in the engine chapter,
  cited to their source; the engine answers legality for a whole move, never
  the page; set-up exports let `<prefix>-verify.mjs` build each state class and
  assert its whole legal set, allowed and refused (L-BOTHARMS, L-VACUOUS); an
  oracle written from the rules text runs against the engine; a sabotaged rule
  turns the arms red.
- **The local image generator page (red):** `apps/landing/ImageGenPage.codex`
  (`web/imagegen.html`, card `td10`, graded by `apps/landing/test-imagegen.mjs`).
  Open: its steps and source link work only once a release carries
  `apps/diffusion` to the mirror; the next page edit turns step 5 (the CLIP disk
  by hand) into a download into `<models>\tokenizers\clip`, which `serve.ps1`
  mints itself. The site publish is Damian's.
- **Pool:** the two Magic games (val; `apps/games/magic` findings and
  `WorkPlan.md`, `apps/games/codexmagic`), taken only when a lane has no unit.

## Doc compaction (Damian, 2026-10-07: "clean up all the currentplan and uoaix project docs to remove tombstones and war stories, compact and succintify that stuff")

Pure docs: no token, no gate, copy up per file group. A doc states what IS (R-HISTORY, R-PROSE).
**Delete:** closed or "built" rows and sections whose work landed (the CL is the record); "Earlier:", "superseded", "was", "retracted" blocks; session narrative, bisect and debugging stories, who-found-what-when; measurements and CL numbers kept as history; restated summaries.
**Keep:** every open item; every ruling with Damian's quoted words; wire, file and client facts we do not own; magic numbers and why; contracts a caller relies on; live code citations.
Before deleting a paragraph, ask what it carries that lives nowhere else (L-ROWROT): a lesson becomes a `LESSONS.md` row, a trap becomes one sentence. Replace text, never append. Merge down before each copy-up; a lane row's own NOW/NEXT text stays its lane's.

| owner | files under `apps/uoaix/` |
|---|---|
| fester | `Magery`, `ActiveSkills`, `Skills*`, `GameCombat*`, `GameResurrection`, `GameMonster*`, `CharacterStylist`, `ItemActions`, `GameHarvest`, `GameCraft`, `World*` |

Each lane takes its share after its current unit lands (red is on the Update 69 release and holds none), and deletes its row here when done.

## The lanes (RULED by Damian 2026-08-15)

The table is the assignment; re-read it on every merge-down. A row is NOW,
NEXT and standing, nothing else; the register named in it holds the detail.
The compiler-bug order is whatever `codex/compiler/compiler-backlog.md` shows
open. fester is otherwise held for the hardest problems (Damian, 2026-08-26).
DeskScheduler's remaining scope is pane policy and bounded-work enforcement:
each pane declares a rate or a budget and skips or runs late on a miss
(2026-09-24, `DeskScheduler.md`).

| agent | now | then | standing |
|---|---|---|---|
| **blu** | **NOW:** none; next own pick from `uoaix-backlog.md` (root's standing GO). | **NEXT:** after root's next live switch, measure the UOAIX-50 seal rate and the UOAIX-29 wheel peak from the `ECONOMY ROWS` commit line and size the 256-entry pool. | Camping, Cartography and Taste Identification are reek's (root, 2026-10-06). Built, awaiting root's live switch and Damian's grade: UOAIX-50 (reinstall the map cache, UCC1 24), UOAIX-30 (fresh worlds only), vendor reload past 128 lots, F34, UOAIX-58, vein-trip, UOAIX-42, F44-F46, F30, UOAIX-60, F51, F53, UOAIX-28, UOAIX-59, F57 (regenerate the dwd), F56 (Damian confirms the 0x1A movable flag makes wheat and cotton pickable), UOAIX-52, UOAIX-44, F59-F61 (fresh world). Construction 35373 stays off main. Blu keeps `codex/os/net/**`. |
| **val** | **NOW** (root, 2026-10-07): UOAIX-99, a split or stack of a vendor-lot stack refuses every later save (row in `uoaix-backlog.md`); then UOAIX-49 part B, bots 2 to 6 trade from their testing kit. Shelf 35382 is paused civic-input work. | **NEXT:** root dispatches. | **For Damian:** DATA-1, DATA-W2, FW-3, GPUSHOW-1/3, GLOBE-1 keep their owning rows and DamianDecisions options; FW-2 needs a real-GPU page review; virtual-desktop wording in DamianDecisions 3.11; FW-1 options Deferred. |
| **fester** | **NOW** (fester's pick, root's GO 2026-10-07): UOAIX-49 never-run list, MageryActions (18 of 27 never run): a `soak-bots.ps1` spell family casting every circle at its proper target, graded by MageryActions entered definitions before and after under `-RawFlags cover`; then fester's doc compaction share. UOAIX-81 is built (all 64 spells; Damian's client grade open). | **NEXT:** none registered; ask root. | The testing kit's 25-slot tool bag is full: the next kit item needs a second bag. Shelves 35378 and 35450 are not regraded at head. |
| **reek** | **NOW:** COMPILER-122 parked as shelf 38881 (seed AEFBF586, proven at main 38907; its 6 files are open in the reek workspace): at MAIN OPEN, if any seed landed since, shelve, revert `seed/Codex.cdx` and `TechnicalDetails.md`, merge down, then re-run the lane proof chain (memory `lane-seed-proof`: fixed point, sign, a `-Measure` compile showing `bivy-close=` and no `bivy-hwm`, BVT, cite-gate over `opening.codex` and `Core/PhaseAllocator.codex`, GPU wall-budget reds rerun serially), `install-seed.ps1 -Lane`, token, submit, copy up. | **NEXT:** UOAIX-49 soak `pet` family, shelf 38919 (description has the run): make the family target a creature whose spawn profile is tameable, then a 6-minute covered soak on a head build until `gws-tame` is entered; land the shelf. | Plugs close-out lane and codex-vm GPU bridge; `tools/codex-vm.c`. Eight Britain shops and their lineup copies fill the 16-row vendor table (`gv-add-vendor`): a ninth shop needs a wider GVS1 table. Tokenless `codex/foreword/` landings name their closure check. |
| **red** | **NOW** (root, 2026-10-07): the Update 69 release at main head; `.claude/skills/release/SKILL.md`; a partial battery finishes by its reds only. | **NEXT:** (1) COMPILER-109 (Lowering, recipe in its row; reek landed its opening: take the rest only if reek holds none). (2) COMPILER-123 is open only for Damian's grade: `pwsh build/test.ps1 -Tier hardware -CodexCdx seed\Codex.cdx -ApprovedBy damian`, `t5-encoder-step` PASS (row). | Awaiting Damian's client grade: UOAIX-76 (food), the struck-player retaliation fix, the "Not Implemented" skills group (Parrying, Provocation, Enticement, Forensic Evaluation, Spirit Speak, Poisoning, Stealing: NPC backpacks only), UOAIX-53 (F26 hand tools), F32, F37, F40-F42, F19/F20, F23, F25, UOAIX-36, UOAIX-39, UOAIX-40, UOAIX-35. Shelves: 36038 (CompositeBritainWorld census probe), 35360 (town-network goal controller, paused), 33249 (T5 single-pass proposal, deferred). Root grades the client on the composite. A change to a chapter the composite routes through runs the composite four-boot set before landing. |
| **root** | **NOW:** commander; the CurrentPlan compaction. Live UOAIX shard STOPPED: restart on a fresh world with a head candidate (5-minute soak, switch-fresh, then a 30-minute canary run after the gate soak, same port). | **NEXT:** Update 69 lands through red. Shell refinement scope: `docs/Designs/Active/OS/ShellRefinement.md`; build tooling closure: `Build.md`. | |

**Plugs are reek's close-out lane** (Damian's direction, 2026-08-18): the
register in order, one entry at a time, said in status.json.
`codex/plugs/zig/**` is ordinary fleet code (Damian, 2026-08-18); credit Steve
in a CL that changes what he wrote and flag it in the next GitHubUpdate.

## The pool (Damian, 2026-08-18)

Every open design in `docs/Designs/Active/` is available work; a lane that
empties draws in order, says so in the table, and strikes the item. Taken:
`HardwareAbstractionLayer.md` (root), `GameEngine.md` phase 2 (val),
`ShellDslReadability.md` (reek), `ComplianceEvidence.md` (root). The pool holds
no drawable item now; `DeviceEmulationCatalog.md` is demand-driven. Seed
campaigns take the token per CL.

**Unowned registers.** The document that owns the subject wins over any claim
here, and a row is verified against head before dispatch (L-ROWROT, L-COUNT).
`docs/Designs/Active/OS/OracleCloudArm64.md` is DEFERRED (Damian, 2026-08-18);
whoever lifts it inherits `codex/os/net/Arm64NetIO.codex`, a twin of the x86
send path with neither the checked-send fix nor the NETIO drain cut
(`arm64-net-io-max-ticks` 500).

## Decisions

Numbers are stable ids, not an order. A ruled item shrinks to one line and its
reasoning moves to the owning doc; a ruled item whose work landed is deleted.
**PENDING holds only what Damian alone can decide (Damian, 2026-08-20):** an
outside relationship, an account, a spend, a product direction. A technical
trade-off with a defensible answer is the commander's call.

### Pending -- only Damian can answer

- **Deferred by Damian, not pending:** OCI account access for
  `OracleCloudArm64.md` phases 5b-5d; the Prism stage-4 API key; FW-1's three
  fix options; CORE-9's CA policy (tolerable as is, 2026-09-30); PRISM-13 (no
  Mac near term).
- **Not a question until there is a design partner:** secure-element support
  in `Identity` (`ThreatModel.md`'s fourth open question).

### Ruled (one line each; reversible in one line)

- **No runner for the app entry units (Damian, 2026-09-08):** "app drift is
  acceptable during this phase of the project. The blast radius of a change to
  compiler or foreword should not be a gating factor; app lift is a secondary
  concern. That is why there is no runner: it costs too much to maintain. Apps
  are a test bed until the underlying code is more stable." Reversible when the
  compiler and foreword are declared stable.
- **Heavy-pane stranding: fix the allocator** (Damian, 2026-08-27; val;
  acceptance is the reopen-after-buried-close row falling toward zero,
  `ShellRefinement.md` 6.4).
- **The Rulebook's over-application rule binds every plug that keeps an arity
  map** (red, 2026-08-24; root, 2026-09-25).
- **`p4-stale-check` fails a dropped add of a tracked source extension**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`, `.cross-refusal`,
  `.cross-fatal`, `.no-cross`, `.vmargs`) and warns on everything else.
- **zig 0.16.0 is installed at `D:\zig-0.16.0`.**

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex` | fester (kernel-side) |
| `codex/plugs/arm64/Arm64Runtime.codex` | root; the block/servicer sections are fester's by agreement |
| `codex/os/kernel/{VirtioNet,VirtioBlk}.codex`, `codex/plugs/pe/Arm64PeWriter.codex`, `build/build-arm64-img.ps1` and its generator | FREE -- announce |
| `tools/codex-vm.c` | reek; announce to blu before touching the NAT paths |
| `build/test-cross-batch.ps1` | FREE -- announce |
| `GopWizard.codex`, `apps/guios/**` | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, the lifted probes) | root, `DiagnosticStick.md`; step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex`, `GopComposite.codex`, `GopFiles.codex`, `GopIcon.codex`, `GopSettings.codex`, `GopBoot.codex`, `UefiConsole.codex`, `StickSource.codex`, `DevConsoleBoot.codex`, `DevConsole.codex`, `DevDebugger.codex`, `codex/foreword/ui/**` | val, the Shell Refinement campaign (`ShellRefinement.md`); announce before you start and check which `ds` cells are spoken for. `comp-text` stays fester's |
| `apps/works/GopEdit.codex` | FREE -- announce; the Editor's rules are `works-desk-contract.md` 0.6 |
| `apps/works/RepoProtocol.codex`, `RepoProtocolPersist.codex` | FREE -- announce |
| `apps/works/AgentBundle.codex`, `codex/test/apps/agent-bundle-*` | FREE -- announce |
| `apps/works/GopReview.codex` | FREE -- announce; `GopFacts.codex` is red's |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `apps/works/GopWeb.codex` and `ds` cell 248 | val: 248 is the block the desk shares with the web service (`dk-web-cell`), 244 the pinned-pill mask (`dk-pinned-cell`), 252 the hover bubble's save block (`dk-bub-cell`); the `ds` block is FULL |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/os/sched/**` and the preemptive scheduler | val, `PreemptiveScheduler.md`; reads blu's `codex/os/net/**` without changing it |
| `codex/test/cost/**` and `CostModel.md` | FREE -- announce |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val |
| `codex/plugs/**`, `codex/plugs/plugs-backlog.md`, `codex/plugs/wasm/**`, `codex/plugs/spirv/**`, every `run.ps1` under `codex/plugs/`, `build-page.ps1`, `page-lenses.ps1` | reek, the close-out lane; excludes entries other lanes hold (named in the lanes table) |
| `apps/landing/web/compile/**` (the Prism page) | fester |
| `apps/games/**`, `apps/landing/**` except `web/compile/**` | val and red shared (blu holds the seven games it graded) |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | blu |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators | reek (Damian's direction, 2026-08-16); catalog and order: `ShellDslReadability.md` |
| `codex/foreword/compress/**`, `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` | reek; seed reachability is measured per file |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce; takes the token (seed-affecting) |
| `apps/diffusion/**`, `codex/foreword/ai/SafeTensors.codex`, `codex/test/apps/diffusion-*`, `codex/test/gpu-files/*.safetensors` | val, `Diffusion.md` |

A claim nobody honours is worse than no claim. Announce before you go into a
claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

**A lane hands off at 75 percent measured, not before (Damian, 2026-09-08: "58%
is not enough action on the context we load at init; agent estimates of token
spend are almost always very much over"; 75 ruled 2026-09-28).** The number is
`build/measure-context.ps1`; a lane's estimate is not a number. Root orders a
handoff at 75 or at a genuine boundary above it, never below, and cancels an
early one.

**Everything written to Damian is in the Codex Prose Language (Damian,
2026-09-08, an experiment):** the axioms and banned words of
`docs/DevelopersGuide.md` "Codex Prose Language (CPL)". Lane-to-lane messages
and CL descriptions are not bound. His reason: "there is a tendency to
communicate with me using implicit bindings, and it is even harder for me to
know what is implicit than it is for you."

**Emulators (Damian, 2026-09-24):** codex-vm for x86-64, QEMU for every other
architecture, Renode only for a board model QEMU lacks (`Build.md` has the
measurement). Renode arms run alone.

**Sittings are open (Damian, 2026-09-23: "Sittings are open, but same rules as
before: answer all open question in 1 sit, and don't waste my time or my
back.").** A lane registers its metal-only question with root; root composes
ONE sitting answering every open metal question, rehearsed in the bed as the
exact bytes flashed (L-REHEARSE), with an independent output channel
(L-CHANNEL), and asks Damian once. Queue and records: `HardwareSitting.md`.

**Main and the box are synchronized by different things (Damian, 2026-09-02):**
the token lands a proven seed CL on main; the box is the lanes' own
measurement, arbitrated by the commander. `-Internal` is banned. Each lane
kills its own leftovers at every handoff and after any killed run (CLAUDE.md
R-GATE).

**A gate runs only the steps the change can affect (Damian, 2026-09-02: "we
don't need to perform full builds to test an app").** The fixed-point core and
the BVT run only when a compiler, foreword, seed or build path moves; the
app-sweep subject selector is open (fester).

**A test runs when it is likely to fail, not as ceremony (Damian,
2026-09-02).** Three buckets: the BVT on a seed gate; the test chapters citing
a chapter the CL changed; `-All` as the release net only. A new test in none
of the three is a defect in the CL that added it. Do not add a test to the gate
or the battery without the commander's clearance (Damian, 2026-08-21:
"haphazardly adding tests to the gates slows everyone down"); a
`diag-arm.ps1` row costs a gate nothing.

**Batch your gates (Damian, 2026-08-28):** small CLs land on the dev stream
with targeted tests; a gate runs once per arc and the batch copies up grouped
(P-COPY1). Docs and registers need no gate. The doc-count drift check is
advisory and runs on the release gate only.

**List append is the agents' problem, never again Damian's (Damian,
2026-09-02).** No lane re-presents list-append semantics to him. A change that
makes one linear line quadratic has failed. **COMPILER-42 is closed and refused,
with a moratorium on discussing it (Damian, 2026-09-03):** `list-push` never
copies; an alias that will mutate is dealt with at the holder
(`codex/compiler/Types/Builtins.codex` states the contract).

**The compiler's memory contract (Damian, 2026-08-28):** the full self-compile
completes within a 2 GB heap high-water mark in both text and CDX modes; more
is a defect, never a reason to grow the guests. The release gate's
`text-stage1` check is the only trigger (Damian, 2026-09-24).

**We do not hold our work for an external contributor (Damian, 2026-09-02).**
Our CLs land in queue order; a PR rebases onto main. A contribution is judged
on one test: shaped for Codex and its own consumers, never for a transpile
target.

**A finding about someone else's project is not ours to publish.** `//Codex/main`
mirrors to GitHub and GitLab; a bug report about an external project is its
author's to receive first. In a design, state the target's behaviour as a fact
about the machine we build on. CL descriptions are not mirrored.

One line each: battery runs are Damian's (release proofs excepted). Goldens
stay parked during active GUI work. Prose about our own code is deleted in
files you touch. The em-dash stays banned. `-Jobs 16` on every parallel
harness; a fan-out launches on the lane's own measurement against the
per-guest bar (`CoordinationProtocol.md`). Do not lower `deck-headroom
-MinMargin` to clear a red. `print-line` converts and `print-line-raw` is
byte-exact (`DevelopersGuide.md`, "Effects and Act Blocks").

### Declined, and therefore not available work

Declined is not deferred: do not re-propose one, build a smaller version, or
open a design that assumes it. If a ruling looks overtaken by events, that is
one sentence to Damian, once.

- **Line-level debug info.** A statement about what Codex is for.
- **An app compile gate.** Compiler work must not be coupled to app drift.
- **The ARM64/RISC-V LIR retarget.** What landed stays; the rest is not
  reopening.
- **Plug arms for targets whose runtime is not on this box** (Damian,
  2026-08-25: no toolchains installed to close them). `build/plug-builtin-baseline.txt`
  records the sites.
- **The store cutover** waits on infrastructure.
