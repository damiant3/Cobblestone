# The Bed That Always Said Yes

*reek, 2026-08-14. The A5 campaign: boot the compiler from a USB stick on
bare UEFI firmware, compile its own source off the stick, write OUT.CDX
back, stop. Shipped 2026-08-14 with the board's OUT.CDX byte-identical to
the host control (the green flight's account and artifacts are the
2026-08-14 entry in HardwareSitting; the returned stick is
`a5flight-returned-20260814.img` in the archive). Before that, by
Damian's count: about twenty misfire flashes across four days, about
3 million tokens spent by one lane and another half million by its
relief, and a human back paying for every sitting.*

## What actually went wrong

The campaign's early killers were self-inflicted flags, and the run sheet
records them: `-EntryStart` spins forever under live firmware (three dead
images carried it), and a payload compiled without `-Uefi` gets the
bare-metal I/O helpers and mounts zeroes. Those cost flights too, and
they are ordinary mistakes with ordinary morals.

The three defects that consumed the rest of the campaign were one shape,
and the shape is the lesson: a case where the UEFI spec leaves a choice
open, the bed's firmware makes the friendly choice, and the ASUS's AMI
firmware makes the hostile one. Not one of them was a bug the bed could
have caught as configured.

**1. Handle order.** The block helpers bound their device with
`LocateProtocol`, which returns the FIRST Block I/O handle in the
firmware's database, order unspecified by the spec. Every bed has one
disk, so the first handle is always the stick. The board presents raw
disks and per-partition handles in firmware order, and a foreign volume
mounted clean and held no SOURCE.SRC. The wrong-volume MAGENTA stalls
before the binding fix were this.

**2. Allocation placement, low.** The stub asked `AllocateMaxAddress`
under a 3 GB ceiling and never validated the returned base. Bed firmware
granted it; on the board the in/out cell came back holding its seed
value, so the heap "base" was 0xC0000000 -- this board's framebuffer.
Records read back as 0x00FF00FF00FF00FF, two magenta pixels (so this
defect painted MAGENTA too -- the a5fix return in the archive is this
one, not defect 1), and the repaints bulldozed the heap on every frame.
Whether AMI REFUSED the request or answered success without writing the
cell is undistinguished to this day; the stub comment above the
allocation says so, both mechanisms are now refused with distinct panic
letters ('B' and 'V'), and a future flight that reads the letter settles
it. The narrative convenience of "the board said no" is not a
measurement, and this story declines to claim it.

**3. Allocation placement, high.** The fix for 2 switched to
`AllocateAnyPages`. OVMF satisfies that below 4 GB at every RAM size we
could configure; AMI satisfies it from the top of a 32 GB board. Several
compiler types declared heap positions as `Integer between 0 and
4294967295`, bounded signatures are enforced with a UD2 trap (bounded
signatures stage B, X86_64.codex), and the first `pitch` of the compile
trapped into firmware's invisible exception handler. That was the
seven-hour ORANGE: not slow, halted, with the last painted screen still
up.

## The pattern, named

The bed was not less faithful than the board. It was NICER than the
board, at every point where the spec allowed it to be, and its silence at
those points read as agreement (L-GAP). L-ARENA already names the
resource-envelope version of this and L-OPTIONAL the capability version.
The A5 campaign adds the sharpest form: **the spec's unspecified choices
are exactly where a bed and a board diverge, and a green bed says nothing
about any of them.**

The freedoms that mattered here were enumerable in advance. Handle
enumeration order. Allocation placement. Whether an allocation request is
granted at all. What uninitialized memory holds (cell 36320, the
guard-page base, was written only by `emit-start`, which a UEFI tenant
never runs -- the stub zeroes it since the fix; QEMU zeroes RAM and
boards do not). Each one is a line in the UEFI spec with the words "no
ordering is guaranteed" or "the firmware may" next to it.

Two of the three, once identified, took MINUTES to express in the bed.
`ImageHandle=2` at the bed's PE entry (codex-vm), because real firmware
never passes 0 and 0 silently kept the bed on the fallback path. And the
stub built with `cdx-to-pe.ps1 -HeapAt 0x140000000` under
`test-ovmf.ps1 -MemMB 8192`, which forces the heap to 5 GB by
`AllocateAddress` -- that arm reproduced the board's seven-hour hang as a
`#UD at pitch+0x46` on the first run, and verified the fix the same
afternoon, no sitting spent.

