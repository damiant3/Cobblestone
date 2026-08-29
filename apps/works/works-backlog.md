# Works -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

**`works-desk-contract.md` is the standing rules for the desk** -- the `ds`
cell map and the rule about pointers in it, the base heap mark and how a pane
exits, the cursor bracket, why the palette arrives as a parameter. Read it
before adding a pane or taking a cell. This file is what is missing; that one
is what must not be broken.

## WORKS-59: FIXED. The Browser's second pill restore killed the guest, a regression this lane shipped at 20497

**RE-MEASURED 2026-08-28 (val) and every earlier statement in this entry was
wrong in the same direction: the symptom is worse than "nothing happens", and
the mechanism recorded as ruled out is the live one.** Head, seed
`FFB76AFD9E981B29`, 1600x900, mouse-driven captures.

**The symptom is a HALT.** The second genuine restore of the Browser from its
pill halts the guest: `Guest halted with IF=0`, `!EXC=06` (invalid opcode) at
`RIP=0x10b936`, `CR2=0x1400000`. Same signature on both runs that reached it.
Nothing renders, so a capture arm sees no frame at all rather than a bad one.

**The recorded "nothing happens" WAS THE INSTRUMENT.** Every desk capture is
taken under `desk.ps1 -Rtc`, which pins the HPET as well as the CMOS. With the
HPET stopped every elapsed reads zero, so `dk-dclick`'s
`el * 1000 <= dk-dclick-ms * tps` is true for any second click on the SAME pill,
and `desk-dispatch` sends a double-click to `desk-pill-minimise`. The window was
already minimised, so the desk repainted the desktop and the frame showed
Welcome plus the pill. **The frozen clock converted the crashing click into a
no-op and the arm recorded the no-op.** Two consequences worth carrying: no
frozen-clock arm can ever exercise a second same-pill click, and a third click
CAN, because `dk-pillc-clear` fires on the pair and makes the next click a fresh
first one -- that third click crashes under `-rtc` exactly as the second does
without it.

**The arms, all on head unless the row says otherwise:**

| arm | clock | result |
|---|---|---|
| Browser, second tab, minimise, pill | live | restores, both tabs, no halt |
| the same, then minimise, pill again | live | **HALT** |
| the same double cycle | frozen | no halt; second click is a double-click, so it minimises and nothing appears to happen |
| frozen double cycle, then a THIRD click | frozen | **HALT**, same signature |
| Files: minimise, pill, minimise, pill | live | restores fully painted with its ESP listing, no halt |
| WORKS-57's arm verbatim (two tabs, dock, open the Monitor, close it over the docked Browser, restore) | frozen | **restores with both tabs** |
| the crashing arm against `GopDesk.codex#140`, the revision BEFORE the reclaim | live | restores, both tabs, no halt |
| the crashing arm with `desk-root-reclaim` ablated to never reclaim | live | restores, both tabs, no halt |

So WORKS-57's arm still passes and the two are not one defect; Browser-specific
stands, now measured on an instrument that can express the failure; and the
cause is the root reclaim.

**THE MECHANISM, AND IT IS THE ONE THIS ENTRY SAID WAS RULED OUT.**
`desk-root-reclaim` (main 20497, D.1, this lane) rewinds the heap to before the
desktop root when `desk-marks-live-above` finds no live entry at or above where
the last root ended. The Browser's mark-stack entry is recorded by
`desk-browser-open` at the frontier of the ORIGINAL open, but `gbr-repaint`
rebuilds `bp-st` at the CURRENT frontier on every repaint (the rule written
above `desk-browser-evict`), and nothing moves the mark when it does. After the
first restore the Browser's live state therefore sits ABOVE the root while its
mark still sits below it, the guard cannot see it, `__heap-restore` frees it,
and the next `desk-browser-reenter` runs `gbr-first-paint` through the freed
`bp`. Files and the 3D panes cannot reach this state because their own `-open`
builds their state once, above their own mark.

**Why the earlier ruling-out was invalid, because the shape repeats.** The
control said "the depot build, which has no guard at all, fails identically".
Both arms of that control ran under the frozen clock, so NEITHER of them ever
performed a second re-entry: both were measuring the double-click no-op, and two
builds agreed about a code path neither one executed. A control that agrees
because neither arm reaches the subject is not evidence about the subject
(L-CONTROL), and the agreement was published in a CL description as well as
here.

**FIXED. `desk-browser-reenter` remarks the Browser's mark-stack entry to the
frontier its state is about to be rebuilt at (`desk-marks-remark`), so
`desk-marks-live-above` sees it and the reclaim declines.** The live-clock
double cycle now restores with both tabs and no halt.

**The obvious fix was tried first and REJECTED on measurement, and WORKS-58
BELOW ALREADY SAID SO.** The contract's row for cells 224/228 said the reclaim
fires "only when the frontier still stands exactly where the last root ENDED".
Implemented, that stops the crash too -- and costs **2,394,032 bytes a
minimise-restore cycle**, because an ordinary re-entry leaves the frontier above
the root end, so the reclaim never fires again and WORKS-58 is undone. That is
the same finding WORKS-58's "THE FIRST FORMULATION WAS INERT" paragraph records
from the other direction, where it read as three arms byte-identical to the
pre-fix build; this is the number under it. **I re-derived it at the cost of a
build and two arms because I did not read the entry two headings down in this
same file** (L-READ). What the contract still had wrong is separate and is now
fixed: it described the inert rule as the one the code implements, and the code
has never implemented it.

**The acceptance, re-measured, and the noise floor is the point.** With the fix,
Files at 1 cycle and at 3 cycles read `0x15cd8b3` and `0x15d43e3`, which looks
like 13,704 bytes a cycle until you run the same point twice: two N=1 runs read
`0x15cd8b3` and `0x15d43a3`, a spread of **27,376 bytes**, so the N=1 against
N=3 difference is inside the run-to-run noise and is not a per-cycle cost. Head
shows the same two values in the opposite order. The reading that carries the
claim instead is the **root address, `0x12dbe63`, identical across every one of
the six readings on both builds**. The rejected frontier rule sat two orders of
magnitude outside that noise, which is how it was caught.

The arm cannot resolve anything under ~27 KB, and it cannot be made to: pinning
the clock is what would settle it and `-rtc` breaks the pill path this arm
depends on.

**`codex/test/desk-root-guard` is the runner and it is IN THE BVT** (root ruled
it in, 2026-08-28, on L-NOGATE: a guest-kill guard nothing runs will rot). It
checks `settled` yes, `live-at-e` no, `live-above` no, `live-below` yes,
`unwritten` no no. Wired through `codex/build/bvtScript.codex` and the emitted
`build/bvt.ps1` in one CL, `check-generated-scripts -Only bvt` at match, 0
drift. The BVT is 76 tests and 137 checks now; deleting the mark test takes
`desk-root-guard` red ALONE, at `live-at-e` and `live-above`, with the diff
printed.

**The sabotage that proved it was itself sabotaged first, and the arm said
PASS.** The first attempt wrote over `GopDesk.codex` while Perforce had it
read-only, `Set-Content` was denied, and the BVT scored the UNMODIFIED build
green -- which reads exactly like a guard that cannot fail. The second attempt
asserts the pattern count goes 1 to 0 on disk before running anything. Any
scripted ablation here wants that assertion; this is the second time this
workspace has produced it.

