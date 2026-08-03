# The Keyboard Was Never Silent

> **IT WORKS. 2026-08-02, on the ASUS TUF.** Damian booted the stick and the
> probe turned green on a keypress -- the first keystroke into Codex on that
> machine. Main 12609.
>
> **And the fix that passed in the emulator was the WRONG fix.** The sign-bit
> correction below is real and necessary, and on its own it was not enough:
> `uefi-read-key-ex` located `EFI_SIMPLE_TEXT_INPUT_EX`, which is **UEFI 2.x and
> OPTIONAL**. OVMF implements it, so the two-arm proof went green on the dev box.
> The board's AMI Aptio V (2015) does not publish it, so the same build returned
> `-1` forever there. It now reads **ConIn at SystemTable+48**
> (`EFI_SIMPLE_TEXT_INPUT_PROTOCOL`, UEFI 1.x, mandatory everywhere);
> `ReadKeyStroke` is at the same vtable offset 8 and writes the same first four
> bytes. The shift-state fold is deleted outright rather than masked, so the
> sign collision is now impossible by construction.
>
> **A green emulator arm is not a proof about hardware when the thing under test
> is an OPTIONAL part of a specification.** That is the lesson this ending adds,
> and it is the one the campaign kept re-learning in other forms: an instrument
> that cannot express the failure reports success. Here the instrument could
> express it perfectly and was simply a more capable machine than the target.
>
> **The board narrowed it in one boot, and only because the probe had
> distinguishable failure states.** `KeyProof` answers in whole-screen colour,
> six of them. The metal run came back RED: not yellow, so the SystemTable was
> live; not magenta, so `uefi-clear-screen` had SUCCEEDED and firmware calls
> worked on that machine. Three classes eliminated in a single reading, leaving
> one failing call -- and "works in the emulator, absent on the metal" plus
> "optional in the spec" is a very short list. **A probe that had answered
> pass/fail would have said only "still broken".**

*red, 2026-08-02. The conclusion of `TheSilentKeyboard.md`, which should be read
first: it is the account of the campaign this one ends.*

## The finding, for anyone who does not want the story

`uefi-read-key-ex` packs its answer as

    ScanCode << 16 | UnicodeChar | KeyShiftState << 32

`KeyShiftState` carries `EFI_SHIFT_STATE_VALID`, which is `0x80000000` -- bit 31.
Shifted left by 32 it lands on **bit 63**. So **a successful key read returns a
negative number**, and every caller that tests `< 0` for failure reads a real
keystroke as an error.

The function worked the entire time.

**Scope of that claim, corrected 2026-08-02 after this document was tested.** An
agent given only the symptom, and not this file, found the sign bit from the
emitted bytes -- and then flagged that the sentence which used to stand here,
"nothing was wrong with the keyboard, the xHCI controller, the firmware, or the
board", claims more than was measured. What is proven is narrower and worth
stating exactly:

- the path every app used **could not** deliver a key on this board, and
- the one path that calls firmware **could not report success** to any caller.

Whether the xHCI driver has defects of its own underneath that is **unproven and
still open**. `InputSource.codex:7` records that cell 28680 can also be fed by a
USB HID pump, so a design that keeps the PS/2-cell architecture brings every
xHCI question straight back. A story that closes a two-month campaign is exactly
where an overclaim is most likely and least likely to be challenged, and this
one had one in its second sentence.

## Why that took two months to find

Three things had to be true at once, and each one alone was survivable.

**One. The input path everything used never asks the firmware anything.**
`uefi-read-key` is four instructions (`emit-uefi-read-key-helper`,
`X86_64Helpers.codex`): load `key-buffer-addr`, `xchg [rdi], rax`,
`and rax, 0xFF`, `ret`. It is a bare read of the cell the IRQ1 handler fills
from **PS/2 port 0x60**. The target board has no PS/2 port. On that machine the
path cannot deliver a key however correct anything else is.

**Two. The path that does ask the firmware was called by nothing.**
`uefi-read-key-ex` is a real `LocateProtocol` +
`EFI_SIMPLE_TEXT_INPUT_EX_PROTOCOL` call. Grep the tree: every occurrence
outside the compiler's own source is generated build output. It was compiled
into every binary we ever shipped and never once invoked.

**Three. When it was finally invoked, success looked exactly like failure.**
The sign bit above.

`con-in` is read from the SystemTable at `UefiConsole.codex:36` and consumed by
nothing tree-wide -- the same shape as the `con-out` gap **twelve lines away in
the same record**, which was found the same morning by an agent who then did not
look at the adjacent line.

## The instrument gaps, which are the real subject

Every one of these reported success because it could not represent the failure.

| gap | consequence |
|---|---|
| `GOP_FB_SIZE` was `GOP_MAX_STRIDE * GOP_MAX_H * 4` with `GOP_MAX_H` = 768 | 6 MB buffer; the ASUS panel at 1920x1080 stride 2048 needs 8.4 MB, so codex-vm **access-violated host-side** before drawing a pixel. The bed built to model padded scanlines had never been run at the panel it was built for. Every probe ever "verified under OVMF" was necessarily verified at a geometry too small to show a stride fault |
| `LocateProtocol` never resolved the InputEx GUID | codex-vm has had a `ConInEx` block and a `ReadKeyStrokeEx` for a long time, unreachable. **The working path was untestable and the testable path could not work on the target** |
| QR codes drawn below the panel's visible area | the channel built to carry exact bytes has **never** reached metal. Every reading ever taken off that board came from text rows, while the run sheet instructed a human to photograph codes that were not on screen |
| Option A images never boot under codex-vm `-uefi` | break at `0x7032`, pre-existing. The legacy stub is testable ONLY under OVMF, and the new stub renders wrong on metal. **Each instrument covers exactly what the other does not** |

