# Current Plan -- Ship The Stick

**Re-cut 2026-08-03 by red, on Damian's direction, after the Update 37 release
and the keyboard closing.** The previous cut is deleted rather than struck
through; Perforce is the record (`p4 changes -m 100 //Codex/main/...`).
**If an item is not here, it is not in scope.**

## What changed, and it changes the whole shape of the plan

**1. The release shipped.** Update 37 is on both public mirrors at commit
`9b502e90`. Battery 1403/1359/0 fail/44 skip, app sweep 267 units 0
regressions, poison build 0 fail. Seed `9DCE330256566B2A`, 2,710,900 bytes,
byte-identical to `build/output/Sut.cdx`. **Nothing about the release is open.**

**2. The keyboard is closed on metal, both input paths.** The firmware path
(main 12609) and the USB HID path (main 12627, proven on the board
2026-08-03). Neither was the xHCI controller.

**3. Therefore the standing gate on everything is DISCHARGED.** Damian's
2026-07-30 ruling -- *"we aren't going to do any sitting until the keyboard
works. the whole I/O thing needs both I and O"* -- froze A2, A3, A4, A5, B3 and
sitting rung 3 at once. **All six are available now.** That is the single
largest change in this document and the reason it needed re-cutting: every
lane assignment in the previous version was routing around a wall that is gone.

## The objective, in Damian's words, unchanged

> Get this other box of mine up and running a codex.os that I can host
> services from and tell other people it basically works as an OS off a
> stick I can burn.

Not the public mirrors, not the founding vision, not perfection. **A stick
that boots his ASUS and is recognisably an operating system, with a network on
it.** Shipping list: boot stick, compiler, services, UI, keyboards, mice,
monitors, USB, 3D graphics, drive management, networking.

**"We aren't looking for perfection, we are looking for good enough to ship."**

## The machine

ASUS TUF, i7-6700K Skylake, 32 GB, Samsung 850 EVO (SATA, not NVMe), GTX 970,
AMI Aptio V 2015 firmware, UEFI with CSM off. Panel comes back **1920x1080
with stride 2048** -- it pads its scanlines, and every emulated bed in this
project has stride equal to width.

Two pieces of luck: the disk is SATA and AHCI is METAL, so storage needs no new
driver; and this box has already booted Codex.

## Where we are

Verdicts are `OsHardwareRoadmap`'s: METAL = proven on physical hardware or real
firmware, EMU = proven under codex-vm only, ABSENT = does not exist.

| Ship item | State |
|---|---|
| Compiler | self-hosting hard fixed point on bare metal |
| Boot stick | **METAL** |
| Monitor | GOP linear framebuffer, 32-bit XRGB -- **METAL**, but see the display row below |
| Keyboard | **METAL**, both the firmware path and USB HID |
| Drive management | AHCI read+write, IDE PIO, GPT + FAT16 read/write -- **METAL** |
| Mouse | USB HID, follows the keyboard's stack -- **EMU**, untried on the board |
| USB | xHCI host + BOT + SCSI -- **EMU** for storage; HID is METAL |
| UI | **EMU only, never rendered on metal.** Input no longer blocks it; the display defect below does |
| 3D graphics | software pipeline exists; GPU acceleration ABSENT and out of scope |
| **Network** | **ABSENT. The only write-from-nothing driver left** |

**The display on the ASUS is the one open metal defect.** Geometry reads
correct (1920/1080/2048, identical to a bed that renders it perfectly), so it
is not stride arithmetic. It cannot currently be reproduced because
`test-ovmf.ps1` runs QEMU `-vga std` at 1280x800 where stride equals width.
**The next step is a bed that can express it**, not another boot. The legacy
stub rendered correctly on that panel but cannot make firmware calls, so
legacy = display-no-keyboard and new = keyboard-no-display.

## RULED 2026-08-03: there is one desktop, and it is GopDesk

**Damian: "right hand didn't know left hand doings. we only need 1."**

Two desktops existed. They are not as independent as they looked, which is what
makes the call cheap:

