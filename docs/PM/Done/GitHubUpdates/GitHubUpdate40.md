# GitHub Update 40

**Scope: main CLs 14534 onward, opened 2026-08-10.** Update 39 covered
13136 to 14533. Accumulate this cycle's themes here as they land; every
number in the final report gets re-measured at the release head, not
carried forward.

Every proof number in "Numbers" was measured at seed `527C2C75` on
2026-08-12 rather than carried forward from Update 39 (L-COUNT). The
battery closed green at 1,399 pass / 0 fail after two stale oracles were
re-minted and a third was skipped by ruling; the skip is described under
"Still open" and is the one thing here a reader should not mistake for a
pass.

---

## The headline: the desk stopped being a demo

Update 39's headline was the trusting-trust witness -- the question of
whether the compiler can be trusted at all. This cycle is the answer to a
plainer question, and it is the one a person actually asks when handed a
machine: can I use it?

At the start of the cycle the bare-metal desktop drew panes and read the
keyboard. It now takes the mouse in every pane, runs a web browser, runs
an editor that saves back to the stick it booted from, renders a 3D scene
through the GPU with shadows, and follows a colour scheme it lets you
change. None of that is hosted on anything. The drivers, the compositor,
the font rasteriser, the FAT writer and the network stack are all in this
tree, compiled by the seed the release ships.

### The pointer reaches everything

The cursor used to be hidden on pane entry and never pumped, so the arrow
died the moment a pane opened. Calc, Calendar, Issues, Diffusion and
Monitor now pump the pointer and paint it, and every repainting pane
brackets its repaint hide-repaint-update -- a repaint under a displayed
cursor leaves the cursor's save buffer describing pixels that no longer
exist. The calculator keypad is clickable, answering the scancode its
label would have produced so that one dispatch table stays the only one.

Panes hit-test by laying the tree out again inside a heap bracket rather
than memoizing it, which is safe because pane state is a `poke-32` block
and not heap.

The cursor itself is an arrow rather than a white box: `cursor-update`
paints a two-mask arrow with the background showing through the notch,
and save/restore still cover the full rect so the restore stays a superset
of what the arrow paints. `codex/test/apps/desk-cursor-arrow` measures the
erase against an in-memory framebuffer (56 painted, 56 after a move, 0
after hide) and is falsified with the restore disabled.

**Still open:** a tracker row is not clickable, because `gtk-tree` builds
its rows through the generic `data-table-widget` and there is no per-row
widget id for `ev-hit-widget` to answer with. The 3D pane never receives
the mouse at all -- `desk-scene` is not passed it. Both are filed as
WORKS-13 and neither blocks anything.

### A browser, on the metal

The browser became a desk pane and then became usable, through a chain of
defects that were each invisible from the layer above:

- **The display path wrote to a banked VBE window nothing scans out**, at
  two VM exits per pixel.
- **codex-vm crashed on the host** when VBE mode was set, because the
  framebuffer was activated at runtime without committing the guest region
  it reads back.
- **The UI 5x7 font stored letters alphabetically while CCE orders them by
  frequency**, so the glyphs were wrong.
- **The event loop never reclaimed its transient heap**, which is what the
  OUT OF MEMORY at boot actually was. `codex/test/heap-frame-reclaim`
  pins it, falsified at 43/5/1000 with the restores removed.
- **Page links were not clickable**, because `widget-find-at-point` tests
  `wn-bounds` and the tree in `tab-content` was never laid out --
  `browser-render-frame` laid out a separate copy. `BrowserState` now
  holds the tree that was actually drawn, with its heap mark, so it can be
  released before a dispatch allocates above it. A click cycle leaves the
  heap where it found it, measured at 0 per cycle.

**The desk stopped declaring Network.** Every network-carrying function in
the browser funnelled through one call, so narrowing that one function
dropped the effect from 22 signatures in `Browser.codex` with zero
duplicated bodies, and the desk chain is back to `Device.Port`. The wire
path is kept by name, uncalled and pruned. The binaries shrank with it:

