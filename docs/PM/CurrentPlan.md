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
| **blu** | **NOW:** handed off 2026-10-09; UOAIX-216 moved to fester (root). Was: UOAIX-216 slow tick: `TICK SLOW` (main 43208) put every slow tick of root's trial at 43215 (`D:\Projects\Cobblestone-root\build-output\uoaix\trial-43215\run\server.log`) in the world step, the timer wheel's drain (`cg-tick`, 255 to 280 ms); the next unit is timing `cg-timers` by timer kind and cutting the kind it names (UOAIX-216 row, main 43254). UOAIX-196 commit encode: waits on root's live log past 5,000 economy events with `WORLD ENCODE DETAIL` (live 43176 carries it); byte loops landed (43002), BvtByteLoops and the restart arm gate them. | **NEXT:** UOAIX-222 city taverns' keeper leisure once city keepers walk home (keepers wander only inside shops, reek 43038); workers of every city already go out (43095). Awaiting root's grade: UOAIX-222 L1-L3 and workers (42896, 42907, 42945, 43077, 43095; fresh world), UOAIX-216 flight (42862). | `codex/os/net/**`. Construction 35373's blu-only chapters stay off main. |
| **val** | **NOW:** (root, 2026-10-10, for val's next session; red takes it if val is not relaunched first) FIRST Damian's bug 2026-10-10 on 43363 ("i killed a brigand, and i saw \"m_brigand\" flash on screen, and a naked, null hued brigand is attacking me now"): `WorldSpawnData.codex:148` profile `m_brigand` has name "m_brigand", body 400, hue 0, so `import-world-spawns.ps1` left the UOX3 name list, skin hue and outfit unresolved; fix the importer for every human spawn profile, wild AND town (Damian 2026-10-10: Moonglow's "m_thiefguildmaster" is the only broken one he found there), every profile whose name is still an m_ or f_ template key; regenerate, check the Moonglow thief guildmaster and check a brigand spawns named, hued and dressed. With it Damian's "a brigand was attacking me, and when I tried to attack back it said it would drop noteriety": a hostile spawn carries its UOX3 notoriety (brigands are murderers, red), and attacking a creature that attacked you is never a crime (aggressor rule; `Law.codex` is red's, so coordinate that half with red). And Damian's "spectres are all black, not properly ghostly": the import's hue for spectres (and every translucent UOX3 hue) reaches the client as solid black; fix with the hue class. Then Damian's order 2026-10-10 on live 43363: "there should be a very dense deer and sheep (leather) spawn here on this penninsula", standing at 2080,2661 z 10, the peninsula east of Trinsic's window (x 1811-2075, y 2643-2912). A themed wild region (UOAIX-218 machinery, val's 43074) bounded by that peninsula's land, deer and sheep, dense well past the 2.4 a thousand tiles of the other themes; name its soak family. Then Damian's order 2026-10-10: "the decaying building i am at here is the home of a liche spawn", British at 845,1547 z 0: a lich spawn home bounded by that ruin, its liches staying inside (UOAIX-150 leash). Then Damian's order 2026-10-10: "and we need orc lords and orc mages at the nearby orc camp": orc lords and orc mages added to the orc camp a little west and north of 845,1547 (Damian: "just a little west and to the north"; find the camp's UOX3 spawn region). Then Damian's order 2026-10-10: "the labyrinth i am in now should have 2 daemon spawn in it", British at 1141,2236 z 60: two daemon spawn homes inside that labyrinth, leashed to it. Then Damian's order 2026-10-10: "graveyards like this one in nujelm, there is one outside vesper and one on moonglow, at least, all need skeleton spawns like we got north of britforge" (British at 3516,1141 z 20): every graveyard in the land gets the undead spawn of the graveyard north of Britain, Nujel'm's, Vesper's and Moonglow's at least; find the rest by their gravestone statics. Then Damian's order 2026-10-10: "we do need scorpion spawn outside the city on the island here" (British at 3742,1307 z 20 on Nujel'm): scorpion spawns on Nujel'm's island outside its city window (x 3544-3773, y 1166-1400). The UOAIX-232 unit gate is green (unit failures=0, 241, at main 43151). Landed 2026-10-09, NOT RUN in game unless said: UOAIX-217 Moonglow, Yew and Wind shops (42893) with soak families moonglow, yew, wind (43031); UOAIX-225 price labels (42910); UOAIX-191 seal archive buffer below the mark (42948, LOANS SEAL log for the trial); UOAIX-230 game gumps, ring rows 16 KiB, chess troughs, card watchers, craft gump frame (42966, 42985, 42995); BVT unit green at 43057; UOAIX-218 wild themes at 2.4 a thousand tiles (43074); UOAIX-228 bookcase library slice 1 (43098). | **NEXT:** UOAIX-217 workers for Moonglow (city 2), Yew (3) and Wind (4), root GO 2026-10-09: the three surveys are in the UOAIX-217 row (main 43222-43224); land one city at a time as a `cw-city` branch in `apps/uoaix/CityWorkers.codex`, merging down right before each edit, on the pattern of `cw-skara` (city 5): `CgCity` houses (rooms behind unsigned doors, UOX3 `felucca_doors.jsdata` with no `felucca_signs.jsdata` sign within 4 tiles, floors bounded by the 1.25 statics' walls), woods and gardens (the 8 x 8 blocks of the city window holding the most trees and wild plants in STATICS0), ore and rock (mountain land, none on an island), field, pasture, shore, the inn and its door (`cw-door`), stations at the city's shop counters (`Cities.codex` shop rows), a `place-base`, and its vendors (`cw-vendors`). Wind is underground: no woods, field or shore expected. Then root's wild soak on 43074 and the LOANS SEAL lines from the next trial; UOAIX-117 stage G, held for Damian's next fresh world. |
| **reek** | **NOW:** handed off 2026-10-09 at 73% context, clean (nothing open or shelved). Landed today, graded by own bvt cases, NOT RUN in game: UOAIX-217 workers for Vesper (43229), Minoc (43238), Cove (43243), Moonglow (43270), Yew (43277), Wind (43279), Buccaneer's Den (43287; Nujel'm surveyed, none); UOAIX-150 a full region's creatures spill at most 8 tiles from home (43304); UOAIX-234 Britain lumberjack in the forest (43135; bvt.ps1 check 43193); UOAIX-218 spawn retry and UOX3 CALL breeding (43080, 43159); UOAIX-143 path 2 (43089); UOAIX-211 fizzle (43184); UOAIX-233 keepers wander (43038, 43053). | **NEXT:** root FIFO (the Wind herb farmer is fester's NEXT; reek's lead is in the UOAIX-217 row). Open: UOAIX-234 part 4 (forest route windows per city); guard coverage at Britain's north (UOAIX-150 row). |  Plugs close-out lane and codex-vm GPU bridge; `tools/codex-vm.c`. Tokenless `codex/foreword/` landings name their closure check. |
| **fester** | **NOW:** (root, 2026-10-10) UOAIX-236 player housing in the scenery houses, Damian's whole order to its end state (backlog row: signs on every unused house, bids into escrow, GM grant or reject with refunds, ownership with runes and earlier runes deleted, saves, and the admin website surface); stage it, land each stage. Landed, NOT RUN (fresh world and decoration re-import): census, signs, bids into escrow, `[houses`/`[house grant|reject N`, refunds, the save, runes. Next: the admin website (item 6). **NEXT:** UOAIX-238 Nujel'm's powders of restoration, Damian's whole order to its end state in the backlog row; then UOAIX-216 13.45 `cv-advance` (red's split on trial 43383: up to 27.6 s guest per slow tick). **Standing:** UOAIX-49 C long tail; UOAIX-141 waits on a `MAPCACHE INVALID` line. |
| **red** | **NOW:** FIRST once root says the live switch to 43454 passed (Damian, 2026-10-10: "lets go ahead and once we get to a good state, do a public push to preview branch, and update the prism site"): `build/push-preview.ps1` (dry run, then `-Push`) and the whole cobblestoneproject.com site per `docs/Agents/PublicPush.md` "The website mirror" steps 1-6, red the named owner, the landing page gaining the Ultima (UOAIX) work (Damian, same day: "not just prism but the whole cobblestoneproject.com website to include the ultima stuff, with a screenshot of the admin tool and a cut down tree as the headline pic, set to the right with text flowing down under it"): a UOAIX section in `apps/landing/LandingPage.codex` with the felled-tree screenshot floated right and the text flowing beside and under it, plus a screenshot of the admin page (headless browser on the live admin port); root supplies the in-game tree screenshot from a client run; then resume UOAIX-237 F1b. Budget (Damian same day: "we have low budget of token remaining for a few days, so we need to start stepping lightly with purpose"): no runs beyond what a step needs. (root, 2026-10-10; done on main today: healers and taverns never close 43390, closed-keeper greeting 43392, stablemasters 43398/43409, dungeon entrances 43411, anvil smithing 43418, longsword wear 43422; UOAIX-216 keepers 12/15 fixed by fester's 43362) Damian's ruling 2026-10-10 ("brambles, small rocks, and saplings should not block movement"): those statics stop blocking in the walk map (map cache install, `install-map-cache.ps1`, and any server collider), for players and NPC routes alike; then UOAIX-150 guard coverage of Britain's north streets (the nearest guard stood 54 to 61 tiles from the elemental at 1474,1600). Landed 2026-10-09, NOT RUN in game: UOAIX-224 shop door note, MGM2 restore (42935); UOAIX-229 shopkeepers sell, banked tool message, per-vendor closed flags (42950); vendor restore lot heads (42969); city founding past a no-clear-tile shop (42976); catalog BVT pins (42980); UOAIX-217 city guards (42988), city shop hours and counter restock (43005), Nujel'm, Serpent's Hold and Jhelom guards (43008, 43015), city crimes with a jail per guarded city (43092; its bvt took 301 s beside root's soak-43060 and the live shard; 13 runs of the same code took 74-123 s, bound kept at 120); UOAIX-235 city tree misdemeanor for players (43116); the law's guard table grows past 32 (43128); UOAIX-235 NPC half (43146).; then UOAIX-237 the merchant fleet, Damian's whole order to its end state in the backlog row; Damian rejected Nujel'm as home port ("it has to be some kind of tangible resource, something only found there"); Nujel'm's resource is its powders of restoration (UOAIX-238), which the fleet carries | **NEXT:** none; root assigns. Head BVT red: "a worker leaves the inn ... reaches its node". Every worker jams at the inn door (1495-1496,1617-1619) by the 45 s check; until reek's 43135 only the lumberjack's in-town tree (1474,1617) gave a leg 2 (reek, read from root's bvt-43060/42998). Doors and routes are red's: the inn door jam is red's to fix unless root routes it elsewhere. | Red holds doors, beds, routes, shop doors, the city guard and city law mechanism. Catalog ids 139-146 are blu's (UOAIX-174). Shelves: 39506 (COMPILER-109 batch, seed-affecting: needs stage 2 == 3, BVT, cite-gate, signed self-verified seed, token; NOT RUN), 35360 (town-network goal controller, paused), 33249 (T5 single-pass proposal, deferred). |
| **root** | **NOW:** live UOAIX shard main 43454 on a FRESH world `build-output\uoaix\fresh-43454\world.disk` since 2026-10-10 ~03:00 local (supervisor `live-43454` PID 21148, guest 5084; server 2600 behind the packet proxy on 2593; admin 2594, key `live-43454\run-1\admin-owner.key`), plan `TESTPLAN-43454.md`, regression to 43454 (rows 1-64). Trial 43454: hour 49, 45 workers made > 0, 0 !EXC, 0 save fails; BVT wall 240 s (load). Every Damian bug on 43363 fixed on this build. Next switch carries UOAIX-236 housing (fresh world plus decoration re-import, the uoaix-init "Build and run" line). Red pushes the preview branch and the whole site; the felled-tree screenshot is owed to red (Damian or a root client run). Low token budget (Damian 2026-10-10): pulse 3600 s, event-only. | **NEXT:** the switch recipe: candidate.ps1 at head (read the trial log, stop the trial guest by hand); bake-decor.ps1 against the live admin port; stop the live supervisor through its `stop` file; merge `tools/codex-vm.exe`; `switch-live.ps1 -Fresh -Artifact bvt-<h>\server.cdx -World fresh-<h>\world.disk -OutDir live-<h> -Port 2600 -ClientRoot C:/Users/Damian/uo1998-client -DecorationFile %LOCALAPPDATA%\uoaix-bvt\britannia.dwd -FloraFile %LOCALAPPDATA%\uoaix-bvt\flora\flora.flr -PlayerClients D:\Projects\uoaix-client-flora,D:\Projects\uoaix-client2` (no `-Running`; `candidate.ps1` is an untracked root script in `build-output\uoaix`); patch clients only if the bake exported anything (bake dir outside the repo). Damian's desktop links: "UOAIX Client 1 (UOAssist)", "UOAIX Client 2" (CLIENT2.EXE). | |

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
