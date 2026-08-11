# CurrentPlan -- the shape and the priority order

*This file is the fleet's open work and its priority order. It carries no
history: shipped work is deleted, not memorialized (Perforce and the
GitHubUpdate reports are the record). Consolidated 2026-08-08 by reek at
Damian's direction: the five per-agent workplans and the findings-outbox
channel were retired, their open items folded in here, and their durable
facts moved into the reference docs that own them. A closed item is
DELETED, not annotated.*

**Where an item ORIGINATES in one app or quire, it lives in that
register** (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) and is named here only if it blocks a
track. There is still no platform-wide register beyond this file; do not
recreate `docs/PM/BACKLOG.md`.

## Where we stand, in three sentences

The compiler is a hard fixed point of itself on bare metal and Update 38
is on the public mirrors: the desktop boots from USB on the ASUS with
keyboard, mouse, click-driven panes, shutdown and screenshot-to-stick,
all through our own stack. Diverse double-compiling now closes on the
whole compiler: an independently built Roslyn arm reproduces the shipped
seed byte for byte except the 96-byte signature region, and an
independently written rechecker agrees with the compiler on every
definition of the compiler and the standard library. The A6 F12
regression is root-caused and reverted on metal (main 14141: the guard
meant to protect the FAT write path was itself refusing correct writes),
which leaves no red item on the board. The class that guard covered is
guarded again at main 14169 (A6 below), and that guard FLEW 2026-08-09 and
passed: a shot landed correct on the ASUS and the returned volume was
clean on every question the sitting asks. WORKS-8 is closed. What the
flight exposed is one layer down, in the USB mass-storage driver, and is
open as WORKS-9.

## Track A -- the stick is an OS

- **A6 F12 regression (fester). ROOT CAUSE FOUND AND REVERTED on metal,
  main 14141.** Read `docs/PM/Active/Stories/TheShotThatWorkedOnce.md`
  for how it was hunted (L-GREEN, L-ROUTE). Two contributors, both now
  addressed:
  - **The 13613 chain-free guard WAS the regression: it refuses correct
    writes.** Measured on the ASUS -- the pre-guard build lands two
    consecutive desk shots with clean non-overlapping chains, while head
    refused the second at `s7 m3 c1 p3` (mount ok, last transfer
    success) and no collision existed on the returned stick. Reverted at
    main 14141.
  - **A pane visit leaked 4,617,256 bytes permanently** (`gsc-run`
    allocates render target and depth buffer outside any extent);
    bracketed in `desk-scene` at main 14131/14133. Real, and not what
    produced the one-shot-per-boot: exhausting the arena takes many
    visits and the flights failed after one.
  Separately, `GopScene` now polls the keyboard until the pump yields
  rather than once per frame (main 14142): `kbd-pump-one` advances a
  three-phase state machine one step per call, and the 3D pane's frame
  costs about a second on metal, so Esc would not close the pane.
  **The class 13613 guarded is guarded again as of main 14169** (blu),
  by an in-memory check against the chains the ROOT directory claims
  rather than a re-read of the FAT off the disk. It cannot repeat the
  13613 failure by construction: what it cannot walk it leaves unmarked,
  and unmarked never refuses. It descends into subdirectories, so it
  covers `EFI/BOOT/BOOTX64.EFI`, and it carries the arm 13613 lacked --
  a correct multi-cluster write that it must ADMIT -- alongside the one
  that must refuse. **FLOWN 2026-08-09 AND IT PASSED** (blu): shot 1
  landed correct on the ASUS at 1024x768x24 and the returned volume is
  clean on all four questions -- chains match sizes, no overlaps, no
  clusters allocated to nothing, both FAT copies identical. Shot 2 failed
  BELOW FAT, `s7 m3 c256 p2 w14`: a 32 KB data-phase transfer with no
  completion event inside `xhci-fuel`, which is reek's `GopUsbMsc` /
  `GopXhci` and is routed to him with a discriminator rather than a cause.
  **WORKS-8 is CLOSED and deleted; that second-write gap is open as
  WORKS-9 against the driver, not against the FAT writer.**
  Detail in `apps/works/works-backlog.md` and `docs/HardwareSitting.md`;
  the stick is read with the new `build/dump-usb.ps1` and
  `build/fat16-walk.ps1`. `desk-files` -> `gfl-run` has
  the same unbracketed shape as the leak, much smaller, untouched.
  **Damian's standing ruling still holds for anything further here: do not
  propose flights or sittings for this row.** He directed the 2026-08-09
  sitting himself, which is what lifted it once; it is not lifted
  generally, and the follow-up now belongs to reek's driver anyway.
  Whether the stick comes off `ceremonyboot.img` (`C423418D`) is fester's
  call.
