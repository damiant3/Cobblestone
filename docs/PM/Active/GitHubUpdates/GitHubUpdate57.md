# GitHub Update 57

**PUSHED 2026-09-08 evening from `D:\Projects\Cobblestone-root-main` at main head, to github master and gitlab main, no force.** Shipped: seed #778 `B63014D717B1A2F9`, 3,322,781 bytes; `seed/Codex.img` `DD959678725F1833`; `seed/Codex.map` 187,903 bytes; `build/boot/diag.img` unchanged at `6F077EEB` with its 50-of-50 rehearsal record of 2026-09-08 13:04. **This commit also reverses the withholding of the card game that the 3907073c push made this afternoon**: the game (`apps/games/codexmagic`, its mobile app, the edge mesh, the explorer, their tests, generators and designs) returns to the mirror; what stays withheld, on purpose and named in `.gitignore`, is the solver (`apps/games/magic` and now its annotations), the wademo app with its design and tests, and the customer quire. Verified absent on the pushed tree: the solver 0, wademo 0, the customer quire 0, the third-party specifications 0, the `diag-sitting*.cfg` files 0.

**Scope: main CLs after the Update 56 release push commit, 24063 through
the release head 24552, all landed on 2026-09-08.** One day's work, released
the same evening on Damian's direction to cut the release ceremony to the
proofs that matter (the full gate, the battery, the app sweep, the poison
battery and the DDC) and to measure the box while they run.

## Still open from Update 56

- **COMPILER-42, the rewrite pass** (blu): still OFF; the chunk functions in
  `opening.codex` must return the list they build.
- **plugs 2.26** (unowned): 48 plug runners still write their IR to one
  fixed scratch path (L-SHARED).
- **safari on the site** (val): the port runs and grades; the page and card
  are not on the landing page yet.

Closed since: COMPILER-66's comparator (seed 05E254BFF0C46406, 24146),
COMPILER-68 (the BVT batches, with a control), COMPILER-69 (24205), and the
arm64/riscv bed question (Renode is in, 24513).

## Landed this cycle

127 changelists reached `//Codex/main` between the Update 56 push (main
24062) and main 24534 on 2026-09-08 (measured 2026-09-08 by
`p4 changes -l //Codex/main/...@24063,#head`; re-measure at the release head
for the final report, L-COUNT).

