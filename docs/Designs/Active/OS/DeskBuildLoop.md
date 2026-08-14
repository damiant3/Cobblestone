# The Desk Build Loop

**Goal.** Edit a compiler chapter in the desk, save it, and compile it on the
machine from the desk console, with the answer on the glass. The end state is
a stick that carries its own source, its own compiler, and the proof the two
agree, where a person can change the source and watch the proof move.

**Status 2026-08-13 (updated after the flight): the edit half is done, the
blocker is GONE, and Road A is the road.** VT-x is available on the ASUS,
measured on metal. The road that looked cheapest (Road B) is still closed.

## Where this is up to

| Piece | State |
|---|---|
| Edit a chapter out of `SRC/` and save it back | DONE, main 14802 (WORKS-20) |
| A console pane in the desk | DONE, main 14815 |
| `vmx` probe reading `IA32_FEATURE_CONTROL` from the desk | DONE, main 14829 |
| Compile from the console | BLOCKED, see below |
| Compare the result against `CODEX.CDX` on the same volume | not started |

`seed/Codex.img` already carries all three of the things the demo needs, side
by side: `SOURCE.SRC` (2,779,919 bytes, the whole compiler), `CODEX.CDX`
(2,759,449 bytes, the compiler as a binary) and `SRC/` (the 64 chapters
individually, plus `INDEX.TXT`). That image is the bed for everything below.

## What is actually built, measured rather than assumed

**`VmCompile` is complete.** It builds the serial input (`mode\n` + source +
EOT byte 4), runs a guest, and parses `SIZE:` out of the guest's serial
output, with a `CODEGEN-HALTED` arm. Nothing in it is a stub.

**`DevHypervisor` is a real Intel VMX hypervisor written in Codex.** `vmxon`,
VMCS field encodings, `vmwrite`, `vmlaunch`, guest page tables, CPUID and MSR
emulation, a device-aware run loop, and capability-MSR control adjustment. It
is not a sketch. This surprised the session that read it and is the single
most important thing on this page: **do not go looking for the missing
hypervisor, it is there.**

**The whole in-box build stack exists** -- `VmCompile`, `VmPingpong`,
`VmSweep`, `CompilerDriver`, `VmRunner`, `SourceConcat`, `SweepHarness`,
`BuildManifest`, `TestRunner`. Until main 14815 every one of them was cited
by exactly one thing: `DevConsole`, which WORKS-5 records as removed from the
boot menu on 2026-08-05 because it is written against UEFI ConIn/ConOut that
the Option A boot path does not have. The machinery was complete and
reachable only from a console that does not boot.

**`ShellDispatch` is admission, not execution, and its `run` verb is a stub.**
`ShellCore.codex` `exec-run` looks the program up in the registry and returns
the Text `"run: <name> grant=<bitmask>"`. It never transfers control. The
chapter's real work is `install` / `verify` / `trust` / `rule` / `revoke`:
signature checking, trust lattice, policy, capability bitmasks. It is worth
having in the console eventually, and it is NOT the build path. A session
that reads the verb list will assume otherwise.

## The blocker: ANSWERED 2026-08-13, VT-x IS AVAILABLE

**The flight happened and the answer is yes.** `vmxprobe.img` on disk 2,
read off the glass by Damian through the console `vmx` command:

```
IA32_FEATURE_CONTROL = 5
VT-x available
VMX revision id 4
```

5 is `101` -- lock bit set, VMX-outside-SMX set. **Road A is open and
Road C does not need pricing.** The revision id is a genuine MSR read
rather than the `vmx-available == False` default of 0, and 4 is what
this processor reports, so 4 is what stamps the VMCS.

**Keep the paragraph below, because its lesson outlived its blocker.**
The bed says 1 and the metal says 5. Two independent codex-vm
measurements on two different days agreed with each other and were both
irrelevant: reproducibility is not validity when the instrument is
pointed at the wrong machine. Anything else gated on a bed-measured
firmware bit deserves the same suspicion.

## The blocker as it stood, and it was one bit

Everything in `DevHypervisor` is gated on `vmx-available`, which requires both
the lock bit and the VMX-outside-SMX bit in `IA32_FEATURE_CONTROL` (MSR 58).

**Under codex-vm that MSR reads 1** -- lock set, VMX-outside-SMX clear, the
encoding of firmware with VT-x switched off. First measured 2026-07-27 and
recorded in `DevHypervisor.codex` prose; independently reproduced 2026-08-13
through the desk console, which is a different surface on a different day.
So a guest compile cannot start in the bed at all.

**It has never been read on metal.** There was no surface that could ask: Dev
Console needs a UART the ASUS does not have. The console `vmx` command is now
that surface, and the reading costs one keystroke on the next boot of any
stick carrying a current desk.

**That reading is the cheapest next action on this whole page**, because it
decides between the two roads below and nothing else can.

## The roads

### Road A -- the hypervisor. THIS IS THE ROAD. VT-x is on.

If the ASUS reports VT-x available, `vm-compile-cdx` can be wired to a
`compile <path>` console command directly. The cone is small: `VmCompile`
cites `Maybe`, `CCE` and `DevHypervisor`; `DevHypervisor` cites `Maybe`,
`HexFormat`, `DevConsoleMenu`, `Kernel VmSerial`, `Kernel VmIde`. The desk
already compiles with `DevHypervisor` cited in (main 14829) at `decks=150`
unchanged, so the dependency cost is measured, not guessed.

