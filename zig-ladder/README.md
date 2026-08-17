# The zig plug's phase-oracle ladder

## What this is

**A chapter** is Codex's module: one `.codex` file, naming what it `cites`
from other chapters. The compiler is about sixty of them, and a rung's subject
is some subset concatenated into one compilable unit.

**Codex** is the language this repository implements. Its compiler is written in
Codex and emits bare-metal images, so compiling anything means booting a
machine: `ring_compile.py` runs the compiler as a QEMU kernel and feeds it
source over a serial line.

**The seed** (`seed/Codex.cdx`) is the trusted compiler binary at the root of
that. It is the authority every measurement here is compared against, and it is
also what produces the IR the plug consumes -- which is why a new seed
invalidates everything banked.

The seed is not only a compiler. It is an operating system with the compiler
inside it: it has a network stack, a FAT16 driver, a framebuffer and a process
model, all written in Codex. That is why a transport can be an oracle surface,
why `read-file-uni` means a disk, and why the compiler's `opening` has an effect
row mentioning block devices.

**The plug** (`codex/plugs/zig/ZigEmitter.codex`) transpiles Codex IR into zig
source: `prog.ir -> prog.zig`, which `zig build-exe` then makes native. Point it
at the compiler's own IR and you get the Codex compiler as an ordinary Linux
process instead of a kernel.

**This ladder tests whether that transpile is faithful**, and the answer has to
be byte-exactness rather than "it seems to work", because a transpiled compiler
that is subtly wrong produces subtly wrong binaries forever after.

What it proves is **agreement, not correctness.** Both arms descend from the
same source, so a mistake they share is a mistake the ladder cannot see -- if
the back end computed a rodata offset wrongly, the transpiled back end would
reproduce that faithfully and the diff would be empty. That is what
`ast/f4_boot.py` is for: booting the emitted binary asks a third party with no
stake in the argument.

## How a rung works

**A rung** bundles real compiler chapters into one unit -- the **subject** --
together with a generated harness. That subject is compiled twice: the seed
compiles it on bare metal, and the plug transpiles it into a zig program which
is then built and run. Both print the same thing, and the two outputs must be
**byte-identical**. A rung that passes says the transpiled compiler and the real
compiler agree about that much of the compiler.

**Banking** is recording a truth arm's output as the golden file
(`ast/<m>.truth`) that the zig arm is diffed against. **Re-banking** is doing it
again because something upstream moved.

The ladder is broadly cumulative -- each rung bundles more of the compiler than
the last -- so a failure names the phase that broke rather than "the plug is
wrong". It is not strictly a phase order, though: five rungs vary the *output*
rather than the phase set. `text` and `pingpong` emit Codex source, `fib` emits
the IR in IRTextEmitter's grammar, and `lir` and `fibx` emit machine code. So
read the **what it adds** column as "what this rung newly puts under test", not
as a position in the pipeline.

**CCE** is Codex's own character encoding, one byte per common character, and
it is what `Text` is made of everywhere inside the compiler -- on bare metal, in
the C# plug and in the zig plug alike. A target's own encoding appears at I/O
and nowhere else. **IR-CCE** is the IR text wire format in that encoding, and it
is what a plug is fed.

**A CDX** is what the Codex compiler emits: a bare-metal image in three
pieces -- a 224-byte header, the content, and a tail. The content holds the
code, the rodata, the capability and effect tables, and a debug map of symbol
names and offsets; the header says where each piece is, where to start, and
carries a sha256 of the content. A multiboot header sits immediately after the
224, so a loader will boot it. It is a kernel, not a hosted program -- running
one means booting a machine.

**The driver** is `codex/compiler/opening.codex`: the chapter holding the real
compiler's `opening`, which reads a mode line and a source unit, runs the
front end, the IR pipeline and the back end, and writes the result out. It is
what you are running when you run the Codex compiler.

**A harness stands in for the driver.** Every rung's subject carries one instead,
because two chapters cannot both define `opening` -- and because the driver is
an operating-system program, not just a compiler one: its effect row is
`[Console, FileSystem, Device.Block]`, its first two acts are painting a
framebuffer and reading a serial line, and it cites Fat16, FactDisk and
ImportGate. Standing in for it is what keeps a subject to the compiler rather
than the OS. The cost is that a harness can differ from the driver, and where it
does, that is a difference between two drivers rather than between two
compilers.

Everything generated is ignored and regenerates from a script beside it. The
scripts are the record.

## The fourteen rungs

`LADDER_RUNGS` in `ast/oracle_lib.sh` is the list, shared by the sweep and the
re-bank so they cannot disagree about what the ladder is.

