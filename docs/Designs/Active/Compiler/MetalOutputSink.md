# The metal output sink (A5 blocker 3)

Owner: reek. Opened 2026-08-08. A5 blockers 1 and 2 are the prerequisites
(1 closed at CL 14210; 2 has its fix on disk, unlanded).

## The problem, as measured rather than assumed

`emit-binary-tail` (`codex/compiler/opening.codex:1449`) is the only sink:

```
print-line-uni ("SIZE:" & ...)
write-binary     (result.header-bytes)
write-binary-buf (result.content-buf) 0 (result.content-len)
write-binary     (result.tail-bytes)
```

`__write_binary_buf` (`codex/compiler/Emit/X86_64Helpers.codex:3086-3196`) has
exactly two paths, and neither exists on the ASUS:

1. **Host blit.** Read port `0x511`; if it answers `0xB7` this is codex-vm, so
   store the base at cell 36160 and the length at 36168 and `out 0x510, 3`. The
   host writes the file. A board answers nothing.
2. **Fallback: COM1, one byte at a time.** Poll the LSR at `0x3FD` for THRE,
   then `out 0x3F8, al`. The ASUS has no UART.

So on metal 2.6 MB goes out a port nobody reads. `emit-vga-progress`
(`X86_64Boot.codex:1694`) is not a third channel either: it stores into the VGA
text buffer at `0xB8000`, which a UEFI boot does not have.

The diagnostics have the same problem. `print-line-uni` is the Console effect,
Console is serial, and every `DISK:` failure message the compiler can produce is
therefore invisible on the board too.

## The decision: write the CDX to the volume

The plan offered two options, "the CDX is written to the volume or a verdict is
painted". **Write the volume.** Painting is second, and small.

1. **A verdict alone is not the deliverable.** A5 is the compiler producing a
   CDX on the box. A painted `ok` proves it did not fault; it does not hand back
   the artifact, so nothing can be compared against the host compile, which is
   the only check that means anything.
2. **The writer already exists and is already cited.** `opening.codex:5` cites
   Foreword chapter Fat16, which carries `fat16-write-binary-file` and the whole
   create/replace/chain path (`Fat16.codex:1214`, `:532`, `:304`).
3. **Painting cannot reuse GopDraw.** `gop-draw-text` and `gop-draw-text-wrap`
   exist (`apps/works/GopDraw.codex:105`, `:138`), but the quire order is
   `codex.foreword -> codex -> codex.os -> apps` (`DevelopersRulebook.md:236`),
   so the compiler cannot cite them. Painting means new foreword-level GOP code;
   the volume needs one new function on an existing path.
4. **This writer does not carry red's GopFat16 gap.** `HardwareSitting.md:302`
   records that GopFat16 never verifies a cluster it is about to take lies
   outside an existing chain. The foreword allocator does:
   `fat16-find-free-cluster` scans the FAT for a genuinely free entry
   (`Fat16.codex:197-215`) and `fat16-claim-cluster` marks it `0xFFFF` before
   anyone links to it. They are two different writers and only the compiler's
   is on this path.

A multi-megabyte FAT write does come home from the real board:
`HardwareSitting.md:233`, `SH183500.BMP`, 2,359,350 bytes, extracted intact.
That is GopFat16, so it proves the USB BOT write path and the stick, not this
writer.

## The price, and it is not the one the bed already measured

**The compiler's writer is exercised only at trivial sizes, and never on
metal.** Its callers are `codex/test/scope-runtime-{open,deny,spawn}.codex`,
`fat16-subdir`, `editor-save` and `apps/works/KeyManager.codex:133` -- all
small text through `fat16-write-file`. Nothing anywhere drives it at megabyte
scale. `fat-write-guard` does not count either: despite citing Foreword
chapter Fat16, its 3 KB write is `gfat-write-file`
(`fat-write-guard.codex:203`). `codex/test/apps/fat-write-big.skip`'s 3 MB in ~100 s is
NOT this writer: `fat-write-big.codex:3` cites Works chapter GopFat16 and
`:56` calls `gfat-write-file`. GopFat16 builds the chain in a loaded FAT,
flushes both copies in bulk, and moves data in 64-sector chunks
(`fat-write-big.codex:8-11`). The foreword writer does none of that, so its
benchmark does not transfer and there is no measured wall-clock for this path
anywhere in the tree.

Two properties of the foreword writer decide the shape of the work, and both
are in the code rather than inferred:

- **The free scan restarts at cluster 2 on every allocation.**
  `fat16-extend-chain` calls `fat16-alloc-cluster` per cluster (`:310-316`) and
  `fat16-find-free-cluster` is `fat16-scan-free-sectors vol 2` (`:197-198`).
  Already-claimed clusters are no longer free, so each allocation walks past
  every one before it. Quadratic in the chain length.