**Compiler defects and the seeds that carry them** (red, blu). COMPILER-20
was a live miscompile present in every earlier seed: a closure entered with
more arguments than its remaining arity returned a heap pointer typed as
Integer instead of applying the surplus. Seeds this cycle, in order: #775
27D2386F7AF76F0C closes COMPILER-71, the CDX6020 hazard search that read
only 11 of 20 `AExpr` variants and left a hole a partial traversal could
pass through undetected (24250); #776 82DC1A4CEEA20EFE closes COMPILER-20
above and opens COMPILER-73 for the resulting +9.2% size (24407); #777
F7A1343619F08614 closes the dead CCE-to-Unicode residue of COMPILER-23,
228 bytes smaller (24438); #778 B63014D717B1A2F9 closes COMPILER-73, 197 KB
back off the size regression. The release ships #778. Two earlier seeds,
7A36B22409B9B1D8 (fester's DISK-mode path fix, 24114) and 05E254BFF0C46406
(red's COMPILER-66 all-bindings sort rebuild, 24146), also landed and
shipped ahead of #775. COMPILER-69, an unused `let` bound to an unapplied
partial application, now warns CDX2097 and is closed (24205, seed
75B414046BEE5208). COMPILER-70 folds sine and cosine to an octant, cutting
worst-case error from 5.63e-8 to 9.77e-15 against python; not seed-affecting
(24230). COMPILER-9 and COMPILER-43 close as already-resolved rows, docs
only (24271, 24474). COMPILER-16's ratchet
over the compiler's self-compile warnings stays OPEN on its retention half:
the gate discards the log on success and a lane cannot verify a gate change
(24264). COMPILER-72, blu's audit of ARM64's hand-written field-index
tables (an unobserved table that a sabotaged index would pass unnoticed),
is in progress across several CLs (24244, 24328, 24350, 24366, 24384,
24454) with its checker wired into the gate but not yet closed.

**The build pipeline model, burr 4 ruled** (reek). The generator migration
campaign moved from 21 of 58 generators on the `Pipeline` model to 47,
CLs 24103 through 24517, each proven `match / 0 drift` against the depot
seed under `check-generated-scripts.ps1` before the shipped `build/*.ps1`
is called untouched. Damian ruled the shape of the ten generators blocked
on control flow (24420): a step is a stage or a group, and a group carries
an optional repeat condition, an optional guard condition and an optional
handler stage, covering while/foreach, if, and try/finally. The runner's
vocabulary was designed against `apps/workflow/WorkflowTypes.codex` rather
than invented fresh, with `StepStatus` and `StepSkipped` answering
L-DENOM by the type rather than by convention (24429); root granted the
all-42 fan-out proof of the migrated set in principle, bounded at 4 jobs,
FIFO against the box. The campaign independently rediscovered the same
vacuity guard shape five times in five different hands (L-VACUOUS,
24289 through 24453) and found four other pre-existing gaps: an accepted
but unwired `-Force` switch (24157), an unread `$ForewordDir` assignment
(24103), a stale count that read 34 against a census of 42 (24420), and a
targetless generator that graded nothing while the gate reported 0 drift
and exited 0 (24514). By 24517 the census reads 47 migrated, 1 flat
(target-less by design), 10 blocked awaiting the runner build.

**The diagnostic stick's 50-arm rehearsal flake, closed** (fester, blu).
The symptom (four NIC arms failing in sequence, passing in isolation) was
registered (24067), then a second full run reproduced the same four arms
(24080), then the "always fails these four" reading was refuted by two
independent standalone passes while a genuine ordering defect was
confirmed by ledger (24085, 24087). Harness, payload and seed were each
refuted by measurement in turn, and at head the four-arm failure does NOT
reproduce over three full 50-arm runs (fester, main 24252). A staleness
guard now keys the diag image to the payload bundle's own closure rather
than a stored reading (24252). The shipped image 6F077EEB is unchanged and
was re-rehearsed 50 of 50 today.

**Works desk and Sheets** (val). SHEET-10 lands step by step, a spreadsheet
pane on the desk gaining a cell store view, click-to-select, typing into
cells and DeskApps wiring (24178 through 24381, closed). SHEET-12's two
paint defects close: slack now sits below the grid rather than through it
(24125), and the pane's number/text rendering is shared with `CellView`
rather than reimplemented (24134). SHEET-13 eliminates both of its
redundant painters (24391, 24395). `ShellRefinement` D.4 and D.5 measure
and rule the shape of the next desk increment (24423, 24439). WORKS-47's
icon-axis hardening lands with its sighting recorded as not reproducible
(24460). WORKS-44 is diagnosed and closed: the review pane now holds the
fact store between keystrokes through a sixth `DeskApps` field (24475,
24525, 24527). `DeskBoot`, a temporary that had gotten half-officialised,
is removed on Damian's ruling; its flight-log history stays in
`HardwareSitting.md` (24319).

**The network receive path** (blu). A chain of per-frame allocation cuts:
attribution finds one transport record built twice inside a 208-byte frame
(24210), `TcpTransport` now builds one record per frame instead of two,
208 to 144 bytes (24214), `NetIO`/`Arm64NetIO` follow on the drain path
(24218), and a vestigial `recv-buf` field drops the frame to 120 bytes
(24239). The web mux now keeps a pool of closed connections' transports and
rebinds one on accept instead of minting a fresh 65,536-byte receive buffer
per accept, cutting a connection's cost from 66,328 to 792 bytes (24131);
its pool cap is measured refused rather than assumed (24177). `TrustTransport`'s
identity copy is removed, collapsing 14 hand-written transport rebuilds onto
one call (24435). Two of the cycle's own numbers were corrected by their
author before the release: the 208/144/120 bytes-per-frame figures describe
the unrecognised-frame path only (the reductions hold for every frame, the
absolutes do not), and the design's "14 signatures" is 3 frame signatures
plus address-only entries (24494, 24496).

**The img plug gives every source its own name** (fester, plugs 1.102).
An image can now hold a directory of distinctly named sources instead of
exactly one (24308).

**A withheld block in `.gitignore` for unreleased and customer work**
(root). Every plan, register, update and doc reference to the withheld
material was purged from what reaches the public mirror (24276).

**Perforce process: a server-side guard on CRLF corruption** (root,
P-CRTRIGGER). A change-content trigger now refuses a stray carriage return
in a `.codex` file at submit time (24295, `PerforceProcess.md` 4.8), widened
to every text-typed file with 29 `.failing` sidecars stripped of a stray CR
in the same pass (24297).

**Rulings landed.** Burr 4 is ruled shape 3, the group node carrying
repeat, guard and handler (Damian, relayed by root, 24417, 24420).
**Renode is in** (Damian, 24513). Fan-outs now self-serve on a runtime
measurement rather than a fixed default (24534, `box-sample.ps1`). The
manifest quire progresses: a quire is a directory or a manifest and the
compiler is citable through it (24339, 24348, 24365, 24400, 24458, 24501,
24523), clearing the Phase B stage 2 blocker for the build-scripts-into-Codex
work above. COMPILER-70 answers Steve Howell's issue 122 (24166).

**Outside contributions.** No outside PR landed this cycle. Steve Howell's
issue 122 was answered by COMPILER-70 (24166, 24230).

## What the release proofs found

Two red gates, both process: the first gate launch died in nine seconds because its log sat in the directory the clean phase removes, and the second died at `check-shell-raw` on six generators whose raw-shell counts had risen in the day's migrations without their baseline rows moving (a blanket `-Update` cleared it and hid three lanes' rises; `check-shell-raw` gained `-Update -Only <generator>` and the rule is now "edit your own rows"). One red proof, the poison battery's first run, not reproducible (table). The DDC's plug build cleans `build-output/` and took the `ddc-arm` scaffold with it. The release itself, from Damian's order at 15:50 to every proof green, took 50 minutes of wall clock beside a working fleet; the afternoon's purge of the card game was reversed inside the same release on Damian's correction.

| proof | result |
|---|---|
| Full gate (every phase) | green at 24552 in 777 s (test-compile 117 s, test-run 308 s, app-sweep 61 s); the first launch died in 9 s because its log sat in `build-output/`, which the clean phase removes, and a second launch died at `check-shell-raw` on six generators whose raw-shell counts had risen in the day's migrations without their baseline rows moving (baseline moved, 24551) |
| Sut === seed | `build/output/Sut.cdx`, `seed/Codex.cdx` and `build-output/bare-metal/Codex.cdx` byte-identical, `B63014D717B1A2F9`, 3,322,781 bytes |
| ir-fidelity `-Grade` | green, 8 cases, unexpected 0, 24 compiles in 7.5 s, every case naming the release seed |
| Battery `-Tier all` | 1,795 total, 1,746 pass, **0 fail**, 49 skip; compile 181 s, run 148 s; the admission guard admitted 2 of 4 slots at 7.5 GiB free; oracles scalar 2013/2013, vector 130/130, cce 1485/1516 with 31 in documented gaps and 0 unexplained |
| App sweep | 293 units, 290 clean, 3 known-dirty, **0 regressions**, `-Check -Jobs 4`, 96 s, kernel the release seed |
| Poison (`-Poison`, `-Tier all`) | poison seed `B3C490B100B10ADD`, 3,322,781 bytes; first run 1,795 total, 1,745 pass, **1 fail** (`heap-bracket-shape`, FAIL_OUTPUT, 2 slots admitted); the same poison-compiled binary rerun alone by `test-run.ps1` printed all nine expected lines, and the full poison battery rerun 1,795 total, 1,746 pass, **0 fail**, 49 skip, so the red is not reproducible and its cause is unmeasured beyond "under two parallel slots" (L-SHORT names the shape) |
| DDC witness | **HOLDS**: poison seed and Roslyn arm both 3,322,781 bytes, 95 differing bytes all inside the signature region 40..135 and 0 outside; Roslyn built the emitted 4,329,881-byte `Codex.cs` with 0 warnings and 0 errors in 10.5 s. The plug build cleans `build-output/` and took the `ddc-arm` scaffold with it, so the csproj is re-created after the plug build, not before |
| `check-doc-counts` | exits 0 on all 61 claims; 5 had drifted and were re-measured at main 24546 (red) |
| Map | `seed/Codex.map` taken from the gate's `Sut.map`, 187,903 bytes, validated against the seed's embedded MAP1: 5,660 rows, 0 missing, 0 address differences, 0 size differences |
| Img | `seed/Codex.img` rebuilt on seed #778, `DD959678725F1833`, 16 MB, 65 source chapters aboard |
| Diag | **unchanged at `6F077EEB`**, its 50-of-50 rehearsal record taken 2026-09-08 13:04 against those exact bytes; fester's three full 50-arm runs today found the Update 56 four-arm failure not reproducible and the staleness guard now keys on the payload bundle closure; `check-shipping-images` OK (the baked config is the checked-in default) |
| Box memory (the diagnostic half) | `build/box-sample.ps1` beside every proof, 527 samples at 5 s: free-memory floor **2.53 GiB** at 16:35:28 (the gate's test-run phase) with 5 guests holding 2437 MB together, about 487 MB each, beside 13 pwsh at 1315 MB; peak guest count **18** at 16:05:52 (test-compile) holding 2342 MB together, about 130 MB each, with 4.35 GiB free. The 3 GiB-per-guest bar in CoordinationProtocol overstates a compile guest by about twenty times and a run guest by three; the battery's own admission guard admitted 2 of 4 slots at 7.5 GiB free |
