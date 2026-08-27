# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

*Pruned 2026-08-15 (1,100 lines to 670) and again 2026-08-18 by red at
Damian's direction. The second prune took the closed campaign accounts of
Track B's frame audit, Track C, Track D items 1-19, B4 steps 1-5 and the
per-agent lane rows, every one already recorded in the CL, the GitHubUpdate
for its cycle, or the doc named beside it (`HardwareSitting.md` for the
flights, `ExaminersAssay.md` for every guard, `VerifiedFormatParsing.md`
section 10 for the census, `DevelopersRulebook.md` "The repository wire").
If you are looking for how something was hunted, that is where it lives.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## THE UPDATE 51 RELEASE IS COMPLETE AND MAIN IS OPEN (2026-08-26, red)

Pushed public: github `7a6c5682` (master), gitlab the same commit (main);
same-night addendum mirror push `a0425e10` carrying PR 92 (absorbed and
closed), COMPILER-9's close, and the standalone Prism file, seed unchanged.
Correction push `012a9d2e` (2026-08-27, both mirrors): `a0425e10` claimed
PR 92's emitter and did not carry it (copy-up dropped it to a missed
noclobber refusal; found by Steve Howell); repaired main 19980, zig arm
graded first, account in GitHubUpdate51's addendum correction.
Original release line:
seed `C3181693` (2,917,073 bytes) unchanged since main 19889. All proofs
green against that seed: full gate 769.4 s every phase, battery `-Tier
all` 1,663 total 0 fail 50 skip (after `desk-cursor-arrow` was
re-baselined to the crisp pointer at 19947/19949 -- the erase invariant
held throughout), app sweep 265 clean 0 regressions, poison clean, DDC
witness HOLDS (both arms 2,917,073 bytes, 96 differing all inside the
signature region, 0 outside). Artifacts shipped: map 5,358/5,358 vs
embedded MAP1, img `12B7333C`, diag rebuilt + rehearsed full
(`682FA342`, shipping check OK). Doc counts 63/0.
`GitHubUpdate51.md` is the report; `GitHubUpdate52.md` is rotated and
carries the open items (Steve's PR 92 queued for absorption first).
**RED DECLARED MAIN OPEN 2026-08-26** -- seed-affecting copy-ups are
unblocked.

## The previous release (Update 50, superseded record)

Pushed public: github `8cc80685` (master), gitlab the same commit (main),
from main 19779, seed `C45E5825` unchanged from the freeze. All four
proofs green against that seed: full gate 757.4 s, battery `-Tier all`
1,658 total 0 fail 50 skip, app sweep exit 0, poison clean, and the DDC
witness HOLDS (both arms 2,922,230 bytes, 95 differing all inside the
signature region, 0 outside) after the csharp arm was repaired at
19775/19777 (lifted-lambda params emit as dynamic; the CS1977 map-list
sites route through `_Buf.dmap`; the failed copy-transform heuristic
stripped). Artifacts refreshed and shipped: map (5,351/5,351 vs embedded
MAP1), img (`6E1A9A59`), diag rebuilt and rehearsed at 46 arms
(`6F3124AC`, shipping check OK; the `b3-banklost` arm now derives its
expected note from the trail instead of a re-pinned literal). Steve
Howell's PRs 84-91 closed with credit. **RED DECLARED MAIN OPEN
2026-08-26** -- seed-affecting copy-ups are unblocked.

## Where we stand, in three sentences

The compiler is a hard fixed point of itself on bare metal, the Update 51
release is on the public mirrors (github `012a9d2e` after the addendum and
the PR 92 correction push, seed `C3181693`, headlined by the Cobblestone
landing page and Prism compiling in the tab), and the
compiler has booted the ASUS from bare UEFI, compiled its own source off the
stick and written it back byte-identical (A5). The trust audit is closed on
the whole compiler (diverse double-compiling, the `jonquil` runner, the
independent rechecker at one honest abstention), and Track D closed on
2026-08-16 with every reached parser of foreign bytes guarded and the latent
ones named. What is left is metal-gated (the network and the stick, which
advance at sittings), the plugs register (reek's close-out lane since
2026-08-18, with items lent to fester and blu), and the unowned defects at
the bottom of this file.

## THE WEEK: the Cobblestone rename campaign (Damian, 2026-08-24)

The project's public name becomes **the Cobblestone Project**: the GitHub
repo renames in place to `damiant3/Cobblestone`, the OS and every brand
surface outside the compiler becomes Cobblestone, and the language and
compiler stay Codex. Artifacts keep their names. One week, culminating in a
public push carrying the rename, the prism/REPL surface, and the parallax
home page. **The plan, the measured blast radius, the ruled boundary, and
every lane's assignment are in
`docs/Designs/Active/Marketing/Cobblestone.md`** -- read it before touching
any brand string; it names the traps (filename strings beside brand strings,
the width-fitted desk label, no bare-word sweeps, archives untouched).
Lane summary: red the repo rename, doc references, and the directory-side
workspace rename (SOLO, fleet stood down for it, Damian's timing); val the
on-device brand; reek or blu (first free) the web surfaces and live docs;
fester prism/REPL; Damian + red the home page (draft not yet located in
the depot; Damian points at it).

**val's on-device brand is DONE (main 19288 and 19293).** Desk chrome (top
bar, sidebar, taskbar entry, welcome text), the boot ceremony, both wizards,
the VGA banner, the ESP volume label at BOTH sites that write one, the
built-in webserver masthead, and the dev console including the
`install-boot-test` banner assert. Verified by capture at 1024 AND 1600 and
by a wizard boot, not by grep alone: the top bar and the welcome paragraph
were invisible to the string sweep and only the picture found them. Left
deliberately, by this campaign's own boundary rather than as a deferral:
`apps/cvmm/CvmmServer.codex`'s two lines are MOCK data, `apps/market`'s is
demo data, and `codex/test/apps` are fixtures.

## The network demo pair (Damian, 2026-08-24)

Two items, deliberately decoupled; the second is the near-term prize.

1. **A webserver app in the guios** (val, desk pane + wiring; blu consults
   on the net side after COMPILER-18). It serves BOTH protocols: HTTP and
   the browser app's own `codex://` wire (`apps/browser/PageFetcher.codex`
   / `DataChannel.codex` are the client side; the server speaks what they
   fetch). `apps/works/WebServer.codex` and the proven TCP stack are the
   parts; the work is the desk pane (start/stop, live request log) and the
   codex:// service. Bed first via codex-vm NAT port-forward; metal rides a
   future sitting. Originates in the works app; register
   `apps/works/works-backlog.md`, this row is the pointer.