| rung | subject | what it adds |
|---|---|---|
| `lex` | compiler chapters + harness | the tokenizer |
| `parse` | + parser | the parse tree |
| `desugar` | + desugarer | the desugared AST |
| `scope` | + chapter scoper | scoping |
| `check` | + type checker | inference and checking |
| `lower` | + lowering | the IR |
| `text` | + codex-text emitter | source out, not a dump: stage 1 of a fixed point |
| `pingpong` | subject = `text`'s OUTPUT | stage 2: `text1 == text2` or the serializer lies |
| `lir` | + instruction selector | machine-code bytes; the harness's functions are chosen to reach the selector's branches |
| `fib` | front end end to end | one `(def ...)` line per definition, in IRTextEmitter's own grammar |
| `fibx` | + the whole x86 back end, through `finalize` | a complete CDX binary, symbol map and all |
| `scale` | subject = `CCE.codex` verbatim | capacity. 61 IR defs against fib's 3 |
| `whole` | every chapter but `opening.codex` | the middle end too, and it must actually run (see below) |
| `clamp` | subject = `plug-oracle-arith` | the **error** path -- the one rung whose subject does not compile cleanly |

Two of those need a word.

**`whole` runs the IR pipeline.** IR emission prunes to what the opening
reaches, so bundling `Simplify`, `Occurrence` and `LambdaLifting` without
*calling* `run-ir-pipeline` prunes them straight back out. The harness calls it
exactly where `compile-frontend-passes` does. Its subject is also chosen to make
the passes do something: dropping the inline passes moves `scale-by-four` from
22 bytes to 29, so a broken inliner fails the rung instead of passing it.

**`clamp` exists because every other rung exercises the success path.** Its
subject produces emit errors, which is how the diagnostic accounting got tested
at all -- see `deck-record-repro/README.md`, finding 11.

## The two arms, and the two transports

Every rung has a **truth arm** (`ast/truthcycle_<m>.sh`) and a **zig arm**
(`ast/<m>cycle.sh`). The transport differs; the verdict never does.

- **TCP** (`plug_run_checked.py`) is the default. It exercises the Codex-written
  network stack on every run, which is itself an oracle surface -- it is where
  the odd-frame defect was caught. Two transfers at different chunk sizes must
  agree byte-for-byte before an output is trusted.
- **The ring** (`plug_run_ring.py`) is for subjects past the TCP intake ceiling.
  The TCP receive path costs ~130 bytes of guest heap per byte of IR; the
  compiler's own serial-ring reader costs one. fibx's IR is 13 MB, so it has no
  choice.

`arm_for` in `ast/oracle_lib.sh` decides, because which transport a rung needs
is a property of the rung. `fibx`, `scale`, `whole` and `clamp` take the ring.

## Scripts

- `ast/allcycles.sh` -- rebuild **both plugs** (there are two, built from the
  same `ZigEmitter`: one fed over TCP, one fed through the serial ring -- see
  the transports above; refreshing only one would report yesterday's emitter for
  whichever rungs use the other), then sweep every rung. The guard
  against fixing one rung and breaking four. A rung whose output contains
  neither `ORACLE` nor `TRANSPORT FAILED` is failed. `ORACLE` matches both
  `ORACLE PASS` and `ORACLE DIFF`, so the rule is not catching failure -- it is
  catching **silence**, a rung that produced no verdict at all. One did once,
  and read exactly like a rung that never ran.
- `ast/rebank_all.sh` -- re-bank every truth arm, then sweep. **Run this after
  any seed change**: a new seed invalidates both arms, since it compiles the
  truth binary *and* produces the IR-CCE the plug consumes. Ordered cheapest
  rung first and stops on the first failure, because the failure modes are
  shared.
- `ast/truthcycle_<m>.sh` / `ast/<m>cycle.sh` -- one rung, one arm.
- `ast/plugcycle.sh <m>` -- rebuild and run one rung, reporting markers grepped
  from the emitted zig. Error counts under-report: zig stops at the first
  `@compileError`.
- `cycle.sh` -- rebundle the zig plug, ring-compile it, run the warmup oracles.
- `ring_compile.py` -- compile through the seed under QEMU via the codex-vm ring
  contract. Blobs larger than the 1 MB ring stream through it: the host refills
  behind the guest's read cursor over the gdbstub. `ring_refill_test.sh` is that
  path's oracle.
- `codex_vm.py` -- launch/READY/run helpers shared by the above.

## Consumers of what the ladder emits

The rungs prove the plug emits the same bytes. These ask whether the bytes mean
anything.

The `f` numbers are milestones of the fib ladder: F1 was fib through the front
end, F2 was fib through the x86 back end (the `fibx` rung), F3 runs the emitted
code, F4 boots the emitted binary.

- `ast/f3_run.zig` -- carves a function out of an emitted CDX, drops it in RWX
  memory and calls it. `fib(30) = 832040`, from both the truth dump and the zig
  dump. Works because the emitted code uses the System V ABI and buffer-relative
  call displacements.
