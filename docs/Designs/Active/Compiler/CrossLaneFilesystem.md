# A File Read on ARM64 and RISC-V

**Status**: proposal, not started. Measured 2026-07-21 (val).

**Ruling 2026-08-05 (Damian): RESURFACED into the IoT/cross-arch chain.** Steps 2-5 (VirtioBlk on the plug lanes, block-read-sector, FileSystem grounding) are live again alongside ProtocolStack's remainder; step 1 landed independently (see the corrected table below).

The arm64 and riscv lanes cannot read a file, and the reason is further
down than it looks. This is the implementation plan. It came out of
a one-line register entry describing a builtin
table; the entry is retired because the capability is four layers deep
and a register row cannot hold a route.

---

## What is actually missing

Measured against source, not inferred:

| Layer | State |
|---|---|
| `read-text` servicer | Absent. x86 installs `__fs-read-servicer` via `emit-seed-fs-handler` (`Emit/X86_64Chapter.codex`); the plugs install nothing. |
| FAT16 | **Present and portable.** `codex/foreword/core/Fat16.codex` is pure Codex and needs only the block primitives underneath it. |
| Block builtins | Absent. Neither plug has an entry for `block-read-sector`, `block-read`, `block-sector-count` or any sibling. |
| Raw load/store | **Present (measured 2026-08-05; Step 1 landed independently of this plan).** `codex/plugs/arm64/Arm64Runtime.codex` registers `peek-byte`/`poke-byte`/`peek-16`/`peek-32`/`peek-qword`/`poke-qword` (~lines 2044-2063) and `codex/plugs/riscv/RiscVRuntime.codex` likewise (~1943-1955). |
| A block device | Absent from the committed Renode boards (`tools/renode/codex/codex-arm64.repl`, `codex-riscv64.repl` declare CPU, UART and RAM only). QEMU virt has virtio-mmio. |

The good news is the shape: **only the bottom two layers are missing
work.** FAT16 and everything above it is ordinary Codex that the plugs
already compile. Give the lanes raw load/store and a block device and
the rest arrives without new code.

## The finding that outranks the feature

`a64-patch-calls-loop` (`Arm64CodeGen3.codex`) and its riscv twin
resolve a call by looking the target up in `func-names`, and **when the
lookup fails they skip the patch and continue**:

```
in if target-offset >= 0
 then ... a64-patch-insn st (patch.insn-index) (arm64-bl rel) ...
 else a64-patch-calls-loop st patches (i + 1)
```

The branch is left as emitted. So every builtin a plug does not
implement compiles to a silent broken call, with no diagnostic at any
stage. `peek-32` and `block-read-sector` are only two instances;
the class is "any name the plug lacks".

**This is step 0 below and it is worth doing even if the rest is never
built.** It converts an entire class of silent breakage into a build
failure, and it is the honest floor under everything here: without it,
each step of this plan can half-land and still look green.

---

## The route

### Step 0 -- an unresolved call is a diagnostic

Report `[UNSUPPORTED]` from the unresolved-call path in both plugs.
The report channel and the harness refusal already exist (they were
built for the read builtins): a `[UNSUPPORTED]` line in the plug's
reports makes `run.ps1` exit 6 and fail the build.

**Validation is the whole cost here.** The change is a few lines; what
it needs is a full `build/test-cross-batch.ps1 -Arch arm64` and
`-Arch riscv64` run to establish that no call is legitimately
unresolved by design. Do not ship this on inspection. If some calls
turn out to be intentionally unresolved, they need an allow-list with
a reason per entry, not a weakened check.

Independently valuable, small, and it belongs on the register as its
own row rather than here.

**Note 2026-08-05:** what landed is a `[WARN]` shadow warning at the
unresolved-call path (`Arm64CodeGen3.codex` ~1815), not the hard
`[UNSUPPORTED]` failure this step prescribes.

### Step 1 -- raw load and store

