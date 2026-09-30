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
General recursive method specialization and field-local runtime polymorphism
remain outside the approved scope; COMPILER-83 owns the language boundary.
A red that is red on BOTH the old and the new plug
and that the change does not touch is a register row, not a promotion block
(root's ruling, 2026-09-12, on eq-generic-fields a_ and typeclass-smoke T578
under the Zig plug).
The fleet runs on the stable AgentGrid deployment. AgentGrid integration
continues under Potato; outstanding
acceptance gaps remain in D:/Projects/AgentGrid/agentgrid-backlog.md.
These dispatches use CLAUDE.md R-GATE and runtime RAM admission,
which override older blanket job-count and gate statements below. A changed
build script alone does not establish seed reachability; inspect the inputs.
Existing unverified shelves remain preserved and require their own proof
before landing. Task-scoped workers do not acquire fleet seats or mailboxes.

**Outside contributions (Damian, 2026-09-10).** A PR is ingested through
PerforceProcess.md section 7 with the contributor credited in the CL; the PR
and its issue stay open until Git publication, then close with the commit,
the CL and the credit paragraph. No GitHub merge button. The Update 59 intake
(PR 135 to 145) is closed; issues 110, 115 and 126 stay open with receipts.

**Parallel shelf work is authorized (Damian, 2026-09-10).** Each lane owns
one bounded unit below and runs scratch proofs while another lane resolves a
dependency; numbered shelves with explicit dependencies, not unproven main
changes. Rebase retained work onto current main before fresh proof. A fixed point
does not grade typed IR or hosted output.

Declared test exclusions are deliberate decisions (Damian, 2026-09-10).
Preserve the runner's prescribed `-Tier all -Jobs 16` selection for normal
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
- Claims: `apps/games/**` and `apps/landing/**`, except `web/compile/**`, which
  is the Prism page and stays fester's.

### The wasm plug (reek)

Parity with the hosted x86-64 lift is closed (wasm 53 = hosted linux 53,
2026-09-01); wat2wasm stays on the PATH as the assembler (Damian, 2026-09-02).
Open:

- The release gate's `wasm-bundles` phase builds the wasm plug and all nine
  `apps/*/build-wasm.ps1` modules with the compiler the run built (85 s,
  2026-09-24); games builds its default game only, and `wasm-run` then RUNS the
  hosted corpus under wasmtime and `wasm-e2e.ps1`; `page-build` builds the
  compile page and runs its own arms; `browser-checks` runs the gpushow WGSL
  sweep and ModBuilder's `test-emit.mjs` / `test-site.mjs` in headless Edge
  (release gate only). Still outside every gate: `apps/fishtank/ft-verify.mjs`,
  which grades the tracked `fishtank.wasm` by hand (node).
Claims: `codex/plugs/wasm/**` is reek's; fester keeps
`apps/landing/web/compile/**` and the Prism page; reek announces before
touching `build-page.ps1` or `page-lenses.ps1`. The plug alone takes no token.

## MAIN AND PUBLIC RELEASE

MAIN OPEN (Update 63 pushed 2026-09-25, commit `fb5f2a70`). The token is granted for proven seed CLs only.

Latest public release: **Update 63** (2026-09-25, commit `fb5f2a70` on
GitHub master and GitLab main, frozen at main 29159, seed `C74F10419BA0DB66`).
`GitHubUpdate63.md` records the proofs and limits; `GitHubUpdate64.md`
accumulates the new cycle. Re-measure the seed and mirror tips at the next release.