- **Every sector read is a permanent allocation.**
  `emit-block-read-sector-helper` bump-allocates off the heap pointer with
  `add r10, 512` and never frees (`X86_64Helpers.codex:3374`). The chain walk,
  the free scan and the read-modify-write of each data sector all go through
  it.

Together those put the arena, not the clock, in front.

### Measured 2026-08-08

Two writes through `fat16-write-binary-file` on a 16 MB FAT16 image under
codex-vm, each bracketed by `__heap-save` so the delta is the writer's own
allocation and not the source list's:

| bytes written | heap consumed | bytes of heap per byte written |
|---|---|---|
| 65,536 | 3,622,688 | 55.28 |
| 131,072 | 7,279,552 | 55.54 |

Both `ok=1`. Doubling the input doubled the heap (ratio 2.009), so the cost is
**linear at this scale and about 55 bytes of arena per byte written**. Per
512-byte cluster that is ~55 sector buffers: one data RMW, four for the two FAT
copies, and ~50 walking the free scan.

The seed CDX is **2,745,998 bytes** (re-measured 2026-08-09; CLAUDE.md's
"~2.1 MB" is stale). At the measured rate that is **~144 MB**, against a
**128 MB** arena shared with the compiler's own selfhost working set
(L-ARENA). And 144 MB is a floor, not an estimate: the free scan's cost per
allocation grows with the frontier, which is why the constant is 55 here and
can only rise over 5,300 clusters.

**The write does not run out of time. It runs out of arena, and it does so
before speed is even a question.**

So the buf retarget below is necessary and **not sufficient**. The allocator
needs a cursor instead of a restart at cluster 2, and the sector buffers need
reusing rather than bump-allocating per read. Both are on this same path.

### Fixed and re-measured 2026-08-08 (part 1, CL 14245)

Same probe, same seed, same image, control and fix run back to back:

| bytes written | control | fixed |
|---|---|---|
| 65,536 | 3,622,688 | 84,768 |
| 131,072 | 7,279,552 | 104,384 |

Marginal cost falls from **55.8 to 0.30** bytes of arena per byte written, so
the projection for a 2,745,998-byte CDX goes from ~144 MB to **~866 KB**. The
arena objection is closed. (The 0.30 rate over-projects: measured at full size
below, the real figure is 50,608 bytes. Fixed cost still dominates at 131 KB,
so the marginal rate taken there does not extrapolate.)

The read-back oracle is `fat16-read-bytes`, which shares no code with the
write path, and both files are verified AFTER both writes so an overlapping
chain shows up in whichever was written first. Both `bad=0`. The oracle is not
vacuous: its two calibration arms fire, `bad=65536` against a shifted source
and `SIZE=65536` against a wrong claimed length.

Thirteen standing tests over this path pass: `fat16-alloc`, `-dirgrow`,
`-list`, `-mkdir`, `-overwrite`, `-subdir`, `-write`, `scope-runtime-{deny,
open,spawn}`, `editor-save`, `fat-write`, `fat-write-guard`.

The probe that produced the table above cannot be the instrument for the
2.7 MB run, and not only because its payload is a `List Integer`. Its oracle
`fat16-read-bytes` bottoms out in `fat16-read-cluster-bytes`
(`Fat16.codex:1133-1149`), which is unbracketed and has exactly the defect the
write path just lost: one permanently bump-allocated 512-byte buffer per sector
read, plus a `list-push` per byte. At 2.7 MB that is ~2.7 MB of dead sector
buffers and a ~21.8 MB list, inside the same 128 MB arena that has to hold the
compiler. The 2.7 MB verification therefore needs a different instrument:
drive `fat16-write-segments` from a buf filled with `poke-byte`, and verify by
streaming sectors through a running checksum rather than materialising a list.
The read path's own allocation defect is a separate finding and is not fixed
here.

### The 2.7 MB run, measured 2026-08-09 (part 3)

Built as that second instrument says. A buf of 2,745,998 bytes (the current
seed CDX size) is filled with `int-mod (i * 7 + i / 513) 251` through
`poke-byte`, written by one `fat16-write-segments` call, and verified by
walking the FAT chain and comparing each sector in place, every sector
bracketed by `__heap-save`/`__heap-restore` so the oracle is O(1) arena.

Two beds, both green:

| bed | first cluster | chain | recorded size | arena | bad |
|---|---|---|---|---|---|
| `-kernel` on a 16 MB FAT16 volume | 4891 | 5364 | 2,745,998 | 66,152 | 0 |
| `-uefi` PE payload, `-HeapPages 32768` | 226 | 5364 | 2,745,998 | 50,608 | 0 |