- **A5 (reek, prepped 2026-08-08, NOT FLOWN): the compiler runs on the
  box.** The mechanism exists and is now proven in the bed. The compiler's
  `DISK` mode reads two stdin lines (the word `DISK`, then a path), mounts
  the boot volume through the GPT, reads the source AND its cites off the
  volume, and emits.

  **Baseline: booted from an 8 MB image with the seed as the PE payload and
  the program as `SOURCE.SRC`, the compiler read the source off the volume
  and emitted an 84,759-byte CDX BYTE-IDENTICAL to the host compile of the
  same source with the same kernel.** Rebuild with `build/cdx-to-pe.ps1`
  then `build/build-img.ps1 -Source`, and drive it with
  `codex-vm -kernel seed\Codex.cdx -disk <img> -input` carrying
  `DISK\nSOURCE.SRC\n`.

  **A5 now runs end to end in the bed, 2026-08-09.** UEFI boot with NO
  serial input of any kind: `stage1` as the PE payload via
  `cdx-to-pe -EntryStart -HeapPages 32768 -Stdin "DISK\nSOURCE.SRC\n"`,
  `build-img -Source`, then
  `codex-vm -kernel <img> -uefi -disk <img> -headless`. The stub's prefill
  survived `emit-start` (`all_consumed=1`), the compiler read `SOURCE.SRC`
  off the volume and wrote **`OUT.CDX`, 84,462 bytes, byte-identical to the
  host compile of the same source with the same kernel**, with `OUT.TXT`
  reading `OK OUT.CDX 84462`. That run was on **raw IDE**, because the payload
  was not compiled `-Uefi`, and IDE is not a transport the ASUS has. That is
  why the flight below wrote nothing.

  **THE COMPILER COMPILES ITSELF ON THE UEFI BLOCK PATH, 2026-08-09 (reek).**
  Payload built with `compile.ps1 -Uefi`, so `block-read-sector` and
  `block-write-sector` are both firmware helpers, at `-HeapPages 32768` (128 MB,
  the flying envelope). The guest read a 2,766,116-byte `SOURCE.SRC` off the
  volume, compiled it, and wrote **`OUT.CDX` at 2,753,312 bytes, byte-identical
  to the host compile of the same source with the same kernel**, in 5.0 minutes.
  A small source comes out byte-identical too (84,660). Serial produced ZERO
  bytes: diagnostics went to ConOut, so nothing on this path needs a UART.

  **The control is what makes that a measurement rather than a hope.** The same
  image against codex-vm before 14398, whose UEFI `WriteBlocks` was a no-op,
  changes 0 bytes and has no `OUT.CDX`. A payload on IDE would have written
  there too, so the write provably went through UEFI Block I/O.

  It also answers the read-path arena question by completing: 2.7 MB read and
  compiled inside 128 MB. **What is left is metal itself.**

  **Four things stood between that and a flight, none of them guesswork:**
  1. **CLOSED at CL 14210.** `cdx-to-pe -EntryStart` runs the bare-metal
     runtime init, so the payload's `block-read-sector` works under `-uefi`
     and the CDX it emits is byte-identical to the host compile.
  2. **CLOSED in the bed at CL 14235.** `cdx-to-pe -Stdin` prefills the
     serial ring and `emit-start` zeroed it straight back (CL 14213).
     `emit-start` now skips that zeroing when it sees `serial-primed-magic`
     in cell 30696, clearing the cell as it goes so a warm restart still
     zeroes. Only `serial-write-pos` is preserved; `serial-read-pos` is
     still zeroed, which is correct because the stub sets it to 0 anyway.
     Being codegen, it reaches a payload only through a compiler that
     carries it, so the arm below was built from `stage1`, not `Sut`.
  3. **CLOSED in the bed at CL 14245, not yet on metal.** `emit-cdx` sank to
     Console, which is serial, and the ASUS has no UART. It now writes the
     CDX to the volume through the foreword FAT writer
     (`docs/Designs/Active/Compiler/MetalOutputSink.md`). Measured
     2026-08-09 on codex-vm: a guest compile under `DISK` wrote `OUT.CDX` at
     84,462 bytes, extracted back out of the FAT by an independent host
     reader and **byte-identical to the host compile**, with `OUT.TXT`
     reading `OK OUT.CDX 84462`; serial for that run fell from 84,616 bytes
     to 122. The item also carried two allocation fixes in `Fat16.codex`:
     the writer cost **55.8 bytes of arena per byte written** and now costs
     **0.30**, which is what takes a 2.7 MB CDX off a ~144 MB projection
     against a 128 MB arena.

     **The 2.7 MB run is done, 2026-08-09.** A 2,745,998-byte payload (the
     current seed CDX size) written by one `fat16-write-segments` call and
     verified by streaming the FAT chain: chain 5364, recorded size exact,
     `bad=0`, and the shifted-pattern control answers `bad=2745998` so the
     oracle fires. Run twice, and the second is the one that matters: as a
     UEFI PE payload at `-HeapPages 32768`, which is the flying image's whole
     128 MB arena (L-ARENA), with the 2.7 MB buffer resident inside it. Net
     arena for the write was 50,608 bytes. The ~144 MB projection is dead by
     measurement. Recipe in the design.

     **FLOWN 2026-08-09. It wrote nothing, and the cause is found and
     bed-reproducible.** The returned stick differs from the flashed image
     in exactly two sectors, LBA 0 and 1, which are the two
     `flash-usb.ps1 -SpecFit` writes at flash time; the other 16 MB is
     identical. The payload is the depot seed, compiled in plain `Exit`
     mode, so its `block-read-sector` is **raw IDE port access**
     (`X86_64Helpers.codex:1613-1617` dispatches on exit mode). `codex-vm
     -disk` presents an IDE device and the ASUS boots USB mass storage,
     so every bed run for this item passed on a transport the target does
     not have. Dropping `-disk` reproduces it: `EXC=00`, divide by zero in
     the BPB parse, nothing written.

     **CLOSED at main 14398: the ExitUefi block path works end to end, read
     and write.** `block-write-sector` has the exit-mode dispatch
     `block-read-sector` already had, and `emit-uefi-block-write-sector-helper`
     calls `WriteBlocks` at `+0x20` then `FlushBlocks` at `+0x28`. The flush is
     not optional, because the operator pulls the stick.

     The upstream blocker was that `uefi-systab-addr` was cell 36208 = 0x8D70,
     which is entry 430 of the PML4 that `emit-build-process-page-tables` puts
     at 0x8000 and zeroes wholesale. `cdx-to-pe.ps1` primes that cell before
     jumping, and the page-table build ate exactly that write, so every
     `uefi-*` helper dereferenced null. The cell is now 30704, below the
     tables, and `emit-start` copies `0x8000` in only when it is still zero, so
     a pointer an earlier stage primed survives. Measured under `-EntryStart`:
     the cell read 0 with 0x8000 holding a PML4 entry, and now reads 983040 =
     0xF0000, the real SystemTable.

     **The write stayed invisible until codex-vm 14384 because
     `UEFI_TRAP_BLK_WRITEBLOCKS` shared a case label with `BLK_RESET` and fell
     through to a bare break**, returning EFI_SUCCESS and writing nothing, so
     no bed could express a guest write at all. Same payload and same image,
     only the emulator moving: 0 bytes changed and `PROBE.TXT` NOT FOUND,
     against 37 bytes changed and `PROBE.TXT` at cluster 222 with exact
     content, agreed by the guest's own readback and by a host-side FAT walker
     that shares no code with the writer. ConOut echoes to stderr now too, so a
     `-headless` UEFI payload is no longer mute (it landed only in the VGA text
     buffer, where scrolling discards a line for good).

     **CLOSED by measurement 2026-08-09: a 2,766,116-byte source reads and
     compiles inside a 128 MB arena** (`-HeapPages 32768`), so the projection
     that 2.7 MB needed 180 to 230 MB is dead. `fat16-bytes-to-text` stopped
     rebuilding its accumulator per byte at main 14371 (blu); it is now one
     `text-concat-list` over a char list. Reading into a buffer instead of two
     intermediate collections is still the tidier shape, but nothing is blocked
     on it.

     **THE UEFI BLOCK WRITE PATH IS PROVEN ON THE ASUS, 2026-08-10 (main
     14514).** `blockladder.img` flew white and the write was confirmed on the
     medium, not from the guest's readback: LBA 30000 came back holding the
     boot-sector copy with byte 0 replaced by `0xA5` and `55AA` intact, where
     both earlier A5 flights left it zeroed. `LocateProtocol`, `ReadBlocks` and
     `WriteBlocks` all work under firmware after the kernel installs its own
     CR3.

     **Still open: the sink's own 2.7 MB write has never run on metal.** The
     ladder proves one `block-write-sector`, not `fat16-write-segments`. The two
     A5 sticks stay grounded until their flight arm is rebuilt with the ladder's
     reporting order (paint before print, no report waiting on a firmware call)
     and its failures forced in the bed -- the two silent flights are now best
     explained by their own first ConOut call, which is an inference from the
     ladder's flights and not a measurement of those payloads.

  **Source must be LF.** The stdin path applies `utf8-to-cce`; the DISK path
  does not, and CR has no CCE code point, so a CRLF file fails at the first
  line ending (measured: CDX1000 at 11:43 on a 41-character line 11).
