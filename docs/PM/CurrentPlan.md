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
  Detail in `apps/works/works-backlog.md` and `docs/Hardware/HardwareSitting.md`;
  the stick is read with the new `build/dump-usb.ps1` and
  `build/fat16-walk.ps1`. `desk-files` -> `gfl-run` has
  the same unbracketed shape as the leak, much smaller, untouched.
  **Damian's standing ruling still holds for anything further here: do not
  propose flights or sittings for this row.** He directed the 2026-08-09
  sitting himself, which is what lifted it once; it is not lifted
  generally, and the follow-up now belongs to reek's driver anyway.
  The open question of whether the stick came off `ceremonyboot.img`
  (`C423418D`) is **settled 2026-08-11: reek reflashed disk 2 with
  `a5bigflight.img`, dumping all 16 MB first.** No objection to the
  reflash -- the row is reek's driver now and the ceremony campaign
  closed green on 2026-08-05. What it exposed is worth a line here:
  `ceremonyboot.img` was NEVER in the depot, in any stream, at any
  revision, while `docs/Hardware/HardwareSitting.md` said it "remains at
  `build/boot/ceremonyboot.img`". Corrected there. **The one thing not
  reproducible from source is the 124-byte `IDENTITY.DAT` the guest
  wrote to the ESP on real hardware, and the only copy was reek's
  p4-ignored `build-output/stick-before-20260811.img` (`629821CF...`) --
  one gate `clean` from deletion, which is exactly how blu's three
  preserved sticks were lost the same day.** Rescued to
  `D:\Projects\stick-archive\stick-before-20260811.img`, verified byte
  for byte, outside every workspace. Whether 16 MB of it earns a depot
  slot is Damian's call, not mine; every other flown image beside it is
  checked in.