`chain` is 5364 = `ceil(2745998 / 512)`, and it is printed precisely so a run
that resolved nothing convicts itself: `sb-walk` returns 0 for a cluster below
2, which is indistinguishable from a clean verify without it.

**The oracle fires.** Checking against the pattern shifted by one byte gives
`bad=2745998`, every byte of the file, so `bad=0` is a measurement rather than
an instrument that cannot fail (L-FALSIF).

**The second row is the one that answers the arena question.** `-HeapPages
32768` is 128 MB exactly, which is the flying boot image's whole envelope
(L-ARENA), and the 2.7 MB source buffer is resident inside it alongside the
write. The measurement above is NET heap growth and says nothing about peak;
completing in that envelope does. The ~144 MB projection is dead by
measurement, not by argument.

The instrument is `codex/test/apps/fat-sink-big.codex` and its `.skip` carries
both recipes and the calibration. About 5.5 minutes under `-uefi` and 3 under
`-kernel`, which is why it is by-hand and not a battery leg -- the same
arrangement `fat-write-big` has for the Works writer.

### FLOWN 2026-08-09 AND IT WROTE NOTHING. The cause is that this sink cannot reach a USB stick at all.

Damian booted `a5flight.img` on the ASUS. **The returned stick differs from the
flashed image in exactly two sectors, LBA 0 and 1, and those are the two
`flash-usb.ps1 -SpecFit` rewrites at flash time.** Every other byte of 16 MB is
identical. The board wrote nothing.

**The mechanism, reproduced in the bed rather than inferred.** `block-read-sector`
dispatches on exit mode (`X86_64Helpers.codex:1613-1617`): `ExitUefi` gets
`emit-uefi-block-read-sector-helper`, everything else gets the raw IDE port
reader. The payload is the depot seed, which is compiled in plain `Exit` mode, so
its block I/O is **IDE port access**. `codex-vm -disk` presents an IDE device, so
every bed run this design records passed on a path that does not exist on the
target: the ASUS boots from USB mass storage, which is not an IDE device.

Dropping `-disk` reproduces it exactly -- `codex-vm -kernel <img> -uefi -headless`
with no IDE device faults at `EXC=00`, a divide by zero, and writes nothing. That
is `fat16-parse-bpb` reading a sector of zeroes, taking `bytes-per-sector` 0 and
dividing by it downstream, which is the case `Fat16.codex:49-51` describes in
prose and does not fully guard.

**`-Uefi` is necessary and not sufficient, and this is the part that scopes the
work.** Compiling the payload with `compile.ps1 -Uefi` moves reads onto UEFI
Block I/O and moves `print-line-uni` onto `__uefi_print`, which also ends the
silent-box problem: diagnostics land on ConOut. But **`emit-block-write-sector-helper`
(`X86_64Helpers.codex:1619, 3462`) has no exit-mode dispatch at all.** It is
unconditionally the raw IDE writer. So a `-Uefi` payload would read its source
and then fail to write its artifact, which is the entire deliverable.

**What A5 needs is a UEFI block WRITE helper**, the counterpart of
`emit-uefi-block-read-sector-helper`. `EFI_BLOCK_IO_PROTOCOL` is Revision 0,
Media +0x08, Reset +0x10, **ReadBlocks +0x18, WriteBlocks +0x20, FlushBlocks
+0x28** -- read off the existing helper, which calls `[r13+0x18]`. Seed-affecting
codegen, and it takes the build token.

**Landed at main 14398 with the seed.**

### THE ExitUefi BLOCK PATH WORKS. The systab cell was living inside the page tables.

`uefi-systab-addr` was cell 36208 = 0x8D70, which is entry 430 of the PML4 that
`emit-build-process-page-tables` puts at 0x8000 and zeroes wholesale.
`build/cdx-to-pe.ps1` primes that cell before jumping and the page-table build ate
exactly that write, so `[uefi-systab-addr]` was not a live SystemTable when the
helper ran: `rbx` was not BootServices, `UEFI_TRAP_BOOT_LOCATEPROTO` (39) never
fired, `r13` took stack garbage (`0x4b000`, where a real interface is
`UEFI_TABLE_PAGE + 0x800 = 0xf0800`), and `call [r13+0x18]` jumped into padding at
`0x0fea`, faulting `EXC=06`.

The cell is now 30704, below the tables, confirmed unclaimed across
`tools/codex-vm.c`, `apps/works`, `codex/foreword` and the compiler;
`cdx-to-pe.ps1` carries the constant and moves with it. `emit-start` copies
`0x8000` in only when the cell is still zero, so a pointer an earlier stage primed
survives. Measured under `-EntryStart`: the cell read 0 before and reads
983040 = 0xF0000 after.

