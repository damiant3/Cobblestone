# GitHub Update 38

**Scope: main CLs 12647 to 13135, 2026-08-03 to 2026-08-05.** Update 37
covered 10469 to 12646. That is 153 changelists in three days, counted
with `p4 changes` over the range.

---

## The headline: the desktop works on real hardware

On 2026-08-05, on the ASUS TUF board, booted from a USB stick with no
firmware assistance beyond UEFI: the Codex desktop came up, the keyboard
typed and kept typing, the mouse moved the cursor and clicked panes open,
the Shutdown button clicked and powered the machine off, and F12 wrote
the live screen to the stick as a BMP -- through our own xHCI driver, our
own USB stack, our own FAT16 writer, our own compositor, all compiled by
the self-hosted compiler whose seed verifies itself.

The camera that used to photograph the monitor is retired. The machine
now documents its own screen onto its own boot medium.

It took three flights and two defects, and both defects are the same
story: **a hypothesis the bed could not falsify survived until real
silicon falsified it.**

### Defect one: first-match binding

The board carries four keyboard-shaped USB interfaces (a Logitech
Unifying receiver contributes three; the wired keyboard typed on binds
fourth). `usb-attach` bound the first boot-keyboard interface and
stopped, so sixteen probe versions interrogated a dongle nobody typed on,
ever more deeply, for weeks. The fix binds EVERY boot-keyboard interface
as peers pumping one mailbox, plus each device's other HID interrupt
interfaces as raw counting listeners. Flight 1 (2026-08-04) went from
color bars straight to a typing desktop on the first boot.

The bed gap behind it: injected keys reached every guest through the PS/2
emulation regardless of what the USB stack did, so no bed run had ever
proven a scancode crossed the interrupt-IN DMA path. codex-vm gained
`-hid-keys` (keys travel USB only) and `-hid-root-silent` (the first
keyboard answers SUCCESS with eight zero bytes forever -- the exact metal
state), and the arm that reproduces the board's silence delivers its keys
through the second keyboard or fails.

### Defect two: the completion steal

Flight 2 flew the mouse walk and a boot-time HID table. The table named
the bus (VID:PID per interface, photographed); the keyboard typed and
then DIED mid-session; the mouse never moved. One defect explains both:
`xhci-wait-xfer` matched transfer events by SLOT alone, and the Unifying
receiver carries three armed interrupt endpoints on ONE slot. A pump
polling the shared slot consumes a sibling endpoint's completion; the
victim is left armed with no TRB queued, and if the thief generates no
traffic of its own, the slot goes silent forever. The mouse starved from
frame one; the wired keyboard lost a 500 ms idle heartbeat to its own
raw sibling minutes in. Flight 1 survived only because the pre-fix walk
never armed the sibling interfaces: nobody could steal.

The fix keys the completion latch by (slot, DCI) and every waiter --
control, storage, keyboard, mouse, camera -- passes its endpoint.

The bed gap behind THIS one is the instructive part: codex-vm completed
every interrupt IN TRB instantly at doorbell time, so completions were
so plentiful that a defect fed by their SCARCITY could not be expressed;
a steal was healed within two loop iterations, and the combo-device arm
passed green against the broken code. codex-vm gained
`-hid-nak-unchanged`: interrupt endpoints NAK until their report would
differ from the last one delivered, as silicon does, with pending TDs
re-rung when input state changes. Under that model the new
`usb-hid-steal` arm answers `pos=0,0` (one report, then starvation --
the metal shape exactly) against the old code and `pos=80,40 btn=1`
against the fix, deterministically, both directions.

### Screenshots as telemetry

F12 captures the framebuffer as a 24-bit BMP and writes it to the boot
stick's own ESP, named by the RTC (`SHhhmmss.BMP`), from the desktop or
any pane. Under it: a multi-cluster FAT16 writer (cluster chain built in
the loaded FAT, both FAT copies flushed in bulk, data in 64-sector runs)
proven by a 3 MB write read back through the independent bulk reader with
zero bad bytes, and by extracting a pixel-exact pane capture out of a
bed disk image by walking its FAT chain. On the board: shot, taskbar
verdict, file visible in the Files app, frames readable on any machine
that mounts the stick.

---

## The independent rechecker found what the compiler erases

Track C2's rechecker -- a plug that re-derives well-formedness from the
emitted IR text alone, with a published kill-rate -- moved from novelty
to instrument this cycle, and its findings were the wire's, not its own:

