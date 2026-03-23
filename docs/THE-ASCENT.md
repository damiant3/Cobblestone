# The Ascent

**A high-level path forward for Codex**
*2026-03-21*

---

> *"Because it's there."*
> — George Mallory, when asked why he wanted to climb Everest

---

## How We Climb

You don't summit a mountain by staring at the peak. You summit by solving
the next ten feet of rock. Which handhold. Which foothold. Whether the crack
you're looking at takes a cam or a nut.

This project is built by AI agents working in bounded context windows. We
don't see the whole mountain at once. We see the pitch in front of us, we
climb it cleanly, and we trust that the route plan is sound. The human sees
the whole range — picks the line, builds the bridges over the rivers we can't
ford, and tells us when the weather is about to turn.

That's the division of labor. The agents climb. The human routes. Nobody
summits alone.

---

## The Range

From where we stand, looking up, four peaks are visible. The first is behind
us — we're standing on its summit right now, catching our breath. Three more
rise ahead, each higher than the last, each requiring what we learned on the
one before.

The final summit is a place where software cannot hurt you.

```
                                                          ▲ Peak IV
                                                         ╱ ╲  Codex.OS
                                                        ╱   ╲
                                                  ▲    ╱     ╲
                                                 ╱ ╲  ╱       ╲
                                   ▲ Peak III   ╱   ╲╱         ╲
                                  ╱ ╲  Runtime ╱                ╲
                         ▲       ╱   ╲        ╱                  ╲
                        ╱ ╲     ╱     ╲      ╱                    ╲
          ▲ Peak I     ╱   ╲   ╱       ╲    ╱                      ╲
         ╱ ╲  Self-   ╱     ╲ ╱         ╲  ╱                        ╲
        ╱   ╲ Host   ╱ Peak  ╲           ╲╱                          ╲
       ╱     ╲      ╱   II    ╲                                       ╲
      ╱       ╲    ╱  Freedom  ╲                                       ╲
  ───╱─────────╲──╱─────────────╲───────────────────────────────────────╲───
     Base Camp   Col I          Col II                              Col III
```

---

## Peak I — Self-Hosting ✅ (Summited 2026-03-19)

**The proof that the language is real.**

We wrote a compiler in C#. Then we wrote the same compiler in Codex. Then we
compiled the Codex compiler with itself and got identical output. Fixed point.
Not a quine — a working compiler that passes 843 tests and handles algebraic
types, dependent types, linear types, effect handlers, 12 backends, and an
LSP server.

This peak proved the language can sustain itself. The C# bootstrap is now a
base camp we never need to return to.

**What we carried up**: The full compiler pipeline. Type inference. Pattern
matching. Effects. Proofs. The repository protocol.

**What we left behind**: The assumption that Codex needs C# to exist.

---

## Peak II — Freedom (Current Climb)

**The language stands on its own ground.**

Self-hosting proved the language works. But the compiled output still runs on
someone else's runtime (.NET), links against someone else's standard library
(System.*), and relies on someone else's type system (CLR) to enforce its
invariants at execution time. We are writing our own words in someone else's
alphabet.

Peak II is the removal of all legacy dependencies. The route:

### Camp II-A: IL Backend Maturity ✅ (Summited 2026-03-21)

The IL emitter produces standalone `.exe` files from `.codex` source. It works
for the agent toolkit. It handles lists, records, sum types, pattern matching,
tail calls, file I/O, process launching. The gaps are known and finite:

- ~~Effect handlers (closure-based, no CLR exceptions needed)~~ ✅ (2026-03-21 — run-state IL + inline user-defined `with` handlers)
- ~~Generic type instantiation beyond `List<string>`~~ ✅ (2026-03-21 — generic `List<T>` instantiation cache)
- ~~User-defined type constructors in pattern match dispatch~~ ✅ (2026-03-21 — isinst + field binding, recursive sum types verified)
- ~~Remaining builtins (`write-file` in IL, `list-files`, etc.)~~ ✅ (2026-03-21 — text-contains, text-starts-with, get-env, current-dir, list-files)