Implement `peek-byte`, `peek-32`, `peek-qword`, `poke-byte`, `poke-32`,
`poke-qword`, `poke-mmio` in both plug builtin tables as native loads
and stores (`LDRB`/`LDR`/`STRB`/`STR` on ARM64; `LB`/`LW`/`LD`/`SB`/
`SW`/`SD` on RV64). One argument in a register, one instruction out;
the emitters for field access already do the addressing.

Nothing above this step can be written without it, and after Step 0 the
absence of these is a loud build failure rather than a silent one.

**Note 2026-08-05:** landed. Both plug runtimes register the peek/poke
builtins (see the table above).

**Volatility.** These lanes run with the MMU off on the committed
boards, so a plain load is a real bus access and no barrier is needed
for correctness today. Do not rely on that silently: if an MMU is ever
enabled, the device pages must be mapped uncached, and this doc is the
place that claim gets revisited.

### Step 2 -- virtio-blk over MMIO, in Codex

**There is already a `codex/os/kernel/VirtioBlk.codex`, and this design
never mentioned it. It is not the route, measured 2026-08-16, and here is
why so nobody re-asks:**

- It is **PCI transport** (`cites Kernel chapter VirtioPci`,
  `virtio-blk-init : Arm64PciDevice -> VirtioBlkState`), not MMIO.
- Its buffers are fixed at `#300000`, `#310000` and `#320000`, which are
  **below RAM base on both cross beds**: `test-cross-batch.ps1` loads
  arm64 at `0x40100000` and riscv64 at `0x80000000`, so those addresses
  are unbacked there. It was written for a different memory map.
- It has **no caller anywhere in the tree** (L-UNCALLED), so nothing has
  ever exercised it, and it has no write path at all.

What it does prove is that the bed can carry this class: `arm64-web-server`
cites `Arm64Pci`/`VirtioPci`/`VirtioNet` and COMPILES on the cross lane
(10.6 s). It is `PASS_COMPILE_ONLY` with no `.expected`, so it has never
run, and compiling is the only thing it establishes.

A new foreword chapter (`codex/foreword/core/VirtioBlk.codex`), pure
Codex over the Step 1 primitives, so it compiles for every target
rather than being emitted per plug:

1. Probe the device: magic `0x74726976`, version, device id 2.
2. Status handshake: ACKNOWLEDGE, DRIVER, FEATURES_OK, DRIVER_OK.
3. Queue 0 setup: queue size, and the descriptor / avail / used ring
   addresses. The rings are ordinary Codex buffers; their alignment
   requirements are the part to get right.
4. A read request is three descriptors: a 16-byte header (type 0,
   reserved, sector), the data buffer, and a one-byte status.
5. Notify, then poll the used ring. Fuel-capped, like every other
   polling loop in `codex/boards/`.

Base addresses are machine properties, not ours: QEMU virt puts
virtio-mmio at `0x0a000000` (arm64) and `0x10001000` (riscv64), and the
device is discovered by probing the slots rather than assuming one.
**Read them off the machine, do not hardcode a slot.**

**Where the rings may live on ARM64** (root, 2026-08-16): the ELF loads
at `#40100000` with entry `0x40100880` and grows with the program, so
`#401xxxxx` is code and nothing of ours goes there. The only reserved
region is above the CCE tables, and `Arm64ProcessKernel.md` Stage 1 moves
the heap base from `#40202000` to `#40204000` to hold the process table.
**Any fixed cell here goes above `#40204000` and bumps `a64-heap-start`**
rather than sitting below it. Ordinary Codex buffers off the heap need
none of this; the constraint binds only if the ring alignment forces a
fixed address.

### Step 3 -- the block builtins

Map `block-read-sector`, `block-write-sector` and `block-sector-count`
onto the Step 2 driver. This is where `Fat16` starts working, and the
moment it does, `codex/test/fs-layer` should pass on both lanes with **no
change to the test**: it already reaches the disk through its own handler
into `fat16-read-text`.

**This step needs NO PLUG EDIT, measured 2026-08-16, and this paragraph
used to say "in both plugs".** A Codex chapter that DEFINES
`block-sector-count` (calling into the Step 2 driver) is enough: the
plug's patcher resolves a call by looking the name up in `func-names`
(`a64-patch-calls-loop`, `Arm64CodeGen3.codex`), and a function the
program itself defines is in that table like any other. Nothing has to be
emitted in `Arm64Runtime.codex`.