If VT-x is off in firmware, it may simply be a BIOS toggle. The console
prints the exact sentence naming that.

**The arena was named as the next wall. It was MEASURED 2026-08-13 and it
is not a wall.** The console's `seed` command reads `CODEX.CDX` off the
volume and converts the whole thing to `List Integer`:

```
CODEX.CDX 2759577 bytes
list 2759577 elems  heap +33554448 bytes
elapsed 0s
```

**32 MiB and sub-second**, against a 512 MB heap (`cdx-to-pe -HeapPages
131072`). The number is exactly capacity 2^22 times 8 bytes plus a 16-byte
header, so the list holds one 8-byte slot per element and the growth
doubles.

**The fear was that `list-push` copies.** It does not: `__list_snoc`
(`codex/compiler/Emit/X86_64ListHelpers.codex:223`) compares length
against capacity, stores in place and bumps the length when there is
room, and its growth path extends in place when the list is the last
thing on the heap -- which it is inside an accumulation loop. So the
append is amortised O(1) and 2.7 million of them are linear, not
quadratic. **Do not reintroduce this worry without re-measuring**; the
command is on the stick and answers in one keystroke.

Still try a small chapter first when wiring `compile`, for the guest
round trip rather than for memory: `SRC/NAME.COD` is 307 bytes and is the
smallest thing on the stick.

**What is NOT measured**: the guest side. `vm-compile` asks for a
256 MB guest and hands the seed across a serial input built from it, and
none of that has run anywhere, because codex-vm reports no VT-x and only
metal can execute it.

### Road B -- call the compiler in-process. Closed as a cheap option.

**The compiler is not a citable quire.** `build/quire-map.ps1` maps 94
quires and `codex/compiler` is not one of them; the compiler's own source is
assembled by `concat-codex-self.ps1` globbing that directory, which is why
intra-compiler cites resolve by assembly rather than by the map. So
`cites Compiler chapter ...` from the desk does not exist and cannot be
written. Making it exist is an architectural change to how the compiler is
assembled, not a wiring job. **Do not start here believing it is a shortcut.**

### Road C -- chain-load the compiler binary. NOT NEEDED, kept for the record.

**Road A is open, so this does not need pricing.** It stays written down
because it is the fallback if the arena question below kills Road A, and
because it is a genuinely different demo shape.

`CODEX.CDX` is already on the volume beside the source, and A5 proves the
compiler compiles itself on the box when it IS the payload (2,753,312 bytes
out, byte-identical to the host compile, 5.0 minutes, UEFI block path). The
desk already has `GopFat16` and can read that file. What is missing is the
hand-off: load the CDX and transfer control, which is what `GopBoot` already
does for payloads.

This needs no VT-x and no quire change. It costs the desk session -- control
does not come back -- so it is a different shape of demo: "the desk launches
the build" rather than "the desk runs the build in a pane". Nobody has costed
it. **If Road A dies on VT-x, price this before Road B.**

## Resume recipe

Everything below runs from the workspace root and needs no token or gate;
this is app-only work.

```powershell
# the desk, headless, against the image that carries SRC/ and CODEX.CDX
build\desk.ps1 -Force -Disk seed\Codex.img -Keys '<timeline>' -Shot out.bmp -ShotDelayMs 9000

# keys are ms:scancode, semicolon-separated, and there is NO auto-break --
# script make AND break (break = make + 128). Console opens on 't' = 20.
# 't' then "vmx" then Enter:
#   4000:20;4150:148;5000:47;5100:175;5250:50;5350:178;5500:45;5600:173;5900:28;6000:156

# read a volume back with a host-side walker that shares no code with the writer
build\fat16-walk.ps1 -Image build-output\desk-Codex.img
```

The desk's `-Disk` runs against a WORKING COPY at
`build-output\desk-<name>.img`; the original is untouched. That copy is what
to walk after a write.

## Traps already paid for

- **Nothing clips a widget panel to its box.** A pane that pushes more rows
  than fit draws them outside the frame and over whatever is below. The
  console's scrollback depth and wrap width are both sized for the NARROWEST
  pane for this reason. Verify any layout change at two resolutions, not one:
  row height and UI scale move together and one resolution cannot tell you
  which bounds it.
- **The `ds` block is 64 bytes, cells 0 to 60**, and on 2026-08-11 two lanes
  independently took cell 48 because nothing in the file said it was spoken
  for. No gate can catch that: each change is green alone. Cells in use are
  8, 16, 24, 28, 32, 36, 40, 48, 52, 56. Name any new one.
- **`GopDesk.codex` is edited by several lanes at once.** A merge-down that
  includes it wants `p4 resolve -am`, never a bare `-at`, and the definitions
  you landed want grepping afterwards -- `0 yours` does not mean your content
  survived.
- **FAT names on the volume are uppercase 8.3.** `ls src` will not resolve;
  `ls SRC` will. The chapters land as `PARSER.COD`, `TYPECHEC.COD` and so on.
