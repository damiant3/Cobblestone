# The zig plug's phase-oracle ladder

## What this is banked against

| | |
|---|---|
| Seed | `270227BE0202EDBB` (2,827,487 bytes) |
| Update | 45 |
| Rungs | 13 of 14 green |
| Red | `clamp` -- page fault in `__list_snoc`; cause found, fix in test |

This table is the point of the whole arrangement, so it is the first thing on
the page and it is allowed to be unflattering. A ladder that cannot say which
seed it agrees with is not evidence about anything.

The Update is not typed here by hand -- `seed_identity.py` derives it from the
seed's own hash by finding the release note that names it, so the label cannot
drift from the binary. Run it to see what a checkout is actually holding.

## What this needs, and what it does not

**It needs no changes to the Codex repository.** Everything Linux and QEMU
specific lives here. The driver that boots the guest, `codex_vm.py`, is ours: it
launches QEMU directly and re-implements the contracts the author's `codex-vm`
host provides (the guest RAM size written at physical `0xFE8` before boot, the
ring preload, the paced serial send) rather than calling `Start-VmRun` in
`build/vm-config.ps1`. Every plug defect the ladder found was fixed in
`ZigEmitter.codex` and carried upstream, so a checkout needs no patch to run
this. That claim is checkable, and it should stay empty:

    git diff upstream/master HEAD -- . ':(exclude)zig-ladder'

**From the checkout, tracked and used unmodified:** `seed/Codex.cdx`,
`build/concat-codex-self.ps1`, `codex/plugs/common/plug-build-lib.ps1`, the
chapters under `codex/compiler/`, and `codex/test/plug-oracle-arith.codex` with
its `.expected`.

**From the checkout, NOT tracked:** `codex/plugs/zig/build-output/zig-plug.cdx`
and `plug-source.codex`. These are products of the author's gated PowerShell
build, not source, so a fresh clone does not have them and the rungs that use
them fail on a missing file. Run that build once before the first sweep.