## Four hypotheses that were wrong, and how each died

Recorded because the refutations cost real money and should not be re-bought.

- **PSI / hub / TT.** Dead by topology: `rt=0 tt=0` means the keyboard is on a
  root port. The fix is real and addresses a topology this board does not have.
- **Relocation collision.** Refuted on metal. It was retrospectively refutable
  from records already held: `HardwareSitting.md:503` shows the original sitting
  at `intel-route=y`, `EPINT=0 SCANS=0`, with one controller up and no collision
  possible. A reproduction in emulation was allowed to stand in for checking
  that the premise applied to the target. **That cost a trip to the board.**
- **Cached MMIO.** The device page directory really did map the gigabyte above
  RAM identity write-back cacheable (`131` = `0x83`, PCD clear), and a cached
  MMIO mapping really is a mechanism by which a driver polls a register that
  cannot change. Fixed, gated, landed -- and it changed nothing on metal. A
  correct fix for a real defect that was not this defect.
- **NX on firmware pages.** `bare-metal-nx-boundary = 3` marks everything above
  6 MB no-execute, and OVMF's code sits at ~2.1 GB, so calling firmware looked
  certain to fault. Tested by building a compiler with RAM left executable:
  **no change.** Refuted in twenty minutes on the dev box at zero human cost.

## The control arm that ended it

`uefi-read-key-ex` returning a negative number is not attributable on its own.
It could have been InputEx specifically, or it could have been that **no** call
into firmware survives `emit-start` loading CR3 with our own page tables.

So the probe called `uefi-clear-screen` first -- same SystemTable, same ConOut
path, a firmware call whose success or failure is independent of keyboards -- and
painted magenta if it failed. It did not fail. Firmware calls work. That single
arm eliminated the entire structural class and forced the fault back onto the
one helper, where the sign bit was visible in the emitted bytes.

**Without it, "returns negative" would have been read as "firmware is
unreachable", which is a rewrite of the boot path rather than a one-line fix.**

## What the probe had to be

The instrument must not depend on the subsystem it reports on. The board's text
renderer was itself broken, so a probe that answers in glyphs answers in the one
language that panel could not speak. `KeyProof` answers in **whole-screen
colour**: blue waiting, cyan zero, red no-key, green key arrived, yellow the
SystemTable cell is zero, magenta firmware calls are dead. Six states, each
distinguishable across a room, none of them requiring a font.

Three times the probe's own logic hid the answer before it revealed it: a fuel
count too large to terminate inside the screenshot window, so every run
photographed "still polling", which is not an answer; a `-Keys "1e"` that
matched nothing in the harness's decimal map, so **no key was ever sent** and
the arm was a false negative; and a `k > 0` success test that the sign bit made
permanently false. **The instrument was wrong more often than the subject.**

## The lesson

**Check what SUCCESS looks like before concluding a call failed.** A success
encoding that collides with the failure encoding is a defect in the interface,
not in the caller, and it will be rediscovered by every caller forever until the
interface is fixed. Two months of hypotheses about a device were spent on a
function that had been returning the right answer in a form nobody could read.

And the older lesson underneath it, which this project has now paid for twice:
**a path that nothing calls is a path nothing has tested.** `uefi-read-key-ex`
was compiled into every binary we shipped, was never invoked, and was broken.
The same is true today of `con-in`, still read from the SystemTable and still
consumed by nothing.

`L-SUCCESS` and `L-UNCALLED` in `LESSONS.md`.

## The fix, and where it goes

Not landed as of this writing. Two changes, both compiler-side:

1. **`X86_64Helpers.codex`, the shift-state fold.** Stop putting a word whose
   bit 31 is `EFI_SHIFT_STATE_VALID` into the top half of a signed result.
   Either drop the shift state entirely (`ScanCode << 16 | UnicodeChar`, always
   non-negative) or mask the valid bit off before shifting. `-1` stays the
   failure value, and then it means only that.
2. **`codex/foreword/ui/KeyInput.codex:151`**, which is the console's only
   reader of the PS/2 cell: route input through the firmware path when the
   SystemTable is live, keeping the cell as the fallback for boards that have a
   PS/2 port. Plain `ConIn` `ReadKeyStroke` (SystemTable+48) is UEFI 1.x and
   universally present; no builtin exists for it, so that route is a second
   compiler change.

**How to falsify the whole account.** Rebuild `KeyProof` with the shift removed;
if it still reports red under `build/boot/test-ovmf.ps1` with a valid injected
key, the helper has a second fault and this story is incomplete. Land the fix,
boot the stick, and get nothing -- then there is a real delivery problem
underneath and the xHCI campaign was not wasted, only mistimed.

**Prove a key on the dev box before booking another sitting.** Two board trips
were spent on this in one day.
