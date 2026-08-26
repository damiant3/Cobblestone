# GitHub Update 45

**Scope: main CLs 15687 onward, opened 2026-08-16.** Update 44 covered 15254
to 15686. Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 44

- **The battery harness can lose bytes from a batch stream** and file the
  survivors under the wrong names. Seen once, at 15671 with ten VMs live; not
  seen in the two batteries run afterwards with the box otherwise idle. The
  lossy layer (guest serial, host `-output` writer, or the parser's marker
  walk) is not established. `ExaminersAssay.md` "The batch stream can lose
  bytes".

- **The zig plug's four defects** (`plugs-backlog` 1.13, val): no `int-mod`
  emitter, a record literal emitted without the parentheses zig needs before a
  field access, bounded-field clamping not emitted, and the fourth found at
  15634. The oracle arm stays unwired until they are fixed.

- **`plugs-backlog` 1.2, 35 transpiler plugs leaking the field slot** (val).

- **Track D items 18 and 19 land here** (reek 15677, 15690, 15691, 15695,
  queued for MAIN OPEN at the release): Lz4, Lz77 and Deflate guarded; Rle a
  row and no guard; Brotli a termination arm and no guard. Item 19 is closed on
  the `compress/` subset only: the `ai/`, `ui/` and `core/` leg is unswept and
  unowned (reek 15696), and `tls-decode-record` and `pbkdf-verify`, run by a
  harness though called by no production code, qualify for a guard under the
  ruling if it is extended past `compress/`. The OtaBoot flash length residue
  stays open by name. Then the census queue.

- **Every stub path carries unflown bytes since 15503**; the native-GOP metal
  half is a photograph.

- **Nothing exercises the guard page under a genuine allocation walk.**

## Landed this cycle

- **ARM64 has a process and capability kernel** (root, main 15782, 15795,
  15807; `docs/Designs/Done/Compiler/Arm64ProcessKernel.md`, `plugs-backlog`
  1.10 closed at enforcement). A process table at `#40202000` laid out by
  x86-64's offsets, the seven process builtins as runtime functions with
  x86-64's semantics (pid range checks, the admin bit demanded of the caller),
  the boot grant derived plug-side from the `opening` type through
  `codex/plugs/common/PlugManifest.codex` (the same `Capability` table x86-64
  reads, bundled by a `-CommonChapters` switch RISC-V can opt into), and an
  inline capability check gating the four network builtins with x86-64's
  refusal values. Six x86-64 tests that were `.no-cross`-excluded as "bare
  runtime with no kernel" pass unchanged on ARM64 (`cap-direction`,
  `cap-process-family`, `network-scope-deny`, `network-scope-open`) or by new
  twin arms (`arm64-proc-cells`, `arm64-net-gate`), each stage ablated before
  it was believed. Found on the way: the ARM64 ELF loads at `#40100000`, not
  the disassembler's `#40000080`, and a first table placement inside it broke
  every program over 64 KB until the full cross bed said so, which is why every
  stage was measured against a same-day full-bed baseline. Left by ruling: the
  SVC servicer path (1.17, blocked on the ARM64 block device) and process
  lifecycle (1.18); the three `cap-*-denied` tests stay x86-64-only because they
  poke the absolute address 20536.

- **ARM64 has a process lifecycle** (root, main 15834, 15863 and this
  cycle's close; `docs/Designs/Done/Compiler/Arm64ProcessLifecycle.md`,
  `plugs-backlog` 1.18 closed). The cooperative half of x86-64's model, which
  is all of it without a timer: `process-spawn` (and `-with-heap` with the
  32 MiB region bound), `__proc_entry`, `process-exit`, `process-wait`,
  `process-yield`, an idle dispatch and a resume, on a spawn pool of sixteen
  32 MiB regions at `#50000000` with the running pid a function of the stack
  pointer. The process table moved to the mirror of x86-64's 20480 and the
  qword MMIO helpers joined the low-memory remap, so `proc-state-running` and
  `spawn-reuse`, which read the table by absolute address, pass unchanged;
  `process-exit-status`, `nested-spawn` and `network-scope-spawn` with them.
  Every stage was ablated (no status delivery: `0/0`; no FREE store: `2`; no
  bound: `10`) and measured against a same-day full-bed baseline. Of the
  fifteen tests once excluded as "bare runtime with no kernel", nine now pass
  unchanged across 1.10 and 1.18; the six left wait on a device or are
  x86-64-only by design.
- **PR 66 carried in, and every wired plug oracle arm is green for the
  first time** (val, `plugs-backlog` 1.26). **Steve Howell's**
  `ZigEmitter.codex` from `showell/NewRepository` `zig-plug-arith` taken
  wholesale: 224 definitions against the depot's 207, carrying his own
  implementations of the four defects 1.13 closed plus the third store
  site, and 27 the depot lacked. One line merged on top, the only thing he
  lacks: a `list-snoc` registration beside his `list-push`, without which
  the shared oracle subject emits `@compileError("zig plug: no emitter for
  list-snoc")` and does not compile. **That is what took zig's arm from red
  to green: `build/plug-oracle-test.ps1` is now 5 passed 0 failed, python,
  javascript, zig, wasm and csharp each 33 of 33.** His `run.ps1` is the one
  file not taken, because main's passes `-Passes 'text-plug'` and his does
  not; `zig-ladder/` stays on his branch, as PR 65 set the precedent.
  **Rung 13, his whole-compiler arm, does not reproduce here and it is not
  his emitter.** His bundler builds the subject on this box (54,856 lines),
  it compiles at his deck scale and runs on bare metal, and IR emission is
  13.5 MB -- then the plug guest raises `OUT OF MEMORY` part way through
  emitting, stopping between 534,800 and 547,400 bytes across five
  identical runs, every length a multiple of `net-mss`, at 3 GB and at
  12 GB alike. His 16,874-line result and the Linux process form stand as
  **his measurements on his harness**. Found in passing and not fixed:
  `build/plug-run.ps1`, which 38 plugs share, reported `OK` on every one of
  those dead guests, because it greps the VM's stderr for `TRUNCATED sent=`
  and the guest console is not on stderr.