**PREVIEW NOW (Damian, 2026-09-30: "push this to preview for now, and keep going till we resolve these last bits with Fester and figure out this tensor core bs problem").** red runs ``build/push-preview.ps1`` (dry run at head, then ``-Push``; Damian's word is given). The release waits, additionally, for fester's remaining DeskScheduler items (bounded rendering, pane policy) and an answer to the tensor-core question: whether a page can reach the tensor cores (WebGPU subgroup-matrix work in Chromium, behind a flag or shipped) or the page must hand off to a local native engine when one is present; reek investigates, then reports the options to Damian.

**The next release is gated (Damian, 2026-09-30, revised the same day: "focus on getting the perf of the diffusion app to parity with diffusion forge, and then we ship the release").** Owner: red. The gates: first, the in-browser diffusion page measured against Forge at the same model, size, steps and sampler on this box, with red closing what WebGPU allows (register tiling, `shader-f16`, whatever else the measurement names) and the number shown to Damian before the release (stable WebGPU reaches no tensor cores, so native parity is not assumed); then, every lane merged down past main 32141 (DeskScheduler stage 2's presentation) with its own tests green. Spark studio implementation is AFTER the release. The image page's publish rides this release.

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
   console.** The service and its admin pane shipped (`PreemptiveScheduler.md`,
   "The shape that shipped"; WORKS-48).
   The bed is the instrument, via codex-vm NAT port-forward; there is no metal
   (2026-09-09).
2. **The compiler in WASM building itself in a static page** (fester) is
   shipped (`codex/plugs/wasm/page/index.html`, `build-page.ps1`). **Hosting
   that page from our own kernel is a separate later step (Damian); do not
   couple item 1 to it.**

## Track A -- the stick is an OS. SITTINGS ARE OPEN (Damian, 2026-09-23).

The sitting rule is the standing rule below ("SITTINGS ARE OPEN"). An item a
bed can answer is answered in the bed; an item only metal can answer is
"metal-gated" and waits for the next sitting, which root composes.

- **The I219 medium-death hunt is PARKED (Damian, 2026-08-24)** and does not
  revive on a flight; `I219IsNotAnE1000.md` is the record.
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

- 18, `OtaBoot boot-load` (reek): LATENT, no production caller.

## The Prism dev environment (red's when Damian opens the build-out; the games rules campaign comes first)

Damian, 2026-09-23: red goes on "full blown prism awesomeness build out"
after the Valheim work (MB-4, `apps/modbuilder/modbuilder-backlog.md`). Every other lane takes a Prism stage only when nothing
is live on its own register, and says so. Design and stage register:
`apps/prism/design/Active/PrismDevEnvironment.md`; row:
`apps/prism/prism-backlog.md` PRISM-7. Two traps bind every page deploy:
rebuild from a seed at or after `7B6A4950`, and regenerate
`build/output/Codex.codex` FIRST (L-SAMEVER).

Open, in order:

1. **Stage 4, the Claude panel**: the code is on main and the arm green; the
   acceptance needs a real key and a billed call. The request shape is pinned
   in the design and must not be written from memory. The key is Damian's and
   DEFERRED (2026-09-08).

The one-command public `compile/` page refresh is Damian's. Rulings (Damian,
2026-08-28): the stage-5a Linux bed is ALL of the options (WSL verification
arms, a QEMU Linux guest, 5b's `.exe` verified natively); **boards** means IoT
board build targets per HAL board chapter; **bench** means our codegen
benchmarks against any configured build chain; the zig work (Steve's PRs)
rides LAST.

## The games rules campaign (Damian, 2026-09-29)

Damian's words: "i want a thorough review of all the games, every move from
every possible board state class. no more half baked rules." Scope: every
game in `apps/games/classic` (36 engines) as played in the arcade
(`apps/landing/web/games/arcade.js`, `rules.js`), engine AND page, because
the page decides which legal move a click makes. Per game: first, the rules
written from the canonical source, every move and every exception; then, the
board-state classes those rules distinguish; finally, one graded arm per
class asserting the LEGAL SET, both what is allowed and what is refused
(L-BOTHARMS, L-VACUOUS). A rule the engine lacks is fixed, not noted. Lead:
red. val takes the card games;
`apps/games/**` and `apps/landing/web/games/**` are shared by the two for
this campaign.

The idiom, set by backgammon: the rules are prose in the engine chapter
("The Rules", `Backgammon.codex`), cited to their published source; the
engine answers legality for a whole move, so the page never chooses one;
set-up exports (`bg_empty`, `bg_put`) let `<prefix>-verify.mjs` build
each state class and assert its whole legal set; an oracle written in the
grader from the rules text, not from the engine, is run against the engine
on random positions and against every move of the engine's own player; the
page's `move` and `view` are driven for the choices only the page makes;
and a sabotaged rule must turn the arms red. For a solo puzzle the legal set is the moves the rules allow from a position (for sudoku, a digit not already in the cell's row, column or box, if the page refuses conflicts; else the win test) and the win condition, each graded both ways.

| game | owner | state |
|---|---|---|
| backgammon | red | rules graded, engine and page: moves (20 classes, 800-position oracle), the doubling cube (offer, take, drop, ownership) and gammon/backgammon scoring (300-game oracle) |
| checkers | red | rules graded, engine and page: compulsory capture, capture chains (the turn stays with the capturing piece), crowning ends the move, men capture forward only; a whole-set oracle at every ply of 12 games |
| chess | red | rules graded, engine and page: promotion to any of the four pieces (the page asked, not a forced queen), threefold repetition from an exact position record, dead positions by material; movement stays graded by perft 4 and an independent generator |
| go | red | rules graded, engine and page (Chinese rules, Tromp-Taylor area): captures before the suicide test, positional superko, region scoring, komi 7.5; an oracle on every point and both areas through random play |
| othello | red | verified, no change: ot-verify already asserts the whole legal set, flips, passes, the end and the winner in lockstep over 40 games, and the page takes legality from the engine |
| connect4 | red | verified, no engine change; c4-verify gained both diagonals and the full-board draw |
| mancala | red | rules graded (Kalah): sowing past the opponent's store sowed the next pit twice; now a cursor walk; a lockstep oracle over 60 games reaching every rule |
| hex | red | rules graded: wins by a path oracle with its own neighbours; the swap rule (player 2 may take over the first stone, mirrored), which the engine uses on a central opening |
| dots and boxes | red | verified, no change (edges, scores, the extra turn on a completed box, the full board) |
| royal ur | red | verified, no engine change; ur-verify gained a Finkel-rules oracle in lockstep over 40 games (every legal set and move; the wasm layer keeps its own legality copy, and sabotaging it turns the oracle red) |
| 2048 | red | rules graded: the score was the sum of the board, now the merged values; a slide/merge/spawn/score oracle over every move of 20 games |
| minesweeper | red | rules graded: flags added (a flagged cell is never opened, by click or flood) and a safe first click (the mine moves, Microsoft rule); counts, flood, loss and win were already graded |
| sudoku | red | rules graded, engine and page: givens are the engine's (a fixed mask; the page asked a set of its own), the win is all 27 constraints, and a dealt puzzle has exactly one solution (a hole is kept only while a capped solution count stays 1); 17 placement classes, the whole legal set over 300 played positions, 6 win classes, 30 puzzles counted unique by an independent solver, a deadly-rectangle control; sabotaged boxes, givens and uniqueness turn them red. `sudoku-solve` and `sudoku-remove-cells` now copy, so `/api/sudoku/` no longer serves the solution as the puzzle |
| mastermind | red | rules graded, engine and page: the secret took `state mod 1296` one LCG step from the seed, and 1103515245 is 81 x 13623645, so only 16 of the 1296 codes were ever the secret (16 reached over 20,000 seeds); each peg is now a mixed draw (`rand-in-range`), all 1296 reached. Key pegs over all 1,679,616 ordered pairs; the guess legal set at a new game, 5, 9 and 10 wrong guesses and a broken code; the codes still fitting recomputed from the history; sabotaged scoring, row limit and pool filter turn them red. `classic-games-run.expected` moved with the secret |
| life | red | verified, no engine change (Conway B3/S23 on a 20x20 torus; rules prose added): lf-verify gained every (state, live-neighbour count) class, 2 x 9, at an interior, an edge and two corner cells, and the toggle; the generation oracle was already complete. Sabotaged S234 and a broken wrap turn them red |
| tictactoe | red | verified, no engine change (rules prose added): wasm-verify gained the whole game tree played both sides, the legal set, mover and result at all 5,478 positions against a rules oracle, and the published totals (958 final: 626 X, 316 O, 16 drawn; 255,168 games) counted by the engine's own verdicts; a dropped diagonal and a ninth-mark win scored as a draw turn them red |
| rps | red | verified, no engine change (rules prose added): rp-verify now drives `rp_play`, the page's only move, which nothing graded: from one state all three of your throws meet the same reply (simultaneous throws) and each round scores by the rules, over 800 rounds; a throw off the table is refused. An opponent that reads your throw turns it red |
| yahtzee | blu | rules graded, engine and page: the upper bonus (63 for 35), the Yahtzee bonus (100 with 50 in the box) and the Joker rules (forced upper box, full lower scores, else 0 in an upper box); the engine answers the legal boxes and the page offers only those; 7 state classes and a 400-card oracle, a sabotaged Joker turns them red |
| liars dice | blu | rules graded, engine and page: the loser of a challenge opens the next round (the next player still in when the loser is out), bid legality answered by the engine (the page already asked it), and the computer calls when no raise is legal; a lockstep oracle over 30 games checks every legal bid set, bid and challenge, and a sabotaged opener turns it red |
| battleship | blu | rules graded, engine and page: turns alternate one shot at a time and the shot that sinks the last ship ends the game (the old round fired both sides and could hand a same-round finish to player 1), each ship keeps its number so a sinking is announced (the page names them), placement falls back to a scan so a fleet is always whole; a 1000-fleet shape arm and a lockstep oracle over 40 games, and a sabotaged turn order turns them red |
| mahjong | blu | rules written (Shanghai: 36 groups of four with flowers and seasons each one group, free by sliding left or right on the one-layer layout) and graded, no engine change: every ordered pair at every state of 6 deals against the rules, 2,178 legal and 6.2 million refused, and "stuck" as no legal pair; the page already asks the engine; a both-sides-free sabotage turns it red |
| monopoly | blu | rules graded, engine and page (main 31032): the full Hasbro rules, 2008 amounts; railroads and utilities with their rents, taxes, both 16-card decks, double rent on a whole group, houses and hotels built and sold evenly from a Bank of 32 and 12, doubles and three-doubles Jail, all four ways out of Jail, auctions, mortgages at 10%, debts raised or bankruptcy to a player or the Bank. Every action is answered by `mono-legal`; `mo-verify` runs an oracle written from the rules text in lockstep (100,032 actions, 32.9 million legal-set questions, 83 classes reached) and drives seat 0 only through the page; five sabotages turn it red. The old dice could never throw doubles (consecutive Rng states alternate their low bit), fixed |
| risk | blu | rules graded, engine and page (main 31086): the classic 42-territory board, 83 borders, six continents at 5, 2, 5, 3, 7, 2; the deal round the table and setup placement of 40/35/30; reinforcements with continent bonuses; 44 cards traded in sets for 4, 6, 8, 10, 12, 15 and then 5 more, with the 2-army territory bonus and the forced trade at five; any number of attacks with the attacker choosing 1 to 3 dice; move-in of at least the dice rolled; an eliminated player's cards taken with the forced trade at six; one fortifying move through connected territories. Every action is answered by `rk-legal`; `rk-verify` runs an oracle written from the rules text in lockstep (122,536 actions, 36.2 million legal-set questions, 43 classes reached) and drives seat 0 only through the page; five sabotages turn it red. The old dice came from consecutive Rng states, fixed |
| hexwar | red | rules graded, engine (moved from blu by root 2026-09-29): rules written to the Avalon Hill conventions with the game's own tables; seven defects fixed: a unit could step into an enemy-held hex; retreats went in a random direction (off the map, into enemies, into zones of control) where a blocked retreat now eliminates; adjacent artillery added nothing yet died with the attackers; the CRT was read transposed (the 5:1 column unreachable) and exactly 1:2 fell to 1:3; defenders fought with strength instead of the defense factor; a stack defended as one unit; an exchange killed every attacker. hw-verify: 11 step classes, 45,280 steps against a rules oracle, 4,000 random assaults and every odds boundary at every die checked board-for-board; each defect's sabotage turns them red. `hexwar-run` and `hexwar-supply-census` expectations moved (the census's contact count fell to 0 in all 130 games; which fix removed those cases is not isolated). `fortified` and `elevation` are read by nothing |
## The local image generator page (Damian, 2026-09-29)

Damian: the diffusion app "needs a webUI on the cobblestoneproject.com with a
card on the landing. it should direct users on how to start generating images
locally on their gpu", with a test of capabilities, and "a tutorial about
downloading the models from huggingface, and the lora site with the
downloads". Owner: red.

**Built (2026-09-29):** `apps/landing/ImageGenPage.codex` (`web/imagegen.html`,
generated by `apps/landing/build.ps1`) and the landing card `td10`. The probe is
the html plug's `gpu-probe-then` (adapter, limits, `shader-f16`, a timed 1024^3
f32 GEMM checked exactly on 64 samples), graded by
`apps/landing/test-imagegen.mjs` (8 arms: real GPU, no WebGPU, a GEMM made
wrong). The route is NVIDIA compute capability 8.9 and up only (the PTX plug
targets `sm_89`); a browser cannot read device memory, so the page states
SDXL's 9.6 GB instead. Flux is listed as not served, with no downloads, since
`serve.ps1` takes SDXL and SD1.5 only. The SDXL download is byte-identical to
the measured file; the SD1.5 one matches every required tensor (headers
compared, not run); the tokenizer disk minted from Hugging Face's two files
is byte-identical to Forge's; the driver command compiles.

**Publish gate:** the public mirror has no `apps/diffusion` yet (404,
2026-09-29), so the page's steps and source link work only after a release
carries it. The page's step 5 mints the CLIP disk by hand, which still works; since main 31057 `serve.ps1` mints it itself from `<models>\tokenizers\clip` (vocab.json, merges.txt; T5 under `tokenizers\t5`), so the next page edit makes step 5 a download into that folder.

Pool work (Damian, 2026-09-29: "lets take that on when we need more things
to do"): in-browser generation (wasm orchestration, WGSL kernels on WebGPU,
SD1.5 then SDXL; red, taken 2026-09-29, `docs/Designs/Active/Apps/InBrowserDiffusion.md`: stage 1 in progress: f32 storage lowers in the WGSL plug; next, the kernels in the gid convention), and the two Magic games (val, taken 2026-09-29; `apps/games/magic`, its 16 open
findings and `WorkPlan.md`'s waves; `apps/games/codexmagic`), taken when a lane
has no registered unit. The site publish is Damian's.
## Spark, the image studio (Damian, 2026-09-30)

Damian's words: a "visual uplift of the app, add links in to download the
popular models and loras from the community sites, and make it a useful tool
to someone new to image generation. it needs to have a simple interface with
'advanced' mode for full features that are useful but niche. I want it to work
into a LLM account for the actual prompt, like we are trying to do with the
prism dev environment. basically this feature now needs to hook into spark the
app and spark the app needs to be lifted and built into the cobblestoneproject
landing as a touch it now fully featured app."

Owner: red for the implementation, after the release; **the design and plan in ``apps/spark/`` are reek's, now** (after reek's current refiner unit); val holds
`apps/landing/**` and reviews the landing card. Starts after red's current
unit (the checkpoint list, the run console and the job history). **The source is the ORIGINAL Spark, `D:\Projects\Spark`** (Damian's WPF app, .NET 8, 101 .cs/.xaml files, outside the depot): an AI creative workbench whose Concept Art tab already has Forge integration, prompt stacks, a LoRA browser, refine presets and preference tracking; `Services\CivitAiClient.cs` (the community model and LoRA site); `Services\OllamaClient.cs` (local LLM for story and prompt generation in the new-project wizard); `SetupGuidePresenter` (the newcomer path); a project document store; `Spark\PLAN.md`, `SPARK_VISION.md`, `Spark.md`, `THEME_SPEC.md`. `apps/spark` in the depot is NOT a port of it: it is a separate 3D, canvas and audio suite (May, Phases 1-7; June's WebGPU studio) that carries none of the workbench. The port replaces the Forge HTTP backend with the in-browser engine (and `codex_image` natively). Stages, each
its own landing:

- **0, the design** (`docs/Designs/Active/Apps/SparkStudio.md`): an inventory of
  the original's features and what each maps to in Codex, what Spark
  is in the browser (today `apps/spark` is a bare-metal suite whose browser
  demo is SPARK-6), the simple and advanced modes (which Forge controls each
  shows), the model and LoRA links (links to the community sites, never
  hosted copies), the LLM prompt panel and its account model shared with
  Prism stage 4, the first-run path for a newcomer; a naive reader (R-NAIVE).
- **1, the visual uplift and the simple mode** over the current page.
- **2, the model and LoRA catalogue with download links**, checked by the
  page's header classification.
- **3, the LLM prompt panel** (the account model is Damian's, below).
- **4, Spark in the browser around it**, and the landing card "touch it now".

**Ruled (Damian, 2026-09-30):** both a local LLM and a provider one ("a provider based one like you"); the local one runs in OUR OWN wasm/WebGPU code, not Ollama. Spark's full scope stays the goal ("audio, video, 3d modeling etc. but one step at a time"). **The design and the plan are written into `apps/spark/`** (a design doc and a plan) now; **ALL implementation is shelved until after the next release** ("write the goals down, but shelve the implementation work till after release").

**ROOT commands the fleet; red assists (Damian, 2026-09-12).** The table is the
assignment, not a suggestion; re-read it on every merge-down. An item here is
a pointer; the register named beside it holds the detail. The compiler-bug
order is whatever `codex/compiler/compiler-backlog.md` shows open. fester is
otherwise held in reserve for the hardest problems (Damian, 2026-08-26).
**DeskScheduler's remaining scope is pane policy and bounded-work enforcement.**
The 2026-09-24 ruling still applies: each pane declares a rate or a budget
and skip or run late on a miss (`DeskScheduler.md`). The design separates
policy, enforcement and the required physical GUIOS acceptance.
| agent | now | then | standing |
|---|---|---|---|
| **blu** | **NOW:** WORKS-78's first half (shelf 31969), on fester's DeskScheduler presentation (main 32141): repaint only what changed; the present path is fester's. reek holds the img2img resize and masked-content modes. | **NEXT:** the code-layout campaign, stage 3 (Damian, 2026-09-30: "the expression formatting terseness sometimes creating walls of text"): `codex/build/CodeLayoutFormat.codex` over the compiler, quire by quire, each landing under the token. Design, recipe and the stage 2 proof: `docs/Designs/Active/Build/CodeLayout.md`. | `ProtocolStack.md` holds no open edge-mesh row (checked 2026-09-24). |
| **val** | **NOW:** complete the nine-set pool's coverage (8.1: LEA/LEB/2ED/3ED/ARN/ATQ/LEG/DRK/FEM), largest family first, colour change included; no size cap (Damian, 2026-09-30: "there is no work too big to get that first nine sets done"). Then: the Magic games from the pool (`apps/games/magic/WorkPlan.md`, Wave 2 onward; findings in `MagicFormatSolver.md`); the battery is `apps/games/magic/Test.codex`, 257/257 (2026-09-30). Lanes K, S and A and wave 3 are done, FIX-27's word tokenizer included; next, the era ladder 8.1 (WorkPlan wave 4); lane D DR-I is blocked on a WPF Command Bridge host (WorkPlan lane D). The card games are done. The diffusion sampler and scheduler table is complete (`Diffusion.md`); `DiffusionDriver` still refuses every sampler except Euler and DPM++ SDE. Open: the driver refuses `lora=` lines; WORKS-77 (the preview fill is a partial application nothing calls); `p4 status -a`'s lapse in the server log (`Build.md`, "Open, unowned"). GopBoot is val's. | **NEXT:** `apps/games/codexmagic`, then the diffusion driver's `lora=` lines and WORKS-77. | **For Damian, each blocked on a ruling, hardware or an asset:** (DATA-1, DATA-W2, SPARK-3, FW-3, GPUSHOW-1/3, GLOBE-1, each in its row; the options live in `docs/PM/Active/DamianDecisions.md`); fishtank 1.1 (the Stable Diffusion endpoint); fishtank 1.5, SPARK-6 and FW-2 (a look at the page on a real GPU). Standing: the virtual-desktop wording (`DamianDecisions.md` 3.11); FW-1's options are Deferred. |
| **fester** | **NOW:** DeskScheduler stage 2 remaining work: bounded rendering and pane rate/budget plus skip/run-late policy (`DeskScheduler.md`). | **NEXT:** renderer continuation ownership and bounded work units. Physical acceptance stays with the coordinated sitting; render workers require the design's scheduling and ownership proofs. | `test-self-verify`, `check-generated-scripts` and `test-cross` take `-Kernel`; `build/boot-arm64.ps1` does NOT. `deck-headroom`; ProductBuilder stage 6 on hold |
| **reek** | **NOW:** the four built-in extras with a refiner (`serve.ps1` refuses them; `Diffusion.md`, "Built-in extras"). `img2img-refined` already conditions the base on the request's extras and the refiner on `ti-no-extras` (`i2-ref-swap`, `Img2Img.codex`), which is Forge's behaviour if `apply_refiner`'s reload leaves the refiner UNet unpatched (`modules/sd_samplers_common.py:189-207`). Next step: confirm in `update_inner_model` that no patch survives the reload, lift the refusal, and grade whole pictures against Forge through its API (port 7861, as `build/esrgan-picture-oracle.py`) at the refiner rows' settings, with FreeU, SAG, PAG and dynamic thresholding on, beside the distance the extras move Forge's own picture. | **NEXT:** `flux-euler`, `flux-first-image` and `sdxl-euler` sit at the 60 s bvt wall budget on a cold file cache or a shared card; `diffusion-esrgan` needs the `build-output/diffusion-upscalers` junction per workspace. | Plugs close-out lane and the codex-vm GPU bridge; `tools/codex-vm.c`; a tokenless `codex/foreword/` landing names the closure check in its CL (`PerforceProcess.md`, root 2026-09-08); Blocked on Damian: SPARK-4 |
| **red** | **NOW (Damian, 2026-09-30, after testing the demo page):** the checkpoint list. The page asks once for the models folder (the browser's directory picker; a page cannot list a folder unasked), reads only each `.safetensors` header, and shows every file: grey while unchecked, red when it is not SD1.5-compatible (by the same tensor binding the page and `img2img-model` already use), normal and selectable when it is. With it, generation stats on the page (Damian, same day: "more stats about the generation like wallclock"): total wall clock from the click, checkpoint load, text encode, each sampling step and the steps per second, VAE decode, the GPU adapter and the settings used, shown beside the image, and a live console panel that streams them as the run goes, the way Forge's console window does (Damian, same day: "i get more feedback about the run in diffusion forge console window, i would like to see that here"): the checkpoint read and bind as it happens, a per-step progress line with steps per second and the time left, each stage's start and end, and a history of the session's finished jobs with each job's total time and stage breakdown ("like how long the jobs are taking"). Then register tiling in `bk-linear`/`bk-conv2d` (the page is ~7x the native time; the design names the one-output-per-thread tiling as the gap), then `shader-f16`. **Before that:** In-browser diffusion (`docs/Designs/Active/Apps/InBrowserDiffusion.md`), standing GO: stages 1 and 2 landed; every SD1.5 kernel runs on WebGPU (main 31305). Every stage-3 graph is in a chapter a page can cite (`SamplerGraph`, `UNetGraph`, `UNet15Graph`, `ClipGraph`, `VaeGraph`; native outputs byte-identical, 2026-09-29), Euler runs in the browser over `BrowserSampler`; a page parses a picked checkpoint's header with `SafeTensors` and binds it with `CheckpointLayout` exactly as natively (the html plug is CCE-faithful with tail calls, main 31726; arms in `codex/plugs/html/arms/`). `build/push-preview.ps1` is built (main 31698); its dry run at 31678 is clean and the push waits Damian's word after he previews the image page. The image page is built (section above) and publishes after a release carries `apps/diffusion`. The Update 65 release (draft main 30717) waits Damian's go. Sitting 18's card and image are ready to fly (`HardwareSitting.md`, `diag-sitting18.img` 81B75294, rehearsed arms=60; boot 2 needs a USB mouse). | **NEXT:** stage 3: the VAE decodes in a page within max 1 of Forge (`vae-decode.mjs`) , one SD1.5 UNet step matches `sd15-unet-step` on all 26 blocks (`unet15.mjs`) and CLIP-L encodes within 3.0e-5 of Forge (`clip.mjs`, `InBrowserDiffusion.md`); `ClipBpe` tokenizes in a page as Forge does over the html plug's byte heap (`tokenizer.mjs`); the first SD1.5 image in a tab matches `codex_image`'s render within a mean 0.52 of 255 (stage 4, `txt2img.mjs`, 126 s); the demo page runs (`apps/diffusion/BrowserImagePage.codex`, `node apps/diffusion/build-browser-page.mjs` writes `build-output/browser-imagegen.html`: pick an SD1.5 file, 18 s to load, 94 s an image at 512 x 512, 20 steps); next emphasis and several chunks (`ClipPrompt` cites the native encoder), a tokenizer source for a published page, and `Txt2Img`'s `ti-den` onto `guided-with`; after it, register tiling in `bk-linear`/`bk-conv2d`. | Releases, personally and end to end; the next release carries GPUSHOW-3, the gpushow live-compile block (Damian, main 30196), beside his batch site republish; `GopWizard.codex`, `apps/guios/**` (`apps/works/GopBoot.codex` released to val for WORKS-5, root 2026-09-24); the 4.3 seed hash check runs BEFORE build-complete; appendix F is refreshed with `build/lp-findings-index.ps1` and its table 2 reproduced by hand, which the script overwrites. |
| **root** | **NOW:** commander. ModBuilder gameplay acceptance remains in `apps/modbuilder/modbuilder-backlog.md`. Damian's direct ModBuilder assignments supersede the prior pause for the assigned work. | **NEXT:** respond to Damian's play-test feedback. Separate campaign figures remain MB-1; the claude.ai pitch page stays. Rejected features: quick-stack (vanilla has it), boar/deer respawn (balanced now), night-sky effects (sky is busy). | Survey and scope in `docs/Designs/Active/OS/ShellRefinement.md`. Build-tooling first slice main25858; remaining closure in `Build.md`. `DiagnosticStick.md` composition; `ComplianceEvidence.md`; `HardwareAbstractionLayer.md` question 5 blocked on a board crypto manual; OracleCloudArm64 deferred; `build/boot/diag/**` released to red for the two stage lifts. |

**Plugs are reek's close-out lane** (from val, Damian's direction
2026-08-18): the register in order, one entry at a time, said in
status.json. Entries other lanes hold are named in the register.
`codex/plugs/zig/**`
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

- **Deferred by Damian, not pending:** OCI account access for
  `OracleCloudArm64.md` phases 5b-5d; the Prism stage-4 API key; FW-1's three
  fix options; CORE-9's CA policy (naming, revocation, key location; the
  current state is tolerable, 2026-09-30); PRISM-13 (no Mac in the near term,
  2026-09-30).
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
- **The Rulebook's over-application rule binds every plug that keeps an
  arity map** (red, 2026-08-24; root, 2026-09-25). Measured correct on every
  backend that runs here (2026-09-25), under-application included.
- **A ping goes unanswered, deliberately** (red, 2026-08-20): no production
  caller for `icmp-parse` until something needs one. Re-verified 2026-09-07:
  one definition in `Icmp.codex` and six callers, all in `icmp-test`. (Track
  B, blu.)
- **The rechecker keeps deriving type-variable instantiation itself** (red,
  2026-08-20); the compiler does not emit it. That is the fork's whole value
  (L-CAPABILITY-LOST); the abstentions are the price. (Track C, val.)
- **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`,
  `.cross-refusal`, `.cross-fatal`, `.no-cross`, `.vmargs`) and warns on
  everything else. (red; built, `p4-stale-check.ps1` prints FAIL on a dropped
  add, verified 2026-09-25.)
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
| `GopWizard.codex`, `apps/guios/**` (`apps/works/GopBoot.codex` released to val for WORKS-5, root 2026-09-24) | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, the lifted probes) | root, 2026-08-18, `DiagnosticStick.md`. Step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex`, `GopComposite.codex`, `GopFiles.codex`, `GopIcon.codex`, `GopSettings.codex`, `GopBoot.codex`, `UefiConsole.codex`, `StickSource.codex`, `DevConsoleBoot.codex`, `DevConsole.codex`, `DevDebugger.codex`, `codex/foreword/ui/**` | val, 2026-08-20 (the Dev Console four 2026-09-24, WORKS-5), the Shell Refinement campaign (`ShellRefinement.md`). Announce-before-you-start stands, and so does checking which `ds` cells are spoken for. `comp-text` stays fester's |
| `apps/works/GopEdit.codex` | FREE -- announce; the Editor's standing rules are `works-desk-contract.md` 0.6 |
| `apps/works/RepoProtocol.codex`, `RepoProtocolPersist.codex` | FREE -- announce |
| `apps/works/AgentBundle.codex`, `codex/test/apps/agent-bundle-*` | FREE -- announce |
| `apps/works/GopReview.codex` | FREE -- announce; `GopFacts.codex` is red's |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `apps/works/GopWeb.codex` and `ds` cell 248 | val, WORKS-48 DONE 2026-09-08: 248 points at the block the desk shares with the web service (`dk-web-cell`). 244 is the pinned-pill mask (`dk-pinned-cell`) and 252 the hover bubble's save block (`dk-bub-cell`); the `ds` block is FULL |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/os/sched/**` and the preemptive scheduler work | val, 2026-08-28, `PreemptiveScheduler.md`. blu keeps `codex/os/net/**`; the scheduler reads that side and does not change it |
| `codex/test/cost/**` and `CostModel.md` | FREE -- announce |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | reek, the close-out lane (from val, 2026-08-18). Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table). **`codex/plugs/wasm/**` is reek's for the parity campaign (2026-08-31, Damian); fester keeps `apps/landing/web/compile/**`; `build-page.ps1` and `page-lenses.ps1` are RELEASED to reek without announce (fester's row, 2026-09-01, on that lane being parked)** |
| `apps/games/**`, `apps/landing/**` except `web/compile/**` | val and red shared for the games rules campaign (2026-09-29; blu holds the seven games its row names).  `web/compile/**` is the Prism page and stays fester's |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | reek, with the plugs lane |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | blu, 2026-08-18 |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators | reek, 2026-08-16, by Damian's direction. Catalog and order: `ShellDslReadability.md` |
| `codex/foreword/compress/**` and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 item 18) | reek, 2026-08-16, red's routing. Seed-reachability is measured per file, not assumed from the row |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce, and it takes the token (seed-affecting) |
| `apps/diffusion/**`, `codex/foreword/ai/SafeTensors.codex`, `codex/test/apps/diffusion-*`, `codex/test/gpu-files/*.safetensors` | val, 2026-09-29, `Diffusion.md` stages 3 and 5 (layouts, VAE decoder) |
A claim nobody honours is worse than no claim. Announce before you go into
a claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

**A LANE HANDS OFF AT 75 PERCENT MEASURED, NOT BEFORE (Damian, 2026-09-08
03:35: "58% is not enough action on the context we load at init; agent
estimates of token spend are almost always very much over"; 75 ruled
2026-09-28).** The number is `build/measure-context.ps1` over the transcript;
a lane's own estimate is not a number. Below 75 measured a lane keeps
working, and a handoff a lane starts early is cancelled by root and the
session resumed; root orders one at 75 or at a genuine boundary above it,
never below. The init read is paid once per session and a handoff at 58 throws a
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

**Emulators (Damian, 2026-09-24): codex-vm is the default for x86-64, QEMU for every other architecture, and Renode is the last resort** (only an arm that needs a board model QEMU lacks). Measured basis: Build.md (blu 26811, reek 26821-26831): QEMU ran the arm64 battery in 25.0 min against Renode's 57.6, at 316 MB per guest against about 1,050, and caught five arm64 codegen faults Renode hid.

**SITTINGS ARE OPEN (Damian, 2026-09-23, his words: "Sittings are open, but
same rules as before: answer all open question in 1 sit, and don't waste my
time or my back.").** A lane does not ask Damian for a sitting; it registers
its metal-only question with root. Root composes ONE sitting that answers
every open metal question, rehearsed in the bed as the exact bytes flashed
(L-REHEARSE), with an output channel independent of the subsystem under test
(L-CHANNEL), and asks Damian once. The queue and records are
`HardwareSitting.md`.

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
believed (fester, 2026-09-01). **No trigger of its own (Damian, 2026-09-24):**
the release gate's check is enough, and CDX mode needs no arm because text
mode is the memory hog.

**BATCH YOUR GATES (Damian, 2026-08-28).** Small CLs land on your dev stream
with targeted tests only; a gate runs once per work ARC, never per one-line CL,
and the batch copies up grouped (P-COPY1). Seed lands are unchanged. Docs and
registers need no gate.

**WE DO NOT HOLD OUR WORK FOR AN EXTERNAL CONTRIBUTOR (Damian, 2026-09-02
10:35).** Our compiler CLs land in queue order and a contributor's PR rebases
onto main when it arrives. The reviewing lane judges it on one test: the result
must be shaped for Codex and its own consumers, never for a transpile target. An
ancillary subsystem with no fleet CL in flight may wait for a contributor; the
entry chapter is not one. Steve Howell's issue 115 is closed: every compiler
chapter compiles with only what it cites (main 28693), and the allocator
initialization it asked about is measured in `docs/ArchitectsSketchbook.md`.

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
banned. `-Jobs 16` on every parallel harness; Renode arms run ALONE; a fan-out
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
