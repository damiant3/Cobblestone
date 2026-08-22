# The Desk Build Loop

**Goal.** Edit a compiler chapter in the desk, save it, and compile it on the
machine from the desk console, with the answer on the glass. The end state is
a stick that carries its own source, its own compiler, and the proof the two
agree, where a person can change the source and watch the proof move.

**Status 2026-08-15: the edit half is done, the hypervisor is provisioned, and
what is left is an arena the flying image can spare.** VT-x is available on
the ASUS, measured on metal 2026-08-13. The road that looked cheapest (Road B)
is still closed. Read "How the guest is provisioned" below before planning any
of this.

## Where this is up to

The editing, the console pane, the `vmx` probe, EPT and the sizing refusal,
device-path launch, and an arena big enough to hold a guest are all done and
in the depot.

| What is left | State |
|---|---|
| Compile from the console | The read path runs in the bed and is gated (`codex/test/apps/gcon-compile-read`). The LAUNCH has never run anywhere: no bed has VT-x, so metal is the first machine to execute it |
| Compare the result against `CODEX.CDX` on the same volume | Built and gated (`gcon-cdx-verdict`, `codex/test/apps/gcon-cdx-verdict`). It compares `vcr-binary` against the volume's buffer with `peek-byte`, so it allocates nothing on top of what the compile already holds, and it reports both lengths and the first differing offset rather than a yes or no (L-SHORT). Seven arms cover identical, a differing byte at the first and a middle offset, produced short, produced long, produced empty, and both empty; with the diff sabotaged to answer identical always, six of the seven flip. Only the launch that feeds it real bytes still waits for metal |

`seed/Codex.img` carries the three things the demo needs side by side:
`SOURCE.SRC` (the whole compiler as one file), `CODEX.CDX` (the compiler as a
binary) and `SRC/` (the chapters individually, plus `INDEX.TXT`). That image
is the bed for everything below. Sizes move with every seed, so read them off
the image rather than from here.

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

## VT-x, and the one number that differs between bed and board

`DevHypervisor` is gated on `vmx-available`, which needs both the lock bit and
the VMX-outside-SMX bit in `IA32_FEATURE_CONTROL` (MSR 58).

**The board reads 5 and the bed reads 1.** On the ASUS: lock set,
VMX-outside-SMX set, VMX revision id 4, so Road A is open and 4 is what stamps
the VMCS. Under codex-vm the same MSR reads 1, which is the encoding of
firmware with VT-x switched off, so **a guest compile cannot start in the bed
at all** and never will. Two codex-vm measurements on two different days
agreed with each other and were both irrelevant to the question (L-OPTIONAL).

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

**What is NOT measured**: the guest side. It has not run anywhere, because
codex-vm reports no VT-x and only metal can execute it.

## How the guest is provisioned

The guest runs under EPT, so guest physical addresses are translated and the
guest cannot reach the host. `vm-prepare-guest` refuses before it allocates
anything if VT-x is absent, if the request is outside 32 MB to `guest-max-mb`,
or if the arena has less free than the request plus a 16 MB reserve; the bump
allocator has no bounds check of its own, so this is the only thing standing
between an oversized guest and a silent overrun.

Given a size it takes one region for the guest and three pages for the EPT
tables, maps guest physical to that region in 2 MB leaves, and hands back the
EPTP. `GuestConfig.memory-mb` sizes it. `guest-max-mb` is derived from the
512-leaf cap rather than written down twice, so the two cannot drift apart.

The guest's low 64 KB is initialised and its RAM size written at
`ram-size-addr`, because a raw CDX enters at `__start`, which loads RSP from
there and then stores its heap position to `deck-pos-addr` and `heap-hwm-addr`.
Those are guest-physical under EPT and land in the guest's own region; the rest
of the region is handed over unzeroed, because zeroing it would be one store
per dword across the whole allocation.

`vmlaunch-full` and `vmresume-full` set HOST_RSP and HOST_RIP themselves:
`emit-vmlaunch-full-helper` aims host RIP at the landing pad immediately past
`vmlaunch`, so an exit returns 0 to the Codex caller and a failed launch
returns 1. **Nothing in the `.codex` names those VMCS fields**, so grepping the
Codex source for `vmcs-host-rip` finds nothing and answers a question about
Codex rather than about the machine. Use the plain `vmlaunch` builtin and no
host state is written at all.

`codex/test/apps/vmx-ept-table` asserts the encoding in the bed, which it can
do because building the tables is pure memory writes and needs no VT-x: leaf
`0xB7`, non-leaf `0x07`, EPTP `0x1E`, the 2 MB leaf stride, the 512-leaf cap,
both sizing refusals, and the RAM size landing where `__start` reads it.
Sabotaging `ept-memtype-wb` to 0 moves the leaf row by exactly 48 and leaves
the other two unmoved.

**Do not relocate the guest instead of relying on EPT.** The obvious-looking
alternative is to move the host's structures aside and patch the guest's
addresses, but `ram-size-addr`, `deck-pos-addr` and `bare-metal-heap-base` are
compile-time constants baked into every CDX by the code generator. Changing
them means the guest is no longer the same `CODEX.CDX` that sits on the stick,
which deletes the demo: the whole claim is that the compiler on the stick
compiles itself.