| | `apps/guios/GuiShell.codex` | `apps/works/GopDesk.codex` |
|---|---|---|
| Runs | codex-vm `-gop`, EMU only | the Option A boot image, plus `build/desk.ps1` on the dev box |
| Neighbours | self-contained | `GopXhci`, `GopUsbKbd`, `GopUsbMsc`, `GopBoot`, `GopFat16` -- the metal stack |
| Content | 19 app views, documented as wireframes | compositor, Files pane over a real ESP, 3D scene in a content pane |

**`GopFont` already cites `Guios chapter FontLoad` and `Guios chapter
GuiDisplay`.** The metal desktop has been reusing the guios font pipeline
verbatim the whole time. The genuine duplication is the shell and the app
views, not the font work, and both stacks already share the UI foreword
(`Widget`, `Theme`, `Layout`).

**The ruling:**

- **`GopDesk` is the product.** It lives in the metal stack and now has working
  input beneath it.
- **`FontLoad`, `FontAi` and `GuiDisplay` stay** as the font library they
  already are. Nothing to port; `GopFont` cites them today.
- **`GuiShell`, `GuiTimer` and the app-view chapters retire** once anything
  worth keeping is lifted into GopDesk panes. They are wireframes by their own
  design doc, so the port is a rewrite either way and the views are not the
  valuable part.
- Do not start a merge campaign. Retire a chapter when a GopDesk pane replaces
  what it did, and delete it in that changelist.

## Track A -- THE STICK IS AN OS

**A2. The desktop renders on metal.** GOP is METAL, input is METAL, this is
bring-up rather than invention, and it has never been seen on real firmware.
The screenshot that makes the whole thing real.

**It is not close to a demo, and the "unblocked" above must not be read that
way.** The display defect stands between here and the glass, and the first
action is a bed that can express it, not a boot. **Nobody proposes a sitting
for A2 until A2a lands** (`docs/Agents/red-workplan.md`). Unblocked means the
input wall is gone, not that the row is nearly done.

**A3. Mouse on metal.** USB HID, same stack the keyboard now proves.

**A4. Storage on metal.** USB mass storage on the real xHCI. Sitting rung 3
(`msc-align.img`, the 64 KB TRB question) is the one item that still owes a
board trip, and it is now unblocked.

**A5. The compiler runs on the box.** Compile a Codex program on bare metal, on
the ASUS, from the stick. The most convincing thing in the demo and mostly
already true.

**A6. 3D on screen.** Software pipeline against the GOP framebuffer. **GPU
acceleration is out of scope for the ship.**

**A7. Clean shutdown. RULED LAST.** ACPI is ABSENT. It blocks nothing and
nobody picks it up ahead of another row.

## Track B -- THE NETWORK

Damian, 2026-08-03: **"we definitely need the nic, but it seems parallel to
guios looks good."** Track B runs continuously and does not compete with Track
A for the board.

**B2. The Intel I219-V driver.** The part is identified (`00:1f.6`,
`8086:15b8`), the MAC reads live off RAL/RAH, `MAP=ok`, and the emulated model
now has the MDIC/PHY path it was missing. Absent on the real part: descriptor
rings, link bring-up, RX and TX. **The longest single item on the ship.**

**B3. The stack over the real NIC.** TCP/IP exists and runs over NE2K under
codex-vm. Re-point it and prove a handshake against another machine on the LAN.

**B4. Deploy a real service and serve the repository protocol.** The point of
the track: serve it off the box and have something else talk to it.

## Track C -- THE TRUST AUDIT (new, ruled 2026-08-03)

Damian: **"i think the dcc and rechecker can come as a package, that is fine."**
One campaign, two instruments, because they audit two different sentences.

**C1. Diverse double-compiling: is the seed HONEST.** The fixed point proves
the seed is a stable fixed point of itself, and a Thompson trojan is a
perfectly stable fixed point too. `docs/OperatorsManual.md` has the theory and
the trap already: **emitting to RISC-V and back buys nothing**, because that
binary is `A(sA, target=riscv)` and the seed had its chance to inject on the
way out. Only a different implementation of the source-to-binary function
counts, and the only live candidate is the C# plug through Roslyn.

