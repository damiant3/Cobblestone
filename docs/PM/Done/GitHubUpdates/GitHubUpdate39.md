# GitHub Update 39

**Scope: main CLs 13136 to 14533, 2026-08-05 to 2026-08-10.** Update 38
covered 12647 to 13135. That is 406 changelists in six days, counted with
`p4 changes` over the range.

---

## The headline: two independent implementations now check the compiler, and they agree

A self-hosting compiler invites exactly one question, and Ken Thompson
asked it in 1984: what checks the thing doing the checking? A compiler
that compiles itself can carry anything at all, and every test it passes
is a test it graded.

This cycle answers that question twice, from two directions that share no
code, and both answers are byte-exact.

### Arm one: a compiler from a different lineage reproduces the seed

Measured at this release head, on the seed this release ships:

| | |
|---|---|
| Compiler source | 2,769,366 bytes |
| IR handed to the C# plug | 15,566,097 bytes |
| Emitted C# | 3,783,820 bytes |
| Roslyn build | 0 warnings, 0 errors |
| Roslyn arm's output | **2,755,007 bytes** |
| The shipped seed | **2,755,007 bytes** |
| Differing bytes | **96** |
| Differing bytes outside the signature region | **0** |

A compiler built by Roslyn, a toolchain with no ancestry in this project,
compiled the Codex compiler's own source and produced a CDX identical to
the shipped seed except the 96 bytes at offsets 40..135, which the sign
phase stamps rather than the compiler emitting. That is Wheeler's
`stage2 == X`.

Getting there cost a run of defects that only a byte comparison could
have found, each of them a place where the plug and the bare-metal
runtime disagreed about something neither had ever been asked to state:

- `__list_snoc` returns a NEW list when the backing array has no spare
  capacity, so `list-push [] x` leaves the original empty. The plug
  emitted an in-place `Add`. That made `tco-ensure-temps` append to its
  own temp-local list, and the second tail call in a function reused the
  first one's spill slots. Before the fix, 17 functions diverged and
  13.05 per cent of the code section agreed. After it, both arms are
  2,724,728 bytes with all 5,000 symbols identical in name, order,
  address and size.
- `__record-set` mutates in place; the plug copied.
- The plug's text model was Unicode where the compiler's is CCE.

**State the residual hole, because it is large and this is not a complete
Wheeler DDC.** The seed still sits upstream twice: it produced the IR,
and it compiled the C# plug itself. A real double-compile needs the
independent arm to do `source -> IR` on its own. What is narrowed here is
the frontend and the text emitter, on one input, with Roslyn as the
independent lineage. That is worth having and it is not the whole claim.

**The witness broke during this cycle, which is the reason it is now a
release gate.** Adding `block-write-sector` as a builtin was a change with
nothing to do with C#, and the plug's builtin table gained no entry for it,
so the emitted C# referenced a name that did not exist and Roslyn refused
the file. The compiler was correct throughout and every other proof stayed
green, which is exactly the problem: a witness nothing runs is a witness
that reports the last answer it was given. It is repaired, it is re-measured
above, and it now runs on every release rather than on demand.

### Arm two: an independently written checker re-derives what the compiler asserted

The rechecker reads the compiler's IR output as text and re-derives every
type judgement in it. It is forbidden from reading the compiler's own
judgement code, so when the two agree, two implementations agree rather
than one implementation agreeing with itself.

It began this cycle with **1,365 findings it could not decide**. It ends
with **one**.

Over the compiler and the standard library, 4,821 definitions:

| | |
|---|---|
| AGREE | 4,821 |
| DISAGREE | **0** |
| UNSUPPORTED | **0** |

The single remaining finding is an honest abstention: one integer range
inside a very large diagnostic expression in `compile-type-check` that
the checker declines to derive.

**The second product turned out to be worth more than the first.** The
checker abstains wherever the language guide is silent, so its abstention
set is not a list of its own weaknesses. It is a map of where Codex was
unspecified. Three sections of `docs/DevelopersGuide.md` were written out
of that map, each from measured arms with a control rather than
transcribed from the type checker:

- **Type Variables.** What one is, that a signature binds the variables
  it mentions at that definition, and consistent instantiation.
- **Integer literals.** A literal is typed plain `Integer`, not as a
  singleton range.
- **Variance of Type Arguments.** Ruled INVARIANT. The compiler now
  enforces it, at a measured cost of fifteen widening sites across the
  whole tree.

Two rows of the Static Bounds Prover table were **false** and are
deleted: `bit-and` and `bit-shru` were documented as proven and the
compiler refuses both. A missing row costs an independent implementation
a proof the compiler makes. A false row is the worse direction, telling
it to prove a range the compiler will not accept.

### And then it found a defect in the compiler

`lower-lambda` recorded the type it was HANDED -- at a polymorphic call,
the callee's declared parameter with its type variables still in it --
at the moment the lambda's body had been lowered and its concrete type
was sitting in a local. `lower-let`, twenty lines above, records the
resolved type.

Nothing miscompiled. But `subst-type-vars-from-arg` learns a callee's
type variables by matching its declared parameter against the argument's
recorded type, so a lambda argument matched itself, substituted every
variable for itself, and learned nothing. **The compiler resolved the
instantiation and then discarded it**, and the application's result type
reached the wire uninstantiated for every downstream consumer: this
rechecker, and every transpiler plug alike.

The fix records the lambda's actual type. No emitter change was needed,
because the emitter always printed the node's type and the node is now
right.

---

## The counterweight: a silent miscompile, and four tolerances in series

A compiler that emits wrong code and reports success is the worst thing
this project can produce, and this cycle had one.

The symptom was absurd on its face. At a deck scale of 32 the compiler
produced WRONG CODE and reported a clean compile. At 28 and below it
raised a loud `CDX9002`. At 36 and above the output was byte-identical to
the known-good build. The failing scale was the DERIVED one for 1,436 of
1,674 entry points.

The root cause: `check-all-defs` guards each definition, but the work
AFTER that walk allocates on the same deck guarded by nothing. At scale
32 it ran **9,285,768 bytes past its reservation**. The ceiling sits 4 KB
from where the bivy begins, and the bivy holds the recorded types, so the
overrun destroyed 133 of them and left the compile looking healthy.

Then four silent tolerances in series turned lost types into wrong bytes:

1. `check-ov` samples the deck BEFORE the overrunning work, so its
   window closes before the overrun opens.
2. `mcopy-sat` did a `__record-set` on a non-mutable record and dropped
   the result, so its flag read 0 on every path ever taken -- and the
   deck floor had been chosen by reasoning from that flag.
3. `mcopy-type` detects an invalid tag and forwards the pointer anyway.
4. `lower-bin-op` defaults an unrecognised operand type to the LIST
   opcode, so a Text concatenation was lowered as a list append and
   `__list_concat_many` read a Text's byte-length header as an element
   count.

Every one of those is a place where code chose to continue rather than
refuse. Individually each looks like robustness. In series they turn a
memory overrun into a program that runs and gives wrong answers.

The fix re-measures the deck after the post-loop work, answers `ErrorTy`
from the unsafe escapes so the existing fallback can run, refuses at the
append dispatch, and deletes the dead flag. The repro now REFUSES at
`-Decks 32` and is byte-identical at 36. The arithmetic arms of
`lower-bin-op` were given the same treatment: an unclassified operand is
refused rather than defaulting to the integer opcode, shipped only after
measuring that the strong refusal fires 0 times on the compiler's own
source, 0 times on the whole foreword, and once across 2,423 test and app
chapters.

---

## The compiler compiles itself through firmware

The seed has always been able to compile itself. This cycle it did so
without an operating system, without a serial port, and without any host
handing it its own source: it read the source off a UEFI block device
through firmware calls, compiled it, and wrote the result back to the
same volume.

Measured in the emulator, with the payload built `-Uefi` so both
`block-read-sector` and `block-write-sector` are firmware helpers, at
128 MB of arena: a **2,766,116-byte** source read off the volume,
compiled, and written back as **`OUT.CDX` at 2,753,312 bytes,
byte-identical to the host compile** of the same source with the same
kernel. Five minutes. Serial produced **zero bytes** -- diagnostics go to
ConOut, so nothing on this path needs a UART.