The end-to-end catch is still a hand-driven capture: a `.uiscript` under
`apps/works/tests/` would record the live-clock double cycle, and nothing
invokes `test-app-gui.ps1` from any gate either.

## WORKS-58: FIXED at main 20493. Every minimize and every pill restore leaked a desktop root

**The cycle table is flat in N and that is the acceptance.** One window open, so
no click can reach another pane's content; N cycles of minimize then restore
from the pill, then the Monitor for the readout:

| cycles | before | after |
|---:|---|---|
| 0 | `0x15fd18b` | `0x15fd18b` |
| 1 | `0x1840fcb` | `0x15276fb` |
| 3 | `0x1ba73f3` | `0x15276fb` |

One cycle and three now read the SAME frontier. Per-cycle growth **1,782,292
to 0**, and it settles 875,152 BELOW the 0-cycle baseline, because the first
minimize also reclaims the root `desk-run` built at boot.

**The fix, and the guard is the whole of it.** `desk-root-note` records the
frontier either side of every `desk-draw` at all eight sites;
`desk-root-reclaim` frees the root a rebuild replaces only when no LIVE
mark-stack entry sits at or above where that root ENDED. A pane opened after
the root has its state above it and freeing that is WORKS-57, so the guard
declines exactly once after any pane opens and permits thereafter.

**THE FIRST FORMULATION WAS INERT AND ONLY THE MEASUREMENT SAID SO.** It
guarded on the frontier still equalling `root-end`, which can never hold: every
rebuild site allocates after the root, and `desk-loop`'s per-iteration mark
sits above `root-end`. Three arms came back byte-identical to the pre-fix build.
It failed CLOSED rather than open, which is the direction to fail in, but a
change that reads as a fix and reclaims nothing is exactly what a green suite
cannot distinguish from a working one (L-FALSIF).

**Regression arms.** Files survives three cycles fully painted with its ESP
listing; the Files-plus-Edit two-pane restore is pixel-identical to the pre-fix
frame, which is the guard correctly DECLINING; the buried close moves
`0x24cee7f` to `0x1f86a17` with Edit surviving. Every `-reenter` path brackets
its own allocations with `__heap-save`/`__heap-restore`, so none of them leaves
persistent state above the root.

**What is left.** The first cycle still costs, because the root that predates a
pane sits below that pane's mark and cannot be freed while it lives; that is the
stranding option D is for, not a separate defect. And WORKS-59 above is
unexplained.

## WORKS-57: FIXED at main 20387. A window close destroyed every other pane's state, and a pill restore painted an empty window

**Both halves are fixed and the arm that proves it has a depot control.**
`desk-wnd-close-to` now does what `desk-app-close-plain` does about memory --
kill the closing pane's entry, reclaim dead entries from the top, restore to
the reclaimed target or to nothing when a live entry sits above -- and keeps
every surviving pane's state instead of handing down `desk-apps-empty`.
`desk-pill-restore` now dispatches to the pane's own `-focus`, which re-enters
a live pane and opens a dead one.

**The arm, and the second tab is the point.** Open the Browser, add a second
tab, dock it, open the Monitor, close the Monitor over the docked Browser,
then restore the Browser from its pill:

| build | result |
|---|---|
| depot | bare desktop, no pills, the Browser destroyed |
| fixed | the Browser restores with BOTH tabs and its page |

A Browser rebuilt fresh shows ONE tab, so a single-tab arm would have passed
on both builds and proved nothing (L-CONTROL). Also run: the app sweep at
`-Jobs 4` against the SUT, CHECK OK, 265 clean, 5 known-dirty, 0 regressions;
and all 27 test chapters citing `GopDesk` compiled clean against the SUT.

**WHAT STILL HAS NO RUNNER, which is why this shipped.** Nothing in the tree
calls `desk-pill-restore` or `desk-wnd-close-to`. Both are effectful and take
a framebuffer, so there is no unit arm today, and the decision they now share
-- which mark to restore to and which states survive -- is written inline in
both rather than in something a test could call. A guard wants either that
decision lifted into a pure function over the mark stack, or the arm above
recorded as a `.uiscript` under `build/test-gui.ps1`. Until one exists this
path is checked by nothing (L-NOGATE, L-UNCALLED).

The account of how the cause was first got wrong is kept below, because the
wrong version reached main and someone re-reading the fix should see why it
is not about eviction.

### The original row, and the correction

Found 2026-08-27 (val) while re-measuring the ShellRefinement 6.4 frontier
table, which is where the numbers live. Mouse-driven arms on the desk at main
20359, seed 61C81B04, 1600x900.

**THE FIRST VERSION OF THIS ROW NAMED THE WRONG CAUSE AND IS CORRECTED HERE
IN FULL.** It said raising a docked Files calls `desk-files-open` and so
evicts the Browser. **A pill click never reaches `-open`:** `desk-dispatch`
sends it to `desk-pill-restore`, which sets the window state to normal,
raises it in the registry, repaints, and passes `apps` through untouched.
Nothing is evicted on that path. The eviction reading came from reading
`-open`, finding `desk-browser-evict` and stopping there because it explained
the symptom -- L-MECHANISM, and the cheap discipline it names (grep the line
your mechanism runs through) is exactly what was skipped. The measurements
are unaffected; only the cause was wrong.

**A. `desk-pill-restore` never re-enters the pane, so a restored window comes
back empty.** It raises and calls `desk-wnd-paint-all`, which paints the
frames and chrome; nothing asks the pane to redraw its content, and no
`-reenter` is called. Measured twice in the same arm set: a restored Files
and a restored Browser both paint a title bar and an empty client area,
against a live Browser that paints a tab bar, an address bar and the page.

**B. Closing a window while another is open frees the survivor and wipes
every pane's state.** `desk-app-close` (`GopDesk.codex:1518`) asks the
registry for a next window and, whenever there is one, routes to
`desk-wnd-close-to` rather than to `desk-app-close-plain`. That path does
`__heap-restore (peek-32 ds desk-mark-cell)` -- the DESK BASE MARK -- and
hands `desk-apps-empty` down to `desk-loop`. So the surviving window's state
record is dropped and the heap goes back below where its `-open` allocated.
Measured: dock Files, dock the Browser, raise Files, close it, and the
desktop comes back BARE, welcome frame and no pills.

**Its own prose says why this was believed safe, and that premise is the
defect.** `desk-app-close:1511-1516` states that "every windowed pane keeps
its state in a block `desk-run` allocated BELOW that mark, so the window left
standing survives it". True of the light panes, which own `ds` cell blocks
allocated in `desk-run`. **False of the four heavy panes**, whose `Just st`
is built by `-open` above their own pushed mark -- which is the entire reason
the mark stack exists.

**What this changes for the stranding work.** `desk-app-close-plain` is the
only path that kills a mark entry, reclaims, and picks a restore target, and
by `:1521-1523` it runs ONLY when the closing window is the last one. So the
buried-mark case the option-D ruling is aimed at is largely unreachable
through the close button: a close with another window open never consults the
mark stack at all. Whatever the allocator fix turns out to be, it lands on a
path that today is bypassed.

