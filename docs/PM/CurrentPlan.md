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

Latest public release: **Update 72** (2026-10-26, main 49091; `GitHubUpdate72.md`); Update 73 covers main after it. Re-measure the
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

- **The MCP service is key tech (Damian, 2026-10-31):** "the mcp is important and key tech"; "anything that looks helpful to add to the mcp, please do. that has been cool feature and i'd even like to get a clarifier skill". Campaign (red): a `codex_clarify` tool on `codex/os/core/Clarifier.codex` (design `docs/Designs/Done/Language/Clarifier.md`), then a `/clarify` skill that calls it, then the `apps/accp/accp-backlog.md` rows. The live server runs the bundle `D:/Projects/Cobblestone-root-main/apps/accp/build-output/standalone-52813` with root-main's `apps/accp` scripts at head (`.claude.json`); a new bundle is copied there beside it, hash-checked, and the `-Bundle` path repointed, after `mcp-test` passes on its bytes; Damian then reconnects `/mcp`.

- **No new toolchains (Damian, 2026-10-31, asked about gfortran):** "i wish i could say yes, but we have to streamline our profile here, not expand it, until more help is offered with cost and driving fleets, sadly." No toolchain is installed for a plug; every row waiting on one (COMPILER-129, the Fortran `merge()` hoist, php, ruby, lua) is Deferred.