- **A5 (reek): SHIPPED 2026-08-14, FLOWN GREEN.** The compiler booted the
  ASUS from bare UEFI, read its own 2.8 MB source off the stick, compiled
  itself in about a minute, and wrote `OUT.CDX` back **byte-identical to
  the host control** (2,790,018 bytes, `AB3A207EFB9279A6`), with `OUT.TXT`
  reading `OK OUT.CDX 2790018`. The account, the flight card, and the
  build discipline are the 2026-08-14 entry in
  `docs/Hardware/HardwareSitting.md`; the retro is
  `docs/PM/Active/Stories/TheBedThatAlwaysSaidYes.md` (lessons L-FREEDOM,
  L-REHEARSE). The returned stick is `a5flight-returned-20260814.img` in
  the archive. The history below stands as the campaign record.

  The compiler's `DISK` mode reads two stdin lines (the word `DISK`, then
  a path), mounts the boot volume through the GPT, reads the source AND
  its cites off the volume, and emits.

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

     **The sink's own 2.7 MB write still has not run on metal, but the arm for
     it now exists and is calibrated** (`sinkladder.img`, 2026-08-10). The block
     ladder proves one `block-write-sector`, not `fat16-write-segments`;
     `apps/works/SinkLadderProbe.codex` drives the real write and the streaming
     oracle and reports as a colour, paint before print. Bed: all six rungs
     green, `chain=5364 bad=0`, and every rung below WHITE forced by
     `build/sink-arm.ps1`, positive control included. **FLOWN 2026-08-11 and it
     is RED on metal: the screen held ORANGE, the last line was `SINKLADDER bpb
     continuing`, and the returned stick's root holds no `BIG.CDX` at all.** The
     BPB parsed and `fat16-write-segments` created no directory entry, so the
     fault is at or before the first allocation rather than in the chain walk.
     Neither the bed nor the glass separates hung from faulted here, and the
     next arm needs a heartbeat INSIDE `sl-fill` and between write segments,
     because the rung printing reports only the stage that already passed. Full
     account and the preserved stick image in `docs/Hardware/HardwareSitting.md`. **This
     was blu flashing and Damian flying; the finding is reek's to act on, and
     the A5 sticks stay grounded behind it.**

     **The compiler paints now, so the two A5 sticks are no longer blind
     (2026-08-11, reek).** `codex/compiler/Core/BootPaint.codex` gives the DISK
     path six rungs -- CYAN entered, YELLOW stdin said DISK, MAGENTA volume
     mounted, ORANGE source read, BLUE compile finished, WHITE `OUT.CDX` on the
     volume -- and every one is forced and watched by `build/disk-arm.ps1`,
     CYAN's failure included. WHITE re-reads the directory rather than trusting
     the writer, because with the medium read-only the writer answered True 833
     failed host writes in a row. **This did NOT need foreword-level GOP code**;
     the entry above said it did and that was wrong: `peek-qword` / `poke-32`
     are builtins and a chapter under `codex/compiler/` is answered by presence
     in the unit. Operator and arm tables in `docs/Hardware/HardwareSitting.md`.

     **Both sticks are rebuilt and bed-verified, so what is left is a flight.**
     `a5flight2.img` is now `A90E7DA0...` and returns `OUT.CDX` at 84,660 bytes
     hashing `ACF9823E...`; `a5bigflight.img` is `9E6E35AC...` and returns
     2,759,023 bytes hashing `9E823495...`, which is a fixed point of itself
     (compiling the same source with it reproduces it). Both were run on COPIES
     with the masters re-checked virgin, both painted through to WHITE, and the
     reader's negative arm fires on each virgin master. The two silent flights
     remain best explained by their own first ConOut call, which is an inference
     from the ladder's flights and not a measurement of those payloads -- and
     these sticks are what would settle it.

     **The rebuild found one defect and it would have cost the flight. It is
     now fixed in the compiler, and it was never one file.** `build/boot/a5src.codex`
     is stored `unicode+C`, the client's `LineEnd` is `local`, so every sync on
     Windows writes it CRLF: 246 bytes in the depot, 257 on disk.
     `build-img.ps1 -Source` copies what it is handed, so the image built
     straight from the workspace file died at `CDX1000` on line 5 and the
     ladder stopped at ORANGE. **Measured 2026-08-13: 2439 of 2508 `.codex`
     files in the tree carry CR on disk**, so every A5 image built from
     workspace sources had this defect and `-SourceDir`, which walks the tree,
     had it for every chapter it copied.

  **Source no longer has to be LF, and the old account of why was wrong.**
  `utf8-to-cce` was never what saved the stdin path: byte 13 is below 128, so
  it passes through untouched, and the function returns its argument unchanged
  when the file holds no high bytes at all. What drops CR is
  `__bare_metal_read_serial` itself (`X86_64Helpers.codex:1319`), which
  compares the byte to 13 and skips the store and the length increment, BEFORE
  its CCE table lookup. The DISK path had no equivalent: `fat16-byte-to-cce`
  handed 13 to `from-unicode`, which answers -1, and `code-to-char (-1)`
  produced **CCE 255** where the line ending was, which is the character the
  lexer refuses with CDX1000.
  **Fixed at the same layer the serial reader uses it.** `fat16-read-source`
  drops byte 13 before CCE conversion and is what the compiler's two disk
  source reads call (`opening.codex:978` for cited chapters, `:1756` for the
  source); `fat16-read-text` is unchanged, so the ten test consumers and
  `FileSystem.codex` see exactly what they saw before. `codex/test/fat16-source-cr`
  carries the arm: a CRLF file and an LF file now read to the identical text,
  with `fat16-read-text` as the control showing the 255 still present.
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
- **A6: the desk 3D View GPU path (val, Damian-directed 2026-08-11).** The old
  "not critical" grading is superseded: Damian ruled val onto the parked GPU path.
  **LANDED on main:** (a) **14668** (seed-affecting) -- `runtime-init` re-grants the
  manifest cap mask to proc 0 after re-initializing the proc table, fixing a LATENT
  defect where it zeroed the cap word so ANY payload calling runtime-init lost all
  manifest caps (only surfaced now because only GPU ports/mem are cap-guarded); seed
  converged to **2759215, content A078BD8F**. (b) **14673** (app) -- the desk detects
  a usable GPU (port 0x403) and registers the 3D View app only when one is present,
  else omits the sidebar button and deads the dispatch; on a real machine with no GPU
  driver (the ASUS 970) it correctly does not appear. (c) **14691** (app + `codex-vm`,
  no seed) -- **Stages 1 and 2: the pane RENDERS through the host rasterizer.** A
  viewport (scissor) rect in `codex-vm` confines the clear, depth clear, triangle bbox
  and the fullscreen atmosphere glow; a new `Engine chapter GpuScene` holds the GPU
  scene path `EngineDemo` carried privately, now parameterized by a view rect, and
  EngineDemo loses its copies rather than the tree gaining a second implementation.
  `G` swaps paths in the pane. **The viewport ports are the OUT side of 0x403 and
  0x40F, deliberately inside the 0x400-0x417 window `gpu-port-hi` guards: a port above
  it would be reachable without holding `Gpu.Compute`,** and staying inside kept the CL
  off the seed (`Sut` byte-identical to seed, 0 differing bytes).
  (d) **14703** (app + `codex-vm`, no seed) -- **Stages 3 and 4: SHADOW MAPPING.** A
  light-space depth pass into a host-side buffer plus a per-fragment compare carried
  across from `r3d-shadow-test` unchanged: same fixed point, same bias of 3000, outside
  the map counts as LIT, and a shadowed fragment takes the AMBIENT colour, which is what
  `r3d-cover-sh` does with `sc-amb`. **No port was free inside the guarded window**, so
  the map size rides on 0x402 (which until now only ever carried a zero) and the pass
  flag rides in the count word at 0x400; the per-vertex light position goes in a
  parallel array at 0xBE500000 rather than the shared 72-byte triangle record, six of
  whose words are UV where a non-zero value selects the texture path.
  **Parity is measured, at 75.1 per cent shadow-mask IoU.** A direct pixel compare
  between the renderers is meaningless (one ground is a checkerboard, the other flat),
  so each is differenced against ITSELF with shadows off and the two masks compared;
  both controls read exactly 0 (no mask in the sky over 39055 samples, none on chrome).
  **A6 IS CLOSED (2026-08-13).** Stage 5 landed at main 14818 on Damian's ruling
  that the GPU path is the default iff the machine has one, software otherwise;
  the desk also stopped GATING the pane on the present probe, since otherwise the
  software case was unreachable and the second half of the ruling could not
  happen. On metal with no rasterizer the pane now opens and runs software at
  roughly 1 fps -- slow, but reachable, where before it was hidden.

  **A shadow regression shipped in Update 40 behind a skip and is now fixed
  (14796, 14805).** `codex/test/engine-shadow` is unskipped. The skip said to
  re-mint the oracle; re-minting would have blessed the regression. 14721 filled
  the depth map from the faces facing AWAY from the light, which for an object
  RESTING on its receiver is COPLANAR with that receiver, so `md` equalled `ld`
  and no bias could shadow it -- the shadow detached from its object. Front-face
  casting plus a slope-scaled bias restores it (ground shadow 738 -> 1929 against
  a 1942 pre-regression control, self-shadow acne 0), and the software path now
  runs the host's 3x3 filtered compare so both renderers match on edges too.
  **Second-depth shadow mapping is wrong for anything standing on the surface it
  shadows; that is the durable lesson.**

  The GPU ground texture is DONE, main 14859 (val, 2026-08-13). The pane
  renders the checkerboard and the blue artifact that blocked CL 14844 is
  explained: the host's textured branch IS the earth-globe shader, which reads
  UVs as spherical coordinates, fabricates a sphere normal, and adds a Fresnel
  atmosphere rim worth up to +140 blue. Applied to ordinary ground it mapped
  texel 6E5F4B to exactly 405A9C, which resembled the cube albedo by
  coincidence of hue and is why four geometry hypotheses failed. A single-pixel
  host probe (`CODEX_GPU_PROBE=x,y`, shipped in that CL) named the drawing
  triangle as the ground itself, sampling the correct texel.

  Two claims this register carried are now measured FALSE and are corrected
  here rather than left to mislead again. **`apps/globe/TerrainGen.codex` HAS
  driven ports 0x408-0x40B since they were written** (it commits 0, and
  GlobeDemo uses it), so "no caller" was wrong; and **`poke-byte` exists** --
  TerrainGen packs three RGB bytes a pixel with it, and the host sampled three
  bytes a pixel bilinear. CL 14844 had changed that wire to 32-bit words, which
  read the globe's texture misaligned; the commit value now carries the format
  as well as the shading, 0 being the original three-byte bilinear globe wire
  restored verbatim and 1 one 32-bit word a pixel sampled nearest.

  Also known and deliberately unfixed: a triangle straddling the near plane is
  dropped whole rather than clipped (this orbit distance never reaches it).
  App + `codex-vm` (NOT seed, no token); `codex-vm.c` is a
  shared tool, coordinate with reek before an exe rebuild. val memory
  `val-gpu-desk-render` carries the port constraint, the cull pairing that is easy to
  get wrong, and the frozen-clock rule without which the two arms are not comparable.