Three of six steps exist. `codex/plugs/csharp/emit-compiler.ps1` already runs
concat, seed-to-IR, and IR-to-`Codex.cs`. **Nothing in `build/` invokes any of
it, and its stated success criterion -- that the C# compiles -- is not the DDC
criterion.**

| Stage | Deliverable |
|---|---|
| C1.0 | Re-run the existing pipeline; confirm `Codex.cs` still emits |
| C1.1 | `dotnet build` inside the harness rather than by hand |
| C1.2 | **Run the Roslyn-built compiler against `Codex.codex` and diff its CDX against `seed/Codex.cdx`.** The first stage that proves anything |
| C1.3 | Make the transpiled compiler do its own source-to-IR step, so the seed is responsible only for generating C# text |
| C1.4 | Publish the residual hole; grep the generated C# across re-emissions |

**The residual hole is stated in the manual and must not be softened in any
claim document.** The seed sits upstream in two places, not one: `compile.ps1`
is the seed, and `csharp-plug.cdx` is itself a Codex program the seed built.
C1.3 closes one of the two. The full claim needs a `B` the seed never touched,
and the only such witness is the retired `old/` compiler, frozen at an April
language version and able to check only a seed nobody ships. **What the narrow
version still buys: a payload forced through 300 KB of readable generated C#
has to survive as text that can be grepped, and Thompson's attack works because
binaries are unreadable.**

**C2. The independent rechecker: is the TYPE CHECKER right.** Design is
`docs/Designs/Active/Tools/IndependentRechecker.md` and is complete; nothing is
built. Stages 1-3 (well-formedness, bounded-integer re-derivation, effect-row
and linear re-derivation) need **no compiler change and no seed**. Stage 4
(proof terms) is the only seed-affecting part and can be deferred indefinitely.

**Binding constraints, from that design, and they are what make it worth
anything:** it may reuse the foreword but may **not** call, cite or copy
anything under `codex/compiler/Types/`; it should be written by an agent who
did not write the checker, from the DevelopersGuide rather than from
`TypeChecker.codex`; **UNSUPPORTED is a first-class answer and must never be
counted as AGREE**; and **the deliverable is the mutation kill-rate, not the
rechecker.** A rechecker that agrees with the compiler on every input in the
tree is indistinguishable from one that returns AGREE unconditionally.

**Neither C1 nor C2 gates anything.** They do not join `build/build.ps1`, and a
DISAGREE is a bug report against one of two implementations, unresolved until a
human reads it.

## The lanes, assigned 2026-08-03

| Lane | Assignment | Needs the box? |
|---|---|---|
| **red** | **A2: the desktop on metal.** First the bed that can express the 1920x1080 padded-stride display defect, then GopDesk on the board. Owns the stick and the sitting sequence | yes, later |
| **reek** | **A4: USB mass storage on the real xHCI**, then sitting rung 3. `GopXhci`/`GopUsb*` are reek's and the HID half is already proven | dev box, then one boot |
| **blu** | **B2: the Intel I219-V driver.** Rings, link bring-up, RX, TX. `codex/os/kernel/E1000e.codex` and `codex/os/net/**` are blu's | no |
| **fester** | **C1: diverse double-compiling**, stages 0 through 3. Box-heavy, unattended, independent of every other lane | box only |
| **val** | **C2: the independent rechecker**, stages 1 through 3. Must be written from the language docs, not from `TypeChecker.codex` | no |

**Two standing constraints on this cut.** val and fester are on Track C
deliberately: neither wrote the type checker or the C# plug, which is a
correctness requirement for C2 and merely convenient for C1. And **the desktop
retirement (GuiShell) belongs to whoever is standing in GopDesk**, which is
red -- it is not a campaign anybody schedules separately.

## FILE CLAIMS. Announce here BEFORE you open one.

