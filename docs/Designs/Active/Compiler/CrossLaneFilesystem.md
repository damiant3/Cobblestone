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

### Step 3 -- the block builtins

Map `block-read-sector`, `block-write-sector` and `block-sector-count`
in both plugs onto the Step 2 driver. This is where `Fat16` starts
working, and the moment it does, `codex/test/fs-layer` should pass on
both lanes with **no change to the test**: it already reaches the disk
through its own handler into `fat16-read-text`. That is the acceptance
criterion for this step, and it is a test that exists today and fails
today.

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