The ablation, both arms through the same plug binary in one run, one
variable changed:

| arm | a Codex `block-sector-count` | `[WARN] unresolved call to 'block-sector-count'` |
|---|---|---|
| A | absent | **present** |
| B | present | **absent** |

**CORRECTION, same day, before anything was built on it: that is not a
usable route for `fs-layer`, and the paragraph above overstated what the
ablation proved.** The ablation is sound -- a Codex definition does
resolve the call on the plug lanes. What it does not establish is that
such a definition can be REACHED by `fs-layer` without breaking the other
lane, and it cannot:

- A Codex definition shadows the builtin on **x86-64 as well**. Measured:
  a chapter defining `block-sector-count = 424242` prints `count 424242`
  under codex-vm rather than performing syscall 10.
- `fs-layer` **passes on x86-64 today** (measured: output matches
  `.expected` exactly), and it must keep passing.

So a shared chapter defining the block builtins would fix the cross lanes
by breaking the x86-64 lane, and `fs-layer` cannot cite one. The
definition has to be TARGET-SPECIFIC, which means the plug supplies it,
which means `Arm64Runtime.codex` (and its riscv twin) after all. The
claim released to root on the strength of the ablation is being taken
back; root was told.

What survives is narrower and still useful: **a program that defines the
block builtins in its own Codex resolves them on the plug lanes**, which
is how a cross-only probe can exercise the Step 2 driver without touching
either plug. That is a probe technique, not the route for `fs-layer`.

**The route that does work, and why it dodges the shadow.** Never define
`block-read-sector` in Codex. Define only the `vb-*` driver names, and
have the PLUG emit a native `block-read-sector` that tail-calls
`vb-read-sector`:

- `a64-record-func` names it, so the patcher resolves the builtin call
  the way it resolves the process and net helpers (root's shape, next to
  `a64-emit-net-helpers` in `a64-emit-runtime`).
- Nothing named `block-*` exists in Codex, so x86-64 keeps its syscall
  helper and `fs-layer` keeps passing there.
- `vb-read-sector` has to BE in the program, and
  `compile-arm64.ps1` has no injection point -- it compiles the user
  source and nothing else -- so the program must CITE `VirtioBlk`. One
  `cites` line on `fs-layer` does it, costs about 3 bytes
  (`DevelopersRulebook.md` 7), and shadows nothing, so the x86-64 arm is
  unaffected.

That one line is a change to the test, which this design said would not
be needed. The "no change to the test" claim was about handler code --
`fs-layer` already installs its own `FileSystem` handler and that part
holds -- and it does not survive the citation question.

**That premise was measured the same hour and the measurement was scoped
wrong.** `Arm64Runtime.codex` does contain zero `arm64-bl` emissions and
zero `call-patches` registrations -- that grep was correct -- but the
conclusion drawn from it, "nothing in the tree has ever called out of a
runtime routine into a program function", was not. **`a64-add-call-patch`
already existed** (`Arm64CodeGen.codex:269`) and `__opening_print`
(`Arm64CodeGen3.codex`) already used it. Greping ONE file and concluding
something about the tree is the error, and it is the same shape as
reading a library-shaped byte diff as a statement about a probe's own
functions, two steps earlier in this document.

So the route needed no new machinery at all. `a64-emit-call-to` is a
two-line wrapper over the existing helper -- record the patch, emit the
placeholder it names -- and it lives beside the block helpers only
because that is where it is used.

The alternatives if it is refused: emit virtio in ARM64 machine code,
which duplicates Step 2 and is the reason Step 2 was written in Codex; or
give the compiler a target-conditional builtin, which is seed-affecting.
Both are worse and both deserve a ruling rather than a quiet choice.

`a64-find-func-offset` searching BOTH the runtime funcs and the program's
own Codex functions is what makes any of this work, and it is also why
the probe technique above resolves.