| Binary | Before | After |
|---|---|---|
| DeskVm | 819,028 | 707,227 |
| GopBoot | 919,032 | 850,710 |
| browser | 497,468 | 394,985 |

The guard on that is the good part: `codex/test/apps/browser-offline-load`
declares `Console` alone, so rewiring the named arm stops it compiling.

### An editor that writes to the stick it booted from

`GopEdit` is the Codex IDE's editor pane on the desk. Type, Enter to
split, Backspace and Delete, F2 to save. Every save keeps one generation
as `NAME.BAK`, and F3 reverts by LOADING the `.BAK` into the buffer rather
than copying it over the file -- so the medium is untouched, the pane goes
dirty, and the next F2 makes the abandoned content the new `.BAK`. One
generation therefore gives undo and redo of a save from one mechanism,
with no second slot and no rename.

Verified end to end on the image: `SOURCE.SRC` moved to a new chain at the
edited size, `SOURCE.BAK` kept the original chain at the original size,
and both copies of the first line were found on the medium with the typed
byte in front of exactly one of them.

The boot stick now also carries the 64 compiler chapters as individual
files in `SRC/` alongside the concatenated `SOURCE.SRC`, which is what
makes editing a chapter practical: `SOURCE.SRC` is 2.77 MB and every
insert near its top moves the whole tail, while `Parser.codex` is 78 KB
and instant.

**Stated plainly because it is the honest state:** the Edit pane lists the
root only and drops directory entries, so it cannot yet open the `SRC/`
files it most wants (WORKS-20). There is no keystroke undo (WORKS-14).
Saving `SOURCE.SRC` takes more than 23 seconds; the pane now paints
"SAVING, do not power off" before the write, but there is no progress
(WORKS-18).

### 3D through the GPU, with shadows

The desk's 3D View renders through the host rasterizer, in five stages, of
which four have landed:

- **Stage 0** detects the GPU and registers the app only when a usable one
  is present. That needed a compiler fix first: `runtime-init` was zeroing
  the capability word when it re-initialized the process table, silently
  dropping GPU and every other capability for every payload that called
  it. That is the one seed rebuild of the cycle.
- **Stages 1-2** put a viewport (scissor) in codex-vm's rasterizer and
  render the pane through it. The GPU scene path that `EngineDemo` carried
  privately moved into a shared `GpuScene` chapter parameterized by a view
  rect.
- **Stages 3-4** are shadow mapping: a light-space depth pass plus a
  per-fragment compare, moved across from `r3d-shadow-test` unchanged.

Parity is measured rather than asserted, by differencing each renderer
against ITSELF with shadows off: **shadow-mask IoU 75.1 per cent, with
both controls at exactly 0.**

Two defects fell out of measuring it. **Shadow acne was in BOTH
renderers**, and predates the campaign: the depth map is now filled from
the faces pointing away from the light, so a lit surface no longer casts
the depth it is then compared against. Cube-face self-shadowed pixels went
4,344 to 1,063 in software and 5,470 to 1,934 on the GPU, the remainder in
both being the torus cast shadow, unchanged. And **the GPU pane free-ran
at 271 fps** with nothing consuming the frames, saturating the rasterizer
threads and the display copy; it is paced to 60 like the cinematic path.

Shadow edges on the ground were a comb of texel-shaped teeth, because the
light view-projection fits the whole scene and the ground is 3,000 units
across. A finer map on the host path plus a 3x3 filtered comparison
replaced the binary in/out test; penumbra pixels on the ground band 1,177
to 3,015 and the frame rate is unchanged at the cap.

**Stage 5 is a judgement, not a missing capability:** whether the GPU path
becomes the default in 3D View. The software renderer is still the default
while that is open.

### The desktop looks like something

The compositor honours the theme's adornments and an Appearance pane turns
them on and off. The Monitor pane and the F12 verdict strip read the
palette. The desk scales, so 1600 and 1920 are a bigger desktop instead of
a 1024-sized one in a big frame.