- **A8 the desk build loop (fester). The edit half is DONE and the blocker is
  GONE: VT-x IS AVAILABLE ON THE ASUS, measured on metal 2026-08-13.** Plan,
  roads and traps in `docs/Designs/Active/OS/DeskBuildLoop.md` -- read that
  before touching any of it, because one of the three obvious approaches is
  ruled out there. Landed: the Edit pane descends into `SRC/` and saves back
  into it (14802, WORKS-20 closed), a Console pane in the desk (14815), and a
  `vmx` command that reads `IA32_FEATURE_CONTROL` from the desktop (14829).
  **`VmCompile` and `DevHypervisor` are COMPLETE, not stubs** -- a real Intel
  VMX hypervisor written in Codex. `vmxprobe.img` flew on disk 2 and read
  `IA32_FEATURE_CONTROL = 5` (lock set, VMX-outside-SMX set) with revision id
  4, against the 1 the bed reports. **Do not re-measure this in the bed and
  conclude anything**: two codex-vm readings agreed with each other, and both
  were irrelevant to the hardware. So Road A is the road and Road C needs no
  pricing. **The next wall is the arena, not the wiring** -- `vm-compile`
  takes the seed as `List Integer`, 2.7 MB as boxed integers, which nobody has
  measured; start with `SRC/NAME.COD` at 307 bytes, the smallest thing on the
  stick.
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
  **CTRL IS READ-ONLY ON THIS PART (boots 3 and 4, 2026-08-13).** Clearing
  `CTRL.SLU` and reading it straight back gives `0x180240` unchanged, and
  `CTRL.ASDE` refuses to set even on an arm running first from the firmware
  state. **MDIC writes DO work** (`e1000-phy-write` writes MDIC at `0x0020`
  and polls it ready, and succeeds every arm), so the CSR write path is
  fine and CTRL specifically refuses. **B2c is NOT blocked**: RX/TX needs
  RCTL, TCTL, the ring registers and RAL/RAH, none of them CTRL.
  **Finding 4 is CLOSED**: ASDE is inert because the bit is not writable.
  **Two claims below are now wrong and are corrected in the run sheet.**
  The link is brought up by `na-phy-kick` over MDIO, not by our CTRL write.
  And the reset "not wedging on a warm part" describes an event that never
  happened: the `CTRL|RST` write is discarded, RST is never set, and
  `e1000-await-reset` answers `settled=1` on its first read. **Do not
  pursue the cold-versus-warm reset hypothesis.** What wedged the box on
  08-11 is unexplained again, and it was not CTRL.RST.
  **No further sittings are needed to develop this.** `codex-vm
  -e1000-ctrl-ro` reproduces the board exactly and `codex/test/e1000-ctrl-ro`
  pins it, with a control proving three of its six rows flip when the arm
  is off.
  **FLOWN 2026-08-13: THE LINK COMES UP ON THE REAL I219.** Touch reads
  `LU=0` with a live cable; both arms then read `LU=1 FD=1 SPEED=1000`, and
  `ICR` carries Link Status Change. The touch row is read before any write
  and is byte-identical to the 08-11 flight, so it is the control that says
  our code did it rather than the PHY doing it alone. **B2 link bring-up is
  ACHIEVED and B2c (RX/TX on the real part) is unblocked.** The account,
  with the full decode, is `docs/Hardware/HardwareSitting.md` at the top.
  **The reset did NOT wedge**, on the same driver code that wedged on
  08-11; the only change was ORDER, with the arms warming the part before
  RST fired. One observation, not a mechanism -- but `e1000-init` resets
  cold, which is the order that wedged, so it is worth a discriminating
  flight (cold reset first, then warm).
  **Finding 4 is still NOT answered.** Both arms returned identical values
  where the bed has them differing sharply, and three explanations survive:
  ASDE is inert, the write does not stick (the probe never reads `CTRL`
  back), or arm 2 inherited arm 1's link. The arms are not independent,
  which is a defect in the probe. Next arm: read `CTRL` back after each
  write, run `ASDE=1` first from cold, print the bound USB VID:PID.
  **The F12 bank failed as a TRANSPORT failure, not a GPT one**, and that
  refutes the eject-and-reinsert hypothesis the run sheet carried: stage 1
  means the read itself failed (`c4` = USB transaction error in the CBW
  phase, `l1` = the GPT header sector, recovery failed). Not WORKS-9, whose
  signature is a data-phase timeout with no completion event; `f945044` of
  1000000 clears fuel by WORKS-9's own criterion.
  **The 2026-08-11 flight settled which step wedges, and it is the RESET,
  not ASDE.** Two rows painted, eligible and the read-only touch
  (`STATUS=0x40080080 CTRL=0x180240`, so the BAR is right and reads
  complete), and nothing below them. ASDE is UNTESTED rather than
  disproved: the bit was never written.
  **`AsdeStageProbe` was rebuilt 2026-08-13 and the order is reversed.**
  Both arms now run with NO RESET at all -- `na-bring-up-after` is
  `na-bring-up` minus the reset, and firmware leaves `CTRL.SLU` set -- so
  Finding 4 gets answered whether or not the reset is ever solved, and the
  reading is banked ABOVE the step known to kill the box (L-BANK). The
  reset then rides last, split into its five MMIO operations with a row
  before each, so the glass names which one did not return (L-STATES).
  The driver is deliberately unchanged from the flown build: moving the
  reset path would make a different outcome uninterpretable.
  Bed-verified both ways. **The arm is `build/boot/asdeflight.img` and the
  procedure is `docs/Hardware/HardwareSitting.md`, the section headed "THE
  ARM, kept for the next flight"** -- it carries the outcome table, the new
  SHA-256 and the 2500 ms screenshot delay. **Awaiting a sitting.**