**From the host:** `qemu-system-x86_64` (6.2.0), `python3` (3.10.12), PowerShell
(7.5.4, for the bundlers, which are the author's tooling), and `zig` (0.16.0)
for the arm under test. `/dev/kvm` is optional: `CODEX_ACCEL` selects the
accelerator and the default is `tcg`.

Two known warts, both the same shape: three scripts hardcode
`~/.local/pwsh/pwsh`, and until every call site moves to `ladder_root.py` some
scripts still find the checkout by counting directories up from themselves.

## What this is

This directory holds a Diverse Double-Compiling check, in Wheeler's sense:
take one piece of source, compile it two ways through toolchains of unrelated
lineage, and require the two results to agree byte for byte. If they disagree,
one of the two toolchains is wrong. The check here is built in fourteen steps,
called rungs, each putting more of the compiler under test than the last.

Four things to know before anything else.

**Codex** is the language this repository implements. Its compiler is written
in Codex and emits bare-metal images, so compiling anything means booting a
machine: `ring_compile.py` runs the compiler as a QEMU kernel and feeds it
source over a serial line.

**The seed** (`seed/Codex.cdx`) is the trusted compiler binary at the root of
that. It is the authority every measurement here is compared against, and it is
also what produces the IR the plug consumes -- which is why a new seed
invalidates everything banked.

**The plug** (`codex/plugs/zig/ZigEmitter.codex`) transpiles Codex IR into zig
source: `prog.ir -> prog.zig`, which `zig build-exe` then makes native. Point it
at the compiler's own IR and you get the Codex compiler as an ordinary Linux
process instead of a kernel.

**A rung** is one test. It bundles part of the compiler into a single
compilable unit -- the **subject** -- and compiles that subject twice. The
first compile is the real one: the seed, on bare metal, under QEMU. The second
goes through the plug: the same subject, transpiled to zig, built by
`zig build-exe`, run as a Linux process. Both print the same thing, and the two
outputs must be **byte-identical**. A rung that passes says the transpiled
compiler and the real compiler agree about that much of the compiler.

The ladder exists to test whether the transpile is faithful. The answer has to
be byte-exactness rather than "it seems to work", because a transpiled compiler
that is subtly wrong produces subtly wrong binaries forever after.

## What the check proves, and what it does not

Be careful about how much DDC to claim here. Two things are true, and both
matter.

**The code generation on the zig arm has genuinely independent lineage.** The
bytes of the zig arm's executable are produced by the zig toolchain, which has
nothing to do with Codex -- not its back end, not its seed, not anything in this
repository. Worth being specific, because it is better than it sounds: these
rungs build with a plain `zig build-exe`, and zig 0.16 does not use LLVM for
that. It uses its own x86-64 back end. (Measured, not assumed: the same source
built with `-fllvm` produces a different binary of a different size.) So the
machine code on the two arms comes from two independently written x86-64 code
generators, and a third is one flag away if we ever want it.

When the two arms agree on the output of a compiler phase, that
agreement crossed a real toolchain boundary. This is the DDC property, and it is
the reason the ladder is worth running.

**The seed is still in the loop.** The plug consumes IR, and it is the seed that
compiles the subject down to that IR. So both arms pass through the seed before
they diverge. The seed has not been removed, and this ladder on its own is
therefore not a complete DDC witness -- it is a DDC check over the part of the
pipeline that comes after the IR.

And the caveat that outranks both:

What it proves is **agreement, not correctness.** Both arms descend from the
same source, so a mistake they share is a mistake the ladder cannot see -- if
the back end computed a rodata offset wrongly, the transpiled back end would
reproduce that faithfully and the diff would be empty. That is what
`ast/f4_boot.py` (below) is for: booting the emitted binary asks a third party
with no stake in the argument.

## The parts

**A chapter** is Codex's module: one `.codex` file, naming what it `cites` from
other chapters. The compiler is about sixty of them, and a rung's subject is
some subset concatenated into one compilable unit.

**The seed is not only a compiler.** It is an operating system with the compiler
inside it: it has a network stack, a FAT16 driver, a framebuffer and a process
model, all written in Codex. That is why a transport can be an oracle surface,
why `read-file-uni` means a disk, and why the compiler's `opening` has an effect
row mentioning block devices.

**CCE** is Codex's own character encoding, one byte per common character, and it
is what `Text` is made of everywhere inside the compiler -- on bare metal, in
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
compiler's `opening`, which reads a mode line and a source unit, runs the front
end, the IR pipeline and the back end, and writes the result out. It is what you
are running when you run the Codex compiler.

## Anatomy of a rung

### The subject and its harness

A rung's subject is usually a set of real compiler chapters, concatenated. Three
rungs use something else -- `pingpong` takes another rung's output, `scale` takes
a single chapter verbatim, `clamp` takes a small program written to fail -- but
the shape is the same either way: one compilable unit, compiled twice.

**A harness stands in for the driver.** Every rung's subject carries one instead,
because two chapters cannot both define `opening` -- and because the driver is
an operating-system program, not just a compiler one: its effect row is
`[Console, FileSystem, Device.Block]`, its first two acts are painting a
framebuffer and reading a serial line, and it cites Fat16, FactDisk and
ImportGate. Standing in for it is what keeps a subject to the compiler rather
than the OS.

The cost is that a harness can differ from the driver, and where it does, that
is a difference between two drivers rather than between two compilers. The
places they differ are the places their output can legitimately differ from the
seed's: proof pruning, dropped-def handling, mode flags. `whole` and `zigc` come
closest to the driver -- they run the same phases in the same order.

### The two arms

Every rung has a **truth arm** (`ast/truthcycle_<m>.sh`), which is the seed on
bare metal, and a **zig arm** (`ast/<m>cycle.sh`), which is the plug's output
built and run natively.

**Banking** is recording a truth arm's output as the golden file
(`ast/<m>.truth`) that the zig arm is diffed against. **Re-banking** is doing it
again because something upstream moved -- most often a new seed, which
invalidates both arms at once.

## The fourteen rungs

Every rung carries a generated harness, including the ones whose subject cell
names a file: `scale`'s subject is `CCE.codex` verbatim *plus* a small driver,
because a subject with no `opening` cannot be run. Where a cell says
"subject = X", read it as "X is what this rung compiles", not "X is the whole
unit".

`LADDER_RUNGS` in `ast/oracle_lib.sh` is the list, shared by the sweep and the
re-bank so they cannot disagree about what the ladder is.

The subject column is cumulative: `+ parser` means everything above it plus the
parser. Where a rung replaces the subject outright rather than extending it, the
cell says so.

| rung | subject | what it newly puts under test |
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
| `whole` | every chapter but `opening.codex` | the middle end too, and it must actually run (below) |
| `clamp` | subject = `codex/test/plug-oracle-arith.codex` | the **error** path -- the one rung whose subject does not compile cleanly |

The ladder is broadly cumulative, so a failure names the phase that broke rather
than "the plug is wrong". It is not strictly a phase order, though: five rungs
vary the *output* rather than the phase set. `text` and `pingpong` emit Codex
source, `fib` emits the IR in IRTextEmitter's grammar, and `lir` and `fibx` emit
machine code. That is why the third column reads "what it newly puts under
test" and not "where this sits in the pipeline".

Two rungs need a word.

**`whole` runs the IR pipeline.** IR emission prunes to what the opening
reaches, so bundling `Simplify`, `Occurrence` and `LambdaLifting` without
*calling* `run-ir-pipeline` prunes them straight back out. The harness calls it
exactly where `compile-frontend-passes` does. Its subject is also chosen to make
the passes do something: dropping the inline passes moves `scale-by-four` from
22 bytes to 29, so a broken inliner fails the rung instead of passing it.

**`clamp` exists because every other rung exercises the success path.** Its
subject produces emit errors, which is how the diagnostic accounting got tested
at all -- see `deck-record-repro/README.md`, finding 11.

Four rungs -- `fibx`, `scale`, `whole` and `clamp` -- are the ones that run the
back end all the way to a CDX. They are also the four that need the ring
transport, and the four that are expensive to run. All three facts follow the
same boundary.

## The two transports

The transport is how a subject's IR gets into the guest. It differs between
rungs; the verdict never does.

- **TCP** (`plug_run_checked.py`) is the default. It exercises the Codex-written
  network stack on every run, which is itself an oracle surface -- it is where
  the odd-frame defect was caught. Two transfers at different chunk sizes must
  agree byte-for-byte before an output is trusted.
- **The ring** (`plug_run_ring.py`) is for subjects past the TCP intake ceiling.
  The TCP receive path costs ~130 bytes of guest heap per byte of IR; the
  compiler's own serial-ring reader costs one. fibx's IR is 12.9 MB on the current seed, so it has no
  choice.

`arm_for` in `ast/oracle_lib.sh` decides, because which transport a rung needs is
a property of the rung. `fibx`, `scale`, `whole` and `clamp` take the ring.

There are two plugs, built from the same `ZigEmitter`: one fed over TCP, one fed
through the serial ring. Both have to be rebuilt together, or a sweep reports
yesterday's emitter for whichever rungs use the other one.

## Running it

Paths below are relative to this directory; the bundlers and rung scripts live
in `ast/`, the transports and VM helpers at the top level.

    ast/truthcycle_lex.sh     # one rung's truth arm: bundle, compile, bank
    ast/lexcycle.sh           # the same rung through the plug, diffed
    ast/allcycles.sh          # rebuild both plugs, sweep all fourteen
    ast/rebank_all.sh         # re-bank every truth arm, then sweep

A passing rung prints one line:

    ORACLE PASS: zig lex output byte-identical to bare-metal truth

**`ast/allcycles.sh`** rebuilds both plugs and then sweeps every rung. It is the
guard against fixing one rung and breaking four. A rung whose output contains
neither `ORACLE` nor `TRANSPORT FAILED` is failed. `ORACLE` matches both
`ORACLE PASS` and `ORACLE DIFF`, so the rule is not catching failure -- it is
catching **silence**, a rung that produced no verdict at all. One did once, and
read exactly like a rung that never ran.

**`ast/rebank_all.sh`** re-banks every truth arm and then sweeps. Run it after
any seed change: a new seed invalidates both arms, since it compiles the truth
binary *and* produces the IR-CCE the plug consumes. It is ordered cheapest rung
first and stops on the first failure, because the failure modes are shared.

Then the smaller pieces:

- `ast/truthcycle_<m>.sh` / `ast/<m>cycle.sh` -- one rung, one arm.
- `ast/plugcycle.sh <m>` -- rebuild and run one rung, reporting markers grepped
  from the emitted zig. Error counts under-report: zig stops at the first
  `@compileError`.
- `cycle.sh` -- rebundle the zig plug, ring-compile it, run the warmup oracles -- hello, recurse and fib, three small programs with
  banked answers, which check the plug end to end before any rung is worth
  running.
- `ring_compile.py` -- compile through the seed under QEMU via the codex-vm ring
  contract. This is the compile path, not the transport of the same name. Blobs
  larger than the 1 MB ring stream through it: the host refills behind the
  guest's read cursor over the gdbstub. `ring_refill_test.sh` is that path's
  oracle.
- `codex_vm.py` -- launch/READY/run helpers shared by the above.

Costs, measured: the ten cheap rungs bank in one to four minutes each. The four
ring rungs are the expensive ones, about twelve minutes to bank and about
fourteen through the plug. The cheap rungs go through the plug in well under a
minute each, so the four ring rungs dominate everything. Measured end to end:
banking the last three rungs and then sweeping all fourteen took **110
minutes**. Run them in the background and watch for the
markers above.

## Operating rules

1. **Sweep after any emitter change.** `ast/allcycles.sh`. One rung passing
   proves nothing about the other thirteen.
2. **Re-bank after any seed change.** `ast/rebank_all.sh`, before any diff means
   anything.
3. **Validate a new subject standalone first.** Compile it through the seed on
   its own (about a minute) before spending a full cycle -- twelve minutes to
   bank plus fourteen through the plug, for the expensive rungs -- discovering
   it does not compile.

## Consumers of what the ladder emits

These are run by hand, not by `allcycles.sh`; the sweep's cost covers the
fourteen rungs only.

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
  `emit-binary-tail` does (header, content, tail), boots it, and checks it prints
  what its subject says. Six binaries: fibx, scale and whole, from truth and from
  zig. This is the one check that does not depend on the two arms sharing a
  mistake.
- `native_build.sh` -- builds `codexir` (.codex -> .ir) and `zigemit`
  (.ir -> .zig), the two tools that take QEMU out of the pipeline entirely.
  `zigemit` is not a second implementation: it is the same `ZigEmitter`, bundled
  with a four-line body that reads stdin and writes stdout instead of the ring
  plug's serial framing, then transpiled and built like anything else here.

## zigc: the compiler as an ordinary process

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

## Generated files

`ast/gen_<m>_harness.py` writes `ast/<M>Harness.codex`. The four rungs that run
the back end all the way to a CDX -- `fibx`, `scale`, `whole` and `clamp` --
share `ast/emit_harness.py`, as do `zigc` and `codexir`. It holds the compile
pipeline once -- `frontend_source` (source text to a lowered IR) and
`pipeline_source` (that plus the x86 emission) -- because six generators run
that sequence and it must not drift between them.

The bundlers are PowerShell (`ast/bundle_<m>.ps1`), because they call the
repository's own `plug-build-lib.ps1` to resolve chapter cites. That is why pwsh
is a requirement here.

Everything generated is ignored and regenerates from a script beside it. The
scripts are the record.

## Requirements

qemu-system-x86_64, pwsh, python3, zig 0.16. Paths derive from script locations;
nothing assumes a checkout directory.

## Open questions

- The `zigc` transcript below elides its middle step ("...then compile that
  subject to IR with the seed and push it through the plug"). `native_build.sh`
  does that for `codexir` and `zigemit`; `zigc` has no such script, so the
  transcript is not reproducible as written.
- `clamp`'s paragraph points at finding 11 in `deck-record-repro/README.md`
  without saying what it is. That finding is currently being re-measured and
  may not survive, so it is deliberately not summarised here yet.
- **Could the seed be taken out of the loop entirely, making this a complete
  DDC witness?** Today both arms pass through the seed, because it is the seed
  that compiles a subject down to the IR the plug consumes. `codexir` and
  `zigemit` are the beginning of an answer -- a native compiler emitting IR and
  a native plug consuming it -- but nothing yet establishes that a
  seed-independent chain produces the same IR, which is the claim that would
  matter.