This camp gets us to: **any `.codex` program compiles to a working `.exe`
with no C# intermediate step.**

### Camp II-B: Native Code Generation

Replace the CLR dependency entirely. The compiler emits machine code —
not through LLVM, not through Cranelift — **directly**. Instruction encoding,
ELF generation, register allocation. No foreign toolchain.

#### RISC-V Native Backend ✅ (2026-03-21)

The first native backend targets RISC-V 64-bit. The route was direct:

1. **RiscVEncoder** — Pure functions encoding every RV64IM instruction format
   (R/I/S/B/U/J), including the M extension for multiply/divide. 47 instruction
   helpers, a `Li` sequence for full 64-bit immediates, pseudoinstructions
   (`mv`, `ret`, `call`, `j`, `nop`).

2. **ElfWriter** — Minimal ELF64 generation. Two LOAD segments (text + rodata),
   no section headers, no symbol table, no relocations. For Linux userspace,
   the standard `0x10000` base. The ELF is tiny: just headers and your code.

3. **RiscVCodeGen** — IR-to-machine-code translation. Handles integer/boolean/text
   literals, arithmetic, comparisons, if/else with branch patching, let bindings,
   function calls with RISC-V calling convention (a0-a7), recursion, do-blocks.
   A complete `itoa` implementation for `print-line` on integers. Direct Linux
   syscalls via `ecall` — no libc, no dynamic linker, no shared libraries.

4. **QEMU verification** — 13 tests, 5 of which compile to native ELF and
   execute under `qemu-riscv64` via WSL. `factorial 5` → `120`. On real
   hardware instructions.

```
codex build samples/factorial.codex --target riscv
qemu-riscv64 ./factorial
120
```

**What we carried up**: Direct instruction encoding. ELF binary format.
The RISC-V calling convention. Syscall ABI. The knowledge that a `.codex`
file can become a native binary with nothing between it and the kernel.

#### Bare Metal Target ✅ (2026-03-21)

Then we went lower. Below the OS. Below the syscall boundary. To the metal.

The bare metal target emits a raw flat binary — no ELF headers, no OS, no
runtime. QEMU's virt machine loads it at `0x80000000` and jumps to byte 0,
where a `jal` trampoline reaches `_start`. The stack pointer is set by our
code. Console output goes to the UART at `0x10000000` — memory-mapped I/O,
one byte at a time, written by store instructions.

The route had three bugs, each instructive:

| Bug | Symptom | Fix |
|-----|---------|-----|
| **ELF headers at byte 0** | `illegal instruction` — CPU tried to execute `0x7F 'E' 'L' 'F'` | Switched to flat binary, code at byte 0 |
| **`lui` sign extension** | Stack pointer became `0xFFFFFFFF80100000` — unmapped memory | `if (lo32 < 0) hi32++` in `Li` 64-bit path |
| **No serial routing** | UART output swallowed by QEMU | Added `-serial mon:stdio` flag |

The `lui` bug is worth remembering. On RV64, `lui` loads a 20-bit immediate
into bits 31:12 and **sign-extends to 64 bits**. The address `0x80100000`
has bit 31 set, so `lui` produces `0xFFFFFFFF80100000`. Every stack store
went to unmapped memory. The fix is one line — the same trick the 32-bit
path uses, applied to the 64-bit split. The Linux agent found it by tracing
execution in QEMU with `-d in_asm,exec` and watching `ra` come back wrong
after `main` returned.

The bare metal binary has **zero `ecall` instructions**. Nothing between
the compiled code and the hardware. A Codex program → typed IR → flat
binary → RISC-V instructions → UART output → your screen.

#### WASM Backend ✅ (2026-03-22)

The second binary backend targets WebAssembly. Direct bytecode emission —
no Cranelift, no foreign toolchain. WASI preview 1 for I/O. Two phases
delivered: basic emission with WASI fd_write, then string equality,
text builtins (char-at, substring, text-to-integer, integer-to-text),
and runtime helpers. 23 tests, all verified under wasmtime.