**Not done, and structural rather than lazy:** `GopEdit` and `GopFiles`
paint fixed colour constants. `GopDesk` cites `GopEdit`, so `GopEdit`
reading the desk's palette directly would be a citation cycle -- the
palette has to arrive as a parameter. Whether a code editor's syntax
colours are its own scheme or the desktop's is a separate design question
and is deliberately unanswered (WORKS-17).

---

## The Shell DSL drift campaign ended, and it found real defects on the way

The build harness is being replaced by scripts generated from a Codex
description. The risk in adopting a generator is that the generated script
silently differs from the shipped one, so every adoption this cycle was
proven as two arms, byte-compared, with a sabotage control that fires.

That method paid for itself repeatedly, because three of the generators
were wrong in ways nothing would have reported:

- **The bvt generator's test list held 16 of the 75 tests.** Adopting it
  unread would have dropped 59 from the gate -- every ECDSA, X.509 and TLS
  test, every proof, the whole repository set -- **while the BVT went on
  printing PASS.** The `.disk` sidecar handling was missing entirely, and
  `-Jobs` defaulted to 4 against the standing ruling of 8.
- **The compare-codex-semantic generator exposed a false PASS in the
  sem-equiv gate leg itself.** The shipped precedence table gave `&` and
  `|` the same rank where the compiler gives Ampersand 3 and Pipe 2, so
  `(a | b) & c` compared EQUAL to `a | b & c`.
- **The run-plug generator emitted a bare catch** where the shipped script
  catches `IOException` only, so any fault in the socket receive loop was
  swallowed and reported as "plug produced no output" -- blaming the plug
  for a harness error. Proven by injection: typed exits 1 with the real
  error, bare exits 6 saying no output.

Every generated script now carries a header warning that a hand edit must
not be submitted, from one definition cited by all three emitters.

**A correction belongs here too**, because the first report of the
precedence fix was wrong in the safe-looking direction. It was published
as inert on the real corpus, on an identical verdict over 5,237
definitions. It is not inert: dumping and diffing all 10,474 normalized
bodies showed it changes 4 of them, all real boolean `(a | b) & c` in the
parser and type checker. The old table stripped their load-bearing parens
from BOTH sides equally, which is exactly why the verdict never moved. A
pass/fail is the narrowest possible view of a transformation, and a
symmetric error cancels in it perfectly.

---

## An optimisation that was silently deleting the browser

A source-emitting plug -- the HTML and JavaScript backends -- resolves a
Codex call by its NAME. The browser primitives are stubs: `set-render (fn)`
is literally `= 0`, and the real body is the plug's own runtime function,
matched by name when the page is emitted.

The leaf inliner does exactly what it is supposed to do with a body like
that. It substitutes the constant and deletes the call. **So the primitive
became a no-op in the emitted page -- no error, no warning, a page that
builds clean and half-works.** An optimisation that is correct for a
machine-code backend is destructive for a backend whose whole contract is
that the call survives to be recognised.

The fix is a backend-specific IR pipeline. `passes=text-plug` resolves to
constant folding alone, so the calls reach the plug intact.

**The check written alongside it is the part worth reading**, because the
obvious version of it passes for the wrong reason. An unknown pass name is
the identity transform, so a compiler that has never heard of `text-plug`
runs NO passes at all and preserves every call -- indistinguishable, from
the outside, from the fix working. `build/check-text-plug-passes.ps1`
closes that two ways: a control arm first proves the DEFAULT pipeline
erases the same calls, and the subject arm reads the compiler's own
CDX4030 pipeline report to confirm which passes actually ran, failing
explicitly if the name was passed through unresolved. It names the kernel
that answered, too, because `build-output` is not the depot seed and goes
stale after a merge-down.

Run against the shipped seed at this head:

```
OK: default erased 2, text-plug preserved 3 of 3 [seed\Codex.cdx [527C2C75092B0F9A]]
```

It is labelled an instrument rather than a gate: nothing runs it
automatically.