- **Native GOP resolution and diag word wrap (red, scoped by Damian
  2026-08-07).** The 1024 is the ASUS firmware's GraphicsConsole mode,
  activated by the stub's own ClearScreen; nothing ever calls SetMode.
  Fix: after ClearScreen and before reading Mode->Info,
  QueryMode-enumerate and SetMode the largest mode. The stub is
  hand-assembled bytes with rel8 branch ceilings, so the loop is its own
  body, and ON ANY FAILURE (QueryMode error, SetMode error, MaxMode 1) it
  falls through to today's behavior. L-OPTIONAL applies: the bed's GOP is
  more capable than AMI, so the fallback is the safety, not the bed. The
  measured clipping surface is the scale-2 probe rows
  (`build/boot/diag/*`); DeskBoot's trace rows are scale 1 and fit. The
  wrap fix is a `gop-draw-text-wrap` primitive in GopDraw (word-boundary
  break, returns LINES USED so the caller owns its vertical space, no
  allocation), landed WITH its first caller, not bare. Convert live
  surfaces; do not sweep the retired probes.
- **A6 residue (red). Damian's grading after flight 2: "not critical
  right now."** The shadowed scene on metal is choppy to the point of
  unwatchable (CPU-only software render, seconds per frame); shadow
  quality is primitive. Candidates when re-graded: smaller or filtered
  shadow map, per-frame cost profiling on metal, scene simplification, or
  the parked GPU path. **Do not pick this up ahead of the items above
  without a ruling.**
