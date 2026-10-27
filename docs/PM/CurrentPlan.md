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

**Lanes (Damian, 2026-10-16): "i want one lane dedicated to uoaixassist, one
to do any compiler/language/kernel/forwards work, and the rest on uoaix".**
**Server lane (Damian, 2026-10-20): "use red for any bugfixes, or ongoing work
on uoaix server".** blu is the UOAIX Assist lane, directed by Damian; red is
the UOAIX server lane; val, reek and fester take the compiler, language,
kernel and foreword work and the unowned Active designs (root's assignment).

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

Latest public release: **Update 69** (2026-10-07, commit `fa0eaade` on GitHub
master and GitLab main, seed `58D18336`; `GitHubUpdate69.md`). Re-measure the
seed and mirror tips at the next release.

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
  4 (proof terms) stays deferred unless Damian calls it; its wire is designed
  (`IndependentRechecker.md` section 5). The compiler publishes every call
  site's instantiation and the rechecker derives its own and holds the two
  against each other (L-CAPABILITY-LOST).
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
| **blu** | **NOW:** no Perforce work open or shelved. The relay runs main 48620: restarted 2026-10-25 09:54 local on Damian's word by the sub-init's line, log `assist-2650-20261025-095458.log`; its PID is the 2650 listener's. A session started in `apps/uoaix/assist` (Blu-assist) begins with `/assist-init` (`apps/uoaix/assist/.claude/skills/assist-init/SKILL.md` and that folder's `CLAUDE.md`, main 48629). Seen working live 2026-10-25: the cave miner's At my feet put-downs with the digging going on (48577); the smelting loop at delay 0 and the smallest ore two at a time, strength +6 in one session (48594, 48620); `MakeRoom` putting thread, not shirts, on the floor (48564, Magincia). Live, not yet seen working: main 48572 (room made for a vendor's thread only when the purse pays for the whole stock; a put-down pile found again under the serial uo1998 sends it back with, where every pile read "gone" and was left on the floor); main 48577 (the cave miner's At my feet and collection to Ore to, the lumberjack's: Circuit tab mining rows Ore to, At my feet, Collect at; `mine-setup ore-to | feet on|off | collect-at <n>`); main 48583 (a cave within its radius and CaveRuneSlack is walked to, not recalled to; the recall only when that walk fails); main 48588 (the mining toolbox within CircuitReach of the cave fetched from before a shovel trip, the house's at 2481,73 for Home2; ore piles always hopped, never carried); main 48594 (the smelting loop's delay goes to 0; a refusal at 0 tries 500 ms); main 48600 (the mining toolbox's spot kept in the settings, so a box out of view or unseen since a restart is still fetched from: 48588's fetch never ran live, the relay restarted at the house); main 48612 (the toolbox's spot kept whenever the server draws it; the shovel trip takes from the box at its last known spot before the vendor and the bank rune; a hop moves every stack on its tile and drops only onto a pile of the same art and hue, the large and small ore never merging; cave mining's Collect now, `mine-collect`); main 48620 (the smelting loop lifts the smallest ore, 19B7, two at a time; a collection hop already in reach makes no walk, the stand on a pile's own tile taken beside it). Left on shop floors 2026-10-25 before it: 82 thread at 1362,3779 (Jhelom, Ella's), 44 at 1547,1662 (Britain, The Right Fit), and smaller piles in Ocllo, Nujel'm and Vesper. Live, not yet seen working: main 48564 (`Map`/`Set` throughout the Assist and the walk planner on arrays, from 48560; fourth-audit findings 1 to 3; a vendor's answer from past 3 tiles, the shopkeeper convo per `human.m:2027`, followed by closing in on it before one more ask; `MakeRoom` never puts shirts down, the stock bought whole and the thread over the limit put on the floor, Damian 2026-10-25). Seen working live 2026-10-25 on 48560: homespun home sewing at about 1110 ms a shirt, sales to every tailor, thread on the pile, the bank stop by the Britain rune, the reagent loadout refilled. At 09:47 relay time (before 48564) seven shirts were put down one by one at 2966,623 in Vesper's north tailor shop for Morena's thread, and Damian stopped the circuit; they lie there unless he picked them up. Suites: test-assist 817/817 at 48564. NEXT, the fourth audit's findings still open, each with a world start to prove it: (3b) a homespun with no kit, cloth at home and no thread to buy never buys kits (the shop is passed over as idle, and `HomespunHome` does not go home with no kit and no thread), so the floor's cloth is never sewn (a world with a tailor selling kits but no thread shows it; `HomespunWorldTests` redraws the floor's cloth at a home landing); (4) a failed home step while at home has no backoff (`HomespunHome`'s blocks test `!home`); (5) a failed same-town walk in `ToShop` falls back to a recall; (6) `Fetch` keeps a far pile in `dumped`, so every bank stop stays critical; (7) a non-silent vendor failure in `BuyShopThread` is asked again within the visit (`SkipVendor`'s first span is none). Then a fifth independent audit; bounce only with Damian's word after a clean one; at the bounce `timing answer 3000` once he is in (his own play's sample set 4500 at 17:46:53), then `circuit go`. Open: ShopIdle found work at The Lord's Clothiers (a tailor paying the floor) where the visit sold nothing, cause not yet read; Damian's call on the jump to Vesper every round (sewing leaves 24 to 29 mana, under the jump's 33, so each round goes to Tate while he buys and no other shop's thread is bought). The settings hold `runes.reserved=4424FD7B` (the Moonglow bank roof rune, Damian's own). Seen working live 2026-10-24: home sewing at 1112 ms a shirt (was 1269; the rest is the server's own replies, 845 ms use to picks and 250 ms to the result, per the sew timing line each home stop now logs), the RUNE BLOCKED report, a shop not reached passed over, no repeat kit or thread ask after "has none", the buy circuit carrying the shop runes bag, the bank stop by any city rune. Seen working by Damian 2026-10-23: the feather circuit (hunt, carve, the server's false "no room" line read as a merge into the pack's stack, the ribs put back on the corpse), the house parts drawn in priority order and his registered house in gold on the map. Live, not yet seen working: a blocked tailor landing by the bank's city rune (a bank by a carried rune first); a door that swung shut by itself opened again (either leaf of a double door); a teleporter linked from its tile's lower floors within 16 (Moonglow 4540,898); the buy circuit's shop passed over when no keeper's list names anything wanted, a list older than a restock period counting as unread for both trip and asking, and no bank stop before a trip without a short purse or a heavy pack; the carried rune landing nearest a shop chosen over the city's; "bank" never said out of a banker's hearing (a town with no bank goes by a carried city rune first); a short reagent refill not followed by a bank stop for reagents for 30 minutes; homespun not going home with a pack full of shirts and nothing to do there; the house key put back from within 5 tiles of its key spot; Keep thread's restocks seen live and due by phase; the house door drawn whole on the map; the purse from the bank box's top-level gold only (the gold stock bag gone) and a purse the box cannot fill ending the circuit; the loops, mill, spin and weave, and the reagent loadout on the task runner (`AssistStructure.md` stage 3); Keep thread waiting until the vendor is due (`RunKeepThread`); the Vendors tab's identify with a wand of identification; the Reagents circuit; the circuit map; the gear menu's popups; the start and stop controls on the Circuit job card; the pack item count by the 1998 rule (a spellbook or key ring one item); the Session status's signed notoriety; the house placement pretest (`AssistHousePlacement.md` stages 2 to 4: the `[house` command, the map face, the placement log); no "wild 1" mark from a chop's logs (a server delete in view takes a resource mark at once); an answer after the player's own lift not timed for "answer"; the Shops tab's Buy list surveys by date and the vendor's trades grid; no Circuits tab blink with a card collapsed; a house's walls and footprint kept after a bounce (map, walks, the homespun pile's house); shop runes bags set in the pack; a part lift's drop judged by its remainder (the bank deposit that ended the homespun circuit at 13:08 and 13:17); no key turn on a door whose own lines say it is locked (read from every line: the door barks as the player); our own house vendor not catalogued during a loop; the tailoring steps in place of Night (Buy thread, Weave, Sew from bolts, Sew from cut cloth; cut cloth first); a shut door opened past a person beside it; no recall without a task (a Go with shirts sells first, the pack's house rune recalls home with no bank stop); RELAY SLOW (0 lines in 2 hours of play: the "pile of messages hung up at the relay" Damian saw is not yet caught by it); and from before main 48252 the lumber sentinel, LumberDiscover and LumberTree, the bunnyhop collection, the busy auto-tune, the miner's Targeted place storage and respawn timing, and AssistStructure stage 2. The bounce is `uoaix-init.md`'s "The uo1998 relay and its client" bullet; check the PID alive 15 s after the start. Watch the newest `assist-2650-*.log` in `D:\Projects\Cobblestone-root\build-output\uoaix\assist` with a Monitor running `pwsh -File apps/uoaix/assist/watch-relay.ps1 -RelayPid <pid> -Log <log>`, re-armed at each 30-minute expiry. Damian manages blu directly (root 2026-10-21): take his asks, report landings to him. Open: the planner case "a stop test that never fires" hung past 10 s once in about 30 suite runs (0 of 30 on 2026-10-24; the case takes 0.5 to 1.4 s); no case yet walks near a locked house with the key in the pack, or through `HomeEnter`/`HomeLeave` (the door's lock read from its own lines, `DoorLocked`), or reaches the vendor catalogue's own-vendor skip (the suites hold no house walk and no player vendor); no case reaches the call sites of `ToShop`'s blocked-landing fallback, `BuyShopThread`'s thread guard, `HomespunHome`'s no-task guard or `ToBanker`'s reagent stamp (the rules themselves are graded as pure functions; the suites hold no house, shop-and-bank fixture or loadout). **NEXT:** first, Damian's live asks; then the fourth audit's open findings (above, 3b first); then the home reagent wedge (`TripReady` with no bank in the city here walks to the nearest town's bank and sellers rather than refusing; a world start that reaches it); then the bank-by-rune choice counting the recall's own cost (20:32 and 22:07: a recall spent to save 6 to 14 tiles of walking); then selling by the price rules (`tailoring-circuit.md`, "How a 1998 vendor prices"), waiting on his go-ahead; then `AssistStructure.md` stage 3's rest (the one stop across the shared helpers). Deferred until Damian raises it: the Keep thread circuit's own purse, order and trip rules (main 47926); `AssistCircuitBrain.md` (paused before stage 3, Damian 2026-10-24). | `apps/uoaix/assist/**`. Construction 35373's blu-only chapters stay off main. |
| **val** | **NOW:** handed off 2026-10-26 at 75 per cent, nothing open or shelved. The linear-`Task` change is Damian's decision: its census is in `compiler-backlog.md` COMPILER-138. | **NEXT:** the next unit root dispatches. | `apps/games/**` and `apps/landing/**` per the file claims. |
| **reek** | **NOW (handed off at 75%, nothing shelved):** GSP GPU stack (`docs/Designs/Active/OS/GspGpuStack.md`): stage 2's SM 8.9 plug path lists 11 of the 15 kernels of `GemmKernels.codex` (main 47648; `test-turingas.ps1` 28872 of 28872 words, 81 WARPSYNC checked against NAK's fields); the other 4 wait on Damian's ruling on division by a variable (the design's Open). Stage 5's host proofs gained the GSP-RM queue transport (main 47658, `NvGspQueue`, graded against tinygrad's `NVRpcQueue`); the GSP host work left is thin (`NvGspArgs` already grades the queue page table and headers). `DeviceEmulationCatalog.md` (root's unit): its queue item 1 was already built (`-usb-bot-revive-on-reset`), deleted from the queue (main 47664); item 1 now is per-controller attachment, DEFERRED by red with its size measured, and item 2 is the `-gop-stride` caveat, so the design has no takeable stage until Damian or root reopens attachment. OtaBoot `boot-load` (Track D item 18) is design-blocked: the flash length word can only be bounded by a slot size the caller supplies, `boot-load` has no production caller, and the chapter is foreword (seed-affecting), so it waits for whoever wires OTA up. Firmware stays off the depot (licence ruling). Waiting on Damian: the deployment's client half (NOT RUN); the buildable multis (45422 to 45463, NOT RUN) on his `switch -Bake`. **NEXT:** ask root for a unit at session start; with Damian's division ruling, lower `/` by a variable in the sass plug (`SassLower` `lw-binary`, refusal `operator`) and grade the 4 kernels with `codex/plugs/sass/test-turingas.ps1`. | UOAIX: doors, beds, routes, shop doors, the city guard and city law mechanism, the stablemasters, teleporters and the merchant fleet (from red, 2026-10-16). |
| **fester** | **NOW:** The Magic pool (root, 2026-10-20, standing GO): era ladder Phase 8.1, one Ice Age family per CL, each with a battery arm that goes red under ablation, the census moving exactly that card, cite-gate, and the ICE counts updated here and in Phase 8.1; every ICE instant and sorcery is now read (49 exact, 33 approx, 0 unmodelled, 2026-10-26, CoverageExport over `data/ICE.cards.psv`); next family from the 33 APPROX ICE spells, then the ICE permanents; `apps/games/magic/MagicFormatSolver.md` Phase 8.1 holds the measured work list. Built UOAIX items still wait on a trial or Damian's client grade: UOAIX-53, 76, 268, 262, 254, 253, 242 D6, 238, 232, 231, 228, 223, 219, 211, 210, 204, 188, 178, 119; UOAIX-243 waits on Damian's Q1 to Q10. Walk-map collider growth (main 46784, `walk-grow`) is graded only by `bvt/BvtWalkGrow`: no trial has overfilled a cell. | **NEXT:** the rest of Ice Age, then Alliances, Homelands, 4th and 5th; D7 waits on Damian's R6 (Vultr: region, plan and date). | reek holds doors, beds, routes, shop doors, the city guard and city law mechanism, the stablemasters, teleporters and the merchant fleet. Catalog ids 139-146 are blu's (UOAIX-174). The brambles change (43433) needs a map cache reinstall to reach a live walk map. Shelves on BigWhite_Codex_fester: 35450 and 35378 (deferred diagnostics). Shelves on BigWhite_Codex_red: 35360 (town-network goal controller, paused), 33249 (T5 single-pass proposal, deferred). |
| **red** | **NOW:** the hosted plugs' `codex/test/ops` censuses, one landing per batch with the plug's oracle at 59 values: 2.116 javascript 43/71 (the Real batch landed, main 49064; next batches are the shapes 2.115 closed for python: heap, bits, printing, equality names, patterns, bounded fields), then 2.117 C# 30/71. Python (2.115) and perl (2.111) stand at 63/71; their last 8 are 2.114 (CCE text, on C#'s run-time `_Cce` model) and two heap probes. Damian's in-game UOAIX findings come first, then UOAIX-286 (trial-49037-coroner's save refused the civic Law checkpoint, `lsc-encode`; it blocks a switch); UOAIX-275's rows 0 and 1 stall at stage 4. The census method is each plugs row's Measured line: every `codex/test/ops` subject with an `.expected` and no other sidecar, through `codex/plugs/<plug>/run.ps1 -Src <subject> -Out <file>`, run by the plug's runtime, stdout against the `.expected`. COMPILER-136 (main 48964) and COMPILER-139 (main 49030) landed; COMPILER-138's Task-linear option waits on Damian. (Damian, 2026-10-19: "send fester, val, and red on absolutely any wild goose chase investiation, bug, or whatever. just keep builds and test low and gated through you. code, docs, light bvt and specific tests only.") UOAIX server bugfixes and ongoing work (Damian via root, 2026-10-20: "use red for any bugfixes, or ongoing work on uoaix server"); live is 46874, and Damian's in-game findings come to red first. UOAIX-267: the probe searches are bounded and a clipped probe is fail-cached (`rte-plan-clip`, `BvtProbeBudget`); graded at trial-46982 (the fail-cache holds); the route-ended and goal-changed rise is one walker's loop from mid-trial, not a code change (row text); the walker is guard 2196 or 2204 trailing a moving foe (`cg-chase-step`); main 47754 drops a chase that has not closed in 10 steps (`bvt/BvtChaseGiveUp`), graded by root's trial-48654. UOAIX-275 coroners and the burial quest: design `docs/Designs/Active/Apps/UoaixCoroners.md` (Q1-Q4 ruled); stages 1 to 3 landed (main 47037, 47060, 47074) and switched on: awaiting Damian's in-game grade. Q5 ruled by root and landed (main 47445). Soak family `coroner` (`-Only coroner -Bots 4`, alone, 60 min): trial-47689 graded offer, accept, refuse and burial pass; paid unreached (the town purse could not buy the deed, paid=2, said once and owed by design). The pulse rise in that trial (p50 61 to 153 ms) was the idle tail, b and British standing 50 minutes in the graveyard keeping its spawn awake (kind 16, 2.8M wakes); main 47726 parks them in town when the family ends. An owed deed was never paid (nothing funded the purse); main 47730 funds it from the crown as every town expense is. Next: root's rerun on main 47730 or later for 275-paid; then Damian's in-game grade. UOAIX-279 (coin as g/s/c) needs its client patch installed with the same switch as its server. A game link never closes for response size (root ruling 2026-10-20, L-SCALE; from val, 2026-10-20): parts 1 (main 46839, `VIEW CATCHUP`, `bvt/BvtRingGump` arm e) and 2a (main 46843, `stx-enqueue`) landed and held in trial-46860. Part 2b landed (main 47203; val's shelf 46848 is superseded): a game link's response past its 64 KiB tx goes on into a boot-made spill (`sl-spill-bytes` 4 x 64 KiB, 8 MiB at 32 links) and refills tx as it drains (`sl-enqueue`, `stx-refill`, `sl-pending`; `bvt/BvtLinkSpill`); past tx and spill together it is still refused. Then read `VIEW CATCHUP`, `CARDS TOLD` and `SLOW RELAY` in the next graded soak, and the `-Only craft` soak for UOAIX-185. | **NEXT:** 2.114 for python, javascript and perl (C#'s `_Cce` run-time class), then the compiler and quire backlogs (COMPILER-129's rest needs toolchains this box lacks, COMPILER-116 deferred, CORE-9, CORE-11 and CORE-12 are Damian's). | `codex/compiler/**`, `codex/foreword/**`, `codex/os/**` (`codex/os/net/**` from blu), the release owner. |
| **root** | **NOW:** commander, resumed 2026-10-20. Live shard main 46874 on a fresh world (supervisor PID 21532, guest 19288, OutDir `build-output\uoaix\live-46874`, proxy 4400 on 2593 to 2600), switched on Damian's word for a smoke test; plan `TESTPLAN-46874.md`. Delete the untracked `tools\codex-vm-live44460.exe` (Remove-Item refused: access denied). Open for Damian: D2 Vultr, D3 publish, `UoaixPlayerDungeons.md` Q1-Q10. | **NEXT:** route every bug Damian reports on 46874 to red (uoaix-init.md, 2026-10-20). | |

**Plugs are reek's close-out lane** (Damian's direction, 2026-08-18): the
register in order, one entry at a time, said in status.json.
`codex/plugs/zig/**` is ordinary fleet code (Damian, 2026-08-18); credit Steve
in a CL that changes what he wrote and flag it in the next GitHubUpdate.