**A second defect hid the write, and it was in the bed.** codex-vm's
`UEFI_TRAP_BLK_WRITEBLOCKS` shared a case label with `BLK_RESET` and fell through
to a bare break, returning EFI_SUCCESS and writing nothing, so no arm could tell a
correct writer from a missing one. Fixed in the same changelist.

**Proven end to end 2026-08-09.** A payload built with `compile.ps1 -Uefi` at
`-HeapPages 32768` read a 2,766,116-byte `SOURCE.SRC` off the volume and wrote
`OUT.CDX` at 2,753,312 bytes, **byte-identical to the host compile of the same
source with the same kernel**, in 5.0 minutes, with serial producing zero bytes.
The control is the same image against codex-vm before 14398: 0 bytes changed and
no `OUT.CDX`. A payload on IDE would have written there, so the write went through
UEFI Block I/O and not the transport the board lacks.

**The helper's failure path is CLOSED.** Both UEFI block helpers now test the
SystemTable pointer, the LocateProtocol status and the returned interface before
calling through any of them, and the read helper tests the ReadBlocks status too.
On any of those the read returns 0 and the write returns a non-zero status,
instead of calling an address taken from uninitialised stack.

Measured on the same probe with the same image, the arms differing only in which
compiler built the payload, with no `-disk` so LocateProtocol genuinely fails:

| arm | result |
|---|---|
| before | `EXC=06 RIP=0x0fea`, `R13=0` -- a jump into padding, and NO output at all |
| after | `read2048 sum=0`, `bps=0 spc=0`, `write=False`, then `EXC=00` |

So the failure is now legible off a stick: three printed lines saying the block
device was not there, rather than a dead machine (L-STATES). The `EXC=00` that
follows is the pre-existing unguarded divide in `fat16-parse-bpb` (`Fat16.codex`
49-51), now REACHED in a defined way rather than jumped over. With `-disk` both
arms are identical (`write=True`, `readback=blkprobe wrote this`), so the guards
cost the success path nothing.

This is L-UNCALLED: the helper was compiled into every ExitUefi payload, invoked
by nothing until now, and broken the whole time. Every A5 bed run in this document
before 14398 was plain `Exit` mode over IDE, so nothing had ever run it.

Two things this does NOT change. The 2.7 MB measurement above stands: it is about
arena, and it was taken through the FAT writer either way. And the bed remains
correct for everything except the storage transport.

**L-OPTIONAL and L-ARENA in one: the bed was not less faithful, it presented a
device class the target does not have.** Every green run in this document was
taken over IDE.

**Still owed: the write has never been exercised on metal at any size.**

### Wired and measured end to end 2026-08-09 (part 2)

A guest compile on codex-vm now lands its artifact on the volume. `SOURCE.SRC`
compiled under `DISK`, `OUT.CDX` written at 84,462 bytes and extracted back out
of the FAT by an independent host-side reader: **byte-identical to the same
source compiled on the host**, magic `CDX1`, with `OUT.TXT` reading
`OK OUT.CDX 84462` beside it. Serial output for that run fell from 84,616 bytes
to 122, which is the sink moving rather than duplicating.

**Why the first wiring was dead code, measured rather than guessed.** The
trigger was `parse-mode-cmd mode == "DISK"` evaluated inside `emit-cdx`, and it
was always false. A probe at the head of `emit-cdx` printed `mode` raw: on the
DISK path it arrives as twelve bytes of unrelated memory
(`35 e3 83 a2 00 00 00 00 00 00 00 25`), not a damaged string but not a string
at all. So every `has-mode-flag` answers False and the `== "DISK"` fallback
fails with it. `mode` is intact at `dispatch-on-mode` (`opening.codex:1726`,
which is how the DISK branch is taken at all) and is passed by name with no
transformation to `emit-from-disk` and on to `emit-cdx`, so something inside
`emit-from-disk` overwrites its storage.

The fix does not wait on that diagnosis. `emit-from-disk` knows which path it
is on; `emit-cdx` now takes `force-disk : Boolean` and is told, rather than
re-deriving it from a value in that state. Two hypotheses died by reading
instead of building: `read-line` cannot be reusing one buffer, because it
bump-allocates and commits (`X86_64Helpers.codex:1025,1035`), and the `|`
operator was never involved.

**The `mode` corruption is a separate, pre-existing defect and is not closed by
this design.** The depot seed loses `debug` on the DISK path too, and my change
is not in that binary. It disables every mode flag for a metal compile,
including `uefi`, `decks=` and `passes=`. It belongs on the A5 list in its own
right and wants a small standalone repro rather than a compiler rebuild per
hypothesis.