**The control is what makes that a measurement rather than a hope.** The
same image against the emulator before the fix changes 0 bytes and
produces no `OUT.CDX` at all. A payload writing over raw IDE would have
written in both cases, so the write provably went through UEFI Block I/O.

Two defects stood behind it, and the second is the more instructive:

- `uefi-systab-addr` lived at cell 36208, which is entry 430 of the PML4
  that the page-table builder puts at 0x8000 and zeroes wholesale. The
  boot stub primed that cell and the page-table build ate the write, so
  every UEFI helper dereferenced null.
- **The emulator's `WriteBlocks` shared a case label with `BLK_RESET` and
  fell through to a bare break**, returning EFI_SUCCESS and writing
  nothing. No bed could express a guest write at all, so every arm that
  depended on one had been passing for free.

**And on 2026-08-10 it was confirmed on real hardware.** On the ASUS,
read back off the medium rather than from the guest's own readback:
LBA 30000 came back holding the boot-sector copy with byte 0 replaced by
`0xA5` and the `55AA` signature intact, against `EB` at LBA 2048. Both
earlier flights had left that sector zeroed. LocateProtocol, ReadBlocks
and WriteBlocks all work under firmware after the kernel installs its own
CR3.

**Stated as plainly as the success: this proves ONE block write, not the
2.7 MB one.** The FAT sink's full write has never run on metal, and the
self-compile above is an emulator result. Both flight sticks stay
grounded until their arm is rebuilt.

---

## The desktop on real hardware, second pass

Update 38 shipped a desktop that booted from a USB stick on real
hardware. This cycle it went back to the same board and found that F12,
the screenshot key, had stopped writing after the first shot per boot.

**The guard was the regression.** A check added to protect the FAT write
path from overwriting a live cluster chain was itself refusing correct
writes. Measured on the ASUS: the pre-guard build lands two consecutive
shots with clean, non-overlapping chains, while the guarded build refused
the second one -- and no collision existed anywhere on the returned
stick.

The class that guard existed for is guarded again, by a check that cannot
repeat the failure by construction: it works from the chains the ROOT
directory claims rather than re-reading the FAT off the disk, and what it
cannot walk it leaves unmarked, so unmarked never refuses. It descends
into subdirectories, so it covers `EFI/BOOT/BOOTX64.EFI` -- the file a
root-only walk cannot see and whose loss stops the stick booting -- and
it carries the arm the original lacked: a correct multi-cluster write
that it must ADMIT, alongside the one it must refuse.

It flew on 2026-08-09 and passed. Shot 1 landed correct at 1024x768x24
and the returned volume was clean on all four questions asked of it:
chains match sizes, no overlaps, no clusters allocated to nothing, both
FAT copies identical.

**Shot 2 failed, one layer below, and that is open.** A 32 KB data-phase
transfer with no completion event, inside the USB mass-storage driver
rather than the FAT writer. Part of it is closed already: timed-out
transfers now recover and retry, which exposed three defects at once --
no recovery on timeout at all, a Reset Endpoint issued against a RUNNING
endpoint where xHCI defines it only for Halted, and Stop Endpoint's own
mandatory Stopped event left latched and taken by the next transfer as
its own completion.

Also fixed: a pane visit leaked 4,617,256 bytes permanently, and the 3D
pane polled the keyboard once per frame while a frame cost about a second
on metal, so Esc would not close it.

---

## The network stack gets a clock

`NetIO` had a defect that every consumer inherited and none could see.
`net-send` refuses a chunk when the retransmit queue is full and answers
the session unchanged. `net-io-send-chunk` never read that refusal and
advanced anyway, and nothing in the send path ever read an inbound frame,
so no ACK could ever prune the queue.

**Past 11,200 bytes every caller lost data and saw a clean close.**
Measured: a whole-compiler recheck report went from 11,200 bytes cut
mid-word to all 85,472, byte-identical to a control.

