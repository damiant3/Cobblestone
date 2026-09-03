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

**The hole is that there is no install STEP to instrument.** `PerforceProcess.md`
4.3b prescribes a hand-typed `Copy-Item -Force build/output/Sut.cdx
seed/Codex.cdx`. A bare copy cannot consult a verdict, so the only thing
standing between a two-pass gate and a bad seed is whether the operator read
the right screen. In the 21215 case the operator read a one-pass line out of a
DIFFERENT CL's log file, and a raw `Copy-Item` had no way to disagree.

**Design, and it is deliberately not "fail the gate".** The two-pass branch is
a legitimate outcome: the CL can be perfectly good and only the seed install is
premature, so exiting non-zero there would block real work and teach people to
route around it.

1. **The gate writes a verdict file every run**, `build/output/seed-verdict.txt`,
   naming the outcome, the artifact that IS the fixed point, and its whole-file
   hash: `one-pass Sut.cdx <sha>` or `two-pass NewSeed.cdx <sha>`.
2. **The install becomes a script**, `build/install-seed.ps1`, which refuses
   unless the verdict says one-pass, refuses if the verdict file is older than
   the artifact it names (a stale verdict is the same error one step over),
   verifies the named hash against the bytes it is about to copy, and only then
   installs, self-verifies, and refreshes the four `TechnicalDetails.md`
   digests that 4.3b already warns are easy to leave stale.
3. **4.3b prescribes the script instead of the raw copy**, and says why.

**The property that matters: the verdict comes from THIS run.** A grep of a log
by name cannot satisfy it, which is exactly the substitution that failed here.
A cheap corroborator worth keeping in the doc either way: a true one-pass costs
`cdx-fixedpoint` about 0.0 s, and any real duration means stage2 was built.

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
the one `build/build.ps1` builds; a lane's seed is a candidate until the
lane path passes `-Repl` exactly as the gate does (blu's unit, docs and
scripts, after MAIN OPEN).

**Scope note so nobody widens it:** this changes no compiler source and no
check that already works. `Get-CdxContentHash` compared correctly and caught
the difference; an earlier claim of mine that the gate's fixed-point test was
vacuous was WRONG and is retracted at 21226. Do not "fix" the gate.

## THE FLEET DISPOSITION (Damian, 2026-08-31; root commanding)

red: Steve Howell's PRs and his published issues. blu: finishing the work
that was suspended for token budget (its register names it). val and reek:
the two campaigns below. **fester is UNPARKED (Damian, 2026-09-01 evening)
on plugs 2.15 (LANDED 21297/21349/21396; now FAT32 long-name WRITE); Renode
stays out of the loop, and no lane runs riscv or arm64 work until Damian
lifts that** (fester's row in the lanes table). Docs go straight to main;
a code arc gates once per arc, and the batch rules are
`CoordinationProtocol.md`.

**2026-09-02 morning (root, Damian present): riscv and arm64 work is
POSTPONED for the day (Damian, "focus on broader concerns"); Renode stays
out.** Fleet merge-down ran at 21483-21486 (fester and red skipped on
shelves; red is merging itself). Dispatched, each from its register row
read at send time, then CORRECTED to Damian's rulings that landed via red
at 21487 while the dispatches were in flight: **val** GAME-49, pinochle
and bridge as ONE job (trick state into the record, split the hand loop
into a step); **blu** COMPILER-23 repair 2 FIRST (ruled YES, but ALREADY LANDED 19813 on 2026-08-26 and verified at head 2026-09-02: no build was needed,
no token, no seed), then COMPILER-45 (`-EscapeCheck` GP in `copy-sx-pos` on the
selfhost), COMPILER-41 CLOSED 2026-09-02 (it was fixed at 21214 and never closed; verified at head, only the PLUGS question remains); **fester**
FAT32 long-name WRITE (the READ half landed 21446; 2.15 is LANDED
21297/21349/21396); **red** COMPILER-36 (ruled GO, scratch measurement
first), landing the 21370 DESUGAR-KEEP instrument on the same compiler
batch if it fits; **reek** COMPILER-46 (DISK compile answers
`CODEGEN-ERRORS:1` with no diagnostic text; establish where the text goes
before the cause), plugs 2.11 / stage 4 being CLOSED by ruling and SPARK-4
still Damian's. The rest of the "Pending" queue is Damian's for today.

**Queued (Damian, 2026-08-31), unowned, the next drawable plugs row:** text
plugs emit CCE encoding code a simple program never needs and the emitted
`opening` round-trips `to-cce (from-cce x)`; some emitters do not, and they
are the control. Census first, then one plug per CL: `plugs-backlog.md`
2.15.

### Campaign: the games reach the landing site (val)

Stages 1 and 2 DONE: all 33 games run in the browser (arcade, 21145); Spider
page landed 21272 (ace-low, run splits, pips, deal animation). **Klondike
added 21282** (Damian: the arcade had no Klondike), engine to page with its
own grader, so the arcade is 34 games. It has NO thumbnail art: the tracked
source is `assets/games/<id>/` and there is no Klondike scene, so its gallery
card reads plain the way chess does.

What is left in this campaign:

1. **Lifting the watch-only games. THE WRAPPER-ONLY CLASS IS FINISHED**:
   rps, sudoku, mahjong and yahtzee landed 21301, setgame and gofish
   landed 21326. **Playable is 21 of 34, re-measured at 21326** by
   counting descriptors carrying `move`, `keys` or `solo`. Yahtzee turned
   out to be in NO descriptor at all rather than watch-only, and Set could
   not play because the engine counted the sets on the table without being
   able to say where one was. Accounts are GAME-44 and GAME-45.

   **Life landed 21369; war, battleship and mastermind landed 21376.
   Playable is 25 of 34, re-measured at that CL.** Two of those four
   turned out NOT to need engine work at all: Life already exported
   everything but a single-cell toggle, and War has no decisions in it, so
   `wr_round` was the whole of a player's part and the lift was a click
   target. Accounts are GAME-46 and GAME-47.

   **Crazy Eights landed 21404 and Liar's Dice 21424. Playable is 27 of
   34, re-measured at 21424.** Accounts are GAME-48 and GAME-49. Crazy
   Eights was nearer a wrapper than its label said (the third time), and
   what it really needed was the DECLARATION: the engine names the suit an
   eight calls, and that is the player's to choose.

   **NOTHING IS LEFT. THE ARCADE IS 34 OF 34 PLAYABLE**, re-measured from
   the descriptors at the CL below. `pokervariants` closed it, and the
   sizing here was wrong in the way this campaign's sizing has now been
   wrong six times: it said "six variants each needing their own state (a
   seven-card hand, a spade side pot, a wild rank that moves during the
   deal, an opener rule)", and all four of those are state the engine
   ALREADY COMPUTES. What every variant lacked was a DECISION, and the work
   was not per-variant at all: one table carrying a variant tag, a deal that
   takes five or seven, and a showdown that dispatches to the ranking the
   variant already has. Account: `games-backlog.md` GAME-58. The record of
   how each game was sized stays below, because reading the WRAPPER is the
   step that changed the size six times and is the transferable part:

   - ~~**`monopoly` is nearly free.**~~ **DONE, main 21600.** The sizing
     held: one split, at the buy-or-pass, and the watch-only runner became
     a POLICY over that same turn rather than a second copy of it. Two
     defects came out of it that no arm could see, and both are the shape
     this campaign keeps finding: `mo-wasm-step` advanced the turn TWICE,
     so two of four seats never moved for as long as the arcade has
     existed and twenty-one grader arms passed over it; and the board view
     read a board SPACE as a property index and as a player index off the
     same number, so no owned square has ever been coloured. Account:
     `games-backlog.md` GAME-54.
   - ~~**`risk` is the middle one.**~~ **DONE, main 21612.** It was TWO
     phases and not three: this bullet said "reinforce, attack and
     fortify" and there is no fortify in the engine, which reading it
     settled. What the arc is worth remembering for is the arm that a
     sabotage walked straight through: letting a territory attack with its
     last army changed no arm's colour, because every territory is dealt
     three and the opening reinforcement only adds more, so no opening
     position could express the rule. The repair was a different CORPUS,
     stepping twelve turns in first, not another assertion. Account:
     `games-backlog.md` GAME-55.
   - ~~**`hexwar` is the largest.**~~ **DONE, main pending.** The sizing
     was half wrong and reading the chapter settled it: MOVE legality was
     already top-level and reachable, and `ai-step-toward` already
     composed the four conditions a legal one-hex step needs. What was
     absent is ATTACK legality, because combat is chosen by
     `ai-find-target` and the strength of an assault is the sum of every
     adjacent friendly, so the engine has no notion of one unit attacking
     another. An assault therefore names the DEFENDER. The runner is a
     policy over the same steps, proven by a sabotage of the shared
     legality moving the watch-only fingerprint. What cost the time was
     an arm of mine that was wrong twice in the same way, comparing an
     opened state against an unopened one, which are a turn's beginning
     apart; underneath the second reading was a real defect that ended
     three battles in fifty-two on a different board. Account:
     `games-backlog.md` GAME-57.
   - ~~**`pokervariants` is six variants, not one job**~~ **DONE.** It was
     one table and three riders, not six jobs: see GAME-58.

   - ~~**Two trick-taking games that share ONE piece of work: `pinochle`
     and `bridge`.**~~ **DONE, main 21543.** It was one job: the trick went
     into both records (leader, turn, a card per seat, running points), the
     monolithic `play-all-tricks` and `play-all-br-tricks` became a
     `-step` that plays one card, and the runners now drive the same step
     the page clicks. **Playable is 29 of 34**, re-measured from the
     descriptors. The legality was NOT the same change twice and that is
     the part worth knowing before the next pair: pinochle must follow
     suit, head the trick and trump when void, bridge must only follow
     suit. GAME-17 closed on the way through, because the played card
     leaving the hand IS that defect. Account: `games-backlog.md` GAME-50.
   - ~~**Two games needing a betting round nothing models: `poker` and
     `pokervariants`.**~~ **`poker` DONE, main 21568**, and the pairing was
     wrong: the betting round is built and shared, and it buys
     `pokervariants` two of its eight variants because all eight are
     one-shot deal-and-compare with no per-variant state. `poker` itself
     was a BUILD and not a lift, its wrappers being stateless with no
     handle at all, and `draw-count` was uncalled in `Poker.codex`.
     Account: `games-backlog.md` GAME-52 and GAME-53.

   **A count here was hiding a shape, three times, and the third was mine
   (L-ADJECTIVE).** "Nine left", then "seven left", read as a remainder to
   be worked down one at a time; the truth was three pieces of work, the
   first of which closed two games at once in a day. Then this section
   said poker and pokervariants were one job, and they are one plus six.
   **So the rule for this campaign is written here rather than left to be
   rediscovered: report what the remaining work IS, never how many games
   are left.** A fraction is the form the mistake keeps taking, and it
   reads as a shortfall whatever the number.

   **Before sizing any of them, READ THE WRAPPER.** Three times now a game
   filed under "needs engine work" needed little or none, and each time the
   classification had come from the game's reputation rather than from its
   exports.