### Two measurements on the read path, for blu, who owns it (2026-08-09, val)

The join and threaded table at CL 14371 are the fix and nothing here proposes
another. These are two facts measured beside it that the CL does not cover.

**1. The per-byte mapping that CL 14371 deliberately preserves loses data at
or above 128, and it ALIASES.** Round-tripping one byte through
`fat16-bytes-to-text` and back with `text-to-unicode-bytes`: `65` and `10`
return themselves, but `200` returns **1536**, and **`13` and `255` both
return 8768**. Two distinct bytes become the same text, so the loss is not
recoverable by a later reader. Verifying the new construction agrees with the
old one byte for byte is the right check for a performance change and it
cannot see this, because both sides share the mapping. Whether a file on a
FAT volume is ASCII-only by contract is the question that decides whether it
matters; A5's source path is LF-and-ASCII, so it is not blocking A5.

**2. The read path reaches 2.7 MB inside 128 MB.** Measured above: a
`compile.ps1 -Uefi` payload at `-HeapPages 32768` read a 2,766,116-byte
`SOURCE.SRC` off the volume and compiled it byte-identically to the host. So
the 66-to-84-bytes-per-source-byte figure does not extrapolate linearly to a
whole file, and nothing here is blocked on arena.

A cheaper construction exists if one is ever wanted: the
`unicode-bytes-to-text` builtin writes one buffer and measured **21 bytes per
char in situ** (65,536-char file written and read back on codex-vm, 1,373,480
bytes of arena), because it does not build a Text per character. It is NOT a
drop-in: it changes the mapping above 128, which is what finding 1 is about.
`raw-bytes-to-text` is cheaper still and is wrong for ASCII (byte 65 becomes
code point 65, not 41).

### The flight payload rebuilt, 2026-08-10 (`a5flight2.img`)

The 2026-08-09 flight wrote nothing because its payload was the depot seed,
which is plain `Exit` mode over raw IDE ports. **The payload for this arm
cannot be the seed**; the compiler has to be recompiled `-Uefi` so reads and
writes go through `EFI_BLOCK_IO_PROTOCOL`. Confirm the flag moved something
rather than trusting it: the same 2,768,194-byte source builds to 2,754,800
bytes plain and 2,739,216 `-Uefi`.

Bed-verified on a COPY at `-HeapPages 32768`, master untouched and still
virgin. `OUT.CDX` came off the volume at 84,660 bytes with magic `CDX1` and a
complete 166-cluster chain, **byte-identical (`ACF9823E...`) to the same
source compiled on the host by a plain-mode compiler built from that same
source** -- so the two arms differ only in exit mode. The artifact then RUNS
and prints `A5 338350`, which is `sum i*i` for 1..100 computed from the
arithmetic rather than captured from an earlier run.

The done-signal problem is closed as a side effect: `-Uefi` puts
`print-line-uni` on `__uefi_print`, so `SIZE:` and `DISK-OUT:` land on ConOut.
The UART took 8 bytes for the whole run. The operator now has a line to wait
for instead of a drive LED the ASUS does not have.

Recipe and the returned-stick checks are in `docs/HardwareSitting.md`.

### And the self-compile arm, same payload, bed-verified 2026-08-10

A second image (`a5bigflight.img`) carries the identical payload and swaps
`SOURCE.SRC` for the 2,768,194-byte concatenated compiler source. In the bed
it read that source off the volume, compiled it, and wrote `OUT.CDX` at
2,754,800 bytes in 4.7 minutes, **byte-identical (`CECC9D94...`) both to the
host control and to the plain-mode compiler binary that produced the
control** -- which is itself a fixed point of that source. So the guest
reproduced the compiler, not merely an artifact.

This is what makes the small arm worth flying anyway rather than being
subsumed: on separate boots the two answer different questions, and a
failure of the big one alone would be about scale rather than about the
sink. On ONE boot they would confound, which is why they are two sticks.

The arena question is settled by this run and not by extrapolation: 2.7 MB
of source, its AST and a 2.75 MB artifact coexist at `-HeapPages 32768`,
which is 128 MB exactly, the flying image's whole envelope (L-ARENA).

### FLOWN 2026-08-10 AND WROTE NOTHING AGAIN. The arm could not say why, and that was a defect in the arm.

Returned volume byte-identical to the master except LBA 0 and 1, the two
`-SpecFit` rewrites. Screen held the stub's green.