- **Releases and the cross outputs (Damian, 2026-10-31):** "We should not gate a x86 good release on plugs, or other outputs than .cdx or .codex from the compiler itself. any deficiencies in riscv or arm or wasm are secondary. do not put that in the release at all, even to report. it is a seperate processes when we have customers on those architectures that care and want to pay for the tokens." No release phase builds, runs or reports a plug or cross output, with one exception: the DDC step stays, and with it the csharp plug it needs (Damian, same day: "yes the ddc stays, and so the c# plug right?"). Cross defects wait in `plugs-backlog.md` 2.122.

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
  item 18, `OtaBoot` `boot-run` (reek): its flash length is bounded by the caller's slot size; no production caller until a board boot path exists.
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
- **Pool: projects for the uncited chapters** (Damian, 2026-10-31: "they all sound
  good. add them to the plan going forward and do these instead of Magic cards when
  other issues are less pressing"). A lane with no more pressing unit takes the first
  untaken row, marks `(taken: <lane>)` here on main first, and wires the chapter to a
  caller with a test. In focus first: (3) `foreword` SessionTypes: done (main 51182, 51198): `Session p a`, `SOffer`, `Offered` and `Answered` in SessionTypes; `DtlsSession` (client, and a server session only past a verified cookie) and `TlsSession` (TLS 1.3 client and server over `TlsEndpoint`) in Encode; an out-of-order step or a second HelloRetryRequest fails with CDX2001 (`codex/test/errors/dtls-session-*`, `tls-session-*`); sessions are linear, so a reused session fails with CDX2061 (main 51222, `codex/test/errors/session-reuse`); a linear field bound by a `when` pattern is tracked too (COMPILER-151, seed 996B53E4, main 51247); the test-only builtin `SChan` is retired (COMPILER-152, seed 875F2CEB, main 51336); (5) `Kernel` Ivshmem: done (codex-vm `-ivshmem`; `ivshmem-live` and `ivshmem-channel` run through `.vm-guests`);
  (6) `Kernel` Mouse: the i8042 aux driver, codex-vm's aux model and `codex/test/ps2-mouse` are built (GlobeDemo reads it), QEMU's i8042 grades the handshake and packet delivery (`ps2-mouse-qemu`, its pointer injected through the QEMU monitor by `build/test-qemu-x86.ps1`), and the desk falls back to it with no USB mouse (main 51187, `codex/test/apps/desk-ps2-pointer`). Then: (8) `Net` GossipProtocol, DistributedConfig, ServiceProxy: the chapters' defects are fixed (main 51205; `codex/test/apps/` gossip-protocol, gossip-dissemination, distributed-config, distributed-config-ttl, service-proxy); open: a multi-shard UOAIX cluster (gossip membership, shared config, one proxy front door) (red, after (3));
  (9) `gpu` Atomic (taken: fester): GPU atomics callable from a kernel and graded on the GPU (main 51240: `ReduceKernels` histogram, `codex/test/gpu-histogram`), MatchedNoise's histogram on it (main 51253, `codex/test/apps/diffusion-matched-histogram`), and its sort on a GPU bitonic sort (main 51265); done (the softmax max was dropped: every PTX softmax row is one block's tree reduction, with no cross-block step for an atomic); (11)
  `Dev` ScreenRecorder (taken: fester): recordings of GUI test runs; built: codex-vm `-record DIR` (port 0xE5, capped) and ScreenRecorder's `sr-capture` (main 51312, `codex/test/apps/screen-recorder-*`) and `-record-every-ms`, which records any run on codex-vm's own clock (main 51322, `screen-recorder-timed`); done;
  The six effect-only foreword chapters stay. Magic stays cut.

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
| **blu** | **NOW, standing GO:** x86 kernel, compiler and foreword refusal gaps, each verified at head, landed, then the next (root 2026-10-31); a census or audit names its subject in `ExaminersAssay.md` or `DeviceEmulationCatalog.md`. **Resting (2026-11-01, handoff at 70% on root's order):** no Perforce work open except shelf CL 52993 (the rung 4 WASM interpreter `apps/accp/AccpRun.codex` and its probe, compiled and timed only, in no test or gate); the Assist items below are unchanged. Main seed 4B5449A8 (main 53177). HumanApprovalGrant rung 4: Damian ruled route A (section 8, 2026-11-01), then its stage 0 FAILED the bound (S 208 to 311 against 10, section 8 "Stage 0 result"), which by that section sends the choice back to him; root carried it, and his answer is not yet recorded. The options: route B (the `wasmtime` C API embedding, section 8), a wasm-to-x86-64 compiler in the conduit, or a runner over the IR. If he keeps route A, unshelve 52993 and continue from section 8's route A list. COMPILER-160's build waits until after the release (red); its row holds the reading. gpu-linear-wmma is red on the seed before 53072 too (val bisects). First action next session: ask root for the next unit. `TimeZones.md` Open holds one row, the `HumanApprovalGrant` zone, which waits on the same rung 4. COMPILER-158's remaining plugs are plugs-backlog 2.127 (parked by 2.122). The Assist relay (PID from the 2650 listener, started 2026-10-31 05:58 local, log `assist-2650-20261031-055808.log`) runs blu 50445; the settings' per-character keys were moved at that bounce (Kwik 02BE4F79; Apples 04BF2460 the mining toolbox, its spot and Store to; `settings-2650.txt.bak-personal` the file before). A landing is one `cover-assist.ps1 -Measure <touched files>` run (50409). **test-assist is red at its default 10 s world-case guard** (homespun towns "telescope in Twotown" and "the house rune's landing always blocked" take 8 to 9 s run after any other case, 3.6 s alone, 2026-10-30): the relay learns the back pad's jump one tile short (73,73 for 72,72; `AssistState` takes where the player stood when the pending diagonal step does not match `Facing`), and `ToOverflow`'s walk then plans to the pad 80 times, 400,000 states each, as a plan can never end on a known teleporter. Unfixed; live, the same could learn a pad one tile off. Not yet seen live: 50443 (hidden told from invisible, and the magery loops say "." before a cast at a hidden or invisible player, a "Target can not be seen" with no flag known said "." and cast once more), 50440 (the magery loops play the pack's instrument while a cast waits for its pace or for mana), 50438 (the magery loops: a cast refused for reagents refills the reagent bag from the set storage to the profile's amounts and casts once more; stops with the bags unset or out of view, a refill failed or giving none, or refused again with no cast between), 50419 (Provocation's answer off the shared timing), 50420 (the trainer's walk 3 times the tiles between), 50421 (regions reread when written), 50417 (the trainer's fence is a wall static only: a palm on the line let a bull outside the yard be provoked), 50414 (Provocation combat until the two fight, its gone creatures never targeted and the last-targets box unticked; Provocation trainer, farm animals with a fence between, new each round), 50411 (the Provocation loop: round zero's two creatures swapped each round, the second cursor the loop's, a failed check ending only its round), 50406 (the reagent loadout's four profiles, the pick per character, Light Mage unpicked), 50404 (bags, boxes and targeted runes per character; the live settings' server-wide values to be moved at the bounce, while the relay is stopped, by Damian's word on the split; the reagent bags one cursor each, Refill never targeting), 50402 (Go in the cave standing in pay dirt digs there, not the doorway chunk first), 50400 (Suggest in the cave miner: each chunk, the doorway chunk, the cave walk, a stand walk and the collection wait for Do it or Skip; Suggest shown only for tailoring and cave mining), 50398 (the Map tab under a ceiling reads a tile with nothing below it open: mountain, the castle, raised ground no longer black), 50395 (an instrument's sound answers its play: the Musicianship loop at the 1.1 s action gap, not 2.4 s), 50393 (a loop's use waits for the pickup's pending lifts, never the reverse, 3 s at most with no lift), 50390 (the Map tab marks every resource pile seen, reagents included), 50358 (a sale to a vendor no longer read as its restock: Watch me sell to Watson logged "Watson restocked" each sale; the false lines stay in `restocks-<server>.txt`), 50338 (the Map tab under a ceiling: in a cave the cave floor, not the mountain above), 50328 (the Musicianship loop: the pack's instrument played round after round), 50326 (the Combat tab: a stand-up melee simulator by the 1998 rules, loadout by slot, opponent from the 1998 templates, Compare list and Compare words), 50322 (the Furniture tab: the pieces in view, a way button turning the picked one from a free tile it picks), 50316 (the smelting loop's whole-pile rounds are its smelts, the smallest ore two a round), 50314 (the tailoring loop named Tailoring, its settings carried over), 50313 (the General settings' Colors tab: the state colours, Default and three colour-blind presets), 50312 (the Actions and Circuits tab rings, the tab order, Shops' one Find row, Private House Security, the Status name coloured by the vitals), 50311 (the closed Loops header says what runs; a tailor shop keeper with unread stock "tailor or weaver"), 50310 (the vendor catalogue clicks again after a circuit has stopped; no price read is "not read"; transportation), 50307 (the Circuits tab: the resets on the circuit row, Training removed, one tailoring item picked from TailorItems with its least price in place of the sell list and the sewing menus), 50306 (the Activity report's history by session and sentiment, its You line; the "on last targets" box; the weave's green border; one-row Watch me sell and Bunny hop; the pickup list sized to its items; then tailor removed), 50241 (the Skills tab's attributes table 88 px, no scroll bar), 50206 (Arm and disarm on the Hotkeys tab; the Target last target hotkey, no key set, its button on the Hotkeys tab: the player's last target into the cursor the client shows, the client's own later answer to that cursor kept from the server), 50217 (a sewing kit on the floor used where it lies), 50228 (the Skills tab's attributes table: shown, base, item modifiers from StatMod and spell or potion buffs from the six stat sounds (StatBuffs), the base's change since the session's first read; BaseStat takes a known spell buff out), 50200 (a closed header's summary one short line, Chat and events' without its time), 50194 (each map's "Own window"), 50196 (no drop walk deeper, no phantom wild ore, the struck chunks' flash), 50197 (the Info tab's Activity report, PersonKinds), 50190 (the cave miner: the whole-cave two-shovel plan, the "Mine: one shovel: <why>" warning, collect only when the cave is dug out with Collect at removed, ore flows only toward Ore to, wild ore collected, Ore to and the tools on the Circuits map, ore put down before a walk deeper, the walk to the toolbox carrying the piles beside it), 50192 (the window: drag to reorder each tab's sections, no header on one-section tabs, the Agent tab with Record me, Mill, spin and weave in the Loops menu, All Timings and Delays in the gear menu, the page repainted after every fit), 50181 (ore too small to smelt stays in the pack, never carried to the trash; no forge walk for the seed alone), 50184 (every Timings... button inside its status light or its section header), 50185 (a Hotkeys tab holds every action's key; the panels lose their Key buttons), 50168, 50169, 50173 (Damian's rulings of 2026-10-27), 50174 (UserBags), 50176, the window half of 50177 (the Circuit job's run row with Suggest, the log expando, Timings on the light row). Seen working live 2026-10-29 17:20: 50321's face (a chest turned to its east-west art and back by the control port). Seen working live 2026-10-28 evening: 50188's "Tailoring (bolts, then cloth)" loop, 80-round runs, each worn kit a real deletion. Seen working live 2026-10-27 19:27 to 19:41: 50177's combines within 3 tiles (no "too far away") and 50178's ore at feet (small ore only on the ground, the seed kept). Seen working live 2026-10-27: the pad walk home (49786), blocks for their attempt only (50165), homespun road count 1 kit (50166), the loadout reset at the bank (50167), the doorway chunk dug first (50170), a refused pile left and the collection going on (50175). **NEXT:** with his go (it touches every walk's teleporters), the Assist jump learned one tile short. Then the other shared concepts the 2026-10-27 audit found written per site: one hold type for every pause and rest (Reinitialize clears 5 of about 15, local and UTC clocks mixed), one per-server record file class (a bad line drops the rest of `underfloor` and `shops`), one bank stop (eight shapes, four purse targets), one stop for shared waits (`TailorResult`, `Watch` test the loops' `stop`, not a circuit's); then his decision whether a circuit start keeps the sell-order rests (it clears them; a sold-down tailor is asked again at once, a wasted recall each: Jannelle 05:05); then the ramp alerts: a forecast after a dry check can land past the ramp deadline the circuit keeps, and the alert never shows the deadline (his call whether alerts are due no later than it); then the vendor-state priority switch (tailoring-circuit.md Decisions, 2026-10-27: tailors refusing shirts while thread vendors produce make the ramps the priority; not built); then the 2026-10-27 audit's gaps: a non-homespun pack full of thread with no pile set is never woven, the miner's shovel is used again after a dig with no answer. His calls open: two-shovel mining's two digs in flight against rule 3; whether Keep thread goes back to the top. Open: HomeCave's 2461,59 refuses every ore drop (0x28) even with no pile on it (2026-10-26 18:58, 2026-10-27 16:06; cause unread); the house door's 14-tile sweep runs at every trip home; the purse top-up from a box pile is unhandled in the homespun towns world. Deferred until Damian raises it: Keep thread's purse, order and trip rules; `AssistCircuitBrain.md` (paused). | `apps/uoaix/assist/**`. Construction 35373 (submitted on the blu stream, not shelved) keeps its blu-only chapters off main; a copy-up goes by path. |
| **val** | **NOW:** handed off at 68% measured on root's order (2026-11-01); nothing open, nothing in flight; the parked Magic census shelf CL 50158 is the only pending CL. | **NEXT:** AccpInformationFlow rung 2, declassifiers (`docs/Designs/Active/Compiler/AccpInformationFlow.md` section 10): build it in both implementations (`AccpFlowIr.codex` val, `AccpFlowCheck.codex` blu) to the section 10.6 corpus once root carries D1 to D4 (section 10.7) back ruled; until then answer blu's design questions only. Root's UOAIX trial lines wait until root resumes UOAIX. | `apps/games/**` and `apps/landing/**` per the file claims. |
| **reek** | **NOW:** resting (handed off at 75 measured, 2026-11-01): nothing open or shelved, no run in flight. Landed today: AccpRequesterAuth rung B (main 52967), AccpDecidablePolicy rung 3 (53019), CORE-17 (53050), net send framing (53065), growable TCP receive buffer (53093), plain fetch off its 8 MiB reservation (53120), NIC fetch witness `build/http-fetch-nic-test.ps1` (53157), `http-encode` 18,984 -> 11,472 B (53161), `http-parse-response` 26,856 -> 10,848 B (53170). | **NEXT:** ask root for the next unit. Open and homed: CORE-23 (the POST body decode in `CCE.codex`, seed-affecting) and UOAIX-289 (ShardLinks arena sets, cluster work). Open elsewhere: TorchZip storage bound (`codex/foreword/ai/ai-backlog.md`, waits on Spark). | UOAIX: doors, beds, routes, shop doors, the city guard and city law mechanism, the stablemasters, teleporters and the merchant fleet (from red, 2026-10-16). |
| **fester** | **NOW:** handed off (2026-11-01, 72% measured); nothing open or shelved. CORE-18, -19 and -21 closed (2026-11-01): DTLS and TLS records sealed and opened under a heap mark, per-session buffers drained, an MQTT or CoAP message one payload copy each way. Root standing GO: TLS/DTLS and parser refusal gaps, one landing per unit, each with a head arm. | **NEXT:** the HTTP heap census (root GO), measured 2026-11-01 on seed 78E6A5F3B1C71075 with fixtures built before each window: per request, `http-parse-request` (`apps/works/Http.codex`) 446 bytes for a GET and 17,739 for a POST with a 1,000-byte body, `http-encode` 18,984 for a 1,000-byte response, and `http-parse-response` (`codex/os/net/HttpClient.codex`) 26,856 for one, about three copies; the probe is `build-output/fester-http/probe.codex`. Root (2026-11-01): start at WebServer and HostedServeTls and leave HttpFetch's receive alone, reek's CORE-22 (main 53120; read that row first), which `http-parse-response` serves. Per connection: `HostedServeTls` answers one request and closes (`hst-respond`), so it holds no buffer across requests; the WebServer request path (`codex/os/net/WebServer.codex`, `wb-slice` of the pending buffer before `http-parse-request`) is not yet measured; `codex/os/net/**` is under blu's file claim, so agree a turn with blu before editing WebServer. Then the next refusal gap verified at head. `codex/foreword/core/Tls.codex` is outside the compiler's cite closure, so a change there is not seed-affecting. A guest without RDRAND is `codex-vm -no-rdrand`. | reek holds doors, beds, routes, shop doors, the city guard and city law mechanism, the stablemasters, teleporters and the merchant fleet. Catalog ids 139-146 are blu's (UOAIX-174). The brambles change (43433) needs a map cache reinstall to reach a live walk map. Shelves on BigWhite_Codex_fester: 35450 and 35378 (deferred diagnostics). Shelves on BigWhite_Codex_red: 35360 (town-network goal controller, paused), 33249 (T5 single-pass proposal, deferred). |
| **red** | **NOW:** holding context for the release, which red runs on root's go once blu, fester, reek and val have handed off. Parked shelves 45053, 35360, 33249 are older parked work. R-PROSE: red's share `codex/foreword/encode` is done; the top-level `codex/test` chapters are done except `prose-*` and `net-recv-heap`, left on purpose. A prose batch outside the compiler unit (compiler + Foreword core + Math, `build/concat-codex-self.ps1`) is not seed-affecting and needs only `build/cite-gate.ps1 -FilesFile <changed>`; inside it, the signed stage 2 must be whole-file equal to the depot seed (two `-Repl -MemMB 3072` compiles, `build/sign-seed.ps1` on a copy, hash against `p4 print` of `//Codex/main/seed/Codex.cdx`); `check-builtin-alloc` digests a pinned body with its trailing prose, so a prose edit after one needs a code-unchanged check and a re-pin. | **NEXT:** the release; then COMPILER-160 (only self tail calls jump), left until after it. | `codex/compiler/**`, `codex/foreword/**`, `codex/os/**` (`codex/os/net/**` from blu), the release owner. |
| **root** | **NOW:** commander. Focus (Damian, 2026-10-31): compiler, forewords, kernel, TCP/crypto, and the MCP service ("the mcp is important and key tech"); no plug or cross work, no new toolchains, Magic cut (standing rulings above). Lanes at 21:30: blu 62% (boot RAM-cell refusal landed, main 52028, seed B1342E3B; standing GO), fester 74% (TLS/DTLS/X509 refusals through main 52023; hands off past 75), red 61% (ACCP campaign through main 51982, one lexical row left; reported to Damian after 2 early-stop nudges), reek and val handed off at 75 (relaunch is Damian's). Live shard main 50382 on a fresh world (supervisor 36740, guest 15876, port 2600 behind the 2593 proxy, `build-output\uoaix\live-50382`). Live MCP bundle `D:/Projects/Cobblestone-root-main/apps/accp/build-output/standalone-52813`; Damian reconnects `/mcp` to load it. Root's dev `tools/codex-vm.exe` is held at an old revision because the live shard guest locks it: revert it out of every merge-down until the next shard restart. | **NEXT:** dispatch each lane's next unit on its own pick in focus; deploy each new ACCP bundle by the recipe in the MCP ruling; relaunch-ready lanes start from their rows. | |

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
| `apps/accp/accp-backlog.md` information-flow and decidable-policy rows (Nectry section 5 items 1 and 3) and their designs, `docs/Designs/Active/Compiler/AccpInformationFlow.md`, `AccpDecidablePolicy.md`; `codex/os/trust/PolicyDecide.codex` | val (root's GO, 2026-11-01); information-flow rung 0 is blu's |
| `docs/Designs/Active/Security/HumanApprovalGrant.md`, the design (Nectry section 5 item 2) | reek owns the design and answers val's questions on it; val builds its rungs (root, 2026-11-01) |

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