**This is the enabling half, not the whole of it.** The HTML plug's widget
event bridge still calls `_wkOnClick`, `_wkOnInput` and `_wkOnPick` with
nothing declaring them, and `if(_wkOnClick)` on an undeclared identifier is
a ReferenceError rather than a falsy test, so the listener still dies the
first time a widget is clicked. `set-render` and `register-handlers` still
emit as the constant 0. That is html-backlog 1.2 and it is open.

---

## The doc-count checker had stopped checking the README

Update 39 named this and did not fix it: "24 README claims are NOMATCH, so
the checker's patterns stopped matching and those claims are verified by
nobody. That is how 158 went stale unseen. Named, not swept."

It is swept now. `build/check-doc-counts.ps1` exits 1 on a claim whose
pattern no longer matches, precisely because a doc that changed shape has
quietly stopped being checked -- and README.md had been rewritten, so 22
of its 51 claims were unwatched. The quire table had only lost its bold.

What had drifted behind the silence:

| Claim | Said | Measured |
|---|---|---|
| compiler files | 63 | 64 |
| compiler lines | 53,652 | 54,138 |
| foreword modules | 430 | 431 |
| library modules | 590 | 591 |
| plug source modules | 145 | 148 |
| app modules | 1,010 | 1,008 |
| codex/test files | 1,403 | 1,449 |
| seed cdx bytes | 2,755,007 | 2,759,215 |
| seed cdx SHA-256 | `AF4E14D9..` | `B56AD5AF..` |

**The seed digests are the ones that mattered.** `seed/Codex.cdx` moved at
main 14668 and the README went on publishing the Update 39 artifact, so
the root-of-trust digest table on the public front page named a file
nobody could reproduce.

The checker now also carries a second anchored claim wherever the README
states the same fact twice. A regex takes the first match, so the
duplicate was unchecked -- and it had already diverged, which is how the
1,010-against-1,008 disagreement surfaced at all. 51 claims to 59.

---

## Hardware: the stick is an OS

The compiler paints its own DISK rungs and `build/disk-arm.ps1` forces all
six. A sink-ladder probe and its calibration landed. Both A5 stick images
were rebuilt with the painted payload and bed-verified.

**A5 is ready to fly and has no owner.** That is the one thing in this
cycle blocked on a decision rather than on work.

The network lane is still short of the metal: `NetIO` connect now
retransmits a lost SYN and the ARM64 send path stops at a refused chunk
instead of walking past it, both closed with negative controls, and
`arm64-send-refusal` passes on the Renode ARM64 lane. The I219-V link
bring-up on the real part is still unexplained.

---

## Corrections, and why there are so many of them

This cycle retracted an unusual number of its own claims. Listing them is
the point rather than an embarrassment, because each was caught by an
instrument someone built on purpose:

- **The "unclaimed stick flew" claim was withdrawn.** It rested on checks
  run against one agent's masters only, while another builds its own
  images in its own workspace and flashed one onto that very stick the
  same day. A survey of one workspace was reported as a survey of all of
  them, and the question cannot now be settled in either direction.
- **BROWSER-3 was deleted, filed on a misread hex trace.** codex-vm's HID
  key log prints the scancode in hex, so an injected 80 read back as
  `sc=50` and an innocent path was blamed.
- **A `-screenshot` finding was retracted** as a decoder bug.
- **WORKS-16, an editor crash, was corrected**: it stopped reproducing,
  and the two defects fixed alongside it are a plausible cause and not a
  demonstrated one. New evidence in the same cycle argues against the fix
  explaining it -- three runs ended with codex-vm exiting with no `!EXC=`
  line at all, which is the shape of the HOST process dying, not a guest
  fault. **Left open deliberately.**
- **A stale claim that the runtime half of scope enforcement was never
  built** was corrected: it is built and sound on x86-64, and the gap is
  ARM64 parity. The earlier note saying there was no cross bed was also
  wrong -- the QEMU/Renode bed exists.

**One correction is a loss and is recorded as one.** `build/build.ps1`
wipes `build-output` in its clean phase. Three raw stick images were
preserved there and two gate runs destroyed all three, after their SHA-256
digests had gone to main telling readers where to ask for them. The
findings survive only because they were written down as readings rather
than stored as files. A standing rule now sits where the next person will
meet it.