### What Road A still needs

1. **An arena the flying image can spare. The value is measured: 131072.**
   The Option A path is past ExitBootServices, so firmware cannot be asked for
   pages at run time and the guest's region comes out of the same arena the
   desk is using; at the shipping `-AllocPages 32768` a 256 MB guest refuses
   every time.

   ```powershell
   pwsh build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex `
     -Out build-output/desk.img -AllocPages 131072 -Kernel seed/Codex.cdx -Ebs -Uefi
   pwsh build/boot/test-ovmf.ps1 -Img build-output/desk.img -Out build-output/ovmf.png -Seconds 30
   ```

   Measured 2026-08-15: that image boots on real edk2 firmware in a 2048 MB
   machine and paints the desk (five colours, the `26,26,46` ground with grey,
   orange, green and cyan on it). The control is the SAME image under
   `-MemMB 256`, which never reaches the paint -- one flat colour -- so the
   bed can refuse and this green is not vacuous. The guard's own arithmetic
   was checked separately against four `-mem` values (3072, 1024, 512, 256):
   `vm-arena-free` tracks the real top every time and the 256 MB request
   refuses at exactly the point it should, so it is reading the machine rather
   than a constant. Cell 4072 carries the stack top under UEFI and the RAM
   size on bare metal, which is the top of the arena either way, which is why
   one expression is right on both paths.

   **This does not settle the ASUS.** OVMF satisfying a 512 MB
   `AllocateMaxAddress` below the aperture is precisely the spec freedom
   L-FREEDOM is about, and AMI may answer differently; the stub raises `H` on
   a failed heap allocation, so a board that refuses will say so. Do not read
   the bed's yes as the board's yes (L-ARENA cuts the other way here: the bed
   is now the GENEROUS one about firmware, not about size).

2. **`compile <path>` is wired (2026-08-19, `GopConsole.codex`
   `gcon-compile`).** It mounts the ESP, reads the source and `CODEX.CDX`
   through `gfat-read-file`, converts the source with `unicode-bytes-to-text`
   and the seed with `gcon-buf-list`, and hands both to `vm-compile-cdx` with
   `vmx-read-revision-id`, printing the returned size or the log.

   **What is proven and what is not. RE-MEASURED 2026-08-21, and most of this
   paragraph was describing code that no longer exists.** It said the refusal
   fires before any file is touched and that the bed could reach neither the
   read nor the conversion. That was true of the order the check sat in until
   main 18368 moved it to just above `vm-compile-cdx`, and the paragraph was
   not updated with the code it describes.

   Measured against a copy of `seed\Codex.img`, `compile SRC/INDEX.TXT` now
   answers, in order, `SRC/INDEX.TXT 2320 bytes, CODEX.CDX 2872563 bytes` and
   then `VT-x is locked off in firmware (IA32_FEATURE_CONTROL=1)`. So the
   mount, BOTH reads, `unicode-bytes-to-text` and the byte-count report all
   run in the bed, and the refusal still fires after them. **What remains
   unexercised anywhere is the launch alone** -- `vm-compile-cdx` and below.
   codex-vm is itself a WHP hypervisor and its guest sees no VT-x, so the
   first machine to execute the launch is the metal, and a failure there is
   expected rather than surprising.

   **Both arms have a runner now** (`codex/test/apps/gcon-compile-read`),
   which is the point: this paragraph was wrong for two days because a
   hand-run arm written into prose is a claim nothing re-evaluates. The gated
   arm takes NO `.disk` sidecar deliberately, and `no FAT volume on the boot
   medium` is its pass: reaching the mount at all is what proves the check is
   no longer above it, and restoring the old order makes the same run answer
   with the VT-x refusal instead. Byte counts are deliberately not pinned
   there -- they move with every seed refresh, and a fixture re-minted for
   unrelated reasons is how an arm stops being read. To reproduce the richer
   reading above, which is the one that needs a real ESP:

   ```powershell
   pwsh build/compile.ps1 -Src codex/test/apps/gcon-compile-read.codex `
     -Out <out>.cdx -Log <log> -Kernel seed/Codex.cdx
   pwsh build/test-run.ps1 -Kernel <out>.cdx -OutFile <out>.txt -DiskFile <a COPY of seed/Codex.img>
   ```

   The peak cost is a little over nine times `CODEX.CDX` held as a
   `List Integer`, plus the source, on top of whatever the pane holds. The
   `seed` command already pays the larger half and prints what it cost.

### Road B -- call the compiler in-process. Closed as a cheap option.

**The compiler is not a citable quire.** `build/quire-map.ps1` maps 94
quires and `codex/compiler` is not one of them; the compiler's own source is
assembled by `concat-codex-self.ps1` globbing that directory, which is why
intra-compiler cites resolve by assembly rather than by the map. So
`cites Compiler chapter ...` from the desk does not exist and cannot be
written. Making it exist is an architectural change to how the compiler is
assembled, not a wiring job. **Do not start here believing it is a shortcut.**

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
