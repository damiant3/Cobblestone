# Cobblestone -- Technical Details

**The measured claims behind [README.md](README.md): what is verified, how it
is verified, and every number beside the date it was measured.**

Cobblestone is the path; Codex is the stone it is paved with. Cobblestone
is the substrate: the operating system, the desktop, the network stack,
the trust lattice, the tools. Codex is the language and the compiler
beneath all of it -- **the first self-sustaining compiler**.

A *self-sustaining compiler* is a single artifact that, on its own,
runs on bare metal, takes its own source as input, and produces another
instance of itself -- with no external host, no separate operating system,
no runtime to install. The compiler *is* the runtime. The compiler *is*
the OS. They are not two things cohabiting; they are one entity.

The operational test:

> Hand me the compiler artifact. Nothing else. Can I do anything with it?

| Artifact | Can I do anything with just this? |
|---|---|
| gcc / rust / ghc / ocaml | **No.** Need Linux/macOS/Windows. |
| HolyC binary (TempleOS) | **No.** Need TempleOS underneath. |
| Oberon compiler | **No.** Need the Oberon System. |
| Lisp Machine compiler | **No.** Need the Lisp Machine environment. |
| Forth interpreter | I can run Forth code, but it can't reproduce itself. |
| **Codex.cdx** | **Yes.** Boot it. Compile its own source. Get another copy of itself. |
| **Codex.img** | **Yes.** Write it to a USB stick. Put it in your computer. It runs. |

That property -- *the artifact alone is sufficient* -- has not been
achieved by any prior compiler. Self-hosting (the compiler can compile
itself) has been done thousands of times. Self-sustainment of a *system*
(TempleOS, Native Oberon, Lisp Machines) has been done a handful of
times -- but in each case the compiler is one component within an
operating system, not the whole. Cobblestone collapses that distinction. The
compiler-as-binary *is* the OS, not because they're packaged together,
but because there is no internal seam between them.

Built solo by one human in collaboration with a fleet of AI agents, in
41 days (2026-03-14 to 2026-04-24).

---

## Verified

Measured 2026-08-03, except where an item gives its own date.

1. **The compiler is a hard fixed point of itself on bare metal**
   (measured 2026-09-10). Current seed `48EB5C6C43515592` was built from
   depot seed `7574D00206D4069C`. Scratch CDX stages 2 and 3 are whole-file
   byte-identical. BVT: 79 compile and 64 runtime passes, plus the batch
   control. The signed seed verifies itself. Focused controls cover declared
   wasm exports on both IR wires, dead-code pruning, invalid declarations
   and fishtank fallback. The text round-trip remains a release check.

2. **Two independent implementations check the compiler, and they agree**
   (measured 2026-08-10). A fixed point proves the seed is *stable*, not
   that it is *honest*: a compiler carrying a Thompson trojan is a
   perfectly stable fixed point too, recognising its own source and
   re-injecting the payload into every compiler it emits, and no amount of
   self-consistency or reproducible-build determinism can see it. Two
   things answer that, from directions that share no code.

   **Diverse double-compiling.** The whole compiler is rendered to C# by a
   plug, built by Roslyn -- a toolchain with no ancestry in this project --
   and that compiler then compiles the Codex compiler's own source.
   Measured 2026-09-10 against Update 58 seed `F9165E313BE16815`,
   its output was **3,358,838 bytes against that seed's
   3,358,838, with 96 differing bytes, every one of them inside the
   signature region at offsets 40..135 and none outside it.** The signature
   is stamped by the sign phase rather than emitted by the compiler. That is
   Wheeler's `stage2 == X`. The witness is a release gate and is re-run
   against whatever seed a release ships, so this figure names the run it
   came from rather than standing in for any later one. The carry/borrow
   repair has no new diverse-compilation proof. How many bytes
   differ INSIDE the signature region is not a criterion: 96 is the width
   of the region, and two unrelated signatures agree at a given byte about
   one time in 256, so a run differing in 95 or 96 is equally ordinary.
   **And the witness has been shown to fail.** A check that
   has only ever come back green proves nothing, so we poisoned the
   compiler on purpose. Inject a payload into the code generator: the
   witness goes red by 2.2 million bytes and reconstructs the honest
   compiler. Inject one into constant folding instead: red again. In both
   cases stage2 lands exactly on the untampered seed and exposes the
   poisoned build, and in both the trojan existed only in the compiler
   binary and in no source in the tree. **The measured boundary is
   whether the payload reaches the readable intermediate, not whether it
   self-reproduces** (corrected 2026-08-11 by a third arm). Any trojan
   that lives only in the binary is caught, anywhere in the compiler,
   because the double-compile rebuilds it from clean source. A payload
   survives the byte-comparison only if the poisoned compiler writes it
   into the IR that becomes the C# -- measured with a hook that emits one
   function's trojaned IR verbatim: stage2 then matches the poisoned
   build byte for byte.

   **And that is the neutralisation, not a leak.** A survivor is, by
   construction, sitting as readable text in the IR and in the emitted C#
   (the injected bytes were greppable in both), so it can be diffed across
   re-emissions and content-addressed. Thompson's attack depends on the
   payload being invisible because a binary is unreadable; here a trojan
   hidden in the binary is caught by the rebuild, and a trojan that survives
   the rebuild is not hidden. The witness runs on every release, because it
   is the only proof here that does not take the compiler's word for
   anything.

   **An independently written rechecker.** It reads the compiler's IR
   output as text and re-derives every type judgement in it, and is
   forbidden from reading the compiler's own judgement code -- so agreement
   means two implementations agree, not that one agrees with itself. Over
   the compiler and the standard library, **4,821 definitions: zero
   disagreements**, and one honest abstention, down from 1,365 at the start
   of the cycle. Its abstention set turned out to be a map of where the
   *language* was unspecified rather than a list of the checker's
   weaknesses, and three sections of the language guide were written from
   it. It also found a real defect in the compiler: `lower-lambda` recorded
   the type it was handed rather than the type it had just resolved, so the
   compiler worked out a lambda's instantiation and then discarded it.