- **B3 TCP over the real part: the stack now holds a real TCP conversation
  over the e1000, and it did not before 2026-08-14 (blu, main pending).**
  Nothing had ever run TCP over that card: every TCP test in the tree
  (`tcp-reliability`, `tcp-seqwrap`, `tcp-listen-reclaim`, `net-io-clock`)
  cites `NetworkStack`/`Tcp`/`Ethernet` and touches no NIC at all, and the 55
  plugs that DO speak TCP over a wire all ride the NE2000. DHCP over the
  e1000 was the whole of it, and DHCP is one UDP exchange.
  **The blocker was a wall-clock constant wearing tick clothing, and it lands
  on metal.** `NetIO` counted a tick as 100000 empty receive polls. Measured:
  one million empty polls cost **15.52 s on the NE2000 and 0.029 s on the
  e1000**, because an NE2000 poll is a port IN and therefore a VM exit while
  an e1000 poll reads a descriptor out of RAM -- which is what the real
  I219-V does too. Every bound in `NetworkStack` is a count of ticks, so the
  same four constants meant give-up at 219 s on one card and **405 ms** on
  the other, with the first SYN retransmit at **8.6 ms**. That chapter's own
  prose argued this could not happen; the argument covers the ESTIMATOR,
  where srtt is in the same ticks, and not the fixed counts. Corrected in
  place rather than left to mislead again.
  **`net-driver-calibrate` measures the rate once at bring-up** against HPET
  and stores it in cell 36328, and `net-io-tick-interval` /
  `net-io-max-polls` derive from it. A guest that never brings a driver up
  reads the old 100000, so every plug keeps the numbers it was tuned with.
  Achieved: tick 105 ms on the NE2000 and 66 ms on the e1000, a 540x spread
  closed to 1.6x. `codex/test/net-poll-calibrated` pins it with the OLD
  constant as its own built-in control, which reads out of band on both
  cards -- so the arm can be seen to fail.
  **A second defect underneath, in `tools/codex-vm.c`.** A retransmitted SYN
  re-entered the NAT's new-connection branch, opened a second host socket
  over the first and leaked it, and the guest's payload went to a socket
  nobody had accepted. A SYN retransmit is correct TCP, so the bed could not
  express one at all. Isolated three ways on one guest binary: old exe + old
  stack FAILS (4 SYNs), new exe + old stack PASSES (3 SYNs), new exe + new
  stack PASSES (1 SYN).
  **What is NOT proven: this is the bed, not the board.** The e1000 model's
  poll is a RAM read like the real part's, which is why the calibration is
  expected to hold, but no flight has measured it. The arm is bed-only
  because it needs a host TCP peer; 30 existing tests, `nat-conn-churn`,
  `cdx-serve` and `tls-interop` are green, and the gate is green.