- A2, A3, A4 and A7 are CLOSED on metal.

## Track B -- the network (blu). Metal-gated: advances at sittings, not before.

- **B2 Intel I219-V link bring-up.** Register audit is COMPLETE; do not
  re-run it, and `e1000-phy-addr = 1` is correct, do not "fix" it.
  **Findings 1-4 are now closed in the bed** (4 on 2026-08-10). ASDE could
  not be exercised before because the model had no ASDE bit and no speed
  fields: `na-line` printed SPEED and ASDV off a register nothing wrote, so
  both read 10 Mb/s on every arm ever run. `-e1000-asde` fills them,
  `e1000-link-up` clears the bit per 82583V 12349, and
  `codex/test/e1000-asde-speed` carries the arm with a control and a
  sabotage. **This says nothing about why metal wedges** -- the datasheet
  does not say what a part does when the bit is set, so the model invents
  no failure.
  **The ASDE arm WEDGES the machine deterministically on the real part**
  (hang after `entering bring-up`), so anything riding `na-bring-up` on
  metal hangs the boot. `AsdeStageProbe` now runs **ASDE=0 before ASDE=1**,
  which is what makes the next flight decisive: a wedge before any arm row
  indicts the reset, ASDE=0 painting and ASDE=1 wedging indicts the bit the
  datasheet says to clear, and both arms painting exonerates ASDE here.
  Rows paint whether or not a volume mounts (the 2026-08-10 flight returned
  nothing because the mount was a precondition), and a failed mount now
  names which of the seven stages failed instead of printing "no ESP".