3. **The safety claims are compiler-enforced, not aspirational.** Effect
   rows are first-class inferred data checked at every boundary; linear
   ownership follows through moves, calls, captures and containers with
   all nine laundering routes closed; bounded-integer parameters and
   returns are checked statically and dynamically at the function
   boundary; hardware access carries `Device.Port`/`Device.Block`/
   `Device.Mmio` capability effects. Each was landed by writing the
   program that *should* be rejected, confirming the hole, then closing
   it and pinning the rejection as a `.failing` test.
4. **Dependent types with machine-checked proofs.** `===` in type
   position produces propositional equality; `Refl` is verified by the
   unifier. Structural induction with per-constructor subgoals; the
   flagship proof is `reverse (reverse xs) === xs` through a four-lemma
   chain. All proofs erase at emit -- zero machine code, zero runtime cost.
5. **Declared cost, enforced by the compiler: `punctual` for time,
   `bounded` for allocation.** The `punctual` keyword promises a worst-case
   execution budget -- it rejects heap allocation, recursion, closures, any
   effect in the signature, and calls to non-punctual functions, and reports
   an instruction count per function against an optional budget. No
   production language has that combination: Ada Ravenscar is global and
   needs external WCET tools, Rust has nothing, MISRA-C is external linters.

   `bounded` says the other half. It declares a CEILING on what a function
   allocates -- `none < fixed < linear < growing` -- and the compiler infers
   the class from the body and refuses when it exceeds the ceiling. The
   refusal is transitive, which is the whole point: this function is linear
   where you read it and quadratic underneath, and nothing said so before.
   **This is the first slice: `linear` and `growing` are inferred and
   checked; `none` and `fixed` are named rungs the compiler does not infer
   yet and refuses to accept (CDX6103) rather than take on trust.**

   ```
     bounded linear unpack-text : List Integer -> Text
     unpack-text (bs) = unpack-go bs 0 ""

     unpack-go : List Integer, Integer, Text -> Text
     unpack-go (bs) (i) (acc) =
       if i == list-length bs then acc
       else unpack-go bs (i + 1) (acc & byte-to-text (list-at bs i))
   ```

   Before, that compiled and ran, and quietly copied its accumulator once per
   byte until the arena ran out. Now:

   ```
     CDX6101: 'unpack-text' declares bounded linear but is inferred growing:
              an accumulator is copied by & inside a self call, here or in
              something it calls
   ```
6. **601 library modules across 22 quires** (439 foreword + 162 OS): data
   structures, crypto, a full TCP/IP stack with TLS 1.3 and X.509 peer
   verification, 3D and game engines, AI inference, encoding, math,
   compression, a themeable UI toolkit, and hard real-time primitives.