- **The metal questions this track has left are QUEUED, not scheduled:
  `docs/Hardware/HardwareSitting.md`, the section headed "THE SITTING QUEUE"
  at the top of the file** (opened 2026-08-14 at Damian's direction). Five
  questions, NIC-1 to NIC-5, designed to ride ONE boot in an order that is
  argued rather than preferred: the pure reads bank first because a write
  destroys their control, and the arm that could hang the box goes last.
  **Damian's standing ruling still holds and this section does not soften
  it: agents do not propose flights.** The queue exists so that a sitting he
  decides to hold does not spend its first half hour being designed. NIC-2
  is the one that matters most -- whether the poll-rate calibration transfers
  to the real part is the single assumption B3 and B4 now rest on.
- Then **B4** serve the repository protocol. EdgeMesh Phase 2
  (`docs/Designs/Active/Features/EdgeMeshGameServers.md`) is the consumer
  waiting on B2-B4; nothing to do there until the track lands.
- The ARM64 send path and the lost SYN are both closed (fester,
  2026-08-10). `arm64-net-io-send-chunk` reads the refusal and drains,
  the ARM64 loops tick, and `net-io-wait-established` ticks and gives up
  rather than running out a cap below one tick interval. **There IS an
  ARM64 bed on this box**, which is what the entry was blocked on:
  `build/test-cross-batch.ps1 -Arch arm64` runs under Renode, and
  `arm64-send-refusal` is a no-peer arm that PASSES there. What that bed
  cannot reach is virtio RX and a real peer, so the drain's own polling
  is still unrun on metal.
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
  - **State the residual hole every time C1 is reported. The witness has
    been falsified, so the hole is now stated precisely.** Sabotage arms
    2026-08-10 (`OperatorsManual`, "The witness has a negative control")
    poisoned the code generator and then constant folding, each payload
    living only in the binary and in no source; both went RED against the
    tampered build and reconstructed the honest seed. **The measured
    boundary is self-reproducing versus not:** any payload in the binary
    alone is caught, because the double-compile rebuilds from clean source.
    The sole survivor is a self-reproducing quine, which is a high bar, not
    the loose "the seed sits upstream." The seed does still sit upstream
    (it produced the IR and compiled the plug), and the `source -> IR` arm
    a complete Wheeler DDC wants is DONE -- the C# arm parsed raw source and
    emitted a byte-identical CDX on demand this cycle -- so the residual is
    the quine and nothing smaller.
  - **OPEN, and named on purpose because the README now states it publicly:
    build the self-reproducing quine that DEFEATS the witness.** The two
    sabotage arms proved the boundary is a quine and nothing smaller; the
    quine itself is the falsification arm for that boundary, and until it
    exists the "sole survivor is a quine" claim is reasoned, not measured.
    It is a real construction: a trojan that recognises it is compiling the
    compiler and writes a copy of its own injector into the emitted IR, so
    stage1 and stage2 inherit it across two generations. The tractable
    injection point is the IR TEXT emitter (`ir-emit-def`,
    `IRTextEmitter.codex:820`), because IR text is the interchange format and
    a self-reproducing string is easier to get right than self-referential
    IR nodes. Test it with the exact procedure in `OperatorsManual` "The
    witness has a negative control": build the trojaned compiler from
    sabotaged source with the clean seed, REVERT the source, then confirm
    stage2 MATCHES the tampered build (green) rather than the clean seed.
    val's, follows from the sabotage work; ~4 min per emit/build/compare
    cycle and high iteration, so budget it as a session, not a wedge.
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
    4** -- 88 of the 92 closed in the ARTIFACT and the rechecker derived the
    remaining 4. Account in `IndependentRechecker.md`; the control that was
    the evidence and was read as a fact is L-CONTROL.
  - **The remaining 4: CLOSED 2026-08-14, 4 to 0** (val 15022, main 15023,
    seed-affecting, seed converged at 2760410). They were the same defect one
    level down, in the BRANCHING NODE rather than the lambda -- but **this
    register's account of WHY was wrong and the correction is the useful
    part.** It said the body's "own recorded type is the bare variable, so
    there is nothing concrete to substitute." The branch bodies were concrete
    the whole time: in `fold-expr`'s `for stmt in ss -> when stmt` both arms
    type as `(sum "IRActStmt")`. The node DISCARDED that. `lower-match`
    already had `infer-match-type`, which derives the type from the arms, and
    consulted it only `when ty is ErrorTy`; a bare type variable is not
    ErrorTy, so it took `is otherwise -> ty` and threw the derived type away.
    The `if` is the same shape through `merge-ty`, which answers `a` unless
    `a` is ErrorTy. Both now consult a witness and `lower-lambda`'s existing
    substitution propagates it, so no change was needed there.
    **Whole compiler: AGREE 4862, DISAGREE 0, UNSUPPORTED 0, IMPROVED 0,
    SINGLE-WITNESS 0**, kill-rate 27/27 with a passing control, and
    `tvar-spine-branch-arms` still CAUGHT -- unlike the lambda fix, this one
    cost the rechecker no arm. **The rechecker now raises exactly ONE finding
    against the whole compiler**, the underived range below.
    - A witness is sound only where the unifier admits no widening, which it
      does for integers and reals at a non-argument position, so those are
      refused and every other form is nominal or invariant. **A first attempt
      guarded agreement with `types-equal` and measured NO CHANGE AT ALL**:
      that function fell through to `otherwise -> False` for ten of the 26
      CodexType variants, so it answered "not equal" on two identical sum
      types and every witness was rejected. Never a soundness gap -- its only
      caller is a fast-path short-circuit in `unify-resolved` whose
      fall-through unifies those forms correctly -- but a trap for the next
      caller, so **it is now total** (Damian's call: fix it rather than
      document around it). **The fast path it was declining is worth nothing
      measurable**: same input, old compiler against new, 11.8/11.96/12.41 s
      against 11.8/12.38/12.30 s, a noise floor of about 0.4 s. The value is
      that the trap is gone, not speed.
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

## The Shell DSL backport -- CLOSED 2026-08-14, now ambient

**Damian's ruling 2026-08-14: tie it up, take it off the board, keep it as a
routine check rather than a campaign.** Re-measured at seed `D9A6A7A2` on the
day it closed (L-COUNT -- do not copy these forward): **46 generators checked,
4 drifted, 0 broken, 1 dead target.** The 4 are exactly the backlogged stubs
(`test`, `cdx-to-pe`, `build`, `build-img`), which stay backlogged by the
2026-08-06 ruling. **Every ordinary generator in the tree matches the script it
ships beside.** The entry that stood here said 12 drifted with eight generators
left to close, and it was a full campaign out of date.

What keeps it closed is two mechanisms, neither of which is a campaign:

- **The banner.** Every generated script opens with `generated-banner`, so a
  file carrying it is exactly a file that matches its generator. That is the
  hard rule at the top, and it is what discourages the hand edit.
- **`build/check-generated-scripts.ps1`**, a leg of `build/build.ps1` at about
  61 s. It fails on a BROKEN generator or a NEWLY drifted one, never on the
  baselined four. Since 2026-08-14 it also reports the other direction: a
  script under `build/` that no generator emits, against
  `build/handwritten-scripts.txt`. **That half is report-only and must stay
  that way** -- 93 of 135 scripts under `build/` have no generator and most are
  meant not to (probes, flight arms, interop harnesses, one-offs). Gating it
  would be 93 reds whose answer is always "expected", which is the failure the
  drift baseline already exists to avoid.

Method, traps and every worked example are in
`docs/Designs/Active/Build/Build.md`; read that before touching a generator.

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

## Bare `print-line` printed raw CCE -- CLOSED in the primitive, premise now inverted

Found 2026-08-11 (blu) closing the browser's garbled banner. **`print-line`
is the RAW builtin**: `Types/Builtins.codex` lowers it through
`emit-print-line-raw-builtin`, which writes the internal CCE bytes at the
serial port with no conversion at the I/O boundary. `print-line-uni` is the
one that converts, and it is the only line-printer
`codex/foreword/core/Console.codex` actually declares. The name is the whole
trap: the unadorned one is wrong and the suffixed one is right.

It does not fail loudly -- it prints, and the bytes are wrong in a way that
reads as a font or terminal fault. `Codex Browser v0.1` arrived as `2` then
`$:` (`C` is CCE 50 = ASCII `'2'`, `e` is 13 = carriage return, `B` is 58 =
`':'`), and it was carried in the browser backlog as a display problem for
weeks.

`DevelopersGuide.md` is where these came from and is fixed: all three of its
`Effects and Act Blocks` examples said `print-line`, and its `effect Console`
block declared a `print-line` the platform does not have.

**CLOSED 2026-08-13 by blu, in the PRIMITIVE, and the premise above is now
INVERTED. Do not start the sweep this section describes.** Main 14806/14809:
`print-line` lowers through `emit-print-line-builtin` and CONVERTS; the
byte-exact one is now named `print-line-raw`. So the unadorned name is the
RIGHT one and the 192-site sweep is not work that exists.

Re-measured 2026-08-13: **85 bare sites**, not 192, and they are correct by
default. `apps/fishtank` went 43 to 1. Heaviest now are `opening.codex` 31,
`RadioStationMain` 9, `FontExtract` 9, `FontTrain` 7, `ChatServer` 5.

**The residual is the OPPOSITE hazard and it is much smaller:** a WIRE emitter
still calling the unadorned name now gets converted bytes where it needs exact
ones. blu pinned the two known ones (the fishtank page emitters) to
`print-line-raw` and proved the bytes unmoved, same SHA and byte-identical
serial stream. Anything else that emits a wire format through `print-line`
wants the same treatment, and that is an audit of emitters rather than a sweep
of call sites.

**This entry is kept rather than deleted because the trap it documents
reversed direction**, and a reader who half-remembers the old rule will reach
for the wrong name. L-COUNT and "prefer the fix in the primitive": the sweep
was 192 sites of per-site judgement and the primitive change closed all of it.

## The cost model -- proposed 2026-08-14, unscheduled

`docs/Designs/Active/Features/CostModel.md` (blu, at Damian's direction).
**Damian has ruled the SHAPE: it sits in the same part of the rainbow as
`punctual`,** so it is a declared property checked transitively and refused at
compile time, not a document reviewers are asked to remember.

The gap is `punctual`-shaped because `punctual` forbids the case: CDX6002
refuses heap allocation outright, so a `punctual` function is one that does
not allocate, and there is no way to say "this function allocates, in
proportion to its input" or anything that checks it. All three defects
measured on 2026-08-14 live in that middle -- `unpack-text` appending per byte
(main 15054), `NetIO`'s tick as a count of polls and `e1000-await-tx`'s fuel
(main 15013, 15028). Each was semantically correct, each was green, and two
produced WRONG BEHAVIOUR rather than slow behaviour.

It is a proposal with zero implementation and it is not scheduled. The open
questions are in section 5 and the sharp one is what instrument keeps it
honest, since the type rules only got teeth because the rechecker abstains
where the guide is silent and nothing equivalent exists for cost.

## Rulings Damian owes (the only queue that blocks)

(empty -- A5's owner question closed itself when reek shipped it
2026-08-14.)

## File claims (one owner at a time)

| File | Claimed by |
|---|---|
| `tools/codex-vm.c` | FREE -- one owner at a time, announce before you start |
| `apps/works/GopBoot.codex`, `GopWizard.codex`, `apps/guios/**` | red |
| `apps/works/GopDesk.codex` | FREE -- announce before you start |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/plugs/csharp/**`, `build/` DDC harness | fester |
| `codex/plugs/recheck/**` | val |

`GopDesk.codex` was claimed by red and is released 2026-08-11 (blu, at
Damian's direction) because the claim had stopped describing anything: three
agents changed that one file on 2026-08-11 alone -- blu 14641 (mouse in every
pane), val 14673 (GPU-gated 3D View) and blu's Appearance pane. A claim
nobody honours is worse than no claim, because it reads as coordination
while providing none. **It cost something real this time**: val and blu
independently took `ds` cell 48, which no gate could catch because each
change is green alone, and the collision surfaced only in the merge. If you
are going into this file, say so first, and check which `ds` cells are
already spoken for in the Appearance section.

## Standing rules that gate nothing but bind everyone

Battery runs are Damian's (release proofs excepted, per the release
skill). Goldens stay parked during active GUI work. No new platform-wide
register. Prose about our own code is deleted in files you touch. The
em-dash stays banned.