The stub shape used by `a64-emit-net-helpers` (`a64-rt-gated-zero`) is
deliberately NOT proposed as an interim. It would silence all 21
unresolved-call warnings and return a defined value, which reads as
progress while making a broken disk path look like a working one -- the
exact failure Step 0 exists to prevent.

**What the ablation does NOT show is that it works.** It shows the branch
is patched instead of left reading a stale `x0`. Whether the driver
behind it drives a real device is Step 5's question, and nothing before
Step 5 can answer it.

**Measured 2026-08-16: `fs-layer` does not FAIL on the cross lanes today,
it is SKIPPED, and this paragraph said "a test that exists today and
fails today".** `build/test-cross-batch.ps1 -Arch arm64 -UseQemu -Filter
fs-layer` answers `SKIP fs-layer (machine sidecar (.disk))`: the harness
skips any test carrying `.disk`, `.disk2`, `.disk-src`, `.vmargs` or
`.keys` unconditionally (`test-cross-batch.ps1:88`), on a comment that
says no cross board can mount one. So the acceptance criterion cannot be
OBSERVED until Step 5 makes a `.disk` test eligible under QEMU. **Step 5
is not optional alongside Step 3; it gates it**, which the sequencing
note below half-says already ("needed to prove 3") and this paragraph
contradicted.

The control that IS available before Step 5, and the one to start from:
compile a program calling `block-sector-count` through
`codex/plugs/arm64/run.ps1`. Today it answers

    [WARN] unresolved call to 'block-sector-count': the arm64 plug emits
    no such function, and the branch was left unpatched -- reaching it
    reads a stale x0

and then **`OK: ...elf (13070 bytes)`, exit 0**. That is Step 0's soft
`[WARN]` standing where this design prescribes a hard `[UNSUPPORTED]`
failure, observed in a live case rather than inferred: a build that
should fail is green, and the artifact it hands back has a call that
reads a stale register. Step 3 is done when that warning is gone and the
branch is patched; it is PROVEN when Step 5 lets `fs-layer` run.

**PARTLY DONE 2026-08-16: `block-sector-count` works on the arm64 lane,
against a real device.** A probe prints `32768` for the 16 MB
`fs-layer.disk`, by both routes:

    direct  32768     (Codex calling vb-capacity-auto)
    builtin 32768     (the plug's native block-sector-count)

The two agreeing is the control: it shows the native builtin returns the
driver's answer rather than printing something of its own.

The chain that now exists: native `block-sector-count` ->
`a64-emit-call-to` patch -> BL -> Codex `vb-capacity-auto` -> `vb-find`
probe -> `vb-capacity` config read -> QEMU virtio-blk. Capacity was chosen
first deliberately: it needs no queue and no DMA, so it isolates the MMIO
and probe path from the ring path.

**Two machine facts, both measured, both of which silently answer "no
disk" if you get them wrong:**

- QEMU's `virt` presents virtio-mmio **version 1** (legacy) unless
  `-global virtio-mmio.force-legacy=false` is passed. The raw registers
  read `magic 1953655158 ver 1 dev 0` without it and `ver 2` with it. The
  driver implements version 2 only and refuses version 1, so the flag is
  required rather than cosmetic; it is in `test-cross-disk.ps1` with that
  note.
- **QEMU fills virtio-mmio slots from the TOP.** With 32 slots the block
  device is slot **31**; slots 0-30 read `dev 0`, which is an empty slot
  and not the end of the array. A probe that checks slot 0 and stops finds
  nothing on a machine that has a disk.

**What still blocks `fs-layer`: the driver is ELIMINATED unless Codex
references it.** Citing is not enough -- measured, a program that cites
`VirtioBlk` and only reaches it through the builtin has NO `vb-*`
definition in the IR at all, and the plug's patched call then reports
`unresolved call to 'vb-capacity-auto'`. That is correct behaviour
(`DevelopersRulebook.md` 7: a cite governs visibility, and unreached code
is still eliminated), and the plug cannot influence it because the only
caller is machine code the compiler never sees.

**RULED 2026-08-16 (red): no user-visible reference.** Reachability is
derived from the program's OWN DECLARATION, the way the boot grant is
derived from the manifest: a program whose `opening` carries the block
capability, or that uses a block builtin, keeps the driver; one that does
not has it eliminated exactly as today.

The reason is the shape of the alternative, not its cost. A disk program
that must cite a driver chapter to make its disk exist is a guarantee held
by whoever remembers the incantation (L-FREEDOM) -- it works for the
author who just debugged it and fails silently for the next person, who
gets a program that compiles, boots, and reads nothing. `keep-driver` in
the probe above is exactly that shape, and it is why the probe is a probe
and not the pattern.

**Where it lands.** The IR handed to a plug is pruned by
`ir-prune-unreachable (fe.ir) "opening"` (`compiler/opening.codex:1657`),
from the single root `opening`. `ir-prune-unreachable-roots` already
exists and takes a root LIST -- `compile-to-cdx-with-exit-mode` uses it
with `cdx-emit-roots` -- so the change is to pass the driver entry points
as additional roots when the program declares `Device.Block`, and nothing
new has to be invented to prune from more than one place.

That is a `codex/compiler` change and therefore seed-affecting: it takes
the token and states its `Sut` hash.

**THE READ PATH WORKS, measured 2026-08-16 against a real device.** Two
different sectors, each checked against the bytes actually in
`fs-layer.disk`:

    s0 tail 85 170        the boot signature
    s1 head 69 70 73 32   "EFI ", the GPT header
    s1 tail 0 0           the opposite of sector 0's tail
    distinct 648          two separate buffers

Reading one sector proves less than it looks: a stale pointer or a zeroed
buffer answers something. Two sectors with DIFFERENT known content, each
matching, is what shows the sector number is honoured rather than a read
happening at all.

`block-read-sector` bump-allocates 512 bytes from the heap frontier in x28
and answers that address, which is x86-64's contract for this builtin
(`emit-block-read-sector-helper` does exactly this with r10).

**The alignment fault, because it is the shape of bug this bed produces.**
An available-ring ENTRY is 16 bits and the entry array starts 4 bytes into
the ring, so every odd entry is at 2 mod 4. There is no `poke-16` builtin
(only `poke-byte`, `poke-32` and the mmio forms), and the first draft used
`poke-32` there. With the MMU off an unaligned store is not slow, it is a
fault:

    !A64FAULT ESR=0000000096000061 FAR=0000000040010106

DFSC 0x21, and `FAR` is ring+262, which is entry 1. **The first read of
any program succeeded and the second killed the guest**, so a probe that
read one sector would have passed and shipped it. Two byte stores fix it.

**THE WRITE PATH WORKS TOO, measured 2026-08-16.** Write a pattern to a
sector, read it back:

    before 0 0 0
    wrote 1
    after 65 66 67

A read and a write differ in exactly two fields, the header type and
whether the data descriptor is marked WRITE, so `vb-request` is written
once and `vb-read-sector` / `vb-write-sector` are two-line wrappers. The
ring dance is where the alignment fault lived; a second copy of it would
be a second place for that class of bug to hide.

The probe writes through `snapshot=on`, so the committed `.disk` fixture
is not modified by a test that writes.

**The root list is a maintenance trap and it fired immediately.** Adding
`vb-read-auto` without adding it to `ir-emit-roots` left it eliminated,
and `fs-layer` reported `unresolved call to 'vb-read-auto'` -- exactly
what the note beside that list warns about, one increment after the note
was written. Nothing checks it, and the symptom names the missing
function rather than the list. All three entry points are in the list
now.

**`fs-layer` PASSES ON THE ARM64 LANE, 2026-08-16, with no change to the
test.** `build/test-cross-disk.ps1 -Arch arm64 -Test fs-layer` answers
`compile ... OK` / `run ... PASS`. It writes `NOTE.TXT` through its own
`FileSystem` handler, reads it back, and reads a second absent file, all
through `Fat16` onto real virtio hardware. That is the capability this
design was written for.

**The last defect was a CONTRACT, not a bug in the driver, and every
sector write was already succeeding when the test said otherwise.**
`block-write-sector` answers **0 for SUCCESS**. x86-64's helper ends
`li reg-rax 0` unconditionally
(`X86_64Helpers.codex:emit-block-write-sector-helper`) and `Fat16` reads
it that way: `fat16-claim-cluster` is `if w == 0 then c else 0`, so a
non-zero answer means the cluster was not claimed. Returning the driver's
own 1-for-success straight through made `fs-layer` report
`<write failed>` while the disk was being written correctly.

Finding it needed the failure to be narrowed rather than guessed. The
volume was suspected first and cleared by measurement -- the arm64 lane
parses it correctly (`part-start 2048, bps 512, spc 1, clusters 30414`),
so `fat16-vol-is-usable` was never the problem. The x86-64 helper is the
document of record for what a block builtin answers, and a new lane
implements that contract rather than choosing one.

### Step 4 -- the servicer

Install a `read-text` handler on the plug lanes, the equivalent of
`emit-seed-fs-handler`. Only after this does a program that never
declares its own handler get a file read, which is what the original
register entry asked for.

### Step 5 -- the harness

The committed Renode boards have no block device, so these tests run
under QEMU. `build/test-cross-smp.ps1` is the precedent for a
QEMU-only cross lane keyed off a sidecar; a `.disk` sidecar on the
cross lanes would follow it. The single-core Renode batteries must
**skip** any test carrying one, exactly as they skip `.smp`, or they
fail by design.

**DONE 2026-08-16.** `build/test-cross-disk.ps1` follows the `.smp`
precedent, and `test-cross-batch.ps1` now ROUTES a `.disk` test
(`SKIP fs-layer (block device (build/test-cross-disk.ps1))`) instead of
calling it a machine sidecar. `.disk2` and `.disk-src` stay ineligible: a
second image and a compile-onto-the-disk fixture are still codex-vm's.

Transport: `-device virtio-blk-device` is the MMIO transport on `-M virt`
(`virtio-blk-pci` is the PCI one), which is the choice Step 2's driver
probes for. The image is attached `snapshot=on` so a writing test cannot
mutate the committed sidecar, which `fs-layer` would otherwise do on
every run.

**`fs-layer` is now OBSERVABLE, and it fails.** First run on the arm64
lane:

    expected: layer from the layer / absent <absent>
    actual:   layer <write failed>  / absent <absent>

with 21 `unresolved call` warnings for `block-read-sector` and
`block-write-sector`. **The guest boots, the `FileSystem` handler runs,
and the UART is compared** -- so what remains is the block path alone,
which is Step 3. This is the control Step 3's acceptance criterion needs
and could not have before this step existed.

One caution about reading that output: the `absent <absent>` line
matching is NOT evidence the read path works. A read that returns a stale
register answers `None` too, so that line passes for the wrong reason
today, and only the first line discriminates.

---

## Sequencing and ownership

Steps 0 and 1 are plug codegen and sit in **reek's** lane. Step 2 is a
foreword driver. Steps 3 to 5 are wiring and harness.

Step 0 is independent of the rest and should not wait for it.

The order is forced: 1 before 2, 2 before 3, 3 before 4. Step 5 can be
built alongside 2 and is needed to prove 3.

## What would make this not worth doing

Stated so the decision stays visible. Nothing in the tree needs a file
on these lanes today: they are cross-architecture *codegen* lanes, and
their battery is a parity check on compiled programs, not a platform.
The 14 filesystem tests in the battery are the only callers, and they
exist to test the filesystem rather than the backends.

If the answer is that the cross lanes are only ever a codegen parity
check, then the correct close is Step 0 alone -- every unavailable call
refuses loudly -- and Steps 1 to 5 never get built. **That is a real
option and it is cheaper than this document.**

## Cross-references

- `docs/Designs/Active/Compiler/LirRetarget.md` -- the other live
  workstream in the plugs; touches the same emitters.
- `docs/ExaminersAssay.md` -- the cross-architecture battery, the
  `.smp` sidecar precedent, and how a QEMU-only cross test is run.
- `codex/foreword/core/Fat16.codex` -- portable, and the reason the top
  of this stack costs nothing.