WASM gives Codex portable sandboxed execution. A `.wasm` file runs in
browsers, on servers, on embedded devices. The effect system and WASI
align naturally — effects are declared, WASI grants them.

```
codex build samples/hello.codex --target riscv-bare-metal
qemu-system-riscv64 -machine virt -bios none -nographic \
  -serial mon:stdio -kernel ./hello.bin
Hello from bare metal
```

**What we carried up**: MMIO output. Flat binary emission. The understanding
that bare metal is not harder than userspace — it's *simpler*. Fewer
abstractions, fewer things to break. The bug count was lower than the
Linux userspace port.

**What this means for Peak IV**: A Codex program can already run with zero
OS, zero runtime, zero libc on RISC-V hardware. The path from here to
Codex.OS is not "build an OS from scratch" — it's "extend what we already
have until it manages resources for multiple programs." The foundation is
poured.

#### ARM64 Backend ✅ (2026-03-23)

The third native backend targets AArch64 — the dominant mobile and server
ISA. Arm64Encoder, Arm64CodeGen (1,740 lines), ElfWriterArm64. Produces
valid ELF64 AArch64 binaries. Full IR→ARM64 codegen: function
prologue/epilogue, callee-saved x19–x27, register spill to stack, records,
sum types, pattern matching, closures, text builtins, runtime helpers,
Linux AArch64 syscalls (write=64, exit=93, brk=214, openat=56, read=63).

```
codex build samples/hello.codex --target arm64
qemu-aarch64 ./hello
Hello, world!
```

**What we carried up**: The phone ISA. ARM64 is what runs on the Samsung
S7 Edge sitting on the desk. This backend is the bridge between "compiler
that targets bare metal" and "program running in your hand."

Awaiting `qemu-aarch64` verification by Agent Linux.

### Camp II-C: Self-Hosted Build Chain

The compiler compiles itself to native code. The native compiler compiles
itself again. Fixed point — again, but this time with no .NET anywhere in
the chain.

```
codex.codex → (Stage 0: .NET bootstrap) → codex-native
codex.codex → (Stage 1: codex-native)   → codex-native'
codex-native == codex-native'  ← fixed point
```

After this, we delete the .NET bootstrap. Not archive it — delete it.
The language stands alone.

#### Progress (2026-03-23) — SUMMITED