2. ~~Card `td3` ("aquarium, forty GPU demos, star map, all in your
   browser")~~ **DONE, main 21623.** Confirmed on reading: `web/` holds
   exactly `compile` and `games`, so there was nothing to link and td3 is
   the only card in the grid carrying no link at all. The wording now says
   those three run on the DESKTOP and that seeing them means booting it.
   **Its neighbour td4 was stale the other way and undersold the arcade**:
   "33 classics" against 34 since Klondike, and six games named as playable
   against 32 of 34. Account: `games-backlog.md` GAME-56, which also
   records that `landing.html` is generated from `LandingPage.codex` and
   that the html plug emits LF where the depot copy is CRLF, so a
   regeneration diffs as all 325 lines until it is normalised.

Rules. A card's tag reads "playable now" only while a visitor can play. A
game that fails on wasm is a PARITY finding for reek's campaign below: one
message naming the subject and the failing test, and no workaround in the
game. Chess (GAME-10) stays not built and its `games.json` row stays
honest; GAME-10's sentence about the landing page is stale and is val's to
fix when the file is first touched. Claims: `apps/games/**` and
`apps/landing/**` except `web/compile/**`, which is the Prism page and
stays fester's.

### Campaign: the wasm plug at parity with the hosted x86-64 lift (reek)

Stages 1-3 DONE 2026-09-01. **PARITY HOLDS, re-measured 2026-09-01 on seed
D6ED6F35 with the plug rebuilt to match: wasm 51 = hosted linux 51, one ahead
of hosted windows 50. RE-MEASURED 2026-09-01 after the comparison repair, on
of hosted windows 50**, over 60 selected of 1002 eligible, both arms same
kernel, calibration passed. Exactly ONE subject where wasm is behind linux
(`apps/dev-watch`) and one where it is ahead (`apps/codex-boot`, which both
hosted targets fault on). ops corpus 40/0; SIMD family closed (21134); Arm64Elf
ships (21221); build-page incremental 20.6 s vs 168 s full (21197); no wasm
deck inflation (21210).

**Do not compare those numbers to any score recorded before 21152.** Making the
harness reach every eligible subject (plugs 2.16) grew the corpus to 1002 and
the default cap now selects 56 `apps/*` of its 60, where it used to select
`act-let-scope` .. `dtls-openssl-fragments`. Hosted reading 101/19 against a
recorded 120/0 is a different SLICE, not a regression. The default 60 is no
longer a sample of the corpus, it is a sample of `codex/test/apps`; plugs 2.14
has the account.

seed 42ACED00 with the plug rebuilt again: **wasm 53 = hosted linux 53, one
ahead of hosted windows 52** (hosted 105 pass 15 fail over both targets). The
repair moved all three arms EQUALLY, by exactly the two subjects that were
false reds on every arm, so the verdict is unchanged and is now measured rather
than predicted. **There is NO genuine wasm parity gap left. `apps/dev-watch` was the last one
and it is not a defect in any target.** Diagnosed 2026-09-01: 14 of its 16
output lines are identical on all three arms, and the two that differ print a
RAW ALLOCATOR ADDRESS. Bare metal and hosted linux both land the arena at
6291456 (0x600000), so linux passes by coincidence of layout; windows reports
2147418112 and wasm 71922, and every arm puts beta exactly 64 bytes after
alpha, so the behaviour is identical and only the base address moves. The
oracle pinned a bare-metal address. **FIXED, main 21790 (reek):** the test
asserts `beta - alpha = 64` and the two successful adds report only their ok
flag, so the refusal messages stay graded in full. Proven with a depot-state
control, wasm 0 pass 1 fail before and 1 pass 0 fail after, the control's own
output naming the cause (71922/71986 against 6291456/6291520).
`apps/codex-boot` still passes on wasm and faults on both hosted targets, so on
this corpus wasm is AHEAD rather than level.

The deck-consumption question 2.10 left open; and want 3 of the
`build-page-modules` row under "Registers carrying unowned work" (why
`wat2wasm` never started on `riscv-stdio`; 2.03's addendum names the npm
shim as the candidate and nobody has confirmed it is the same event).

4. Plugs 2.11, emit the binary wasm encoding and retire `wat2wasm`:
   **CLOSED BY RULING (Damian, 2026-09-02, via red): "wat2wasm is fine."**
   The project runs in two directions, one ahead and one behind: the
   transpiler stack is the rope thrown back to the old world, and the cord
   is cut in the forward direction. A native binary wasm emitter or a WAT
   assembler in Codex serves neither, so neither is built. wat2wasm stays
   on the PATH as the assembler. The sizing (21211, 21234: wat2wasm 0.3 s,
   IR-to-WAT 3.6 s of a 172.8 s page build) stands on the plugs-backlog row
   as the record.

5. **No gate builds a web or wasm bundle.** `app-sweep` compiles 267 app ENTRY
   CHAPTERS against `build/app-sweep-baseline.txt` (`-Internal` strides 30 of
   270), which is the bare-metal side and not the browser one, and nothing in
   `build/` invokes `codex/plugs/wasm/build-spark.ps1`. The WGSL half of this
   item CLOSED 2026-09-01 (21344): `apps/gpushow/tools/validate-all.mjs` grades
   all 83 shader modules the app ships and is calibrated both ways, though
   nothing invokes it either -- it needs a browser, so it lives where
   `build/check-app-pages.ps1` lives. What is still cross-lane is that any lane
   touching a browser app inherits a blind spot no gate can see; the per-app
   gaps are in `spark-backlog` SPARK-4, `gpushow-backlog` and `starmap-backlog`.

Games arriving from val's campaign join list 1 as subjects. Claims:
`codex/plugs/wasm/**` moves to reek for the campaign; fester keeps
`apps/landing/web/compile/**` and the Prism page, and reek announces before
touching `build-page.ps1` or `page-lenses.ps1`. Seed-affecting only if a
compiler chapter moves; the plug alone takes no token.

## MAIN IS OPEN

Latest public release: **Update 53** (red, 2026-08-28), github `58b08c38`
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
   half (window slot, request-log ring in `ds` 244, state word in 248,
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
- **Native GOP resolution (red).** Bed half done (`build/gop-mode-arm.ps1`;
  `ExaminersAssay.md` "The GOP Mode Arms"); the `SetMode` half in
  `codex/build/cdxtopeScript.codex` is red's too. Sitting 6 answered
  `gopmode honoured` at 1920x1080 with 10 modes on the ASUS
  (`HardwareSitting.md` "FLOWN 2026-08-21"); red closes the row or names
  what the metal half still lacks.
- **Identity, RULED 2026-08-18 (queue 11, 12): the identity file stays on
  the ESP; auto-unlock is bed-only.** Rotation (`RotationFact`) stays with
  `Designs/Done/OS/Identity.md`; nothing else is open.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE": the standing questions ride ONE diagnostic boot per sitting,
in an argued order (bank before you risk, L-BANK). Every flight's card and
archive row is there, not here. NIC-1, NIC-2 and NIC-3 are answered on
metal; NIC-4's ring half is answered (`rdh-writable=y`, sitting 6).

Open, in the order the ladder flies them:

- **The i219 acquire-loop fix** (blu): unblocked by sitting 12, which
  eliminated both candidate register writes as the medium's cause
  (`HardwareSitting.md`, sitting 12).
- **NIC-4's successor question**: whether a frame ARRIVES during
  `nicring`'s own window. `pre=3` says the part receives before the stage
  looks; the during-window GPRC read has not survived a flight, so "nothing
  arrived" and "arrived and was invisible" both stand. The discriminator
  can say NO (`-e1000-rdh-ro`, the `nic-rdhro` arm) and rides B3's boot.
  Details and the caveat are on the NIC-4 card in `HardwareSitting.md`.
- **From NIC-3**: `aneg-done` is never set on this part while `STATUS.LU`
  comes up, so `phy-bring-up` returns 0 against a link that is up.
- **B3, a real TCP conversation with a real peer**: `DiagB3.codex`, ladder
  stage 13, has answered on metal (sitting 11: thirteen bytes echoed back
  unchanged over the real I219). What the composer owes each sitting: the
  peer named in `DIAG.CFG` must ECHO (`build/boot/echo-peer.ps1 -Port 7`),
  because the conversation is raw TCP and not the repository wire. The
  next sitting is the gate for what b3 still cannot say, not for whether
  it works. **Every diag image built before main 18665 carries a BLIND b3
  (`sent=` was the intended length, not the sent one), and `45239937` is
  one of them: it does not fly.**
- **Finding 4 (ASDE)**: `DiagAsde.codex`, ladder stage 14, risk writes,
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

**STATUS 2026-09-02 10:28 (Damian): a future build-out, probably reek's or
fester's, "not quite yet". Nothing below is drawable or pending until he
opens it. Unrelated to ProductBuilder, which is parked customer work.**

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
2. **Stage 3, templates and the build tab** (unowned; was val's, queued
   behind the games campaign). Stage 2's cite resolution, which it needed,
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
5. **Stage 5c, sockets**: untouched, unowned.
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
| **blu** | **NOW (2026-09-02 15:20): COMPILER-42 IS MID-ARC AND THE LANDING IS ALL THAT IS LEFT ON WHAT IS BUILT.** Five CLs are on `//Codex/blu` and NOT on main: **21963** the ownership analysis (`own-uses` in `IR/Occurrence.codex`, alternatives-take-MAX join beside the inliner's summing one, plus an `own-report` pass so the decision is readable) which passes 4 of 4 design controls and marks all 6 clause (a) violators shared; **21978** x86-64 `__list_snoc_copy` + the `list-push-copy` builtin, proven by arm 4 answering `0,1,1` where `list-push` answers `2,2,2`; **21993** the wasm `$list_push_copy` + dispatch, proven `3,3,3` -> `1,2,2`; **21994** the vacuity note; plus merge-downs and count repairs 21997/22000/22002. **HELD ON A RED THAT IS NOT MINE:** the arc's `-Internal` read 2,881 pass / 80 FAIL, and `build/bvt.ps1:298` builds its whole codex-vm line as `-kernel -output -mem -headless` plus `-disk` and NOTHING else, so it runs chapters the battery excludes or configures (`usb-kbd-connect` needs a `.keys` timeline and an 820-byte `.vmargs`). fester owns that fix; re-gate after it lands, order val, fester, blu, then the token. **Then the landing:** merge main, expect two-pass so run the P-STAGE2 dance, token naming the merged head, FOREGROUND wait (run_in_background is withdrawn under the token), copy up, account both directions. **Then CL5, the call-site rewrite:** an IR pass rewriting `list-push` -> `list-push-copy` where the argument is not provably linear. It is a 47-arm IRExpr tree rebuild and a missed arm silently DROPS IR, so the control is an IR diff of an unchanged program proving the rebuild is identity; register it but do NOT add it to `default-ir-pipeline` until arm64 and riscv know the name. **CL4, arm64 + riscv copying helpers, is WRITTEN-AND-SHELVED ONLY pending Damian's ruling on the arm64/riscv ban** (root asked; same question as plugs 2.06); the arm64 one duplicates ~49 emitted instructions so split the prologue from the grow body rather than restating it. **The staging is deliberate and must not be "fixed" early: the NEW name marks the COPY case**, so absence of the rewrite is exactly today's behaviour and nothing regresses while back ends learn the name; the flip to copy-by-default is its own CL. Arms 3, 5 and 6 of the measurement plan have never run and arm 6 has nothing to sabotage until the rewrite lands. Earlier NOW (root, 2026-09-02 12:30): COMPILER-42 stage 3 (`PersistentListAppend.md`, census 21572/21604; the standing rule LIST APPEND IS THE AGENTS' PROBLEM binds: the bar is a measurement that no previously linear line went quadratic). **THE TIMED TOKEN TEST CASE IS DONE: COMPILER-17 LANDED main 21846, seed 15A1A565 (3,193,347 bytes, verified from the depot by root), one-pass fixed point.** Measured: request 12:00:58, grant the same second, release 12:24:34, HOLD 23.6 min. Work under the token: head re-check 0.2 s, proof gate 510 s green, one forced docs merge-down (P-REMERGE), submit + copy-up + account ~90 s, about 10 min in all. The other 13.0 min is ONE block, measured by blu: grant seen at 12:01:06 (8 s after it was written), proof gate exited green at 12:09:58, and blu did not read it until root's SendMessage at 12:22:55. The lane launched the gate detached and ended its turn, and nothing wakes a lane when a detached process exits; "poll the log" cannot be done by a lane that is not in a turn. **The fix is mechanical and binds from now: a gate run UNDER THE TOKEN is waited on in the foreground with bounded `Wait-Process` calls until it exits (the standing rule "A LANE GRANTS ITSELF THE BOX ON THE FLOOR" has the exact shape; the first form of this rule, the harness `run_in_background`, was killed by the harness at ~3 minutes on red's first use and is withdrawn).** Off-token gates may stay detached; the commander bumps on idle. Pre-token, off the clock: four gates (the overlap with red cost 1.58x wall, 893 s against 565 s alone), one seed rebuild, two merge-downs. Earlier (Damian, 07:58): THE TIMED TOKEN TEST CASE. COMPILER-17 is landed on `//Codex/blu` at 21654 (seed 39A279CE, one-pass fixed point) and NOT copied up: blu's token was cancelled at 07:53 (four launches, ~75 minutes held, a foreground wait that blocked root's messages) and blu is blocked from main for the rest of 2026-09-02. **Next session, first unit. THE CHANGE IS A SHELF: `//Codex/blu` CL 21708 (root, Damian's word 2026-09-02) holds COMPILER-17's ten files, and the stream itself equals main (root resolved the stuck merge 21684 as 21700 and printed main's content over the eight edited files as 21709; only the two new `cap-word-64` test files sit on the stream). Apply it: `p4 unshelve -s 21708 -c 21708`, `p4 resolve` per file with judgement (X86_64Helpers and Builtins moved under COMPILER-36), redo the TechnicalDetails counts for the new test file, re-gate WITHOUT the token until green, then request the token NAMING the merged head, then under it sync, merge, quick build and proof, BVT, copy up, `build-complete`, **recording wall-clock per step from the token request onward**. That measurement is what a previously proofed, ready CL costs to land under the 21663 token shape, and the fleet plans on it. Then COMPILER-42 stage 3 (`PersistentListAppend.md`, census 21572/21604). Earlier NOW (2026-09-01): COMPILER-43 first (measure which __eq_ generator is operative; the eq-req-has name-only dedupe hazard is in the row), then COMPILER-33's code, then the compiler backlog in register order (rows whose BODY says open)** | CostModel: `fixed` is unshipped and blocked on the registry (81 of 265 `bs-alloc` rows read `unknown`, re-measured 2026-08-25; `CostModel.md` 5.1 has the closed decisions); B3 stage 13 and B4 step 6 fly at the grouped sitting (Track B carries the flying caveats: the desk peer is an ECHO listener, the discriminator counts the writeback half only, `match=y` is valid only while identity-mapped) | `codex/os/kernel/E1000e.codex`, `codex/os/net/**`; WORKS-16 |
| **val** | **NOW (2026-09-02 late): THE SAFARI INTAKE IS COMPLETE THROUGH STAGE 5 AND THE SCREENSAVER DRIVES IN A BROWSER. Landed: 22154 provenance, 22160 the 27 port chapters and the Safari quire, 22166 the findings routing, 22205 stage 3 the wasm road, 22215 stage 4 Steve's own 18 graders at 59,816 values and 0 BAD, 22221 stage 5a the browser module and its wire, 22261 stage 5 the page.** No full gate ran and none was needed: every CL was apps and docs, no seed, no token, `check-doc-counts` green 61 of 61 at each landing. **THE NEXT UNIT IS THE LANDING CARD, and it is deliberately not done:** the page is at `apps/landing/web/safari/` and `apps/landing/build.ps1` stage 3e builds its module with the site, but nothing links to it from the front page, and adding a card means editing `LandingPage.codex` and regenerating `landing.html` while fester and reek were both in those files on 2026-09-02. Announce before starting. Publishing to CobblestoneWeb is reek's push. Acceptance for the whole thing is two commands and needs no guest beyond the build: `pwsh apps/safari/build-wasm.ps1 -Page -Wasm` then `node apps/safari/sf-decode.mjs`, which prints PASS and asserts the wire ends exactly on the module's reported length, no non-finite coordinates, and a ride that actually advances. **THE LESSON WORTH CARRYING OUT OF IT: a fixed address inside the range the bump heap walks.** The page buffer sat at 12 MB and its held state at 15 MB against ONE frame's heap reaching 59,528,656 bytes, which produced both a garbage frame header and a ride that restarted every frame, and which imitated a wasm plug defect well enough to nearly be routed to reek as a finding before reading the emitted WAT killed that theory. The invariant is an ORDER rather than a place: read held state before anything allocates, write anything that must survive after all allocation is done. **STAGE 2's staged expectation was also WRONG and the correction generalises:** the chapters were expected not to build at our head and all 27 compiled clean, because the twenty fork commits are wasm and zig BACKEND fixes and the x86-64 front end was never going to care. Before repeating "a red compile is expected" about donated code, ask which end of the compiler the donor's patches are on. Earlier: **SAFARI INTAKE, STAGED AND NOT STARTED, AND THE FIRST READ ALREADY CHANGED THE SHAPE (val, 2026-09-02).** The repository is cloned and sized. What matters before anybody compiles a line: **`safari-codex` is built against a FORK of Cobblestone, not against our main.** `PROVENANCE.md` pins it to base `58b08c38` (Update 53) PLUS PR 100 PLUS TWENTY local commits (re-measured at intake from that file, in two groups of 11 and 9; "about thirteen" was wrong, L-COUNT), and they are not cosmetic: `wasm: Real is an f64, not an i64 with f64 bits in it`, `wasm: a ^ b was emitting a * b; INT64_MIN garbage`, `wasm: ask the IR which imports a module needs`, `wasm: list literals in halves, the 4 GiB ceiling`, `wasm: a scrutinee local per guard-nesting depth`, `zig: emit a chapter one definition at a time`, and more. **So the chapters are expected NOT to build on our head, for reasons that are OURS rather than Steve's**, and PR 100 is the one this fleet deliberately did not land (its `.expected` encodes x86's NaN and overflow answers; `plugs-backlog` 2.07). Treating a red compile here as a defect in the donated code would be the wrong reading, and routing those twenty to the wasm and zig plug lanes as FINDINGS is most of the value of the intake. **STAGE 1 IS LANDED: `apps/safari/PROVENANCE.md` (val 22152), which carries the commit, Steve's permission, the fork pin, and a re-measured size table that supersedes the figures below.** **Size, measured rather than guessed:** `port/` 25 chapters (~1.0 MB, of which `CatStills.codex` 550 KB and `EmojiStills.codex` 232 KB are generated frame data), `judge/` 19 grading chapters, `gold/` 18 goldens, `poc/` 9, `probe/plug/` 10 small plug probes, `harness/` 30 scripts (python, shell and js), and 230 KB of documents (`README` 73 KB, `PORTING_NOTES` 91 KB, `WASM_FINDINGS` 50 KB, `FINDINGS` 40 KB, `NOTES` 27 KB, `PLUG_WORK` 22 KB). `build/` is GENERATED, several files past 3 MB, and must not come in. **The stages, on the C64 shape:** (1) provenance and credit landed first, `apps/safari` with `PROVENANCE.md` naming the commit and Steve's permission (given to Damian 2026-09-02), before any source; (2) the `port/` chapters in, compiled through our x86-64 path, every failure classified as ours-or-theirs against the twenty commits rather than fixed; (3) the same through the wasm plug; (4) `judge/` and `gold/` as the grader, which is the part that makes this more than a screenshot, since Steve shipped a full checking suite; (5) page and card. **Stage 1 is the next session's first unit; nothing is ported yet.** Earlier: **ADDED (Damian, 2026-09-02 15:48): PULL IN STEVE HOWELL'S `safari-codex` (github.com/showell/safari-codex, master, pushed 2026-09-01), his Safari driving screensaver ported to Codex with three verification arms (zig plug against the unmodified Zig game as oracle; bare metal under QEMU; wasm by two roads), `FINDINGS.md` (nine toolchain defects), `WASM_FINDINGS.md` (eleven in `codex/plugs/wasm`, seven fixed on his side), `PLUG_WORK.md` (an emitter change).** Intake after mathbook stage 1 lands: the Codex chapters become `apps/safari` with his provenance and credit in the CL; compile through our x86-64 path and the wasm plug at the head seed; his three harnesses are read for what ours lack; the findings are routed as rows to the compiler and plugs backlogs by owner, none acted on unverified; a page and a landing card follow the C64 shape. **The repo carries no license file; Damian has Steve's permission directly (2026-09-02 16:25, "I am chatting with him, I have his permission"), so the pull is a GO after DATA lands, with that provenance stated in the CL.** **NOW (Damian, 2026-09-02 14:02, direct): APPS ON THE LANDING PAGE.** val registers the stages on this row as it finds them; fester's claims (`apps/landing/web/compile/**`, `build-page.ps1`, `page-lenses.ps1`) are announced before touched; the boundary with reek's 3D-apps campaign is agreed by one message. **STAGE 1, THE C64 EMULATOR, IS LANDED: main 22025 (val 22024), gate green at seed F134E3E7** (exit 0, `CHECK OK 2 clean, 0 known-dirty, 0 regressions`, `constants.hash` unchanged, core SKIPPED graded with the depot seed). The emulator runs in a browser tab: `C64Wasm.codex` is the wasm-facing chapter, `apps/c64/build-wasm.ps1` the pipeline, `apps/landing/web/c64/` the page, landing card td7. **The grader is `apps/c64/c64-verify.mjs` and it holds the machine to the KERNAL's own boot screen** -- "COMMODORE 64 BASIC V2" and "READY." in screen RAM by frame 35, PC in ROM -- with a sabotage control that zeroes the KERNAL ROM and must not produce the banner; 6 of 6, control fires. The port needed no new capability because the C64's whole machine already lives at fixed addresses under #A18000, so it survives between calls in linear memory with no page-held state: a THIRD state contract beside games-backlog GAME-11's two. What did have to move is in `c64-backlog.md` C64-2 to C64-4. **Two defects found by census and deliberately NOT fixed in the port:** `sid-state` and `cia-state` are both `#A16000`, so SID registers 16-31 ARE CIA1's 0-15 and a program making sound on voice 3 writes the keyboard matrix (`OdeToJoy.codex` is such a program, C64-3); and `C64Wasm` duplicates the raster loop, cycle loop and two init functions from `opening.codex`, which wants a shared machine chapter (C64-4). Also repaired on the way: the landing page's td3 said the GPU demos were "the one thing on this page you cannot click", which reek's gpushow card (main 22014) had made false. **STAGE 2 IS mathbook AND IT IS UNDER WAY. Its four stages, on the C64's shape: (1) `MathbookWasm.codex`, the core to wasm; (2) the page at `apps/landing/web/mathbook/`; (3) `mb-verify.mjs`, a grader with a sabotage arm; (4) the landing card and the `build.ps1` hook.** The one unknown is ANSWERED and it removes the hard part rather than adding one: **there are TWO wasm conventions in this tree and mathbook wants the compiler page's, not the arcade's.** The arcade's games export i32 functions and take throwing WASI stubs. The compiler page's module is a **WASI program**: source in through `fd_read`, output out through `fd_write`, driven by `_start`, and its input is plain **UTF-8** rather than CCE, because the foreword converts at the I/O boundary (R-CCE). So the page needs NO JavaScript CCE codec, which is what the text-in/text-out worry was about; `codex/plugs/wasm/page-workspace-arm.js:279-305` is the working precedent. Quire `Mathbook` is already in `build/quire-map.ps1`. What mathbook lacks is an ENTRY: it has no `opening` and no `read-line`/`print-line` anywhere, only `parse-expr : Text -> ParseOutput` and `parse-input : Text -> AssignResult`, so stage 1 is an `opening` that reads an expression and prints parse, simplify and print. Because a WASI `_start` runs once and exits, the page re-instantiates per evaluation, which is exactly what the compile page does per compile. Picked by a portability census over all 1,061 `.codex` chapters under `apps/` (effect rows, `Device.Port`, `Gpu.Compute`, framebuffer, net), restricted to apps off reek's 3D list (gpushow, fishtank, starmap, globe, spark). mathbook is the purest substantial core in the tree: **zero effect rows, zero Port, zero Gpu, zero framebuffer, zero net across 17 chapters and 170 KB** (the census's one hit was a false positive, `is ExVar (name) -> [name]` being a list literal). It is a real CAS (a 28 KB `Parser`, `Expr`, `Simplify`, `Solver`, `Calculus`, `MatrixAlgebra`, `NumberTheory`, `Statistics`, `Plotting`, `Proof`), interactive by nature, and it ships `TestExpr`/`TestParser` so the grader gets an answer key rather than a screenshot. **Its one real unknown is that mathbook is TEXT in and TEXT out** where the C64 and the games pass only integers, and CCE is not ASCII, so it needs a text convention across the wasm boundary; the compile page already feeds source into a module, so the precedent exists and is the first thing to read. Then, in order: **data** (480 KB, 42 chapters, one effect row; a browser SQL console), **circuits** (472 KB, 66 chapters, an EDA suite with SPICE, but 73 framebuffer sites make it a C64-shaped port), then **erp**, **market**, **vision**, **helm**, **secrets**, **fileshare**, **collab** (pure, but business software rather than something a visitor clicks), and **diagram** (72 framebuffer sites). Not eligible: `works`/`guios` are the desktop itself (457 effect rows, 385 `Device.Port`), and `browser`/`spark` carry real framebuffer rendering. The task-frame stages and PreemptiveScheduler (COMPILER-49's fix is red's 21932, landing under the token today) queue behind it. Earlier: nothing in flight. The games campaign is CLOSED, landed main 21911 (val 21906): the arcade is 34 of 34 playable, hexwar and pokervariants both lifted, and `GameServer.codex` repaired after being red on main since 21272. SPARK-4 is DONE by deletion in the same copy-up (val 21907).** | **NEXT, in order: task-frame stages 3 and 4** (`ShellRefinement.md` "THE TASK FRAME, and its four stages"; 3 is hot-launch pills, 4 is the Cobblestone button's position, both not started). First step of 3 is to find where the band builds its pill list and establish whether a pill's IDENTITY is a window handle, because a launch pill has no window. **Then PreemptiveScheduler stages 1+2**, which are BLOCKED on `compiler-backlog.md` COMPILER-49: the first process a program spawns is pinned to the boot processor, so stage 1's arm cannot go green and must not be softened to pass. Its shipped caller and arm are shelved in val CL 21810, gate NOT RUN. Then the hover preview. | `ShellRefinement.md` "WHAT IS STILL OPEN IN 6.4" is the only list; WORKS-47/41/44/46/40 in the works register |
| **fester** | **NOW (2026-09-02 10:20, root, from Damian's ruling): `-Internal` ALWAYS runs sem-equiv (L-NOGATE); build it in `build/build.ps1` (read `Build.md` first, the shipped script is hand-maintained), prove it with a control that shows the phase running on a CL that touches no compiler chapter, gate, land. REFINED 10:25 (standing rule "A TEST RUNS WHEN IT IS LIKELY TO FAIL"): "always" means whenever the core runs, on ANY compiler chapter, not only `opening.codex`; a docs CL skips it with the core. THEN the L-NOGATE repair under the same rule: `-Internal` RUNS the test chapters whose source cites a changed chapter (it compiles them today), with a sabotage control. THEN a gate defect found by val 2026-09-02 10:52 (L-SAMEVER): on a `core SKIPPED; graded with depot seed <digest>` run, `test-compile-batch` compiled with `build-output/bare-metal/Codex.cdx`, which held BE8B04B5 from a 07:18 run, not the depot seed the line names; three untouched foreword subjects went red with CDX1073 and the red vanished when the kernel was refreshed. The gate must pass the depot seed as `-Kernel` to every phase it grades on a skipped core, or refuse when build-output's kernel digest differs from the seed's. Then the previous NOW:** the battery-batch row under "Registers carrying unowned work" -- WHICH LAYER loses bytes so a batch can hand a test another test's output. Contained by the `DROPPED` refusal; the CAUSE is unowned. Read before any guest, and ask before running one. Renode stays OUT: no riscv or arm64 work at all (Damian, 2026-09-01 evening), so 20867's attribution and every test-cross arm stay parked.** DONE this morning: plugs 2.15 (row closed, main 21520) and the whole FAT32 long-name row -- the library WRITES long names (21520), the reader's cluster boundary is graded (21539), and the guest is graded against a HOST-written FAT32 image (21560). The `Fat32Writer.codex` item that row carried was struck as not being work; the reasoning is on the row. Prism page claims (`web/compile/**`, `build-page.ps1`, `page-lenses.ps1`) are released; reek's campaign may touch them without announce | A8 the desk build loop when VT-x metal is available (Track A) | `apps/landing/web/compile/**`, `build-page.ps1`, `page-lenses.ps1` (RELEASED to reek without announce, this row's NOW column); `deck-headroom`; WORKS-24 rides a sitting; WORKS-17's syntax half is a `Theme` decision; ProductBuilder stage 6 is ON HOLD pending customer approval (`codex/product/product-backlog.md` 6) |
| **reek** | **NOW (2026-09-02, end of session; Damian direct, superseding the csharp NOW below for the evening): THE STAR MAP IS LIVE ON THE PUBLIC SITE.** cobblestoneproject.com/starmap/index.html, site commit 581f07d, all 11 live URLs verified 200 by request. On main: 22237 (the module, build script, grader, page, landing section 8, card), 22244 (landing.html regenerated), 22270 and 22272 (a `.Count` on a scalar in the just-merged fireworks section, which under StrictMode Latest took the WHOLE site build down; fireworks ships exactly one page and one kernel, and the same shape is latent in the gpushow, starmap and summary lines). The fireworks tile and the refreshed compile page went up alongside, because the gallery entry points at the fireworks app and shipping one without the other is a 404. `safari/` and `spark/` are staged in the site working tree and deliberately NOT published: nothing links either. **SHELVED CL 22295, NOT ON MAIN, is starmap v2 and it is the next action:** the module stops carrying a catalogue and reads all 117,931 stars of `data/starmap.dat` in place (module 34,798 -> 17,746 bytes), and the projection is fixed to normalise direction before projecting, which is what a star map shows. The old perspective projection could not frame a catalogue spanning 155,000,000 to 1 in distance: 21 of 80 objects sat behind the observer. Graded by hand: `node apps/starmap/sm-verify.mjs`, 17 arms green plus 2 sabotage controls red, the magnitude query checked against a count computed independently from the bytes (5,071 vs 5,071). Resume: `p4 unshelve -s 22295 -c <new>`, `pwsh apps/starmap/build-wasm.ps1 -Kernel seed\Codex.cdx`, `node apps/starmap/sm-verify.mjs`, then the landing build, then submit and republish. **THREE PRE-EXISTING DEFECTS were found by compiling this app for the first time ever** and they are the reason to distrust anything else in `apps/starmap`: a stray `)` in Polaris's coordinates, `mk-star` and `mk-dso` bodies that parsed as partial applications, and a `star-dist` squaring coordinates whose square is 1.02e23 and does not fit a 64-bit integer. A fourth is in the DATA and cannot be fixed here: `import-hyg.ps1` patched the constellation offset into `con_count`'s slot, so the 239 constellation lines are unreachable and `hyg_v42.csv` is not in the tree to regenerate from (`starmap-backlog.md` STARMAP-6). **What the wasm plug's `wasm-export-list` actually is, since the backlog said otherwise: it is NOT the export mechanism.** Emission is pruned to `opening`'s closure, so a function only the page calls is dropped and its export names nothing; `sm-keep-alive` touches them, as `apps/fishtank/FishTankWasm.codex` does under that name. **I OWE ROOT AN APOLOGY AND IT IS RECORDED HERE:** auditing my own leftovers I killed pid 19428, which was the `Wait-Process` completion waiter on root's `build/build.ps1` release gate (pid 3628), on a wrong assumption that it was an orphan of mine. The gate itself was unharmed and still running; only root's completion notification died. Message left in root's inbox. **Two things the landing build needs and nobody had written down:** `apps/landing/web/landing.html` is a TRACKED file the build writes, so the build cannot run on a clean workspace without `p4 edit` on it first; and the L-SAMEVER guard at main 22147 fires after every merge-down that touches `codex/compiler`, so `build/concat-codex-self.ps1 -CodexDir codex\compiler -OutFile build\output\Codex.codex` is part of the recipe, run AFTER the merge and not before. Earlier: **NOW (Damian, direct, 2026-09-02 ~19:52): FIX THE CSHARP PLUG; the Update 55 release does not ship without the DDC, and main is PINNED for seed CLs until reek says DDC-green.** The plug dies OUT OF MEMORY emitting the compiler's 17.1 MB IR at 3,281,441 chars of C#, on `builtins()`; the previous release emitted 4.2 MB from 16.98 MB. **THE CAUSE IS THE BASELINE, and reek's first two answers were both wrong: it is NOT the per-definition Text and NOT plugs 2.21.** `CSharpStdio.plug-emit-ir-stream` runs `ir-tokenize` over the whole 17.1 MB and `build-tree` over all of it, and BOTH live for the run because `__heap-restore` only reclaims BETWEEN definitions, so the ceiling scales with the IR. `HEAP=0xB9E00008` is GUEST-SIDE, below the 4 GB hole: root at `-MemMB 4096` and reek at 6144 die byte-for-byte identically, so memory cannot move it. **THE PLAN, not started: per-def tokenization.** Slice the raw IR by top-level paren matching, then per definition `__heap-save`, tokenize that slice, build its tree, emit, `__heap-restore`; plus a first pass for `arities` and `muts`, which today come from the whole tree. The care is paren-scanning CCE IR text containing string literals. Half a day. Acceptance: `emit-compiler.ps1 -Kernel seed/Codex.cdx` on the head IR completes, `dotnet build` succeeds, root runs `build/ddc-witness.ps1`; land `codex/plugs/csharp/**`, copy up, message root DDC-green. **LANDED 2026-09-02 (main 22195) AND NEITHER IS THE FIX:** `run.ps1` now REFUSES a truncated `Codex.cs` (halt text or unbalanced braces is FAIL, calibrated both ways on real artifacts) because it printed OK on one and the witness would have certified a compiler with `builtins()` missing; and the `emit-list-elems` list-literal quadratic, real but per-definition transient, labelled in its own CL as NOT the OOM. **DONE 2026-09-02, so do not re-take it:** the cdx-arm parity divergence was NOT codegen, it was `build/output/Codex.codex` five hours stale so the page module was built from pre-COMPILER-48 source (L-SAMEVER); `build-page.ps1` now refuses a concat older than `codex/compiler` (main 22147, plugs 2.27). The site is LIVE and correct at CobblestoneWeb `cfa0e0e` with the arcade fixed: the wasm plug's `wat-wrap` had no case for a full-width i64 wrapping band, so every store into one emitted `unreachable` and every game RNG trapped (main 22184). Earlier: **RULED (Damian, 2026-09-02 ~17:50, via root): the cdx-arm parity divergence (wasm-hosted compiler 89,347 B against bare metal 89,506 B on the 92-byte page program; predates tonight, the published module is byte-identical) is reek's to FIND AND FIX, then continue the site work; if the fix moves the compiler or a plug, reek rebuilds ALL of the site. Second finding, Damian's words: "find this math and data work that somehow made it into the landing, but not the apps they link to": the mathbook (22084) and data (22109) cards are on the landing page while the app pages they link to are not reached on the live site. Establish where they went missing (not built, not staged by name, or not linked by the path the card uses), fix it, and before the push verify by request that EVERY card's link answers 200.** Then the NOW below stands. **NOW (Damian, 2026-09-02 evening, via root): REBUILD AND REDEPLOY COBBLESTONEWEB, then it feeds the release.** In this order, each a CL on main before the site push: (1) merge down (directive in the inbox; main 22126 carries the R-GATE docs fix); (2) plugs 2.25, the wgsl `bh_march` drop, because Damian wants the globe on the site "for sure" and it does not validate until that is fixed; (3) the globe page (stage 2 on this row); (4) the c64 (landed, td7), fishtank IF it looks good by your own eye, globe, and safari once val's intake lands (val's row; if val is not restarted, safari is off this cycle's site and the card waits) all linked from the landing page with cards, and THE GRAPHICAL FUN UNDER ONE CARD with a gallery page on the gpushow shape (gpushow, fishtank, globe, spark when it is ready); (5) every game and every landing app builds fresh from `apps/landing/build.ps1`, refusing on any stale artifact; (6) the Prism plugs built fresh (`build-page.ps1`, `page-lenses.ps1`, released to you) and pushed, and the compile page's library drop-down updated to the release head's code INCLUDING the self build; (7) the site push, staged by name, with the six-tracked-file trap from the gpushow push in mind. Every run that starts a guest is asked of root first (R-GATE); the site push itself needs no ask. Root runs the release behind you and freezes the release head when your landing-page CL is on main; say "site landed <CL>" by one message. Earlier: **HANDED OFF 2026-09-02 (seventh session) WITH ONE UNDIAGNOSED FAULT, plugs 2.25: the wgsl plug drops `bh_march` from `GlobeKernels` while emitting a module that CALLS it, so globe does not validate and must not be queued for the site. My theory that the topological loop exhausted its iteration budget is REFUTED by the run that tested it (budget raised, nothing changed). The standing contradiction, and it is the whole job: the emitter reports `emitset 9, emitted 8` and that ONE MORE PASS AFTER THE LOOP WOULD EMIT THE NINTH, which excludes the no-progress exit, while raising the budget excluded the budget exit, and there is no third exit. One of those two measurements is lying. Already excluded and not to be re-bought: self-recursion alone, buffer forwarding, readiness and ordering, and my own instrument failing to look (67 callee names examined, not 0). Next arm: have the loop report per pass how many definitions it added. Shelved CL 22112 holds a named-state refusal instrument that is worth landing on its own (it turned a silent wrong answer into a named one and eliminated three hypotheses in four minutes of box time) plus the dead budget line. NOTHING OF MINE IS RED ON MAIN. Earlier: **RULED 2026-09-02 ~15:05 (Damian, direct to reek; scope corrected by Damian to root 15:08): PUBLISH GPUSHOW FIRST (21952) on CobblestoneWeb; fishtank (21967) and spark (21981) are landed on main but "need a lot of work" before they are served, so they stay off the public site until reek brings them up. And START THE REAL GLOBE PORT, a Codex-built renderer, which answers the truth question reek raised at 14:33 (globe.html and starmap are hand-written JS with inline WGSL, not Codex output): they are not shipped as they sit; globe is rebuilt from Codex, starmap follows the same rule. The plugs 1.59 emission sweep has RUN on F134E3E7 (49 outputs, kernel hashed before and after) and its classification is queued behind the globe unit.** Earlier NOW (Damian, 2026-09-02 13:50, direct): THE 3D APPS ONTO COBBLESTONEWEB (gpushow, fishtank, starmap, globe; spark last and as a real port over the `apps/spark` records, its browser page having been deleted under SPARK-4 at 21911). root's ordering was globe first, then gpushow (`validate-all.mjs` grades its 83 shaders), fishtank, starmap. **FINDING that moves gpushow first: `apps/globe/web/globe.html` is NOT built from Codex.** Its WGSL is an inline template literal in the page, and `apps/globe/README.md` puts the app at 60 percent with no renderer, no fetch layer and no `opening`, so shipping it as a Codex demo is a false claim. DAMIAN'S RULING WANTED on whether it ships as an openly hand-written page or waits for a real port. gpushow is the one that is ours end to end: 42 `[Device]` kernels through `codex/plugs/wgsl`, all 42 `.wgsl` present, 83 modules clean on nvidia lovelace 2026-09-02. **SPARK PORT, SIZED 2026-09-02 (reek, root asked; no code written).** The deleted bundle's 98-export bridge is NOT the shape to rebuild, and the corpus size is misleading: 87 chapters and 12,303 lines, but no entry needs them. Transitive closures measured: `SparkGfxDemo` 13 chapters / 67,718 B, `SparkApp` 13 / 79,182 B, `SparkDemo` 3 / 29,461 B. The bare-metal surface is ONE library chapter, `SparkDisplay` (`gop-display-new`/`gop-clear`/`gop-blit` against the GOP framebuffer at #40000000); every other closure member is pure computation, and the only other files naming a raw address are `UndoIntegration` and the five entry points. `SparkGfxDemo` already renders into a `Framebuf` and THEN blits, which is exactly the shape fishtank-wasm proved today: wasm computes pixels in linear memory, JS blits them to a canvas with ImageData, no WebGPU needed for the first slice. Its closing `spin-wait` is unbounded recursion used as a halt and a browser build drops it, driving frames from the host. **GLOBE PORT, STAGED 2026-09-02, and MY EARLIER READING OF IT WAS WRONG.** I told Damian globe was "write the renderer, not recompile it", from `apps/globe/README.md` saying the app is 60 percent with no framebuffer draw calls. The README describes the CPU side. `apps/globe/kernels/` holds `GlobeKernels.codex` and `EarthKernel.codex`, which cite `Gpu chapter DeviceEffect` and carry ten `[Device]` entry points, the SAME shape gpushow ships: earth-pixel, earth-shade-pixel, earth-sky-pixel and a black-hole raymarcher. Measured, not read: the closure compiles through `codex/plugs/wgsl/run.ps1` on kernel F134E3E7094AF77B to 9,240 chars of WGSL with ZERO refusal markers, and emits real entry points (`earth_pixel_main`, `bh_pixel_main`, `@compute @workgroup_size(64)`, storage-buffer framebuffers). Damian's "mostly a recompile" was nearer the truth than my correction to it. **STAGE 1, and it is a PLUG gap not a globe one:** Chrome's WGSL compiler rejects the emitted module with one error, `value 4278190080 cannot be represented as i32`. The wgsl plug maps Codex Integer (64-bit) onto i32, so the ordinary opaque-alpha idiom `255 * 16777216` (0xFF000000) is unrepresentable; it appears at FIVE sites across the two kernels and is the only thing between here and a validating module. The plug already emits `bitcast<f32>(<n>u)` for floats, so the mechanism exists and the fix is to fold an out-of-range integer constant and emit `bitcast<i32>(<n>u)` where the value fits u32. Do NOT work around it in the kernels: the idiom is correct Codex and the next app hits it too. **STAGE 2:** a page that dispatches those kernels and blits the storage buffer to a canvas. gpushow is the precedent for the SHAPE (generated shaders, hand-written host wiring) and it is the standard the site already ships on; fishtank and spark are the precedent for the framebuffer-to-canvas blit, including that `gop-put-pixel` packs BGRA with no alpha so the JS swaps and sets it. **NOT in this port:** the 16 live data overlays, which are the hand-written page's doing and are a separate item. **PUBLISHED 2026-09-02: gpushow IS LIVE on cobblestoneproject.com** (site commit f934112, 165 files; main 22014). Verified by request rather than assumed: landing, the gallery, a demo page, a generated `.wgsl`, the `.codex` behind it, and 39 of 39 gallery thumbnails and 42 of 42 kernels all answer 200. fishtank and spark are landed but NOT published and NOT linked, on Damian's verdict that they need a lot of work; the gaps are `fishtank-backlog` 1.5 and `spark-backlog` SPARK-6, and neither guesses a cause. `fishtank-backlog` 1.2's claim that the wasm page hangs because `FishTankWasm` ships a stub opening is DELETED, it was false. Two publish mechanics worth keeping: `robocopy` overwrote SIX tracked files this run (the compile page and the arcade) that this change had not rebuilt, and they were restored with `git checkout` before staging, because shipping an unrebuilt artifact over the live one is a regression that reads as a publish; and staging is BY NAME, never `-A`, since 52 untracked lens modules sit in that repo and nothing requests them. **SPARK STAGE DONE (root's GO, 2026-09-02), and it came in under the estimate.** `SparkDisplay` needed NO wasm variant and is untouched: every write it makes goes through `gd-fb-addr` and only `gop-display-new` knows the GOP address, so `apps/spark/SparkWasm.codex` builds a `GopDisplay` pointing at WASM linear memory instead and the whole surface moves. It advances the heap above the framebuffer reservation BEFORE it allocates, because a fixed-address framebuffer is otherwise overwritten by the first large allocation (the heap starts at 71,746 and the `Framebuf` pixel list is megabytes). `apps/spark/build-wasm.ps1` is short because `codex/plugs/wasm/run.ps1` already resolves the closure; it carries `--enable-tail-call` and REFUSES on a wat2wasm failure and on any builtin refusal marker, from the start rather than after shipping something stale. The module and its WAT are `.p4ignore`'d build output and `apps/landing/build.ps1` section 7 BUILDS them, so the bundle is reproducible from the depot; fishtank's tracked binary is the counter-example. Measured by driving the module with the page's own import object: `spark_render` returns 334 faces, the framebuffer goes from 1 distinct value to 9, and 50,276 of 307,200 pixels are not the clear colour, so geometry was actually drawn rather than a clear having succeeded. **One thing I could NOT verify and am not claiming: the page's BGRA-to-RGBA swap.** It is correct by construction from `gop-put-pixel`'s packing, but all 9 rendered values are `r == g == b`, so no measurement on this scene can tell a correct swap from a reversed one. That is SPARK-5's fault, new on the spark backlog: `scene-render` reads a light's intensity and DROPS its colour, measured by changing both lights and watching the shading move while every value stayed grey. **PROBE RUN, and it removes the unknown and shrinks the job:** that closure compiles through `codex/plugs/wasm/run.ps1` today, IR 217,686 B, WAT 264,113 chars, **ZERO refusal markers on either spelling**, `wat2wasm --enable-tail-call` exit 0, module 33,035 B. So there are NO missing builtins to implement, which was the risk. Its imports are `fd_write` and `fd_read`, the pair the fishtank page already supplies, and its only exports are `_start`, `memory`, `__heap_reset` and `disk_reserve`, because the chapter has an `opening` and names no exports. **Steps:** (1) a `SparkDisplayWasm` chapter writing into linear memory at a fixed base instead of the GOP address, as `FishTankWasm` does; (2) an entry chapter exporting init, tick and the framebuffer base, with no unbounded spin; (3) `apps/spark/build-wasm.ps1` on `apps/fishtank/build-wasm.ps1`'s shape, carrying `--enable-tail-call` and a refusal on wat2wasm failure from the start rather than after it ships a stale module; (4) a page plus a Node driver check that instantiates with the page's own imports; (5) section 7 in `apps/landing/build.ps1`. **Size after the probe: about 1 lane-day for `SparkGfxDemo` as a rendered 3D scene, the compute half being done already. The interactive studio (`SparkApp`) is a separate and larger item and is NOT in that number.** **FISHTANK STAGE DONE:** the wasm page had never run. Three faults, each hiding the next: the emitted JS was a SYNTAX ERROR (22 Codex `#` hex spellings emitted as JS numerals, so the whole script died at line 39); `print-line-raw` terminates with the raw serial byte 10, which inside a CCE payload decodes to the character `7` and glued `7function` onto the next token, so the page emitter now uses `print-text` with explicit CCE newlines (the compiler note at `X86_64Builtins.codex` already said not to delegate a newline to `print-line` in a payload built in pieces); and the page supplied `fd_write` but not the `fd_read` the module also requires, which is a LinkError at load. `build-wasm.ps1` omitted `--enable-tail-call`, the only wat2wasm call site in the tree that did, and merely WARNED on failure, so it shipped a module four days behind its source beside a freshly assembled page and printed `done`; it now refuses. Graded by driving the module with the page's own import object: 52 fish after `init_aquarium`, 80 specks, all 8 sampled fish moved over 60 ticks. Byte-identical before and after the 21960 plug fix, which bounds that fix's blast radius to the hex-pattern case. Landing surface is two files, 46 KB; none of the 30 MB of assets and models is reached. **gpushow STAGE DONE:** its 40 pages fetched `/kernels/`, `/web/` and `/screenshots/` at the SERVER ROOT, so they resolved only under `tools/serve.mjs` and would have painted blank canvases from a site subdirectory; relativized, re-graded 83/83, and `apps/landing/build.ps1` now assembles `web/gpushow/` as .p4ignore'd build output behind two refusals (a kernel with no `.wgsl`, and any server-root absolute ref in a page). plugs 2.06 is POSTPONED by Damian 2026-09-02, until there is less interesting work. fester's two page-build scripts are announced before touched. The four handed-off actions below queue behind it. Earlier: HANDED OFF 2026-09-02 (sixth session). No red gate outstanding, nothing open, level with main. FOUR next actions, recipes in reek's memory file: (1) the plugs 1.59 emission sweep, `test-plugs.ps1 -Subject overapply`, then classify all 51 `test-output/<plug>/overapply.out` FAMILY-AWARE, reporting REFUSE/EMITS and NEVER ok (`ada` emits uncompilable Ada and scores PASS at 7,166 B); (2) the 2.10 close, re-run `hosted-elf-test.ps1` windows AND linux after red's 21866, both were 58/60 on red's trapping-multiply defect, and if N of N RE-CUT shelved CL 21836 rather than unshelving it (its base is stale and an unshelve already clobbered a landed correction once); (3) the wasm phase-name build for COMPILER-50, which needs a full `build-page.ps1` because `codex-compiler.wasm` is 09-01 and is not in the page-lenses manifest; (4) plugs 2.06 is DAMIAN'S, do not start it. PR 116 is REVIEWED and NOTHING WAS POSTED, deliberately, pending Damian's choice between Steve's cut and fester's 21788; the review's findings are COMPILER-47, COMPILER-48 and the note that the PR cannot be applied to head without reverting red's LOWER keep-deck work. Earlier NOW: REVIEW STEVE'S PR 116**, the `opening.codex` split into `Chapter: Compile Driver` (the standing rule "WE DO NOT HOLD OUR WORK FOR AN EXTERNAL CONTRIBUTOR" carries the terms and the PR summary). Review on the diff, not the description: fetch the branch, census the concat (195 = 126 + 69, no line lost), confirm the seam runs one way, compile the whole compiler with the depot seed and self-compile it (the fixed point is the test; token only if you decide to LAND it, which is a seed move); judge whether the chapter reads as ours. Post the findings as a PR comment from the project account, credit Steve, and put the verdict on this row. THEN write issue 115 (deck-discipline dependencies undeclared, the Lexer's silent `deck-record` identity) as a `codex/compiler/compiler-backlog.md` row, unowned. Earlier: Both of Damian's 2026-09-02 rulings are LANDED: `emit-cce-to-utf8-helper` deleted (main 21767, seed moved to 930B322B, TechnicalDetails digests re-measured at 21777) and the `apps/dev-watch` oracle fixed (main 21790). The wasm campaign is CLOSED.** Stage 4 (plugs 2.11) was closed by ruling 21487, "wat2wasm is fine", so the old "stage 4 per Damian's design pick" line is struck. Landed today: COMPILER-46 closed (21582) after three independent faults each hiding the next, and DISK mode was never at fault; plugs 2.19 (the img plug corrupted every embedded SOURCE.SRC through one spurious CCE conversion); plugs 2.20 closed by the new `plug-selftest` gate phase (21608), which runs a plug's own `test-*.ps1` when that plug changes. `test-disk-compile` passes end to end for the first time | BLOCKED ON DAMIAN, sized: SPARK-4 (generate the bundle from `apps/spark/`, or delete bundle and script; hand-repair ruled out by measurement). OWED, blocked on the Renode ban: the cross phase against 21289. Registered and deliberately NOT chased, nobody is reporting them: plugs 2.10, and `plug-binary` building only ONE binary per plug so spirv's second is ungraded | WORKS-9 is metal-gated, routed to red's sitting; `ShellDslReadability.md` stays reek's |
| **red** (live again since 2026-09-02; the 09-01 "lane is reek's" handoff is superseded) | **NOW (root, 2026-09-02 13:55): COMPILER-49**, the spawn-affinity cell that reads 0 at rest where `X86_64Boot.codex:2647` stores -1, so a program's FIRST spawn is pinned to the BSP and never runs while the parent waits (val's row and the compiler backlog carry the mechanism and the narrowing: the store's neighbours take effect and the store does not; val's arm code is shelved as val 21810 and is the acceptance test). Seed-affecting, token, one gate at a time. **BUILT 2026-09-02 (red, CL 21932): the -1 was stored before `emit-build-process-page-tables` zeroed the PML4 page that holds 36224 (the file's :295 prose names the same trap for 36208); the store now follows the tables. Probe reads -1 at rest; val's `gopweb-spawn` arm goes from `NO AP RAN THE SERVICE` under FC01D8B7 to `an ap ran the service` under the fix; stage 2 == stage 3. Gate and token next.** **DESUGAR MEASURED AND CHOSEN (red, 2026-09-02 15:05, at head seed F134E3E7, `-Measure` by ADDRESS as the Sketchbook's own rule requires): the DESUGAR deck is 49,904,584 B at 302.1..352.0 MB and NO later phase touches that range (SCOPE deck 92..98, SCOPE bivy 201..236, CHECK 199..227, CHECK bivy 937..986, LOWER 138..160 and 826..840), so unlike LOWER's old deck these are unique pages in the union and a real host prize of about 50 MB against a 540 MB peak; the keep copy that follows it grows the frontend keep by 54.2 MB, so the deck is all survivors. THE CHOICE: desugar straight into the frontend keep (set the keep before `desugar-document` and pass the chapter through instead of `copy-as-chapter-guarded` copying what is already above the reservation base), which removes both the 50 MB scratch range and the 54 MB copy; the desugarer's bivy is 309 KB, so there is no scratch to strand. Acceptance is head-against-candidate host peak working set (never intermediate-against-intermediate) plus the frontend keep's growth staying at about 54 MB, which is what says no desugar garbage landed in it. Seed-affecting, one unit, built next.** **BUILT AND SHELVED, NOT PROVEN (red, 2026-09-02 15:40, handoff on Damian's word): red CL 21992 holds the candidate (whole `desugar-document` inside one `deck-record` extent with the deck cursor at `fk-base`; `relocate-chapter-tail` in `AstNodes.codex` copies only the six fields `desugar-assemble` passes through from the parse document; `desugar-def` copies `chapter-slug` instead of sharing the parse Def's; a `keep-escapes` count over [keep cursor, R10) under `-EscapeCheck`). Measured at F134E3E7: host peak 506 MB against 548 head (two runs each, sampled codex-vm WorkingSet64), one-pass fixed point, keep copy 54 MB to 619 KB, desugar bivy 309 KB to 24 B, AST walk 0 escapes. NOT SOUND: the `-PoisonCompact` self-compile dies at RIP 0x103347 under the frame root with R10 ITSELF = 0xA3A3A3A3AFB9DA84, the heap pointer restored from a poisoned mark after the frontend compact, which the AST walk cannot see; before the extent wrap it died reading a poisoned `chapter-slug` in `scope-adefs-ll`, so the shape is a parse-side object still shared past the compact. RESUME: unshelve 21992 onto a merge of main, `concat-codex-self` to scratch, stage 1 from the seed, then `compile.ps1 -Kernel <stage1> -PoisonCompact -Repl` on the concat and map the crash RIP; suspects in order: a `PhaseStart` or heap mark that now lives above the keep cursor, the parse-side results (`colliding`, `assignments`, `chapter-names`, `parse-bag`) sharing LEX-deck or PARSE-keep objects, and what `__deck-pos` reads after a `deck-record` extent closes. The `-EscapeCheck` run dies separately in `copy-sx-pos` on a non-canonical pointer and is the same question from the other side.** **STAGE 3 LANDED (red, 2026-09-02, red 21992, seed 3127F4C7, 3,179,730 B, signed and self-verified; the first land under the new R-GATE: scratch fixed point, one granted BVT run 137/0, sign as four single guests, token for the landing only).** The cause was not a mark: R10 was a legitimate cursor plus a poisoned qword, an allocation whose SIZE was read from parse memory the desugar compact had filled with 0xA3. Census by shape against the parse copier found five surviving parse-side pointers the walk had missed (L-FALSIF): the ListExpr and PropEqType spans, the BoundedIntType `OverflowMode` cell, the cite's two texts; each is copied now. Measured head against candidate at F134E3E7: host peak 550.2 / 550.2 to 505.8 / 505.6 MB, DESUGAR deck 50.57 MB at the keep's own address, bivy 24 B, one-pass fixed point; the `-EscapeCheck` crash is COMPILER-45 and reproduces identically on the head seed. Account: `ArchitectsSketchbook.md` "Stage 3 LANDED". **PLUGS 2.21 IN PROGRESS (red, 2026-09-02, handoff 16:45): sized on its row (main 22090); wasm half LANDED main 22103 (plain add/sub/mul trap via three preamble helpers, probe traps, sabotage control prints the wrapped number); text family 1 zig/csharp/rust LANDED main 22121 (checked primitive for plain, wrapping form for the band; `plug-oracle-arith` carries two wrapping-band rows, zig and csharp 57/57). NEXT: family 2, the checked-primitive plugs with no runtime on the box (java, kotlin, scala, groovy, go), then the refusal family as one mechanical CL under root's ruling (named diagnostic, never a silent wrap); arm64/riscv wait on the Renode ruling. The row carries the recipe and the expected python/js/ts reds on the band rows.** **LANDED TODAY: COMPILER-36 add/sub (21798, seed F185CB2E), the spelling unit (21902, seed FC01D8B7: bare `Integer wrapping`, the i64-min literal), the evidence plug trap fix (21855), the app LCG sweep (21924: ten chapters banded, three u64 readers shift-or).** Earlier: **THE COMPILER MEMORY CAMPAIGN (Damian's direct assignment, 2026-09-01, outranks the handoff above). Stage 1 LANDED main 21187 (red 21185, seed FECCDD90): per-definition reclamation in CHECK, SCOPE, PARSE and LEX, self-compile host peak 1,147 to 537 MB measured with the SUT as kernel; the account is `ArchitectsSketchbook.md` "Per-definition reclamation". It also closed the sem-equiv release blocker above. STAGE 2 LANDED main 21359 (seed 5EB49E2C; -Internal green at 21350, one-pass fixed point, 205 refusals, deck-headroom 1.33, app-sweep 28 clean): four steps, LOWER per-definition reclamation with a keep deck (21278), the CHECK-RESOLVE tail in 16 MB chunks, resolve a run then copy it (21319), the LOWER keep floor = the LOWER floor after the BVT refused desk-root-guard at 96 MiB (21342), and LOWER leaving the cell at keep-end so its compact releases the scratch reservation, with a refusal on a truncated lowering replacing the lower-sat guard that could never fire (21345). Guest: CHECK-RESOLVE deck 155 to 28 MB, LOWER deck 200 MB to 21.5 kept, pre-emit heap 487 to 160 MB. HOST PEAK DID NOT MOVE, measured head seed E5425317 against the landed 5EB49E2C, two runs each: selfhost 545 to 549 MB, desk-root-guard 554 to 564 MB; the copy-up description's 597 to 564 compared two intermediate kernels, not head, and the 508 quoted for step 1 alone was a 96 MiB keep that refused the desk unit. Stage 2 bought guest headroom (about 305 MB less deck across LOWER and CHECK-RESOLVE) and an honest truncation refusal, not RAM: LOWER's old deck sat on pages the frontend had already touched. The physical peak is set by the frontend set (LEX through CHECK, the keeps, about 150 MB baseline). Step 1 alone crashed under -PoisonCompact (a bivy bag merged after LOWER's compact), fixed in 21319; the CL descriptions carry every measurement. The -EscapeCheck crash is filed, COMPILER-45. NEXT: measure DESUGAR's 49 MB before choosing. Both collectors from the blocker LANDED main 21381 (red 21379): `$tSemantic` fires on `IR/Lowering.codex`, `-Internal` scopes off `p4 opened` unioned with the stream's diff2 against main (the batch-scoping fix), and `compare-codex-semantic.ps1` names an empty source body instead of a body mismatch, each verified with a control. Then NOW (2026-09-01): COMPILER-36, the trapping integer default on x86-64 (root's ruling and red's sizing are on the row; measure the wrap-by-design foreword sweep FIRST, in scratch, before any seed moves). **GO (Damian, 2026-09-02): "yes do it"; red is on the scratch measurement.** **MULTIPLY UNIT LANDED main 21676 + 21678 (red 21675/21677, seed E0042890, gate 776 s green at 21663, one-pass fixed point, self-verified): plain Integer `*` traps on overflow on x86-64, the exact i64 `wrapping` band is accepted and reaches the op by the left operand, the wire spells `mul-int-wrapping`, `Foreword chapter Wrap64` carries `w64-mul`, and 22 wrap-by-design sites across the compiler, foreword, os and apps are declared. Two red gates on the way (Hamt djb2 in the BVT, the decimal-literal accumulator in a refusal) and one broken window on main between 21676 and 21678 (the copy-up dropped three pending adds; `check-seed-orphans` had said so) are on the compiler-backlog row. NEXT: the add and sub units, same shape; the plug-side gap is `plugs-backlog.md` 2.21; the spelling sugar (`Integer wrapping`) stays Damian's.** **ADD/SUB UNIT LANDED 2026-09-02 (red; seed F185CB2E, 3,191,641 B, signed, self-verified; -Internal green 599 s, one-pass fixed point on the merged head at main 21784, BVT 408 lines 0 drift): same shape, `add-int-wrapping`/`sub-int-wrapping` on the wire, `Wrap64` `w64-add`/`w64-sub` with `Sha512` citing it, fixture `codex/test/ops/int-add-wrapping`; the stage-2 self-compile needed no new annotation and stage 2 == stage 3 in one pass; the account is the compiler-backlog row. DESUGAR MEASURED with the 21675 phase-trace instrument at seed E0042890: desugar deck 50,081,624 B, DESUGAR-KEEP used 54,452,008 B (of which the desugar copy is the deck figure), desugar bivy 309,432 B; the "49 MB" on the row is 50.1 MB.** Steve Howell's queue is absorbed: nine of the ten open PRs (99-114) are on main with credit and were closed with a comment at the Update 54 push; PR 100 is NOT landed (its .expected encodes x86's NaN/overflow answers, which arm64 and riscv64 saturate; plugs-backlog 2.07) and stays open with a status comment. Still red's from that queue: COMPILER-36 (issue 109); nothing else from that queue (the `-Internal` scoping fix landed main 21381). Not red's: COMPILER-37 (issue 106, unowned), issue 102 (COMPILER-32, blu), issue 110 and PR 112's `wasm-exports` (ruled to reek) | Sittings, and the diag step-2 lifts red owes (xHCI truth, keyboard, MSC align, largest GOP mode + `SetMode`); the Review pane stages 3+ (`works-backlog.md` WORKS-44, WORKS-46); identity stage 4 (trust-root write, passphrase change, `IDENTITY.DAT` on the ESP); COMPILER-23 re-presentation when Damian calls; `BatteryReorg.md` step 6 | releases, personally and end to end; `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; the 4.3 seed hash check runs BEFORE build-complete, not after the report |
| **root** | **NOW (Damian, 2026-09-02 evening, direct): THE RELEASE of main under the `/release` protocol, full gates, run by root** (this supersedes "releases are red's" for this release; red is down). Head freezes at reek's site landing; non-essential seed CLs (blu's COMPILER-42 landing) hold for MAIN OPEN after the push. Fleet merge-down ran 22127-22130 (blu, fester, red, val); reek merges itself. Then **commander**: the register, the dispatches, the pulse. **NIGHT SHIFT 2026-09-01 20:40-23:20 HANDED OFF, whole fleet down at Damian's order (each lane at or before 75% context).** Head seed BE8B04B5 (main 21462, blu COMPILER-16), five seed moves tonight, every one a depot-verified one-pass fixed point; ~55 main CLs. Landed: the plug runner (21440, Damian's "add the runner"), tCompiler scoped to compiler SOURCE (21475), memory stage 2 (host-RAM claim RETRACTED 21373: guest deck headroom only), COMPILER-43/33/12/16 (18/19 found already done), SMP teardown, VM admission, Prism 2c, FAT32 long names, memory-contract runner, plugs 2.15, arcade 27 of 34 playable, harness census (only extract-x86-output was dead). Box policy measured: ONE -Internal gate at a time (two light gates overlapped cost 405 s against 80-105 alone); gates launch from a SHORT Start-Process call and the lane polls the log; lanes end their turn after reporting, so the commander bumps on every idle notice. Resume recipe and Damian's morning queue: root's memory file. Renode riscv/arm64 stays BANNED until Damian lifts it | DiagnosticStick composition (`DiagnosticStick.md`; step-2 lifts by the lane that flew the probe); `ComplianceEvidence.md` (FactStore ingestion pending); `HardwareAbstractionLayer.md` open question 5, hardware crypto dispatch: unit 1 is built (`VirtioRng`, main 18963), steps 2 and 3 are BLOCKED on a board crypto manual `docs/Reference` does not hold (red's ruling, 2026-08-21); OracleCloudArm64 DEFERRED | `build/boot/diag/**`; plugs 1.34 is rulings queue 10; the HAL carries its full designed surface |

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

- **THREE TEST CHAPTERS ARE RED AT HEAD, and they are red for their OWNERS
  rather than for whoever gates next (blu, 2026-09-02).** `codex/test/lib/
  numeric-test`, `codex/test/apps/spark-noise-test` and `codex/test/apps/
  safetensors-bounds-guard` each emit far MORE than their `.expected`:
  measured 910 chars against 96, 900 against 112, and 1348 against 479.
  **Not truncation and not a sidecar gap.** Their expected files look stale
  against what the code now prints. **The discriminator is on the record so
  nobody re-runs it: each was compiled with the DEPOT seed `F134E3E7`, the
  unmodified compiler, and each fails identically**, so this is head's state
  and not any lane's change. They were invisible until the run phase began
  honouring sidecars (fester, main 21999 and 22037): that work did not cause
  them, it revealed them, along with 77 others it then fixed. The owners
  re-baseline or fix; a lane gating over them should cite this row rather
  than re-derive it.

- **`docs/Designs/Active/OS/OracleCloudArm64.md`: DEFERRED by Damian
  2026-08-18.** Deferred with it: `codex/os/net/Arm64NetIO.codex` is a full
  twin of the x86 send path and carries NEITHER the checked-send fix NOR the
  NETIO drain cut (still `arm64-net-io-max-ticks` 500). Whoever lifts the
  deferral inherits both as the first item; an arm64 TCP send that hangs or
  truncates before then is this row, not a new defect.
- **Six `-Internal` phases have their runner under `build/` and do not
  trigger on it** (`jonquil`, `plug-binary`, `cross-smoke`, `plug-smoke`,
  `app-sweep`, `sem-equiv`; reek, 2026-08-25; PARKED, not claimed).
  Blanket-widening `$tBuild` would make `-Internal` the full gate for any
  build change, so it is a question, not a build (L-LESS). Interim rule:
  whoever edits one of those six harnesses runs its phase by hand and says
  so. Table with costs: `docs/Designs/Active/Build/Build.md`.
  **RULED (Damian, 2026-09-01 late, "add the runner") and LANDED main 21440
  (reek 21438): the graded SET was the gap, not the trigger; both phases now
  append the plug directories in the change, proven by a sabotaged `qt` going
  red and a no-plug control staying deferred.** The ruling was: a
  change under `codex/plugs/<plug>/**` must IMPLICATE `plug-binary` and
  `plug-smoke` for that plug in `-Internal`. Measured the same hour (fester,
  2.15 arc): a gate over four plug CLs went green in 437 s having deferred
  both phases as "nothing implicated" and tested no plug at all. Scope is the
  plug SOURCE trigger only, not the six harness-edit triggers above (those
  stay the question they were). The generator is `codex/build/BuildScript.codex`
  (reek's claim); read `Build.md` first, the shipped script is hand-maintained
  and drifts the other way. Prove it with a sabotaged plug: the phase must
  run and go red on the sabotage and stay deferred on a docs-only CL.
- **"Four harnesses are dead on every box" is STALE, and one of them is
  catching a live defect** (row from reek 2026-08-24; re-measured by fester
  2026-09-01). The original account is right about history and wrong about
  today. `Start-VmRun` no longer routes by host preference: it is QEMU-only
  now, refuses in one named line when no QEMU is found, and the phantom
  `-data-port`/`-ctrl-port` are gone from it (only a comment recording them
  remains, and that comment's "ignores an unknown flag in silence" is itself
  stale since codex-vm exits 2). **QEMU 11.1.0 IS installed on this box**,
  `D:\qemu-11.1.0\qemu-system-x86_64.exe`, and `vm-config.ps1`'s own
  discovery resolves it. So "unrunnable wherever codex-vm is present" no
  longer holds, and delete is NOT the answer for three of the four.

  **`test-disk-compile` RUNS END TO END. DIAGNOSED 2026-09-02 (reek), and it
  was never a DISK-mode defect.** Two separate faults, both now measured.

  The harness broke its read loop ON the `CODEGEN-ERRORS` line while the
  compiler prints the diagnostics AFTER it, so the only text naming the
  failure was emitted and discarded. Fixed reek 21506, generator and script,
  0 drift, with a before/after control. The four EMPTY lines were never the
  missing text: `print-codegen-error-header` prints an empty line for a phase
  with zero errors, so they are four clean phases.

  The recovered line was `CDX2040: Unresolved call to 'opening'`, and the
  cause was upstream of the compiler entirely: the img plug embedded
  `SOURCE.SRC` through one spurious CCE conversion, so every FAT16 image
  built with `-Source` carried a corrupted source. Fixed reek 21526; the
  bytes at cluster 178 are now byte-identical to the source file. Account and
  evidence: `codex/plugs/plugs-backlog.md` 2.19.

  **DISK MODE IS CORRECT AND THE COMPILE SUCCEEDS.** Measured 2026-09-02 off
  the image the run leaves behind: `OUT.CDX` is 89515 bytes, `OUT.TXT` reads
  `OK OUT.CDX 89515`, and the extracted `OUT.CDX` is BYTE-IDENTICAL to the
  host-built CDX from the same compiler and source.

  **CLOSED. `test-disk-compile` PASSES, for the first time ever** (reek 21550).
  The last fault was the harness asking for a binary DISK mode never sends:
  `emit-binary-tail` (`codex/compiler/opening.codex:1642`) prints `SIZE:` and
  then branches, writing `OUT.CDX` to the volume rather than streaming it.
  Step 2 now reads on to the `DISK-OUT:` line and step 2b extracts `OUT.CDX`
  from the image host-side, with the guest's declared `SIZE:` as the oracle.
  Full loop green: source to CDX to PE to FAT16 image to boot to guest compile
  to extract to run, printing 12.

  The row above is kept because the three faults were independent and each hid
  the next: a harness that discarded the diagnostic, a plug that corrupted the
  source, and a harness contract that asked for the wrong artifact. Nothing was
  ever wrong with DISK mode or the compiler.

  **`extract-x86-output` is DELETED (2026-09-01).** It existed to send an
  `ELF` mode header, and `compile-plain` (`codex/compiler/opening.codex`)
  accepts exactly `CDX`, `IR-UNI`, `IR-CCE`, `MEASURE`, `TEXT`, refusing the
  rest by name since main 20534. There was no ELF mode to revive it against
  and no caller in the tree. Reviving the elf plug means WRITING AN ELF
  EMITTER, not restoring a script, and `codex/plugs/elf/run.ps1` now says so
  where it used to tell the reader to run the deleted file. That instruction
  was the one live consumer the grep found and it is corrected in the same
  CL (L-PUBLISHED: grep for the consumer before retiring the thing).

  Four prose references remain and are deliberately NOT edited:
  `codex/compiler/opening.codex` (compiler source, so editing prose there is
  seed-affecting and wants a token, while its CODE is unaffected),
  `docs/OperatorsManual.md`, `plugs-backlog` 1.41 and its neighbours (reek's
  register), and `LESSONS.md` L-ACCEPTED, which names this file as the
  evidence for its compiler half. That evidence stays reachable through
  Perforce, which is where this tree keeps history by its own rule.

  **`sim-test` and `gdb-watchpoint` were RUN 2026-09-01 and both are alive.
  Neither should be deleted.**

  `sim-test` works and its SUBJECT does not. It refuses in one named line
  until its baseline is built (`Compile SimBaseline first: ...`), which is a
  harness behaving correctly, and building that baseline is what fails:
  `apps/games/codexmagic/Simulate.codex:97` and `:98` call
  `apply-screw-fix` and `apply-flood-fix`, neither defined anywhere, so
  CodexMagic does not compile. That is an app defect standing in front of a
  working driver, and it belongs to whoever owns the games quire, not to this
  row. `Token.codex` also shadows `rng-new` and `rng-next` from
  `CodexMagic--GameRules` (CDX3006), which is separate and may be deliberate.

  `gdb-watchpoint` works mechanically, end to end. It refuses without a
  target (`Specify -Watch, -Break, or -ReadWatch with an address`), and given
  one it boots QEMU in TCG with a GDB stub on :1234, attaches WSL gdb, sets
  the hardware breakpoint and reports `=== STOPPED at hbreak ===`. The run
  ended `READY not received within 120s` with the guest exiting normally,
  which is MY invocation and not the harness: the address came from
  `build/output/Sut.map`, the compiler's map, which is not the kernel that
  boot ran, so nothing was ever going to hit it. A real session needs an
  address from the booted kernel's own map and probably more than 120 s under
  TCG. Unproven as a debugging SESSION, proven as a path.

  So of the four, exactly one is dead.

  `tools/test-codex-vm.ps1` is a FIFTH in the same family that this row never
  named (`OperatorsManual.md` does): it passes the two phantom flags directly
  AND invokes `codex.build\sample-compile-selfhost.ps1`, a path that no longer
  exists. `tools/codex-vm.c` is a claimed file (claims table); announce.
- **COMPILER-23 (compiler-backlog): RULED 2026-09-02 (Damian, via red) repair 2 YES, and repair 2 HAD ALREADY LANDED at CL 19813 on 2026-08-26.** No build was needed and none was made. Verified at head by blu 2026-09-02, not read off the CL: the guard `codex/test/ops/unicode-bytes-roundtrip` matches its `.expected` on all 14 lines against depot seed `BE8B04B5`, tier 2 included, and a sabotage of the tier-2 lead chosen by arithmetic against the real inputs proves the guard is not vacuous. The row body never recorded its own landing, which is why it was re-presented and re-ruled a week later; it now opens with the closure. Defects A, B, (i) and (ii) are all gone.
  **`__cce_print` is ANSWERED (reek, 2026-09-02): DEAD WEIGHT, and not in any
  binary.** Its emitter `emit-cce-to-utf8-helper`
  (`X86_64Helpers.codex:926..1104`, 179 lines) is called by nothing, so the
  symbol is never emitted: 0 of 5620 symbols in `Sut.map`, against
  `__cce_print_multi`, which is emitted and called. That also corrects this
  row's own analysis, which reads `__cce_print` as the live multi-byte print
  path reading unconcatenated rodata; the live path is `__cce_print_multi` from
  a different helper, which is why the corruption that analysis predicts was
  never observed. Deletion recommended and NOT taken: compiler source is
  seed-affecting and a cleanup that moves the seed is Damian's call. Account:
  `compiler-backlog.md`, the COMPILER-23 residue section.
  **What is still open under this row and is NOT the CCE builtin pair,
  UNOWNED:** the two UEFI print loops fixed under COMPILER-21, which stay ungated
  because nothing in `codex/test` runs under `-uefi` (L-NOGATE). The
  remaining CORE-8 residue is the `from-unicode` answering -1 call-site
  policy, in `codex/foreword/core/core-backlog.md`.
- **A battery batch can hand every test another test's output: CONTAINED
  (red, 2026-08-28).** A dropped-bytes report or a short stream now
  invalidates the whole batch, and `Get-FailHint` names misattribution.
  **The WHICH-LAYER question is CLOSED for any future occurrence** (fester,
  2026-09-02): since 21584 codex-vm's writer names its own shortfall with one
  of five causes, so a short stream with a silent `.err` now eliminates the
  writer instead of implicating it, and the whole chain is reproducible with
  `CODEX_VM_SHORT_WRITE_AT` (measured: writer reports, batch warns, 3 of 3
  members exit 99, `BATCH INVALIDATED`). The two historical events cannot be
  attributed retrospectively and nobody should try. Residue, UNOWNED and
  small: the guest-serial and blit causes have no injection knob, so only the
  host-writer arm of the census is proven. Record:
  `ExaminersAssay.md` "The batch stream can lose bytes".
- **VM admission (`Get-VmAdmittedSlots`, `vm-config.ps1`): wired at the
  fan-outs of `deck-headroom` and the battery's two phases** (fester,
  2026-08-28). The budget is 1100 MB per guest against 1024 MB host reserve
  (`-mem` is a ceiling, not footprint; L-REQUEST); re-measure on a
  different box (L-COUNT). Left open, UNOWNED: `test-cross-batch`'s run
  phase dies at 466 subjects while a filtered slice runs clean (reek,
  2026-08-28); the helper now reserves the remaining growth of every live codex-vm or Renode process and names them in its line (red, main 21434); the battery half is unproven end to end until Damian runs one.
  A run launched as a background shell dies with a fleet session restart
  and reads as a load-independent kill; launch detached
  (`OperatorsManual.md`).
- **A host crash after complete output still PASSES the harness** (red,
  2026-08-22; UNOWNED). The emulator half of this row LANDED main 21429
  (red 21410): codex-vm deleted its partition and freed guest memory while
  the application-processor threads were still running, and an AP wrote
  serial bytes during the output dump; the APs are now stopped, cancelled,
  joined and their VPs deleted right after the main loop. Measured with
  `smp-halt` at -smp 4, twenty runs four at a time: 3 host faults before, 0
  in 60 after, and both SMP tests match through `test-run.ps1 -Smp 4`.
  What remains is the instrument: `test-run.ps1` reads the output file and
  never the emulator's exit code, so a codex-vm that faults (exit 0xC0DE)
  or dies of heap corruption (0xC0000374) after writing complete output
  reads as a pass. The fix is in `testrunScript.codex` (generated script;
  read `Build.md` first): refuse on those two exit codes by name.
- **FAT32 long names: the library has them, the img plug does not.**
  `Fat32.codex` READS them (fester, 21446) and WRITES them (fester, main
  21520: a VFAT run plus a searched 8.3 alias, placed as one consecutive slot
  run inside one cluster, growing the directory when no cluster holds a long
  enough run). Graded by `codex/test/fat32-longwrite`, whose every arm is a
  round trip through the reader that `codex/test/fat32-longname` grades
  independently. The reader's CLUSTER-boundary threading, which had no arm
  since it landed at 21446, is graded by `codex/test/fat32-cluster-lfn`
  (21539); `fat32-longname`'s prose said that arm needed a writer and was
  wrong, since growing a root by hand is two `fat32-set-fat-entry` calls, and
  that paragraph is corrected. **The `Fat32Writer.codex` item this row used to
  name was NOT work and is struck**: every one of its twelve
  `fat32-write-dir-entry` calls passes an eleven-character LITERAL, it builds
  one fixed ESP (`EFI/BOOT/BOOTX64.EFI`, `SEED/CODEX.CDX`), and it has no path
  that can produce a name needing a run. `Fat16Writer.codex` is the same shape,
  and its `source` argument is one Text blob written as a single `SOURCE.SRC`,
  not a tree. Neither plug writer lacks long names in any sense that describes
  work (L-CAPABILITY). The row had conflated them with the 2026-08-28 FAT16
  work, which landed in `codex/foreword/core/Fat16.codex` and in
  `build/build-img.ps1`, in neither plug.
  That left one real gap and it is CLOSED: `build-img.ps1 -Fat32` writes `SRC/`
  chapters under their own long names, and nothing graded `Fat32.codex` reading
  a HOST-written FAT32 image, so the two implementations had never been told
  apart on this width (L-ORACLE). `codex/test/fat32-img-longname` does it, on
  the same six discriminating name shapes `img-longname` uses. Sabotaging the
  HOST checksum alone moves all six arms and leaves `SRC` itself, an 8.3 name
  needing no run, standing. Its 38.6 MB sidecar carries its own REGENERATION
  COMMAND in the chapter's prose, which the FAT16 fixture does not and which is
  why that one cannot be rebuilt. **The FAT32 long-name row is DONE.**

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

6. **Free-vs-solved wire marker: OPEN, WITH STEVE HOWELL (Damian,
   2026-08-28), and it BLOCKS NOTHING.** Damian's read is ADD the marker
   with Steve's zero-sized-default rider; the final call rides Steve's
   answer. No lane implements until it comes back. When ruled it is
   seed-affecting, token per CL, priced at one lane-day (the 20327 sweep);
   scope and evidence: COMPILER-32/33, the ir-fidelity census, issue 94.

**PARKED, customer work, NOT REPORTED (Damian, 2026-09-02 10:28: "stop
reporting on it until I bring it up again"):** 16, ProductBuilder stage 6
(`codex/product/product-backlog.md` 6). Not drawable by any lane; not in any
status, pulse or rulings list until Damian raises it.

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

- **COMPILER-30 ErrorTy: SPLIT NOW** (Damian, 2026-08-27). `ErrorTy` is
  reserved for genuine type failures; a distinct no-expectation marker takes
  the sentinel meaning. Every plug inherits; Steve's PR rebases. Open work,
  unowned; row in `codex/compiler/compiler-backlog.md`.
- **Heavy-pane stranding: option D, FIX THE ALLOCATOR** (Damian,
  2026-08-27). val's campaign; acceptance is the reopen-after-buried-close
  row (+2,465,912 today) falling toward zero, and the frontier table in
  `ShellRefinement.md` 6.4 carries it.
- **Prism's product scope** (Damian, 2026-08-24): compile/transpile on the
  fly; the pre-baked IR path goes. `apps/prism/prism-backlog.md` is the
  register.
- **COMPILER-18: DONE, and the ruled design is NOT what shipped** (ruled
  Damian 2026-08-24; closed and re-measured blu 2026-09-01). The ruling was
  "the partial-application closure gains a remaining-arity word". No such
  word was built: the closure is still `(1 + num-captures) * 8` bytes and
  the arity is carried by a TRAMPOLINE FAMILY instead, which is why
  L-PEROBJECT's CDX9002 never reached main -- the word would have doubled a
  zero-capture partial application at ~20 million allocations per
  self-compile. Guarded by `codex/test/ops/closure-under-apply`, 5 of 5
  today. Row in `codex/compiler/compiler-backlog.md`.
- **plugs 1.34, the ARM64 MMIO boundary: (a)**, gate the MMIO window in the
  effect system; (b), a real EL0 boundary, is a different project.
  Seed-affecting, token. Until it lands the ARM64 capability gate covers the
  `block-*` builtins only. (red, routed from root.)
- **plugs 1.57: the Rulebook's over-application rule binds every plug that
  keeps an arity map** (red, 2026-08-24). The java half stands as ruled;
  the riscv wiring named in the row is INERT and the real miscompile site is
  unnamed, so reek hunts it from the reproducer rather than re-wiring.
  Account: `plugs-backlog.md` 1.57.
- **A ping goes unanswered, deliberately** (red, 2026-08-20): no production
  caller for `icmp-parse` until something needs one. (Track B, blu.)
- **The rechecker keeps deriving type-variable instantiation itself** (red,
  2026-08-20); the compiler does not emit it. That is the fork's whole value
  (L-CAPABILITY-LOST); the abstentions are the price. (Track C, val.)
- **`check-vm-differential` retries once, only when an arm produced NO
  BINARY**; "hosts disagree" is never retried. (red; ruled, NOT BUILT as of
  2026-08-31: the script has no retry.)
- **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
  (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`,
  `.cross-refusal`, `.no-cross`, `.vmargs`) and warns on everything else.
  (red; ruled, NOT BUILT as of 2026-08-31: `Show-Untracked` still only
  warns.)
- **sem-equiv and text-stage1 run WHENEVER THE CORE RUNS** (Damian's ruling
  2026-09-02, fester); the 2026-08-28 `opening.codex` widening and its
  residue, that any other compiler chapter could break semantic equivalence
  unseen, are both closed. Measured 34.5 s + 59.6 s.
- **5** (2026-08-16): zig 0.16.0, at `D:\zig-0.16.0`.

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
| `apps/works/GopWeb.codex` and `ds` cells 244 and 248 | val, 2026-08-28, WORKS-48. 244 is a pointer to the request-log ring, 248 the state word, both written in `desk-run` before the base mark; the `WebMux` lives in `DeskApps`; `codex/os/net/WebServer.codex` is read, not changed; 252 stays free |
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

**A GATE RUNS ONLY THE STEPS THE CHANGE CAN AFFECT (Damian, 2026-09-02,
06:34: "we need to only do build steps that are necessary. we don't need
to perform full builds to test an app, run the bvt on the compiler, and
do an app sweep to build an app. that is absurd").** Measured the same
morning on val's apps-only poker CL: `-Internal` took 615 s of which the
seed fixed-point core and BVT were ~170 s of phase time, the strided
app-sweep 90 s, and 311 s sat in NO phase line at all. The rule: the
fixed-point core and the BVT run only when a compiler, foreword, seed or
build path moves; app-sweep is cite-scoped like test-compile, and strides
only on a compiler move; the unaccounted 311 s is measured and named.
Sabotage control: a compiler CL still runs the core. **LANDED main 21620
(fester): behind-main REFUSES, apps-only 10 s against 172 s, stale-kernel
control clean; the app-sweep subject selector is the open half (fester).
R-GATE in CLAUDE.md carries the quoting rule (21628).**

**SUPERSEDED 2026-09-02 15:55 (Damian, after the fleet was stopped at
15:48: "the token is for synchronizing MAIN not the BOX. the Fleet
Commander is how you synchronize on the BOX"; and 15:52: "-Internal is
banned. it shaln't be run"). The rule is in CLAUDE.md R-GATE: the token
lands an already-proven seed CL on main in ~90 s with no gate inside it;
every guest-starting run is asked of root by one message and granted
FIFO, the lane ending its turn while it waits; `-Internal` is not run,
verification is the touched tests one at a time plus, for a seed CL, the
scratch fixed point and the BVT as granted runs; each lane audits and
kills its own leftovers at every handoff and after any killed run. The
paragraphs below are the history of the afternoon's floor rule, kept for
the measurements; none of them binds.**

**A LANE GRANTS ITSELF THE BOX ON THE FLOOR (Damian, 2026-09-02 12:50: "we
need to get this logjam unjammed and stay unjammed").** Measured that
morning: five lanes idle at once, each single-guest run and each
`-Internal` gate waiting on a root GO-BOX message, two turn latencies per
hop, on a box that sat at 6 GiB free. The rule now (CLAUDE.md R-GATE):
read free memory, launch above 3 GiB, hold below it; root grants only a
`-Jobs` above 1 harness run, a Renode arm, and the token. **The launch sits INSIDE the branch that tests the reading:
`if ($freeGiB -gt 3) { <launch> } else { 'HOLD' }`, so a low reading cannot
launch.** "In the same call" was the earlier wording and three lanes read
it as print-then-launch in one block (reek 12:59, val 13:38, fester 15:36,
each launching under the floor after printing a number below it); a
reading that is printed and not branched on is a report, not a guard. **ONE `-Internal` GATE AT A TIME** (13:20: red's token
proof and fester's off-token gate both entered their parallel phase, four
guests each, and the box read 0.28 GiB free; fester's was stopped to
protect the proof). A gate is self-granted when no other gate's
`build.ps1 -Internal` process is up; single-guest compiles and runs
still ride the floor beside it. **A build that spawns guests for minutes
(the landing-page assembly, a plug rebuild, a 34-module game rebuild) is
a multi-guest run: it runs only when no gate is up, because the floor
check is taken once at launch and the gate's parallel phase arrives
later** (reek 15:38: a landing assembly launched at 5.4 GiB free put the
box at 1.3 GiB under blu's gate and reek killed its own run). **And no
watcher process of any kind (Damian, 15:40): the Bash tool is banned
(R-SHELL), and `tail -f | grep` or `until grep; do sleep; done` in any
shell leaves a process that polls a log forever once the build it
watched is killed. root killed 21 of them at 15:40, from 10:46 onward,
spanning three lanes' gate windows; "git for windows hangs" was those.** **Count gates by a pwsh whose command
line is `-File <path>build.ps1 -Internal` AND contains no `-Command`,
never by matching a substring: a slot waiter's own `-Command` text
carries the launch line verbatim, `-File` included, so a substring match
counts the waiter as a gate** (fester twice, val once, and root once at
14:45 with a pattern that had only the first half; val's waiter would
have given up at 45 minutes on a clear box). And ONE waiter per lane: two
alive race to launch two gates the moment the box clears. Nobody waits on
another lane except for the token and for a red at head. A gate run
UNDER THE TOKEN is waited on in the FOREGROUND: launch it, then
`Wait-Process -Id <pid> -Timeout 540` in repeated tool calls (600,000 ms
timeout each) until it exits, then read the log; never detached, and not
the harness `run_in_background` either (red, 13:11: the harness killed
the background task at ~3 minutes while the gate kept running, so the
exit re-invoked nobody). Messages queue and drain between the calls.

**WE DO NOT HOLD OUR WORK FOR AN EXTERNAL CONTRIBUTOR (Damian, 2026-09-02
10:35, in substance: "we don't hold our work for external contributors,
except where it is isolated to an ancillary subsystem ... the final form
must preference codex, not any other language. if it's not good for us,
well, let's see what it looks like before we judge").** Occasioned by
Steve Howell's 10:20 mail announcing a PR today that refactors
`opening.codex` so the zig ladder can consume it. Our compiler CLs land in
their queue order; his PR rebases onto main when it arrives. The reviewing
lane (red absorbs Steve's queue) judges it on one test: the result must be
shaped for Codex and its own consumers, never for a transpile target, and
a "mechanical" refactor of the entry chapter is read with suspicion until
the diff says otherwise. An ancillary subsystem with no fleet CL in flight
may wait for a contributor; the entry chapter is not one.
**The PR is 116 (10:47): a pure move of 69 of `opening.codex`'s 195
definitions into a new `Chapter: Compile Driver` (the phase functions,
deck arithmetic, flag readers), so a program that is its own entry point
can bundle the driver; +994/-972 across two files, no other change
claimed. REVIEWED by reek 2026-09-02 11:30 (reek's row has the detail):
NOT applicable as-is, cut from the Update 54 file so 18 of 891 moved
lines are stale and would revert red's memory stage 2 while gating green.
**OUR CUT IS DAMIAN'S (confirmed 11:40): `opening` becomes `codex-opening`
in place and a new near-empty entry chapter holds the one-line `opening`;
fester's shelf 21788, +74 bytes, one-pass fixed point; lands as a seed CL
under the token after fester's build batch and after blu.** That gives
Steve's harness the same thing his 69-definition move wanted (a chapter
with no `opening` in it that can be bundled) in two files. **PR 116 CLOSED 2026-09-02 11:45 (Damian's word; root
posted the two-file design as the comment, credit to Steve for naming
the seam, and mailed him the pointer). Steve is invited to name any gap
the two-file shape leaves on the PR thread.** Review it on the
diff: is it a pure move (concat line census, 195 = 126 + 69), does the
seam run one way, and does the chapter read as ours. Also from Steve
today, UNOWNED: issue 115, four deck-discipline dependencies declared
nowhere (Lexer cites nothing and rewinds the bivy needing `deck-record`
to be the real intrinsic; in a subset bundle it compiles clean and
page-faults, the fallback being a silent identity, L-BAILVALUE's shape);
goes to `codex/compiler/compiler-backlog.md` as a row.**

**A TEST RUNS WHEN IT IS LIKELY TO FAIL, NOT AS CEREMONY (Damian,
2026-09-02 10:25, in substance: "internal is something in between that
catches extended cases. all new tests belong in some runner bucket, but
that runner only gets run when it is somewhat likely to demonstrate an
error, not as a routine ceremony except at release. we need agility in
dev, we need to trust the release to catch unexpected shrapnel victims,
but test the bits we see before us at the time").** Three buckets and
nothing outside them: the BVT runs on every gate; `-Internal` RUNS, not
merely compiles, the test chapters whose source cites a chapter the CL
changed (the L-NOGATE repair, fester, after sem-equiv-always); `-All` is
the release net and is never run for dev. A new test that is in none of
the three is in no runner and is a defect in the CL that added it.
"sem-equiv ALWAYS" (10:20) reads under this rule as: whenever the core
runs, on any compiler chapter, not only `opening.codex`; a docs CL skips
the core and skips it.

**THE DOC-COUNT DRIFT CHECK LEAVES `-Internal` (Damian, 2026-09-02 07:27):
it runs on the FULL (release) gate only.** "blu is holding up the build
token to fix docs that will drift again before we release." fester lands
it as its own tiny CL; a lane does not fix counts to satisfy `-Internal`,
and a gate red only on counts lands once that CL is on main.

**LIST APPEND IS THE AGENTS' PROBLEM, NEVER AGAIN DAMIAN'S (Damian,
2026-09-02, verbatim in substance: "i refuse to make any more decisions
ever again about list-snoc, list-push, & and that shit").** No lane
re-presents `list-snoc`, `list-push`, `&` or any list-append semantics to
him; the fleet designs a compiler that does NOT go quadratic in memory on
list append, and the lane that changes anything on that path proves it
with a measurement. **The acceptance bar is absolute: if a change makes
even ONE line of code that is currently linear go quadratic, that change
has utterly failed.**

**CLOSED AND REFUSED, 2026-09-03 (Damian). COMPILER-42 IS NOT OPEN WORK AND
NOBODY IS TO REOPEN IT. There is a MORATORIUM on any agent discussing
`list-push`, `list-snoc`, `&` or list-append semantics again.** The ruling, in
his words: `list-push` in no case should ever copy the list, ever; and **if a
holder has an alias to a list that is going to mutate, that is dealt with AT
THE HOLDER** -- the one caller that needs the old value copies it there, where
one caller pays, instead of every call site paying through the allocator.

The in-place append is not a defect, it is the contract, and it is what makes
this compiler fit its memory budget: it took **more than 4 GB before appends
grew in place**. Everything built on the other reading has been ripped back
out of the seed (blu, 2026-09-03): the `list-push-copy` builtin,
`__list_snoc_copy`, the wasm `$list_push_copy`, and the whole `own-report` /
`own-rewrite` ownership apparatus. `codex/compiler/Types/Builtins.codex` now
states the contract at the table, because the ABSENT contract is what made the
behaviour read as a bug and pulled two agents into rebuilding it.

**THE COMPILER'S MEMORY CONTRACT (Damian, 2026-08-28): the FULL
SELF-COMPILE must complete within a 2 GB heap high-water mark, in BOTH text
and CDX modes.** A contract on the compiler, not a scheduling policy: a
self-compile needing more is a DEFECT to fix, never a reason to grow the
guests. Measured 2026-08-28, the CDX-mode self-compile peaks ~1.09 GB at
head, flat across the release interval. The gate's self-compile phases gain
the <= 2 GB assertion as the runner (fester, with the admission work); the
text-mode hwm is still to be read from the gate's fast path, since a
standalone `-Text` compile measures the serial channel rather than the
compiler.

**THE RUNNER IS BUILT AND THE TEXT NUMBER IS READ (fester, 2026-09-01):
1,170,074,911 bytes, 1116 MB, against the 2 GB ceiling.** `Invoke-BuildText`
already captured the per-phase allocator telemetry and `build.ps1` threw it
away one line later, so the reading costs no run at all: the high-water is
the MAX of the `WD:PHASE-*` positions, and `text-stage1` now refuses above
2 GB. Not max-minus-min: the positions are absolute and they DROP across the
run (h4-parse 1,170,074,911 then h5-desugar 92,313,911), so a span would
subtract away the reuse the contract exists to bound.

The reading was corroborated rather than assumed. Text mode at 1116 MB sits
within 3 per cent of the ~1.09 GB this row already records for CDX mode, two
independent modes of one compiler agreeing, and the arm was shown to FAIL
before it was believed: at a 1 GB ceiling it refuses the same capture, and
with the telemetry absent it refuses rather than passing, so a reader that
breaks cannot read as a green contract.

Left open, and it is the same hole as the plug phases (ruled 21384):
`-Internal` defers `text-stage1` unless the change implicates it, so this arm
does not fire on most work. Whether the contract wants a trigger of its own
is a decision, not a fix. The CDX-mode half of the contract has no arm yet
either; those phases keep no telemetry artifact to read.

**BATCH YOUR GATES (Damian, 2026-08-28).** Small CLs land on your DEV stream
with targeted tests only; `-Internal` runs once per work ARC or window, never
per one-line CL, and the batch copies up grouped (P-COPY1). Seed lands are
unchanged: token and gate at the land. Docs and registers need no gate. Until
the VM admission check lands, gate windows are coordinated through the
commander so heavyweight builds do not stack on the box.

Battery runs are Damian's (release proofs excepted, per the release skill).
Goldens stay parked during active GUI work. No new platform-wide register.
Prose about our own code is deleted in files you touch. The em-dash stays
banned. `-Jobs 8` on every parallel harness; heavy runs overlap while the
commander's sampler floor stays above 3 GiB free and hold below it,
whatever the count (Damian 2026-09-02 06:16, superseding the 2026-09-01
two-run allowance and the 2026-08-27 `-Jobs 4`); Renode arms run ALONE,
and every heavy run is granted by the commander (`CLAUDE.md` R-GATE
carries the measurements, ExaminersAssay "The parallelism default" the
history). Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (a wire emitter wants `-raw`, everything else
the plain name; `DevelopersGuide.md` "Effects and Act Blocks").

**Do not add a test to the gate or the battery on your own initiative; get
red's clearance first.** Damian, 2026-08-21: *"haphazardly adding tests to
the gates slows everyone down."* The cost is every agent's gate run for the
rest of the project. The rule is about the GATE and the BATTERY, not a ban
on arms: a `build/boot/diag-arm.ps1` row is pre-flight rehearsal and costs a
gate run nothing.

**A finding about someone else's project is not ours to publish.**
`//Codex/main` mirrors to public GitHub and GitLab, so anything landed there
is published; a bug report or critique about an external project is that
author's to receive first and goes in the depot at no path while it is
unpublished. Nor does a note saying one was withheld. In a design, state the
target's behaviour as a fact about the machine we build on ("TMOD is the
truncating remainder, measured"), not as a defect in somebody's document.
Perforce CL descriptions are not mirrored; only files are pushed.

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