## The pool (Damian, 2026-08-18)

Every open design in `docs/Designs/Active/` is available work; a lane that
empties draws in order, says so in the table, and strikes the item. `DeviceEmulationCatalog.md` is demand-driven. Seed
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
`tools/agents/measure-context.ps1`; a lane's estimate is not a number. Root orders a
handoff at 75 or at a genuine boundary above it, never below, and cancels an
early one.

**A red whose only reason is the wall budget stops nothing (Damian,
2026-10-07: "the machine is loaded with work. don't let wallclock failures
stop anything"; "we didn't change that code so the test results are
meaningless").** A gate, a landing or a release carries on and names it.
Results for code the change did not touch grade nothing, so they get no solo
re-runs. Any other red (wrong output, crash, compile failure) still stops the
line.

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

**UOAIX: Damian's live bugs come first (Damian, 2026-10-08: "the fleet's goal
is to resolve all bugs I can identify faster than I can identify new bugs").**
A bug Damian reports in game preempts every stage unit in every lane: root routes
it to one lane at once, the lane fixes and lands it before resuming its stage, and
root lists every open report in the live TESTPLAN. **Feature freeze while
any reported bug is open (Damian, 2026-10-08: "absolutely no new features to
land on main until bugs I identify are fixed. no more wait a day for a bug
fix"):** only bug fixes land on main; a finished feature stays shelved; no live
switch until the queue is empty.

**UOAIX: code only, straight to main (Damian, 2026-10-08: "no agents are allowed to run builds or tests. only code. all code gets put directly onto main, no workstreams. all code will be tested after all the code for both lanes is written"; "there will be no ceremony, only coding").** A lane writes code in its `<lane>_main` client and submits to main with no compile, BVT, boot or test; it syncs and resolves before each submit. Root compiles and runs the BVT once, after every lane's code is written. This overrides the compile-at-merged-head and BVT-before-landing rules below for UOAIX.

**UOAIX: no proofs; Damian's in-game test is the proof (Damian, 2026-10-07: "ok
no more proof", "stop all that", "the proof is me testing it in game", "if the
agents aren't confident after writing it, tell them to re-read it and lets get
a new build up within the next 5 minutes of every lane's outstanding changes";
"if these agents cant be more confident of these small changes without 20-30
minute test sweeps we have lost the handle entirely").** A UOAIX change is
re-read until the author is confident, compiled with CompositeGameServer AT THE
MERGED HEAD with 0 errors, and only then landed (Damian, 2026-10-08: "never
push to main before they confirm it builds"; "skipping ceremony is not
skipping CI tests to prevent fleet blockage"). A change to a save codec also boots
its candidate once on a scratch world and reads WORLD RESTORED after one restart
before landing: a server that refuses its own save blocks every lane's switch. Apart from the UOAIX BVT below: no replay, no soak, no citer sweep. While
Damian tests, every lane finishes every item in its lane in one pass and lands
it; then root makes one build and one server restart (Damian, 2026-10-07:
"everyone should work to finish every single item in their lane in a single
pass on the code, get it on main, and lets have one single build and one single
re-run of the server after that. no more of this chasing flies with cannons").

**UOAIX: every item from harvest, transform, consume (Damian, 2026-10-08: "any
feature that exists now must be reconciled against those rules, and any future
feature must ship with all that complete").** The rules are in
`apps/uoaix/CompositeGame.md`: no item from nothing, every item made by an NPC trade
from harvested or transformed inputs, every harvested input gathered by an NPC.
A UOAIX feature is done only when its items' whole chain (gatherer, transform,
consumer) exists and runs, and every admin function the feature needs is on the admin page and works (Damian, 2026-10-08: "acceptance criteria for a feature should also include any necessary admin functions added and working to the website"); val's audit drives the reconciliation of what exists.

**UOAIX BVT (Damian, 2026-10-08: "i want a bvt test that must be run, and added
to for each bugfix or new feature that does both positive and negative unit
testing: fast, small, targeted").** One suite, `apps/uoaix/bvt/`, run by one
command, `apps/uoaix/bvt.ps1`, in one guest, under 2 minutes in total. Every bug
fix and every feature adds, in the same CL, at least one positive case (the
intended behaviour happens) and one negative case (the bad input, state or old
defect is refused or absent), each a direct call into the changed function with
no world boot where possible. The whole BVT runs green at the merged head before
every UOAIX landing. Harness owner: fester. The BVT is a smoke test, not a
coverage suite (Damian, the same day: "bvt does not smoke out every single
possible bug, but is a lightweight smoke test to ensure builds don't break
easily"; "e.g. is it testable is the goal of the bvt"): the BVT answers one question,
can Damian test this build. Its base cases are the server compiles, boots, restores
its own save after a restart and admits a login; each CL's case grades the one
thing that CL changed, in milliseconds. A CL that adds behaviour names, in its
description, the live call site that runs it in the booted server (L-UNCALLED).
Run `pwsh -File apps/uoaix/bvt.ps1`
(it prints PASS/FAIL per case and `UOAIX BVT PASS`); add a case as a chapter in
`apps/uoaix/bvt/` defining `bvt-<name> : Integer -> [Console] Integer` that
returns its failure count, with `bvt-check` from `BvtCheck` per assertion
(`BvtLaunch.codex` is the pattern). It boots in TESTING mode, as the live shard does, in 41 s (2026-10-08).

**UOAIX live switch (Damian, 2026-10-08).** Root freezes a release candidate: root's stream is merged down to one main CL and no further; a defect the trial finds is fixed on main by a lane and only that CL is merged into root ("have fleet agents sync up to your local workstream and quickfix things so you can get the server up without merging down changes from other agent's future feature work increasing risk"). Before the switch the candidate passes the BVT and one game day on a fresh world at 30x (every worker leaves the inn, routes and produces, no SAVE SECTION FAILED, no !EXC); dispatch is FIFO and a reported bug preempts features; both test clients carry identical data files.

**No save durability (Damian, 2026-10-07: "we don't need save durability its
wasted code and testing"):** no old-version readers, no compatibility arms,
no durability tests; a format change boots a fresh world.

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