`-Uefi` was necessary and still not sufficient, but the useful finding is
about the instrument rather than the sink. **The arm's only success signal was
`DISK-OUT:` over ConOut, and `__uefi_print` had never rendered a character on
that board** (L-UNCALLED: it is in every binary we ship and was called by
nothing on metal). A payload that died early and a payload that ran perfectly
into a mute console produce the same screen and the same volume, so the flight
eliminated nothing. Two flights were spent this way.

Nothing further should be inferred about `LocateProtocol`, the BPB or the
write helper from this flight. It did not get far enough to say, and it could
not have told us if it had.

`apps/works/BlockLadderProbe.codex` is the replacement and it reports as a
screen colour, the one channel demonstrated to reach a human on that box.
Every rung is forced in the bed by `build/ladder-arm.ps1`. Its result decides
what this design does next, and until it flies the cause of both failures is
**unmeasured**.

### The ladder's own first flight, 2026-08-10: GREEN, and it repeated the arm's mistake

`837F79FA...` flew and painted nothing; LBA 30000 came back zeroed. The cause
was in the instrument, not the board. Its `probe-step` printed to ConOut before
it painted, so the first thing it did on metal was a firmware call, and the
colour channel it existed to provide could not run until that call returned.
It reproduced the defect it was built to escape, one level down.