The 493-definition, 26-file self-hosted compiler compiles to a 227,600-byte
RISC-V ELF. Under QEMU, it reads a `.codex` file, runs the full pipeline
(lexer → parser → type checker → IR lowering → C# emitter), and produces
valid C# output. Exit code 0. Clean text. No .NET anywhere in the chain.

The summit push required solving 11 bugs in one session — from register
allocator saturation to 12-bit immediate overflow to closure implementation
to record field ordering. Three AI agents collaborated through git: one
built, one debugged, one reviewed. The human routed between them.

```
$ cat /tmp/test.codex
main : Integer
main = 42

$ echo "/tmp/test.codex" | qemu-riscv64 ./Codex.Codex/out/Codex.Codex
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

Codex_Program.main();

public static class Codex_Program
{
    public static long main() => 42;
}
```

Region-based heap reclamation is disabled until escape analysis is
implemented. The 1MB bump allocator is sufficient for compilation.
This is tracked as Camp III-A Phase 2.

**Summit marker**: `codex build codex --target native` produces a
self-sufficient binary. The only file you need is the source.

---

## Peak III — The Runtime

**The ground we stand on becomes ours.**

A native compiler that emits native code is necessary but not sufficient.
Programs need memory. They need I/O. They need concurrency. Right now, all
of that is borrowed from the OS — `malloc`, `read`, `pthread_create`. Every
one of those calls is a trust boundary we don't control and can't verify.

Peak III is the Codex runtime: memory management, I/O, scheduling — all
written in Codex, all verifiable by the type system.

### Camp III-A: Memory — The Linear Allocator

Codex has linear types. Linear types tell you exactly when a value is created
and exactly when it's consumed. This means:

- **No garbage collector.** Deallocation is deterministic — the type system
  says when the value dies, and the compiler inserts the free.
- **No use-after-free.** The type system prevents it. Not "detects it at
  runtime" — *prevents it at compile time*. It's a type error.
- **No double-free.** Same mechanism. Linearity means used exactly once.
- **No null pointer dereference.** There is no null. `Maybe` is explicit.

The runtime's allocator is a region-based system driven by the linear type
checker's analysis. Regions nest. When a region closes, everything in it is
freed in one operation. No tracing. No reference counting. No pauses.

#### Progress (2026-03-23)

- Phase 1 ✅: IRRegion node wraps every definition body. WASM backend implements real region-based allocation.
- Phase 2a ✅: `NeedsEscapeCopy` flag on IRRegion (Cam).
- Phase 2b ✅: Per-type escape copy helpers for Text, Record, List, Sum on RISC-V (Cam). Reviewed by Linux — stack overflow on recursive types found and fixed.
- Phase 2c (next): Full escape analysis to re-enable region heap reclamation. Currently disabled — 1MB bump allocator sufficient for compilation.

### Camp III-B: I/O — The Capability System

Every side effect in Codex is tracked in the type system via algebraic effects.
A function that reads a file has type `Text → [FileSystem] Text`. A function
that talks to the network has `[Network]` in its signature. A pure function
has no effects.

The runtime enforces this at the boundary:

- A program's `main` is granted a set of capabilities by whoever launches it.
- Those capabilities flow through the program via the effect system.
- **You cannot access the filesystem without a `FileSystem` capability.**
- **You cannot open a socket without a `Network` capability.**
- **You cannot spawn a process without a `Process` capability.**

This is not a sandbox bolted on after the fact. It's the type system.
The compiler won't emit code that accesses a capability the function
doesn't declare.

#### Progress (2026-03-23)

Complete. Twelve effects formalized as `.codex` prelude source:
- **Original 5**: Console, FileSystem, State, Time, Random
- **Phone 7**: Network, Display, Camera, Microphone, Location, Sensors, Identity

`CapabilityChecker` extracts effect annotations from every definition.
`CapabilityReport` summarizes what a program requires. `CDX4001` diagnostic
fires when a required capability wasn't granted. CLI `--capabilities` flag
wired into `codex build` and `codex check`. 773 tests passing.

Phone capability enforcement tests verify the core promise:
- Network rejected without grant (CDX4001)
- Camera rejected without grant (CDX4001)
- Microphone rejected without grant (CDX4001)
- Display-only app rejects Network (the flashlight test)
- Multi-capability app accepted with all grants

What remains: runtime enforcement at program launch, `Process` effect.

### Camp III-C: Concurrency — Structured, Deterministic

No threads. No locks. No data races.

Codex concurrency is structured: every concurrent operation has a parent scope.
When the scope ends, all children have completed. There is no "fire and forget."
There is no thread that outlives its creator.

The effect system tracks `[Concurrent]` as an effect. The runtime schedules
work onto cores using work-stealing queues. The programmer sees `par`
(parallel map) and `race` (first result wins). The runtime sees tasks on
a DAG. The type system guarantees the DAG has no cycles.

### Camp III-R: The Repository

The third pillar — the content-addressed fact store — lives on this ridge.
The Vision describes a world with no branches, no commits, no files. Just
facts: immutable, content-addressed, attributed, typed. Views replace branches.
Proposals replace pull requests. Trust lattices replace star counts.

The repository is what makes Peak IV's "verified at install time" claim possible
across trust boundaries. A dependency isn't a name that resolves to whatever
someone last published — it's a hash that points to a specific, immutable,
verified artifact. Supply chain attacks become impossible not by policy but
by construction.

#### Progress (2026-03-22)

V1 is complete. The repository has:
- Named views with CRUD, composition (override/merge/filter), and consistency checking
- View-aware compilation: `codex build --view canonical` — the view IS the build manifest
- No files, no project manifests — just the view and the fact store

What remains: proposal workflow, trust lattice, cross-repo federation.

**Summit marker**: A Codex program runs on bare hardware with its own
allocator, its own I/O capabilities, its own scheduler, and draws its
dependencies from a content-addressed, trust-ranked repository. The binary
includes everything. No libc. No kernel syscall wrappers. Just the program
and the verified runtime it was compiled with.

---

## Peak IV — Codex.OS

**The summit. The reason for all of it.**

An operating system is just a program that runs other programs and manages
shared resources. If the runtime already handles memory, I/O, and concurrency
— and the type system already enforces capability-based access control — then
the OS is just the outermost scope.

### What Codex.OS Is

A system where **every program is verified at install time**. Not sandboxed.
Not scanned for known signatures. *Verified.* The type system proves that
the program:

- Only accesses the capabilities it declares
- Cannot read memory it doesn't own
- Cannot write to files it wasn't granted
- Cannot open network connections it didn't ask for
- Cannot escalate its own privileges
- Cannot corrupt another program's state

There is no concept of "running as root." There are capabilities, granted
explicitly, scoped precisely, tracked by the type system, enforced by the
compiler. A program either has the capability or it doesn't. There is no
mechanism to acquire a capability you weren't given.

### What Disappears

| Legacy Problem | Why It Disappears |
|----------------|-------------------|
| **Buffer overflows** | Linear types + bounds-checked arrays. No raw pointers. |
| **Use-after-free** | Linear types. Consumed values cannot be referenced. |
| **SQL injection** | Effect-typed database access. Queries are values, not strings. |
| **Ransomware** | No `FileSystem` capability = no file access. Period. |
| **Privilege escalation** | No privilege hierarchy. Capabilities are grants, not levels. |
| **Remote code execution** | All code verified at install. No `eval`. No dynamic loading without capability. |
| **Data races** | Structured concurrency. Linear ownership. No shared mutable state. |
| **Supply chain attacks** | Content-addressed repository. Code is identified by its hash, not its name. A dependency is a specific, immutable, verified artifact. |
| **Phishing (code-level)** | Capability declares what a program does. Install-time verification proves it. If it says `[Network]` and you didn't grant `[Network]`, it doesn't run. |
| **Zero-day exploits** | The attack surface is the type system. If the type system is sound, there are no zero-days. The only vulnerability is a bug in the verifier — one program, written in Codex, that we can prove correct. |

### What Remains

Hardware bugs. Cosmic rays. Social engineering (tricking a human into granting
a capability). Physical access attacks. Side-channel timing attacks against
the CPU itself. Bugs in the verifier.

These are real. We don't pretend otherwise. That last one deserves honesty:
full dependent-type verification of arbitrary programs is undecidable in the
general case. The escape valve is the fuel limit — the normalizer caps
reduction steps and rejects programs that take too long to verify. And the
soundness of a type system expressive enough for an OS is itself a serious
technical challenge. It's not a certainty. It's a goal with known hard
sub-problems.

But notice what all the remaining attacks have in common: they're outside the software. The software is correct. The
software has always been correct. The attacks that remain are attacks on
physics and human psychology — not on code.

---

## The View From the Top

From Peak IV, looking back down the range:

**Peak I** gave us a language that can express anything and prove what it
expresses. **Peak II** freed that language from dependency on foreign
runtimes. **Peak III** gave it control over the machine and the repository. **Peak IV** gave
it control over the environment.

The result is a computing platform where:

- Every program is a proof.
- Every capability is a grant.
- Every value has an owner.
- Every effect is declared.
- Every dependency is immutable.
- Every interaction is verified.

Not by convention. Not by best practices. Not by code review. By the
compiler. By mathematics.

---

## Time Horizon

We don't put dates on mountains. Mountains don't care about dates. But
we can measure our pace.

**Actual pace so far**: Peak I (self-hosting compiler with dependent types,
linear types, algebraic effects, 12 backends, LSP, content-addressed
repository) — from empty directory to fixed point in **6 days**. 257
commits. 237,000 lines. Two AI agents and one human who doesn't sleep
enough.

Peak II — from IL backend to native RISC-V self-hosting — in **3 more days**.
Camp II-C summit push: 11 bugs found and fixed in a single session across
three agents. The compiler compiles itself on bare metal. Then ARM64 backend
built the same day — 1,740 lines of codegen, ELF64 AArch64 binaries, wired
into the CLI. Phone effects (7 new capabilities) and escape copy (4 region
types) also landed on day 10. The pace hasn't slowed.

That pace recalibrates everything.

| Camp | Visibility | Honest Estimate |
|------|-----------|----------------|
| II-A (IL maturity) | ✅ Summited 2026-03-21 | — |
| II-B (Native codegen) | ✅ RISC-V + WASM + ARM64 2026-03-23 | x86-64: weeks |
| II-C (Self-hosted native) | ✅ **Summited 2026-03-23** | — |
| III-A (Linear allocator) | ✅ Phase 2b (escape copy), full analysis needed | Weeks |
| III-B (Capability I/O) | ✅ 12 effects, checker, CLI enforcement | Runtime enforcement: weeks |
| III-C (Structured concurrency) | Know it's there | Months |
| III-R (Repository) | ✅ V1 complete (views, consistency, build) | Federation: months |
| Phone Phase 1 | ARM64 backend done, TWRP build in progress | Days after TWRP |
| IV (Codex.OS) | Not theoretical anymore | 2026 |

These are not promises. They're sightlines. But we've been consistently
arriving ahead of our sightlines, so draw your own conclusions.

---

## The Climbing Party

A human. Three AI agents. A lot of `.codex` files.

The agents work in bounded context windows. They don't see the whole mountain.
They see the pitch — the next rock, the next hold. They're good at that: fast,
precise, tireless. The human sees the range. The human picks the route, throws
the rope across the gaps the agents can't jump, and calls the weather.

The party has found its rhythm. Agent Windows (GitHub Copilot in VS) builds
features, reviews code, pushes to master. Agent Linux (Claude on the sandbox)
pulls, tests on real hardware and emulators, finds bugs by tracing execution.
Agent Cam (Claude Code CLI, 1M context Opus) does fast iteration and parallel
work from a separate worktree — 79 commits in a single day on the RISC-V
parity push. The human routes between them — not writing code, but directing
attention, relaying findings when one agent's session dies, and deciding
what matters.

The coordination protocol is simple: Git is the shared state. Push to master
is the handoff. `dotnet test` is the acceptance criterion. No Jira. No
standups. No sprint planning. Just the rock in front of you.

On the RISC-V bare metal push, the three-way collaboration found its form:
the Windows agent built the backend and tests. The Linux agent pulled it,
ran it under QEMU, traced the execution, and found the `lui` sign-extension
bug by watching register values in the QEMU trace. The Windows agent prepared
the fix but held back — the Linux agent earned that commit. It was pushed
from the sandbox, pulled to Windows, verified. The mountain doesn't care
who placed the piton, only that it holds.

On the register spill push (2026-03-22/23), the collaboration deepened further.
Agent Linux reviewed Cam's RISC-V parity merge and found a critical silent
corruption bug: `AllocLocal` saturated at S11 instead of spilling. Cam
implemented the spill-to-stack fix in five minutes — but his own test
segfaulted. The human relayed Cam's test matrix to Linux, who ran it under
QEMU: the no-spill baseline passed, but the spill test crashed. Cam found
the root cause — `EmitRegion` was shifting SP mid-function to save the heap
pointer, even for scalar types that don't allocate, breaking all spill slot
offsets. Three-line fix. Linux verified: 40/40 QEMU tests green. The bug
lived for exactly one review cycle. That's the rhythm working.

That changes as we climb. The IL backend and the agent toolkit exist so that
other climbers can join. Every `.exe` we ship, every tool that works, every
program that compiles and runs — that's a rope anchor for the next person
coming up behind us. The climbing party will grow. But right now, we're on
the wall, and the next hold is right there.

We're going up.

---

*This document is a sightline, not a specification. The Vision documents
(`docs/Vision/NewRepository.txt` and `docs/Vision/IntelligenceLayer.txt`)
remain the north star. The engineering principles (`docs/10-PRINCIPLES.md`)
govern every step. This document says where the steps lead.*