- Then **B2c** RX/TX on the real part, **B3** the TCP/IP stack over it,
  **B4** serve the repository protocol. EdgeMesh Phase 2
  (`docs/Designs/Active/Features/EdgeMeshGameServers.md`) is the consumer
  waiting on B2-B4; nothing to do there until the track lands.
- **The ARM64 send path silently drops everything past 11,200 bytes, and
  it is the 14317 defect in a second copy** (read 2026-08-09, blu).
  `arm64-net-io-send-chunk` (`Arm64NetIO.codex:102`) calls `net-send` and
  advances by `arm64-net-mss` without reading the refusal and with no
  `net-rexmit-full` check at all, so past `net-rexmit-capacity * net-mss`
  the bytes are gone with a clean return. The x86 path was fixed at main
  14317 and this copy was not. Its loops do not tick either, so the poll
  clock landed 2026-08-09 does not reach it. **Not fixed because there is
  no ARM64 bed on this box**; it wants the Oracle Cloud lane or a cross
  boot, not a blind edit.
- **A lost SYN is never retransmitted.** `net-io-wait-established` runs to
  a 10000-poll cap, far below `net-io-tick-interval`, so no tick can fire
  inside it: `net-connect` arms the timer and `transport-connect` sends the
  SYN once, and a connect that gets no SYN-ACK simply fails. A hang it is
  not; a capability gap it is.
- **The e1000 BAR window hazard is unowned, not urgent, and measured
  2026-08-07.** `e1000-window-lo`/`-hi` do not track
  `bare-metal-ram-size`; they track `bare-metal-pd-count`, which is
  `ceil(ram-size / 1 GB)`, because the device PD is the single gigabyte
  PD at that index. At 3 GB the two readings coincide, so a `ram-size`
  derivation looks right and is not. Deriving or guarding it drags the
  whole x86 emitter across the quire boundary into a NIC test kernel, and
  house practice spells every device base literally, so **making a shared
  memory-map chapter is a design decision, not a cleanup.** Left as
  measured.

## Track C -- the trust audit

- **C1 (fester): diverse double-compiling. The main claim is LANDED.**
  - **State the residual hole every time C1 is reported, because it is
    large. This is NOT a complete Wheeler DDC.** The seed still sits
    upstream twice: it produced the IR and it compiled `csharp-plug.cdx`.
    A real DDC needs the C# arm to do `source -> IR` itself. What is
    narrowed is the frontend and the text emitter, on one input, with
    Roslyn as the independent lineage.