**Open and deliberately not guessed at.** B restores to the base mark, and
yet the frontier after that arm still sits 6,480,104 bytes above a baseline
desktop (ShellRefinement 6.4). Those two do not fit together and the residual
has no mechanism yet. It is the next measurement, not a paragraph.

## WORKS-56: the band's cached depth is measured EMPTY, so the content box overlaps it as soon as a pill exists

Found 2026-08-27 (val) while measuring what a pill-only strip costs for
ShellRefinement 6.7.3. **Measured, not reasoned**, with the band laid twice
under identical conditions but for its pill set:

| geometry | band with no pills | band with one pill |
|---|---:|---:|
| 1280x800 faceless, bottom | 28 | **36** |
| 1600x900 faceless, bottom | 56 | **72** |
| 1600x900 CMUNSS, bottom | 56 | **72** |

`dk-task-init` lays the real taskbar and caches its measured extent in
`dk-task-band-cell`, and it runs from `desk-run` at boot and from the settings
apply on style code 7 (an edge change) and **nowhere else** -- not on open, not
on minimise, not on close. At boot no window exists, so the cached number is
always the EMPTY band's. `dk-task-px` answers it, and all four `dk-cbox-*`
reserve from it, so **the moment the first window is minimised the band lays
out 16 device pixels taller at 1600 than the content box has reserved for it**,
and the bottom of the content box is underneath the band.

**The arm that exists cannot see this and that is the interesting half.**
`desk-pane-origin` asserts the laid content slot against the content box at all
four edges, which is exactly the right question -- and it calls `dk-task-init`
itself, with a `ds` whose registry is empty, so both of its roads are computed
from the same empty band. An instrument built from its subject (L-BOTHARMS).
The repair to the arm is to give it a registry with one window in it.

**The cheap fix is to measure the band with a SPECIMEN pill always**, the way
`dk-strip-init` already measures a strip, so the cached depth is the maximum
the band can need and cannot under-reserve whatever the window state. That
costs the empty desk 16 device pixels of glass at 1600 and removes a defect
that is live on every desk with a minimised window. The alternative, re-running
`dk-task-init` on every open, minimise, restore and close, keeps the empty
desk's pixels and adds a measure to four hot paths.

**FIXED, and the fix is the max of two separately measured trees.** A wrapper
panel holding the bar and a specimen together was tried first and abandoned:
it answered 72 for an empty band and 88 for one holding a pill where the bar
alone answers 36 and 72, and those numbers could not be accounted for against
the engine's padding rules without reading it. A number that cannot be
explained does not ship. `dk-task-init` now measures the band and a
one-specimen strip separately and caches the larger, so the depth is the
maximum the band can need whatever the window state, and `band empty` equals
`band with a pill` on every geometry measured.

**What moved, all of it accounted for by name.** The horizontal band grows by
the empty-versus-pill difference, 8 logical, so 8 device at 1280 and 16 at
1600; the content box loses exactly that; and the centred welcome frame moves
half of it. `desk-pane-origin` and `desk-welcome-box` re-recorded on those
rows only. **The vertical rows do not move at all**, because a vertical band is
already wider than a strip of the same pills, which is also the answer to
6.7.3's open question: a pill-only strip is the SAME depth as the band on both
axes once both are built by `dk-strip-panel`, so there is no saving to take and
no new constant to invent. Every containment and centring verdict in both arms
still holds.

**`desk-pane-origin` still measures with an empty registry** and the fix is
what makes that harmless rather than the arm being repaired. Giving it a
window would be a real strengthening and is not done here.

## WORKS-51: `gop-scene-backbuffer` measures a pane that no longer paints without a window

Found at the Update 50 release battery (red, 2026-08-25). The test is
unchanged since 2026-08-05 and answers `rows land by stride: 0 of 24, pane
fully painted: no` at the release seed: main 19574 (6.5's 3D panes) made
`ScenePane` derive its view rect from the WINDOW's rect via `gsc-place`
before every frame, and the test drives the pane with no window, so the
rect is empty and nothing paints. The change was intended and verified by
capture in the campaign; the casualty is the instrument (L-INSTRUMENT:
repoint the arm at the part that still answers its question, never soften
it). Skipped with a `.skip` citing this row; the repointed setup needs the
window fact the pane now reads, which is this lane's plumbing to supply.
Un-skip with the repair.

## WORKS-50: one pane, two names, depending on where you look

Found 2026-08-25 (val) while giving the taskbar pill its app icon, and left
unfixed deliberately because deciding which name wins is not an icon change's
business.

**`desk-wnd-title` and `gpr-entries`'s `ge-label` disagree for four of the
fifteen panes.** The launcher row says `Editor` and its window titlebar says
`Edit`; `Browser` against `Web`; `System Info` against `Monitor`; and
`Programs` has a window title but no launcher entry at all. A person opening
Browser from the start menu gets a window that calls itself Web.

**There is a third table and it is NOT the defect**: `desk-focus-name` gives
deliberately short names for the taskbar's `tasks` slot (`Web`, `3D`, `Fish`,
`Edit`) because a band is narrow. That one is fine. The question is only
whether the two FULL names should agree.

**The cost of the divergence is already paid once.** `dk-pill-icon` had to
become a second table keyed by focus id rather than a join against
`gpr-entries`, because the join misses on those four and `gicon-named` answers
the `file` icon for an unknown name rather than refusing, so four pills would
have worn the wrong picture with every count still agreeing. Fixing the names
would make that join sound and let one table go.

**What settling it needs.** Pick the surviving name per pane, then change
whichever table loses. `desk-wnd-title` is read by the titlebar, the pill and
`desk-window-registry`'s expectation; `ge-label` is read by the launcher, the
start menu, and `desk-gpr-icons`'s id join (`"gpr-" & ge-label`), so a label
change moves widget ids and `gpr-id-scan` with them. Neither is a rename in
one place, and `codex/test/apps/desk-chrome-icons` counts rows by those ids.

## WORKS-48: a webserver pane in the guios

Damian, 2026-08-24, routed by red. A desk pane over
`apps/works/WebServer.codex`: start and stop, and a LIVE REQUEST LOG. It
serves BOTH plain HTTP and the browser app's `codex://` wire, whose client
side is already written (`PageFetcher`, `DataChannel`).

Bed first, through codex-vm's NAT port-forward, which is host-into-guest and
is the direction a server needs (`docs/OperatorsManual.md`; outbound needs no
`-portfwd` because the guest reaches the host at `10.0.2.2`). Metal later.
blu consults on the net side after COMPILER-18. `docs/PM/CurrentPlan.md`,
"network demo pair", carries the shape.

**Not started, and it is queued behind the windows campaign** Damian asked
for directly the same day (`ShellRefinement.md` stage 6). Nothing here is
blocked; the order is his to change.

**BOTH QUESTIONS SETTLED 2026-08-28 (val), from the code rather than by
ruling, which is what they turned out to want.**