2. **The crazy-boss page: the compiler running in WASM, building itself,
   in a static webpage** (fester; it is the reason the wasm OOM fix is in
   fester's lane). Compile the compiler through the wasm plug, load it in a
   browser, feed it its own source, show the build happening on the page.
   Static hosting anywhere is fine for this step. **Hosting that page from
   our own kernel and OS is EXPLICITLY a separate later step (Damian: it
   requires environment he does not have yet); do not couple item 1 to
   item 2's delivery.** Candidate centerpiece for the Cobblestone
   culminating push, beside the home page.

   **Where it actually stands, re-measured 2026-08-25 after plugs 1.68
   (fester). THE COMPILER'S OWN MODULE ASSEMBLES AND STARTS.** The plug
   emits the whole compiler, 9,468,360 chars of WAT from 16,316,110 bytes of
   IR, `wat2wasm` exits 0 with ZERO errors, and the 1,508,424-byte module
   instantiates under wasmtime and runs `_start`. Getting there took plugs
   1.60 (the builtins), 1.61 (a CCE layer), and 1.63 (partial application as
   a closure rather than an under-applied call); the undefined-name census is
   now zero and the 35-to-11 figure is superseded.

   **It reads its own source now** (plugs 1.64 and 1.65): `read-line` and
   `read-file-uni` are wired to `wasi_snapshot_preview1.fd_read`, one import
   surface both hosts satisfy, and the browser shim implements it beside the
   `fd_write` it already had. 1.65 needed NO compiler change: `read-file-uni`
   reads the WIRE on x86-64 and its name and `FileSystem.Read` row are a
   misnomer, so there was never a missing stream path.

   **THE SPIN IS FIXED** (fester, main 19524, plugs 1.69). Empty source,
   `Chapter: Hi` and a hello program all finish in 0.1 to 0.3 s where they
   ran forever. The cause was in the emitter and was neither of the two
   defects this row named on 2026-08-25: `$_rp`, `$_lp` and `$_tv` are ONE
   set of scratch locals per emitted function, so a construction nested
   inside an enclosing construction's field expression reset the enclosing
   object's pointer. In `tokenize-collect` the token list was written over
   the `EndOfFile` TAG, `is-done` never answered True, and
   `skip-to-next-line` looped. The guard is the wasm operand stack, two
   instructions, and the module grew 1.05 per cent.

   Plugs 1.68's two defects were real and are also fixed and graded (`==` on
   a constructor compared POINTERS where the oracle compares STRUCTURALLY;
   `show` on a Boolean rendered `1`/`0`), and the suite is 13 of 13 against
   x86-64. **Neither was the spin, and this row said one of them was.**

   **THE TRAP IS FIXED AND IT WAS A COMPILER DEFECT, NOT A WASM ONE**
   (fester, plugs 1.71). 1.70 called the backtrace impossible and pointed at
   the funcref path; the chain was real throughout. `emit-ir-cce` calls
   `compile-frontend-passes`, which stops after LOWER, while only
   `compile-frontend-cdx` runs LIFT, so **the IR-CCE wire has carried
   unlifted lambdas to every plug since it existed**. `WasmEmitter.codex:758`
   emitted a value-position `IrLambda` as its body alone with its parameters
   hoisted into the enclosing function as uninitialised locals, so `builtins`
   genuinely called the x86 register allocator with zeroed arguments. The fix
   runs the lifter the compiler already has, `IR/LambdaLifting.codex`, on the
   IR-CCE path. Measured: 153 lambdas in `Types/Builtins.codex` became
   funcref indices, inlined `emit_*_builtin` calls in `$builtins` went 113 to
   0, the table gained 334 defs and lost none, IR grew 0.39 per cent, and the
   module went from trapping at 0.06 s to running 21 s and exiting 0.

   **IT COMPILES. The compiler running in WebAssembly compiled a Codex
   program and its output is BYTE-IDENTICAL to the same compiler on x86-64**
   (fester, plugs 1.74). Same kernel source, same raw input, two targets:
   102 chars, SHA-256 `3BE25DB2..0F709CF4` from wasmtime and from codex-vm
   alike, in 0.26 s with a full phase trace to `h7-resolve`. Nine defects
   stood in the way and each hid the next; the table is in plugs 1.74 and
   every one has an arm in `codex/plugs/wasm/test/` graded both ways. The
   headline three: `phase-compact` was setting `heap_ptr` to zero at every
   phase boundary (that was the 922 MB of stdout), `list-set-at` was emitted
   as a copy when it is the in-place mutator the skip list links through, and
   `text-compare` was emitted as `text_eq` so every ordered lookup missed.

   **IT RECLAIMS. The wasm target has a deck and the self-compile hands
   memory back at every phase boundary** (fester, plugs 1.75). On the
   compiler's own 2,945,374-byte source the heap falls 1,377,816,869 to
   89,357,943 across the desugar boundary and tracks x86-64 within a few per
   cent at every phase, where before it only ever climbed. The deck is one
   cursor swapping between two saved positions, not a second region, and the
   half nothing had named was `deck-record`: this plug had no arm for it, so
   nothing ever allocated into the deck at all.

   **AND IT COMPILES A 252 KB REAL UNIT BYTE-IDENTICALLY** (plugs 1.76). The
   wasm plug's own bundled source, 252,035 bytes, emits 216,243 characters,
   SHA-256 `51CEBB12..1E65CC99` from wasmtime and from codex-vm alike. 1.74's
   headline was a 102-character program.

   That took fixing a defect 1.75 shipped: `__heap-advance` moved the cursor
   without growing linear memory, so a deck reservation was backed by no page
   and the first write into it faulted. **1.75 also published that the trap
   was an i32 address-space limit at 4 GiB, and that was wrong in every part**
   -- the fault was at 437 MB and the message says so on its own line. Arm
   `heap-advance-rt`, ablated against the shipped revision.

   **THE COMPILER COMPILES ITSELF IN WEBASSEMBLY, BYTE-IDENTICALLY TO
   x86-64** (fester, plugs 1.81, main 19763, pushed on Damian's direct
   instruction during the freeze). Its own 2.9 MB source, `TEXT decks=125`,
   wasmtime with a 16 MB wasm stack: **2,460,088 characters, SHA-256
   `B3491BE7..` from the wasm module and from codex-vm alike, zero
   diagnostics, five seconds on either target.** The unlocking mechanism was
   saturating closure application: `$clo_apply1` allocated an intermediate
   closure per argument, thirty million times per compile; the `$clo_applyN`
   family calls a bare funcref applied at exactly its arity straight through
   the table, and the wasm target now allocates LESS deck than x86 on a
   mid-size unit (lift 4.8x to 0.05x, resolve to 0.93x). Full account and
   the attribution ladder that found it: plugs 1.77 through 1.81.

   **THE PAGE SHIPPED AND WAS WITNESSED** (fester, plugs 1.83, main 19774,
   2026-08-25). Damian pressed the button and the compiler built itself in
   his browser: **2,460,178 characters in 19.0 s, hash `6F0A4122..`
   computed IN THE TAB, equal to the bare-metal anchor to all 64
   characters.** The anchor is computed at page build from the exact bytes
   served and is never hard-coded, so the byte-identity claim is measured
   rather than asserted. `codex/plugs/wasm/page/index.html` plus
   `build-page.ps1`; any static host serves it, and hosting it from our own
   kernel remains the separate later step above.

   **Plugs 1.14 no longer gates the page. It gates the WORKER.** The first
   click died at 240 emitted bytes with `Maximum call stack size exceeded`,
   and the discrimination is in plugs 1.83: tail calls are present, so
   everything 1.82 fixed stays fixed, and what dies is the emit spine's
   genuinely non-tail recursion. Measured in node worker_threads on the
   same module and input, 1 MB of stack fails at the first emitted bytes
   and 2 MB completes; wasmtime's `max-wasm-stack` bounds a leaner resource
   (Cranelift frames are a fraction of V8's), which is why the same depth
   passed there at the same setting. A browser worker's stack behaves like
   the 1 MB arm and cannot be enlarged, so the page tries the worker first
   for a responsive UI and retries on the main thread, whose stack is
   larger; that retry is itself the measurement in every browser that runs
   it.

   **CLOSED 2026-08-27 (fester, plugs 1.91, main 20013), and every part of
   the paragraph above naming the durable fix was wrong.** It was not
   compiler-side, not seed-affecting, and not `codex-emit-expr`'s tree
   descent, which is shallow and contributed six frames of 4,817. The stack
   was `emit-streaming-ir-defs` recursing once per definition because the
   tail-call walker had no `IrAct` arm, and the compiler's own streaming
   emitter is written in `act` blocks. Plug-side, no seed, no token. The
   page was rebuilt on it (anchor `5B4CADE2..`) and re-measured in node
   worker_threads at the browser's own numbers: **0.25 MB dies with nothing
   emitted, 0.5 MB and 1 MB and 2 MB all complete byte-identical to the
   bare-metal anchor**, against the previously shipped module dying at 1 MB
   as the control. The main-thread retry is now a fallback under half a
   megabyte rather than the path the self-compile depends on. What misaimed
   this for two days was reading "dies at 240 emitted bytes" as program
   text: those 240 bytes are the eight `WD:PHASE-` diagnostic lines, and the
   death is 86 per cent of the way through the output (L-MECHANISM).

   Residue, non-gating: the one-shot oracle-vector red recorded in fester
   19756. **That CL calls it "the shape of a VM launch that died" and
   `build/compile.ps1` does not support the reading** (fester, 2026-08-27):
   a launch producing NOTHING is caught at the emptiness check and writes a
   17-byte `FAIL: no output` log plus the captured stderr, so a 5-byte log
   cannot come from that branch and the VM must have emitted something. What
   it emitted is unknown and TWO mechanisms were proposed and killed by
   measurement, so nothing further is claimed here. The diagnostic gap is
   the concrete part: the exit-4 path taken when the output carries no
   `SIZE:` line writes neither stderr nor any of the output it did see, so
   the next occurrence will be exactly as opaque as this one. `compile.ps1`
   is generated (absent from `build/handwritten-scripts.txt`), so closing
   that gap is a generator-side change and wants its own item rather than a
   speculative edit to a script every lane's gate runs. **The riscv plug-source unit NO LONGER FAULTS** (plugs 1.94): it
   compiles under wasm byte-identically to x86-64, as does the larger arm64
   unit, so the "a large unit traps" class is closed at the capability. The
   mechanism is unattributed and the obvious suspect was refuted by
   ablation; the original 729,046-byte input was a build artifact and no
   longer exists, so that experiment is not reconstructable from the tree. **The `emit-wat-expr-at:746`
   silent-const-0 landmine is CLOSED** (plugs 1.92): it answered `0` for
   the whole remaining subexpression past depth 256, so 300 nested `let`s
   printed 44 against x86-64's 300 and assembled and ran clean. It refuses
   past 4096 now, which is above what the parser's own fuel will pass. **Parse deck is CLOSED** (main 20041): `list-insert-at`
   grew capacity to exactly `n + 1`, so its in-place path could never be
   taken and every insert copied the whole list. The published 2.4x was a
   growth rate read as a constant, rising to 18.70x at the compiler's own
   size while x86 stayed linear over the identical units; now 1.09x.

## Track A -- the stick is an OS

**Sittings are coordinated by red (Damian, 2026-08-18) and grouped, not
serial.** Every metal question below rides ONE diagnostic boot per sitting:
a lane with a metal question routes it to red with its arm and its expected
readings, red composes the boot (bank before you risk, L-BANK; rehearse the
exact bytes, L-REHEARSE), and Damian sits once. No lane proposes a flight of
its own. Standing metal questions today: the ASUS allocation grant (A8),
the largest GOP mode and `SetMode` (native GOP), the sink's 2.7 MB write
(WORKS-9), the e1000 ring (NIC-4), the TCP conversation (B3), ASDE (finding
4), and NIC-5 last.

- **The diagnostic stick (red, approved 2026-08-18): one image that
  detects the box and says what needs to happen.** The flown probes so far
  (`nicsitting`, `nicring`, `sinkladder`, `asdeflight`, the A8 allocation
  probe, the six-colour keyboard probe, the GOP mode arms) are each a
  one-question image built by hand. The design collects them into one
  template that a stranger can write to a stick and boot on a box we have
  never seen: enumerate firmware and devices, run every probe that applies
  with L-STATES failure states, bank the readings to the stick before any
  risky arm, and print a report that names what worked, what did not, and
  what to send us. Design: `docs/Designs/Active/OS/DiagnosticStick.md`
  (proposal first, then staged). **Steps 1, 3 (root, main 16822/16851) and 4
  (root, main 17203: `DIAG.RCP` in the image and bank, `diag.rehearsed`,
  `flash-usb -Rehearsed`, the UsersHandbook procedure, the release recipe)
  are landed; the stick FLEW 2026-08-18 (HardwareSitting.md). Step 2 lifts
  are per lane; step 5 is the grouped sitting.** The far end of the same road is a


  **THE I219 MEDIUM-DEATH HUNT IS PARKED (Damian, 2026-08-24, after
  sitting 12).** The production path is proven: boot, bring up once, talk
  TCP -- b3 ran 13/13 both-ends-verified on metal (card 19188) BEFORE the
  ladder ran. The death has only ever been observed inside the ladder's own
  mid-session re-reset, which the production path never does; the capability
  at stake is warm NIC recovery, which nothing queued needs. Sitting 12
  eliminated both named candidates (swflag, CTRL|SLU -- both run after the
  medium is already gone) and the death MOVED between same-shape ladders,
  so it is state-dependent with no mechanism named. **REVIVE only on
  production evidence: if a production session ever loses the medium, this
  block, the sitting cards (19188, sitting 11, sitting 10 in
  `HardwareSitting.md`), and the banked eliminations are the resume point,
  and the next arm is blu's to compose.** Everything below stands as the
  documented investigation; no lane draws from it while parked.
  **THE I219 CAMPAIGN record (red, 2026-08-21, Damian directed):
  `docs/Designs/Active/OS/I219IsNotAnE1000.md`.** `8086:15b8` is a PCH part,
  MAC in the PCH and the I219 as the PHY, and the family requires ULP disable,
  the `EXTCNF_CTRL` SWFLAG semaphore, K1 disable at 1 Gbps (the MAC STALLS
  without it), SMBus and LANPHYPC handling, and an LCD reload after PHY reset.
  **`E1000e.codex` had none of the eight on 2026-08-21 morning (grep, zero
  matches). By 14:19 the same day the table was CLOSED on main (blu, 18888,
  `I219IsNotAnE1000.md`):** K1 disable and the SWFLAG semaphore ship ON (both
  flew, sittings 9 and 10; the board refuses ownership, MNG held); LCD reload
  and ULP entry-disable are built and wired OFF behind the reset hang; the
  SMBus control is a read-only row, force half unbuilt; LANPHYPC is no
  procedure in either datasheet; **LTR is UNCITABLE** (every LTR field is
  zero in both datasheets, and the morning's "LTR 7" was a substring
  artefact). Three of the eight have a half no effort closes without a
  document nobody here has. It does NOT establish the cause and is not
  written up as if it did.
  **codex-vm modelled an e1000 and no bed arm could see any of it; by the
  same evening it models the I219 K1 stall gated on `STATUS.LU`, the
  semaphore with MNG held, `779.16`, GPRC at MAC acceptance, a late frame
  injection and a BOT device that dies at a named LBA** (reek, fester,
  2026-08-21), and every one
  of them stays green however wrong the driver is (L-OPTIONAL).
  Three lanes, none blocking another: blu the driver layer, root a `pch-state`
  stage painting to the glass plus the b3 fuel cap and the L-BANK fix, reek an
  I219 model in codex-vm built FROM THE SPEC and never from our driver.
  Standing rule for the campaign: one boot answers everything, no stage may
  end the run, the recorder outlives its subject, and nothing flies until the
  bed can express at least one of these failure modes.
  **SITTING 11 FLEW 2026-08-21 (red): THE ASUS TALKED TO THE DEV BOX.**
  Image 2C7030D7. At 17:01:11 the dev box's echo peer logged
  `192.168.6.200` sending `codex-diag-b3` and receiving it back unchanged,
  clean close: b3's whole exchange over the real I219. All seven
  `e1000-reset` operations ran and banked (`ctrl=0x100240` at the RST
  write, `settled=1`); **the sitting-10 hang did not reproduce** and stays
  open as state-dependent (L-GREEN). The medium died INSIDE the NIC's
  bring-up (bank ends at `step=rings-link`; glass said `BANK LOST AT STAGE
  15 pchk1`), so the candidate for every bank death since sitting 7 is now
  "xHCI/MSC dies inside b3's SECOND bring-up, in the SWFLAG acquire (2,000
  `EXTCNF_CTRL` writes with MNG held) or the `CTRL|SLU` write, neither of
  which nicinit ever did" (reek narrowed it: nicinit's bring-up was banked
  whole), L-CHANNEL; `pchk1`, `asde`,
  `sink` are glass-only. Full card in `HardwareSitting.md` "FLOWN
  2026-08-21: SITTING 11". **Sitting 12 (root):** a step paints
  `banked=n` when its note is refused; the listen after K1 (18948) flies.
  **SITTING 10 FLEW 2026-08-21 (red) AND NAMED THE HANG: it is inside
  `e1000-reset`, the first line of bring-up.** Image C6B1CEAC, b3's
  bring-up stepped and banked; the medium's last line is `stage=b3
  step=reset`. K1, the semaphore and `e1000-link-up` never ran and are
  exonerated for the hang. `nicinit` ran the identical seven operations at
  stage 12 in under 11 ms on the same boot, so the difference is the part's
  state (`nicring` leaves the receiver enabled; L-SUSPECT). Full card in
  `HardwareSitting.md` "FLOWN 2026-08-21: SITTING 10". **Routed:** blu,
  whether the receiver must be quiesced before the RST write and what the
  model does with RST on a live ring; root, the b3 clock note was lost to
  the step note (every note must append) and sitting 11 splits `reset`
  into its seven operations. `nicring` read quiet this time (`gp=0 pre=0`).
  **Composition queue, sitting 12 or later (root):** no stage LISTENS after
  the K1 write (reek, 2026-08-21: order is nicinit, nicring, b3, pchk1, asde,
  and nicinit never does the K1 step), so the board cannot yet show DD
  landing once K1 is disabled. A listen after b3's k1 step is the
  flight-shape change that proves the campaign's claim on metal; the bed
  proof meanwhile is reek's `-i219-k1-nvm 0` control beside `nic-invisible`.
  **SITTING 9 FLEW 2026-08-21 (red) and stopped inside `e1000-init`.** Image
  ECC60AF4, the first flight with the K1 write and SWFLAG ON. Bank whole
  through `nicring`, no b3 row; pulled after 14 min at `b3 -> bring-up`.
  Three findings, full card in `HardwareSitting.md` "FLOWN 2026-08-21:
  SITTING 9": **firmware holds MDIO/NVM ownership** (`extcnf=002c0089`, MNG
  bit set, so the semaphore can never be acquired and the driver writes K1
  anyway, `E1000e.codex:799`: blu); **the RING successor is answered,
  arrived-in-window on our ring and not written back** (`gp=1 rnbc=0
  ddset=0 aim match=y`: blu); **clock proven advancing at stage 12**
  (three budgets exact). The hang itself is not decidable from the medium.
  **Sitting 10 composition routed to root:** bring-up paints and banks its
  sub-steps, b3 banks the HPET at entry, and pchk1 (a read) runs BEFORE b3
  (the writer) so the blocked/taken word banks first (L-BANK). blu's
  `b3=short` arm rides the rebuild. Sink never ran (reek, WORKS-9 still
  open).
  **SITTING 6 FLEW 2026-08-21 (red).** Image 63EFDB8A, payload
  4e021f4b6b96c76b, rehearsed 33 of 33 both beds. Full record and every
  verbatim row in `HardwareSitting.md` "FLOWN 2026-08-21". Headlines:
  **the sink ladder returned a THRESHOLD on metal, 16 sectors, `done=4`**
  (reek); **`gopmode honoured` at 1920x1080 with 10 modes**, which closes the
  metal half of the GOP row below (red); and the NIC answered `wb=0 dd=0` with
  descriptor zero PRISTINE. **CORRECTED same day by blu: this does NOT settle
  it** -- `pre=3` is earlier windows only, so arrived-invisible and nothing-arrived BOTH stand. The discriminator is the stats row second GPRC and the dead bank ate it. `rdh-writable=y`
  answered on metal, closing one branch.
  **It did not complete: `b3` hung with no fuel cap and `asde` never ran**, so
  the ASDE question is still open and rides sitting 7. Two defects in
  `build/boot/diag/**` fell out and were root's.

  **(a) The `b3` fuel cap: DONE, main 18386.** The ceiling is an absolute poll
  count rather than one derived from the tick interval, and the head row now
  paints `poll-interval`, `spend`, `cap` and `clamped` so a run reports the
  ceiling it used. The cause was not what the first three readings of it said:
  every loop on that path terminates, and what was unbounded was the SIZE of the
  bound, because `net-io-max-polls` is the interval times 500 and an ordinary
  calibration can write 200,000,000 (blu). That is about 55 minutes for ONE wait
  at the 32.9 ns metal poll. blu clamped `net-io-max-polls` itself at main 18389
  with an arm, which is what bounds the send drain, since that starts at zero
  and runs once per 1400-byte chunk so no caller can cap it.

  **(b) The bank dying at `sink`: the LOSS is CLOSED (root, 2026-08-21); why
  sink refuses on the board is OPEN and is WORKS-9's question, not this one.**
  Sitting 7 measured the cost and it was the whole point of the flight:
  `DIAG.TXT` came back holding stages 1 to 8 and stopping, because sink at
  stage 9 killed the medium and the six stages after it reached only a
  photograph. Sink now EXECUTES last -- its number, its `DIAG.CFG` key and its
  glass row are unchanged, so nothing composed against the ladder moves -- and
  a labelled `before-deferred` summary is banked before it runs. `asde` stays
  in the main pass on purpose: there is no serial port on the ASUS, so a
  deferred `asde` would be one photograph, and its row is worth more banked
  than its execution is worth guaranteed. The stated cost is that an `asde`
  that wedges the box now takes the marker and sink's answer with it.
  Also fixed under it: `dg-paint-banklost` was handed the cell value (stage+1)
  and printed it as a stage, so the GLASS named the stage after the one that
  lost the bank while the file named the right one. Nothing in any bed reads
  the band, so that half rests on inspection rather than on an arm.

  **The bed DOES reproduce the loss now, and that corrects what this entry
  used to say.** "All 15 stages reach `DIAG.TXT`" is true only with no drop
  lever: a blanket `-usb-bot-drop` keyed into sink's DATA phase reproduces
  sitting 7 exactly, stages 1-8 and nothing after. What does not reproduce is
  the CAUSE -- metal refuses with `rty=1` (recovery itself refused) where the
  bed reaches `rty=2` -- so a board reading is still what is wanted for that
  half (L-ARENA). Both sink arms were re-keyed 500/620 -> 950 for the new
  order (band 830..1070 swept, middle taken); at 500 `bank-lost` went green
  with the wedge in `nicinit`, because it read only the bank row and every
  wedge produces one. It asserts the six stages and the marker now.

  **Also landed for sitting 7: `pch-state` (main 18373)**, stage 10 ahead of
  nicsit, and the slot-grant fix under it (main 18332) without which no
  non-picture stage could paint to the glass at any width.
  mini-agent on the stick that live-diagnoses in firmware and rebuilds the
  kernel; that is the direction, not this item.
- **WORKS-9 (reek). The USB mass-storage driver's second write, and the
  sink's own 2.7 MB write on metal.** `sinkladder.img` FLEW 2026-08-11 RED
  at or before the first allocation; the heartbeat arm is landed (main 15426)
  and the card is queued in `HardwareSitting.md`. Metal-gated; the arm and
  account are in `apps/works/works-backlog.md`. Its stub predates 15469 and
  15503, so any rebuild needs a fresh full-mission run (L-REHEARSE).
  **Damian's standing ruling: agents do not propose flights or sittings.**
- **A8 the desk build loop (fester).** Edit half done, hypervisor complete,
  arena measured at `-AllocPages 131072`; plan, roads and traps in
  `docs/Designs/Active/OS/DeskBuildLoop.md`. **The allocation question is
  ANSWERED 2026-08-19: GRANTED.** `desk.img` at 131072 pages reached the
  first-boot wizard on the ASUS and the refusal colour never appeared
  (`HardwareSitting.md` "A8", flown by red). L-FREEDOM is closed for this
  one. The arm, the five-state colour table and the both-ways bed census
  stay in `HardwareSitting.md` "A8"; a refusal paints DARK RED instead of
  sharing the anonymous in-stub blue, which is what makes it readable on a
  board with no serial port. **`compile <path>` is WIRED (fester, 2026-08-19,
  `GopConsole.codex` `gcon-compile`).** **RE-MEASURED 2026-08-21, and the two
  bed arms recorded here described code that main 18368 had already
  replaced**: the refusal used to fire before any file was touched, 18368
  moved the check to just above `vm-compile-cdx`, and neither this row nor
  `DeskBuildLoop.md` was updated with it. Against a copy of `seed/Codex.img`,
  `compile SRC/INDEX.TXT` answers `SRC/INDEX.TXT 2320 bytes, CODEX.CDX
  2872563 bytes` and then the VT-x refusal, so the mount, both reads,
  `unicode-bytes-to-text` and the byte-count report all run in the bed.
  **What waits for metal is the launch alone**, `vm-compile-cdx` and below,
  because codex-vm is itself a hypervisor and its guest sees no VT-x. Both
  arms are gated now (`codex/test/apps/gcon-compile-read`, fester 18476),
  which is the point: they were hand-run into prose, so nothing re-evaluated
  them and the claim stayed wrong for two days. That arm takes NO `.disk`
  sidecar on purpose and `no FAT volume on the boot medium` is its pass. **The
  compare against `CODEX.CDX` is BUILT AND GATED (fester, `gcon-cdx-verdict`,
  `codex/test/apps/gcon-cdx-verdict`)**, so the verb now ends by naming both
  lengths and the first differing offset instead of a size alone; it walks the
  volume buffer with `peek-byte` and allocates nothing the compile did not
  already hold. It was the design's one remaining `not started` row and it
  needed no VT-x, which is the same shape as the read path above it. The image
  is still NOT flight-ready for anything else (no `-Identity`, no source), and
  its recipe is kept current on the seed.
- **Native GOP resolution and diag word wrap (red). BED HALF DONE
  2026-08-15; the metal half is a stick rebuild and a photograph.** The stub
  picks the largest GOP mode on every non-`-EntryStart` payload; six bed arms
  in `build/gop-mode-arm.ps1`; account in `ExaminersAssay.md` "The GOP Mode
  Arms". Left: the ASUS's largest mode and whether AMI's `SetMode` honours it,
  which the bed cannot answer (L-FREEDOM); the next option-a stick built for
  any reason carries the change. Not a proposed flight. The `SetMode` half in
  `codex/build/cdxtopeScript.codex` is red's too.
- **Identity reconciliation (red, opened 2026-08-18 at Damian's direction;
  taken when commander duties are idle).** The shipped ceremony
  (`apps/works/GopWizard.codex`) was not built from `Designs/Done/OS/Identity.md`
  and the review measured it: the passphrase key is malformed
  (`wz-words-to-bytes` re-expands `hkdf`'s bytes into `[0,0,0,b]` x 32 at
  `:348`/`:493`; `IdentityManager.codex:385-393` fixed the same defect on a
  copy nothing calls); the unlocked seed is discarded (`Just (_)` at `:540`,
  `:563`) so nothing loads, signs or zeroes; passphrase and seed are never
  zeroed; `wz-auto-pass` opens any stick made with it; compare is not
  constant-time; no rotation or passphrase change; storage moved from
  DiskFacts to `IDENTITY.DAT` with magic/version/length checks only.
  Stages, none seed-affecting: ~~(1) `IDENTITY.DAT` v2 with the correct
  32-byte HKDF key, version 1 refused (no v1 exists outside this bench, Damian),
  a vector arm the wizard cannot dodge~~ DONE 2026-08-18 (red; account `ExaminersAssay.md`
  "The Identity Wrap Known Answer"; gate green, `Sut` == seed); ~~(2) keep the seed: `key-load` into the pinned
  region, `key-zero` on lock/shutdown, constant-time compare, zero the
  passphrase~~ DONE 2026-08-18 (red; `heap-scrub-to` in `HeapScrub.codex` scrubs the
  whole secret half of the ceremony under one mark, since a Text built by
  appending leaves every prefix on the heap; the ceremony order is now
  upstream, timezone, identity; account in `ExaminersAssay.md`); ~~(3) the bench auto-unlock removed or bed-only~~ DONE
  2026-08-18 (red; `wz-auto-try` runs only when CPUID.1:ECX[31], the hypervisor bit, is set, so metal always asks;
  `first-boot-ceremony` arms both sides); ~~(4) trust-root
  write and passphrase change, on `IDENTITY.DAT` on the ESP~~ DONE 2026-08-20 (red, 17792;
  `IDENTITY.DAT` v3 carries a 64-byte self-vouch over the public key, signed at
  keygen and verified at parse, v2 refused like v1; P on the unlocked screen
  rewraps under a new passphrase with fresh salt/iv; account in
  `ExaminersAssay.md` "The Identity Wrap Known Answer". The bed cannot type
  printables, so the P-key screen itself rides the same widgets first boot
  proved on metal; the wrap, vouch and refusal semantics are the serial arms').
  **RULED 2026-08-18 (queue 11, 12): the identity file
  stays on the ESP; auto-unlock is bed-only.** All four stages done; the item
  closes. Rotation (RotationFact) was never in the stages and stays with the
  design.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

**SITTING 12 FLEW 2026-08-24 (mastered by blu) AND ELIMINATED BOTH NAMED
CANDIDATES.** The medium dies at b3's FOURTH note, `reset-rst-write`, so
`swflag` and the `CTRL|SLU` write both ran after it was already gone and
neither can be its cause. The death also MOVED earlier than sitting 11's
(`rings-link`), which is evidence against a specific register write being the
mechanism at all. b3 answered `ok sent=13/13 rx=13` with the dev box's echo
peer independently logging the exchange from 192.168.6.200; the K1 write took
(770.17 `d104` -> `f104`); asde reached `RESET s2 warm reset` and stopped,
which by its own rule means the wedge is unchanged and is not the added
quiesce. Card and archive rows in `HardwareSitting.md`.
**Two consequences for this lane.** The i219 acquire-loop fix is UNBLOCKED:
its registration gated it on this bank and said it is wrong whichever way the
bank reads. And NIC-4's successor question is not answerable by a quiet ring
alone -- `nicring` read `gp=0` on the same boot where the next stage held a
full TCP conversation, where sitting 11 read `pre=3`.

The queue Damian draws from is `docs/Hardware/HardwareSitting.md`, "THE
SITTING QUEUE": five questions on one boot, in an argued order (bank before
you risk, L-BANK). NIC-1, NIC-2 and NIC-3 are ANSWERED on metal (the part
arrives cold, the poll calibration transfers, `e1000-init` does not hang);
NIC-4 flew 2026-08-16 and hung in `e1000-await-link`, fixed in 15588; the
rows are in `HardwareSitting.md`, not here.

- **THE NETIO CEILING IS RULED, 2026-08-21: CUT THE DRAIN, THE NIC COMES
  FIRST** (blu's item, off root's finding; ruled by Damian on red's
  escalation). **"tcp correctness is a working nic, not adherence to a
  standard I can't use because the nic is broke."** So the trade this entry
  used to state -- TCP correctness against campaign rule 2 -- was the wrong
  framing and is retired. A 50 s per-chunk drain buys standards-conformant
  patience with a stalled peer, and that is not spendable until the part works
  at all. **Campaign rule 2 wins: no stage may end the run.** blu cuts the
  drain budget to fit under the 28.8 s give-up ladder and does NOT block on
  migrating callers to `net-io-send-raw-checked`; that migration stays worth
  doing and is no longer a gate.
  **BOTH HALVES ARE DONE, and this entry read as open work for three days
  after they landed (blu, 2026-08-24).** The cut is `net-io-drain-ticks = 96`
  at blu 18682, on main as 18685, with `codex/test/net-drain-budget` failing
  if either number crosses the ladder: 96 is two clamped RTO intervals, so a
  merely slow peer still gets two full retransmit cycles. **The migration has
  no production callers left to migrate**, measured over the tree excluding
  `build-output` (L-COUNT): the bare `net-io-send`, `net-io-send-raw` and
  `net-io-send-text` are called by NOTHING, and `net-io-send-chunk` is reached
  only from inside `NetIO.codex` by those three. So the unchecked x86 surface
  is UNCALLED (L-UNCALLED), which is a different thing from residual risk: the
  shorter drain cannot make a path hit more often when no caller takes it.
  **The live unchecked send path is arm64's** -- `arm64-net-io-send-chunk` and
  `arm64-net-io-send-raw` (`Arm64NetIO.codex:201-215`), called by
  `codex/test/arm64-web-server` -- and its project is the one Damian deferred
  2026-08-18, which is where that risk is already registered.
  Two things that look like callers and are not: `apps/spark/SparkServer.codex:172`
  is a `print-line-uni` STRING naming the function, and the mentions in
  `cdx-serve`, `WebServer`, `HttpFetch` and `DiagB3` are backticked prose. That
  is the prose-read-as-code shape, so a census here has to require a non-name
  character after the name and still read the hits.
  **The claim this entry carried for an hour was FALSE, and blu's
  verification is what caught it (2026-08-21).** red wrote that `b3` was not
  a blind caller because it compares the reply against `DIAG.CFG`'s
  expectation. Verified: `b3` sent through the bare `net-io-send-raw`,
  printed `sent=` as the length it INTENDED rather than the length that
  left, and `expect=` is off by default, so a truncated send read `ok`. That
  is the L-REFUSED shape sitting in the one stage the campaign exists to
  read. Fixed in blu 18665: checked send, `sent=n/total`, a new `short`
  state with its own verdict row, seven b3 arms green. **Every diag image
  built before 18665 reaches main carries the blind b3, and `45239937`
  (red 18660) is one of them: it does not fly.** The list of callers still
  on the bare `net-io-send` / `net-io-send-raw` pair was the residual risk and
  belonged in the CL; censused 2026-08-24 it is EMPTY on x86, per the entry
  above.
  The lesson is L-MYSIDE one level up: red asserted the safe case from the
  shape of the stage rather than from its send path, and said "verify rather
  than believe" while publishing the unverified half as the premise.
  The escalation itself was the finding: this was never queued. Nobody put it
  in front of Damian, and blu held correctly for a ruling that no one had
  asked for. That is a routing failure, not a judgement failure.
  The unclamped cell is fixed
  (main 18389, `net-driver-cal-max`, with `codex/test/net-poll-clamped`
  proving both directions). What is left is policy, not a defect:
  `net-driver-tick-ms` is 100 and `net-io-max-ticks` is 500, so the ceiling is
  **50 seconds of wall clock even with a perfect calibration**, and the
  give-up ladder NetIO's prose deliberately sits above is 288 ticks = 28.8 s.
  `net-io-send-drain` is entered ONCE PER CHUNK, so a stalled peer pays that
  per 1400 bytes.
  **The trap for whoever takes it: shortening the drain converts a hang into
  SILENT DATA LOSS.** `net-io-send-chunk` returns early when the retransmit
  queue is full, and `net-io-send` and `net-io-send-raw` hand back a bare
  `TcpTransportState`, so the caller cannot tell a complete send from a
  truncated one -- the L-REFUSED shape val closed at 11,200 bytes, still
  present on the unchecked path. A shorter drain makes that path hit more
  often. So the drain budget and the move of callers onto
  `net-io-send-raw-checked` are ONE piece of work, not two, and the tick
  ceiling interacts with the RTO ladder that `NetIO.codex:36-43` argues for.
  That last sentence used to read "not taken unilaterally: it trades TCP
  correctness against red's campaign rule 2", and the ruling at the head of
  this entry retired the framing. Taken by blu.
- **THE RING QUESTION IS ANSWERED, sitting 6, 2026-08-21: `rdh-writable=y`.**
  RDH is ours to write on the I219-V, so the 08-15 reading of `RDH` moving 0
  to 15 under `RDT=15` was the part advancing its own head over descriptors it
  consumed, not a register refusing our writes. **What is still open is the
  successor question, not this one**: whether a frame ARRIVES during nicring's
  own window. `pre=3` says the MAC counted three good frames before the stage
  ever looked, so the part receives; the during-window count is the `stats`
  row's second GPRC read, the bank died at `xhci`, and only the glass
  survived, so "nothing arrived" and "arrived and was invisible" both still
  stand. Details and the caveat are on the NIC-4 card in
  `HardwareSitting.md`. The history below is kept because it is what the
  instrument was built against. `RDH` moved 0 to 15 with `RDT=15` on 08-15,
  which was either frames moving or `RDH` being unwritable as `CTRL` is. This entry said "the arm cannot separate them" and that has
  not been true since the stage was rebuilt: `nir-run-part` writes `RDH=7`,
  reads it back and restores it BEFORE anything that can spin, and banks
  `rdh-writable=y/n`; the `dd=` maps are painted after-listen and after-send,
  and GPRC is read twice so the pre-stage window is separable. What was
  missing was proof the discriminator can say NO -- every bed run answered
  `y`, because codex-vm always accepted the write, so the `n` branch had
  never executed anywhere. `-e1000-rdh-ro` and the `nic-rdhro` arm supply it,
  paired with `nic-nolink` asserting the `y` side so neither can pass a field
  stuck on one word. It rides B3's boot rather than a flight of its own.
  Also open from NIC-3:
  aneg-done is never set on this part while `STATUS.LU` comes up, so
  `phy-bring-up` returns 0 against a link that is up.
- **B3, a real TCP conversation with a real peer. THE STAGE IS BUILT (blu,
  2026-08-20): `build/boot/diag/DiagB3.codex`, ladder stage 13, so this
  rides the grouped sitting rather than a flight of its own.** The stack
  holds one in the bed over the e1000 (main 15013/15028) and the serving peer
  runs on both cards (`ExaminersAssay.md` "The Serving Peer"). What the
  sitting still needs from whoever composes it: the peer named in `DIAG.CFG`
  has to ECHO, because the stage's conversation is raw TCP and not the
  repository wire. **THAT PEER EXISTS AND B3 HAS ANSWERED ON METAL, so this
  is a composition step and not open work (blu, 2026-08-24).**
  `build/boot/echo-peer.ps1 -Port 7` is the listener, it is in main, and
  sitting 12's recipe already names it; at sitting 11 on 2026-08-21 it logged
  `CONNECTION 28 from 192.168.6.200:49157`, thirteen bytes `codex-diag-b3`
  echoed back unchanged and closed clean, which is b3's whole exchange over
  the real I219 (`HardwareSitting.md`, sitting 11). The next sitting is the
  gate for what b3 still cannot say, not for whether it works. **Finding 4 (ASDE) IS A STAGE (blu, 2026-08-20): `build/boot/diag/DiagAsde.codex`, ladder stage 14, risk writes, last after b3** -- eight L-STATES words, the arms run first with no RST pulse and ASDE=1 from the firmware state, the reset rides last as one row, and the positive control `asde-differs` moves with the input (ASDE=1 SPEED=10, ASDE=0 SPEED=1000) rather than merely passing. Its ownership was red's in the DiagnosticStick step-5 table and that contradicted this file; corrected 2026-08-20. Previously: `build/boot/asdeflight.img` is built, bed-verified
  both ways, and awaits a sitting.
- **NIC-5: what wedged the box on 2026-08-11.** Not `CTRL.RST` (discarded on
  this part). Terminal by construction, flies last.
- **B4, serve the repository protocol: steps 1-5 DONE in the bed (root,
  main 16636).** The wire is written down in `DevelopersRulebook.md` "The
  repository wire"; `EdgeMeshGameServers.md` Status names the surface Phase 2
  starts against. Step 6, the same conversation on the part, is B3's flight.
- **ICMP is send-only** (`icmp-parse` has a test and no production caller).
  Whether we answer a ping was CALLED in rulings queue 1: we do not, and the
  parse stays latent. Nobody writes the receive side. `Tftp`, `Syslog` and `Icmp` have no production caller and are
  latent; `syslog-decode-bytes` builds its body with the quadratic `acc &`
  accumulator (CostModel 3.6), and whoever gives `syslog-parse` a production
  caller fixes that in the same change.

## Track C -- the trust audit (val)

C1 (diverse double-compiling) and C2 (the independent rechecker) are LANDED
and enforced: `docs/Designs/Active/Tools/IndependentRechecker.md`,
`docs/Test/Active/DDC-QUINE-ARM.md`, `OperatorsManual.md` "The witness has a
negative control". COMPILER-3 and COMPILER-5 are closed with no hole in the
fixed point (accounts in `OperatorsManual.md` and main 15410). Left:

- **The rechecker fork is CALLED** (red, 2026-08-20, rulings queue 3): the
  plug keeps deriving type-variable instantiation and the compiler does not
  emit it, because a plug reading the compiler's own answer stops being a
  second implementation (L-CAPABILITY-LOST). Not open.
- **C2.5 stage 4 (proof terms) stays deferred unless Damian calls for it.**

## Track D -- bytes we did not produce (RULED 2026-08-15, CLOSED 2026-08-16)

The census and both sweeps are `docs/Designs/Active/Features/VerifiedFormatParsing.md`
section 10 (141 parsers in the first sweep, 43 decoder chapters in the
second); 10.1 is the ranked queue and its last paragraph is the take order;
10.3 says how a row can be wrong. Every REACHED row is guarded and the guard
pattern is settled (`ExaminersAssay.md`: "The UDP Frame Guard", "The CDX
Input Guard", "The GPT Integrity Guard", "The FAT Geometry Guard", "The
Config-Descriptor Clamp"): clamp where the length decides a slice, refuse
where it decides WHERE a read lands, put the ablated call IN the arm, predict
every expected value before it runs.

**Still open in 10.1, all unowned unless named:** 8b (`VirtioBlk`'s
device-written used-ring index, waits for a bed), 9 (`AgentBundle` refusal
paths), 10 (DONE reek 2026-08-20: `Modbus` is LATENT with no production
caller, `Lwm2mClient` is GUARDED, and `Lwm2mFirmware`'s unbounded flash write
from a frame is FIXED on red's ruling -- the bank size enforces, CoAP `Size2`
refuses early only. Four ablated arms; account in `OTAFirmwareUpdate.md`), 18 (`OtaBoot boot-load`,
reek, LATENT: no production caller), and the latent corpus rows 6, 7, 11 and
13. **Item 20, the additive bounds
guards, is CLOSED (reek, 2026-08-20): the remainder is swept and every site
is safe.** WIDTH decides it, not shape -- every remaining addend is a literal
constant, a fixed-width wire field ceilinged at 2^32-1, or the length of an
object that exists, and none can approach the i64 wrap. The rule that replaces
the count is in `ExaminersAssay.md` "A Bounds Guard That ADDS Can Be
Overflowed": the additive form is unsafe exactly when an addend is UNBOUNDED,
which here means a decimal parse or an already-i64-wide value, and
`sdw-decode` was the only such site.

## The lanes -- RULED by Damian 2026-08-15, re-pointed 2026-08-18

**RE-RULED by Damian 2026-08-26 (via red, commanding), and this paragraph
supersedes the rows below where they disagree:** **blu** takes the open
compiler bugs (`codex/compiler/compiler-backlog.md`). **This paragraph
ordered COMPILER-28 first and then COMPILER-23 until 2026-08-26, and BOTH
had already landed when it was read** (28 at main 19731, 23 at 19818), which
is L-COUNT one register over: not a stale count but a stale ORDER, carried
forward without re-measuring against the backlog. 25 landed at 19828, 24 at
19845, 26 at 19889. **The live order is COMPILER-27, then whatever the
backlog still shows open** -- read the register, not this sentence; **val**
makes the guios
windows MOVABLE (drag by titlebar; ShellRefinement stage 6, the rect is a
stored fact since 6.3c); **reek** takes the wasm website / marketing
effort (`docs/Designs/Active/Marketing/Cobblestone.md` web surfaces plus
presenting the wasm self-compile page), with the plugs close-out register
explicitly BACK BURNER; **fester** is held idle in reserve for the
hardest problems only (Damian: reserve the spend). Plugs 1.14's wasm half
CLOSED 2026-08-27 and the row above carries what it cost; what is open in
that lane is riscv-729, the `emit-wat-expr-at:746` landmine and the
oracle-vector flake.

**THE DESK HAS NO SCHEDULER. PARKED 2026-08-26 at Damian's call, not cancelled**
-- the choppiness that prompted it is gone and the drag was good enough to land,
so it is to be iterated later. **Its measurement arm exists and is on main**
(19875): the topbar reads desk-loop iterations per second, about 20,000 idle
and **60 with a 3D pane focused**, which is the case for the scheduler stated as
a number rather than an argument. Building it found a real defect on the way --
nothing called `hpet-start` at desk boot, so the timer ran only if you had
opened the 3D pane. Everything below stands as written. Every pane
invents its own repaint-rate policy inline and `desk-loop`'s period is whatever
the focused pane's step costs, so pane painting sits in the input path. Four
different policies are in the tree and the window drag added a fifth.
`docs/Designs/Active/OS/DeskScheduler.md` is the proposal: one tick source the
desk owns, a pane DECLARES its period as a per-pane fact, and the loop pumps
input every iteration and services a pane only when due. It touches
`works-desk-contract.md` section 0 and all fourteen panes, and it carries two
questions only Damian can rule on (rate or budget; skip or run late on a miss).
**Not built. The choppiness that prompted it is NOT reproduced in the bed after
four measurements, and the design says so rather than claiming the fix.**

An item here is a pointer; the register named beside it is where the detail
lives. Re-read this table on every merge-down; it is the assignment, not a
suggestion. Each lane, in order:

| agent | now | then | standing |
|---|---|---|---|
| **blu** | **B3 IS A STAGE (2026-08-20, blu 17988): build/boot/diag/DiagB3.codex is ladder stage 13, so the grouped sitting can carry the TCP question.** Twelve L-STATES words; it runs the production path (net-driver-bring-up through net-io-close), costing a second CTRL.RST after nicinit's, spent after every earlier stage has banked. Four bed arms including a real-peer positive control through codex-vm's NAT: measured sent=13 rx=13, echo returned byte for byte. **The desk peer must be an ECHO listener** -- the conversation is raw TCP and not the repository wire, so a cdx-serve peer reads as 
o-reply; it is deliberately not B4 step 6. A codex-vm defect fell out of it and is fixed in the same CL: e1000_rx_cursor was never reset (there was no case E1000_REG_RDH in e1000_write), so one receiver bring-up in sixteen resumed permanently stuck, queueing frames in the host while the guest saw a dead wire. **NIC-4 awaits sitting 5, and the instrument is built.** Sitting 4 flew ED90B46A and answered ARRIVED-BUT-INVISIBLE (gprc=1 rnbc=0, rdh=1, ddset=0). Stage 2 landed 17742 on image **27326F86**, rehearsed 20 of 20 across both beds and carrying reek's sink chunk knob, so one image flies both halves of the sitting. **The discriminator we had agreed on was wrong and would have read backwards on metal**: it was a raw all-sixteen-zero test on receive descriptor 0, but `e1000-build-rx-descs` (`E1000e.codex:537`) writes the buffer address into bytes 0..7 itself and zeroes 8..15, so all-zero cannot happen while our ring build works and the test answers "the part wrote our ring" on every flight. The row now counts nonzero bytes in the WRITEBACK half (8..15) only, keeps the full dump for the eye, and carries `buf=` as the other half's control. It rides the QR because the summary is built from each stage's FIRST glass line, which is also why the ladder was NOT reordered. GPRC is read twice, so a count can no longer belong to nicinit's ring two stages earlier. `nicring listen=0` and the `nic-noread` arm are the positive control: our own `e1000-poll-raw` recycles the descriptor it took a frame from, so a successful listen leaves wb=0 and the bed could not otherwise express a writeback at all. Caveat to state before flying: `match=y` compares RDBA against our ring POINTER and is valid only while the guest is identity-mapped, so it is not by itself proof of correct aim | **17603 LANDED (main 17751), new seed 0A37A56F.** `infer-and` records its boolean arm, so `bounded none` and `punctual` no longer refuse a body joining two conditions with `&`. Proven by the control, not the green: the same three shapes are refused with CDX6101 at exit 4 against the depot seed and clean at exit 0 against the SUT | CostModel: `fixed` still unshipped and still blocked on the registry, re-measured 2026-08-25 at 81 of 265 `bs-alloc` rows reading `unknown`. **The two-consumers-read-absence-oppositely hazard is CLOSED** (main 17822): both consumers now ask WHAT the table recorded rather than whether it recorded anything, so filling a gap for one can no longer invert the other. Decisions 17/18 are closed and their record is in `CostModel.md` 5.1 |
| **val** | **TAKEN 2026-08-27: ShellRefinement 6.4, the flick's DIRECTION picks the edge the pill docks to.** Damian ruled it the selection criterion that day, so it is per-edge pill strips, and a docked window's edge becomes a per-window fact in its registry entry beside its rectangle rather than a property of the band. **He ruled the hover preview in the same breath and it is UNBLOCKED: the app decides, and the default is a mini-render of the whole window floating by the pill**, which dissolves the premise failure 20124 recorded rather than working around it. **Only the heavy-pane stranding decision is still his**, and the four options with the measured cost of each are in `ShellRefinement.md` 6.4. **WORKS-52, WORKS-53 and WORKS-54 are all CLOSED** (20133, 20158, 20195); WORKS-55 was mine and is RETRACTED, measured with a face the desk never loads at that width. The task frame's stage 1 landed 20024 and stage 2 the flick 20078; **stage 3 hot-launch pills and stage 4 the Cobblestone button's position are unstarted and unclaimed**, and stage 3 wants doing after the per-edge strips rather than before, since both put things in the band. Resize handles and sizer arrows shipped at 19925 and the windows campaign before that at 19865. `ShellRefinement.md` "WHAT IS STILL OPEN IN 6.4" is the only list; do not restate it here. **This row has now dispatched already-finished work TWICE IN ONE DAY** -- red read it at 20124-era content and sent this lane to build a flick that had shipped, then read it again 24 minutes after WORKS-52/53/54 closed and named all three as the next item. The row is the register; if you close something, the row moves in the same session. **This row said its two shipped asks were open for a day, and red dispatched this lane to build the flick after the flick had landed** -- the register-rot shape the init skill warns about, one file over. **The DeskScheduler is PARKED at Damian's call, not cancelled**, and the argument for it is now measured rather than theoretical: 60 desk-loop iterations a second with a 3D pane focused against about 20,000 idle, which the topbar counter reads out live. **Everything below this sentence is the history of the campaign that is now finished; read it for the traps, not for an assignment.** **ShellRefinement stage 9 (crisp) was the lane** (Damian, 2026-08-21, direct, with a photograph of his Windows 11 desktop as the reference). The approved plan is `~/.claude/plans/crystalline-percolating-pinwheel.md`; the account is `docs/Designs/Active/OS/ShellRefinement.md` stage 9. **This row said stage 4 (Sound) until 2026-08-21 evening, five stages behind, and a fresh session reading the register would have been sent to build work that landed at 18696.** The order is Damian's: crisp inside today's one-pane model first, overlapping windows LAST, and icons become drawings rather than hand-authored bitmaps. **LANDED: 18827 antialiased corners, 18893 the soft shadow (`sh-blur` had been threaded through Theme and the Appearance toggle since they were written and nothing read it), 18916 a vector path can be FILLED and a curve draws (by pointing `Vector` at `GlyphRasterizer`'s existing edge scanline, not by writing a second rasterizer), 18943/18975/19008 every icon the desk names is a path rasterized once at boot, 19034 a docs sweep taking ShellRefinement from 809 lines to 538.** **STAGE 9.3 IS DONE (val 19126).** Its measurement arm had landed inside 18827 (`codex/test/apps/desk-label-metrics`) and the fix had not: `widget-label` and `widget-button` reserve `text-length * 8`, so `CODEX` asked for 80 device pixels and drew 106 while the four lowercase sidebar buttons reserved 49 to 72 MORE than they drew. **The design decision the row said was unmade is made, and the code made it rather than a ruling.** A foreword chapter may not cite the app quire that owns the face, and both candidate routes churn thirty-odd files in other lanes' quires: 36 `Theme` literals in 34 files against 67 `widget-layout`/`widget-measure` sites in 32 files. `widget-measure` returns a leaf UNCHANGED, so nothing in the foreword ever revisits a label's width; the fit is therefore a walk in the app that HOLDS the face. `comp-fit-text` (`GopComposite`) rewrites `wn-min-w` from `gfont-text-w`, `Widget.codex` gains one additive helper and no foreword signature moves. **It is applied to the chrome only, and that is correctness rather than scope: a pane that hit-tests its own subtree uses plain `widget-layout` (`GopBrowser`), so fitting the paint side alone misplaces clicks.** A pinned width is replaced only while it still equals the constructor's guess, because the taskbar pins `task-clock` to 220 over an EMPTY string and an unguarded fit reserves nothing for it. Measured with CMUNSS at 1600: the brand label goes to exactly 0 slack and every button to a constant 32 or 33, which is `widget-button`'s own padding. **A maximum width is now unblocked, and no picture moved: `flex-col-place` still hands every child its container's full width, so an honest cell stays invisible until one lands.** The table and the command that makes it are in `ShellRefinement.md`. **The antialiased STROKE is done too: `vec-stroke-coverage` answers a 0 to 255 grid, so outline artwork is no longer a Bresenham staircase. Its construction is the part worth knowing: `gr-fill-scanline` is even-odd, so one edge list of stroke quads CANCELS where they overlap and guts the ink at every join and crossing. Each quad is rendered on its own over only the rows it spans and merged by MAXIMUM. Measured on an X at width 2: crossing 255 per quad against 47 for the single edge list, with an arm pixel at 255 under both, and the rejected construction is kept as a row in `codex/test/ui/vector-raster` rather than described. Small type is done too: the desk owns `dk-ui-ppem` (16 at 1600 and above, 14 below) and the CBF fallback keeps `comp-glyph-h` at 16, which is the dual purpose that stopped the constant simply being lowered. The MULTIPLIER was the bug: a ppem is already a device size, so `* ui-wscale` made the type exactly twice what it should be. Caps go 22 to 11. Two constants assumed the old cell and the PICTURES found both, not the numbers: `dk-line-step` now reads the face rather than a flat 36 that was too tight at cell 41 and half again too loose at cell 21, and `dk-icon-px` stops quantising a DRAWN icon to a multiple of the 8 pixel stencil it replaced, which had been throwing away seven of the gutter's fifteen pixels. Both metric arms were repaired rather than re-baselined: each was measuring a constant the paint path had stopped using (L-INSTRUMENT). WORKS-35's class is closed too: four chapters restated where the sidebar ends and no two agreed, and `ui-scale` steps at 1024 while `ui-wscale` steps at 1600, so each wrong version was right at SOME width and none was right at both. At 1600 the sidebar is 320 device pixels and the Files pane started at 180, cutting every sidebar button in half on the width every capture is taken at. `ui-sidebar-px` in `GopDraw` answers it once in device pixels; `gsc-sidebar-w` and `dk-mon-x` are deleted. The arm is `codex/test/apps/desk-pane-origin` at BOTH 1280 and 1600, and it compares against the width `widget-measure` lays the sidebar out to rather than only against the function every pane now calls, which would be the instrument built from its subject. Windows are the live campaign, and Damian's ask on 2026-08-24 is wider than the old stage: overlapping windows with focus and alt-tab, close/minimize/maximize, and edge docking (flick from the titlebar, a pill with the app name, click restores, hover shows a live mini-preview). `ShellRefinement.md` stage 6 carries the stage list. **6.1 the offset render path and 6.2 the first window are DONE. 6.3a maximize and restore landed 19312 and the taskbar band that follows the face landed 19342; 6.3c's first half landed 19349 -- a window's rectangle is a stored fact rather than centred arithmetic, which is what two windows were actually blocked on, since every window the old geometry placed landed on the same pixels. **6.3c IS DONE (19392, main 19395): the Calculator is the second windowed pane, the desk paints every open window bottom to top with the focused one last and its titlebar in `pal-primary`, Tab cycles focus, a click raises the window under the pointer, closing one hands focus to the window underneath, and a new window CASCADES so two do not land on the same pixels.** `WindowManager` is deliberately NOT wired in and that is a decision rather than an omission: it is a functional record and the desk's durable state lives below a bump-allocator base mark that every pane exit restores, so a block is the registry and the array order IS the z order. WORKS-49 carries the reasoning and the open question of whether `Window.codex` should be deleted or kept for a caller with a heap. **6.3b MINIMISE IS DONE AND 6.4's FIRST UNIT WITH IT (19469, main 19471): minimise a window to a pill in the taskbar, click the pill to restore.** A minimised window keeps its rect and focus skips it, and restoring needs no kept rectangle because 6.3c made the rect a fact in the pane's own block. The pill is a BUTTON IN THE TASKBAR TREE, which is forced rather than chosen: `desk-taskbar-clock` re-renders that subtree every second, so anything painted into the band from outside it is erased within a second. **6.4's OPEN QUESTION IS ALSO ANSWERED (main 19415), and it moved the item's shape: docking's mechanism already existed as `desk-step-hide`, so what is left is the flick, the pill as its own drawing, and the hover preview.** Measured, a docked HEAVY pane costs 1.6 to 2.1 MB and closing one beneath a live one STRANDS about 3 MB rather than reclaiming (the frontier rises), so 6.4 must not present the pill as a cheap place to leave things. STILL OPEN in stage 6: the flick gesture, the pill drawing, the hover mini-preview, and the heavy-pane decision that stranding forces. **6.5, EVERY PANE A WINDOW, IS UNDER WAY** (Damian, 2026-08-25, on seeing 6.3b run: *"i like the minimize and maximize work. but only for a handfull of the apps. i think they all should do that"*). The flick still waits. **GROUPS A, B AND C ARE ALL CLOSED, NINE PANES: the Console (main 19518), the launcher, the Calendar, the Diffusion pane and Appearance (main 19518, 19521, 19531), with the cascade and the cap at 19518; Issues (main 19544); the Monitor (main 19562); and the 3D View and the Aquarium (main 19574).** `dk-wr-max` was 4 and is 15, which is the registry block's own bound; 4 turned out to be the vertical cascade room divided by the step, so the cascade wraps now, each axis in its own room. Three findings the inventory did not predict, and all three bind the panes still to come. **A pane costs SIX things, not five**: `desk-wnd-over` is a fourth per-pane fact for a pane that paints over its own laid tree, and `dk-wnd-wants-box` is a first placement of the whole content box for a pane whose tree will not fit three quarters of it. **The bigger screen is the tighter one, by nearly half** -- a tree lays out in logical pixels and `ui-wscale` doubles at 1600, so three quarters of the content box goes from 840 by 546 logical at 1280x800 to 480 by 300 at 1600x900, and every line clipped so far was clipped at the LARGE size. **And a full-box window is still one titlebar short of the pane it replaces**, which cost the Diffusion pane a line even with the box. **Review is NOT a group A pane**: `grv-tree` takes data `grv-load` builds by scanning the whole medium, so windowing it needs that cached where the painter can reach it, which is a `DeskApps` field and a signature change through the window machinery. **Issues added a fourth finding and it is the one that generalises: a pane's TREE may be a function of the ROOM it was given**, so `desk-wnd-tree` now takes `w`, `h` and `tf` to find the window's content box rather than the pane's. `desk-wnd-one` is its only caller, so that widening was three lines, and it is the honest reason Review is disqualified and Issues is not. **And a pane that PAGES rather than clips hides a bad fit from every arm and from an eye looking for a clipped edge**: nothing was ever cut off, but at 1600x900 in a three-quarter window the Issues table read `Page 1 of 7` with two rows and every cell truncated to `CD...`, and `dk-wnd-wants-box` takes it to `Page 1 of 4` with four rows and full ids (eight rows to thirteen at 1280x800). **Group C split in two and neither half was a tree.** The Monitor is a pane that DRAWS TEXT, and giving it a tree costs exactly what its draw could reach: what the tree can compute stays in the arm, what is a machine fact too wide for 32 bits joins `desk-wnd-tree`'s arguments (`base` and `stride`, because a framebuffer address is 64 bits and there is no `poke-qword`), and what is settled before the base mark goes in the block (`kbd` and `mouse`). That last kind fails QUIETLY: an unwritten block cell reads zero and zero is `mailbox` and `no`, exactly what a machine with no USB HID reports, so `desk-mon-block` asks the written and unwritten blocks to DISAGREE rather than asserting either reading. **The two 3D panes took the table's other answer and already had it**: `ScenePane` carried a view rect derived from hand-synced copies of the desk's chrome constants, and `gsc-place` replaces that derivation with the window's rect before every frame. **The resize question is answered without spending anything**, and the answer was in `Renderer3D`: `r3d-target-at` models a stride independent of width, so the buffers are allocated once at the content box and a smaller window renders the top-left of them, where a target rebuilt per resize would strand about 4 MB above the pane's own mark until it closed. **GROUP D IS STARTED AND THE BROWSER IS A WINDOW (main 19599), and the stranding decision did NOT have to be made first** -- windowing a heavy pane changes where it paints, not how long it lives, so the mark discipline is untouched and 6.4's 3 MB tail stays a question about DOCKING and about `desk-marks-reclaim` popping from the top only. Two things came out of it: **`dk-wnd-content` is the content box, once**, after that arithmetic had been written out four times and this pane wanted a fifth; and **a heavy pane must repaint itself when the chrome answers `stay`**, because a raise, a Tab cycle or a maximise repaints every window and draws this one's empty body panel, where a 3D pane recovers on its next frame and a pane that only redraws on an event has none. `desk-bro-h` is deleted and `browser-pane-fit` re-pointed at the window content box (L-INSTRUMENT). **THE EDITOR'S BLOCK COLLISION IS CLEARED (main 19619) and it was bigger than the design said.** The row named 56, 60 and 64; grepping the block rather than reading `ged-init` found a fourth and it is the one that mattered, **the filename buffer at bytes 44 to 55**, which is `dk-wnd-st-cell` and the first two words of the rectangle. Nothing had recorded it because nothing but the naming mode reads those bytes and that mode had no test at all (L-UNCALLED). All four are above 108 now, the block is 192 bytes in `desk-run` and in `annot-write` which builds one, and the offsets have names instead of literals. `codex/test/apps/edit-block` is the arm: **the assertion is a CENSUS of bytes 44 to 67 and the two round trips beside it are the control**, because a buffer works perfectly at either offset and only the span can see one sitting on the desk's rectangle. **THE SWEEP IS DONE (main 19645): the panel geometry takes a rect, and Files and the Editor are one argument away from being windows.** `GopFiles` answered where the panel was in four functions of the SCREEN and `GopEdit` called them, so three chapters agreed about one rectangle by each doing the same arithmetic, which is the shape WORKS-35 paid for once already with the sidebar constant. The four are deleted; every drawing and hit function in both chapters takes a `LayoutRect` and the desk says which one: `dk-pane-box` for a pane that owns the desktop, `dk-wnd-content` for one that is a window. **Neither pane is a window yet and that is deliberate** -- this lands the parameter and hands both panes exactly the rect they had, so it is provably inert: `codex/test/files-parse` and `codex/test/apps/desk-pane-origin` are UNCHANGED across it, with the rect spelled out in the first as exactly what the deleted functions computed at 1280x800, and both panes photograph identically. **6.5 IS DONE (main 19665): EVERY PANE ON THE DESK IS A WINDOW, all fourteen.** Files took a `ds` cell at 116; the Editor needed none, its slot being 44 through 60 of the block that grew to 192 bytes for exactly this. **The last finding is one the other twelve could not have shown and it needed a PHOTOGRAPH rather than an arm: a converted pane must not keep its own title bar.** Both drew one through `gfl-draw-frame`, so under the desk's titlebar there were two bars saying `Files` and two `x`es at different heights meaning different things. The bar stays and is a LOCATION now -- the window says which application, the bar says which directory or which file, which is the one thing the window title cannot know. `gfl-close-hit` is deleted rather than left unreachable and **the capability that went with it is written down rather than left to be noticed**: that `x` returned from a preview to the listing and dismissed three of the editor's modes, and Esc does all four. **DAMIAN TESTED 6.5 AND REPORTED SIX THINGS. THREE ARE FIXED (main 19677) AND THREE ARE OPEN.** Fixed: **the desk flickered with two windows open**, because a pane whose content ticks was calling `desk-wnd-paint-all` once a second, which is a full-screen rebuild on a framebuffer with no back buffer; `desk-wnd-paint-from` starts the walk at that window's registry index instead, and **opening and ticking no longer share a paint function** (narrowing the tick had silently narrowed the open, so the chrome was never drawn at all and it looked right for exactly one second). **The taskbar clock flashed** because the tick walked the whole band and drew the time LAST, so the clock was absent for the length of the walk; the erase is `comp-box` over the taskbar's box clipped to the clock's cell, which a flat sub-fill could not do without seaming under a gradient. **And the desk's own hand-drawn text took the bitmap grid** while every widget beside it was proportional: the brand, the Welcome caption and the clock all go through `desk-line` now, with `gfont-text-w` for the clock's right edge. `codex/test/apps/desk-window-repaint` is the arm. **6.6 IS DONE: THE SIDEBAR IS GONE AND THE COBBLESTONE PILL IS A START MENU** (Damian, 2026-08-25, *"lets remove the whole left pane, put the shutdown option inside the cobblestone menu pill on the bottom so it acts more like the start menu than a full screen app. the cobblestone button should expando the program groups and apps."*). Four of the sidebar's five buttons were already rows in `gpr-entries`, so only Shutdown had to move; `ui-sidebar-px` is deleted from `GopDraw` and `dk-cbox-x` answers zero, which widened the content box and is what moved every window rectangle in `desk-window-frame`, `desk-window-registry` and `browser-pane-fit`. **The cascade gets 15 distinct positions of 15 instead of 12, because the wider box wraps later**, and `browser-pane-fit`'s `tree past the granted pane` is UNCHANGED at 430, so nobody should read that file's moved numbers as BROWSER-5 being fixed. **The menu is the launcher COLLAPSED and that is a measurement, not a preference:** put `gpr-tree` whole into a box above the pill and at 1600x900 the Shutdown row lands past the bottom of the glass with the taskbar behind it, while at 1280x800 it fits. `gpr-menu-tree` shows one row per GROUP and expands the group holding the selection, and **the open group is a function of `gpr-sel` rather than state**, so the two presentations stay one model and arrow keys crossing a boundary open the next group with no code that does it. A heading's id carries `gpr-group-prefix` so `gpr-id-scan` cannot match it, or clicking a group would launch an app. **Three findings, each from a capture and none reachable by an arm.** A flex-1 `widget-panel` paints its own background, so the anchoring spacer drew as an empty box until it became a label, which is the shape the deleted sidebar had already proven. **The contract's claim that a panel does not size to its children was FALSE and had been believed for a campaign** -- `widget-measure` propagates minima and `widget-layout` measures before it arranges; the real trap is one line further down `Widget.codex`, that `widget-set-min` preserves flex, so the box stretched rather than collapsed and the two look identical from the far side of a capture. **And the arm written for the overflow could not fail:** an over-tall menu PUSHES the taskbar down rather than overlapping it, so "the menu ends above the band" reported yes on the broken build while the band sat 38 rows off a 450-row screen. Two rectangles that move together cannot adjudicate each other; the row that discriminates compares the band against the GLASS. `codex/test/apps/desk-menu-groups` is the arm and it was proven by sabotage rather than by passing. **`desk-label-metrics` changed specimen and the reason is worth keeping: a row built with `widget-set-min ... 0 h` reserves NO width**, so every launcher row measures zero and a tree that never asks the widget layer's eight-pixel cell for a width cannot measure it. It walks the taskbar now, where the entry reserves 256 device pixels against 93 drawn and 126 against 93 once fitted. **THE PILL AS ITS OWN DRAWING IS DONE (val 19734, main 19742): a taskbar pill carries its app's icon.** `dk-pill-icon` is keyed by focus id beside `desk-wnd-title` and is deliberately a SECOND table rather than a join against `gpr-entries`, because **the window title and the launcher label diverge for four of fifteen panes** (`Edit` against `Editor`, `Web` against `Browser`, `Monitor` against `System Info`, and `Programs` is not an entry at all) and `gicon-named` answers the `file` icon for an unknown name instead of refusing, so the join would have put the wrong picture on four pills with every count still agreeing. The arm is one row in `desk-chrome-icons` covering a missing arm and a misspelled name together, since both land on that same fallback; sabotage-proven both ways at 13 of 14 with `titled=14` unmoved as the control. **That title-versus-label divergence is a real thing a person can see and is NOT fixed: WORKS-50.** **Left in stage 6: the flick, the hover mini-preview, and the heavy-pane stranding decision that docking forces -- and that last one is a product choice about what closing from a pill MEANS, so it is Damian's rather than the lane's.** The table and every measurement are `ShellRefinement.md` stage 6.5.** This sentence read "6.3 and 6.4 are NOT started" until 2026-08-25 and was three CLs behind, which is the register-rot shape the init skill warns about: a stage number carried out of a summary is not addressed until it names its campaign and its CL. Two things 6.1 and 6.2 settled that the old design had wrong or unstated: the origin goes in the LAYOUT RECT so `comp-hit-x`/`comp-hit-y` need no origin, and the close mark must be ONE contour because two crossed bars cancel under the even-odd fill. **The open question for 6.4 is what a docked HEAVY pane costs**, since state above the heap frontier cannot be restored by redrawing, which is the same LIFO constraint that put windows last.** **Windows are last for a measured reason and not for taste: the heap is a bump allocator with save and restore, so lifetime order is LIFO and z-order is the same order, and overlapping windows are exactly the feature that decouples them.** `Window.codex` and `Surface.codex` are both written and cited by nothing; `Surface` is one type substitution from usable (`sf-fb` is a `Framebuf` at 24 bytes per pixel, 18.9 MB per full-screen composite against a 128 MB arena). **Stage 4 still owes focus change, error and notification; stage 5's GUI half and stages 6 and 8 are open; stage 7 is PARKED on a user-model ruling that routes through red.** Everything else this lane owns is in `apps/works/works-backlog.md`: WORKS-47 (a button icon painted outside its button -- the drawn icons MAY have closed it and that is unmeasured, so the row stands), WORKS-41, WORKS-44, WORKS-46, WORKS-40. | 18696 the desk click, 18827 antialiased corners and the eight-pixel-cell measurement, 18893 the soft shadow, 18916 the vector fill, 18943 the icon rasterizer, 18975 the chrome icons, 19008 all nineteen drawn, 19034 the docs sweep | 18437 the settings schema, 18478 the desk wire, 18489 WORKS-47, 18508 check-test-compile, 18559 the stage 4 device half, 18615 the BDL lifetime obligation, 18696 the desk click |
| **fester** | **NOW (2026-08-25): the wasm plug, `codex/plugs/plugs-backlog.md` 1.60, Damian-directed into this lane for the Cobblestone push; wasm is a first-class target and the crazy-boss page is the centrepiece.** Six of the eleven runtime builtins closed today: the linked list and `text-concat-list` (fester 19281, main 19290), `__list-with-capacity` and `list-insert-at` (19298, main 19300), the three `__buf-*` names (19313, main 19323). Each was taken from the x86-64 answer key rather than from the js plug, which matters twice: `__linked-list-push` conses onto the FRONT so `to-list` REVERSES, and `list-insert-at` fills IN PLACE when capacity allows. **`codex/plugs/wasm/wasm-e2e.ps1` is the runner and is new** -- the three previously proven subjects had been hand-run into prose with nothing to re-evaluate them (L-BODY, L-NOGATE). It grades every subject in `codex/plugs/wasm/test/` through source -> IR -> plug -> WAT -> `wat2wasm` -> `wasmtime` against THE SAME SOURCE COMPILED FOR x86-64, refuses rather than skips on a missing toolchain or a plug binary older than its source or the seed, and passes `-Kernel` to BOTH arms after it was found grading two different compilers against each other. **NEW: plugs 1.61, THE WASM PLUG HAS NO CCE LAYER.** `raw-bytes-to-text` was written, measured, and BACKED OUT rather than shipped: the byte copy matches x86-64 exactly, but bare metal holds those numbers as CCE code units and converts on print while this plug writes raw bytes to `fd_write` and converts nowhere. Every earlier subject missed it because they build text from ASCII LITERALS, which agree under both readings (L-CONSTRUCT). **Scope: anything rendering text through this plug is on the unconverted path, so it is not one builtin.** LEFT in 1.60: `text-to-double-bits` (a real decimal-to-IEEE-754 parser) and `raw-bytes-to-text` (blocked on 1.61). **The compiler EMITS 9.24 MB of WAT and does NOT assemble; the 35-to-11 undefined-name census has NOT been re-measured since 2026-08-24, so how close assembling is remains unknown. Do not say the compiler runs in a browser until a module runs.** Previously: **"The battery choreography" item 2, `codex-vm -run-list`, LANDED fester 19089 (2026-08-22); the block below the pool carries the shape and the measurement. It spawns a fresh child per line rather than reusing the process, which is not what the item asked for: the reason given for reuse being safe does not hold, reuse is worth 12.6 ms of a 575 ms test, and red ruled for the supervisor shape after the measurement. `build/check-run-list.ps1` is its runner.** **Re-run at head 2026-08-24: all 5 arms green, 8.8 s, against a codex-vm binary 15 s younger than its own source. Its gating is HALF, and the honest split is worth writing down: `bvt.ps1` drives every BVT test through `-run-list` and the BVT is a standing-gate phase, so a regression on the HAPPY path turns every agent's gate red without this script. The arms nothing runs are the refusal and isolation half -- a corrupt kernel not taking its neighbours, the wall budget stopping one line alone, drop attribution, a nested list refused -- and those can rot unseen (L-NOGATE). Wiring them means a phase in `build/build.ps1`, which is generated and whose generator `codex/build/BuildScript.codex` is reek's claim, so it is red's call and not taken unilaterally. **DONE 2026-08-25 (reek, on red's clearance): the `run-list` phase is in `build/build.ps1`, 5.7 s, keyed on a per-file `$tVm` covering `tools/codex-vm.c`, `tools/codex-vm.exe` and `build/check-run-list.ps1`. All five arms run; both directions were fired before it was called done, positive by opening `codex-vm.c` alone and negative by sabotaging an arm in place. The account is in `docs/Designs/Active/Build/Build.md` under the trigger audit.** Previously: **ShellRefinement stage 1, the GopComposite half, is COMPLETE** (Damian, 2026-08-20, who sent me to ask val for parallel work; red released the guios claim to val). Claimed and now done: `comp-text` and its metrics in `apps/works/GopComposite.codex`, sitting on val's threading at main 18118. **Extents before draw, main 18192**: `comp-fit-px` bounds the string on a GLYPH boundary from the real advance table, `gfont-text-w` wraps the existing `gbf-measure-text` rather than being a second implementation of the metric, and `gfont-text-clip` stays as a second bound (L-FALLBACK). Its arm is `codex/test/apps/comp-text-metrics`, a synthetic face needing no disk, whose 16 values were predicted before the run and which an off-by-one sabotage moves in exactly the six truncating rows. **Cap-band centring, main 18258**, on val's `gf-asc`/`gf-cap`: halving `gf-gh` centres the glyph BOUNDING BOX, 2591 units against a 2048 em for cmunss, which sat the capitals 8.5 device pixels low in a 40 pixel button and hung the descender 6 outside it; centring `gf-asc - gf-cap` to `gf-asc` lands the band centre half a pixel from the box centre and brings the tail back inside. **The fit centralised, main 18285**: callers no longer pre-chop to `box width / 8s` CHARACTERS before the face is consulted. **The caret, main 18294**: the measured prefix width and the cap band, not `cursor * comp-cell-w * s`, which had it floating 38 pixels clear of the text in the Browser address bar. **The 40 px shift in files and issues is CLOSED and did not reproduce**: an artifact of the pre-18241 font pipeline when `gf-gh` was the trimmed ppem, dissolved by val's FontLoad work; it was reverted unshipped at the time, so nothing had to be un-landed. **Two findings this row used to carry were wrong and are corrected**: `fl-write-yoffsets` is a STUB, so the y-offset table `gbf-put-text-loop` reads is uniformly zero and nothing is placed by it (L-UNCALLED), and the vertical defect was the rasterizer trim rather than that table. | **The golden sweep has a runner at last** (main 18127, self-check 18134): `build/desk-goldens.ps1` and `build/bmpdiff.ps1`, requested by val for stage 1. **The SCALE-PAIR arm landed main 18175**, requested by val for stage 2's "identical modulo size" claim, and it corrects the pair that claim has to be measured on: the desk lays out in `w / ui-wscale`, so 1024x768 and 1600x900 are different ROOMS (1024x768 against 800x450) and comparing them measures room and scale together; **800x450 against 1600x900 is the pair that isolates scale**, both laying out in 800x450. A plain equality test is useless on it -- 15,029 of 360,000 cells disagree and 14,818 of those are a channel delta of 3 or less, the theme gradient rounding over twice the rows -- so `bmpdiff -Scale N` censuses MAGNITUDES and `-MinDelta` separates them. **Its baseline reading, before stage 2 has drawn an icon: 60 structural cells on the desk pane, all on the corners of the five sidebar buttons**, 1x showing sidebar background where 2x shows the button adornment ramp. That is stage 2's starting reading and not icon breakage to be blamed on the icons later. `works-desk-contract.md` had named that sweep as the acceptance test for any fleet-wide widget change since 17846 while the thing it named lived in one scratchpad. Proven before being relied on: two sweeps of one kernel byte-identical on all 14 panes, and the comparator shown to report MOVED and exit 1. **val found the hole I had missed** -- `browser` and `browser-key` agreeing is correct (scancode 30 is a no-op and `gbr-step` still releases and repaints, which is what BROWSER-5 closed on) but agreement is also what undelivered keys look like, so `browser-newtab` (Ctrl+T, 12,036 pixels in rows 40-79, the tab bar alone) carries the falsifiable half and `-SelfCheck` asserts the relations inside one set. **The sweep covered 11 of the 16 panes `desk-dispatch` opens and said so nowhere, which is the same hole one level up: an omission nobody wrote down reads as coverage.** Not rot -- all five were dispatchable at 18127, when the script was written, so this was under-scoping from the start, and `works-desk-contract.md` has named the sweep as the acceptance test for any fleet-wide widget change since 17846, meaning a change that broke the editor or the system menu passed it in silence. Widened 2026-08-24: `diffusion` (32), `edit` (18) and `menu` (41) are rows now, each with a `desk`-differs relation so `-SelfCheck` catches the key never arriving, ablated three for three with the browser relations unmoved as the negative control. `scene` (4) and `fish` (16) stay out and the REASON is now written in the script rather than absent: both route to `desk-scene-step`, which animates, and two boots of one kernel disagree (measured, 1600x900, clock frozen). 18 entries, 199.7 s for a full capture. **The BROWSER-5 campaign closed 2026-08-20 across six CLs**: L-BOTHARMS, the compositor clip and the 78-row out-of-bounds write (17846), `desk-bro-h` measuring the band (17892), `comp-translate` (17932), the scroll wiring (17968). **wademo** shipped to a customer and landed at main 18080: pattern and colour choices with a picker, the years running with an honest interpolation label, and a second pyramid to compare against. ProductBuilder stage 5 (main 17784); stage 6 is ON HOLD pending customer approval and lives in `codex/product/product-backlog.md` 6 | **CrossLaneFilesystem is CLOSED and moved to Done/** (2026-08-20): step 0 was already done for the whole unresolved-call class, not only block- names, so the design is complete on both cross lanes. The browser register holds BROWSER-4 alone, and it is network-gated. **BROWSER-8, the scroll indicator, is CLOSED (fester)**, on the second attempt: the first landed at main 18312 and was REVERTED at 18377 for turning `browser-pane-fit` and `browser-scroll-wire` red. The placement was right both times -- wired at `browser-render-with` so the viewport probe narrows with the page, descending to the page column because a bar reserved at viewport height would otherwise make content measure as fitting and kill scrolling silently -- and what was missing was a container the UI quire did not have. **`widget-scroll-view`**: no padding, border or margin, reports only its own declared minimum rather than its content's, paints nothing in all three renderers, and is a `WkCustom` tag rather than a new `WidgetKind` so every existing match site already routes it. Until it existed a scrollable viewport was not expressible at all, because `flex-layout` honours a child's minimum over its container and content taller than its slot is what scrolling MEANS. The measurement that found it, including the mechanism the first post-mortem recorded and that turned out to be false, is in the CL descriptions at 18446 and 18453. First use also found `scroll-bar` itself broken (L-UNCALLED): the bar measured 34 logical pixels against a `scroll-bar-w` of 6 and the gap 14, so its own "the column keeps its width either way" invariant was false in both branches. GopReview's `grv-bar` narrows with it and that pane's golden moves. **The parallel-block throw class is CLOSED in all three harnesses (2026-08-20).** **`ExaminersAssay` gained two classes this session**: no gate phase compiles anything under `codex/test`, so a test chapter that stops compiling is UNRUNNABLE and every instrument stays quiet (`widget-tone` was red four days across a release), and contention corrupts a DURATION as readily as a verdict, which is worse because a duration gets quoted forward rather than checked. Recovered from a message that never arrived: `build/check-mailbox.ps1` catches the three silent-loss paths and is worth running at init. WORKS-24 rides a sitting, WORKS-16 is blu's, WORKS-17's syntax half is a `Theme` decision |
| **reek** | **plugs close-out lane** (from val, 2026-08-18): the register in order, one entry at a time, text builtins first (1.31/1.36/1.37); fester keeps the riscv entries (1.3 family), blu the deck/arm64 tail; say so in status.json | (OTA socket wiring DONE 16793; ProtocolStack CLOSED 16780; item 20 CLOSED for the named files 16769) | WORKS-9 is metal-gated, routed to red's sitting; `ShellDslReadability.md` stays reek's |
| **red** | **NOW (2026-08-27, session handoff): commander through the marketing day and the PR 93 absorb; the fleet is live and self-directing.** The day's landings, each with its account in the named register: the marketing-refocused landing REWRITTEN, PORTED to `LandingPage.codex` (20043), and LIVE on **cobblestoneproject.com** (new repo `damiant3/CobblestoneWeb`, GitHub Pages, HTTPS enforced; `PublicPush.md` gained the website-mirror section and the CNAME rule); the a0425e10 PR 92 overclaim CORRECTED publicly (repair 19980, correction push 012a9d2e, P-CLOBBER gained the copy-up variant); PRISM-6 routed and DELIVERED by reek/fester (eight lenses, UI tab, downloads; live); Steve's zig note transcribed to `docs/Reference/ZigDemandingCustomer_Notes.md` with plugs 1.96 + COMPILER-30 verified out of it; **PR 93 ABSORBED (main 20184) with its SEED (20189, `4341370C`)** -- gate green one-pass, BVT 407/0, plug-smoke 8/8 cross-host, and the origin subject answers 3 on the zig arm; the plug-run StrictMode mask fixed in script + generator (20165/20167). **The seed rode a SECOND token hold because red released the first before running 4.3's hash check -- the check is BEFORE build-complete, not after the report.** Two LinkedIn posts drafted for Damian (first published; the Prism follow-up draft is session-scratch and regenerable from the PRISM-6 row in minutes). Then as before: commander; sittings; Review pane stages 3+; the diag step-2 lifts; COMPILER-23 re-presentation when Damian calls. **Previous NOW (Damian, 2026-08-24): absorb Steve Howell's GitHub PRs and turn the public push around; wrap-up week for every other lane.** All six PRs are reviewed (every citation verified against source) and LANDED in red stream: 19116 COMPILER-18 row (PR 79), 19117 plugs 1.57 row (PR 80), 19125 zig one-heap + emit deck 24 to 28 MB (PR 77, seed-affecting), 19131 zig self-tail-call loops (PR 81), 19133 zig over-application (PR 83), 19140 parser mutual-tail-recursion restructure + COMPILER-19 row (PR 82, seed-affecting). Every CL gated green at its step; the two seed-affecting ones took the token. **COMPLETE 2026-08-25: the push is public (github 0c4327d5, seed 6CF4A8E0, GitHubUpdate50), all six PRs closed with credit on 2026-08-25, and red declared MAIN OPEN 2026-08-25.** The pin on seed-affecting copy-ups from other lanes is lifted. The battery choreography (the prior NOW) is COMPLETE: items 1/2/3 landed 19081/19089/19086. Then as before: commander; sittings; **the Review pane** (Damian, 2026-08-19: the repository protocol's user-facing half on the desk; stages 1 and 2 DONE 2026-08-19: lists proposals and verdicts from the medium's fact partition and casts a verdict signed by the box identity through the new `key-sign-bytes` primitive in `Foreword chapter Identity`, Damian's ruling that the primitive beats the user-space workaround; `works-backlog.md` WORKS-44 has the account, WORKS-46 the gap that keeps it empty on a USB stick: DiskFacts reads through the kernel's ATA syscalls); identity stage 4 (trust-root write, passphrase change, on `IDENTITY.DAT` on the ESP; stages 1-3 landed 2026-08-18); the diag step-2 lifts red owes (xHCI truth, keyboard, MSC align, largest GOP mode + `SetMode`) so the first grouped sitting carries every standing question | `BatteryReorg.md` step 6 | `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**`; Update 47 PUBLIC 2026-08-18 (github 69cd9ce8, seed 90646EEB); **interim mirror push 2026-08-19 (github a061c173, seed 800A7683) carrying Steve Howell's PR 69, PR 71 and issue 72, all closed with the commit**; **interim mirror push 2026-08-22 (github ff9eaf4c, gitlab the same, main 19106, seed unchanged A01C1547) carrying the battery choreography, no release proof owed or run; `GitHubUpdate50.md` has the account**; the Update 47 "preview gate on a pre-convergence stage" question is settled by blu's measurement (compile(A)=B, compile(B)=B, ONE fixed point; the preview ran on stage1 of an older seed, which is what the gate said) |
| **root** | **HardwareAbstractionLayer.md** (pool, per red; OracleCloudArm64 DEFERRED by Damian). **Board-threading phase DONE 2026-08-18** (main 16944-17016): the foreword got a linear GPIO `Pin` handle, and all nine board chapters now thread the shipped linear `UartPort` + `Pin` for UART and GPIO (`<b>-uart-open/write/close`, `<b>-gpio-open` + `<b>-pin-write/close`), verified by `build/boards-test.ps1` (9 green, 126 sub-tests). Board chapters are non-seed, no token. **Capability phase (foreword) DONE 2026-08-18** (main 17063, SEED 55E53A81 -> 7590CCA1): `[Gpio]`/`[Uart]`/`[Spi]` are rows in `Capability.codex` (cs-id 18/19/20, bits 27/28/29) mirroring `Flash`; the foreword gpio/uart/spi handle ops carry them; a `Device.Mmio`-only driver is refused CDX2031 (three `hal-launder-mmio-*` tests). check-effect-vocab regen 0 drift, gate green, seed self-verifies, 184 refusals ok. **Board wrappers promoted to the caps (main 17084)** and **the read side DONE (main 17139, SEED 318B2BF6)**: the checker mints owners for the linear components of a returned tuple (let-pattern, `when` on the call, act-bind then `when` on the name; `_` at a linear position CDX2063), the tuple-type parser accepts `linear`, and `Board.codex` carries `gpio-read`, `uart-recv`, `SpiTxn` + `spi-select/transfer/deselect` (`hal-tuple-linear`, `errors/hal-tuple-{leak,dup,wild,owner-leak}`, `errors/hal-spi-cs-leak`). Also 2026-08-18: `capability-doors.expected` re-recorded for the 17063 rows (main 17135, was red since 17063). **Board threading of the read side DONE (main 17146/17148/17156):** `<b>-pin-read` on 8 boards, `<b>-uart-recv` on the 3 with a receive primitive, the linear SPI transaction on the 6 SPI boards; boards-test 9 green, 143 sub-tests. Polled receive + `<b>-uart-recv` on Fe310/Rp2040/Stm32L4 landed main 17164 (146 sub-tests). **`[I2c]`/`[Adc]`/`[Power]` rows + `I2cBus`/`AdcUnit` handles landed main 17174 (SEED 5B2DE4E6; the table's unassigned bits 1/2/13, so COMPILER-17, the imm32 grant hazard above bit 30, stays LATENT and recorded). I2C bus threaded on the five I2C boards and the ADC unit on RP2040 (main 17183; 152 sub-tests). nRF EasyDMA receive landed main 17194 (154 sub-tests; every UART board receives through the handle); `TinkersToolbox.md` HAL section and counts refreshed. **ADC threading landed main 17777 (2026-08-20):** ADC1 drivers on Stm32F4/Stm32L4, SAADC wrappers on both nRF boards (nRF9160 gains SAADC at 0x4000E000); five boards carry `AdcUnit`; boards-test 9 green, 161 sub-tests. Esp32C6 left alone (SAR ADC map unverifiable in-tree); Fe310/Pi4/QemuVirt have no on-chip ADC. **Rulings 15 ruled (a) and BUILT (main 17831, 2026-08-20):** the linear Board threads every open on all nine boards, sleep-deep consumes it, sleep-with-open-handle is CDX2063; boards-test 9 green, 161 sub-tests; seed unmoved (DCE). **Flash follow-on CLOSED (main 17839, Damian-directed):** flash-open-bank threads the Board; sleep with an unsealed bank is CDX2063. **The HAL carries its full designed surface; nothing open in the lane.** `build/boot/diag/**` stays root's | then: HAL follow-ons above, next pool item, or red's call | plugs 1.34 is rulings queue 10 |

**Plugs are reek's lane for the close-out** (from val, Damian's direction-08-18: 19 entries open, two or three fleet-days to a green register): when entry closes, reek takes the next open unclaimed one in
`codex/plugs/plugs-backlog.md` in the register's order and says so in status.json. Entries other lanes hold
are named in the register (1.33 blu, 1.38 and 1.3 fester, 1.36 and 1.32
reek, 1.34 root). `codex/plugs/zig/**` came in with Steve Howell's PR 66 and
is ORDINARY FLEET CODE, edited like any other plug (Damian, 2026-08-18: the
loan was for his early updates, those are absorbed, and it is over). This
supersedes both the 2026-08-16 "not a fleet edit" rule and the "ours to gate
loosely, no rigour beyond a smoke" reading that replaced it. Credit Steve in a
CL that changes what he wrote and flag it in the next GitHubUpdate; that is
courtesy, not a gate.

## Approved campaigns and the pool (Damian, 2026-08-18)

Damian approved every open design campaign in `docs/Designs/Active/` as
available work; a lane that empties draws from the pool below, in this order,
and says so in the table above. Assigned in the table: ProtocolStack + OTA
(reek), CostModel 3.4+ (blu), ProportionalDecks (root), PlugDeepRecursion
(val), the diagnostic stick and BatteryReorg step 6 (red).

**The pool. Corrected 2026-08-19 (fester): most of this list was already
drawn and nobody struck it, so a lane reading it drew a second owner onto
another lane's work.** The table above is the assignment and it says so
itself; where the two disagree the table wins. Taken, and NOT available:
`HardwareAbstractionLayer.md` is root's ("pool, per red", with Power on
rulings 15); `GameEngine.md` phase 2 is val's (shadow campaign stage 1 landed
at main 17190); `ShellDslReadability.md` is reek's by Damian's direction and
carries a file claim; `ComplianceEvidence.md` is root's, whose evidence plug
shipped 2026-08-18 with the open items in it; the QEMU bulk-output path
LANDED at main 17198 (blu) and its account is immediately below.

**`EdgeMeshGameServers.md` phase 2 is DONE, not drawable** (fester, drawn and
finished 2026-08-19; verified against main 2026-08-21). All three connections
landed the day it was drawn: discovery `EdgeMeshLive` at 17570, authenticated
sessions `EdgeMeshAdmit` at 17575, routing `EdgeMeshRoute` at 17597, each with
its own arm under `codex/test/edge-mesh-*`. This paragraph still listed it as
the head of the pool, which is a live item's worth of wasted work for whoever
drew it next, and the design's own Status has said "phase 2's three
connections are all landed" since that day.

**So the pool holds NO drawable item and one queue that is not a pool item at
all:**

- `ThreatModel.md` is CLOSED and moved to `docs/Designs/Done/IoT/` (fester,
  2026-08-21). Ruling 19's four questions are settled in the document with
  the measurement behind each, which is what it lacked: question 1 (NX) is
  closed on x86 and its per-target half is ordinary Phase B1/B3 work in
  another lane's chapters; question 3 (fault-injection redundancy) would
  harden a boot selector whose three entry points are reached only from
  `codex/test/apps/ota-boot-rollback` and from each other (L-UNCALLED);
  question 4 was never ours. **Question 2, hardware crypto dispatch, is the
  one that was still real work and it now lives in
  `HardwareAbstractionLayer.md` open question 5**, which is the design that
  owns board peripherals. **DESIGNED 2026-08-21 (root): the row "Hardware
  crypto dispatch" in that design.** Measured: no board's crypto register
  map can be cited from a document the tree holds (the nine chapters cite
  manuals `docs/Reference` does not contain; the three in-tree summaries
  name blocks without a register), so exactly one unit is buildable today,
  QEMU virt's virtio-rng on the transport `VirtioBlk` already drives, with
  its absent arm. **BUILT main 18963 (root, seed 07C8CCD5):** `VirtioRng`,
  `qemu-rng-open/read/close` threading the linear Board, the `[Rng]` row at
  bit 30, arms `qemu-rng` and `qemu-rng-absent` on the arm64 bed. **Steps
  2 and 3 BLOCKED (red's ruling, 2026-08-21) on a document to acquire: no
  board crypto manual in `docs/Reference`**, only the three summaries
  (`STM32-Reference.md`, `ESP32-C6-Reference.md`, `RaspberryPi-Reference.md`)
  naming blocks without a register. Every other board's unit waits for that
  map, the Esp32C6 TRNG first, beside its ADC; AES/SHA units wait for a map
  AND a bed for the same-answer arm. What needs no map is built: the
  compile-time refusal arm `errors/hal-rng-no-unit` (CDX3002 pinned at
  `rp-rng-open`, the board with no TRNG; its QemuVirt control compiles).
- The `DeviceEmulationCatalog.md` queue is demand-driven, not standing: its
  item 1 landed and item 2 is "whatever the hardware sitting names", which
  makes it red's sittings that produce the next entry rather than a lane that
  picks it up. `tools/codex-vm.c` also carries a file claim, so announce.

Seed-affecting campaigns take the token per CL as usual.

**Strike an item from this list when you draw it.** That is the whole of why
it was wrong: four entries here were live work on four different lanes.

## The battery choreography (Damian, 2026-08-22; red coordinates)

Damian's direction 2026-08-22: the battery is eating too much time; speed it
up, fester spun up to help, then re-measure. **Measured by red before any
change**, on the 2026-08-20 release battery's own log (`test-output/test.log`)
and on scratchpad probes the same day:

| phase | the VM | the host |
|---|---|---|
| 1, per batch of 193 tests | 20 to 62 s compiling all 193 | 8 to 13 s resolving cites, **417 to 456 s parsing the capture** |
| 2, 1,313 runs | **75 ms** per kernel, `codex-vm.exe` alone | 640 to 10,842 ms per test (median 793), 151 s wall: a `pwsh` child per test |

The guest is about a tenth of a ten-and-a-half-minute battery. The two
in-guest shapes Damian raised (run the test inside the compiler's VM and
answer on the REPL; a mini-kernel that takes programs on the REPL) were
weighed against that and are NOT taken now: everything links at
`bare-metal-load-addr` 0x100000, the test's prologue rebuilds the page
tables, IDT, serial and the arena cells the compiler is using, and its Exit
epilogue is `out 0xF4; hlt`, so either needs a second load base, a return
exit mode, arena isolation and host device reset, loses the cold boot phase 2
proves (L-ARTIFACT), and would win the 75 ms times 1,313 over 8, about 12 s.
The two-phase shape stays. Items, in order:

1. **red, the batch parser: LANDED main 19081 (2026-08-22).** The standing
   gate's `gen-scripts` phase, measured standalone afterwards: 58 generators,
   VM 82.8 s, parse 0.6 s, so `Build.md`'s "genuine compile work" reading of
   that phase holds. `test-compile-batch.ps1`
   read its capture as `$raw = if (...) { ReadAllBytes } else { ... }`, and a
   statement's result goes through the pipeline, which unrolls the `byte[]`
   into an `Object[]` of boxed bytes; every `GetString` and `Array.Copy`
   then re-converted the whole buffer, 183 ms per call on a 2.9 MB capture,
   quadratic in the batch (24 tests 10 s, 48 tests 31 s, 96 tests 130 s).
   Fixed in the generator `codex/build/testcompilebatchScript.codex` and
   installed at drift 0. **The standing gate pays this too**:
   `check-test-compile.ps1` compiles through the same parser. Edited under
   reek's generator claim (file claims table) with reek absent, by Damian's
   direction; one assignment, nothing of the readability campaign touched.
2. **fester, `codex-vm -run-list <file>`: LANDED fester 19089 (2026-08-22).**
   One line per kernel carrying exactly the flags a single run takes; `#`
   comments and blank lines ignored, tokens double-quotable, `-kernel`
   beside `-run-list` REFUSED. Per line: `RUN-LIST BEGIN`, that child's
   stderr verbatim, then `RUN-LIST END [i/N] <kernel> exit= output= dropped=
   ms=`. The drop count is keyed on the whole `SERIAL: N guest serial
   byte(s) DROPPED` phrase rather than the word, which the GPU triangle-cap
   warning also prints, so L-SHORT's signal survives batching and lands on
   the kernel that dropped. `exit` is reported and never summed: a healthy
   test exits `(debug_exit_code << 1) | 1`, which is why `test-run.ps1` has
   always ignored it. The 60 s wall budget stays per kernel and a timeout
   signals the child's own shutdown event before falling back to
   `TerminateProcess`. Contract, measurement table and the sidecar note are
   in `OperatorsManual.md` "Batch mode: `-run-list`".

   **It spawns a FRESH child per line; it does NOT reuse the process, and
   this row said it would.** The stated reason a reused process was safe --
   a fresh partition, so no device-reset work exists -- is not sound: the
   device models are host globals and deleting a partition does not touch
   them, so reuse needs 376 file-scope statics reset and one missed static
   makes a test's result depend on what preceded it in the batch. Measured
   first, one kernel: **574.8 ms** per test through `test-run.ps1`, **73.7
   ms** for codex-vm alone, **12.6 ms** of that being the exe's own start.
   The `pwsh` child is 501 of the 575, so what a batch mode must remove is
   the SCRIPT per test and not the exe; reuse buys the 12.6 ms. A fresh
   child pays it and makes the batch byte-identical to N single runs by
   construction rather than by test. red ruled for this shape.

   `build/check-run-list.ps1` is the runner, five arms: byte-identity with
   an ablation proving the comparison can go red, a corrupt kernel not
   taking its neighbours, the wall budget stopping one line alone, the drop
   landing on its own line with the GPU line as a negative control, and a
   nested `-run-list` line refused (a line naming its own file is an
   unbounded fork bomb on a shared box). **Not proven and not implied:** arm
   4 injects the phrase through `-args`, so the scanner and the attribution
   are exercised end to end but no guest-side serial drop is provoked, there
   being no external lever that forces one. **`tools/codex-vm.c` is blu's
   claim; blu is absent and this was Damian-directed, announced in the file
   claims table.** **`test.ps1` phase 2 is wired to it: LANDED main 19095
   (red, 2026-08-22).** One supervisor per `-Jobs` slot, dealt by the last
   run's `.run-ms` with the same rule phase 1 uses; each test's verdict
   reads its own `END` line, so a drop or a timeout is `FAIL_RUNTIME` on that
   test alone; the writable `.disk` copies and `.vmargs` tokens stay with the
   caller; `CODEX_VM_HOST=qemu` keeps the per-test `test-run.ps1` path. Proven
   on 12 real tests covering every sidecar kind: `runtime.actual` byte-identical
   to `test-run.ps1`'s, verdicts identical, a corrupt kernel `FAIL_RUNTIME`
   and a wrong `.expected` `FAIL_OUTPUT` as the controls. Not exercised by
   that harness: the `.fatal` arm, whose logic is unchanged, since no fatal
   test compiles without `-Fatal`. **`bvt.ps1` is wired the same way and
   LANDED with a fix to both (red, 2026-08-22)**: the BVT run phase went 7.5 s
   to 12.4 s on the first wiring, and the cause was measured rather than
   guessed (launch style, temp-dir population, disk copies and stdout
   back-pressure each eliminated by a probe): a `-RedirectStandardError`
   file ANYWHERE on D: costs ~7.5 ms per stderr line, the same eight
   supervisors taking 2.6 s with the file on C: and 12.3 to 12.7 s on D:,
   repo or not. `test-run.ps1` and the batch compiler only ever escaped it
   because `GetTempFileName()` lands on C:, and the `test.ps1` wiring at
   19095 had the D: redirect too. Both now capture on the system temp and
   move the file into `_runs` afterwards; BVT run phase 7.5 s to 3.0 s, total
   21.5 to 17.3 s, all 60 `.out` byte-identical to the old path. The standing
   bed fact is in `ExaminersAssay.md`.
   **Found on the way and FIXED (Damian's direction, 2026-08-22):**
   `test-run.ps1` never deleted the writable `.disk`/`.disk2` copies it makes;
   `%TEMP%` held 9,340 `tmp*.tmp` files, 15.7 GB, the oldest from 2026-06-15.
   9,331 of them (15.32 GB) were deleted, 12 held open were left; the
   generator `testrunScript.codex` now releases both copies in its finally
   block (proven: a disk test through `test-run.ps1` leaves the count
   unchanged, where it was +1 per run). **The re-measure batteries (two, on
   Damian's go-ahead) ran 356 s and 432 s wall** against ~10.5 min on
   08-20, phase 2 80 s and 126 s against 151 s, host parse 1.5 to 4.8 s per
   batch against 417 to 456 s; but phase 1's VM time (102 to 219 s per batch)
   and cite resolution (38 to 52 s) were 4 to 5x the 08-20 figures in both
   runs while the same batch standalone measured normal minutes before and
   after, with the fleet idle and the box reading 32 per cent busy at launch.
   Attributed after the fact to the box: a `codex-vm` from `NewRepository-val`
   had been spinning a core since 2026-08-21 21:05 and two further consumers
   Damian killed by hand. **The third run, on the quiet box, is the
   measurement: 123 s wall against ~10.5 min on 08-20; phase 1 58 s (VM 28
   to 46 s, resolve 10 to 11 s, parse 0.4 to 0.6 s per batch), phase 2
   53 s.** One red per run on the SMP tests and each a different one:
   `smp-affinity` hit the 60 s wall in run 2 with its complete output on the
   wire, and `smp-halt`'s codex-vm child in run 3 faulted on the HOST
   (`HOST CRASH: codex-vm faulted (code=0xC0000005)`) at teardown after its
   complete output; each is three of three green standalone. **That is a
   codex-vm SMP teardown defect, not the harness's and not the guest's, and
   it goes to whoever next holds `tools/codex-vm.c`** (blu's claim). The
   harness's part: the crash line and the supervisor's `END` landed on one
   line and the anchored match reported "no END line", fixed the same day
   by matching unanchored; with the line read, a host crash after complete
   output still PASSES, as it did under `test-run.ps1`, so this is recorded
   here rather than hidden by the verdict.
3. **red, deal the batches by `.src-bytes`: LANDED main 19086 (2026-08-22).**
   Largest first onto the lightest batch instead of `$i % $Jobs`: the slowest
   batch spent 62 s in the VM against the fastest's 20 s. Proven on the real
   list of 1,651: round-robin 11.0 to 17.4 MB per batch, size-dealt 14.2 MB
   and 206 or 207 members in every batch, each batch keeping its
   lightest-first order; a tree with no readings deals identically to
   round-robin (the control). Bytes are a loose proxy (the VM spread was 3x
   on a 1.6x byte spread), so expect the phase 1 VM floor to move toward the
   mean, not to it. Generator `testScript.codex` T04.

Re-measure after 1 and again after 2 and 3; the battery is Damian's to run
and the numbers go to `ExaminersAssay.md` "Batch Compile Architecture".


## Registers carrying unowned work that wants a lane

Named here because a register nobody owns is a register nobody reads.

- **`docs/Designs/Active/OS/OracleCloudArm64.md`**: **DEFERRED by Damian
  2026-08-18** ("basically a dead project, you can defer that"). Every LOCAL
  half is closed (reek; both residues root, main 16697: the stub publishes
  the DMA floor and both virtio drivers read it). The ~5-connection ceiling
  per boot was root's next item and is now deferred with the design; upload,
  VCN and the external smoke test (rulings queue 6) are moot while deferred.
  **Deferred with it, found 2026-08-21 (blu, closing the bare-send
  migration): `codex/os/net/Arm64NetIO.codex` is a full twin of the send
  path and carries NEITHER fix.** No checked sender exists there, and its
  drain is still `arm64-net-io-max-ticks` 500, the same 50 s per chunk the
  NETIO ruling retired on x86. Not touched, by the deferral and not by
  oversight; the `arm64-web-server` test site stays on the bare sender for
  the same reason. **Whoever lifts the deferral inherits both as the first
  item**, and an arm64 TCP send that hangs or truncates silently before that
  is this row, not a new defect.
- **Six `-Internal` phases have their RUNNER under `build/` and do not trigger
  on it** (reek, audited 2026-08-25 under ruling 20's sibling; PARKED, not
  claimed). `jonquil`, `plug-binary`, `cross-smoke`, `plug-smoke`, `app-sweep`
  and `sem-equiv` each invoke a script in `build/`, so `$tBuild` is a true
  answer-dependency for all six: change `jonquil.ps1` and jonquil's answer can
  change without jonquil running. **Blanket-widening them is the wrong fix** --
  `$tBuild` fires on 11 of the last 50 changes on main and would add roughly
  130 s each time, turning `-Internal` into the full gate for any build change.
  The precise fix is a per-file trigger, which is machinery in the map to
  manage a risk the next full gate already catches (L-LESS), so it is recorded
  as a question rather than built. **Interim rule, costing nothing: whoever
  edits one of those six harnesses runs its phase by hand and says so.** The
  three phases whose dependency was real and cheap are already widened
  (`vm-differential` 19236, `deck-headroom` and `gen-scripts` this session).
  Table with costs: `docs/Designs/Active/Build/Build.md`.- **Four harnesses are dead on every box: codex-vm never parsed
  `-data-port`/`-ctrl-port` and silently ignores unknown flags** (reek,
  found 2026-08-24, recorded in `OperatorsManual.md` and plugs 1.41 at reek
  19149; unowned). `Start-VmRun`'s codex-vm path always returns null, so
  `extract-x86-output`, `test-disk-compile`, `sim-test` and `gdb-watchpoint`
  have never run through it. **UNOWNED again: reek claimed it 2026-08-24,
  scoped it, and RELEASED it at wind-down 2026-08-25 without touching
  `tools/codex-vm.c`** (that file is blu's claim elsewhere in this plan, so
  announce). The scoping is the value and it moved the shape twice, so the
  two halves this row named are not the work.
  (1) "Parse the two flags" is not a flag parse: codex-vm's serial is
  file-based by construction (`-input` preloads a ring buffer, `-output`
  dumps at exit), so a live ctrl/data socket means making the emulator's I/O
  loop interactive. (2) `extract-x86-output.ps1` cannot be revived by
  transport at all, because the `ELF` mode header it sends does not exist in
  the compiler either -- measured, it returns byte-identical output to a
  nonsense mode string (plugs 1.41, `DevelopersGuide.md` compile modes).
  So the live questions are whether these four harnesses are wanted at all
  (delete is a real answer for at least one) and the refuse-unknown half,
  which still stands and now has a second instance in the compiler's own
  mode dispatch (L-ACCEPTED). The refusal ships LAST and only after a census
  proves no live caller passes a flag it would kill; a first census was too
  wide and swept up QEMU and openssl flags, so that census is unbuilt. Not
  push-blocking.
- **Bare metal prints the fifteen tier-0 Cyrillic code units as unrelated
  ASCII, because the ASCII-or-UTF-8 branch tests the LOW BYTE of the code
  point rather than the code point** (fester, measured 2026-08-25 against
  depot seeds C9395985 and D74D8855, byte-identical output on both; **FIXED
  2026-08-25 by blu, compiler-backlog COMPILER-21** -- and it was on THREE
  print paths, not the one cited: the serial loop fester measured plus
  `__uefi_print` and `__uefi_print_no_nl`, which the UEFI exit selects.
  Guarded on the serial path by `codex/test/ops/tier0-cyrillic-print`; the
  two UEFI loops stay ungated because nothing in `codex/test` runs under
  `-uefi`. **The three further readers are FIXED too (COMPILER-22), and no
  read of the low table anywhere in `Emit/` is now unpaired with a hi-byte
  combine.** The one that mattered was `__text_to_unicode_bytes`, the emitter
  for the live builtin `text-to-unicode-bytes`, so the truncation was
  reachable from ordinary Codex and returned wrong integers rather than wrong
  glyphs. FURTHER DEFECTS on this surface are open and unowned, and they are
  NOT what this line first said: COMPILER-23 carries them, after two
  retractions. `char-to-text` truncates every multi-byte CCE code to its low
  byte (`emit-char-to-text-builtin` stores length 1 and one byte, never
  consulting `cce-encode-length`), which is the cause of the tier-1 print
  symptom. `text-to-unicode-bytes` answers 0 only when its input text is the
  TOPMOST allocation, so the earlier "round-trips nothing but ASCII" reading
  was an artifact of one heap position. And the tier-2 encode collapses
  u=8364 onto u=192. Damian ruled repair 2 on a mechanism since retracted;
  red is re-presenting.
  `X86_64IO.codex:304-307` loads `cce-to-unicode-byte-table` (the low bytes)
  and branches to the single-byte `__serial_put` when that byte is under
  128; the hi table at `cce-to-unicode-hi-rodata-offset` is read only on the
  other arm, at 317. Every tier-0 code unit whose Unicode has a nonzero high
  byte AND a low byte under 128 therefore prints as that low byte. That is
  exactly CCE 113..127, U+0430..U+0443, whose low bytes are 0x30..0x43:
  measured, `char-to-text (code-to-char 113)` prints `0`, 114 prints `>`,
  127 prints `C`, while 97 (U+00E9, low byte 233) prints correctly. The
  repair is to combine the hi byte before the `cmp 128`, not after it.
  Found by the wasm plug disagreeing with the oracle after plugs 1.61 gave
  the plug a correct CCE layer: the plug prints `d0 b0` and is right.
  Separately, and NOT the cause: a whole second CCE printer is dead.
  `emit-cce-to-utf8-helper` (`X86_64Helpers.codex:644`, emitting
  `__cce_print`) is called from nowhere, so it never reaches a binary, and
  the tables it would read -- `cce0u2-lo`, `cce0u2-hi`, `cce-t2-rodata` at
  `X86_64State.codex:500-523` -- are in no `init-rodata` either. Its three
  offset constants at 525-532 describe a blob layout that is not the one
  line 281 emits, so it would read past the end if anything did call it.
  Whoever takes the branch fix decides whether that second printer is the
  intended replacement or dead weight (L-UNCALLED).

## Decisions

**Numbers are stable ids, not an order.** A ruled item shrinks to one line
here and its reasoning moves to the doc that owns the work; the number stays
so the citations across `GitHubUpdate*`, `CostModel.md`, `plugs-backlog.md`
and the designs keep resolving. Gaps are expected.

**What belongs in PENDING, and it is a narrow test (Damian, 2026-08-20):**
only a decision he alone can make. An outside relationship, an account, a
spend, a product direction. **A technical trade-off with a defensible answer
is the commander's call, not his**, and parking one here spends his attention
to look careful. Eight items below were parked wrongly on that test and were
called on 2026-08-20; each says who called it, so any of them can be
overturned in one line.

### Pending -- only Damian can answer

**Nothing open as of 2026-08-27 evening: all five were ruled or dissolved
in one sitting (Damian with red, decision walkthrough). The rulings, each
recorded in the doc that owns it:**

1. **COMPILER-27 RULED: the bare-metal deck guard goes IN, behind a
   compile-time flag, and the toggle is a single operation** (the build
   selects the guarded or unguarded allocation function once; no live
   per-allocation pre-test). The taker measures the guarded self-compile
   beside the unguarded one. Row updated; open work, unowned.
2. **COMPILER-30 ErrorTy RULED: SPLIT NOW.** `ErrorTy` is reserved for
   genuine type failures; a distinct no-expectation marker takes the
   sentinel meaning. Every plug inherits; Steve's second PR rebases onto
   the new wire (told on PR 93). Row updated; open work, unowned.
3. **COMPILER-29 dissolved before ruling**: blu fixed it (main 20223,
   arm64 43 to 40 and riscv 72 to 69 on the paired beds, zero new
   failures); the fix-shape question answered itself in code.
4. **ir-fidelity RULED: RELEASE GATE, not `-Internal`** (the BVT-not-full
   principle: no standing tax for an infrequent class a release catches
   soon enough). red ran it the same evening: 4 cases, 4.2 s, the two
   untouched-by-lifting cases green; the two lambda cases UNSUPPORTED
   pending the 20176 reader repoint, and one expectation was stale
   post-PR-93 (expected DROPPED on a defect now fixed). ALL DONE the same
   evening (red): the repoint was case-path data (`def:__lam_0/param/0`,
   the reader needed no code change), the expectation flipped to CARRIED,
   the full `-Grade` run is clean against seed 4341370C (self-test ok,
   3 ablations ok, 4 cases CARRIED, unexpected: 0, ~7 s), and the release
   skill carries it as Step 0c.
5. **Heavy-pane stranding RULED: option D, FIX THE ALLOCATOR.** Buried
   heap marks become reclaimable rather than the pill lying about close.
   val's campaign; the ShellRefinement 6.4 frontier table is the
   acceptance arm and its stranded row (+5,114,480 above baseline) must
   drop.

Also standing: red re-presents COMPILER-23 (mechanism retracted, measurement
recorded in the row) when Damian calls for it. The 08-13 vmdiff TEMP residue
was ruled DELETED the same evening; the witness re-run live at head answered
OK, both hosts agreeing on FC5B3D2C0911F075.

The 2026-08-24 note below is history:

- Prism's product scope, RULED (Damian, direct): *"we definitely need
  compile/transpile on the fly for the prism. the canned IR is not the
  correct design."* Full scope PRISM-1..3, in-process compile + fan-out, the
  pre-baked IR path goes. fester's lane; `apps/prism/prism-backlog.md` is
  the register.
- COMPILER-18, RULED (Damian, direct): **"the old answer: give it the
  word."** The partial-application closure gains a remaining-arity word so
  the trampoline can re-close on under-application, which is the Rulebook's
  declared model. Seed-affecting; ASSIGNED to blu (compiler codegen, while
  the I219 swflag item waits on sitting 12's bank). The measurement, the
  entry paths, and the two unexplained behaviors worth settling on the way
  are in the COMPILER-18 row (`codex/compiler/compiler-backlog.md`, Steve
  Howell PR 79); the fix cites that PR. The coverage half of the row --
  the over-application shape beside `test-input/lambda.codex` whose closure
  wants MORE than one argument -- lands with the fix.

**On hold, customer-gated (Damian, 2026-08-20):** 16, **ProductBuilder stage 6
needs a protected-side host.** (fester, 2026-08-19.) Stages 0 to 5 are landed
and every one runs entirely in the Codex bed. Stage 6 is the protected merge,
deploy and rollback half, and it needs either access to an outside
organisation or a decision to build and run a stand-in ourselves. Nothing
before it is unsafe to get wrong and everything from it is. **It is ON HOLD
pending customer approval of next steps.** It is not a question sitting on
Damian's desk, and it is not drawable by any lane until that approval arrives.

**Deferred by Damian, not pending:** 6, OCI account access for
`OracleCloudArm64.md` phases 5b-5d (the whole design is deferred; the local
halves are closed).

**Not a question until there is a design partner:** the fourth of
`ThreatModel.md`'s open questions, secure-element support in `Identity`. The
doc defers it to design-partner requirements and we cannot answer it alone.

### Called by red 2026-08-20, on Damian's direction to stop parking technical calls

Each was in the pending list and should not have been. Reversible in one line.

1. **A ping goes unanswered, and that is deliberate.** No production caller
   for `icmp-parse` until something actually needs one; the parse stays
   built and tested and latent, as it is. Answering costs a reply path and a
   surface for nothing anyone is waiting on, and silence is the cheaper
   default to reverse. Recorded rather than left open. (Track B, blu's lane
   if it is ever drawn.)
2. **ARP learns only from replies we solicited.** Learning from any frame on
   the wire is cache poisoning by construction, and `net-process-arp` still
   does it. Narrow it. It is a real trust-model change and was correctly kept
   out of the crash fix (blu, main 15310); it is now a drawable item in
   blu's lane rather than a question. **DONE (blu, 2026-08-20).**
   `net-process-arp` has three arms: a request aimed at us is answered, a
   REPLY addressed to us is learned from, everything else is `arp-ignored`.
   **The blast radius was measured before the change, not after: this cache
   has exactly ONE consumer in the tree**, `resolve-dst-mac`, and it looks up
   one address, `sess.gateway-ip`. Every other entry the old code learned was
   read by nothing, so the narrowing costs nothing and removes the whole
   surface. Arms in `codex/test/arp-cache-bound` (the flood is replies now,
   plus 300 third-party requests that teach nothing and a request aimed at us
   that is answered and still teaches nothing) and
   `codex/test/apps/arp-reply-test`; account in `ExaminersAssay.md` "A Count
   Is Not A Length". **"Solicited" needed building, not just enforcing**: the
   stack had no way to ASK, so `net-arp-solicit` and `NetIO`'s
   `net-io-resolve` are new, and `cdx-serve` -- the one consumer that
   genuinely relied on learn-from-anything, and said so in its own prose --
   resolves its gateway before it listens.
3. **The rechecker keeps deriving type-variable instantiation itself; the
   compiler does not emit it.** This is the whole value of the fork: a plug
   that reads the compiler's own answer agrees with it by construction and
   stops being a second implementation, which is L-CAPABILITY-LOST exactly --
   a checker that stops asking reports what one that asks and agrees reports.
   The abstentions the derivation costs are the honest price. (Track C, val.)
4. **Ingest the hardware-returned stick images into the depot, and keep them
   out of the mirror.** DONE 2026-08-21 (root). `build/boot/archive/` holds
   three 16 MB images and the three `IDENTITY.DAT` records recovered from
   them, with a `.gitignore` entry so they never ship publicly.
   **This row said `stick-before-20260811.img` was "the only copy of a
   hardware-written `IDENTITY.DAT`" and that was wrong** (red's phrase, red's
   correction): a scan of all 31 returned images found THREE distinct records
   a board wrote, two v1 and one v2, and the parser accepts version 3 only, so
   none of the three is reproducible. Red ruled all three images in on
   2026-08-21 rather than the one named. The versions finding is recorded in
   `ExaminersAssay.md` beside the stage 1 and stage 4 refusal paragraphs it
   falsifies; the provenance table is `build/boot/archive/README.md`.
7. **`check-vm-differential` retries once, and only when an arm produced NO
   BINARY.** "Hosts disagree" is never retried at any count. The line is the
   one the item asked for and it is not arbitrary: a missing binary is an
   absence of measurement and may be re-measured, while a disagreement is a
   FINDING and re-rolling it until it agrees is how a witness is talked out
   of a true answer. (red; the arm is red's.)
8. **`p4-stale-check`'s dropped-add scan FAILS on tracked source extensions**
   (`.codex`, `.ps1`, `.md`, `.expected`, `.failing`, `.disk`, `.cross-refusal`,
   `.no-cross`, `.vmargs`) and warns on everything else. P-CLOBBER calls the
   dropped add the worst trap in the file, and the reason it only warned was
   scratch files sharing the list; extension is the discriminator that keeps
   the warning useful without failing a gate over a stray `.png`. (red.)
10. **plugs 1.34, the ARM64 MMIO boundary: (a), gate the MMIO window in the
   effect system.** Raw accessors refuse or require `Device.Mmio` inside the
   device window. (b), a real EL0 privilege boundary, is a process-model
   campaign and a different project; it stays the named long-term direction
   and is not what closes this gap. Seed-affecting, so whoever draws it takes
   the token. Until it lands the ARM64 capability gate still gates the
   `block-*` builtins only. (red, routed from root.)
19. **`ThreatModel.md`: the recommendations stand** for NX bits now on x86
   data and stack pages, hardware crypto dispatch in the foreword API with
   board chapters providing effect handlers, and fault-injection redundancy
   for the boot selector only. The
   fourth question is the design-partner one above. (red, from fester.)
   **Carried out 2026-08-21**: measured, one of the three was still real
   work and it moved to `HardwareAbstractionLayer.md`; the document is
   closed and in `docs/Designs/Done/IoT/`. It is no longer drawable, which
   the pool paragraph above records.
20. **The check-deck 1.25 floor in `codex/build`: a measured constant bump,
   not ProportionalDecks, and not during the push window.** (red, 2026-08-24,
   from reek's evidence in `ShellDslReadability.md` at reek 19138:
   `cdxtopeScript` sat at exactly 1.25 with zero slack, and one new
   `ShellExpr` constructor moves every build-quire unit one point.) The
   precedent is PR 77's emit-deck 24 to 28 MB the same week: measure the
   need, bump the constant, let the refusal guard the new number.
   ProportionalDecks is new machinery managing a cost smaller than itself
   (L-LESS) unless the bump recurs a third time, which is the trigger to
   revisit. **CLOSED 2026-08-25 (reek): the bump is not needed and was
   never made.** 2c landed without it (`SePathJoinN` is on main in all
   three emitters) and the floor in `build.ps1` is still 1.25. Measured
   the same day with `deck-headroom.ps1 -Quire codex\build -WithSelf`:
   `cdxtopeScript` is the tightest unit at **1.33** (48 of 64), where on
   08-24 it was 1.23 at 52. The shelf cited here as 19130 does not exist;
   that was the pre-submit number and it renumbered on submit (P-RENUMBER).
21. **plugs 1.57: the Rulebook's over-application rule binds every plug that
   keeps an arity map.** (red, 2026-08-24.) The rule at
   `DevelopersRulebook.md:258` is unqualified, the zig plug just implemented
   it (PR 83, landed 19133), and an exemption would leave riscv shipping a
   correct fix nothing calls and java consulting no arity at all. riscv wires
   `rv-emit-closure-over-apply` into its dispatch, java gains the arity
   check; reek's close-out lane, after the push, with the observed-miscompile
   verification the 1.57 row asks for.

### Ruled by Damian, kept as one line while the work is in flight

- **Widen the sem-equiv trigger. HOLDS until after the release cycle**
  (Damian via red, 2026-08-25). Not marked `Deferred`, because it is meant to
  be picked up rather than skipped: it is gate-cost work and the release full
  gate covers the class meanwhile. Proposer reek, unowned after that.
  **Why:** `$tFrontEnd` is `Syntax|Ast|CodexEmitter|TextFormat|SourceText`, so
  a change to `codex/compiler/opening.codex` never runs sem-equiv under
  `-Internal`. Main went red on `opening: compile-frontend-passes` on
  2026-08-25 and no standing gate could see it; a full gate found it. Same
  shape as 19592, one leg over.
  **Measured 2026-08-25 at head, this box, re-derived rather than carried
  (L-COUNT):** text-stage1 34.4 s, sem-equiv 46.2 s (red, green head; reek got
  47.8 and 50.9 on the red head), text-stage2 24.1 s, text-fixedpoint 0.01 s,
  whole leg ~107.5 s against the 98.5 s `PerforceProcess` records for
  2026-08-20.
  **The structural fact that decides the shape: sem-equiv CANNOT be triggered
  alone**, because it consumes the `stage1.codex` that only text-stage1
  produces. So the cheapest useful widening is text-stage1 + sem-equiv at
  ~83 s, not sem-equiv at ~46 s, leaving text-stage2 and text-fixedpoint on
  the narrow trigger where CodexEmitter is genuinely their subject.
  **Name this before ruling:** the comparator counts MID-BODY prose as body
  tokens while stage1 carries none (fester, 19722), so a compiler CL adding
  prose inside a function body turns sem-equiv red. Widening exposes that
  class more often. Arguably right -- it gives R-PROSE a runner -- but it is a
  consequence, not a free win.

- **5** (2026-08-16): zig 0.16.0, at `D:\zig-0.16.0`.
- **9** (2026-08-18): `widget-panel` flex defaults to 1, and already did;
  the correction is in `ModernDesk.md`.
- **11** (2026-08-18): the identity file stays on the ESP as `IDENTITY.DAT`.
  SHIPPED, Update 48.
- **12** (2026-08-18): the bench auto-unlock is bed-only, on the hypervisor
  bit. SHIPPED, Update 48.
- **13** (2026-08-18): (a), the compiler stops emitting an application for a
  unit constructor. BUILT the same day; account in `plugs-backlog.md` 1.42.
- **14** (2026-08-18): warnings do not gate the build; they are audited at
  the release gate. The standing half of this now lives in
  `ExaminersAssay.md` rather than here.
- **15** (2026-08-20): (a), the linear Board. BUILT AND LANDED the same day
  (root, main 17831), flash follow-on closed at 17839; record in
  `HardwareAbstractionLayer.md` "The sleep rule".
- **17 and 18** (2026-08-19): between `fixed` and `input` there is a bounded
  or budgeted class, and `integer-to-text` is in it. blu designs the class
  and ships it; the shape and the measurements are `CostModel.md` 5.1.
  **SHIPPED (blu, main 17581), and the record closed 2026-08-20.** The
  lattice is `none < fixed < budgeted < linear < growing`;
  `integer-to-text`'s registry row is a bare `budgeted` and `substring`'s is
  `budgeted:3`, the per-argument form 17 asked for, accepted when argument N
  is a literal and refused otherwise. Two arms:
  `codex/test/apps/bounded-budgeted-accepted` and
  `codex/test/errors/bounded-budgeted-exceeded`, whose declaration sits on
  the caller while the offending `substring` is in an undeclared callee, so
  it proves the refusal is transitive. **Re-measured against depot seed
  A6D49D19 rather than taken from the CL**: control exit 0, refusal CDX6101
  at exit 4. `DevelopersGuide.md` "Bounded Functions" is the reader-facing
  account. `fixed` is still unshipped and still blocked on the registry: 81
  of the 265 `bs-alloc` rows read `unknown`, which is the refusing side.
- **20** (2026-08-20): (a), a NEW fact kind carrying old-id, new-id and a
  timestamp, and the Review pane shows a PROPOSAL chain. Reasoning and the
  orphan requirement are in the WORKS-44 row; val owns the work.
  **The kind is 42, not the 40 this line first said** (val, 2026-08-20): 40
  is `persist-kind-index-snapshot` and 41 is the `FactArchive` base, both
  live, so 42 is the first free number above the block and the chapter says
  why. The finding that opened the item measured "the fact kinds run 30 to
  39 with none for supersession", which is true and does not mean 40 is
  free; the ruling inherited that inference and the number was checked
  rather than trusted at the moment of use. The RULING is the additive new
  kind, never a particular integer.

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `codex/foreword/core/VirtioBlk.codex`, `codex/plugs/arm64/Arm64Runtime.codex` block helpers and fs servicer, `codex/compiler/opening.codex` `ir-emit-roots`, `build/test-cross-disk.ps1`, `codex/test/{fs-layer,fs-servicer}.*` | fester, 2026-08-16, CrossLaneFilesystem step 4 (landed 16224) and the RISC-V twin. The `Arm64Runtime` claim is the block/servicer sections only and is **by root's agreement**; the rest of that file is root's |
| `codex/plugs/riscv/RiscVRuntime.codex` block helpers (the twin) | fester -- DONE, landed main 16474 on 2026-08-17; this row said FREE for a day after. `fs-layer`, `fs-servicer` and `fs-deny-runtime` all pass on the riscv lane through `build/test-cross-disk.ps1` |
| `codex/os/kernel/{VirtioNet,VirtioBlk}.codex`, `codex/plugs/pe/Arm64PeWriter.codex`, `build/build-arm64-img.ps1` and its generator | FREE -- root closed the OracleCloudArm64 DMA/timeout residues 2026-08-18. Kernel-side `codex/foreword/core/VirtioBlk.codex` is fester's |
| `tools/codex-vm.c` | **reek, 2026-08-24, for the dead-harness row (red's grant). The work's actual shape is the ROW's, not this line's: reek corrected it at main 19205 after reading the source (the "parse the flags" half is really interactive serial, and `extract-x86-output` is unrevivable at any price), so read the unowned row, never a restatement. Announce to blu before touching the NAT paths.** Previously: fester's 2026-08-22 `-run-list` claim (landed 19089) and blu's 2026-08-18 NAT-census claim (landed; the census counters are in), both expired by landing. Older still: FREE -- root landed the PCI-bridge device model 2026-08-18 (DeviceEmulationCatalog queue): `-pci-bridge` puts a header-type-1 bridge on bus 0 -> bus 1, config is bus-aware now, `codex/test/pci-bridge-scan` shows `pci-scan-all` descends (count 5 bus1=1 with the flag, 3/0 without). One owner at a time; announce |
| `build/test-cross-batch.ps1` | **FREE. The parallel-block throw class is CLOSED across all three harnesses** (fester, 18-08 here, 20-08 for the rest): `sweep-app-classes.ps1` and `test.ps1` carry the same `Read-LogShared` helper now, the latter through its generator `codex/build/testScript.codex` with drift measured at 0 both before and after. Account in `ExaminersAssay.md` |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `build/boot/diag/**` (`Diag.codex`, `diag-arm.ps1`, `diag.img`, and the probes as they are lifted into stages) | root, 2026-08-18, DiagnosticStick.md step 1 (landed root 16819). Step-2 lifts by the lane that flew the probe, coordinated with root |
| `apps/works/GopDesk.codex`, `apps/works/GopComposite.codex`, `apps/works/GopFiles.codex`, `apps/works/GopIcon.codex`, `apps/works/GopSettings.codex` (new, stage 5), `codex/foreword/ui/**` | **val, 2026-08-20, the Shell Refinement campaign** (`docs/Designs/Active/OS/ShellRefinement.md`). red confirmed the same day that nothing of theirs is in flight in the desk: it was claimed alongside `GopBoot`/`GopWizard` because the identity ceremony reached into it, and all four identity stages have landed. **The announce-before-you-start rule stands and is not suspended by this claim**, and so does checking which `ds` cells are already spoken for in the Appearance section (the cell 48 collision, 2026-08-11). `GopComposite` is named because stage 1 replaces its text primitive; fester's clip half (main 17846) must survive that and is an arm on the stage, not an obstacle to it. `GopFiles` and the new `GopIcon` joined the row at main 18180, stage 2's type column; `comp-custom`'s icon tag is the next thing to reach `GopComposite` and `comp-text` stays fester's |
| `apps/works/GopEdit.codex` | **RELEASED 2026-08-20 (val), the Editor is done.** WORKS-14, WORKS-15, WORKS-18 and WORKS-19 all closed; what outlives the claim is `works-desk-contract.md` 0.6, which carries the Editor's standing rules |
| `apps/works/RepoProtocol.codex` and `RepoProtocolPersist.codex` (the `Supersession` record and its fact kind) | **RELEASED 2026-08-20 (val), the work landed.** Ruling 20 (a), additive, and the kind is **42** rather than the 40 the ruling named, because 40 and 41 were both live; the chapter says why. Free for the next lane |
| `apps/works/AgentBundle.codex` and `codex/test/apps/agent-bundle-*` | **RELEASED 2026-08-20 (val) UNSTARTED, and it is open work rather than a closed row.** Track D item 9, claimed the same day and never begun; Damian's direction via red was to close out what the lanes hold and not draw new work on top, so it goes back rather than sitting under a name. **Whoever takes it, the ground is surveyed:** the bundle's OWN refusal paths (`gguf-parse-header`, model digest, manifest size) are KAT-only and have no arm, and the layer under it, `Gguf`, is item 16 and was closed 2026-08-16, so the floor beneath is already guarded. Read `ab-check-signature`, `ab-check-model`, `ab-check-size` and `ab-check-digest`; the verdict type is `BundleOk` against `BundleRefused (Text)`, so each guard can be ablated separately |
| `apps/works/GopReview.codex` | **RELEASED 2026-08-20 (val), WORKS-44 closed whole.** Paging, wrapping, reason text, the detail scroll and the supersession view all landed. `GopFacts.codex` was always red's and stays so. Still open in that row and NOT this file's: the store is re-read per key, which is right for a stick and wrong for a large store |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `apps/works/GopFat16.codex`, `Gpt*.codex` | FREE -- announce |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/test/cost/**` and `docs/Designs/Active/Features/CostModel.md` | blu. 3.3 shipped at main 16020, rule 3 at 16118; what is left of it is COMPILER-7 |
| the integer-literal lexer and text emitter; `codex/plugs/csharp/**` and the `build/` DDC harness; `codex/plugs/recheck/**` | val, lane ownerships rather than open work |
| `codex/plugs/**` and `codex/plugs/plugs-backlog.md` | **reek, 2026-08-18, the close-out lane** (handed on from val, whose 2026-08-16 claim this row carried until 2026-08-19). The register is the lane. Includes `codex/plugs/zig/**` (ordinary fleet code, Damian 2026-08-18); excludes the entries other lanes hold (named in the lanes table). **`codex/plugs/wasm/**` split OUT to fester 2026-08-24, Damian-directed: the plug OOMs on compiler-scale IR and prism's REPL needs it. Returns to this row when fester lands the fix.** |
| `codex/plugs/spirv/**` (plugs-backlog 1.24) and every `run.ps1` under `codex/plugs/` (1.15) | reek, with the plugs lane (val released it 2026-08-19 with the rest of the register) |
| `build/plug-oracle-test.ps1`, `codex/test/plug-oracle-arith.*` | **blu, 2026-08-18.** val released both: the zig wiring they were held for landed at 15687 and 1.13 is done. val's claim is per-ENTRY, not tree-wide |
| `deck-headroom` | fester |
| `codex/foreword/shell/**` and `codex/build/*Script.codex` generators (the ScRaw removal and the readability campaign) | **reek, 2026-08-16, by Damian's direction** ("you can own this bit, as no other agents are actively workin on this code"). Catalog and order: `docs/Designs/Active/Build/ShellDslReadability.md` |
| `codex/foreword/compress/**` (`Deflate`, `Lz4`, `Lz77`, `Rle`, `Brotli`) and `core/OtaBoot.codex`, `core/Aes256.codex`, `core/KeyboardLayout.codex` (Track D 10.1 item 18) | **reek, 2026-08-16**, red's routing. Seed-reachability is measured per file before each half, not assumed from the row |
| `codex/foreword/core/FactDisk.codex`, `core/SourceDefWire.codex` | FREE -- announce, and it takes the token (seed-affecting) |

A claim nobody honours is worse than no claim (the `ds` cell 48 collision,
2026-08-11). Announce before you go into a claimed or FREE-announce file.

## Standing rules that gate nothing but bind everyone

Battery runs are Damian's (release proofs excepted, per the release
skill). Goldens stay parked during active GUI work. No new platform-wide
register. Prose about our own code is deleted in files you touch. The
em-dash stays banned. `-Jobs 8` on every parallel harness. Do not lower
`deck-headroom -MinMargin` to clear a red. `print-line` CONVERTS and
`print-line-raw` is byte-exact (main 14809, 2026-08-13; a wire emitter wants
`-raw`, everything else wants the plain name; `DevelopersGuide.md` "Effects
and Act Blocks" is the account and said the opposite until 2026-08-18).

### Declined, and therefore not available work

Damian has ruled these out. They were carried in one agent's memory file,
which is why they kept being re-proposed by everyone else; they are here so
the ruling is reachable by whoever is about to spend a session on one.

- **Line-level debug info.** A statement about what Codex is for, not a
  scheduling call, so it does not come back when the calendar clears.
- **An app compile gate.** Compiler work must not be coupled to app drift.
- **The ARM64/RISC-V LIR retarget.** What landed stays; the rest is not
  reopening.

- **Plug arms for targets whose runtime is not on this box.** Damian,
  2026-08-25: *"we can skip the plug work that requires runtimes we don't
  have i dont need my box polluted with the sins of a hundred thousand more
  engineers."* So `char-encode` has arms in the five plugs that can be
  executed and compared here (python, javascript, zig, csharp, wasm) and
  will not get them in ada, cobol, elixir, fortran, maui, nim, objc, pascal,
  winforms or wpf. Do not install a toolchain to close these.

  **The count is ten and the shape is two.** Eight of those plugs already
  lack `code-to-char` or `char-code-at`, so they cannot construct a `Char`
  at all and a migrated call site is already dead in them: nothing changes.
  Only **ada** and **fortran** can build a `Char` today and would newly lose
  a site to `char-encode`. Both are recorded in
  `build/plug-builtin-baseline.txt`, so the gap is known rather than silent.
  Anyone reading "ten plugs are missing an arm" as ten pieces of work is
  reading a number where there is a structure (L-ADJECTIVE).
- **The store cutover** waits on infrastructure and is not available work.

Declined is not deferred. Do not re-propose one of these, do not build a
smaller version of it, and do not open a design that assumes it. If you
think a ruling has been overtaken by events, that is one sentence to Damian,
once.