- **C2 (val): the independent rechecker. R1 to R4 are all published and
  enforced, and the abstention set is 1 finding from 1365.** All three
  classes are now closed:
  - **THE LANE FOUND ITS FIRST REAL COMPILER DEFECT, and it is fixed
    (2026-08-10, seed-affecting).** `lower-lambda` recorded the EXPECTED type
    it was handed as the lambda's type -- at a polymorphic call, the callee's
    declared parameter with its type variables still in it -- at the moment
    the body had been lowered and its concrete type was in hand. `lower-let`
    twenty lines above records the RESOLVED type. Not a soundness hole: every
    program compiled correctly. But `subst-type-vars-from-arg` at every call
    site learns the callee's variables by matching its declared parameter
    against the argument's type, so a lambda argument matched itself, learned
    nothing, and **the compiler resolved the instantiation and then discarded
    it** -- the application's result type reached the wire uninstantiated for
    every consumer, this rechecker and every transpiler plug alike. Fixed by
    recording the lambda's actual type; no `ir-emit-type` change was needed.
    Whole compiler after: **AGREE 4821, DISAGREE 0, UNSUPPORTED 0, IMPROVED
    4** -- 88 of the 92 now close in the ARTIFACT and the rechecker derives
    the remaining 4, and they agree. **The remaining 4 are the same defect
    one level down** (an `if`- or `when`-bodied lambda, whose body type is
    itself the bare variable) and are open, not fixed. Account in
    `IndependentRechecker.md`; the control that was the evidence and was read
    as a fact is L-CONTROL.
  - **92 nested type variables: CLOSED 2026-08-10, 92 to 0.** Spine-scoped
    substitution, plug only, no compiler change and no seed. Stage 1 over
    the whole compiler is **AGREE 4820, DISAGREE 0, UNSUPPORTED 0**, against
    4776 / 0 / 44 before; kill-rate 28 of 28 with a passing control; 254 s
    against a 255 s baseline. The unsound fix -- bind the argument's
    variable from the parameter it flows into -- was NOT taken, and
    `bounded-arg-into-plain-slot`, the arm that caught that shape in the
    rejected stage-2 fix, still scores.
    - **The honest half: IMPROVED 92, SINGLE-WITNESS 92.** Every comparison
      the substitution decided was decided by a variable fixed from exactly
      one witness, so it is tautological in that direction. Both counters
      now print on the stage line so a fallen abstention count cannot be
      read as more than it bought. What it does buy is a CONCRETE TYPE
      where there was a variable, and two mutations confirmed MISSED before
      and CAUGHT after say that comparison can now fail.
    - **The conflict path has never fired in this tree and nothing tests
      it.** A spine `apply-tvar-inconsistent` needs one variable with two
      disagreeing witnesses, and no site in the compiler has two: where a
      call carries enough information for a second witness the COMPILER
      ALREADY INSTANTIATES IT and no variable reaches the wire. That also
      retired a standing instruction in the design to write a
      two-instantiations mutation, which cannot be written.
    - Account in `IndependentRechecker.md`. The fork underneath is
      untouched and still Damian's: whether the compiler should also EMIT
      its instantiation. Deriving was taken first because it is plug only,
      reversible, and is the arm that would check an emitted one.
  - **3 overflow-mode findings: CLOSED, 3 to 0** (main 14456). This entry
    said it was a ruling of the same shape as variance and that the lane
    should not take it alone. **That was wrong, and the correction is the
    useful part: the measurement decided it and taking the measurement was
    the work.** Mode is accepted both directions at a parameter and inside
    a type argument, while a BAND difference in the identical position is
    refused with CDX2001. Published as `docs/DevelopersGuide.md` "Overflow
    mode is not part of type identity". The invariance argument does not
    carry over because a band is a claim about which VALUES a slot holds
    and a mode is a claim about what happens at an OPERATION, governed by
    the declared type at the site performing it; `list-set-at` through a
    wide view falsifies a narrow view's claim, while a `wrapping` and an
    `error` view over one list cannot falsify each other. The premise is
    stated in the guide so it can be attacked: every mode preserves the
    band. It also caught a LATENT FALSE DISAGREEMENT, `rc-ty-eq` answering
    TyNeq on a mode difference where the compiler agrees, unfired only
    because no such site exists today.
  - **1 underived range**, one site in `compile-type-check` inside a very
    large diagnostic expression. Left as an honest abstention, and it is now
    the ONLY finding the rechecker raises against the whole compiler.
  - **The type-variable rules are PUBLISHED and enforced, and the campaign
    is closed** (stages 1, 2, 3 and 3b, 2026-08-08 and 2026-08-09; the
    account is archived at
    `docs/Designs/Done/Language/TypeVariableRules.md`).
    `docs/DevelopersGuide.md` gained `## Type Variables`, `## Integer
    literals` and `## Variance of Type Arguments`, each written from
    measured arms with a control rather than transcribed from
    `TypeChecker.codex`. Variance was the one real decision and Damian
    ruled it INVARIANT on 2026-08-08; the rechecker enforces it and, since
    stage 3b, so does the compiler, at a measured cost of fifteen source
    sites in the whole tree. That closed the 1062 type-variable findings,
    78 per cent of the abstention set. Whole compiler, 4815 definitions:
    **DISAGREE 0 at all three stages** with the abstention counts unmoved,
    kill-rate **21 of 21** with a passing control.
  - **The motivating case is now REFUSED, re-measured 2026-08-09.**
    `widen-view : List (Integer between 0 and 10) -> List Integer` compiled
    clean when the ruling was made, which is what the ruling rested on, and
    is now CDX2001 in both directions. A `List Integer -> List Integer`
    control is still clean, so it is not a blanket refusal of `List`
    signatures, and widening at an ordinary parameter is still clean, so
    invariance still governs argument positions only. The archived design
    keeps the earlier reading as the evidence that motivated the ruling.
  - **R4, the integer bounds. CLOSED 2026-08-09, 299 to 6.** Re-measured
    on the current source at 4820 definitions: the 299 was confirmed
    exactly (198 + 101) before any change, so the 2026-08-08 figure was
    right. **Its two halves were different problems**, which is the thing
    worth carrying to the next class: one was a checker that had not
    implemented rules the guide already published, the other was a rule the
    guide had never published at all. Assuming which kind you have is the
    mistake.
    - **`bounds-underived` 101 to 3** (main 14407, 14413), and it was NOT
      a specification gap. Two rows the guide's Static Bounds Prover table
      ALREADY publishes and the compiler proves had no arm in the
      rechecker: integer division, and a name bound at module level to a
      literal. The second was almost the whole class, because a named
      `cdx-*` code reaching one of four diagnostic constructors cannot be
      read off its declared type, which is a plain `Integer`. Division on
      its own moved NOTHING on the compiler, measured: no bounded slot
      here is fed by one. Corpus arms `bounds-widened-dividend` and
      `bounds-widened-constant`, each confirmed MISSED before and CAUGHT
      after; kill-rate 21/22 to 23/23 with the control passing, and
      DISAGREE stayed 0 at all three stages, so nothing became a false
      disagreement. The three survivors are unrelated forms in
      `compile-type-check`, `pitch` and `skip-newlines-pos`.
    - **`apply-arg-int-bounds` 198 to 0** (main 14419, 14422), and this
      half WAS a rule question. `docs/DevelopersGuide.md` gained "What a
      bounded parameter admits": an integer argument is admitted iff its
      PROVEN RANGE fits the parameter's declared range. Measured, with the
      discriminating pair being the same field declared 0..1000, refused
      at a 0..255 parameter directly and admitted through `int-mod ... 100`
      because the prover derives 0..99, so the declared type cannot be what
      decides. Overlap is not enough. The refusal is always CDX2051 from
      the bounds prover and never CDX2001 from the type checker, which is
      the sharp contrast with a type ARGUMENT under invariance. Stage 1 now
      defers the site to `RecheckBounds`, the same division
      `rc-wf-lit-into-int` already made for literals. `bounds-underived`
      did NOT rise, so the sites are decided rather than re-abstained, and
      DISAGREE stayed 0.
      **A REMOVAL cannot be validated by the kill-rate and this is the
      part to reuse.** An abstention is not a kill, so dropping one cannot
      lower the score, and no arm had ever expected that kind: the harness
      could not have noticed this change at all. The guard is therefore
      deliberate. `bounds-widened-field-arg` was confirmed CAUGHT AGAINST
      THE OLD PLUG BEFORE the change, which is what makes the bounds
      stage's coverage measured rather than assumed, and it is labelled in
      the file as not a sensitivity gain so it is not read as one later.
    - **Two rows of that table were FALSE and are deleted** (main 14386).
      `bit-and` and `bit-shru` were documented as proven and the compiler
      refuses both, measured in the parameter and field forms, either
      argument order, with controls. A missing row costs an independent
      implementation a proof the compiler makes; a false row is the worse
      direction, making it prove a range the compiler refuses.
  - **Read before the next change to the rechecker's comparison logic.**
    Stage 2 reported three disagreements and all three were the
    rechecker's own, in one line: `rc-expr-ty` typed an integer literal as
    `IntegerTy v v` where the compiler types it plain `Integer` (now
    published as `DevelopersGuide.md` "Integer literals"). **The first fix
    for them was wrong**, and it satisfied the sweep,
    DISAGREE 0, the abstention counts and 21 of 21 while silently
    blinding the checker to `fresh-row-id`, the one real compiler defect
    this lane has found. Only a corpus arm written for the direction
    being changed separated them (`bounded-arg-into-plain-slot`, sabotage
    confirmed: exactly one row moves). Lesson L-CAPABILITY-LOST.
  - **RE-DIAGNOSED 2026-08-09 and REASSIGNED to blu. It is not a
    truncation and it does not need a failure channel.** This entry used
    to say `net-io-send-chunk` truncates against a dead peer and that
    `TcpTransportState` needs a failure channel, at a 23-site cost. Both
    halves are wrong. `net-tick` is what ages a connection and declares a
    peer dead, and **its only production caller in the whole tree is
    `WebServer.codex:270`** (L-UNCALLED): every plug, `HttpFetch`,
    `TrustTransport` and `Arm64NetIO` never retransmit and never declare a
    peer dead. So the send drain waits for an ACK nothing can provoke, and
    the symptom is a HANG, not a truncation. Measured: the 50,000,000 cap
    is not reached in 180 s on the sibling loop, and a RST closes the
    connection while leaving the queue full so an answering peer does not
    help either. Once the clock runs, `net-tick` already sets `TcpClosed`
    and the caller reads `(ts.session).conn.state`.
    **CLOSED 2026-08-09 (blu): the loops tick on a poll count.**
    `net-io-tick-interval = 100000` against a named `net-io-max-polls`, in
    `net-io-send-drain`, `net-io-recv-wait` and `net-io-recv-raw`, each of
    which now also returns on `TcpClosed`. A poll count and not HPET
    because `NetIO` compiles into the plug lanes and the ARM64 path, where
    an x86 MMIO read cannot go. `codex/test/net-io-clock` COUNTS the
    give-up ladder (141 ticks unmeasured, 288 clamped) and then runs a real
    poll loop against a peer that does not exist; sabotaged by pushing the
    interval past the fuel cap, it fails. Two pieces are left and they are
    in Track B.
    Account and census in `docs/Designs/Active/IoT/ProtocolStack.md`,
    "The clock exists. One production caller drives it."
  - **The rechecker's second product still stands and is worth keeping in
    view.** The abstention set is a map of where the LANGUAGE is
    unspecified rather than a list of the checker's weaknesses, because
    `IndependentRechecker.md` section 7 makes it abstain wherever the guide
    is silent. That is what turned a sweep into three published guide
    sections. R4's 299 are the same instrument still pointing at something.
  - **C2.5 stage 4 (proof terms) stays deferred unless Damian calls for
    it.**