- **Effect rows and linear facts never reached the IR.** `linear T` and
  `T` produced byte-identical IR text; an arrow's effect row was bound
  and never emitted, so a function lost its effects the moment it took a
  parameter. Both now ride the wire as optional trailing elements, and
  the lesson index gained L-ERASED: a rule the compiler enforces can be
  absent from the artifact it emits.
- **A chapter's grounds table existed only inside the compiler**, so any
  tool reading IR text saw pure functions performing Device.Port. It is
  published in the header now, and the rechecker folds it in.
- **A constructor pattern over a parametric sum bound its variable at
  ErrorTy** while the same variable's USE carried the real type -- 419
  findings from one lowering gap, fixed with a positional parameter map.
- **ProofTy, PropEqTy, TypeCon and TypeApply shared ErrorTy's atom on
  the wire**, so a proof type was indistinguishable from a type failure.
  Four new atoms, and the parser's unknown-atom fallback keeps unrebuilt
  plugs reading what they read before.

Each fix cycle ends the same way, and the words are in the changelists:
seed rebuilt, hard fixed point in one pass, THE SEED VERIFIES ITSELF.

## The compiler's memory story, measured to its end

The C1 campaign chased a whole-compiler IR-text emit that exhausted a
3 GB guest heap, through four refuted hypotheses (deck floors, the
emitter, the IR passes, the DCE flood) to a measured cause: **IR text
emits every node's type by structure**, ~580 bytes per constructor per
element, so one 259-entry builtins table costs more than the 2.6 GB that
remained. The fix direction (emit types by reference) is priced and
raised as a wire-format decision, not taken quietly. On the way:

- A **guard page** below the boot stack: any heap frontier that reaches
  the stack now faults on first touch instead of silently overwriting
  it. Verified by an arm that fails against the pre-page seed.
- **Deck floors scale with the assembled unit**: a typical app's peak
  frontier fell from 1047 MB to 437 MB with zero configuration, and the
  floor-vs-guard-band distinction that made naive scaling refuse
  near-empty decks is documented in the design.
- A retraction, published as such: the guard page alone fixed C1's
  crash-shape; the earlier claim that it did not was measured against a
  binary whose boot code the change could never reach.

## The build system stopped drifting from itself

39 of 40 script generators had drifted from the scripts they emit --
about 6,300 lines, including generators that no longer passed
`-ApprovedBy` or ran at the standard job count. The Shell DSL grew from
raw-string emission to an intent-level AST (pipelines, records, process
control, colour, constrained parameters), a third of the generators are
converted with byte-verified output, and `check-generated-scripts.ps1`
fails any generator whose emitted script drifts or carries an unhandled
node. Separately, the column-2 prose campaign moved ~2,900 prose lines
out of the Os, Build, Boards and compiler quires into audited JSON
annotation sidecars -- prose the tree's own rules class as untestable
assertions -- with every block audited before transform.

## The network bed learned to say no

The e1000e driver was audited register-by-register against the 82583V
datasheet (three false prose blocks found; zero code defects in RCTL,
TCTL or the descriptor layouts). codex-vm's device models gained the
refusals real hardware makes: MDIO windows with settle time, PHY paging
and slow mode, a configuration value the storage device actually
enforces, ports above eight that no bed could previously seat, a
power-on UNIT ATTENTION on the BOT path, EP0 halting on a transaction
error as xHCI 4.8.3 requires. Descriptor rings are proven across a wrap.
The ASDE bring-up stage is bounded in wall time with a give-up state on
every arm, so a hung stage is no longer a possible outcome.

---

## Release proofs

The battery's first release run came back 4 red, and every one earned its
place in this report's story: a stale desk golden from the deliberate
pane campaign, the foreword outgrowing a pinned deck budget, and two
tests whose expectations predated the guard page -- including
`brotli-test`, whose big round trip turned out to have ALWAYS completed
by silently borrowing the boot stack's 64 MB reserve. The guard page
refused that for the first time, the test now brackets each case's heap
honestly (peak fell 3x), and the collision arm pins the designed double
fault by exception number instead of the old graceful line.

- Full battery (`-Tier all,traps -Jobs 8`): 1414 total, 1383 pass,
  **0 fail**, 31 skipped-with-cause.
- App sweep (`sweep-app-classes.ps1 -Check -Jobs 8`): 261 clean, 6
  known-dirty per baseline, **0 regressions**.
- Poison build (0xCD alloc fill, battery against the poison seed):
  1414 total, 1383 pass, **0 fail** -- tally byte-identical to the clean
  run, so the zero-fill is a safety net and not a crutch.
- Seed `52E0A3A00218E19F` (2,722,559 bytes), hard fixed point in one
  pass, self-verifying.