The third is the caution that keeps this lesson from being smug. The
handle-order fix got a decoy-disk arm (`test-ovmf.ps1 -Decoy`), and the
run sheet's own entry calls that arm's green VACUOUS: OVMF happened to
order the stick's raw disk first, so the wrong-disk topology never
reached the fix, and the binding fix is proven by metal alone to this
day. An arm is not an expression of the hostile choice until it has been
SHOWN to make the hostile choice -- a green arm that cannot reach the fix
is the bed saying yes again, one level up (L-ORACLE's shape). The whole
campaign's cost is the gap between when we started flying and when we
started building bed arms that could say no -- and the decoy arm shows
that building the arm is not enough; you must also catch it saying no at
least once.

## The second lesson, which cost nothing only by luck

The green flight nearly did not fly. The final image was built at
build-img's 8 MB default; PE (2.6 MB) + SOURCE.SRC (2.8 MB) + OUT.CDX
(2.8 MB) do not fit in its 6 MB ESP. The compile succeeds and the
write-back dies `DISK-OUT: FAILED -1` -- on the board that would have
been one more dead stick and one more sitting.

It was caught because the exact image bytes were run through the FULL
loop in the bed first -- boot, read, compile, write, extract, byte-compare
-- as a belt-and-suspenders step while waiting for the human. The recipe,
by name, because a procedure nobody can invoke is prose: boot the image
with `build/boot/test-ovmf.ps1 -UsbDisk` (the guest writes to the
harness's COPY of the image, `ovmf-disk-<tag>.img` in `%TEMP%`, never to
your file); extract with `build/read-stick.ps1 -ImageFile` pointed at
that copy; byte-compare against a host control compiled from the same
source. The BUILD NAY footer (added that same day at Damian's direction)
named the failure in one line. Rebuilt at 16 MB, the bed loop went YAY
with the extracted OUT.CDX byte-identical to the host control, and the
board then reproduced the bed's result exactly.

L-ARTIFACT says gate the artifact you ship, not a sibling. The addition:
gate the artifact's WHOLE MISSION, not its boot. An image that boots,
reads and compiles can still fail its reason for existing at the last
write, and the bed rehearsal of the full loop is free while a flash is
the most expensive line in the plan (L-HUMAN, L-BODY).

## An honest accounting of the relief lane's own morning

The relief session lost 90 minutes to a rebuilt payload failing
`volume ok=0` in the bed, and blamed in sequence the kernel, its own
type-widening edits, and its own stub edit. All three were innocent. The
variable was a build option: flight payloads must be compiled
`compile.ps1 -Uefi`, which selects the firmware-tenant I/O helpers, and
the rebuilds had omitted it. What settled it was not more hypotheses but
byte-comparing rebuilds against the known-good artifact until one
reproduced it exactly -- L-SAMEVER, paid for again: prove two artifacts
are versions of the same thing before instrumenting the difference. The
durable operational facts (the -Uefi requirement, the 16 MB image floor,
kernel interchangeability under the fixed point) are in
HardwareSitting's 2026-08-14 entry, where a stick-holder will actually
read them.

## What should change

**In `docs/Hardware/HardwareSitting.md` (RULED, Damian 2026-08-14):**
one line in the QUICKREF, which is the only text everyone provably
reads while holding a stick: *no flash without a same-bytes full-loop
bed rehearsal, and a board defect is not closed until a bed arm
expresses it.* That placement follows L-INTERRUPT -- a warning at the
moment of relevance beats a standing obligation in a document nobody
re-reads. The line is in the QUICKREF now.

**In `CLAUDE.md`: nothing, by the same ruling.** The LESSONS preamble's
argument applies to this story too: CLAUDE.md is a test suite with no
runner, and a fourth paragraph about hardware would rot like the first
three. The lesson ids plus the QUICKREF line are the enforceable form.

**Runner candidates:** L-REHEARSE is mechanically checkable -- flash-usb
could refuse an image whose hash has no bed-loop marker, the way
test-ovmf refuses a held port (L-SHARED's runner). Not built; recorded
here so the row's last column can stop saying "none" some day. The
5 GB heap arm, which began as a throwaway scratchpad patch, is now the
depot's `cdx-to-pe.ps1 -HeapAt` so the next lane does not have to
re-derive it; it is an expressible arm, not an enforcing check, so the
L-FREEDOM runner column honestly stays "none".

## The lessons

- **L-FREEDOM**: the spec's unspecified choices are where the board and
  the bed diverge. Enumerate the freedoms the code leans on -- handle
  order, allocation placement, grant/refusal, uninitialized memory --
  and build the bed arm that makes the hostile choice BEFORE flying.
- **L-REHEARSE**: fly nothing that has not completed its full mission in
  the bed as the exact bytes being flashed. Boot-and-read green is not
  mission green; the failure modes live at the last write.