## The Shell DSL backport (blu) -- closed except for two decisions

Ten tractable generators are converted and the parse class is gated
(`check-generated-scripts.ps1` compiles every generator, dead target or
not, and hard-fails on PowerShell parse errors with no baseline).
**There is no next generator to pick up; continuing this lane needs
Damian's direction.** What remains:

- **The four stubs (`test`, `cdx-to-pe`, `build`, `build-img`) are
  BACKLOGGED**, Damian's ruling 2026-08-06: "backlog it, that isn't our
  mission." They emit roughly a third of their scripts and hold the bulk
  of the drift lines, so the drift total never approaches zero while they
  stand. That is expected, not a failure.
- **`plug-build` and `plug-run` are an UNADOPTED DESIGN, not dead code,
  and deleting them would throw it away.** Both are generic parameterized
  drivers, the latter carrying the whole TCP wire protocol. What ships
  instead is 60 hand-copied `build.ps1` and 60 `run.ps1`. Adopting the
  pair would delete roughly 220 KB of duplication. **A decision for
  Damian, not a cleanup.**

## The annotation campaign -- COMPLETE 2026-08-07

Every chapter in the tree carrying column-2 prose is audited and
recorded; zero remain. 19,972 prose lines survive and every one was kept
on purpose, against the 64,450 rule 12 measured on 2026-07-28.
**Coverage is `annotations/AUDITED.txt` membership and nothing else**,
because an all-KEEP chapter cuts nothing and writes no sidecar, so run
`-Apply` even when nothing will be cut. **Do not restart this as a
sweep**; a future pass starts from the ledger, not from a guess. The
method and its traps are in
`docs/Designs/Active/Compiler/Annotations.md` section E.

## Rulings Damian owes (the only queue that blocks)

1. Whether the Shell DSL lane continues, and whether `plug-build` /
   `plug-run` are adopted (roughly 220 KB of duplication either way).
2. An owner for A5.

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `tools/codex-vm.c` | FREE -- one owner at a time, announce before you start |
| `apps/works/GopDesk.codex`, `GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/plugs/csharp/**`, `build/` DDC harness | fester |
| `codex/plugs/recheck/**` | val |

## Standing rules that gate nothing but bind everyone

Battery runs are Damian's (release proofs excepted, per the release
skill). Goldens stay parked during active GUI work. No new platform-wide
register. Prose about our own code is deleted in files you touch. The
em-dash stays banned.