**1. The serve cannot live in a pane step, and the reason is mechanical rather
than architectural.** `net-io-accept` (`codex/os/net/NetIO.codex:107`) BLOCKS by
construction: it recurses on `net-driver-recv-frame` until the connection
reaches `TcpEstablished` or `net-io-max-polls` is spent. A desk step handles ONE
event and returns, so a step that calls it stops the desk -- the clock, the
mouse and every other pane -- for the whole poll budget. So the serve is a PUMP
driven from `desk-loop`, not a pane: one non-blocking `net-driver-recv-frame`
attempt per iteration, fed to `transport-process-frame`, with the session in a
`ds` block. That is the shape `desk-clock` already has (it runs from the loop,
gated, and belongs to no pane), and it answers the entry's worry directly: the
server keeps serving while another pane is focused because no pane owns it. The
pane becomes a VIEW over that state -- start, stop, and the log -- and owns none
of the serving.

**2. The log is a fixed-size ring in a `ds`-pointed block, allocated in
`desk-run` before the base mark.** The pattern is already there twice:
`dk-prev-cell` (232) and `dk-pedge-cell` (212) are both pointers to fixed
blocks allocated before the mark for exactly this reason. Unbounded is not an
option and never was -- there is no collector -- so the ring drops the oldest
entry and the pane says so rather than pretending it holds everything. Three
`ds` cells are free (244, 248, 252); taking one is announced in the file-claims
table first, the way the contract asks.

**THIS ROW NAMED THE WRONG CHAPTER, AND THE RIGHT ONE IS ALREADY A REAL
SERVER** (val, 2026-08-28, after Damian said so). There are TWO chapters called
`WebServer`:

- **`codex/os/net/WebServer.codex`, 17,619 bytes, cited as `Net chapter
  WebServer` -- this is the one.** It carries a concurrent event loop:
  `web-mux-accept`, `web-mux-feed` (demux by segment owner, SYN to accept),
  `web-mux-serve-conn`, `web-mux-drain`, `web-mux-sweep` (idle reap on
  `web-sweep-interval`), content-length reassembly (`wb-content-length`,
  `wb-request-total`, `wb-headers-end`), plus `web-serve`,
  `web-serve-concurrent` and `web-serve-framed`. Four tests already pin it:
  `codex/test/web-mux-{concurrent,idle-reap,lifecycle,reassembly}`. Ten
  chapters consume it, `Prism.codex:340` (`web-serve (prism-route ...)`)
  among them.
- `apps/works/WebServer.codex`, 3,411 bytes, a router and handler table with
  no listen, no accept and no socket, whose only callers in the tree are four
  lines of `web-server-test.codex` (L-UNCALLED). **A same-named chapter in
  another quire is exactly the contested-name hazard `cite-override-quire`
  exists for**, and it is why every consumer writes `cites Net chapter
  WebServer` rather than the bare name. Whether this stub should survive at
  all is a question for Damian, not this row.

**So the estimate above is wrong in the useful direction and the pump is
ALREADY FACTORED.** One iteration of `web-mux-loop` (`:345-361`) is exactly
what a desk-loop iteration must do: `net-driver-recv-frame` (non-blocking,
empty list when there is nothing), `web-mux-feed route m frame port`, and
`web-mux-sweep` on the interval, bracketed by `__heap-save`/`__heap-restore`
on the empty-frame path -- which is the desk's own per-iteration idiom
already. Only the LOOP wrapper blocks. The pane therefore needs no new serving
code: it needs the `WebMux` in a `ds` block and those three calls per
iteration.

## WORKS-25: `xhci-connect` opens controller 0 and three chapters use it

`GopUsb.usb-attach` walks every xHCI controller (`usb-hosts` recurses on
ordinal, short-circuiting once keyboard, mouse and disk are all found), and
that is the path the desk and the diagnostic image take. Beside it,
`GopXhci.xhci-connect` is `xhci-connect-at scan 0 0` -- **the first
controller, unconditionally, whatever is on it** -- and three chapters still
call it: `GopUsbMsc.msc-connect-fresh:197`, `GopUsbKbd:168`,
`CamCapture:40`.

So which controller a caller gets depends on which entry point it happened to
use. On a machine whose devices are split across two controllers, anything
reaching USB through the mass-storage, keyboard or camera chapter's own
connect sees ordinal 0 only. The ASUS is such a machine: sitting 9 banked
`ctl1 1b21:1242 at 5:0.0` beside the Intel part at ordinal 0.

Not yet a measured failure, and that is the honest state: the diag stage uses
the walking path, so this has not been caught costing anything. What it needs
before a fix is a decision about what each caller actually wants, and they
differ -- the keyboard wants the controller with a keyboard, the disk the one
with a disk, and "the first one" is neither. `codex/test` has no arm on any of
the three today. `-xhci-two` in codex-vm builds the two-controller machine
(`diag-arm.ps1` arm `xhci-two`), so the bed to measure it on exists.

Found while measuring for the `xhci-two` arm, 2026-08-21 (fester).

## Standing check: every chapter must compile on its own cites

```powershell
build/check-subset-cites.ps1 -Root apps\works -Jobs 8 -Kernel seed\Codex.cdx
```

**Run it after any cite change and after adding a chapter.** It builds each
chapter as its own unit -- the chapter plus the transitive closure of what it
cites -- and lets the compiler answer, so a chapter using a name it never cited
is caught here instead of by whoever next assembles a subset. That is L-SUBSET,
and the glob build cannot see it. The gate does NOT run it.

**Read the coverage line, not the verdict.** A unit that does not compile is a
chapter that was not judged, and a run can have failures above a clean verdict.
Since 2026-08-21 the script names them and says INCOMPLETE rather than OK.