---

## Numbers

Measured 2026-08-12 at main 14745, seed `527C2C75`. **The battery is red and
this release does not ship until that is resolved** -- see below.

| | |
|---|---|
| Battery | 1446 total / **1399 pass / 0 fail** / 47 skip |
| Oracles | scalar 2013/2013, vector 130/130, CCE 1485/1516 (31 documented, 0 unexplained) |
| App sweep | 269 units / 263 clean / 6 known-dirty / **0 regressions** |
| Fixed point | `Sut.cdx` byte-identical to the shipped seed, hard fixed point in one pass |
| Poison BVT | 135 pass / 0 fail |
| DDC | both arms 2,759,449 bytes; **95 differing bytes, all inside the signature region, 0 outside** |
| Seed | 2,759,449 bytes, SHA-256 `527C2C75..0E931A99` |
| `seed/Codex.img` | 16,777,216 bytes, SHA-256 `5672AC1F..64823610` |
| `seed/Codex.map` | 5,127 symbols (was 5,101, two seed generations stale) |
| Compiler | 64 files, 54,148 lines (measured 2026-08-12) |
| Library | 591 modules across 22 quires (431 foreword, 160 OS) |
| Plugs | 55, 148 source modules |
| Apps | 66 applications, 1,008 modules |
| Doc counts | 59 claims checked, 0 drifted (2026-08-12) |

---

## Still open at the end of this cycle

Named rather than smoothed over, because the next cycle inherits them:

- **`engine-shadow` is SKIPPED by ruling, and that is why the battery
  reads 0 fail.** It is the one number in this report that would mislead
  if taken at face value. Expected "shadow is exact ambient: yes", sample
  1512464, 1942 shadow pixels; measured "no", 4734767, 738. It is val's,
  from the shadow work at 14721/14732. **The new output is what val's own
  CL predicts** -- a 3x3 filtered compare makes a shadowed fragment a blend
  rather than exact ambient -- but a prediction in a CL is not the author's
  verdict, and re-minting an oracle to match the code is how a real
  regression gets blessed, so it was left for val rather than adjudicated
  by someone else. Damian ruled it non-critical and overrode it to a
  `.skip` for this release.

  The skip states its own retirement condition, because a skip reason is
  the one claim in this tree no runner revisits: val confirms the three
  measured numbers are the intended output, re-mints the `.expected`, and
  deletes the sidecar. `build/audit-skips.ps1` will report it as REAL
  until then, which is correct -- the test still differs.

  Two facts a later session should not have to rediscover. **It is not the
  compiler:** recompiled and rerun against seed rev 614, the generation
  before 14745, it fails identically with the same deltas. **And it is not
  in the BVT**, which is why `build.ps1` was green for every CL that
  changed the behaviour underneath it. The same was true of `ui-font-test`
  and `desk-parse`, which blu re-minted at 14761; `ui-font-test` also
  stopped mislabelling itself and now names the letters it actually reads
  (CCE 13=e, 17=i, 39=E).

- **A5 has no owner.** Both sticks are rebuilt and bed-verified.
- **GPU stage 5**, whether the GPU path becomes the default in 3D View, is
  a judgement about which look is right.
- **The I219-V link bring-up wedges on the real part** and the cause is
  unspecified in the datasheet.
- **The self-reproducing quine** -- the one attack the DDC witness cannot
  yet catch -- is still unbuilt. Update 39 measured the boundary precisely
  enough to name it; nothing has built it.
- **192 bare `print-line` call sites** still emit raw CCE where human
  output is wanted. Each needs judging rather than sweeping, because raw
  is correct for the wire emitters.
- **The HTML plug's widget event bridge is still undeclared**
  (html-backlog 1.2). The IR pipeline fix means the calls now reach the
  plug; what they reach is still a stub emitting the constant 0.
- **A pane's allocations are not reclaimed when the pane exits**, across
  five desk panes.