7. **A bare-metal OS and GUI.** Preemptive scheduler, IPC, Ed25519
   identity, trust lattice, 5-phase CDX verifier, shell and debugger;
   a GOP-framebuffer desktop with a compositor, TrueType rendering and
   SMP-aware app rendering across cores. Proven on real hardware
   2026-08-05: the desktop boots from USB on a consumer board and runs
   with keyboard, mouse, click-driven panes, shutdown, and
   screenshot-to-stick, all through the tree's own drivers. The network
   followed on 2026-08-21, on the same board's Intel I219: the stack
   brought the part up, dialled the development machine over TCP and got
   its 13 bytes echoed back unchanged, connect to close, with the driver
   as shipped in this seed. SMP is
   complete for x86-64: atomics, AP bootstrap via SIPI, work-stealing
   scheduler, per-core heap isolation, IPI and lock-free channels.
8. **56 plugs, all building clean.** Emitters are standalone CDX programs
   that consume the compiler's IR text: 31 languages, 14 UI frameworks,
   three GPU targets (PTX, SPIR-V, WGSL) and four binary formats (CDX,
   ELF, PE, IMG). A new target is a plug, not a compiler change.

   That last sentence was tested against a target chosen for sharing almost
   none of the assumptions the others were built under. **T3ISA is a
   balanced-ternary machine**: 27-trit words, no sign bit, arithmetic that
   saturates instead of wrapping, word-addressed memory, three-valued logic
   and strings that are not addressable at all. The plug is 2,262 lines of
   Codex, 1,466 of them code, and **the compiler was not touched** -- programs
   compiled both
   ways give identical output on x86-64 and on the ternary emulator,
   covering integers, control flow, formatted output, records, variants and
   pattern matching. Reals, vectors and wrapping overflow are refused by
   name rather than approximated: IEEE 754 is binary by construction and
   wrapping is defined by a binary modulus, so those are the measured edge
   of the claim.
   `docs/Designs/Done/Compiler/T3IsaPlug.md` has the account.

   T3ISA is designed and specified by Manish Jagdish Thatte. Our plug is an
   independent implementation written from the published specification
   alone, against `t3isa-spec-v1.3`:
   [docs/t3isa-reference.md](https://github.com/manishthatte/maniTC/blob/main/docs/t3isa-reference.md)
   in [manishthatte/maniTC](https://github.com/manishthatte/maniTC).
9. **Cross-architecture parity.** ARM64 and RISC-V backends run the test
   battery on Renode and QEMU, and the two agree. This is a claim of
   *parity*, not correctness: a known failure residue remains on both
   lanes, and the per-lane counts have not been re-measured since June.
10. **Nine target boards with register-level drivers**, addresses taken
   from official reference manuals, each with a smoke test on the hosted
   VM: STM32F4, STM32L4, ESP32-C6, Raspberry Pi 4, nRF52840, nRF9160,
   RP2040, FE310 and QEMU virt. Above them sits an industrial protocol
   library -- MQTT v5, CoAP, LwM2M and OTA update, plus Modbus, DNP3,
   BACnet/IP, KNX, J1939, CANopen, OPC UA, LoRaWAN, Zigbee and more --
   each with byte-exact known-answer tests against an independent
   reference encoder.
11. **EU compliance evidence is a build artifact.** `ComplianceEvidence`
    maps 60 regulatory requirements across CRA Annex I, ETSI EN 303 645,
    NISTIR 8259A and IEC 62443 to the language features that satisfy
    each, and `generate-evidence-report` emits the summary. Cobblestone is
    aimed at being the first platform where the compiler proves firmware
    meets Cyber Resilience Act requirements by construction.

**71 applications, 1,158 modules**, all written in Codex and compiled by
the seed; 33 carry a web front end through the HTML plug. Catalog:
[docs/CuratorsCatalogue.md](docs/CuratorsCatalogue.md).

**Test battery: 1,454 tests, 1,427 pass, 0 fail, 27 skip** (measured
2026-08-14 at seed `8D405FDF`). The BVT subset that `build/build.ps1`
gates on is 79 tests, compiled and then run where an `.expected` exists,
for 143 checks; its phase of the gate takes about 19s.

---

## Distribution artifacts

**`seed/Codex.cdx`** (3,374,965 bytes, 2026-09-12, PR 142 class/instance scan at line start, COMPILER-84) -- the canonical seed, and the root
of trust. Ed25519-signed and self-verifying.

| Algorithm | Digest |
|---|---|
| Content hash prefix | `266E24344DE9D861` |
| SHA-256 | `49070BAEB1E31085E2F2250BBA44AA88E042BED66FD436C1AF5825D02BBE6E03` |
| MD5 | `6883F5CAAFEDED6363FF879181F44C13` |

The content hash is the 32 bytes the CDX header carries at offsets 8..39
and it deliberately EXCLUDES the signature, so it is not a prefix of the
file's SHA-256 and the two rows above do not agree by construction.

**`seed/Codex.img`** (16,777,216 bytes) -- bootable GPT disk image, the
first-boot ceremony.

| Algorithm | Digest |
|---|---|
| SHA-256 | `02EB3B12A0D22853C1EC77DAF43E4273E67624E1DFDE914371077227862D9A12` |

Boot it on a UEFI machine and it runs its own first-boot ceremony on the
GOP framebuffer with no OS beneath it: choose an interface, walk the
identity wizard (Ed25519 keypair from hardware entropy, wrapped under a
passphrase you type), and watch the machine read its own seed back off
the stick and verify its own signature before it acts. Everything is
compiled by the seed embedded on the image (`CODEX.CDX`); the loader stub
hands off after `ExitBootServices` and the payload drives the display,
the keyboard and the disk itself.

Real-UEFI boot needs Secure Boot off, Fast Boot off, and CSM/Legacy off
(UEFI-only) -- the image is pure GPT.

**`build/boot/diag.img`** (16,777,216 bytes) -- the diagnostic stick: one
image that boots on a machine we have never seen, enumerates its firmware
and devices, runs every probe that applies, banks its readings to the stick
before any risky arm, and prints a report naming what worked and what did
not. It carries no seed and no identity, so it is safe to hand to a
stranger; the procedure is in
[docs/UsersHandbook.md](docs/UsersHandbook.md).

| Algorithm | Digest |
|---|---|
| SHA-256 | `884FD218348190808E8F0699A34C7320E78113E7634EE2565F53603009D0BF84` |

The payload is reproducible from its source and this seed: `DIAG.RCP` inside
the image names both and carries `payload-sha256`, `bundled-sha256` and
`efi-sha256`, and two builds from the same source and seed agree on all
three. The IMAGE hash does not reproduce across builds, because `DIAG.RCP`
also records the build path and time; compare the three payload digests,
not the image hash, when asking whether something moved.

Flash to USB from an elevated PowerShell. The flasher takes the disk
offline, holds every volume locked while it writes, and reads the image
back byte for byte before its last line; when it finishes, pull the stick
and boot. (Older notes said never to eject: that hazard was our own image
geometry, fixed 2026-07-29, and a stick now survives reinsertion unchanged.)

```powershell
# Find your USB disk number:  Get-Disk | Where-Object BusType -eq USB
build\flash-usb.ps1 -Image seed\Codex.img -DiskNumber <N>
```

---

## Design decisions

- **Single-artifact substrate.** Boot `Codex.cdx`. There is no layer
  beneath it that you didn't compile yourself.
- **Literate by design.** Chapters and Sections aren't comments. They're
  structure. The compiler parses prose alongside code.
- **Own character encoding.** CCE is frequency-sorted, so `is-letter` is
  one comparison rather than a table lookup. Unicode exists only at I/O
  boundaries: UTF-8 in decodes to CCE across all three tiers, CCE out
  emits proper UTF-8.
- **Algebraic effects.** Side effects are declared in types and handled
  explicitly. No surprise mutations.
- **Capability model.** Trust lattice, direction markers and scoped
  capabilities designed in from the substrate, not bolted on after.
- **Seven trust anchors, not a root program.** Each was taken from two
  independent distribution paths and compared byte for byte. A list of a
  hundred authorities, any one of which can issue for any name in the
  world, is the trust model this project exists to replace.

```codex
Chapter: Greeting
  cites Foreword chapter Console

Section: Functions

  greet : Text -> Text
  greet (name) = "Hello, " & name & "!"

  opening : [Console] Nothing = act
    print-line "What is your name?"
    name <- read-line
    print-line (greet name)
  end

Page 1
```

The `[Console]` in the type is the effect. It is part of the contract,
not a surprise that happens at runtime.

---

## Quick Start

**Prerequisites**: `codex-vm.exe` (build via `tools/build-vm.ps1`) or
QEMU with WHPX.

```powershell
build/build.ps1        # text round-trip + CDX fixed point + BVT. The gate.
build/test.ps1 -Tier all -Jobs 4 -ApprovedBy damian # full release battery
```

### Try it without building

The CDX in `seed/Codex.cdx` is a complete compiler. Boot it under
codex-vm, feed it source, and it hands back CDX or Codex text; the output
format is selected by the mode line. Container formats (ELF, PE, GPT/FAT
images) come from plug CDX binaries in `codex/plugs/`.

```powershell
New-Item -ItemType Directory -Force build-output/bare-metal
Copy-Item seed/Codex.cdx build-output/bare-metal/Codex.cdx

build/compile.ps1 -Src codex/test/arithmetic.codex -Out build-output/arith.cdx -Log build-output/arith.log -Kernel seed/Codex.cdx
build/test-run.ps1 -Kernel build-output/arith.cdx -OutFile build-output/arith.out
Get-Content build-output/arith.out
```

Source is pre-loaded into the guest ring buffer at boot and output is
captured from guest UART writes. No TCP sockets.

---

## The language, briefly

Full syntax and semantics: [docs/DevelopersGuide.md](docs/DevelopersGuide.md).

**`linear` is for resources, `mutable` is for data** -- two orthogonal
uniqueness disciplines, neither implying the other. A `linear` value must
be used exactly once on every path: dropping it is a leak (CDX2063),
using it twice is a double-use (CDX2061). A `mutable` record is data you
own and update in place; you may read its fields freely but may not alias
it (CDX2062), and in-place assignment is safe *precisely because* the
record is uniquely owned -- no GC, no copy, no hidden sharing. `freeze :
linear a -> a` is the one-way door between them, and is the identity at
runtime because the source is unique and is spent there.

```codex
  mutable GameState = record { turn : Integer, score : Integer }

  add-score : mutable GameState, Integer -> mutable GameState
  add-score (gs) (points) =
    gs.score = gs.score + points
    gs.turn = gs.turn + 1
    gs
```

**Bounded integers instead of width types.** There is no `Int8` or
`UInt32`; there is one `Integer` and a range constraint, from which width
and signedness are *derived* rather than spelled:

```codex
Byte       = Integer between 0 and 255 wrapping   -- modular arithmetic
Percentage = Integer between 0 and 100 clamping   -- saturates at bounds
SafeIndex  = Integer between 0 and 1024           -- default `error` (traps)
```

Record fields pack tight -- three `0..65535` fields take 6 bytes, not 24.
Out-of-range values are static errors, and where the compiler can prove a
value fits, the runtime check is elided outright.

**Effects, handlers and resilient act blocks.**

```codex
  effect Counter where
    tick : [Counter] Integer

  counted : Integer
  counted = with Counter (tick + tick + tick)
    tick (resume) = resume 1

  fetch-config : [Console, FileSystem] Text
  fetch-config = trying 3 times
    act
      content <- read-file "config.cdx"
      content
    end
  falling back to
    act
      "{}"
    end
```

**Also in the language:** sum types and records; exhaustive pattern
matching with multi-pattern arms, where a forgotten constructor is a
compile error; type classes via dictionary passing with return-type
polymorphism, fully resolved at compile time; `Vector N T` as a
first-class type with SSE2 packed codegen and dependent lane count;
`Real` with approximate equality (`==` on a Real is a compile error) and
declared safety modes; unit types erased at codegen; tuples; lazy
evaluation with memoization; and `for x in xs do f x`.

---

## Compilation Pipeline

```
                            Source (.codex)
                                  |
                 +----------------+----------------+
                 |          FRONTEND               |
                 |  Lexer ------- token stream     |
                 |  Parser ------ syntax tree      |
                 |  Desugarer --- abstract syntax  |
                 |  ChapterScoper namespace scope  |
                 |  NameResolver  resolved names   |
                 |  TypeChecker -- typed AST       |
                 |  Lowering ---- IR               |
                 +----------------+----------------+
                                  |
            +---------------------+---------------------+
            |                     |                     |
       Codex text            IR text               CDX path
       emitter               emitter               (Resolve +
            |                     |                LambdaLifting)
            v                     |                     |
       Codex source               |                CDX emitter
       (bootstrap                 |                (x86-64)
        round-trip)               |                     |
                                  |        +------+-----+------+
                                  |        v      v     v      v
                                  |      CDX    ELF   PE     IMG
                                  |
                 +----------+-----+-----+----------+
                 v          v     v     v          v
              ARM64      RISC-V  WASM  Transpilers GPU plugs
              ELF64      ELF     WAT   (31 langs)  (PTX, SPIR-V,
              (AArch64)  (RV32/  (brow-             WGSL)
                          RV64)   ser)
```

The frontend is shared across every target. From IR, three paths fan out:
**Codex text** re-emits the compiler's own source for bootstrap
verification; **IR text** serializes the typed IR as S-expressions, which
every plug consumes as a standalone CDX binary receiving IR over TCP; the
**CDX path** adds Resolve and LambdaLifting and emits x86-64 machine code
as a signed CDX binary, from which container plugs derive ELF, PE and GPT
disk images.

Each phase has its own deck allocation and `phase-compact` cycle;
cumulative deck is about 208 MB and peak working set about 210 MB for the
selfhost. The compiler pages its own arena in on first touch through a
not-present-PDE trick and a compact `#PF` handler, so physical cost is
measured by a touched-page counter rather than predicted by a formula.

---

## Codegen benchmarks

Function-body instruction counts from disassembly, against C and Zig
compilers. Source: `bench/` (`compare.ps1` for x86-64, `compare-iot.ps1`
for ARM64 and RISC-V). Measured 2026-08-17 on seed `D354208C631FDDA7`;
MSVC 2022 `cl.exe /O2`, zig 0.16.0 `-O ReleaseFast`, GCC 13.3.0 cross.

| Benchmark | Codex x86-64 | MSVC /O2 | Zig ReleaseFast | Codex ARM64 | GCC -O0 | Codex RV64 | GCC -O0 |
|---|---:|---:|---:|---:|---:|---:|---:|
| fib(35) | 22 | 20 | 23 | 19 | 20 | 20 | 34 |
| fact(20) | 13 | 15 | 58 | 12 | 17 | 14 | 27 |
| gcd(a,b) | 10 | 14 | 18 | 10 | 21 | 7 | 26 |
| sum(1M) | 7 | 23 | 15 | 8 | 20 | 9 | 27 |

Codex has no optimizer -- the code generator emits these sequences
directly. On x86-64, sum beats MSVC /O2 by 70 per cent (a tight TCO loop
against an unroll), gcd by 29 and fact by 13; fib is two instructions
behind. Against Zig ReleaseFast (LLVM), Codex is ahead on all four (Zig
unrolls fact's recursion eightfold). On ARM64 and RISC-V all four beat
GCC -O0. The full nine-benchmark tables, with C /Od, the C# and F# RyuJIT
columns, Zig Debug, Codex transpiled through the zig plug, and the GCC
-O2/-Os columns, are in `docs/ArchitectsSketchbook.md` "Codegen Quality vs
C and the JITs", with a note on what a JIT count does and does not contain.

---

## CCE -- Codex Character Encoding

Codex has its own character encoding, designed for computation rather
than compatibility.

**Tier 0** is 128 codepoints in one byte, frequency-sorted so that
classification is arithmetic rather than a table lookup: `is-letter (c)`
is `char-code c >= 13 && char-code c <= 64`, and `to-upper` is `+26`.

**Tier 1** is 2,048 codepoints in two bytes, framed exactly like UTF-8,
covering every character needed for all 27 EU member state languages --
required for IoT deployment under the Cyber Resilience Act, where literate
source must be readable by non-English regulatory reviewers. Latin
Extended, Cyrillic, Greek, Arabic, Hebrew, Devanagari, Thai, Lao, Hangul
jamo, top CJK, kana and math symbols. Conversion uses a 16-entry
block-offset table of about 48 bytes: `unicode = slice-base[offset >> 7]
+ (offset & 127)`.

**Tier 2** is three bytes and covers CJK unified ideographs, full Hangul
syllables, kana extensions and emoji, accepted in string literals and
prose; identifiers stay Tier 0 and Tier 1.

Unicode exists only at I/O boundaries. Internally everything is CCE, and
the compiler's own source uses only Tier 0, so the fixed-point property
is preserved regardless of Tier 1 and 2 support.

---

## Library Quires

Code outside the compiler is organized into **22 quires** (library
namespaces) holding **601 modules** (439 foreword, 162 OS). Quires cite
each other as `cites Game chapter AStar`; the quire name is the last
segment of the directory name, capitalized. Full catalog:
[docs/DevelopersRulebook.md](docs/DevelopersRulebook.md).

| Quire | Directory | Count |
|---|---|---:|
| Foreword | `codex/foreword/core/` | 133 |
| Encode | `codex/foreword/encode/` | 77 |
| UI | `codex/foreword/ui/` | 49 |
| AI | `codex/foreword/ai/` | 43 |
| Engine | `codex/foreword/engine/` | 43 |
| Game | `codex/foreword/game/` | 26 |
| Signal | `codex/foreword/signal/` | 14 |
| Math | `codex/foreword/math/` | 14 |
| GPU | `codex/foreword/gpu/` | 11 |
| Compress | `codex/foreword/compress/` | 8 |
| Punctual | `codex/foreword/punctual/` | 8 |
| Sim | `codex/foreword/sim/` | 7 |
| Shell | `codex/foreword/shell/` | 6 |
| Boards | `codex/boards/` | 9 |
| OS (excl. net, kernel) | `codex/os/*/` | 85 |
| Net | `codex/os/net/` | 41 |
| Kernel | `codex/os/kernel/` | 36 |

---

## Project Structure

```
codex/
  compiler/      Self-hosted compiler (65 files, 60,873 lines)
  foreword/      439 library modules across 13 quires
  boards/        Board HAL drivers -- 9 target boards
  os/            Kernel, net, trust, verify, sched, dev, observe (162 modules)
  plugs/         56 plugs, 195 source modules -- IR-text-driven emitters
  test/          Compiler samples + OS integration tests (1,823 files)
apps/            71 applications, 1,158 modules
annotations/     On-disk annotation sidecars (JSON facts)
build/           Build and test harness (PowerShell)
tools/           codex-vm, status server, USB writer, VS extensions
seed/            Bootstrap seed CDX + UEFI disk image
docs/            Design documents, plans, stories, reference specs
old/             Retired C# reference compiler -- historical only
```

---

## Lines of code

Measured 2026-09-10 from Perforce main at CL 25472 plus the release
additions and edits. Lines use .NET `ReadAllLines`.
Unopened files behind main were read from the depot. Untracked files and
build intermediates are excluded; tracked generated scripts and templates are included.

These are physical-line counts, not statement counts. For `.codex`, blank
means whitespace-only; **prose** means exactly one leading ASCII space
followed by a non-whitespace character. **Code** is every other non-blank
line, including chapter headings and cites. Other languages report all
non-blank lines, including comments and markup.

### Codex -- 4,149 files, 598,654 non-blank lines

| area | files | code | prose | blank |
|---|---:|---:|---:|---:|
| `apps/` | 1,158 | 210,502 | 13,774 | 38,878 |
| `codex/foreword/` | 439 | 61,643 | 7,231 | 14,584 |
| `codex/test/` | 1,823 | 68,000 | 10,381 | 16,575 |
| `codex/plugs/` | 262 | 63,515 | 5,726 | 9,988 |
| `codex/compiler/` | 65 | 45,246 | 6,145 | 9,482 |
| `codex/os/` | 162 | 24,698 | 2,416 | 5,950 |
| build tooling (`codex/build/`, `build/`) | 148 | 11,235 | 2,689 | 4,328 |
| `tracker`, `workflow`, one withheld quire | 33 | 4,862 | 570 | 1,155 |
| `codex/boards/` | 9 | 4,099 | 200 | 823 |
| `tools/`, `shaders/`, `docs/`, `bench/` | 60 | 35,524 | 20,198 | 7,441 |
| **total** | **4,149** | **529,324** | **69,330** | **109,204** |

Including blanks, these files contain **707,858 physical lines**.

### Other listed source languages -- 762 files, 162,035 non-blank lines

| language | files | non-blank | blank |
|---|---:|---:|---:|
| PowerShell | 451 | 66,855 | 6,056 |
| HTML | 137 | 51,077 | 1,757 |
| C | 11 | 16,308 | 1,019 |
| WGSL | 44 | 6,411 | 0 |
| JavaScript | 65 | 17,392 | 1,256 |
| C# (apps, plug templates) | 12 | 1,621 | 227 |
| CSS | 5 | 774 | 78 |
| Python | 11 | 1,342 | 267 |
| uiscript | 25 | 196 | 23 |
| TypeScript | 1 | 59 | 10 |
| **total** | **762** | **162,035** | **10,693** |

This table counts `.ps1`, `.html`, `.c`, `.h`, `.wgsl`, `.js`, `.mjs`,
`.cjs`, `.cs`, `.css`, `.py`, `.uiscript` and `.ts`. JavaScript includes
modules. Of the PowerShell files, 57 carry the generated-script header.
Including blanks, the table contains **172,728 physical lines**.

**The listed source files total 760,689 non-blank lines**, including Codex
prose, or **880,586 physical lines** across **4,911 files**. This is not a
hand-written-only total.

Markdown documentation and instructions contain a further **175,280
non-blank lines and 40,378 blank lines across 701 files**, including the
next update report. JSON sidecars under `annotations/` contain
**48,619 non-blank lines and 6 blank lines across 1,955 files**. Neither
category is included in the source totals above.

### `old/` -- the retired C# reference compiler

Historical measurement from **2026-08-21**, retained without re-reading
`old/` for this census: 264 files, **66,195 non-blank lines** (144,365
including blanks), kept in the
depot as historical record only and built by nothing. It is what BS1 and BS1.1
bootstrapped through before the cord was cut on 2026-04-24; the toolchain that
ordinarily builds Codex today does not run that retired compiler.

| | files | non-blank |
|---|---:|---:|
| C# | 200 | 64,389 |
| `.sln` | 1 | 693 |
| `.txt` | 8 | 442 |
| `.csproj` | 38 | 406 |
| Codex | 12 | 229 |
| JSON | 5 | 36 |

### Not counted, and why

- **Build intermediates:** every `build-output/` directory and
  `build/output*`, including concatenated compiler source and proof outputs.
- **107,802 non-blank lines of vendor specifications** extracted to text under
  `docs/Reference/` (USB 2.0, xHCI, two Intel datasheets, Brotli, HID). Not
  ours.
- **35,799 non-blank lines of archived bootstrap-1 C# stage dumps** under
  `docs/Designs/Done/Test/`, which are artifacts of a build rather than source.
- Source formats outside the listed extensions, project/configuration
  files, expected-output fixtures and non-annotation JSON.
- Binaries, images, audio, fonts and captured screenshots.
---

## Documentation

- [docs/VisionAndVirtues.md](docs/VisionAndVirtues.md) -- **Read this first**
- [docs/PM/CurrentPlan.md](docs/PM/CurrentPlan.md) -- Active plan and direction
- [docs/DevelopersGuide.md](docs/DevelopersGuide.md) -- Language syntax, types, how to write Codex
- [docs/UsersHandbook.md](docs/UsersHandbook.md) -- Boot the IMG, first steps, using the system
- [docs/OperatorsManual.md](docs/OperatorsManual.md) -- Build process, test harness, VM setup, debugging
- [docs/ArchitectsSketchbook.md](docs/ArchitectsSketchbook.md) -- Memory layout, registers, allocators, phase maps
- [docs/DevelopersRulebook.md](docs/DevelopersRulebook.md) -- Foreword quire catalog, library rules
- [docs/ExaminersAssay.md](docs/ExaminersAssay.md) -- Test infrastructure, coverage, known results
- [docs/TheShimmeringPortal.md](docs/TheShimmeringPortal.md) -- Web developer's guide to the UI-to-browser pipeline
- [docs/KingsAndCourts.md](docs/KingsAndCourts.md) -- Hard real-time, EU compliance, IoT regulatory story
- [docs/TinkersToolbox.md](docs/TinkersToolbox.md) -- Board support package, peripheral drivers
- [docs/CuratorsCatalogue.md](docs/CuratorsCatalogue.md) -- Application catalog

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both directions.
We don't put dates on mountains. The critical path is ordered. That's all
we need to know.

---

## Bootstrapping Freedom in 3 Easy Steps

*(j/k, this was hard but fun. It's also done, so you don't have to.)*

![Codex Bootstrap](docs/PM/CodexBootstrap.png)

---

## Kudos

To Anthropic and the Claude team -- Codex's bootstrap was built with Claude
Opus 4.6/4.7 (1M context) running as a small team of parallel agents under
Claude Code. The 1M-token window made it tractable to review thousand-line
codegen diffs against IR invariants in a single pass. The Agent SDK's
parallel-agent model let multiple agents work distinct CLs simultaneously
without cross-contaminating their reasoning. The harness's permission model
and sandboxing made it safe to give the agents direct access to git, p4,
WSL, codex-vm, and gdb without supervising every command. Persistent memory
across sessions meant context compounded instead of evaporating between
runs.

Forty-one days from project start to a self-sustaining bare-metal compiler
is not a thing one human plus one shell does. It's a thing one human plus a
team of disciplined agents does. Codex stands on the shoulders of the C#
self-host, which stands on the shoulders of Claude. Thank you.

---

## License

See repository for license details.