| # | Capability | State of the gap |
|---|---|---|
| WORKS-47 | **A sidebar or taskbar button's ICON is drawn outside the button, below its bottom edge** | Found 2026-08-21 (val) while proving ShellRefinement stage 5's persistence on the glass, so it is not that work's doing and predates it. **It is present in the DEFAULT scheme, and that is the reading that rules out the colour scheme as the cause.** The pristine arm (seahawk, no keys, untouched ESP) paints the Shutdown icon's stand and base BELOW the button's rounded bottom, onto the wallpaper, and the Console icon carries the same overhang in the sidebar. `terminal` only makes it easy to SEE, because a dark icon against a light grey desktop is legible where the same pixels against navy are not, and reading it as a `terminal` defect is the wrong turn this row exists to prevent. Reproduced on three independent boots at 1600x900, and again after a rebuild. **The class is the one `comp-fit-px` closed for text and nobody closed for icons**: the icon is placed and drawn with no vertical bound against the box it was given, which is WORKS-41's unclipped panel one widget along. Not diagnosed past that; `GopIcon.codex` and `comp-custom`'s icon tag in `GopComposite` are where to start, and it belongs to stage 2 of the campaign. |
| WORKS-46 | **Every KERNEL DiskFacts consumer is still IDE-only** | FirstBoot's store, IdentityManager's sysdb and the repo persist called outside the desk all read through the block syscalls, so a UEFI boot and a USB stick see an empty store. The desk's own road is fixed (red, 2026-08-19: `GopFacts` over `GopDisk.disk-read-into`/`disk-write-into`, crossed both directions by `codex/test/apps/gopfacts-cross`). On metal the write half additionally rides WORKS-9's sink refusal, because a USB write that wedges wedges a verdict too. |
| WORKS-44 | **The Review pane re-reads the fact store on every key** | Right for a stick, wrong for a large store, and it is all that is left of this row. Read, cast, paging, wrapping, reason text, the detail scroll and the supersession view all landed 2026-08-19 and 2026-08-20 and the depot carries them. On a real stick it is WORKS-46 that keeps the pane empty, not this. |
| WORKS-41 | **The newtab page overflows a short box, and everything laid past the bottom is clipped away with no way to scroll to it** | Found 2026-08-18 (val) converting the Browser to a step. **MEASURED 2026-08-26 (val) and the mechanism this row used to state is wrong**, so the arm is `codex/test/apps/browser-newtab-overflow`, which drives the same functions `gbr-repaint` drives. Width held at 1024, only the height moves: at a 768-tall box nothing overflows (`info` ends at 750), at 400 five nodes are laid past the bottom (`links`, three of its buttons, and `info` at y 466), and at 1600 nothing overflows, which is the control. **`links` is the only flexible child and absorbs the slack** -- 470 tall at 768, 1302 at 1600, with a floor of 202 -- so overflow does not begin with ANY height reduction, it begins only once the box forces `links` below that floor, which is a box around 500 logical pixels. **The footer is NOT bottom-anchored**: `info` is the last child of the same `DirColumn` and is placed by the same accumulation as every other child. The real mechanism is one layer down in `flex-col-place` (`codex/foreword/ui/Layout.codex:72-83`), which places each child at a cursor advanced by `h + gap` with **no clamp against the container's bottom**, while `flex-col` clamps `avail` at zero. **And the content does not paint through the footer or into the taskbar**: `gbr-paint` walks the page under `vclip`, and `comp-clip` (`GopComposite.codex:628`) is a true intersection, so the overflow is invisible rather than overlapping. That makes this a REACHABILITY defect, not a painting one. What is still missing is what WORKS-23 wants one widget along: nothing scrolls a panel to its box. **Which screen modes cross the threshold is not settled here** -- this arm lays out against a box it is handed and does not compute the pane rect, which is shorter than the screen by the chrome, the titlebar and the taskbar strip. |
| WORKS-40 | **A very long Files session accumulates one frame per directory change, and nothing reclaims it until the pane closes** | Found and bounded 2026-08-18 (val), by design rather than by accident. A step that stores a new list into its state must keep the frame that holds it, so each directory change retains that iteration's repaint garbage along with the listing. **Measured: it is fully reclaimed on close** -- frontier and desk mark after ten directory changes are bit-identical to after none, and 100 changes in one session still render correctly. The exposure is one Files session left open across thousands of directory changes on the 128 MB boot arena, which is not a session anyone has run. **The unmeasured number is the per-change retention itself**, because reading the heap frontier needs the Monitor pane, and only one pane is focused at a time, so the instrument cannot observe the subject. That is the real gap here: an in-guest heap reading that does not require focus, which is the same missing instrument WORKS-32 asks for one level along. Until it exists this row is a bound, not a measurement. |
| WORKS-33 | **The system menu is still the full-screen Programs pane rather than a panel opening above its button** | The taskbar itself is done (2026-08-18, val): `desk-taskbar` is `menu` / `tasks` / `task-clock`, the clock moved out of the top bar into the band, `desk-dispatch` folds the `menu` id into the same `prg` the sidebar's Programs button uses so there is one dispatch and not two, and the `tasks` slot renders live apps from the marks cell. What is left is the menu's SHAPE. Its arm is `codex/test/apps/desk-taskbar-hit`. |
| WORKS-37 | **The idle desk was leaking 3.6 KB a second, and F12's verdict is now transient** | Both found 2026-08-18 (val) while converting `desk-monitor` to a step, neither by the arm that was running. **The leak is FIXED in the same CL.** Measured on the Monitor `memory` row: 69,896 B of gap after 10 seconds idle against 213,656 B after 50, linear at 3,594 B/s. `desk-clock` builds Text and walks the taskbar subtree on every RTC-second edge and `desk-loop` reclaimed none of it. On the 128 MB bare-metal arena that is about ten hours to exhaustion. It had never been seen because reading it means sitting idle and THEN opening the Monitor, and every earlier measurement opened a pane at once, which paused the leak by replacing the idle loop with the pane's own bracketed one. `desk-loop` now takes a mark per iteration and restores it on every continuing path; the mark sits above `root` so `root` survives, and the close path does not restore it because `desk-mon-close` restores to the BASE mark instead. After: frontier identical at 22 s open, 62 s open and 50 s idle, all `0x645b80`. **The clock half is FIXED 2026-08-18; the F12 half is still open.** The band is CONTESTED, not merely shared. Any pane that renders `desk-chrome-with` paints the taskbar from the widget tree, whose `task-clock` label is empty by design, so it ERASES the clock text the desk painted; and `desk-loop` calls `desk-clock` BEFORE the step, so in an iteration where both fire the pane wins and the band is left blank until the next second. Worse, the two gates read the clock differently -- the desk uses `rtc-seconds` (guarded, retries across the RTC update window) and the Clock pane uses `rtc-seconds-unguarded` -- so they see the second turn over on different iterations and which one lands last is a race. Measured in the Clock pane: taskbar clock present at 26 s open, absent at 14 s after a keypress-driven repaint. Every step pane has the milder version of this; the Clock pane has the worst because it repaints every second. **Proposed fix, one CL across all five step panes**: the step returns 2 for "alive and repainted" beside 1 for "alive", and `desk-loop` forces exactly one clock repaint when it sees 2. That costs one repaint per pane repaint instead of one per loop iteration, which is why it cannot simply be forced unconditionally. FIXED, and not by the return-value change proposed above: every chrome render now goes through `dk-chrome-paint`, which renders and then sets `desk-second-cell` to a value no second can equal, so the next `desk-clock` repaints unconditionally and the next one is microseconds away rather than up to a second. That is one band repaint per chrome render instead of one per loop iteration, and it needed no protocol change and no signature churn across six panes. `desk-draw` does the same for the desk's own paint. Verified on the Clock pane, which is the worst case because it repaints every second: the taskbar clock is present in four captures at 14, 17, 21 and 26 seconds open, and present in the exact keypress-driven frame that was blank before. Appearance after two scheme changes shows it too, in the lcars accent. **Still open: F12's verdict is still transient**, for the same reason and now the only one, and 2026-08-18 it has a number: in the Issues pane the verdict was on the glass 250 ms after the keypress and gone at 400 ms, and the window varies with where in the RTC second the shot lands, which is exactly what being erased by the next clock repaint looks like -- it is hand-painted into the band and the next chrome render takes it. Where transient notifications live is still the open question. Also still open, and unrelated to this fix: an UNCONVERTED loop pane freezes the clock rather than blanking it, because a pane like Files draws a window instead of re-rendering the chrome, so the desk's last-painted text simply stays. Converting those panes is what fixes that. |
| WORKS-38 | **F12 reports `shot write FAILED` in the bed, on every path** | Observed 2026-08-18 (val) while running the stage 5 controls, NOT investigated and NOT caused by that change: the verdict strip reads `shot write FAILED s7 m0 c1 p3 w12 f0 10 r0` both inside the converted Monitor pane and at the plain desk with no pane open, which is the arm that rules out the step conversion. `build/desk.ps1` defaults to `-Disk seed/Codex.img` and copies it to `build-output` before attaching, so a writable ESP is supposed to be there. The shot still TAKES and still paints its verdict, so this is the write leg only. Filed because it is a failure seen and not waved through; the cause is unknown and the row should not be read as a diagnosis. |
| WORKS-34 | **The taskbar band is too short to host a themed button** | Found 2026-08-18 (val) while building the taskbar's system-menu entry, and it is why that entry is a label rather than a button. Measured: the band is `dk-task-h` = 28 LOGICAL at every resolution. A themed button needs 36 to draw its box -- the taskbar panel's own padding takes 8 (`edges-uniform 4` in `dk-theme-palette`'s `base`), the button's margin 4, its padding 8, and `comp-glyph-h` is 16. At 28 the box comes out 16 tall with 16-tall text overflowing it, which renders as a green sliver with the text cut through the middle; captured before the entry was changed to a label. Labels do not have the problem because `comp-draw-node` draws a `WkLabel` as text only, with no box to clip against. **Raising it is not a one-line change**: `dk-task-h` is also what the hand-drawn panes reserve (`desk-mon-draw`, `dk-win-y`), and `GopScene` hand-syncs its own `gsc-taskbar-h = 28` to it, so the band, the reservation and the 3D pane's copy have to move together or the band will cover pane content at 1600 and above. |
| WORKS-35 | **The Monitor pane's rows are hard-coded to a 160-pixel sidebar and land under it at 1600** | Found 2026-08-18 (val) while closing WORKS-31, which was the same defect one file over. `dk-mon-x = 160` is PHYSICAL and unscaled, and `desk-mon-row` draws at `dk-mon-x + 12 * s`. The desk's sidebar column is 160 LOGICAL, so at 1600 and above (`ui-wscale` = 2) it is 320 physical and the Monitor's label column starts at 184, inside it. Unmeasured on the glass: the reasoning is from the source, and the capture has not been taken. The fix is the one WORKS-31 used -- scale by `ui-wscale`, or better, read the sidebar's laid width off the tree instead of restating it. |
| WORKS-30 | **The 3D camera's aspect is the literal 1.333. FIXED for the desk's 3D pane; three other surfaces still carry it** | **DONE for `GopScene` (val, 2026-08-18).** `gsc-camera` now takes an aspect and applies it with `__record-set c "c3-aspect"`, and `gsc-frame` computes it per render path, because **the two paths do not share a viewport**: software renders into `tgt` at `cw x ch` while the host rasterizer renders into `gv` at `cw x (ch - gsc-label-band)`, a 20-row difference. Setting the field rather than changing `camera3d-new` leaves the other eighteen callers and every engine golden untouched, and keeps the change out of `codex/foreword`, so it is not seed-affecting. **Measured, frozen clock, three aspects including one the fix had never seen:** sphere ratio **1.037 at 1024x768** (viewport 1.3333), **1.042 at 1600x900** (1.8462, was 1.440), **1.038 at 1920x1080** (1.8333). All within 0.5 per cent of each other. **And the software path was checked separately, because the fix gives each path its own aspect**: toggling `G` at 1600x900 reads **1.041** against the host path's 1.042, so the two agree to 0.1 per cent (the bounding boxes differ slightly, 277x266 against 270x259, which is the 20-row viewport difference and is expected). **Two controls passed.** The 1024 arm, which was already correct because its GPU viewport is exactly 1.3333, did not move at all (224x216 before and after). And at 1600 the bounding box went 373x259 to 270x259 -- **the height is identical and only the width changed**, which is what an X-only correction must do and what a wrong fix would not have produced. **The old 7 per cent residual was my arithmetic, not the renderer**: the first version of this row divided by the SOFTWARE target height while the measured path was the host rasterizer. Against the correct viewport the residual is 3.7 per cent, and it is explained -- the ball is `mesh-sphere 700 12 8`, a 12-segment polyhedron whose silhouette ratio reaches `1/cos(15 degrees)` = 1.0353 against the measured 1.037, so **1.037 is the mesh's own floor and not a defect**. **What is still open:** `GopFish`, `apps/globe/GlobeDemo.codex` and `apps/engine-demo/EngineDemo.codex` all build cameras with the same three-argument `camera3d-new` and therefore still run at 1.333; none has been measured. The durable question they raise is whether `camera3d-new` should keep a default at all, or require the aspect and force all nineteen callers to say what their viewport is. Previous measurement, retained because the arm is reusable: | (`-Rtc`, which pins the HPET as well as the CMOS so the RTC-driven orbit angle is identical) so the two frames differ only in resolution. The yellow sphere's bounding box is **224x216, ratio 1.037, at 1024x768** and **373x259, ratio 1.440, at 1600x900**: a circle at 4:3 and a visibly stretched ellipse at 16:9. **The mechanism is a constant, not a missing term, and the first version of this row said the opposite.** `camera3d-new` (`codex/foreword/engine/Scene3D.codex:56`) takes eye, target and fov and NO aspect argument; its constructor sets `c3-aspect = 1.333` (`:64`); `camera3d-proj` (`:71`) hands it to `mat4-perspective`, which divides X by it (`codex/foreword/math/Matrix4.codex:87`). **`c3-aspect` occurs exactly three times in the tree, all in `Scene3D.codex`, and no caller overrides it.** So on-screen aspect is `viewport_aspect / 1.333`. **The fix is to set `c3-aspect` from the content region at the caller. Do NOT add a correction to `r3d-project-clip` instead** -- the camera already divides by it and a second correction squashes the scene the other way at every resolution; if it ever moves into the projection, `c3-aspect` must become 1.0 in the same CL. Both render paths share the camera (`GpuScene.codex:110`), so the `G` toggle must not change the symptom. **The RATIO between arms predicts to 0.2 per cent** (predicted `(1440/800)/(864/668)` = 1.3917 against measured `1.440/1.037` = 1.3886) **but the ABSOLUTE prediction is out by a consistent 7 per cent** (0.970 and 1.350 predicted against 1.037 and 1.440 measured, both high by 1.069) and that residual is unexplained -- mesh non-sphericity, the pixel filter clipping the silhouette, or the Cordic fov are all uneliminated. Do not close this row on the ratio alone: if the arms converge after the fix to a value far from 1.0, something else is scaling the sphere. **Calibrate the pixel filter before trusting it**: a first attempt thresholded at `R > 130`, measured only the specular highlight, and returned a 106x36 box that is not the sphere. The sphere is `B < 20 & R > 40 & G > 30 & 0.6R < G < R`. |
| WORKS-24 | **The Clock's write to the RTC has never run on hardware that accepts it** | The Clock accessory (main 14905) sets hour, minute, second, day, month and year by writing the MC146818 inside a Status-B SET window. **No bed run can tell a correct write from a broken one**: `tools/codex-vm.c` answers CMOS reads from the host clock and drops every write on the floor -- the line is literally `/* write to CMOS -- ignore */` at 11098. Measured 2026-08-13: bump the hour, press Enter, and the face comes back showing the old hour, which is the emulator behaving as written and says nothing about the guest. **What IS verified is the part that carries the risk.** `codex/test/apps/clock-encode-test` round-trips `clk-decode-hour (clk-encode-hour h b) h` for all 24 hours across all four modes the part can be in (BCD or binary, 12-hour or 24-hour) and every field range, and it pins four values computed by hand FROM THE SPEC rather than from the code -- 18, 146, 129, 35 -- so a round trip that passes with both halves wrong still fails. The port writes themselves are two instructions. **What is left is one boot on metal**: open Clock, `s`, change the minute, Enter, then leave the pane and re-enter it. If the new time sticks, the SET window and the encoding are right on real silicon. Until someone does that, do not describe setting the clock as working. |
| WORKS-16 | **An editor crash that went away, with no proof of what fixed it** | 2026-08-11: Damian opened `SOURCE.SRC` in the Edit pane on the interactive VM, saw the first screen render, and the VM died. **It does not happen any more** -- he opened the whole file and navigated it on the same path after main 14685. Four headless beds never reproduced it at any point (28 keys over 70 s, 50 keys at 300 ms, an open/close/reopen cycle, 120 scripted mouse samples across the list), so **the two defects fixed in 14683 are a plausible cause and not a demonstrated one.** The plausible half: he picked the file with the pointer, and until 14683 every hovered-row change ran a two-function cycle that both left stack frames behind and skipped its `__heap-restore`. That is the one path his run exercised heavily and the bed did not, because the bed's mouse never moved until the last arm. **New evidence 2026-08-11, and it argues against the fix explaining it.** Three headless runs that day ended with codex-vm exiting early, no frame captured, **and no `!EXC=` line, no "Guest halted", no watchpoint** -- the stderr simply stops mid-word. That is not the shape of a guest fault; it is the shape of the HOST process dying, which is also what "crashed the whole vm" describes. Other runs with the same command line and the same guest completed normally, so it is intermittent. Nobody has looked at whether codex-vm can fault on the host side under this load. **Left open deliberately.** If it stays gone through real use, delete this row; if it returns, the guest's own `!EXC=` line is the thing to capture (`build/desk.ps1 -Wait -Force -Disk seed/Codex.img` prints it to the launching console) rather than another headless arm. |
| WORKS-19 | **Typing near the top of a large file moves the whole tail, and MEASURED it is not slow. The gap buffer is not indicated; what is unmeasured is metal** | The buffer is flat, so an insert at offset 0 of `SOURCE.SRC` shifts the whole tail one byte and reindexes every line. That much is true and it is the design. **What this row used to claim past that was "it is visibly slow on that file", and 2026-08-19 (val) measured it false in the bed.** Per keystroke at offset 0 of 2,896,050 bytes: `ged-shift-up` **5 ms**, `ged-reindex` **5 ms**, building the 30 visible rows of Text **2 ms**. End to end on the real surface, ten characters typed 60 ms apart (16 a second, faster than most people type) ALL land: the pane reads `L1:11  2896060 bytes`, and a 307-byte control typed identically reads `L1:11  317 bytes`. The two are indistinguishable, which is the arm that matters, because a cost proportional to file size would separate them. So a gap buffer would be a data-structure change against a 12 ms keystroke, and nothing we can measure asks for it. **Two corrections to the old row while here:** the line count is 62,184 and not 59,461 (L-COUNT, it was carried forward), and the first version of the probe reported 2 ms for the shift because the loop results were unused and the work was being ELIDED -- the numbers above are from the version that threads an accumulator and prints it, and 400 reindexes summing to exactly 400 x 62,184 is what says the loop ran. **What is genuinely open is metal**, where the desk's iteration is far slower for reasons that have nothing to do with this buffer, and where nobody has typed into a 2.77 MB file. Probe: `docs/Probes/edit-keystroke-cost.codex`. |
| WORKS-17 | **The syntax scheme is still the editor's own** | The frame half is DONE (main 17702) and the contrast half is DONE (main 17716): `GopFiles` and `GopEdit` take a `Palette` from the desk and paint through named role accessors, and a selected row is the accent as ground with `theme-ink-on` picking a legible ink from that palette's own two text colours. Computed rather than a stored `pal-on-accent` field, because there are 36 `Palette` literals in 32 files and a stored token fails silently when somebody adds the 37th; `codex/test/ui/theme-ink-on` pins it and prints "SAME, the ground is being ignored" if the helper ever stops reading the ground. A scheme wanting to override a specific pair can still be given a field. **What is left is the syntax question, and it should not be answered by reflex** (blu, 2026-08-11): `syn-colour` maps a token class to a colour and this work did not touch it, so a code editor keeps its own scheme. Decide whether `Theme` grows a syntax block or `SyntaxHighlight` keeps its own and only the frame follows the desk. A `minimal` desk on a white ground needs a light syntax set that does not exist, so it is real work either way and not a rename. |
| WORKS-12 | **A pane visit is not reclaimed. Twelve panes done, one deliberately not** | Noticed 2026-08-11 (blu) bracketing the browser pane's own loop. **The stated cause was only the smaller half** (reek, 2026-08-15). Stranded pane state is real, but the dominant per-visit cost is the desk never unwinding: every pane tail-calls `desk-loop` again, so the `root` tree each exit rebuilds is stacked on top of the root it replaces and neither is ever freed. A mark taken inside the pane cannot reach that root, because the root is allocated after the pane returns. **The fix is one base mark, not a bracket per pane**: `desk-run` stores the frontier in `desk-mark-cell` (ds 4) after the state blocks and BEFORE the first root, and a pane exit restores to it before `desk-draw` rebuilds. **Measured on the desk under codex-vm, four browser visits, base 0x6320a8:** frontier 0x6611f0 without the restore (193,352 bytes above base, about 48 KB a visit) against 0x6376e0 with it (22,072, the live root plus the Monitor pane's own paint, and flat in the number of visits). **DONE for browser, calc, cal, trk, dif, files, monitor, programs, clock, style, console, fish and scene** -- the last two had their own local marks, which reclaimed the pane but not the root, and now restore to the base like the rest. **NOT DONE for `desk-edit`, and that is the interesting one.** `ged-init` allocates an 8 MB buffer and a 1 MB line index on first use and parks both POINTERS in a persistent `ds` cell, so a base-mark restore would free the memory and leave the cell pointing at it, and `ged-ensure` would hand the next visit freed heap. The honest fix is in `GopEdit`: allocate on entry and release on exit, or reserve the block in `desk-run` where every other pointer cell already lives, which costs 9 MB at boot for a pane that may never be opened. That is a GopEdit decision rather than a desk one. `GopConsole` had the same shape at 1 KB and was fixed the cheap way: `gcon-init` runs eagerly in `desk-run` now. **The rule this leaves behind:** a pointer parked in a `ds` cell must be allocated in `desk-run`, below the mark. The instrument is the Monitor pane's `memory` row, which prints the frontier and the base mark; the gap between them is the answer. |
| WORKS-13 | **A tracker row is not clickable, and the 3D pane never receives the mouse** | The two gaps left after the mouse was wired into every desk pane 2026-08-11 (blu). **Issues:** `gtk-tree` builds its rows through the generic `data-table-widget`, which gives no per-row widget id, so `ev-hit-widget` has nothing to answer with and a click cannot say WHICH row it landed on. Selection stays keyboard-only (up/down). Fixing it means either per-row ids from the table widget, which is a `codex/foreword/ui` change affecting every table in the tree, or the pane computing a row index from the table's own bounds, which re-derives layout arithmetic the layout engine already did. **3D View: CLOSED 2026-08-19 (val).** `desk-scene-step` and `gsc-step` both take `UsbMouse` now and the camera answers it. The design question it was waiting on was settled as a TAKEOVER rather than an addition: the angle came from `gsc-orbit`, which the HPET drives and never stops, so adding an offset to a moving angle gives a view that drifts out from under the hand. The first pointer movement latches `sp-look` and the orbit stops being consulted, so the scene turns itself until you touch it and after that you turn it; there is no way back short of closing the pane, which is the honest cost of one bit. Both 3D panes get it from the one change because they share the step. Neither blocks anything: the cursor tracks in every OTHER pane and the keyboard reaches everything. | **CLOSED 2026-08-19 (fester), and the premise above was stale.** The per-row ids already existed: `dt-row-widget` names each row `<table>-row-<absolute index>` and `dt-cell-widgets` names each cell `<table>-cell-<row key>-<column>`. What was missing was the other end, so `data-table-row-of-hit` reads either shape back to a row index (`DataTable.codex`); `ev-hit-widget` answers with the DEEPEST node, so an ordinary click lands on a cell and only a click in the row padding lands on the row panel, and a helper reading one shape would have missed most clicks. `gtk-row-at` asks it and `desk-trk-hit` brackets a layout, reads the integer and drops the tree -- the shape the desk already documents for hit tests -- and `desk-trk-step` takes `clicked` and moves the selection. **What is proven and what is not:** 14 arms in `codex/test/ui/data-table-hit`, three of which go the whole path a click goes (lay out, `ev-hit-widget` at a POINT, id, row index) and the rest of which cover the misses -- another table, the header, a header button, the footer, an absent key, an empty id. **The mouse itself is unexercised: no bed here can press it.** `desk.ps1` scripts scancodes and codex-vm takes `-keys` and no pointer, so "the desk delivers a click to this pane" is carried by the same machinery five other panes already use and is not tested here. The fear in the original entry -- that this needed per-row ids from the table widget, a `codex/foreword/ui` change affecting every table -- did not arise.
| WORKS-9 | **The ASUS shot-2 timeout is explained** (reek). **The flight card for the next sitting is `docs/Hardware/HardwareSitting.md`, "WHICH BYTES ARE QUEUED: REBUILT AND RE-REHEARSED 2026-08-18"** -- image, hash, flash command, what the operator watches, and what not to conclude. | **The three DRIVER defects behind it are fixed at main 14447** and the account is `docs/Hardware/HardwareSitting.md`, "THE SHOT-2 TIMEOUT ABOVE IS THREE DEFECTS DEEP". A retried chunk now survives a dropped completion, so a second sustained write is no longer lost either way. **What is NOT explained is the original question**: `xhci-fuel` is a spin count and nobody has converted it to a duration on that box, and no bed can, because codex-vm completes every transfer before the guest spins once, so `f` reads exactly 1000000 there. The instrument for the next flight is the `f`, `l`, `r` cells on the shot line rather than the reset-and-retry arm this row used to recommend: `f` is the SMALLEST fuel any COMPLETED transfer left behind, which reads off the transfers that succeeded instead of the one that failed, so a small `f` says the budget was marginal and an `f` near 1000000 says the fuel is innocent and the device stopped answering. **The arm blu originally routed here was wrong and is recorded as wrong so nobody rebuilds it**: it called `xhci-recover-endpoint`, which leads with Reset Endpoint, defined only for a HALTED endpoint, and a timeout leaves the endpoint RUNNING, so it answers Context State Error and recovery refuses. New bed levers for anyone reproducing this: `-usb-bot-drop N` and Bulk-Only Mass Storage Reset, both in `docs/OperatorsManual.md`. **SITTING 6, 2026-08-20: the ladder returned a NUMBER on metal for the first time.** Flown by red; banked here because the reading was GLASS ONLY and the bank dies at the sink (L-BANK), so a photograph was the only other copy. `sink ladder-stop done=4 rung-sectors=16 rung-bytes=8192 payload-bytes=65536 note=1, wr=128 cc=256 lba=2169 rty=1 ph=2 after=0 chunk=16`. Four sittings of one bit each reached this; it is the threshold reading and every later one is a comparison against it. |
| WORKS-6 | **The boot path verifies under Secure Boot, and GetMemoryMap is sized dynamically** | Re-homed from BootRoadmap.md (B5.1/B5.2) when that design moved to Done 2026-08-05: the shipped stub boots with Secure Boot off; signing the PE for Secure Boot has no design and no owner. GetMemoryMap's buffer is statically sized; a machine with a bigger map than the allowance fails at ExitBootServices. Neither blocks any current flight. |
| WORKS-7 | **DevDebugger hardware watchpoints and VGA split-screen** | Re-homed from DEVELOPER-DEBUGGER.md Phase 4 when that design moved to Done 2026-08-05: DR-register watchpoints are blocked on `mov dr` builtins (a compiler change plus a seed cycle -- neither exists); VGA split-screen was never built. The I/O-bitmap breakpoint row was cut per the design's own reclassify-or-cut ruling. |
| WORKS-5 | **Dev Console and Serial REPL are reachable from the boot menu** | Removed from GopBoot's interface menu 2026-08-05 (they painted "Selected:" and returned; a menu that only offers what works beats one that promises). Wiring Dev Console means running DevConsoleBoot's console without the UEFI ConIn/ConOut it is written against (gone on the Option A path), and its source-tree indexing returns placeholders (README completeness line); as an alternate payload under OVMF it reaches a black screen and OUT OF MEMORY (build-boot-img.ps1 header). Serial REPL means chaining from the payload into the REPL the compiler serves, which is loader work, not menu wiring. Each is more than a session. When one is wired and proven, add its row back to menu-label/menu-count and give it a dispatcher in menu-loop. |
| WORKS-2 | **A GGUF this app WRITES is readable by llama.cpp** | The read direction is closed and measured (main 10603): `build/gguf-foreign-test.ps1` parses four real llama.cpp models, up to a 3.2 GB gemma3 with a 15.7 MB metadata block, agreeing with an independent host parse on version, tensor count, KV count, architecture, tensor-table offset and first tensor name. Nothing checks the other direction. `build/make-agent-bundle.ps1` writes the bundled model and `Foreword chapter Gguf` reads it, so a green `agent-bundle-test` still says only that the two halves agree with each other. Ollama is installed on this box, so the instrument exists: feed it a Codex-written GGUF and see whether it loads. Deferred by Damian 2026-07-26, not blocked. |
| WORKS-3 | **The diffusion app is finished** | `apps/works/GopDiffusion.codex` (ported from `apps/guios/DiffusionApp.codex`; the port closed the GuiDisplay/GuiTimer/GopRender gating) is an unfinished AssetForge UI: the Generate button dispatches to nothing. `df-draw-generating` fills a progress bar from a counter to 20 and produces no image; there is no model load, no sampler, no inference. Finishing it means the button doing something, which is a capability question and not a UI one -- the foreword has `ai/` (NeuralNet, Tensor, Activation) at the scale of `inference-demo` and `neural-test`, which is a long way from a latent diffusion sampler. **Not blocked, and not urgent.** |