- `ast/f4_boot.py` -- reassembles the dump into a real CDX the way
  `emit-binary-tail` does (header, content, tail), boots it, and checks it
  prints what its subject says. Six binaries: fibx, scale and whole, from truth
  and from zig.
- `native_build.sh` -- builds `codexir` (.codex -> .ir) and `zigemit`
  (.ir -> .zig), the two tools that take QEMU out of the pipeline entirely.

### zigc: the compiler as an ordinary process

`ast/ZigcHarness.codex` is the `whole` rung's chapter set with a real I/O
boundary instead of a baked-in Text literal and a decimal dump: **source in on
stdin, a CDX binary out on stdout.** Built the same way every rung is -- bundle,
compile to IR with the seed, transpile through the plug -- and then
`zig build-exe` on the result.

    $ ast/gen_zigc_harness.py && pwsh ast/bundle_zigc.ps1   # write and bundle it
    $ # ...then compile that subject to IR with the seed and push it through the
    $ # plug, exactly as a rung's two arms do, leaving ast/zigc.zig
    $ zig build-exe ast/zigc.zig -femit-bin=zigc      # 16,905 lines of zig, no
                                                      # @compileError markers, ~4s
    $ ./zigc < ast/repro-mid.codex > mid.cdx          # ~3s
    $ ls -l mid.cdx
    -rw-r--r-- 1 steve steve 87257 mid.cdx

**What it emits is a CDX, not an ELF**, because it is a port of the Codex
compiler and emitting a bare-metal image is what that compiler does. So the
output still wants a machine to boot it:

    $ python3 -c "import codex_vm; codex_vm.run_cdx('mid.cdx')"
    program output (0s):
      > 276

For that subject the 87,257 bytes were byte-identical to what the seed produces
for the same source, and the binary booted and printed the same answer. Roughly
three seconds against about a minute for the equivalent VM round trip.

Two things it is not:

- **Not the driver.** It stands in for `opening.codex` and skips what that
  chapter does around emission -- proof pruning, dropped-def handling, mode
  flags. For subjects that need none of it the output matches the seed's byte
  for byte; for subjects that do, it legitimately differs, and comparing the two
  is comparing two different drivers rather than two compilers.
- **Not a native-code compiler.** `zigc` runs natively; its *output* is still a
  kernel image. Compiling a Codex program to something Linux runs directly is
  the other axis -- transpile the program itself through the plug
  (`prog.codex -> prog.ir -> prog.zig -> zig build-exe`), which is what
  `native_build.sh`'s two tools automate.

## Harnesses

`ast/gen_<m>_harness.py` writes `ast/<M>Harness.codex`. The four rungs that run
the back end all the way to a CDX -- `fibx`, `scale`, `whole` and `clamp` --
share `ast/emit_harness.py`, as do `zigc` and `codexir`. It holds the compile
pipeline once -- `frontend_source` (source text to a lowered IR) and
`pipeline_source` (that plus the x86 emission) -- because six generators run
that sequence and it must not drift between them.

Each harness stands in for the driver, as above. `whole` and `zigc` come
closest to it -- they run the same phases in the same order -- and the places
they still differ (proof pruning, dropped-def handling, mode flags) are the
places their output can legitimately differ from the seed's.

## Running it

Paths below are relative to this directory; the bundlers and rung scripts live
in `ast/`, the transports and VM helpers at the top level.

    ast/truthcycle_lex.sh     # one rung's truth arm: bundle, compile, bank
    ast/lexcycle.sh           # the same rung through the plug, diffed
    ast/allcycles.sh          # rebuild both plugs, sweep all fourteen
    ast/rebank_all.sh         # re-bank every truth arm, then sweep

A passing rung prints one line:

    ORACLE PASS: zig lex output byte-identical to bare-metal truth

Costs, measured: the ten cheap rungs bank in one to four minutes each. The four
ring rungs -- `fibx`, `scale`, `whole`, `clamp` -- are the expensive ones, about
twelve minutes to bank and about fourteen through the plug, so a full sweep is
somewhere over an hour and a full re-bank plus sweep is a few hours. Run them in
the background and watch for the markers above.

## Operating rules

1. **Sweep after any emitter change.** `ast/allcycles.sh`. One rung passing
   proves nothing about the other thirteen.
2. **Re-bank after any seed change.** `ast/rebank_all.sh`, before any diff means
   anything.
3. **Validate a new subject standalone first.** Compile it through the seed on
   its own (about a minute) before spending a full cycle -- twelve minutes to
   bank plus fourteen through the plug, for the expensive rungs -- discovering
   it does not compile.

The bundlers are PowerShell (`ast/bundle_<m>.ps1`), because they call the
repository's own `plug-build-lib.ps1` to resolve chapter cites -- that is why
pwsh is a requirement here.

Requires: qemu-system-x86_64, pwsh, python3, zig 0.16. Paths derive from script
locations; nothing assumes a checkout directory.