That matters for this design because **the kernel installs its own CR3 even in
UEFI mode** (`X86_64Boot.codex:120-169`: the SystemTable pointer has to be
snapshotted before the firmware's tables are destroyed), and the map it installs
covers 0-4 GB and nothing above. Every firmware call this sink makes -- ConOut,
`LocateProtocol`, `ReadBlocks`, `WriteBlocks` -- is a call into memory the guest
has remapped underneath the firmware. A firmware call that never returns is
consistent with every observation on that board so far and is NOT yet
distinguished from the alternatives. The reordered ladder distinguishes them:
if it stops at CYAN, the first firmware call is fatal and that is the whole
finding.

### FLOWN AGAIN, 2026-08-10, AND IT IS WHITE. The write path is good on that board.

The reordered ladder ran every rung and the write was confirmed on the medium:
LBA 30000 came back holding the boot-sector copy with byte 0 replaced by `0xA5`
and the `55AA` signature intact, against `EB` at LBA 2048. Both A5 flights had
left that sector zeroed.

The CR3 paragraph above is therefore refuted by measurement and is kept only as
the record of a hypothesis that did not hold: firmware calls DO survive the
guest's own page tables on this board.

**The retroactive reading of the two silent A5 flights is that they died in
their own first ConOut call**, which is the defect the ladder had too and which
is now fixed in the ladder. That is an inference from the ladder's flights, not
a measurement of the A5 arms: nothing was instrumented inside those payloads.
The way to settle it is to rebuild the A5 flight arm with the ladder's ordering
-- paint first, never let a report wait on a firmware call -- and force its
failures in the bed before it flies. Until that runs, the sink's own 2.7 MB
write on metal remains untested; only the one-sector primitive underneath it is
proven.

The rebuild also surfaced two defects that had been silently inflating results,
both recorded in `docs/HardwareSitting.md`: the read rung tested the buffer
pointer rather than the sector, and `build-option-a.ps1` had no way to pass
`compile.ps1 -Uefi`, so it produced payloads that boot and paint but whose block
reads return zeros.

### The sink's own arm, built and calibrated 2026-08-10 (`sinkladder.img`)

`apps/works/SinkLadderProbe.codex` is the instrument for the question the block
ladder cannot answer: `fat16-write-segments` at 2.7 MB on metal. It drives the
same write and the same streaming oracle as `codex/test/apps/fat-sink-big.codex`
and reports as a screen colour, paint before print on every rung.

**This is why it is a probe and not the compiler.** The compiler cannot cite
`Works chapter MetalLadder`: the quire order is `codex.foreword -> codex ->
codex.os -> apps` (`DevelopersRulebook.md:236`), so a painted rung inside
`emit-from-disk` needs new foreword-level GOP code, which is the cost this
design already recorded as "strictly more work, not on the critical path". A
probe in the apps quire asks the sink question today, at no seed risk, and it
is also the cheaper test to run first: if the 2.7 MB write fails on metal, a
rebuilt compiler arm was wasted work.

Bed result, all six rungs green: `size=2745998 want=2745998 first=5903
chain=5364 wantchain=5364 arena=84840 bad=0`. The chain is `ceil(2745998/512)`
and is DEMANDED by the write rung rather than merely printed, because a writer
that returns True having linked nothing leaves a first cluster below 2, which
the walk answers 0 for and is indistinguishable from a clean verify.

**Every rung below WHITE is forced in the bed** by `build/sink-arm.ps1`
(`pass`/`shift`/`nodisk`/`badbpb`/`small`); the table is in
`docs/HardwareSitting.md`. The `shift` arm is the positive control and it fires:
a payload rebuilt with `sl-shift = 1` writes identically -- same size, same
chain, same 84,840 bytes of arena -- and reports the verify bad, stopping at
BLUE. So a WHITE screen is a measurement rather than an instrument that cannot
fail (L-FALSIF).

**What this still does not answer.** It proves the SINK on metal, not the
compiler using it. Fly the sink ladder first -- it is strictly smaller, and a
red there would mean the compiler arm cannot work either.

### The compiler's own arm, built and calibrated 2026-08-11 (`build/disk-arm.ps1`)

**The remaining half is closed, and it cost less than this design said it
would.** The paragraph above priced a painted rung inside `emit-from-disk` at
"new foreword-level GOP code" because the compiler cannot cite
`Works chapter MetalLadder`. It does not need the foreword. `peek-qword`,
`peek-32` and `poke-32` are BUILTINS (`Types/Builtins.codex:112-114`), so the
whole channel is arithmetic with no library behind it, and a chapter in
`codex/compiler/` is answered by presence in the unit rather than through the
quire registry (`quire-map.ps1`, `Get-PresentChapterNames`). It landed as
`codex/compiler/Core/BootPaint.codex`, 130 lines, cited as
`cites Codex chapter BootPaint`. No foreword change, no new quire.

Six rungs on the DISK path: CYAN `opening` entered, YELLOW stdin said `DISK`,
MAGENTA the boot volume's BPB is 512, ORANGE the source read off the volume,
BLUE the compile finished, WHITE `OUT.CDX` is on the volume at that size. The
operator table and the forced-arm table are in `docs/HardwareSitting.md`.

Three things this arm had to get right that the design did not anticipate.

**WHITE re-reads the directory. It does not take the writer's answer.**
Measured 2026-08-11 with the medium read-only: codex-vm printed
`WARN: cannot reopen disk ... for write` 833 times, `fat16-write-segments`
still answered True, and a rung trusting `ok` painted WHITE over a host file
that had not changed. `emit-disk-verdict` now re-mounts, resolves `OUT.CDX`
and compares `de-size` against the size the compile produced, which is the
same discipline `SinkLadderProbe` needed for the same reason (L-FALSIF).
`DISK-OUT: FAILED` gained the two numbers, so the line says what was short.

**Every hold is guarded on the handoff block being present, and the trace is
too.** The compiler runs on hosts and in the bed as well as on the board, so an
unguarded spin would turn a compile error into a hang and an unguarded print
would put six lines into every compile's stdout. A run with no handoff block
paints nothing, holds nothing and says nothing.

**`mode` is not passed one call deeper to make room for a rung.** The
corruption recorded above is undiagnosed, so `emit-from-disk` keeps its own
frame: the source rung paints through a `let` in the existing chain and does
not trace, and the rung after it names the state instead.

**The bed's screen is not the bed's channel.** codex-vm's `-screenshot` came
back black on runs where the payload reported `painted fb=1` and named
`base=0xBF000000`, which is the address codex-vm itself screenshots. So all
three ladders calibrate from the printed rung lines, deliberately, and the
colours remain verified only on metal. `OperatorsManual.md`, "A PAINTED LADDER
DOES NOT SHOW UP IN -screenshot".

## The one thing this must not do: go through `List Integer`

`fat16-write-binary-file` takes `List Integer`, and `list-at` scales the index
by 8 (`X86_64Builtins.codex:120`, `shl-ri 3`). A List is array-backed with one
64-bit slot per element, so the CDX as a List is **~21.8 MB live**, and
`list-push` growth doubles, so the peak while building it is roughly **44 MB**
with the old array still reachable.

The flying boot image runs heap and stack in one 128 MB region (L-ARENA,
`OperatorsManual.md` "A BOOT IMAGE RUNS IN 128 MB"), on top of the compiler's
own peak working set for a selfhost. This is CLAUDE.md rule 8's 8x blowup
exactly, at the worst possible moment in the run.

It is also unnecessary. `result.content-buf` is already a raw buf
(`X86_64Chapter.codex:971`, the workspace code-buffer) with `content-len`
beside it. Only `header-bytes` and `tail-bytes` are Lists, and both are small.

## Shape of the change

**Fat16.codex -- single-home the chain on a buf, do not fork it.**
`fat16-fill-sector` (`:268`) is the only place the source is read, one byte at
a time via `list-at`. Retarget the chain at a source described by segments
rather than by a List:

- `fat16-fill-sector-seg` reads byte *n* with `peek-byte` from whichever of
  (header list, content buf, tail list) the absolute offset lands in.
- `fat16-write-data-sector` / `-cluster` / `-chain` / `fat16-extend-chain` /
  `fat16-create-file` / `-at-slot` / `-with-cluster` / `-or-replace` /
  `-replace-at-slot` carry the segment triple and a total length instead of
  `List Integer` + `list-length`.
- `fat16-write-binary-file` and `fat16-write-file` stay as they are from the
  caller's side, wrapping their List as a single segment. **No existing caller
  changes and no second chain is added** -- this deletes the per-byte
  `list-length` in the inner loop rather than duplicating it.
- New entry point: `fat16-write-cdx : Text, List Integer, Integer, Integer,
  List Integer -> [Device.Block] Boolean` (path, header, content buf, content
  len, tail).

**Fat16.codex -- the two allocation defects, which the measurement above makes
load-bearing rather than tidy-up.**

- **Carry an allocation cursor.** `fat16-find-free-cluster` restarts at
  cluster 2 (`:197-198`). Thread the last-claimed cluster through the chain
  walk and resume from it; a wrap-to-2 retry keeps it correct on a fragmented
  volume. This is ~50 of the measured 55 sector buffers per cluster.
- **Reuse the sector buffer.** Every `block-read-sector` bump-allocates 512
  bytes that are never returned (`X86_64Helpers.codex:3374`). The free scan
  reads a FAT sector, inspects it, and never needs it again; the same holds
  for the data-sector RMW. `__heap-save` / `__heap-restore` around each
  scan step resets the pointer at no cost, which is the idiom the compiler
  already uses between phases. That is the remaining ~5.

**opening.codex -- the sink.** `emit-binary-tail` and
`emit-binary-tail-with-map` gain a disk arm. The effect rows widen: `emit-cdx`,
both tails, `compile-plain` and `emit-from-disk` are `[Console, FileSystem]`
today and need `Device.Block`, which `dispatch-on-mode` (`:1684`) already
grounds.

`fat16-scope-admits` is not an obstacle: an empty grant admits everything
(`Fat16.codex:1003-1007`).

## Then, and only then, a verdict

Once the artifact lands, one line of confirmation is still wanted, because a
board that wrote nothing and a board that faulted look identical. The cheap
version costs no GOP: after the write, `emit-from-disk` writes a second, tiny
file (`OUT.TXT`: the size, the byte count written, ok or the failure) through
the same path it just proved. A painted verdict is strictly better for a human
at the bench and strictly more work; it is not on this item's critical path.

## Hazard carried forward from CL 14210

The stub allocates its 128 MB region against a 3 GB ceiling, and that region
spans `0xBE000000` and `0xBF000000` -- codex-vm's GPU command buffer and its
GOP framebuffer -- so the stack sits inside the framebuffer. It did not cause
blocker 1's fault (lowering the ceiling below both changed nothing), and it does
not touch the volume path. **It becomes live the moment anything here paints.**

## The option not taken: let the firmware write it

`__uefi_read_file` (`X86_64Helpers.codex:2078-2195`) is ~90 per cent of a
`__uefi_write_file`: same LocateProtocol / OpenVolume / Open, change OpenMode
from `1` (`:2139`) to CREATE|READ|WRITE and call the vtable's Write at `+0x28`
instead of Read at `+0x20`. It is **O(1) heap**, which is exactly the property
the measurement above says we lack. Three things say no:

1. It puts the root of trust's own output through firmware we did not build,
   and requires boot services still live at emit time.
2. It has zero mileage. `emit-uefi-read-file-helper` appears nowhere in the
   tree but its own definition, so the `uefi-read-file` builtin
   (`Types/Builtins.codex:180`) emits a call to a symbol that is never
   recorded. That is a latent unresolved-helper bug and it means this path has
   never run.
3. It cannot be gated here. `tools/codex-vm.c:220-225` traps SFS OpenVolume
   and File Open/Close/Read/GetInfo/SetPos and has **no FILE_WRITE**. Gating
   needs OVMF or a new trap.

Worth taking cheaply and separately: `__uefi_print` is already in the shipped
seed and already null-checks the systab (`X86_64Helpers.codex:1994-2000`), and
`print-line-uni` already dispatches on exit mode
(`X86_64Builtins.codex:709-719`). A diagnostics channel on the board is a
dispatch change, not new code.

## Settled

Output lands at `OUT.CDX` on the boot volume. No painted verdict: the
file-based one costs nothing extra and rides the path it just proved.

**This item grew.** It is no longer "add a sink"; it is a sink plus two
allocation fixes in the foreword FAT writer, because the writer has never
carried a payload this size.

## Landing

Both this and the blocker 2 fix are seed-affecting compiler changes. **They
should take one build-token window between them** rather than two: a seed
rebuild is three gate passes, roughly 12 minutes each way.