Behind it was a second finding that generalises further. `net-tick` is
what ages a connection, retransmits and declares a peer dead, and its
only production caller in the entire tree was one line of
`WebServer.codex`. Every plug, `HttpFetch`, `TrustTransport` and the
ARM64 path never retransmitted and never declared a peer dead. The loops
now turn the clock on a poll count, and each returns on a closed
connection.

Elsewhere in the stack this cycle: DTLS handshake fragmentation and
reassembly (RFC 9147), gated against **real OpenSSL fragments**,
including a refragmented retransmission our own generator cannot produce;
a multi-segment TCP retransmit queue that refuses past its bound; RFC 793
serial arithmetic so sequence numbers wrap correctly; and a retransmit
timer derived from measured RTT per RFC 6298 with Karn's algorithm.

Two gaps stay open and named: the ARM64 send path is the same defect in a
second copy and has no bed on this box, and a lost SYN is still never
retransmitted.

---

## The emulator kept passing arms it could not express

A theme worth naming on its own, because it appeared four times in six
days. Every one of these was a green test proving nothing:

- UEFI `WriteBlocks` fell through to a bare break: no guest write was
  expressible at all.
- The emulator advertised RAM above 4 GB through `GetMemoryMap` and
  allocated top-down into it, but mapped only 4 GB, so it triple-faulted
  guests on addresses its own allocator had returned.
- `load_kernel`'s memcpy ran past a 32 MB pre-commit into reserved
  address space, crashing the host on disk images above 31 MB.
- The e1000 model had no ASDE bit and no speed fields, so a diagnostic
  printed SPEED and ASDV off a register nothing wrote, and both read
  10 Mb/s on every arm ever run.

The pattern behind all four: **an instrument pointed at part of a
question, read as an answer to all of it.**

---

## The prose campaign is complete

Codex is a literate language: prose at column 2 is part of the source. In
July a measurement found 64,450 such lines across 2,601 chapters, much of
it explaining our own code to a reader who has that code in front of
them, and some of it wrong in ways nothing could catch, because no gate
reads prose.

Every chapter in the tree is now audited and recorded. **19,972 lines
survive, and every one was kept on purpose**: external formats we do not
own, magic numbers, and performance or crackability characteristics.

---

## Also in this cycle

- **The classic game engines got oracles derived from their rules.**
  Mahjong and GoFish were unplayable, Monopoly unwinnable, Spider
  oscillating, 2048 and RoyalUr inert; Go, HexGame and DotsAndBoxes
  misused in-place `list-set-at` during candidate-move search.
- **A T3ISA back end** was written as an adversarial test of the plug
  thesis: can this architecture target something it was not designed for?
- **A cons expression was typed as its ELEMENT at lowering**, so
  `(x :: xs) & ys` miscompiled silently.
- **The agent-name derivation rule** in our own tooling did not implement
  the rule as stated.

---

## Release proofs

- Full battery (`-Tier all,traps -Jobs 8`): **1,433 total, 1,402 pass,
  0 fail, 31 skipped-with-cause.** Oracles: scalar 2013/2013, vector
  130/130, CCE 1485/1516 with 31 in documented gaps and 0 unexplained.
- App sweep (`sweep-app-classes.ps1 -Check -Jobs 8`): **268 units, 262
  clean, 6 known-dirty per baseline, 0 regressions.**
- Diverse double-compiling, full run with nothing reused: the Roslyn arm's
  output is **2,755,007 bytes against the seed's 2,755,007, 96 differing
  bytes, all inside the signature region, 0 outside it.** Reproduced twice
  from a cleared state.
- Poison build (0xCD alloc fill): **BVT against the poison seed, 135 pass,
  0 fail.** Narrower than previous releases, which ran the whole battery
  against it; scoped to the BVT this cycle by Damian's call, on the grounds
  that little memory work landed and the structural defects that class used
  to catch are gone. Stated so the number is not read as more than it is.
- Seed `AF4E14D9703985AC` (2,755,007 bytes), hard fixed point in one
  pass, self-verifying.
- Compiler: 63 files, 53,881 lines of Codex.