## The ARM64 site serves, and the fault was one dropped 16-bit write

The OCI ARM64 lane went from a driver that had never moved a frame to a
machine answering `HTTP 200` on the wire, and every step of it was a
measurement rather than a hypothesis.

**The transmit path was dead because `queue_select` never took.**
`virtio-select-queue` is a `poke-16` to common-config offset 22, and `poke-16`
was not a 16-bit store: it was a 32-bit read-modify-write of the enclosing
aligned word, so it landed as a four-byte write at offset 20, which is
`device_status`. QEMU dispatches common-config writes by field and width and
dropped it. `queue_select` stayed 0 forever, so `virtio-setup-queue vd 1`
reconfigured queue 0 with the TX ring's addresses and the TX queue was never
configured at all. VirtioBlk passed throughout for the reason that names the
cause: it has ONE queue and only ever selects queue 0, where a dropped write
costs nothing.

Four candidates were eliminated on the way and are written down so nobody
re-chases them: BAR decoding, page-table mapping, write width, and a
`num_queues` poison test that turned out not to be a control at all, because a
write that never lands reads back 0 exactly like a field the device ignored.
The instrument that settled it was a notify COUNT: 100 notify writes, zero
additional notifies at the device, against a `virtio_set_status` trace proving
our writes did reach common-config.

Real `peek-16`/`poke-16` builtins closed it (root), and the guest transmitted.
Then RX, then a forwarded connection, then the site: `GET /` answers 200 with
948 bytes of HTML and `GET /api/health` answers `{"status":"ok"}`.

**One defect on that path is worth keeping for its shape.** A routed path
arrived as `napinhealth` where `/api/health` was sent, with the wire dump
showing the bytes correct on the link. Three named suspects were all innocent;
what found it was instrumenting the path with its CCE code points beside the
text, which showed `/` decoding as 18 on one request and 44 on the next where
the oracle says 81. **A value that changes between requests is clobbered
memory, not a conversion fault** -- and the cause was the VirtIO DMA regions
sitting inside the guest's own heap, corrupting one CCE table entry, which is
why exactly one character was wrong and the rest survived.

## Cost is a thing the compiler checks now

`bounded <class>` ships as a declaration in `punctual`'s family: a CEILING in
the lattice `none < fixed < linear < growing`, inferred bottom-up from the body
and refused transitively, so "linear where you read it and quadratic
underneath" is a compile error instead of an arena that runs out. It is a first
slice -- `linear` and `growing` are inferred and checked; `none` and `fixed`
are named rungs the compiler refuses with CDX6103 rather than take on trust.

**The instrument was built before the check, deliberately.**
`codex/test/cost/accumulator-corpus` is eleven functions, five measured
quadratic and six measured linear, every label produced by running the thing at
n, 2n and 4n rather than declared. That ordering paid twice over. It is what
scored the check honestly (10 of 11, by ablation against the previous seed
rather than on paper), and it is what NOTICED when the ground moved: root's
in-place text append made one corpus entry change sides, measured quadratic
x12.7 when the corpus was built and linear x3.6 afterwards, on identical
source. A hand-labelled corpus would have hidden that and gone on asserting the
old answer.

**CDX6002 was refusing nothing for punctual functions doing text
concatenation.** The check matched `OpAppend`, but `&` desugars to `OpAnd` and
only the operand type separates concatenation from logical and, so a punctual
function appending text compiled clean and collected an affirmative WCET
certificate. Fixed, with the arm and its control.

## Found by the release itself

The release gate earns its place every cycle by finding what the dev gates
cannot, and this cycle it found four things.

- **A test whose premise had died.** `bounded-exceeded` declared `bounded
  linear` over an accumulator appended with a text literal -- exactly the shape
  the in-place append made linear -- so the declaration started HOLDING and the
  arm stopped refusing. Rebuilt on a shape that still grows.
- **A wire guard that was refusing by accident.** `sdw-decode` bounded a
  length field read off the disk, and its own prose recorded the accident it
  rested on: `text-to-integer` answered a plausible large number for text that
  is not one, so a corrupt length arrived too big and the bound caught it. A
  better primitive, stopping at the first non-digit, made that field answer 0
  -- a legal length -- and the same hostile record decoded clean. Neither
  answer is a refusal. The decoder now checks the field is a number at the
  boundary the hostile input crosses.
- **Both of those had prose stating the old behaviour as measured fact**, in
  two chapters, and one compiler change made both false without touching
  either. The prose is deleted rather than corrected.
- **The release skill was wrong in two places**, and both were corrected in
  this release: its step 1 said "the full battery" where a bare invocation runs
  one tier of five, and it claimed the DDC script also runs the poison build,
  which that script has never done. The blocker above was found by the poison
  step at the full tier, which is to say by the two things the skill would have
  let a release skip.

## The release proof, at head 16148

| proof | result |
|---|---|
| Battery, `-Tier all` | 1,526 tests, 1,480 pass, 0 fail, 46 skip |
| App sweep | 265 clean, 5 known-dirty, 0 regressions |
| Poison build (0xCD fill), `-Tier all` | 1,526 tests, 1,480 pass, 0 fail, 46 skip |
| DDC witness (Roslyn arm) | both arms 2,827,487 bytes, 0 differing outside the signature |

Seed `270227BE0202EDBB`, rebuilt from the release source and byte-identical to
what the pinned source produces.