Four items were built twice on 2026-07-30 by four different pairs of lanes, and
every one was caught by luck rather than process.

| File | Claimed by |
|---|---|
| `tools/codex-vm.c` | FREE -- one owner at a time, announce in your workplan |
| `apps/works/GopDesk.codex`, `DeskVm.codex`, `apps/guios/**` | red |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu |
| `codex/plugs/csharp/**`, `build/` DDC harness | fester |
| the rechecker plug (new dir under `codex/plugs/`) | val |
| `docs/HardwareSitting.md`, the low-memory cell map | red |

**The rule:** before you open a file another lane could plausibly want, add a
row and say so in your workplan. Set it back to FREE when you land.
**And check the other agent's STREAM, not their workplan** -- `p4 changes -m 5
//Codex/<agent>/...`. A lane assignment is not a statement about what the
assigner has already built.

## Discovered work, NOT in the ship

None of it gates the stick. Take from here only when a ship item is blocked.

- Two Brotli tests that use a foreign decoder have no runner in any gate or
  battery, need .NET on the host, and are the only two instruments that can
  catch the failure that quire was once deleted for. Last run 2026-07-26.
- 26 tests carry a `.skip`, last triaged 2026-07-27; of four probed by hand,
  three reasons were stale and one hid a fixed miscompile.
- `lint-unused-cites.ps1:58` tests `$body.Contains($name)` against the whole
  file, so a prose mention marks a cite used. Same class as the checker a prose
  line held green for thirteen days. Wired into no harness, so cheap and safe.
- `audit-skips.ps1` globs `*.skip` only, so `.slow` and `.fatal` have never
  been audited despite its header claiming they are.
- `__watchdog_tier1`/`tier2` are unreachable: both test `== 5,500,001` while
  the panic tests `>= 5,500,000` and the counter steps by one. The default
  watchdog cannot fire either (~3.5 days). **Damian owes a value, pre-ship.**
- Cross-lane parity: figures date from June, roughly 90 RISC-V rows fail, and
  re-measuring needs one Renode run. Until then the numbers are not quotable.
- Prose removal (rule 12): 54,912 lines across 2,640 chapters. blu's campaign,
  per-block judgement, no regex sweep. Delete it in files you already touch.
- The store cutover: the repository protocol's store layer works and nothing
  has been migrated onto it. Peer resolution and multi-disk are unowned.
- `encode/Gltf` writes an accessor bounding box 1000x too large; the fix needs
  a JSON decimal `Json.codex` does not have, and a new `JsonNum` variant would
  be absorbed silently by eleven `is otherwise` arms.
- `AI chapter SafeTensors` recognises I32 and I8 and can load neither.
- `<` `>` `<=` `>=` do not order Text. A language decision, not a bug fix.
- Pure-Codex VMX host, retiring `codex-vm.exe`. Designed, unowned.
- Held back from the public push and awaiting a ruling: the three third-party
  specification PDFs in `docs/Reference/` (redistribution), and the
  `codex/test/fixtures/https/` private-key fixtures (push protection).

## Rulings owed by Damian

- **The watchdog threshold**, pre-ship. It cannot currently fire.
- **The two items held back from the mirror push**, above.

## What a green gate still does not mean

The gate is `build/build.ps1`: text round-trip, CDX hard fixed point, BVT.
**It has never executed a single instruction on Damian's ASUS.** Every EMU
verdict above is green under codex-vm and unproven on the silicon we ship to.

## Cross-references

- `docs/HardwareSitting.md` -- the run sheet, governs any sitting
- `docs/Designs/Active/Tools/HardwareBringUpPlaybook.md` -- the method, generalised
- `docs/Designs/Active/Tools/IndependentRechecker.md` -- C2's design
- `docs/OperatorsManual.md`, "Diverse Double-Compiling" -- C1's theory and its hole
- `docs/Designs/Active/OS/GuiOsBringup.md` -- the desktop, now carrying the ruling
- `docs/PM/Active/Stories/LESSONS.md` -- the lesson index; read a story when its id goes load-bearing
